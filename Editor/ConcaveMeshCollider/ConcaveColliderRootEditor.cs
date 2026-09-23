using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TelleR.ConcaveCollider;
using UnityEditor;
using UnityEngine;

namespace TelleR
{
    /// <summary>ConcaveColliderRoot 인스펙터: 생성 요약·측정값·에셋 링크, Rebuild/Remove/Select Pieces/Open Window.</summary>
    [CustomEditor(typeof(ConcaveColliderRoot))]
    [CanEditMultipleObjects]
    public sealed class ConcaveColliderRootEditor : Editor
    {
        private static readonly GUIContent RebuildContent = new GUIContent("Rebuild", "이 오브젝트에 기록된 설정으로 콜라이더를 다시 만듭니다(Undo 가능). 헐 메시 에셋은 같은 파일을 갱신합니다.");
        private static readonly GUIContent GenerateContent = new GUIContent("Generate", "마지막으로 쓴 설정(없으면 Auto)으로 콜라이더를 만듭니다.");
        private static readonly GUIContent RemoveContent = new GUIContent("Remove", "생성된 콜라이더와 이 컴포넌트를 지우고 끈 기존 콜라이더를 다시 켭니다(Undo 가능).");
        private static readonly GUIContent SelectPiecesContent = new GUIContent("Select Pieces", "생성된 콜라이더 오브젝트를 선택합니다.");
        private static readonly GUIContent OpenWindowContent = new GUIContent("Open Window", "Concave Mesh Collider 창을 이 오브젝트와 설정으로 엽니다.");
        private static readonly GUIContent EditSettingsContent = new GUIContent("Edit Settings…", "창에서 설정을 바꾼 뒤 Generate를 누르면 이 오브젝트에 다시 적용됩니다.");
        private static readonly GUIContent ContainerContent = new GUIContent("Container", "정적 조각을 담은 자식 오브젝트");
        private static readonly GUIContent HullAssetContent = new GUIContent("Hull Asset", "볼록 메시 조각의 메시를 담은 에셋(Hull_00..)");
        private static readonly GUIContent DisabledContent = new GUIContent("Disabled Colliders", "생성할 때 끈 기존 콜라이더. Remove를 누르면 다시 켜집니다.");
        private static readonly GUIContent SelectContent = new GUIContent("Select", "끈 기존 콜라이더 오브젝트를 선택합니다.");

        private static GUIStyle valueStyle;

        private string cachedJson;
        private string cachedSettingsSummary = string.Empty;
        private string cachedPolicySummary = string.Empty;
        private bool showWarnings;

        public override void OnInspectorGUI()
        {
            if (targets.Length > 1)
            {
                DrawMulti();
                return;
            }

            var root = (ConcaveColliderRoot)target;
            if (root == null) return;
            EditorGUILayout.LabelField("Concave Collider Root", TelleRGUI.Header);
            GUILayout.Label("Concave Mesh Collider가 만든 콜라이더를 관리합니다. 런타임 비용은 없습니다.", TelleRGUI.Hint);
            EditorGUILayout.Space(2f);

            if (!root.HasGenerated)
            {
                EditorGUILayout.HelpBox("아직 생성된 콜라이더가 없습니다. Generate를 누르면 이 오브젝트와 활성 자식의 메시로 만듭니다.", MessageType.Info);
            }
            else
            {
                DrawSummary(root);
            }

            DrawSettingsSummary(root);
            DrawButtons(root);
        }

