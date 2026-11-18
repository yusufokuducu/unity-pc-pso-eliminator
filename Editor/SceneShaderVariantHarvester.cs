using System;
using System.Collections.Generic;
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
        private static readonly PassType[]CandidatePassTypes =
        {
            PassType.ScriptableRenderPipeline,
            PassType.ForwardBase,
            PassType.ForwardAdd,
            PassType.ShadowCaster,
            PassType.DepthOnly,
            PassType.Meta
        };

        public static PsoWarmupManifest HarvestProjectMaterialsToManifest(
            string manifestAssetPath = "Assets/PsoWarmupManifest.asset",
            string svcAssetPath = "Assets/HarvestedShaderVariants.shadervariants")
        {
            string[] matGuids = AssetDatabase.FindAssets("t:Material");
            var materials = new List<Material>(matGuids.Length);

            for (int i = 0; i < matGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(matGuids[i]);
                Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m != null && m.shader != null)
                    materials.Add(m);
            }

            return BuildManifestFromMaterials(materials, manifestAssetPath, svcAssetPath);
        }

        public static PsoWarmupManifest BuildManifestFromMaterials(
            IReadOnlyList<Material> materials,
            string manifestAssetPath,
            string svcAssetPath)
        {
            var svc = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(svcAssetPath);
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

            var manifest = AssetDatabase.LoadAssetAtPath<PsoWarmupManifest>(manifestAssetPath);
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

            for (int i = 0; i < materials.Count; i++)
            {
                Material mat = materials[i];
                if (mat == null || mat.shader == null || !mat.shader.isSupported)
                    continue;

                if (mat.shader.name.StartsWith("Hidden/", StringComparison.Ordinal))
                    continue;

                uniqueShaders.Add(mat.shader);
                manifest.hardwareDrawWarmupMaterials.Add(mat);

                string[] keywords = mat.shaderKeywords ?? Array.Empty<string>();
                Array.Sort(keywords, StringComparer.Ordinal);
                string joinedKw = string.Join(";", keywords);

                for (int p = 0; p < CandidatePassTypes.Length; p++)
                {
                    PassType pt = CandidatePassTypes[p];
                    string dedupKey = $"{mat.shader.name}|{pt}|{joinedKw}";
                    if (!seenVariantKeys.Add(dedupKey))
                        continue;

                    try
                    {
                        var variant = new ShaderVariantCollection.ShaderVariant(mat.shader, pt, keywords);
                        svc.Add(variant);
                        manifest.harvestedRecords.Add(new HarvestedVariantRecord
                        {
                            shaderName = mat.shader.name,
                            passType = pt,
                            keywords = keywords,
                            sourceAssetPath = AssetDatabase.GetAssetPath(mat)
                        });
                    }
                    catch (ArgumentException)
                    {
                        // PassType + keyword combination does not exist in this shader; safely skip
                    }
                }
            }

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
