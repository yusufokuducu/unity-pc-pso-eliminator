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

        public static string[] NormalizeKeywords(string[] rawKeywords)
        {
            if (rawKeywords == null || rawKeywords.Length == 0)
                return Array.Empty<string>();

            var unique = new List<string>(rawKeywords.Length);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < rawKeywords.Length; i++)
            {
                string kw = rawKeywords[i];
                if (string.IsNullOrWhiteSpace(kw))
                    continue;

                string trimmed = kw.Trim();
                if (seen.Add(trimmed))
                {
                    unique.Add(trimmed);
                }
            }

            if (unique.Count == 0)
                return Array.Empty<string>();

            unique.Sort(StringComparer.Ordinal);
            return unique.ToArray();
        }

        public string GetCanonicalKey()
        {
            string sName = (shaderName ?? string.Empty).Trim();
            string[] normalized = NormalizeKeywords(keywords);
            if (normalized.Length == 0)
                return $"{sName}|{passType}|";

            string joined = string.Join(";", normalized);
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

                rec.shaderName = rec.shaderName.Trim();
                rec.keywords = HarvestedVariantRecord.NormalizeKeywords(rec.keywords);

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
                int passCmp = a.passType.CompareTo(b.passType);
                if (passCmp != 0) return passCmp;
                return string.CompareOrdinal(a.GetCanonicalKey(), b.GetCanonicalKey());
            });

            harvestedRecords = cleanList;
            return initialCount - cleanList.Count;
        }

        /// <summary>
        /// Computes a deterministic 64-bit FNV-1a hex fingerprint combining the current PC GPU driver
        /// identity, RenderTarget format/MSAA profiles, and all harvested PSO variant records.
        /// Used to detect driver updates or manifest changes that invalidate DX12/Vulkan PSO caches.
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
            hash = Fnv1aAppend(hash, "|RT:");

            if (renderTargetProfiles != null)
            {
                for (int i = 0; i < renderTargetProfiles.Count; i++)
                {
                    hash = Fnv1aAppend(hash, renderTargetProfiles[i].ToString());
                    hash = Fnv1aAppend(hash, ",");
                }
            }

            hash = Fnv1aAppend(hash, "|MSAA:");
            if (msaaSampleCounts != null)
            {
                for (int i = 0; i < msaaSampleCounts.Count; i++)
                {
                    hash = Fnv1aAppend(hash, msaaSampleCounts[i].ToString());
                    hash = Fnv1aAppend(hash, ",");
                }
            }
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
