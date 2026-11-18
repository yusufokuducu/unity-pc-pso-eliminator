using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace YusufOkuducu.PcPsoEliminator.Tests
{
    public sealed class PsoEliminatorTests
    {
        [Test]
        public void RuntimeHitchDetector_FlagsSevereFrameSpikeAfterBaselineWarmup()
        {
            var go = new GameObject("TestOrchestrator");
            try
            {
                var orchestrator = go.AddComponent<AsyncPsoWarmupOrchestrator>();

                // Feed 30 steady 8.3ms frames (120 FPS)
                for (int i = 0; i < 30; i++)
                {
                    bool hitched = orchestrator.DetectRuntimePsoHitch(8.3f, i);
                    Assert.IsFalse(hitched);
                }

                // Feed a 45ms PSO compilation stutter frame
                bool spikeDetected = orchestrator.DetectRuntimePsoHitch(45.0f, 31);
                Assert.IsTrue(spikeDetected);
                Assert.AreEqual(1, orchestrator.DetectedHitches.Count);
                Assert.Greater(orchestrator.DetectedHitches[0].spikeRatio, 4.0f);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void RuntimeHitchDetector_AdaptsTo60FpsBaselineAndRejectsNaNOrEarlyFrames()
        {
            var go = new GameObject("TestOrchestrator60Fps");
            try
            {
                var orchestrator = go.AddComponent<AsyncPsoWarmupOrchestrator>();

                // Invalid inputs must never trigger hitches or corrupt rolling average
                Assert.IsFalse(orchestrator.DetectRuntimePsoHitch(float.NaN, 0));
                Assert.IsFalse(orchestrator.DetectRuntimePsoHitch(-10f, 1));
                Assert.IsFalse(orchestrator.DetectRuntimePsoHitch(float.PositiveInfinity, 2));

                // First frame at 16.67ms (60 FPS) initializes rolling average directly to 16.67ms
                Assert.IsFalse(orchestrator.DetectRuntimePsoHitch(16.67f, 3));
                Assert.AreEqual(16.67f, orchestrator.RollingFrameAverageMs, 0.01f);

                // Even a 25ms frame during the first 4 warmup frames does not falsely trigger
                Assert.IsFalse(orchestrator.DetectRuntimePsoHitch(25.0f, 4));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Manifest_DeduplicatesVariantsCanonicallyAndComputesDriverFingerprint()
        {
            var manifest = ScriptableObject.CreateInstance<PsoWarmupManifest>();
            try
            {
                 // Add two identical variants with keywords in reversed order and dirty whitespace/duplicates
                manifest.harvestedRecords.Add(new HarvestedVariantRecord
                {
                    shaderName = "  Universal Render Pipeline/Lit  ",
                    passType = PassType.ScriptableRenderPipeline,
                    keywords = new[] { " _NORMALMAP ", "", null, "_MAIN_LIGHT_SHADOWS", "_NORMALMAP" },
                    sourceAssetPath = "Assets/MatA.mat"
                });
                manifest.harvestedRecords.Add(new HarvestedVariantRecord
                {
                    shaderName = "Universal Render Pipeline/Lit",
                    passType = PassType.ScriptableRenderPipeline,
                    keywords = new[] { "_MAIN_LIGHT_SHADOWS", "_NORMALMAP" },
                    sourceAssetPath = "Assets/MatB.mat"
                });
                manifest.harvestedRecords.Add(new HarvestedVariantRecord
                {
                    shaderName = "Universal Render Pipeline/Lit",
                    passType = PassType.ShadowCaster,
                    keywords = new string[0],
                    sourceAssetPath = "Assets/MatA.mat"
                });

                int removed = manifest.DeduplicateAndSortRecords();
                Assert.AreEqual(1, removed);
                Assert.AreEqual(2, manifest.harvestedRecords.Count);
                // ShadowCaster (PassType=8) sorts before ScriptableRenderPipeline (PassType=13)
                Assert.AreEqual(PassType.ShadowCaster, manifest.harvestedRecords[0].passType);
                Assert.AreEqual(PassType.ScriptableRenderPipeline, manifest.harvestedRecords[1].passType);
                Assert.AreEqual(2, manifest.harvestedRecords[1].keywords.Length);
                Assert.AreEqual("_MAIN_LIGHT_SHADOWS", manifest.harvestedRecords[1].keywords[0]);
                Assert.AreEqual("_NORMALMAP", manifest.harvestedRecords[1].keywords[1]);

                string fp1 = manifest.ComputeDriverAndManifestFingerprint("NVIDIA RTX 4080", "560.94", "Direct3D12");
                string fp2 = manifest.ComputeDriverAndManifestFingerprint("NVIDIA RTX 4080", "560.94", "Direct3D12");
                string fpDriverChanged = manifest.ComputeDriverAndManifestFingerprint("NVIDIA RTX 4080", "561.09", "Direct3D12");

                Assert.AreEqual(fp1, fp2);
                Assert.AreNotEqual(fp1, fpDriverChanged);

                manifest.msaaSampleCounts.Add(4);
                string fpMsaaChanged = manifest.ComputeDriverAndManifestFingerprint("NVIDIA RTX 4080", "560.94", "Direct3D12");
                Assert.AreNotEqual(fp1, fpMsaaChanged);

                string md = manifest.ExportManifestReportToMarkdown();
                StringAssert.Contains("Universal Render Pipeline/Lit", md);
                StringAssert.Contains("_MAIN_LIGHT_SHADOWS _NORMALMAP", md);
            }
            finally
            {
                Object.DestroyImmediate(manifest);
            }
        }

        [Test]
        public void AsyncWarmupOrchestrator_ExecutesTwoStageWarmupAcrossFormatsAndMsaaTargets()
        {
            var go = new GameObject("TestWarmupOrchestrator");
            var manifest = ScriptableObject.CreateInstance<PsoWarmupManifest>();
            try
            {
                manifest.renderTargetProfiles.Clear();
                manifest.renderTargetProfiles.Add(PsoRenderTargetProfile.SdrR8G8B8A8_UNorm);
                manifest.renderTargetProfiles.Add(PsoRenderTargetProfile.HdrR16G16B16A16_SFloat);

                manifest.msaaSampleCounts.Clear();
                manifest.msaaSampleCounts.Add(1);
                manifest.msaaSampleCounts.Add(4);

                var svc = new ShaderVariantCollection { variantCount = 4, warmedUpVariantCount = 0 };
                manifest.variantCollection = svc;

                var mat = new Material { name = "TestLit", shader = new Shader { name = "Universal Render Pipeline/Lit" }, passCount = 2 };
                manifest.hardwareDrawWarmupMaterials.Add(mat);

                var orchestrator = go.AddComponent<AsyncPsoWarmupOrchestrator>();
                orchestrator.BeginWarmup(manifest);

                Assert.IsTrue(orchestrator.IsWarmingUp);
                Assert.IsFalse(orchestrator.IsCompleted);
                // 2 RenderTarget profiles * 2 MSAA sample counts (1x, 4x) = 4 offscreen RenderTargets
                Assert.AreEqual(4, orchestrator.ActiveDummyRenderTargetCount);

                // Step 1: Warms up ShaderVariantCollection
                orchestrator.ExecuteFrameBudgetedWarmupStep();
                Assert.IsTrue(svc.isWarmedUp);
                Assert.IsFalse(orchestrator.IsCompleted);

                // Step 2: Binds offscreen RenderTargets and executes CommandBuffer draws for materials
                orchestrator.ExecuteFrameBudgetedWarmupStep();
                Assert.IsTrue(orchestrator.IsCompleted);
                Assert.IsFalse(orchestrator.IsWarmingUp);
                Assert.AreEqual(1f, orchestrator.ProgressNormalized, 0.001f);
                // Offscreen resources released on completion
                Assert.AreEqual(0, orchestrator.ActiveDummyRenderTargetCount);
            }
            finally
            {
                Object.DestroyImmediate(manifest);
                Object.DestroyImmediate(go);
            }
        }
    }
}
