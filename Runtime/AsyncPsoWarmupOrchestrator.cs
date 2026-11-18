using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace YusufOkuducu.PcPsoEliminator
{
    [Serializable]
    public struct PsoHitchEvent
    {
        public int frameIndex;
        public float frameTimeMs;
        public float rollingAverageMs;
        public float spikeRatio;
        public string graphicsDeviceType;
    }

    /// <summary>
    /// Frame-budgeted DX12/Vulkan PSO prewarmer and runtime compilation hitch detector.
    /// Combines progressive ShaderVariantCollection warmup with actual offscreen CommandBuffer
    /// draw-state binding so modern desktop GPU drivers compile full hardware PSOs before gameplay.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Performance/PC Async PSO Warmup Orchestrator")]
    public sealed class AsyncPsoWarmupOrchestrator : MonoBehaviour
    {
        [Header("Manifest & Budget")]
        [SerializeField] private PsoWarmupManifest manifest;
        [SerializeField] [Range(0.5f, 16.0f)] private float frameBudgetMilliseconds = 3.5f;
        [SerializeField] private int variantsPerStep = 8;
        [SerializeField] private bool autoStartOnAwake = true;

        [Header("Runtime Hitch Detection")]
        [SerializeField] private bool monitorRuntimeHitches = true;
        [SerializeField] [Range(1.8f, 5.0f)] private float hitchSpikeThresholdMultiplier = 2.5f;

        private readonly List<PsoHitchEvent> _detectedHitches = new List<PsoHitchEvent>();
        private RenderTexture _dummyColorRt;
        private Mesh _dummyQuadMesh;
        private CommandBuffer _warmupCmd;
        private int _currentMaterialIndex;
        private bool _isWarmingUp;
        private bool _isCompleted;
        private float _rollingFrameMs = 8.33f;

        public bool IsWarmingUp => _isWarmingUp;
        public bool IsCompleted => _isCompleted;
        public IReadOnlyList<PsoHitchEvent> DetectedHitches => _detectedHitches;

        public float ProgressNormalized
        {
            get
            {
                if (_isCompleted) return 1f;
                if (manifest == null) return 0f;

                int totalSvc = manifest.variantCollection != null ? manifest.variantCollection.variantCount : 0;
                int warmedSvc = manifest.variantCollection != null ? manifest.variantCollection.warmedUpVariantCount : 0;
                int totalMats = manifest.hardwareDrawWarmupMaterials != null ? manifest.hardwareDrawWarmupMaterials.Count : 0;
                int totalSteps = totalSvc + totalMats;

                if (totalSteps <= 0) return 1f;
                return Mathf.Clamp01((float)(warmedSvc + _currentMaterialIndex) / totalSteps);
            }
        }

        public event Action<float> OnWarmupProgress;
        public event Action OnWarmupCompleted;
        public event Action<PsoHitchEvent> OnHitchDetected;

        private void Awake()
        {
            if (autoStartOnAwake && manifest != null)
            {
                BeginWarmup(manifest);
            }
        }

        public void BeginWarmup(PsoWarmupManifest targetManifest)
        {
            manifest = targetManifest;
            if (manifest == null)
            {
                _isCompleted = true;
                return;
            }

            EnsureOffscreenResources();
            _currentMaterialIndex = 0;
            _isCompleted = false;
            _isWarmingUp = true;
        }

        private void Update()
        {
            if (_isWarmingUp)
            {
                ExecuteFrameBudgetedWarmupStep();
            }
            else if (monitorRuntimeHitches)
            {
                DetectRuntimePsoHitch(Time.unscaledDeltaTime * 1000f, Time.frameCount);
            }
        }

        public void ExecuteFrameBudgetedWarmupStep()
        {
            if (manifest == null)
            {
                CompleteWarmup();
                return;
            }

            long budgetTicks = (long)(frameBudgetMilliseconds * (Stopwatch.Frequency / 1000.0));
            long startTicks = Stopwatch.GetTimestamp();

            // Stage 1: Progressive ShaderVariantCollection warmup
            ShaderVariantCollection svc = manifest.variantCollection;
            if (svc != null && !svc.isWarmedUp)
            {
                while (!svc.isWarmedUp && (Stopwatch.GetTimestamp() - startTicks) < budgetTicks)
                {
                    if ( svc.WarmUpProgressively(Mathf.Max(1, variantsPerStep)))
                        break;
                }

                OnWarmupProgress?.Invoke(ProgressNormalized);
                return;
            }

            // Stage 2: Hardware PSO Draw-State Binding via offscreen 1x1 CommandBuffer
            List<Material> mats = manifest.hardwareDrawWarmupMaterials;
            if (mats != null && _currentMaterialIndex < mats.Count)
            {
                EnsureOffscreenResources();
                _warmupCmd.Clear();
                _warmupCmd.SetRenderTarget(_dummyColorRt);
                _warmupCmd.ClearRenderTarget(true, true, Color.black);

                while (_currentMaterialIndex < mats.Count && (Stopwatch.GetTimestamp() - startTicks) < budgetTicks)
                {
                    Material mat = mats[_currentMaterialIndex++];
                    if (mat != null && mat.shader != null && mat.shader.isSupported)
                    {
                        int passCount = Mathf.Min(mat.passCount, 4);
                        for (int p = 0; p < passCount; p++)
                        {
                            _warmupCmd.DrawMesh(_dummyQuadMesh, Matrix4x4.identity, mat, 0, p);
                        }
                    }
                }

                Graphics.ExecuteCommandBuffer(_warmupCmd);
                OnWarmupProgress?.Invoke(ProgressNormalized);

                if (_currentMaterialIndex >= mats.Count)
                {
                    CompleteWarmup();
                }
                return;
            }

            CompleteWarmup();
        }

        public bool DetectRuntimePsoHitch(float currentFrameMs, int frameIndex)
        {
            if (currentFrameMs <= 0.01f) return false;

            bool isHitch = currentFrameMs > (_rollingFrameMs * hitchSpikeThresholdMultiplier) && currentFrameMs > 22.0f;
            if (isHitch)
            {
                var evt = new PsoHitchEvent
                {
                    frameIndex = frameIndex,
                    frameTimeMs = currentFrameMs,
                    rollingAverageMs = _rollingFrameMs,
                    spikeRatio = currentFrameMs / Mathf.Max(0.1f, _rollingFrameMs),
                    graphicsDeviceType = SystemInfo.graphicsDeviceType.ToString()
                };
                _detectedHitches.Add(evt);
                OnHitchDetected?.Invoke(evt);
            }

            // Exponential moving average with slow adaptation to spikes
            _rollingFrameMs = Mathf.Lerp(_rollingFrameMs, Mathf.Min(currentFrameMs, _rollingFrameMs * 1.5f), 0.08f);
            return isHitch;
        }

        private void CompleteWarmup()
        {
            _isWarmingUp = false;
            _isCompleted = true;
            ReleaseOffscreenResources();
            OnWarmupProgress?.Invoke(1f);
            OnWarmupCompleted?.Invoke();
        }

        private void EnsureOffscreenResources()
        {
            if (_dummyColorRt == null)
            {
                _dummyColorRt = new RenderTexture(4, 4, 24, GraphicsFormat.R8G8B8A8_UNorm)
                {
                    name = "PSO_Warmup_DummyRT",
                    hideFlags = HideFlags.HideAndDontSave
                };
                _dummyColorRt.Create();
            }

            if (_dummyQuadMesh == null)
            {
                _dummyQuadMesh = new Mesh
                {
                    name = "PSO_Warmup_DummyTriangle",
                    hideFlags = HideFlags.HideAndDontSave,
                    vertices = new[] { Vector3.zero, Vector3.up * 0.01f, Vector3.right * 0.01f },
                    normals = new[] { Vector3.back, Vector3.back, Vector3.back },
                    tangents = new[] { new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1) },
                    uv = new[] { Vector2.zero, Vector2.up, Vector2.right },
                    triangles = new[] { 0, 1, 2 }
                };
            }

            if (_warmupCmd == null)
            {
                _warmupCmd = new CommandBuffer { name = "AsyncPsoWarmupPass" };
            }
        }

        private void ReleaseOffscreenResources()
        {
            if (_warmupCmd != null)
            {
                _warmupCmd.Dispose();
                _warmupCmd = null;
            }
            if (_dummyColorRt != null)
            {
                _dummyColorRt.Release();
                DestroyImmediate(_dummyColorRt);
                _dummyColorRt = null;
            }
            if (_dummyQuadMesh != null)
            {
                DestroyImmediate(_dummyQuadMesh);
                _dummyQuadMesh = null;
            }
        }

        private void OnDestroy()
        {
            ReleaseOffscreenResources();
        }
    }
}
