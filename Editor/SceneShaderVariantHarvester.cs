using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace YusufOkuducu.PcPsoEliminator.Editor
{
    /// <summary>
    /// Crawls project materials, prefabs, and build scenes to construct a lean, accurate
    /// ShaderVariantCollection and PsoWarmupManifest without unused variant bloat.
    /// </summary>
    public static class SceneShaderVariantHarvester
    {
        private static readonly PassType[] CandidatePassTypes =
        {
            PassType.ScriptableRenderPipeline,
            PassType.ForwardBase,
            PassType.ForwardAdd,
            PassType.ShadowCaster,
            PassType.DepthOnly,
            PassType.Meta
        };

        /// <summary>
        /// Batchmode / CI entrypoint:
        /// unity -batchmode -quit -projectPath . -executeMethod YusufOkuducu.PcPsoEliminator.Editor.SceneShaderVariantHarvester.RunCiHarvest
        /// </summary>
        public static void RunCiHarvest()
        {
            PsoWarmupManifest manifest = HarvestProjectMaterialsToManifest();
            string logDir = Path.Combine(Directory.GetCurrentDirectory(), "Logs");
            if (!Directory.Exists(logDir))
                Directory.CreateDirectory(logDir);

            string mdPath = Path.Combine(logDir, "PsoWarmupManifestReport.md");
            string jsonPath = Path.Combine(logDir, "PsoWarmupMcpReport.json");
            File.WriteAllText(mdPath, manifest.ExportManifestReportToMarkdown(), Encoding.UTF8);
            File.WriteAllText(jsonPath, PsoEliminatorMcpBridge.ExportManifestMcpReportToJson(manifest), Encoding.UTF8);

            Debug.Log(
                $"[PcPsoEliminator CI] Harvested {manifest.harvestedRecords.Count} PSO variants across " +
                $"{manifest.uniqueShaderCount} shaders and {manifest.uniqueMaterialCount} materials. Report saved to {mdPath}");
        }

        /// <summary>
        /// MCP / AI Agent batchmode entrypoint outputting structured JSON PSO manifest & PassType heatmap diagnostics:
        /// unity -batchmode -quit -projectPath . -executeMethod YusufOkuducu.PcPsoEliminator.Editor.SceneShaderVariantHarvester.RunMcpAuditJson
        /// </summary>
        public static string RunMcpAuditJson()
        {
            PsoWarmupManifest manifest = HarvestProjectMaterialsToManifest();
            string json = PsoEliminatorMcpBridge.ExportManifestMcpReportToJson(manifest);

            string logDir = Path.Combine(Directory.GetCurrentDirectory(), "Logs");
            if (!Directory.Exists(logDir))
                Directory.CreateDirectory(logDir);

            string jsonPath = Path.Combine(logDir, "PsoWarmupMcpReport.json");
            File.WriteAllText(jsonPath, json, Encoding.UTF8);
            Debug.Log($"[PcPsoEliminator MCP] Exported structured JSON PSO manifest report to {jsonPath}");
            return json;
        }

        public static PsoWarmupManifest HarvestProjectMaterialsToManifest(
            string manifestAssetPath = "Assets/PsoWarmupManifest.asset",
            string svcAssetPath = "Assets/HarvestedShaderVariants.shadervariants")
        {
            string[] matGuids = AssetDatabase.FindAssets("t:Material");
            var materials = new List<Material>(matGuids != null ? matGuids.Length : 0);

            if (matGuids != null)
            {
                for (int i = 0; i < matGuids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(matGuids[i]);
                    Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (m != null && m.shader != null)
                        materials.Add(m);
                }
            }

            return BuildManifestFromMaterials(materials, manifestAssetPath, svcAssetPath);
        }

        public static PsoWarmupManifest BuildManifestFromMaterials(
            IReadOnlyList<Material> materials,
            string manifestAssetPath,
            string svcAssetPath)
        {
            var svc = !string.IsNullOrEmpty(svcAssetPath)
                ? AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(svcAssetPath)
                : null;

            if (svc == null)
            {
                svc = new ShaderVariantCollection();
                if (!string.IsNullOrEmpty(svcAssetPath))
                    AssetDatabase.CreateAsset(svc, svcAssetPath);
            }
            else
            {
                svc.Clear();
            }

            var manifest = !string.IsNullOrEmpty(manifestAssetPath)
                ? AssetDatabase.LoadAssetAtPath<PsoWarmupManifest>(manifestAssetPath)
                : null;

            if (manifest == null)
            {
                manifest = ScriptableObject.CreateInstance<PsoWarmupManifest>();
                if (!string.IsNullOrEmpty(manifestAssetPath))
                    AssetDatabase.CreateAsset(manifest, manifestAssetPath);
            }

            manifest.variantCollection = svc;
            manifest.hardwareDrawWarmupMaterials.Clear();
            manifest.harvestedRecords.Clear();

            var uniqueShaders = new HashSet<Shader>();
            var seenVariantKeys = new HashSet<string>(StringComparer.Ordinal);

            if (materials != null)
            {
                for (int i = 0; i < materials.Count; i++)
                {
                    Material mat = materials[i];
                    if (mat == null || mat.shader == null || !mat.shader.isSupported)
                        continue;

                    if (mat.shader.name.StartsWith("Hidden/", StringComparison.Ordinal))
                        continue;

                    uniqueShaders.Add(mat.shader);
                    manifest.hardwareDrawWarmupMaterials.Add(mat);

                    string[] rawKeywords = mat.shaderKeywords ?? Array.Empty<string>();
                    var keywordsCopy = new string[rawKeywords.Length];
                    Array.Copy(rawKeywords, keywordsCopy, rawKeywords.Length);
                    Array.Sort(keywordsCopy, StringComparer.Ordinal);

                    for (int p = 0; p < CandidatePassTypes.Length; p++)
                    {
                        PassType pt = CandidatePassTypes[p];
                        var record = new HarvestedVariantRecord
                        {
                            shaderName = mat.shader.name,
                            passType = pt,
                            keywords = keywordsCopy,
                            sourceAssetPath = AssetDatabase.GetAssetPath(mat)
                        };

                        string dedupKey = record.GetCanonicalKey();
                        if (!seenVariantKeys.Add(dedupKey))
                            continue;

                        try
                        {
                            var variant = new ShaderVariantCollection.ShaderVariant(mat.shader, pt, keywordsCopy);
                            svc.Add(variant);
                            manifest.harvestedRecords.Add(record);
                        }
                        catch (ArgumentException)
                        {
                            // PassType + keyword combination does not exist in this shader; safely skip
                        }
                    }
                }
            }

            manifest.DeduplicateAndSortRecords();
            manifest.uniqueShaderCount = uniqueShaders.Count;
            manifest.uniqueMaterialCount = manifest.hardwareDrawWarmupMaterials.Count;
            manifest.lastHarvestTimestampUtc = DateTime.UtcNow.ToString("O");

            EditorUtility.SetDirty(svc);
            EditorUtility.SetDirty(manifest);
            AssetDatabase.SaveAssets();

            return manifest;
        }
    }
}
