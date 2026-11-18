using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace YusufOkuducu.PcPsoEliminator.Editor
{
    public sealed class PsoEliminatorWindow : EditorWindow
    {
        private PsoWarmupManifest _activeManifest;
        private Vector2 _scroll;
        private string _searchFilter = string.Empty;
        private PassType? _passTypeFilter;

        // UI Toolkit containers
        private VisualElement _heatmapContainer;
        private ScrollView _recordsScroll;

        public PsoWarmupManifest ActiveManifest => _activeManifest;
        public string SearchFilter => _searchFilter;
        public PassType? PassTypeFilter => _passTypeFilter;

        [MenuItem("Window/Analysis/PC DX12 & Vulkan PSO Stutter Eliminator")]
        public static void ShowWindow()
        {
            var win = GetWindow<PsoEliminatorWindow>("PC PSO Eliminator");
            win.minSize = new Vector2(680f, 500f);
            win.Show();
        }

        private void OnEnable()
        {
            string[] guids = AssetDatabase.FindAssets("t:PsoWarmupManifest");
            if (guids != null && guids.Length > 0)
            {
                _activeManifest = AssetDatabase.LoadAssetAtPath<PsoWarmupManifest>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }
        }

        public void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            if (root == null)
                return;

            root.Clear();
            root.style.paddingTop = 8f;
            root.style.paddingBottom = 8f;
            root.style.paddingLeft = 10f;
            root.style.paddingRight = 10f;

            var titleLabel = new Label("PC DX12 / Vulkan PSO Stutter Eliminator & Async Prewarmer")
            {
                name = "header-title"
            };
            titleLabel.style.fontSize = 15f;
            titleLabel.style.marginBottom = 4f;
            root.Add(titleLabel);

            var infoBox = new HelpBox(
                "Harvests active Material + Shader + PassType + Keyword combinations across your project into a lean ShaderVariantCollection & PsoWarmupManifest, and orchestrates frame-budgeted progressive PSO compilation + multi-format offscreen CommandBuffer draw binding.",
                HelpBoxMessageType.Info);
            infoBox.style.marginBottom = 8f;
            root.Add(infoBox);

            // Action Toolbar Row
            var toolbarRow = new VisualElement { name = "action-toolbar" };
            toolbarRow.style.flexDirection = FlexDirection.Row;
            toolbarRow.style.marginBottom = 8f;

            var harvestBtn = new Button(() =>
            {
                _activeManifest = SceneShaderVariantHarvester.HarvestProjectMaterialsToManifest();
                Selection.activeObject = _activeManifest;
                RefreshUiToolkitView();
            })
            {
                name = "btn-harvest",
                text = "Harvest Project Materials"
            };
            harvestBtn.style.height = 28f;
            harvestBtn.style.flexGrow = 1f;
            harvestBtn.style.marginRight = 4f;

            var autoFixBtn = new Button(() =>
            {
                if (_activeManifest != null)
                {
                    PsoEliminatorMcpBridge.AutoFixAndOptimizeManifest(_activeManifest);
                    RefreshUiToolkitView();
                }
            })
            {
                name = "btn-autofix-manifest",
                text = "Auto-Fix & Deduplicate Manifest"
            };
            autoFixBtn.style.height = 28f;
            autoFixBtn.style.flexGrow = 1f;
            autoFixBtn.style.marginRight = 4f;

            var orchBtn = new Button(AddOrchestratorToScene)
            {
                name = "btn-add-orchestrator",
                text = "Add Async Orchestrator to Scene"
            };
            orchBtn.style.height = 28f;
            orchBtn.style.flexGrow = 1f;
            orchBtn.style.marginRight = 4f;

            var exportBtn = new Button(ExportMarkdownAndJsonReport)
            {
                name = "btn-export",
                text = "Export MD / MCP JSON"
            };
            exportBtn.style.height = 28f;
            exportBtn.style.flexGrow = 1f;

            toolbarRow.Add(harvestBtn);
            toolbarRow.Add(autoFixBtn);
            toolbarRow.Add(orchBtn);
            toolbarRow.Add(exportBtn);
            root.Add(toolbarRow);

            // Search & PassType Filter Row
            var filterRow = new VisualElement { name = "filter-row" };
            filterRow.style.flexDirection = FlexDirection.Row;
            filterRow.style.alignItems = Align.Center;
            filterRow.style.marginBottom = 8f;

            var searchField = new TextField("Search Shader / Keyword")
            {
                name = "search-field",
                value = _searchFilter
            };
            searchField.style.flexGrow = 1f;
            searchField.style.marginRight = 8f;
            searchField.RegisterValueChangedCallback(evt =>
            {
                _searchFilter = evt.newValue ?? string.Empty;
                RefreshUiToolkitView();
            });
            filterRow.Add(searchField);

            AddPassFilterButton(filterRow, "All Passes", null);
            AddPassFilterButton(filterRow, "SRP", PassType.ScriptableRenderPipeline);
            AddPassFilterButton(filterRow, "ShadowCaster", PassType.ShadowCaster);
            AddPassFilterButton(filterRow, "DepthOnly", PassType.DepthOnly);
            AddPassFilterButton(filterRow, "ForwardBase", PassType.ForwardBase);
            root.Add(filterRow);

            _heatmapContainer = new VisualElement { name = "heatmap-container" };
            _heatmapContainer.style.marginBottom = 8f;
            root.Add(_heatmapContainer);

            _recordsScroll = new ScrollView { name = "records-scroll" };
            _recordsScroll.style.flexGrow = 1f;
            root.Add(_recordsScroll);

            RefreshUiToolkitView();
        }

        private void AddPassFilterButton(VisualElement parent, string label, PassType? filter)
        {
            var btn = new Button(() =>
            {
                _passTypeFilter = filter;
                RefreshUiToolkitView();
            })
            {
                name = filter.HasValue ? $"btn-pass-{filter.Value}" : "btn-pass-all",
                text = label
            };
            btn.style.height = 22f;
            btn.style.marginLeft = 2f;
            parent.Add(btn);
        }

        public void SetActiveManifest(PsoWarmupManifest manifest)
        {
            _activeManifest = manifest;
            RefreshUiToolkitView();
        }

        public void SetSearchFilter(string query)
        {
            _searchFilter = query ?? string.Empty;
            RefreshUiToolkitView();
        }

        public void SetPassTypeFilter(PassType? passFilter)
        {
            _passTypeFilter = passFilter;
            RefreshUiToolkitView();
        }

        public List<HarvestedVariantRecord> GetFilteredRecords()
        {
            var list = new List<HarvestedVariantRecord>();
            if (_activeManifest == null || _activeManifest.harvestedRecords == null)
                return list;

            string q = (_searchFilter ?? string.Empty).Trim();
            for (int i = 0; i < _activeManifest.harvestedRecords.Count; i++)
            {
                HarvestedVariantRecord rec = _activeManifest.harvestedRecords[i];
                if (_passTypeFilter.HasValue && rec.passType != _passTypeFilter.Value)
                    continue;

                if (!string.IsNullOrEmpty(q))
                {
                    bool match =
                        (!string.IsNullOrEmpty(rec.shaderName) && rec.shaderName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (!string.IsNullOrEmpty(rec.sourceAssetPath) && rec.sourceAssetPath.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
                        rec.passType.ToString().IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;

                    if (!match && rec.keywords != null)
                    {
                        for (int k = 0; k < rec.keywords.Length; k++)
                        {
                            if (rec.keywords[k] != null && rec.keywords[k].IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                match = true;
                                break;
                            }
                        }
                    }

                    if (!match)
                        continue;
                }

                list.Add(rec);
            }

            return list;
        }

        public void RefreshUiToolkitView()
        {
            if (_heatmapContainer == null || _recordsScroll == null)
                return;

            _heatmapContainer.Clear();
            _recordsScroll.Clear();

            PsoManifestHeatmapSummary heatmap = PsoEliminatorMcpBridge.ComputeManifestHeatmap(_activeManifest);
            _heatmapContainer.Add(BuildManifestHeatmapCard(heatmap));

            List<HarvestedVariantRecord> filtered = GetFilteredRecords();
            int limit = Math.Min(200, filtered.Count);
            for (int i = 0; i < limit; i++)
            {
                _recordsScroll.Add(BuildVariantRecordCard(filtered[i]));
            }
        }

        public static VisualElement BuildManifestHeatmapCard(PsoManifestHeatmapSummary h)
        {
            var card = new VisualElement { name = "pso-heatmap-card" };
            card.style.paddingTop = 8f;
            card.style.paddingBottom = 8f;
            card.style.paddingLeft = 10f;
            card.style.paddingRight = 10f;
            card.style.backgroundColor = new Color(0.16f, 0.16f, 0.18f, 1f);

            var header = new Label(
                $"PSO PassType Distribution Heatmap — Total Variants: {h.totalVariants} | " +
                $"Shaders: {h.uniqueShaderCount} | Materials: {h.uniqueMaterialCount} | " +
                $"Offscreen RT Targets: {h.offscreenTargetCombinationCount} | Fingerprint: {h.driverFingerprint}")
            {
                name = "heatmap-header"
            };
            header.style.marginBottom = 6f;
            card.Add(header);

            // Stacked horizontal PassType distribution bar
            var barTrack = new VisualElement { name = "passtype-distribution-bar" };
            barTrack.style.flexDirection = FlexDirection.Row;
            barTrack.style.height = 14f;
            barTrack.style.marginBottom = 6f;
            barTrack.style.backgroundColor = new Color(0.1f, 0.1f, 0.1f, 1f);

            if (h.totalVariants > 0)
            {
                AddPassSegment(barTrack, "seg-srp", h.srpPassRatio, new Color(0.28f, 0.64f, 0.92f, 1f));
                AddPassSegment(barTrack, "seg-shadow", h.shadowCasterPassRatio, new Color(0.92f, 0.62f, 0.16f, 1f));
                AddPassSegment(barTrack, "seg-depth", h.depthOnlyPassRatio, new Color(0.24f, 0.76f, 0.42f, 1f));
                AddPassSegment(barTrack, "seg-forward", h.forwardPassRatio, new Color(0.68f, 0.44f, 0.88f, 1f));
                AddPassSegment(barTrack, "seg-meta", h.metaPassRatio, new Color(0.88f, 0.34f, 0.44f, 1f));
            }
            card.Add(barTrack);

            var legend = new Label(
                $"SRP Passes: {h.srpPassCount} ({(h.srpPassRatio * 100f):F0}%) | " +
                $"ShadowCaster: {h.shadowCasterPassCount} ({(h.shadowCasterPassRatio * 100f):F0}%) | " +
                $"DepthOnly: {h.depthOnlyPassCount} ({(h.depthOnlyPassRatio * 100f):F0}%) | " +
                $"Forward: {h.forwardPassCount} ({(h.forwardPassRatio * 100f):F0}%) | " +
                $"Meta: {h.metaPassCount} ({(h.metaPassRatio * 100f):F0}%)");
            legend.style.fontSize = 11f;
            card.Add(legend);

            return card;
        }

        private static void AddPassSegment(VisualElement track, string name, float ratio, Color color)
        {
            if (ratio <= 0f) return;
            var seg = new VisualElement { name = name };
            seg.style.flexGrow = ratio;
            seg.style.height = 14f;
            seg.style.backgroundColor = color;
            track.Add(seg);
        }

        private static VisualElement BuildVariantRecordCard(HarvestedVariantRecord rec)
        {
            var card = new VisualElement { name = $"variant-card-{rec.shaderName}-{rec.passType}" };
            card.style.marginBottom = 4f;
            card.style.paddingTop = 4f;
            card.style.paddingBottom = 4f;
            card.style.paddingLeft = 8f;
            card.style.paddingRight = 8f;
            card.style.borderLeftWidth = 3f;
            card.style.borderLeftColor = GetPassTypeColor(rec.passType);
            card.style.backgroundColor = new Color(0.21f, 0.21f, 0.23f, 1f);

            string kwText = (rec.keywords != null && rec.keywords.Length > 0)
                ? string.Join(" ", rec.keywords)
                : "<No Keywords>";

            var label = new Label($"[{rec.passType}] {rec.shaderName} — Keywords: {kwText}");
            label.style.whiteSpace = WhiteSpace.Normal;
            card.Add(label);

            return card;
        }

        public static Color GetPassTypeColor(PassType pt)
        {
            switch (pt)
            {
                case PassType.ScriptableRenderPipeline:
                    return new Color(0.28f, 0.64f, 0.92f, 1f);
                case PassType.ShadowCaster:
                    return new Color(0.92f, 0.62f, 0.16f, 1f);
                case PassType.DepthOnly:
                    return new Color(0.24f, 0.76f, 0.42f, 1f);
                default:
                    return new Color(0.68f, 0.44f, 0.88f, 1f);
            }
        }

        private void OnGUI()
        {
            if (rootVisualElement != null && rootVisualElement.childCount > 0)
                return;

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("PC DX12 / Vulkan PSO Stutter Eliminator & Async Prewarmer", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Harvests active Material + Shader + PassType + Keyword combinations across your project into a lean ShaderVariantCollection & PsoWarmupManifest, and orchestrates frame-budgeted progressive PSO compilation + multi-format offscreen CommandBuffer draw binding.",
                MessageType.Info);

            _activeManifest = (PsoWarmupManifest)EditorGUILayout.ObjectField(
                "Active PSO Manifest",
                _activeManifest,
                typeof(PsoWarmupManifest),
                false);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Harvest Project Materials & Build PSO Manifest", GUILayout.Height(30f)))
            {
                _activeManifest = SceneShaderVariantHarvester.HarvestProjectMaterialsToManifest();
                Selection.activeObject = _activeManifest;
            }

            if (GUILayout.Button("Add Async Warmup Orchestrator to Scene", GUILayout.Height(30f)))
            {
                AddOrchestratorToScene();
            }

            if (GUILayout.Button("Export Markdown & MCP JSON Report", GUILayout.Height(30f)))
            {
                ExportMarkdownAndJsonReport();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8f);
            if (_activeManifest != null)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("Manifest Summary", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"Last Harvest (UTC): {_activeManifest.lastHarvestTimestampUtc}");
                string fingerprint = _activeManifest.ComputeDriverAndManifestFingerprint(
                    SystemInfo.graphicsDeviceName,
                    SystemInfo.graphicsDeviceVersion,
                    SystemInfo.graphicsDeviceType.ToString());
                EditorGUILayout.LabelField(
                    $"Unique Shaders: {_activeManifest.uniqueShaderCount} | " +
                    $"Materials Bound: {_activeManifest.uniqueMaterialCount} | " +
                    $"Harvested PSO Variants: {_activeManifest.harvestedRecords.Count} | " +
                    $"Driver Fingerprint: {fingerprint}");

                _scroll = EditorGUILayout.BeginScrollView(_scroll);
                List<HarvestedVariantRecord> filtered = GetFilteredRecords();
                int previewCount = Mathf.Min(100, filtered.Count);
                for (int i = 0; i < previewCount; i++)
                {
                    var rec = filtered[i];
                    string kwText = (rec.keywords != null && rec.keywords.Length > 0)
                        ? string.Join(" ", rec.keywords)
                        : "<No Keywords>";
                    EditorGUILayout.LabelField(
                        $"[{rec.passType}] {rec.shaderName} — Keywords: {kwText}",
                        EditorStyles.miniLabel);
                }
                EditorGUILayout.EndScrollView();
                EditorGUILayout.EndVertical();
            }
        }

        private void ExportMarkdownAndJsonReport()
        {
            if (_activeManifest == null) return;
            string path = EditorUtility.SaveFilePanel("Export PSO Warmup Manifest Report", "", "PsoWarmupManifestReport.md", "md");
            if (!string.IsNullOrEmpty(path))
            {
                File.WriteAllText(path, _activeManifest.ExportManifestReportToMarkdown(), Encoding.UTF8);
                string jsonPath = Path.ChangeExtension(path, ".json");
                File.WriteAllText(jsonPath, PsoEliminatorMcpBridge.ExportManifestMcpReportToJson(_activeManifest), Encoding.UTF8);
                EditorUtility.RevealInFinder(path);
            }
        }

        private static void AddOrchestratorToScene()
        {
            var existing = FindFirstObjectByType<AsyncPsoWarmupOrchestrator>();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                return;
            }

            var go = new GameObject("AsyncPsoWarmupOrchestrator");
            Undo.RegisterCreatedObjectUndo(go, "Create AsyncPsoWarmupOrchestrator");
            go.AddComponent<AsyncPsoWarmupOrchestrator>();
            Selection.activeGameObject = go;
        }
    }
}
