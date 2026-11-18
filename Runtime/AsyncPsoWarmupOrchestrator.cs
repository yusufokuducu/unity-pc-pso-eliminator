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
    /// Combines progressive ShaderVariantCollection warmup with multi-format offscreen CommandBuffer
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
        [SerializeField] private float minimumHitchDurationMs = 22.0f;

        private readonly List<PsoHitchEvent> _detectedHitches = new List<PsoHitchEvent>();
        private readonly List<RenderTexture> _dummyRenderTargets = new List<RenderTexture>();
        private Mesh _dummyQuadMesh;
        private Mesh _dummySkinnedMesh;
        private CommandBuffer _warmupCmd;
        private int _currentMaterialIndex;
        private bool _isWarmingUp;
        private bool _isCompleted;
        private float _rollingFrameMs;
        private int _baselineFramesObserved;

        public bool IsWarmingUp => _isWarmingUp;
        public bool IsCompleted => _isCompleted;
        public IReadOnlyList<PsoHitchEvent> DetectedHitches => _detectedHitches;
        public float RollingFrameAverageMs => _rollingFrameMs;
        public int ActiveDummyRenderTargetCount => _dummyRenderTargets.Count;

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
                _isWarmingUp = false;
                return;
            }

            EnsureOffscreenResources();
            _currentMaterialIndex = 0;
            _isCompleted = false;
            _isWarmingUp = true;
        }

        public void ResetHitchDetector()
        {
            _detectedHitches.Clear();
            _rollingFrameMs = 0f;
            _baselineFramesObserved = 0;
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

            long budgetTicks = (long)(Mathf.Max(0.25f, frameBudgetMilliseconds) * (Stopwatch.Frequency / 1000.0));
            long startTicks = Stopwatch.GetTimestamp();

            // Stage 1: Progressive ShaderVariantCollection warmup
            ShaderVariantCollection svc = manifest.variantCollection;
            if (svc != null && !svc.isWarmedUp)
            {
                while (!svc.isWarmedUp && (Stopwatch.GetTimestamp() - startTicks) < budgetTicks)
                {
                    if (svc.WarmUpProgressively(Mathf.Max(1, variantsPerStep)))
                        break;
                }

                OnWarmupProgress?.Invoke(ProgressNormalized);
                return;
            }

            // Stage 2: Hardware PSO Draw-State Binding via offscreen RenderTargets (SDR & HDR formats)
            List<Material> mats = manifest.hardwareDrawWarmupMaterials;
            if (mats != null && _currentMaterialIndex < mats.Count)
            {
                EnsureOffscreenResources();
                _warmupCmd.Clear();

                for (int rtIdx = 0; rtIdx < _dummyRenderTargets.Count; rtIdx++)
                {
                    RenderTexture targetRt = _dummyRenderTargets[rtIdx];
                    if (targetRt == null) continue;

                    _warmupCmd.SetRenderTarget(targetRt);
                    _warmupCmd.ClearRenderTarget(true, true, Color.black);
                }

                while (_currentMaterialIndex < mats.Count && (Stopwatch.GetTimestamp() - startTicks) < budgetTicks)
                {
                    Material mat = mats[_currentMaterialIndex++];
                    if (mat != null && mat.shader != null && mat.shader.isSupported)
                    {
                        int passCount = Mathf.Min(mat.passCount, 4);
                        for (int rtIdx = 0; rtIdx < _dummyRenderTargets.Count; rtIdx++)
                        {
                            _warmupCmd.SetRenderTarget(_dummyRenderTargets[rtIdx]);
                            for (int p = 0; p < passCount; p++)
                            {
                                _warmupCmd.DrawMesh(_dummyQuadMesh, Matrix4x4.identity, mat, 0, p);
                                if (_dummySkinnedMesh != null)
                                {
                                    _warmupCmd.DrawMesh(_dummySkinnedMesh, Matrix4x4.identity, mat, 0, p);
                                }
                            }
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
            if (float.IsNaN(currentFrameMs) || float.IsInfinity(currentFrameMs) || currentFrameMs <= 0.01f)
                return false;

            if (_baselineFramesObserved == 0)
            {
                _rollingFrameMs = currentFrameMs;
                _baselineFramesObserved = 1;
                return false;
            }

            bool isHitch = _baselineFramesObserved >= 5 &&
                           currentFrameMs > (_rollingFrameMs * hitchSpikeThresholdMultiplier) &&
                           currentFrameMs > minimumHitchDurationMs;

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

            _baselineFramesObserved++;
            // Exponential moving average with clamped adaptation to spikes so a single hitch doesn't skew baseline
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
            if (_dummyRenderTargets.Count == 0)
            {
                List<PsoRenderTargetProfile> profiles = (manifest != null && manifest.renderTargetProfiles != null && manifest.renderTargetProfiles.Count > 0)
                    ? manifest.renderTargetProfiles
                    : new List<PsoRenderTargetProfile> { PsoRenderTargetProfile.SdrR8G8B8A8_UNorm };

                List<int> msaaCounts = (manifest != null && manifest.msaaSampleCounts != null && manifest.msaaSampleCounts.Count > 0)
                    ? manifest.msaaSampleCounts
                    : new List<int> { 1 };

                var createdCombos = new HashSet<int>();

                for (int i = 0; i < profiles.Count; i++)
                {
                    GraphicsFormat fmt = MapProfileToGraphicsFormat(profiles[i]);
                    for (int m = 0; m < msaaCounts.Count; m++)
                    {
                        int validMsaa = SanitizeMsaaSampleCount(msaaCounts[m]);
                        int comboKey = ((int)profiles[i] << 8) | validMsaa;
                        if (!createdCombos.Add(comboKey))
                            continue;

                        var rt = new RenderTexture(4, 4, 24, fmt)
                        {
                            name = $"PSO_Warmup_DummyRT_{profiles[i]}_MSAA{validMsaa}x",
                            antiAliasing = validMsaa,
                            hideFlags = HideFlags.HideAndDontSave
                        };
                        rt.Create();
                        _dummyRenderTargets.Add(rt);
                    }
                }
            }

            if (_dummyQuadMesh == null)
            {
                _dummyQuadMesh = new Mesh
                {
                    name = "PSO_Warmup_DummyStaticTriangle",
                    hideFlags = HideFlags.HideAndDontSave,
                    vertices = new[] { Vector3.zero, Vector3.up * 0.01f, Vector3.right * 0.01f },
                    normals = new[] { Vector3.back, Vector3.back, Vector3.back },
                    tangents = new[] { new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1) },
                    uv = new[] { Vector2.zero, Vector2.up, Vector2.right },
                    uv2 = new[] { Vector2.zero, Vector2.up, Vector2.right },
                    colors = new[] { Color.black, Color.black, Color.black },
                    triangles = new[] { 0, 1, 2 }
                };
            }

            if (_dummySkinnedMesh == null)
            {
                var bw = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
                _dummySkinnedMesh = new Mesh
                {
                    name = "PSO_Warmup_DummySkinnedTriangle",
                    hideFlags = HideFlags.HideAndDontSave,
                    vertices = new[] { Vector3.zero, Vector3.up * 0.01f, Vector3.right * 0.01f },
                    normals = new[] { Vector3.back, Vector3.back, Vector3.back },
                    tangents = new[] { new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1) },
                    uv = new[] { Vector2.zero, Vector2.up, Vector2.right },
                    uv2 = new[] { Vector2.zero, Vector2.up, Vector2.right },
                    colors = new[] { Color.black, Color.black, Color.black },
                    boneWeights = new[] { bw, bw, bw },
                    bindposes = new[] { Matrix4x4.identity },
                    triangles = new[] { 0, 1, 2 }
                };
            }

            if (_warmupCmd == null)
            {
                _warmupCmd = new CommandBuffer { name = "AsyncPsoWarmupPass" };
            }
        }

        public static int SanitizeMsaaSampleCount(int samples)
        {
            if (samples >= 8) return 8;
            if (samples >= 4) return 4;
            if (samples >= 2) return 2;
            return 1;
        }

        private static GraphicsFormat MapProfileToGraphicsFormat(PsoRenderTargetProfile profile)
        {
            switch (profile)
            {
                case PsoRenderTargetProfile.HdrR16G16B16A16_SFloat:
                    return GraphicsFormat.R16G16B16A16_SFloat;
                case PsoRenderTargetProfile.HdrB10G11R11_UFloatPack32:
                    return GraphicsFormat.B10G11R11_UFloatPack32;
                default:
                    return GraphicsFormat.R8G8B8A8_UNorm;
            }
        }

        private void ReleaseOffscreenResources()
        {
            if (_warmupCmd != null)
            {
                _warmupCmd.Dispose();
                _warmupCmd = null;
            }
            for (int i = 0; i < _dummyRenderTargets.Count; i++)
            {
                RenderTexture rt = _dummyRenderTargets[i];
                if (rt != null)
                {
                    rt.Release();
                    DestroyImmediate(rt);
                }
            }
            _dummyRenderTargets.Clear();

            if (_dummyQuadMesh != null)
            {
                DestroyImmediate(_dummyQuadMesh);
                _dummyQuadMesh = null;
            }

            if (_dummySkinnedMesh != null)
            {
                DestroyImmediate(_dummySkinnedMesh);
                _dummySkinnedMesh = null;
            }
        }

        private void OnDestroy()
        {
            ReleaseOffscreenResources();
        }
    }
}