        private void DrawSummary(ConcaveColliderRoot root)
        {
            ConcaveColliderMetrics m = root.Metrics;
            TelleRGUI.Section("Result");
            Row("Pieces", $"{m.pieceCount}  (Box {m.boxCount} · Sphere {m.sphereCount} · Capsule {m.capsuleCount} · Hull {m.hullCount})", TelleRGUI.StrongText);
            if (m.bonePieceCount > 0) Row("Bone Pieces", m.bonePieceCount.ToString(CultureInfo.InvariantCulture), TelleRGUI.StrongText);
            Row("Hull Vertices", m.totalHullVertices.ToString(CultureInfo.InvariantCulture), TelleRGUI.StrongText);
            Row("Coverage", ConcaveColliderGenerator.Percent(m.coverage) + (m.metricsApproximate && m.coverage >= 0f ? " (근사)" : string.Empty),
                ConcaveMeshColliderWindow.CoverageColor(m.coverage));
            Row("Outside", ConcaveColliderGenerator.Percent(m.outsideFraction), ConcaveMeshColliderWindow.OutsideColor(m.outsideFraction));
            Row("Source", $"{m.sourceMeshCount} meshes · {m.sourceTriangleCount.ToString("N0", CultureInfo.InvariantCulture)} tris", TelleRGUI.StrongText);
            Row("Generated", $"{m.generatedAt}  ({m.seconds.ToString("0.00", CultureInfo.InvariantCulture)}s)", TelleRGUI.HintText);

            using (new EditorGUI.DisabledScope(true))
            {
                if (root.Container != null) EditorGUILayout.ObjectField(ContainerContent, root.Container, typeof(GameObject), true);
                if (root.HullAsset != null && m.hullCount > 0) EditorGUILayout.ObjectField(HullAssetContent, root.HullAsset, typeof(Mesh), false);
            }

            if (root.HullAsset == null && !string.IsNullOrEmpty(root.HullAssetPath) && m.hullCount > 0)
                EditorGUILayout.HelpBox($"헐 메시 에셋을 찾을 수 없습니다: {root.HullAssetPath}\nRebuild로 다시 만드세요.", MessageType.Warning);

            int disabled = 0;
            IReadOnlyList<Collider> disabledColliders = root.DisabledColliders;
            for (int i = 0; i < disabledColliders.Count; i++)
            {
                if (disabledColliders[i] != null) disabled++;
            }

            if (disabled > 0)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel(DisabledContent);
                GUILayout.Label(disabled.ToString(CultureInfo.InvariantCulture));
                if (GUILayout.Button(SelectContent, EditorStyles.miniButton, GUILayout.Width(54f)))
                {
                    var objects = new List<Object>();
                    for (int i = 0; i < disabledColliders.Count; i++)
                    {
                        if (disabledColliders[i] != null) objects.Add(disabledColliders[i].gameObject);
                    }

                    Selection.objects = objects.ToArray();
                }

                EditorGUILayout.EndHorizontal();
            }

            if (m.warnings != null && m.warnings.Length > 0)
            {
                showWarnings = EditorGUILayout.Foldout(showWarnings, $"Warnings ({m.warnings.Length})", true);
                if (showWarnings)
                {
                    for (int i = 0; i < m.warnings.Length; i++) GUILayout.Label("• " + m.warnings[i], TelleRGUI.Hint);
                }
            }
        }

        private static void Row(string label, string value, Color color)
        {
            if (valueStyle == null) valueStyle = new GUIStyle(EditorStyles.label) { wordWrap = true };
            valueStyle.normal.textColor = color;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(EditorGUIUtility.labelWidth - 4f));
            GUILayout.Label(value, valueStyle);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSettingsSummary(ConcaveColliderRoot root)
        {
            TelleRGUI.Section("Settings");
            if (string.IsNullOrEmpty(root.SettingsJson))
            {
                GUILayout.Label("기록된 설정이 없습니다. Generate는 마지막으로 쓴 설정(없으면 Auto)을 씁니다.", TelleRGUI.Hint);
                return;
            }

            if (cachedJson != root.SettingsJson) BuildSettingsSummary(root);
            GUILayout.Label(cachedSettingsSummary, TelleRGUI.WrappedLabel);
            GUILayout.Label(cachedPolicySummary, TelleRGUI.Hint);
            if (GUILayout.Button(EditSettingsContent, EditorStyles.miniButton, GUILayout.Width(110f))) OpenWindow(root);
        }

        private void BuildSettingsSummary(ConcaveColliderRoot root)
        {
            cachedJson = root.SettingsJson;
            ConcaveColliderSettings s = ConcaveColliderPrefs.SettingsFromJson(cachedJson);
            var builder = new StringBuilder();
            if (s == null)
            {
                builder.Append("설정을 읽지 못했습니다(Rebuild는 마지막으로 쓴 설정을 씁니다).");
            }
            else if (s.auto)
            {
                builder.Append("Auto");
            }
            else
            {
                builder.Append("Manual · ").Append(s.quality).Append(" · Max Pieces ").Append(s.maxPieces)
                    .Append(" · Tolerance ").Append(s.concavityTolerance.ToString("0.###", CultureInfo.InvariantCulture))
                    .Append(" · Max Hull Verts ").Append(s.maxHullVertices);
                if (s.padding > 0f) builder.Append(" · Padding ").Append(s.padding.ToString("0.###", CultureInfo.InvariantCulture));
            }

            string layerName = root.Layer >= 0 ? LayerMask.LayerToName(root.Layer) : "Same as target";
            if (string.IsNullOrEmpty(layerName)) layerName = root.Layer.ToString(CultureInfo.InvariantCulture);
            builder.Append('\n').Append("Trigger ").Append(root.IsTrigger ? "On" : "Off")
                .Append(" · Material ").Append(root.PhysicsMaterial != null ? root.PhysicsMaterial.name : "None")
                .Append(" · Layer ").Append(layerName)
                .Append(" · Disable Existing ").Append(root.DisableExistingColliders ? "On" : "Off");
            cachedSettingsSummary = builder.ToString();
            cachedPolicySummary = s != null
                ? $"스킨드: 부위별 묶기 {(s.groupBonesByRole ? "켬" : "끔")}, 관절에서 자르기 {(s.clipAtJoints ? "켬" : "끔")}, 제외 본 키워드 '{s.excludedBoneKeywords}'"
                : string.Empty;
        }

