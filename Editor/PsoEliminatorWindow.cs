using UnityEditor;
using UnityEngine;

namespace YusufOkuducu.PcPsoEliminator.Editor
{
    public sealed class PsoEliminatorWindow : EditorWindow
    {
        private PsoWarmupManifest _activeManifest;
        private Vector2 _scroll;

        [MenuItem("Window/Analysis/PC DX12 & Vulkan PSO Stutter Eliminator")]
        public static void ShowWindow()
        {
            var win = GetWindow<PsoEliminatorWindow>("PC PSO Eliminator");
            win.minSize = new Vector2(620f, 440f);
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

        private void OnGUI()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("PC DX12 / Vulkan PSO Stutter Eliminator & Async Prewarmer", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Harvests active Material + Shader + PassType + Keyword combinations across your project into a lean ShaderVariantCollection & PsoWarmupManifest, and orchestrates frame-budgeted progressive PSO compilation + offscreen CommandBuffer draw binding.",
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
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8f);
            if (_activeManifest != null)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("Manifest Summary", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"Last Harvest (UTC): {_activeManifest.lastHarvestTimestampUtc}");
                EditorGUILayout.LabelField(
                    $"Unique Shaders: {_activeManifest.uniqueShaderCount} | " +
                    $"Materials Bound: {_activeManifest.uniqueMaterialCount} | " +
                    $"Harvested PSO Variants: {_activeManifest.harvestedRecords.Count}");

                _scroll = EditorGUILayout.BeginScrollView(_scroll);
                int previewCount = Mathf.Min(100, _activeManifest.harvestedRecords.Count);
                for (int i = 0; i < previewCount; i++)
                {
                    var rec = _activeManifest.harvestedRecords[i];
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

        private void AddOrchestratorToScene()
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
