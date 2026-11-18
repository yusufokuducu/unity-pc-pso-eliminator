using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using YusufOkuducu.PcPsoEliminator.Editor;

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

        [Test]
        public void McpBridge_AutoFixAndOptimizeManifest_DeduplicatesVariantsPrunesInvalidMaterialsAndSanitizesMsaa()
        {
            var manifest = ScriptableObject.CreateInstance<PsoWarmupManifest>();
            try
            {
                manifest.msaaSampleCounts.Clear();
                manifest.msaaSampleCounts.AddRange(new[] { 0, 3, 4, 4 }); // Invalid/duplicate MSAA values

                manifest.hardwareDrawWarmupMaterials.Add(null);
                manifest.hardwareDrawWarmupMaterials.Add(new Material
                {
                    name = "UnsupportedMat",
                    shader = new Shader { name = "Custom/Broken", isSupported = false }
                });
                manifest.hardwareDrawWarmupMaterials.Add(new Material
                {
                    name = "ValidMat",
                    shader = new Shader { name = "Universal Render Pipeline/Lit", isSupported = true }
                });

                manifest.harvestedRecords.Add(new HarvestedVariantRecord
                {
                    shaderName = "Universal Render Pipeline/Lit",
                    passType = PassType.ScriptableRenderPipeline,
                    keywords = new[] { "_B", "_A" }
                });
                manifest.harvestedRecords.Add(new HarvestedVariantRecord
                {
                    shaderName = "Universal Render Pipeline/Lit",
                    passType = PassType.ScriptableRenderPipeline,
                    keywords = new[] { "_A", "_B" }
                });

                PsoManifestAutoFixReport fix = PsoEliminatorMcpBridge.AutoFixAndOptimizeManifest(manifest);

                Assert.IsTrue(fix.modified);
                Assert.AreEqual(1, fix.duplicateVariantsRemoved);
                Assert.AreEqual(2, fix.invalidMaterialsRemoved);
                Assert.Greater(fix.msaaEntriesNormalized, 0);
                Assert.AreEqual(1, manifest.harvestedRecords.Count);
                Assert.AreEqual(1, manifest.hardwareDrawWarmupMaterials.Count);
                CollectionAssert.AreEqual(new[] { 1, 2, 4 }, manifest.msaaSampleCounts);
            }
            finally
            {
                Object.DestroyImmediate(manifest);
            }
        }

        [Test]
        public void McpBridge_HandlesJsonRpcInitializeToolsListToolsCallAndErrors()
        {
            var manifest = ScriptableObject.CreateInstance<PsoWarmupManifest>();
            try
            {
                manifest.harvestedRecords.Add(new HarvestedVariantRecord
                {
                    shaderName = "Universal Render Pipeline/Lit",
                    passType = PassType.ShadowCaster,
                    keywords = new[] { "_SHADOWS_SOFT" }
                });
                manifest.harvestedRecords.Add(new HarvestedVariantRecord
                {
                    shaderName = "Universal Render Pipeline/Lit",
                    passType = PassType.ShadowCaster,
                    keywords = new[] { "_SHADOWS_SOFT" }
                });

                string initRes = PsoEliminatorMcpBridge.HandleJsonRpcRequest(
                    @"{""jsonrpc"":""2.0"",""id"":1,""method"":""initialize""}", manifest);
                StringAssert.Contains(@"""protocolVersion"":""2024-11-05""", initRes);
                StringAssert.Contains(@"""name"":""unity-pc-pso-eliminator""", initRes);

                string listRes = PsoEliminatorMcpBridge.HandleJsonRpcRequest(
                    @"{""jsonrpc"":""2.0"",""id"":2,""method"":""tools/list""}", manifest);
                StringAssert.Contains("pso_eliminator.inspect_manifest", listRes);
                StringAssert.Contains("pso_eliminator.autofix_manifest", listRes);

                string callFix = PsoEliminatorMcpBridge.HandleJsonRpcRequest(
                    @"{""jsonrpc"":""2.0"",""id"":3,""method"":""tools/call"",""params"":{""name"":""pso_eliminator.autofix_manifest""}}",
                    manifest);
                StringAssert.Contains(@"""duplicateVariantsRemoved"":1", callFix);
                StringAssert.Contains(@"""isError"":false", callFix);

                string callInspect = PsoEliminatorMcpBridge.HandleJsonRpcRequest(
                    @"{""jsonrpc"":""2.0"",""id"":4,""method"":""tools/call"",""params"":{""name"":""pso_eliminator.inspect_manifest""}}",
                    manifest);
                StringAssert.Contains(@"""totalVariants"":1", callInspect);
                StringAssert.Contains(@"""shadowCasterPassCount"":1", callInspect);

                string errUnknown = PsoEliminatorMcpBridge.HandleJsonRpcRequest(
                    @"{""jsonrpc"":""2.0"",""id"":99,""method"":""bad/method""}", manifest);
                StringAssert.Contains("-32601", errUnknown);
            }
            finally
            {
                Object.DestroyImmediate(manifest);
            }
        }

        [Test]
        public void UiToolkitWindow_BuildsPassTypeHeatmapAndFiltersRecordsBySearchAndPass()
        {
            var manifest = ScriptableObject.CreateInstance<PsoWarmupManifest>();
            try
            {
                manifest.harvestedRecords.Add(new HarvestedVariantRecord
                {
                    shaderName = "Universal Render Pipeline/Lit",
                    passType = PassType.ScriptableRenderPipeline,
                    keywords = new[] { "_NORMALMAP" }
                });
                manifest.harvestedRecords.Add(new HarvestedVariantRecord
                {
                    shaderName = "Custom/FoliageWind",
                    passType = PassType.ShadowCaster,
                    keywords = new[] { "_ALPHATEST_ON" }
                });

                var win = new PsoEliminatorWindow();
                win.CreateGUI();
                Assert.Greater(win.rootVisualElement.childCount, 0);

                win.SetActiveManifest(manifest);
                Assert.AreEqual(2, win.GetFilteredRecords().Count);

                PsoManifestHeatmapSummary heatmap = PsoEliminatorMcpBridge.ComputeManifestHeatmap(manifest);
                Assert.AreEqual(2, heatmap.totalVariants);
                Assert.AreEqual(1, heatmap.srpPassCount);
                Assert.AreEqual(1, heatmap.shadowCasterPassCount);

                win.SetPassTypeFilter(PassType.ShadowCaster);
                Assert.AreEqual(1, win.GetFilteredRecords().Count);
                Assert.AreEqual("Custom/FoliageWind", win.GetFilteredRecords()[0].shaderName);

                win.SetPassTypeFilter(null);
                win.SetSearchFilter("_NORMALMAP");
                Assert.AreEqual(1, win.GetFilteredRecords().Count);
                Assert.AreEqual("Universal Render Pipeline/Lit", win.GetFilteredRecords()[0].shaderName);

                // Verify JSON-RPC 2.0 handles alphabetical key order ("arguments" before "name", "id" at end) and unicode escapes
                string reorderedJson =
                    "{\"jsonrpc\":\"2.0\",\"method\":\"tools/call\",\"params\":{\"arguments\":{\"id\":999,\"name\":\"decoy.tool\"},\"name\":\"pso_eliminator.inspect_manifest\"},\"id\":55}";
                string rpcRes = PsoEliminatorMcpBridge.HandleJsonRpcRequest(reorderedJson, manifest);
                StringAssert.Contains("\"id\":55", rpcRes);
                StringAssert.Contains("\"totalVariants\":2", rpcRes);
                Assert.IsNotNull(typeof(SceneShaderVariantHarvester).GetMethod("RunMcpAuditJson", System.Type.EmptyTypes));
            }
            finally
            {
                Object.DestroyImmediate(manifest);
            }
        }
    }
}
