using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace YusufOkuducu.PcPsoEliminator.Editor
{
    [Serializable]
    public struct PsoManifestHeatmapSummary
    {
        public int totalVariants;
        public int uniqueShaderCount;
        public int uniqueMaterialCount;
        public int srpPassCount;
        public int shadowCasterPassCount;
        public int depthOnlyPassCount;
        public int forwardPassCount;
        public int metaPassCount;
        public float srpPassRatio;
        public float shadowCasterPassRatio;
        public float depthOnlyPassRatio;
        public float forwardPassRatio;
        public float metaPassRatio;
        public int offscreenTargetCombinationCount;
        public string driverFingerprint;
    }

    [Serializable]
    public sealed class PsoManifestAutoFixReport
    {
        public bool modified;
        public int duplicateVariantsRemoved;
        public int invalidMaterialsRemoved;
        public int msaaEntriesNormalized;
        public int totalVariantsAfter;
        public string fingerprintBefore;
        public string fingerprintAfter;
        public List<string> appliedFixes = new List<string>();
    }

    /// <summary>
    /// Model Context Protocol (MCP) & AI Agent JSON-RPC 2.0 Bridge for PC DX12/Vulkan PSO Stutter Eliminator.
    /// Allows AI coding assistants (Cursor, Claude, Antigravity, Unity MCP servers) and CI pipelines
    /// to inspect PSO warmup manifests, driver fingerprints, runtime compilation hitches, and invoke automated manifest optimization.
    /// </summary>
    public static class PsoEliminatorMcpBridge
    {
        public const string ServerName = "unity-pc-pso-eliminator";
        public const string PackageVersion = "1.2.0";
        public const string McpProtocolVersion = "2024-11-05";

        public static PsoManifestHeatmapSummary ComputeManifestHeatmap(PsoWarmupManifest manifest)
        {
            var h = new PsoManifestHeatmapSummary
            {
                driverFingerprint = "0000000000000000"
            };
            if (manifest == null)
                return h;

            h.driverFingerprint = manifest.ComputeDriverAndManifestFingerprint(
                SystemInfo.graphicsDeviceName,
                SystemInfo.graphicsDeviceVersion,
                SystemInfo.graphicsDeviceType.ToString());

            var shaderSet = new HashSet<string>(StringComparer.Ordinal);
            if (manifest.harvestedRecords != null)
            {
                for (int i = 0; i < manifest.harvestedRecords.Count; i++)
                {
                    HarvestedVariantRecord rec = manifest.harvestedRecords[i];
                    if (string.IsNullOrWhiteSpace(rec.shaderName))
                        continue;

                    h.totalVariants++;
                    shaderSet.Add(rec.shaderName.Trim());

                    switch (rec.passType)
                    {
                        case PassType.ScriptableRenderPipeline:
                            h.srpPassCount++;
                            break;
                        case PassType.ShadowCaster:
                            h.shadowCasterPassCount++;
                            break;
                        case PassType.DepthOnly:
                            h.depthOnlyPassCount++;
                            break;
                        case PassType.ForwardBase:
                        case PassType.ForwardAdd:
                        case PassType.Normal:
                            h.forwardPassCount++;
                            break;
                        case PassType.Meta:
                            h.metaPassCount++;
                            break;
                    }
                }
            }

            h.uniqueShaderCount = Math.Max(manifest.uniqueShaderCount, shaderSet.Count);
            h.uniqueMaterialCount = manifest.hardwareDrawWarmupMaterials != null
                ? manifest.hardwareDrawWarmupMaterials.Count
                : manifest.uniqueMaterialCount;

            int rtProfiles = (manifest.renderTargetProfiles != null && manifest.renderTargetProfiles.Count > 0)
                ? manifest.renderTargetProfiles.Count
                : 1;
            int msaaCounts = (manifest.msaaSampleCounts != null && manifest.msaaSampleCounts.Count > 0)
                ? manifest.msaaSampleCounts.Count
                : 1;
            h.offscreenTargetCombinationCount = rtProfiles * msaaCounts;

            if (h.totalVariants > 0)
            {
                h.srpPassRatio = (float)h.srpPassCount / h.totalVariants;
                h.shadowCasterPassRatio = (float)h.shadowCasterPassCount / h.totalVariants;
                h.depthOnlyPassRatio = (float)h.depthOnlyPassCount / h.totalVariants;
                h.forwardPassRatio = (float)h.forwardPassCount / h.totalVariants;
                h.metaPassRatio = (float)h.metaPassCount / h.totalVariants;
            }

            return h;
        }

        /// <summary>
        /// Automatically deduplicates, normalizes, and optimizes a PsoWarmupManifest:
        /// 1. Removes duplicate or empty variant records and canonically sorts keywords.
        /// 2. Removes null, missing-shader, or unsupported materials from hardwareDrawWarmupMaterials.
        /// 3. Sanitizes and deduplicates MSAA sample counts to valid hardware powers of two (1, 2, 4, 8).
        /// 4. Deduplicates RenderTarget format profiles and ensures SDR + HDR defaults if empty.
        /// </summary>
        public static PsoManifestAutoFixReport AutoFixAndOptimizeManifest(PsoWarmupManifest manifest)
        {
            var report = new PsoManifestAutoFixReport();
            if (manifest == null)
                return report;

            report.fingerprintBefore = manifest.ComputeDriverAndManifestFingerprint(
                SystemInfo.graphicsDeviceName,
                SystemInfo.graphicsDeviceVersion,
                SystemInfo.graphicsDeviceType.ToString());

            // 1. Deduplicate and canonically sort variant records
            int removedVariants = manifest.DeduplicateAndSortRecords();
            if (removedVariants > 0)
            {
                report.duplicateVariantsRemoved = removedVariants;
                report.appliedFixes.Add(
                    $"Removed {removedVariants} duplicate/empty PSO variant record(s) and canonically sorted shader keywords.");
            }

            // 2. Clean up hardwareDrawWarmupMaterials
            if (manifest.hardwareDrawWarmupMaterials != null)
            {
                int beforeMatCount = manifest.hardwareDrawWarmupMaterials.Count;
                var seenMats = new HashSet<Material>();
                var cleanMats = new List<Material>(beforeMatCount);

                for (int i = 0; i < beforeMatCount; i++)
                {
                    Material m = manifest.hardwareDrawWarmupMaterials[i];
                    if (m == null || m.shader == null || !m.shader.isSupported)
                        continue;
                    if (seenMats.Add(m))
                        cleanMats.Add(m);
                }

                int removedMats = beforeMatCount - cleanMats.Count;
                if (removedMats > 0)
                {
                    manifest.hardwareDrawWarmupMaterials = cleanMats;
                    report.invalidMaterialsRemoved = removedMats;
                    report.appliedFixes.Add(
                        $"Removed {removedMats} null, duplicate, or unsupported Material reference(s) from hardware draw warmup list.");
                }
            }

            // 3. Sanitize and deduplicate MSAA sample counts
            if (manifest.msaaSampleCounts == null || manifest.msaaSampleCounts.Count == 0)
            {
                manifest.msaaSampleCounts = new List<int> { 1 };
                report.msaaEntriesNormalized = 1;
                report.appliedFixes.Add("Initialized empty msaaSampleCounts to [1x].");
            }
            else
            {
                var seenMsaa = new HashSet<int>();
                var cleanMsaa = new List<int>(manifest.msaaSampleCounts.Count);
                int msaaChanges = 0;

                for (int i = 0; i < manifest.msaaSampleCounts.Count; i++)
                {
                    int raw = manifest.msaaSampleCounts[i];
                    int valid = AsyncPsoWarmupOrchestrator.SanitizeMsaaSampleCount(raw);
                    if (valid != raw)
                        msaaChanges++;
                    if (seenMsaa.Add(valid))
                        cleanMsaa.Add(valid);
                    else
                        msaaChanges++;
                }

                cleanMsaa.Sort();
                if (msaaChanges > 0)
                {
                    manifest.msaaSampleCounts = cleanMsaa;
                    report.msaaEntriesNormalized = msaaChanges;
                    report.appliedFixes.Add(
                        $"Normalized and deduplicated MSAA sample counts to hardware-valid values: [{string.Join(", ", cleanMsaa)}].");
                }
            }

            // 4. Deduplicate RenderTarget format profiles
            if (manifest.renderTargetProfiles == null || manifest.renderTargetProfiles.Count == 0)
            {
                manifest.renderTargetProfiles = new List<PsoRenderTargetProfile>
                {
                    PsoRenderTargetProfile.SdrR8G8B8A8_UNorm,
                    PsoRenderTargetProfile.HdrR16G16B16A16_SFloat
                };
                report.appliedFixes.Add("Restored default SDR (R8G8B8A8_UNorm) and HDR (R16G16B16A16_SFloat) RenderTarget profiles.");
            }
            else
            {
                var seenProfiles = new HashSet<PsoRenderTargetProfile>();
                var cleanProfiles = new List<PsoRenderTargetProfile>(manifest.renderTargetProfiles.Count);
                for (int i = 0; i < manifest.renderTargetProfiles.Count; i++)
                {
                    if (seenProfiles.Add(manifest.renderTargetProfiles[i]))
                        cleanProfiles.Add(manifest.renderTargetProfiles[i]);
                }

                if (cleanProfiles.Count != manifest.renderTargetProfiles.Count)
                {
                    manifest.renderTargetProfiles = cleanProfiles;
                    report.appliedFixes.Add("Removed duplicate RenderTarget format profiles.");
                }
            }

            // 5. Recompute unique counts
            var uniqueShaderNames = new HashSet<string>(StringComparer.Ordinal);
            if (manifest.harvestedRecords != null)
            {
                for (int i = 0; i < manifest.harvestedRecords.Count; i++)
                {
                    if (!string.IsNullOrEmpty(manifest.harvestedRecords[i].shaderName))
                        uniqueShaderNames.Add(manifest.harvestedRecords[i].shaderName);
                }
            }
            manifest.uniqueShaderCount = uniqueShaderNames.Count;
            manifest.uniqueMaterialCount = manifest.hardwareDrawWarmupMaterials != null
                ? manifest.hardwareDrawWarmupMaterials.Count
                : 0;

            report.totalVariantsAfter = manifest.harvestedRecords != null ? manifest.harvestedRecords.Count : 0;
            report.fingerprintAfter = manifest.ComputeDriverAndManifestFingerprint(
                SystemInfo.graphicsDeviceName,
                SystemInfo.graphicsDeviceVersion,
                SystemInfo.graphicsDeviceType.ToString());
            report.modified = report.appliedFixes.Count > 0;

            EditorUtility.SetDirty(manifest);
            return report;
        }

        public static string ExportManifestMcpReportToJson(
            PsoWarmupManifest manifest,
            IReadOnlyList<PsoHitchEvent> runtimeHitches = null)
        {
            PsoManifestHeatmapSummary heatmap = ComputeManifestHeatmap(manifest);
            var sb = new StringBuilder(2048);
            sb.Append('{');
            AppendJsonProp(sb, "tool", ServerName, true);
            AppendJsonProp(sb, "version", PackageVersion, true);
            sb.Append("\"heatmap\":");
            AppendHeatmapJson(sb, heatmap);
            sb.Append(",\"manifest\":");
            AppendManifestJson(sb, manifest);
            sb.Append(",\"runtimeHitches\":[");
            if (runtimeHitches != null)
            {
                for (int i = 0; i < runtimeHitches.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    PsoHitchEvent h = runtimeHitches[i];
                    sb.Append('{');
                    AppendJsonProp(sb, "frameIndex", h.frameIndex, true);
                    AppendJsonProp(sb, "frameTimeMs", h.frameTimeMs, true);
                    AppendJsonProp(sb, "rollingAverageMs", h.rollingAverageMs, true);
                    AppendJsonProp(sb, "spikeRatio", h.spikeRatio, true);
                    AppendJsonProp(sb, "graphicsDeviceType", h.graphicsDeviceType, false);
                    sb.Append('}');
                }
            }
            sb.Append("]}");
            return sb.ToString();
        }

        /// <summary>
        /// Processes a JSON-RPC 2.0 request according to the Model Context Protocol (MCP) specification.
        /// Supported methods: "initialize", "tools/list", "tools/call".
        /// </summary>
        public static string HandleJsonRpcRequest(string jsonRpcRequest, PsoWarmupManifest overrideManifest = null)
        {
            if (string.IsNullOrWhiteSpace(jsonRpcRequest))
                return BuildJsonRpcError("null", -32700, "Parse error: Empty JSON-RPC request.");

            string trimmed = jsonRpcRequest.Trim();
            if (!trimmed.StartsWith("{", StringComparison.Ordinal) || !trimmed.EndsWith("}", StringComparison.Ordinal))
                return BuildJsonRpcError("null", -32700, "Parse error: Malformed JSON object.");

            string idRaw = ExtractJsonRpcIdRaw(trimmed);
            string method = ExtractJsonStringField(trimmed, "method");

            if (string.IsNullOrEmpty(method))
                return BuildJsonRpcError(idRaw, -32600, "Invalid Request: Missing 'method' field.");

            if (string.Equals(method, "initialize", StringComparison.Ordinal))
            {
                var res = new StringBuilder();
                res.Append("{\"protocolVersion\":\"").Append(McpProtocolVersion).Append("\",");
                res.Append("\"capabilities\":{\"tools\":{\"listChanged\":false}},");
                res.Append("\"serverInfo\":{\"name\":\"").Append(ServerName).Append("\",\"version\":\"").Append(PackageVersion).Append("\"}}");
                return BuildJsonRpcResult(idRaw, res.ToString());
            }

            if (string.Equals(method, "tools/list", StringComparison.Ordinal))
            {
                return BuildJsonRpcResult(idRaw, BuildToolsListJson());
            }

            if (string.Equals(method, "tools/call", StringComparison.Ordinal))
            {
                string paramsObj = ExtractJsonObjectField(trimmed, "params") ?? trimmed;
                string toolName = ExtractJsonStringField(paramsObj, "name");
                if (string.IsNullOrEmpty(toolName))
                    return BuildJsonRpcError(idRaw, -32602, "Invalid params: Missing tool 'name' in tools/call.");

                if (string.Equals(toolName, "pso_eliminator.inspect_manifest", StringComparison.Ordinal))
                {
                    PsoWarmupManifest manifest = overrideManifest ?? FindOrLoadActiveManifest();
                    string payload = ExportManifestMcpReportToJson(manifest);
                    return BuildJsonRpcToolCallResult(idRaw, payload);
                }

                if (string.Equals(toolName, "pso_eliminator.harvest_project_materials", StringComparison.Ordinal))
                {
                    PsoWarmupManifest manifest = SceneShaderVariantHarvester.HarvestProjectMaterialsToManifest();
                    string payload = ExportManifestMcpReportToJson(manifest);
                    return BuildJsonRpcToolCallResult(idRaw, payload);
                }

                if (string.Equals(toolName, "pso_eliminator.autofix_manifest", StringComparison.Ordinal))
                {
                    PsoWarmupManifest manifest = overrideManifest ?? FindOrLoadActiveManifest();
                    PsoManifestAutoFixReport fixReport = AutoFixAndOptimizeManifest(manifest);
                    string payload = SerializeAutoFixReportJson(fixReport);
                    return BuildJsonRpcToolCallResult(idRaw, payload);
                }

                if (string.Equals(toolName, "pso_eliminator.analyze_runtime_hitches", StringComparison.Ordinal))
                {
                    var activeOrch = UnityEngine.Object.FindFirstObjectByType<AsyncPsoWarmupOrchestrator>();
                    IReadOnlyList<PsoHitchEvent> hitches = activeOrch != null ? activeOrch.DetectedHitches : Array.Empty<PsoHitchEvent>();
                    PsoWarmupManifest manifest = overrideManifest ?? FindOrLoadActiveManifest();
                    string payload = ExportManifestMcpReportToJson(manifest, hitches);
                    return BuildJsonRpcToolCallResult(idRaw, payload);
                }

                return BuildJsonRpcError(idRaw, -32602, $"Unknown tool '{EscapeJson(toolName)}'.");
            }

            return BuildJsonRpcError(idRaw, -32601, $"Method not found: '{EscapeJson(method)}'.");
        }

        private static PsoWarmupManifest FindOrLoadActiveManifest()
        {
            string[] guids = AssetDatabase.FindAssets("t:PsoWarmupManifest");
            if (guids != null && guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                PsoWarmupManifest loaded = AssetDatabase.LoadAssetAtPath<PsoWarmupManifest>(path);
                if (loaded != null) return loaded;
            }
            return ScriptableObject.CreateInstance<PsoWarmupManifest>();
        }

        public static string SerializeAutoFixReportJson(PsoManifestAutoFixReport r)
        {
            var sb = new StringBuilder(512);
            sb.Append('{');
            AppendJsonProp(sb, "modified", r.modified, true);
            AppendJsonProp(sb, "duplicateVariantsRemoved", r.duplicateVariantsRemoved, true);
            AppendJsonProp(sb, "invalidMaterialsRemoved", r.invalidMaterialsRemoved, true);
            AppendJsonProp(sb, "msaaEntriesNormalized", r.msaaEntriesNormalized, true);
            AppendJsonProp(sb, "totalVariantsAfter", r.totalVariantsAfter, true);
            AppendJsonProp(sb, "fingerprintBefore", r.fingerprintBefore, true);
            AppendJsonProp(sb, "fingerprintAfter", r.fingerprintAfter, true);
            sb.Append("\"appliedFixes\":[");
            if (r.appliedFixes != null)
            {
                for (int i = 0; i < r.appliedFixes.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append('"').Append(EscapeJson(r.appliedFixes[i])).Append('"');
                }
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static string BuildToolsListJson()
        {
            return "{\"tools\":[" +
                "{\"name\":\"pso_eliminator.inspect_manifest\",\"description\":\"Inspects the active PsoWarmupManifest and returns 64-bit FNV-1a driver fingerprint, PassType variant distribution heatmap, and harvested DX12/Vulkan PSO records.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}," +
                "{\"name\":\"pso_eliminator.harvest_project_materials\",\"description\":\"Crawls project materials to harvest and deduplicate active Shader + PassType + Keyword combinations into PsoWarmupManifest.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}," +
                "{\"name\":\"pso_eliminator.autofix_manifest\",\"description\":\"Automatically deduplicates variant records, canonically sorts keywords, removes invalid materials, and sanitizes MSAA/RenderTarget profiles on the PSO manifest.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}," +
                "{\"name\":\"pso_eliminator.analyze_runtime_hitches\",\"description\":\"Returns detected runtime DX12/Vulkan PSO compilation hitch spikes from AsyncPsoWarmupOrchestrator.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}" +
                "]}";
        }

        private static void AppendHeatmapJson(StringBuilder sb, PsoManifestHeatmapSummary h)
        {
            sb.Append('{');
            AppendJsonProp(sb, "totalVariants", h.totalVariants, true);
            AppendJsonProp(sb, "uniqueShaderCount", h.uniqueShaderCount, true);
            AppendJsonProp(sb, "uniqueMaterialCount", h.uniqueMaterialCount, true);
            AppendJsonProp(sb, "srpPassCount", h.srpPassCount, true);
            AppendJsonProp(sb, "shadowCasterPassCount", h.shadowCasterPassCount, true);
            AppendJsonProp(sb, "depthOnlyPassCount", h.depthOnlyPassCount, true);
            AppendJsonProp(sb, "forwardPassCount", h.forwardPassCount, true);
            AppendJsonProp(sb, "metaPassCount", h.metaPassCount, true);
            AppendJsonProp(sb, "srpPassRatio", h.srpPassRatio, true);
            AppendJsonProp(sb, "shadowCasterPassRatio", h.shadowCasterPassRatio, true);
            AppendJsonProp(sb, "depthOnlyPassRatio", h.depthOnlyPassRatio, true);
            AppendJsonProp(sb, "forwardPassRatio", h.forwardPassRatio, true);
            AppendJsonProp(sb, "metaPassRatio", h.metaPassRatio, true);
            AppendJsonProp(sb, "offscreenTargetCombinationCount", h.offscreenTargetCombinationCount, true);
            AppendJsonProp(sb, "driverFingerprint", h.driverFingerprint, false);
            sb.Append('}');
        }

        private static void AppendManifestJson(StringBuilder sb, PsoWarmupManifest m)
        {
            sb.Append('{');
            if (m == null)
            {
                AppendJsonProp(sb, "lastHarvestTimestampUtc", "N/A", true);
                AppendJsonProp(sb, "uniqueShaderCount", 0, true);
                AppendJsonProp(sb, "uniqueMaterialCount", 0, true);
                sb.Append("\"records\":[]}");
                return;
            }

            AppendJsonProp(sb, "lastHarvestTimestampUtc", m.lastHarvestTimestampUtc ?? "N/A", true);
            AppendJsonProp(sb, "uniqueShaderCount", m.uniqueShaderCount, true);
            AppendJsonProp(sb, "uniqueMaterialCount", m.uniqueMaterialCount, true);
            sb.Append("\"records\":[");
            if (m.harvestedRecords != null)
            {
                for (int i = 0; i < m.harvestedRecords.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    HarvestedVariantRecord r = m.harvestedRecords[i];
                    sb.Append('{');
                    AppendJsonProp(sb, "shaderName", r.shaderName, true);
                    AppendJsonProp(sb, "passType", r.passType.ToString(), true);
                    AppendJsonProp(sb, "sourceAssetPath", r.sourceAssetPath ?? string.Empty, true);
                    sb.Append("\"keywords\":[");
                    if (r.keywords != null)
                    {
                        for (int k = 0; k < r.keywords.Length; k++)
                        {
                            if (k > 0) sb.Append(',');
                            sb.Append('"').Append(EscapeJson(r.keywords[k])).Append('"');
                        }
                    }
                    sb.Append("]}");
                }
            }
            sb.Append("]}");
        }

        private static string BuildJsonRpcToolCallResult(string idRaw, string structuredJsonPayload)
        {
            var sb = new StringBuilder(structuredJsonPayload.Length + 256);
            sb.Append("{\"content\":[{\"type\":\"text\",\"text\":\"")
              .Append(EscapeJson(structuredJsonPayload))
              .Append("\"}],\"structuredContent\":")
              .Append(structuredJsonPayload)
              .Append(",\"isError\":false}");
            return BuildJsonRpcResult(idRaw, sb.ToString());
        }

        private static string BuildJsonRpcResult(string idRaw, string resultJson)
        {
            return $"{{\"jsonrpc\":\"2.0\",\"id\":{idRaw},\"result\":{resultJson}}}";
        }

        private static string BuildJsonRpcError(string idRaw, int code, string message)
        {
            return $"{{\"jsonrpc\":\"2.0\",\"id\":{idRaw},\"error\":{{\"code\":{code},\"message\":\"{EscapeJson(message)}\"}}}}";
        }

        private static void AppendJsonProp(StringBuilder sb, string key, string val, bool trailingComma)
        {
            sb.Append('"').Append(EscapeJson(key)).Append("\":\"").Append(EscapeJson(val ?? string.Empty)).Append('"');
            if (trailingComma) sb.Append(',');
        }

        private static void AppendJsonProp(StringBuilder sb, string key, int val, bool trailingComma)
        {
            sb.Append('"').Append(EscapeJson(key)).Append("\":").Append(val);
            if (trailingComma) sb.Append(',');
        }

        private static void AppendJsonProp(StringBuilder sb, string key, float val, bool trailingComma)
        {
            float safe = (float.IsNaN(val) || float.IsInfinity(val)) ? 0f : val;
            sb.Append('"').Append(EscapeJson(key)).Append("\":").Append(safe.ToString("F3", CultureInfo.InvariantCulture));
            if (trailingComma) sb.Append(',');
        }

        private static void AppendJsonProp(StringBuilder sb, string key, bool val, bool trailingComma)
        {
            sb.Append('"').Append(EscapeJson(key)).Append("\":").Append(val ? "true" : "false");
            if (trailingComma) sb.Append(',');
        }

        public static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.AppendFormat(CultureInfo.InvariantCulture, "\\u{0:X4}", (int)c);
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        internal static string ExtractJsonRpcIdRaw(string json)
        {
            if (TryGetDirectJsonPropertyValue(json, "id", out string rawVal) && !string.IsNullOrEmpty(rawVal))
            {
                return rawVal;
            }
            return "null";
        }

        internal static string ExtractJsonObjectField(string json, string fieldName)
        {
            if (TryGetDirectJsonPropertyValue(json, fieldName, out string rawVal) &&
                !string.IsNullOrEmpty(rawVal) &&
                rawVal[0] == '{')
            {
                return rawVal;
            }
            return null;
        }

        internal static string ExtractJsonStringField(string json, string fieldName)
        {
            if (TryGetDirectJsonPropertyValue(json, fieldName, out string rawVal) &&
                !string.IsNullOrEmpty(rawVal) &&
                rawVal.Length >= 2 &&
                rawVal[0] == '"' &&
                rawVal[rawVal.Length - 1] == '"')
            {
                return UnescapeJsonString(rawVal.Substring(1, rawVal.Length - 2));
            }
            return null;
        }

        internal static bool TryGetDirectJsonPropertyValue(string jsonObject, string propertyName, out string rawValue)
        {
            rawValue = null;
            if (string.IsNullOrWhiteSpace(jsonObject) || string.IsNullOrEmpty(propertyName))
                return false;

            string s = jsonObject.Trim();
            if (s.Length < 2 || s[0] != '{')
                return false;

            int i = 1;
            int len = s.Length;
            while (i < len)
            {
                while (i < len && (char.IsWhiteSpace(s[i]) || s[i] == ','))
                    i++;
                if (i >= len || s[i] == '}')
                    break;

                if (s[i] != '"')
                    break;

                int keyStart = i + 1;
                i = SkipJsonStringLiteral(s, i);
                int keyEnd = Math.Max(keyStart, i - 1);
                string key = UnescapeJsonString(s.Substring(keyStart, keyEnd - keyStart));

                while (i < len && char.IsWhiteSpace(s[i]))
                    i++;
                if (i >= len || s[i] != ':')
                    break;
                i++;

                while (i < len && char.IsWhiteSpace(s[i]))
                    i++;
                if (i >= len)
                    break;

                int valStart = i;
                i = SkipJsonValue(s, i);
                int valEnd = i;

                if (string.Equals(key, propertyName, StringComparison.Ordinal))
                {
                    rawValue = s.Substring(valStart, valEnd - valStart).Trim();
                    return true;
                }
            }
            return false;
        }

        private static int SkipJsonStringLiteral(string s, int quoteStartIdx)
        {
            int i = quoteStartIdx + 1;
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '\\' && i < s.Length)
                    i++;
                else if (c == '"')
                    break;
            }
            return i;
        }

        private static int SkipJsonValue(string s, int valStartIdx)
        {
            int i = valStartIdx;
            if (i >= s.Length)
                return i;

            char c = s[i];
            if (c == '"')
                return SkipJsonStringLiteral(s, i);

            if (c == '{' || c == '[')
            {
                int depth = 0;
                while (i < s.Length)
                {
                    char ch = s[i];
                    if (ch == '"')
                    {
                        i = SkipJsonStringLiteral(s, i);
                        continue;
                    }
                    if (ch == '{' || ch == '[') depth++;
                    else if (ch == '}' || ch == ']')
                    {
                        depth--;
                        if (depth == 0)
                            return i + 1;
                    }
                    i++;
                }
                return i;
            }

            while (i < s.Length && s[i] != ',' && s[i] != '}' && s[i] != ']' && !char.IsWhiteSpace(s[i]))
                i++;
            return i;
        }

        private static string UnescapeJsonString(string escaped)
        {
            if (string.IsNullOrEmpty(escaped)) return string.Empty;
            var sb = new StringBuilder(escaped.Length);
            for (int i = 0; i < escaped.Length; i++)
            {
                char c = escaped[i];
                if (c == '\\' && i + 1 < escaped.Length)
                {
                    char next = escaped[++i];
                    switch (next)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 < escaped.Length &&
                                int.TryParse(escaped.Substring(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                            {
                                sb.Append((char)code);
                                i += 4;
                            }
                            break;
                        default: sb.Append(next); break;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }
}
