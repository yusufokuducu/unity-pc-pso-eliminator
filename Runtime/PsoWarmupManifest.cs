using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace YusufOkuducu.PcPsoEliminator
{
    public enum PsoRenderTargetProfile : byte
    {
        SdrR8G8B8A8_UNorm = 0,
        HdrR16G16B16A16_SFloat = 1,
        HdrB10G11R11_UFloatPack32 = 2
    }

    [Serializable]
    public struct HarvestedVariantRecord
    {
        public string shaderName;
        public PassType passType;
        public string[] keywords;
        public string sourceAssetPath;

        public string GetCanonicalKey()
        {
            string sName = shaderName ?? string.Empty;
            if (keywords == null || keywords.Length == 0)
                return $"{sName}|{passType}|";

            var copy = new string[keywords.Length];
            int validCount = 0;
            for (int i = 0; i < keywords.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(keywords[i]))
                    copy[validCount++] = keywords[i].Trim();
            }

            Array.Sort(copy, 0, validCount, StringComparer.Ordinal);
            string joined = validCount > 0 ? string.Join(";", copy, 0, validCount) : string.Empty;
            return $"{sName}|{passType}|{joined}";
        }
    }

    [CreateAssetMenu(
        fileName = "PsoWarmupManifest",
        menuName = "PC PSO Eliminator/PSO Warmup Manifest")]
    public sealed class PsoWarmupManifest : ScriptableObject
    {
        public ShaderVariantCollection variantCollection;
        public List<Material> hardwareDrawWarmupMaterials = new List<Material>();
        public List<HarvestedVariantRecord> harvestedRecords = new List<HarvestedVariantRecord>();

        [Header("DX12 / Vulkan Hardware RenderTarget State Profiles")]
        public List<PsoRenderTargetProfile> renderTargetProfiles = new List<PsoRenderTargetProfile>
        {
            PsoRenderTargetProfile.SdrR8G8B8A8_UNorm,
            PsoRenderTargetProfile.HdrR16G16B16A16_SFloat
        };

        public List<int> msaaSampleCounts = new List<int> { 1 };

        public string lastHarvestTimestampUtc;
        public int uniqueShaderCount;
        public int uniqueMaterialCount;

        /// <summary>
        /// Deduplicates and canonically sorts harvested variant records in-place.
        /// Returns the number of duplicate records removed.
        /// </summary>
        public int DeduplicateAndSortRecords()
        {
            if (harvestedRecords == null || harvestedRecords.Count == 0)
                return 0;

            int initialCount = harvestedRecords.Count;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var cleanList = new List<HarvestedVariantRecord>(initialCount);

            for (int i = 0; i < initialCount; i++)
            {
                HarvestedVariantRecord rec = harvestedRecords[i];
                if (string.IsNullOrWhiteSpace(rec.shaderName))
                    continue;

                if (rec.keywords != null && rec.keywords.Length > 1)
                {
                    Array.Sort(rec.keywords, StringComparer.Ordinal);
                }

                string key = rec.GetCanonicalKey();
                if (seen.Add(key))
                {
                    cleanList.Add(rec);
                }
            }

            cleanList.Sort((a, b) =>
            {
                int cmp = string.CompareOrdinal(a.shaderName, b.shaderName);
                if (cmp != 0) return cmp;
                return a.passType.CompareTo(b.passType);
            });

            harvestedRecords = cleanList;
            return initialCount - cleanList.Count;
        }

        /// <summary>
        /// Computes a deterministic 64-bit FNV-1a hex fingerprint combining the current PC GPU driver
        /// identity and all harvested PSO variant records. Used to detect driver updates that invalidate
        /// DX12/Vulkan driver shader caches.
        /// </summary>
        public string ComputeDriverAndManifestFingerprint(
            string gpuDeviceName,
            string gpuDriverVersion,
            string graphicsApi)
        {
            ulong hash = 14695981039346656037UL;
            hash = Fnv1aAppend(hash, gpuDeviceName ?? string.Empty);
            hash = Fnv1aAppend(hash, "|");
            hash = Fnv1aAppend(hash, gpuDriverVersion ?? string.Empty);
            hash = Fnv1aAppend(hash, "|");
            hash = Fnv1aAppend(hash, graphicsApi ?? string.Empty);
            hash = Fnv1aAppend(hash, "|");

            if (harvestedRecords != null)
            {
                for (int i = 0; i < harvestedRecords.Count; i++)
                {
                    hash = Fnv1aAppend(hash, harvestedRecords[i].GetCanonicalKey());
                    hash = Fnv1aAppend(hash, "\n");
                }
            }

            return hash.ToString("X16");
        }

        public string ExportManifestReportToMarkdown()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# PC DX12 / Vulkan PSO Warmup Manifest Report");
            sb.AppendLine();
            sb.AppendLine($"- **Last Harvest (UTC)**: `{lastHarvestTimestampUtc ?? "N/A"}`");
            sb.AppendLine($"- **Unique Shaders**: {uniqueShaderCount}");
            sb.AppendLine($"- **Hardware Draw Materials**: {uniqueMaterialCount}");
            sb.AppendLine($"- **Harvested PSO Variants**: {(harvestedRecords != null ? harvestedRecords.Count : 0)}");
            sb.AppendLine();
            sb.AppendLine("| Shader | PassType | Keywords | Source Asset |");
            sb.AppendLine("|---|---|---|---|");

            if (harvestedRecords != null)
            {
                for (int i = 0; i < harvestedRecords.Count; i++)
                {
                    HarvestedVariantRecord r = harvestedRecords[i];
                    string kw = (r.keywords != null && r.keywords.Length > 0)
                        ? string.Join(" ", r.keywords)
                        : "_<None>_";
                    sb.Append("| `").Append(r.shaderName).Append("` | `")
                      .Append(r.passType).Append("` | ")
                      .Append(kw).Append(" | `")
                      .Append(r.sourceAssetPath ?? string.Empty).AppendLine("` |");
                }
            }

            return sb.ToString();
        }

        private static ulong Fnv1aAppend(ulong hash, string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                hash ^= text[i];
                hash *= 1099511628211UL;
            }
            return hash;
        }
    }
}