        private void DrawButtons(ConcaveColliderRoot root)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginHorizontal();
            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = TelleRGUI.SuccessButton;
            bool generated = root.HasGenerated;
            if (GUILayout.Button(generated ? RebuildContent : GenerateContent, GUILayout.Height(24f)))
            {
                var roots = new List<ConcaveColliderRoot> { root };
                EditorApplication.delayCall += () => RunRebuild(roots);
            }

            GUI.backgroundColor = TelleRGUI.DangerButton;
            if (GUILayout.Button(RemoveContent, GUILayout.Height(24f)))
            {
                var gameObjects = new List<GameObject> { root.gameObject };
                EditorApplication.delayCall += () => ConcaveColliderGenerator.RemoveGenerated(gameObjects, ConcaveAssetRemoval.Ask);
            }

            GUI.backgroundColor = previous;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!generated))
            {
                if (GUILayout.Button(SelectPiecesContent, EditorStyles.miniButtonLeft)) SelectPieces(root);
            }

            if (GUILayout.Button(OpenWindowContent, EditorStyles.miniButtonRight)) OpenWindow(root);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawMulti()
        {
            EditorGUILayout.LabelField("Concave Collider Root", TelleRGUI.Header);
            int pieces = 0;
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] is ConcaveColliderRoot root && root != null) pieces += root.Metrics.pieceCount;
            }

            GUILayout.Label($"{targets.Length}개 오브젝트 · 조각 합계 {pieces}개. Rebuild는 오브젝트마다 기록된 설정을 씁니다.", TelleRGUI.Hint);
            EditorGUILayout.BeginHorizontal();
            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = TelleRGUI.SuccessButton;
            if (GUILayout.Button(RebuildContent, GUILayout.Height(24f)))
            {
                List<ConcaveColliderRoot> roots = SelectedRoots();
                EditorApplication.delayCall += () => RunRebuild(roots);
            }

            GUI.backgroundColor = TelleRGUI.DangerButton;
            if (GUILayout.Button(RemoveContent, GUILayout.Height(24f)))
            {
                List<GameObject> gameObjects = SelectedGameObjects();
                EditorApplication.delayCall += () => ConcaveColliderGenerator.RemoveGenerated(gameObjects, ConcaveAssetRemoval.Ask);
            }

            GUI.backgroundColor = previous;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(SelectPiecesContent, EditorStyles.miniButtonLeft))
            {
                var objects = new List<Object>();
                List<ConcaveColliderRoot> roots = SelectedRoots();
                for (int i = 0; i < roots.Count; i++) CollectPieces(roots[i], objects);
                if (objects.Count > 0) Selection.objects = objects.ToArray();
            }

            if (GUILayout.Button(OpenWindowContent, EditorStyles.miniButtonRight)) ConcaveMeshColliderWindow.Open(SelectedGameObjects(), null, null);
            EditorGUILayout.EndHorizontal();
        }

        private List<ConcaveColliderRoot> SelectedRoots()
        {
            var roots = new List<ConcaveColliderRoot>();
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] is ConcaveColliderRoot root && root != null) roots.Add(root);
            }

            return roots;
        }

        private List<GameObject> SelectedGameObjects()
        {
            var list = new List<GameObject>();
            List<ConcaveColliderRoot> roots = SelectedRoots();
            for (int i = 0; i < roots.Count; i++) list.Add(roots[i].gameObject);
            return list;
        }

        private static void RunRebuild(List<ConcaveColliderRoot> roots)
        {
            roots.RemoveAll(r => r == null);
            if (roots.Count == 0) return;
            ConcaveColliderMenus.ReportFailures(ConcaveColliderGenerator.Rebuild(roots));
        }

        private static void SelectPieces(ConcaveColliderRoot root)
        {
            var objects = new List<Object>();
            CollectPieces(root, objects);
            if (objects.Count > 0) Selection.objects = objects.ToArray();
        }

        private static void CollectPieces(ConcaveColliderRoot root, List<Object> objects)
        {
            IReadOnlyList<Collider> pieces = root.Pieces;
            for (int i = 0; i < pieces.Count; i++)
            {
                if (pieces[i] != null && !objects.Contains(pieces[i].gameObject)) objects.Add(pieces[i].gameObject);
            }
        }

        private static void OpenWindow(ConcaveColliderRoot root)
        {
            ConcaveMeshColliderWindow.Open(new List<GameObject> { root.gameObject },
                string.IsNullOrEmpty(root.SettingsJson) ? null : ConcaveColliderPrefs.SettingsFromRoot(root),
                string.IsNullOrEmpty(root.SettingsJson) ? null : ConcaveColliderPrefs.OptionsFromRoot(root));
        }
    }
}
