#if UNITY_EDITOR

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.Collections.Generic;

namespace TelleR.Tools.Editor
{
    [CustomEditor(typeof(MeshPivotTool))]
    public class MeshPivotToolEditor : UnityEditor.Editor
    {
        // 씬 뷰 3D 화면 위에 그리는 색 (인스펙터 스킨과 무관)
        private static readonly Color PositionColor = new Color(0.35f, 0.65f, 0.95f);
        private static readonly Color RotationColor = new Color(0.65f, 0.95f, 0.45f);
        private static readonly Color VertexSnapColor = new Color(0.95f, 0.75f, 0.25f);
        private static readonly Color VertexSnapColorFaded = new Color(0.95f, 0.75f, 0.25f, 0.5f);
        private static readonly Color PositionWireColor = new Color(0.35f, 0.65f, 0.95f, 0.6f);
        private static readonly Color RotationWireColor = new Color(0.65f, 0.95f, 0.45f, 0.6f);
        private static readonly Color OverlayBgNeutral = new Color(0f, 0f, 0f, 0.7f);
        private static readonly Color OverlayBgRotation = new Color(0.65f * 0.3f, 0.95f * 0.3f, 0.45f * 0.3f, 0.85f);
        private static readonly Color OverlayBgSnap = new Color(0.95f * 0.3f, 0.75f * 0.3f, 0.25f * 0.3f, 0.85f);
        private static readonly int SnapControlHash = "TelleRMeshPivotVertexSnap".GetHashCode();
        private const float SnapPickRadius = 40f;

        private enum HandleMode
        {
            Position,
            Rotation
        }

        private enum PendingAction
        {
            None,
            Apply,
            Revert
        }

        // 인스펙터 창이 여러 개여도 토글과 씬 핸들이 같은 모드를 쓰도록 에디터 간에 공유한다
        private static HandleMode currentMode = HandleMode.Position;
        // 씬 핸들을 그리는 에디터 목록(생성 순). 같은 대상의 에디터가 여러 개면 먼저 생긴 하나만 그린다.
        private static readonly List<MeshPivotToolEditor> SceneEditors = new List<MeshPivotToolEditor>();
        private Vector3 customEuler = Vector3.zero;
        private bool eulerFoldout;
        private PendingAction pendingAction;

        // 버텍스 스냅 캐시: 고유 위치 + 위치별 노말 묶음(카메라를 향하는 버텍스 우선 선택용)
        private Vector3[] snapPositions;
        private Vector3[] snapNormals;
        private int[] snapNormalStart;
        private int[] snapNormalCount;
        // 인스턴스 ID 대신 참조로 비교한다 (GetInstanceID는 Unity 6.6에서 컴파일 오류)
        private Mesh cachedMesh;
        private bool hoverValid;
        private bool hasHoverVertex;
        private Vector3 hoverVertexWorld;
        private bool lastCtrlHeld;

        // 핸들 드래그는 놓을 때 한 번만 메시에 적용한다 (드래그 중 Undo·메시 직렬화 폭증 방지)
        private bool dragActive;
        private Vector3 dragPivot;
        private Quaternion dragRotation;

        // 기본 Move/Rotate 기즈모 숨김 (여러 인스펙터가 동시에 열려도 원래 값으로 복원)
        private static int hideToolsRefCount;
        private static bool toolsHiddenBefore;
        private bool holdsToolsHidden;

        private static readonly GUIContent OverlayContent = new GUIContent();
        private static readonly GUIContent WorldAlignContent = new GUIContent("WORLD ALIGN", "피벗 축을 월드 축에 맞춥니다. 여러 번 눌러도 결과가 같습니다.");
        private static readonly GUIContent ApplyContent = new GUIContent("✓ Apply & Remove", "편집 결과를 유지하고 이 컴포넌트를 제거합니다. 메시를 씬에 둘지 .asset으로 저장할지 고릅니다.");
        private static readonly GUIContent RevertContent = new GUIContent("✕ Revert",
            "원본 메시와 편집 전 위치·회전·자식·콜라이더로 되돌리고 이 컴포넌트를 제거합니다. " +
            "작업 메시가 사라진 상태에서는 오브젝트가 편집 직후 위치·회전 그대로일 때만 위치·회전을 되돌립니다.");

        private const string LostMeshMessage =
            "작업 메시가 사라졌습니다(편집 중 프리팹 적용·저장 등으로 씬 전용 메시가 누락됨). " +
            "Revert를 누르면 원본 메시와 피벗 편집으로 바뀐 자식·콜라이더를 되돌린 뒤 이 컴포넌트를 제거합니다. " +
            "오브젝트 위치·회전은 편집 직후 그대로일 때만 되돌리고, 옮겨진 적 없는 오브젝트(프리팹 적용으로 편집 결과만 전달된 다른 인스턴스 등)는 그대로 둡니다.";
        private const string LostMeshPrefabMessage = LostMeshMessage +
            " 프리팹 에셋과 다른 인스턴스도 같은 상태라면 프리팹 모드에서 Revert해 한 번에 복구하세요.";

        private static class Styles
        {
            private static bool? builtForPro;
            public static GUIStyle Title, ToggleButton, SectionLabel, CubeButton, DirButton, ActionButton, Overlay;

            public static void Ensure()
            {
                if (builtForPro == EditorGUIUtility.isProSkin && Title != null) return;
                builtForPro = EditorGUIUtility.isProSkin;

                Title = new GUIStyle(TelleRGUI.Header) { alignment = TextAnchor.MiddleCenter };
                Title.normal.textColor = TelleRGUI.Accent;
                ToggleButton = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold, fixedHeight = 26 };
                SectionLabel = new GUIStyle(TelleRGUI.SubHeader) { alignment = TextAnchor.MiddleCenter };
                CubeButton = new GUIStyle(GUI.skin.button) { fontSize = 9, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                DirButton = new GUIStyle(GUI.skin.button) { fontSize = 10, fontStyle = FontStyle.Bold };
                ActionButton = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold, fixedHeight = 30 };
                Overlay = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter };
            }
        }

        private void OnEnable()
        {
            MeshPivotTool tool = target as MeshPivotTool;
            if (tool != null && !EditorUtility.IsPersistent(tool))
            {
                InitializeToolWithUndo(tool, "Begin Pivot Edit");
                EnsureUniqueWorkingMesh(tool);
            }
            InvalidateSnapCache();
            Undo.undoRedoPerformed += OnUndoRedo;
            Selection.selectionChanged += OnSelectionChanged;
            SceneEditors.Add(this);
            SceneView.duringSceneGui += OnSceneViewGUI;
            UpdateToolsHidden(tool);
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            Selection.selectionChanged -= OnSelectionChanged;
            SceneView.duringSceneGui -= OnSceneViewGUI;
            SceneEditors.Remove(this);
            SetToolsHidden(false);
            dragActive = false;
        }

        // 잠긴 인스펙터에 남은 에디터가 다른 오브젝트 선택 중에 기본 기즈모를 숨기지 않도록 즉시 갱신한다
        private void OnSelectionChanged()
        {
            dragActive = false;
            UpdateToolsHidden(target as MeshPivotTool);
        }

        private void OnUndoRedo()
        {
            // Undo/Redo로 메시가 바뀌었을 수 있으므로 캐시만 비운다.
            // (여기서 재초기화하면 새 Undo 기록이 생겨 Redo 스택이 사라진다)
            InvalidateSnapCache();
            dragActive = false;
            Repaint();
        }

        private void UpdateToolsHidden(MeshPivotTool tool)
        {
            SetToolsHidden(IsSceneEditTarget(tool) && tool.EditsTransform);
        }

        // 씬 핸들을 그릴 대상인지. 단일 대상 + 현재 선택에 포함 + 편집할 메시가 있을 때만 (잠긴 인스펙터·다중 선택 제외).
        // 드래그·스냅 상태는 에디터 하나에 하나뿐이므로 다중 대상 에디터는 핸들을 그리지 않는다.
        private bool IsSceneEditTarget(MeshPivotTool tool)
        {
            if (tool == null || Application.isPlaying || EditorUtility.IsPersistent(tool)) return false;
            if (targets.Length != 1) return false;
            if (!tool.HasSourceMesh || tool.IsWorkingMeshLost) return false;
            return Selection.Contains(tool.gameObject) || Selection.Contains(tool);
        }

        // 인스펙터 창이 여러 개면 같은 대상의 에디터도 여러 개다 — 핸들이 겹쳐 그려지지 않게 하나만 그린다
        private bool IsSceneOwner(MeshPivotTool tool)
        {
            foreach (MeshPivotToolEditor editor in SceneEditors)
            {
                if (editor == this) return true;
                if (editor != null && editor.target == tool && editor.IsSceneEditTarget(tool)) return false;
            }
            return false;
        }

        private void SetToolsHidden(bool hide)
        {
            if (hide == holdsToolsHidden) return;
            holdsToolsHidden = hide;
            if (hide)
            {
                if (hideToolsRefCount++ == 0) toolsHiddenBefore = UnityEditor.Tools.hidden;
                UnityEditor.Tools.hidden = true;
            }
            else if (hideToolsRefCount > 0 && --hideToolsRefCount == 0)
            {
                UnityEditor.Tools.hidden = toolsHiddenBefore;
            }
        }

        // 최초 sharedMesh 교체(원본 → 사본)를 Undo에 기록한다.
        // 미기록 시 컴포넌트 추가 직후 Ctrl+Z를 누르면 원본 에셋 링크가 유실된다.
        private static void InitializeToolWithUndo(MeshPivotTool tool, string undoName)
        {
            if (tool == null || !tool.NeedsInitialization || EditorUtility.IsPersistent(tool)) return;
            RecordRenderers(tool, undoName);
            Undo.RecordObject(tool, undoName);
            tool.EnsureInitialized();
            if (tool.WorkingMesh != null) Undo.RegisterCreatedObjectUndo(tool.WorkingMesh, undoName);
        }

        // Ctrl+D 복제본은 작업 메시(씬 전용 메시)를 원본과 공유한다 — 한쪽을 편집하면 둘 다 바뀌므로 사본으로 분리
        private static void EnsureUniqueWorkingMesh(MeshPivotTool tool)
        {
            Mesh working = tool.WorkingMesh;
            if (working == null) return;
            foreach (MeshPivotTool other in Resources.FindObjectsOfTypeAll<MeshPivotTool>())
            {
                if (other == null || other == tool || EditorUtility.IsPersistent(other)) continue;
                if (other.WorkingMesh != working) continue;

                const string undoName = "Unshare Pivot Mesh";
                Mesh clone = Instantiate(working);
                clone.name = working.name;
                Undo.RegisterCreatedObjectUndo(clone, undoName);
                RecordRenderers(tool, undoName);
                Undo.RecordObject(tool, undoName);
                tool.ReplaceWorkingMesh(clone);
                return;
            }
        }

        private static void RecordRenderers(MeshPivotTool tool, string undoName)
        {
            MeshFilter mf = tool.GetComponent<MeshFilter>();
            SkinnedMeshRenderer smr = tool.GetComponent<SkinnedMeshRenderer>();
            MeshCollider mc = tool.GetComponent<MeshCollider>();
            if (mf != null) Undo.RecordObject(mf, undoName);
            if (smr != null) Undo.RecordObject(smr, undoName);
            if (mc != null) Undo.RecordObject(mc, undoName);
        }

        // 피벗 편집이 건드리는 모든 객체를 기록한다 (transform·자식·콜라이더 center·메시)
        private static void RecordState(MeshPivotTool tool, string undoName, bool includeMesh)
        {
            RecordRenderers(tool, undoName);
            foreach (var col in tool.GetComponents<Collider>())
                Undo.RecordObject(col, undoName);
            if (includeMesh && tool.WorkingMesh != null) Undo.RecordObject(tool.WorkingMesh, undoName);
            Undo.RecordObject(tool, undoName);
            Transform t = tool.transform;
            Undo.RecordObject(t, undoName);
            for (int i = 0; i < t.childCount; i++)
                Undo.RecordObject(t.GetChild(i), undoName);
        }

        private static Mesh GetMeshFromTool(MeshPivotTool tool)
        {
            if (tool == null) return null;
            if (tool.UseSkinnedMesh)
            {
                SkinnedMeshRenderer smr = tool.GetComponent<SkinnedMeshRenderer>();
                return smr != null ? smr.sharedMesh : null;
            }
            MeshFilter mf = tool.GetComponent<MeshFilter>();
            return mf != null ? mf.sharedMesh : null;
        }

        private static bool IsPrefabRelated(MeshPivotTool tool)
        {
            GameObject go = tool.gameObject;
            return PrefabUtility.IsPartOfPrefabInstance(go) || PrefabStageUtility.GetPrefabStage(go) != null;
        }

        private static bool IsInPrefabStage(MeshPivotTool tool)
        {
            return PrefabStageUtility.GetPrefabStage(tool.gameObject) != null;
        }

        // 편집 중 프리팹 적용·저장 등으로 씬 전용 작업 메시가 사라졌지만 원본 메시는 남아 있는 상태 (Revert로만 복구 가능)
        private static bool IsWorkingMeshLost(MeshPivotTool tool)
        {
            return tool != null && tool.IsWorkingMeshLost;
        }

        public override void OnInspectorGUI()
        {
            MeshPivotTool tool = target as MeshPivotTool;
            if (tool == null) return;
            Styles.Ensure();
            serializedObject.Update();
            DrawHeader(tool);

            if (EditorUtility.IsPersistent(tool))
            {
                EditorGUILayout.HelpBox("프로젝트 창의 프리팹 에셋에서는 피벗을 편집할 수 없습니다. 프리팹을 열어(Prefab Mode) 편집하거나 씬에 배치한 뒤 편집하세요.", MessageType.Info);
                return;
            }

            // 편집 중 프리팹 적용·저장으로 씬 전용 작업 메시가 사라진 상태 — 원본으로 되돌리는 길만 남긴다.
            // 렌더러에 다른 메시를 넣었어도 같은 상태로 본다 (자동 재초기화가 그 메시를 덮어쓰고 M을 잃지 않도록)
            if (IsWorkingMeshLost(tool))
            {
                EditorGUILayout.HelpBox(
                    PrefabUtility.IsPartOfPrefabInstance(tool.gameObject) ? LostMeshPrefabMessage : LostMeshMessage,
                    MessageType.Warning);
                EditorGUILayout.Space(6);
                DrawActions(tool);
                ProcessPendingAction(tool);
                return;
            }

            if (!tool.HasSourceMesh)
            {
                EditorGUILayout.HelpBox("MeshFilter 또는 SkinnedMeshRenderer(메시 포함)가 필요합니다. 메시가 있는 오브젝트에 추가하세요.", MessageType.Warning);
                return;
            }

            DrawNotices(tool);
            if (tool.EditsTransform)
            {
                EditorGUILayout.Space(6);
                DrawModeToggle();
            }
            EditorGUILayout.Space(6);
            DrawSnapSlider(tool);
            EditorGUILayout.Space(6);
            DrawPivotPresets(tool);
            EditorGUILayout.Space(6);
            DrawRotationSection(tool);
            EditorGUILayout.Space(6);
            DrawActions(tool);
            serializedObject.ApplyModifiedProperties();

            // 컴포넌트 파괴·모달 다이얼로그는 레이아웃 그룹이 모두 닫힌 뒤에 실행한다
            ProcessPendingAction(tool);
        }

        private void DrawNotices(MeshPivotTool tool)
        {
            if (tool.NeedsInitialization)
            {
                EditorGUILayout.HelpBox("피벗 편집이 시작되지 않았습니다(Undo로 취소됨). 아래 버튼을 누르거나 피벗을 조작하면 편집을 다시 시작합니다.", MessageType.Info);
                if (GUILayout.Button("Begin Pivot Edit"))
                {
                    Undo.IncrementCurrentGroup();
                    int group = Undo.GetCurrentGroup();
                    Undo.SetCurrentGroupName("Begin Pivot Edit");
                    InitializeToolWithUndo(tool, "Begin Pivot Edit");
                    Undo.CollapseUndoOperations(group);
                    InvalidateSnapCache();
                }
            }

            if (!CanRevert(tool))
            {
                EditorGUILayout.HelpBox(
                    "원본 메시를 찾을 수 없어(에셋이 삭제되었거나 누락됨) Revert할 수 없습니다. " +
                    "편집 결과를 유지하려면 Apply & Remove를 누르세요.",
                    MessageType.Warning);
            }

            if (!tool.EditsTransform)
            {
                EditorGUILayout.HelpBox(
                    "본이 있는 스킨드 메시는 본이 렌더링을 결정하므로 씬에서 보이는 변화가 없습니다. " +
                    "메시 데이터의 원점·축과 bindpose만 함께 바뀌며, 메시를 내보내거나 다른 곳에서 재사용할 때 의미가 있습니다. " +
                    "씬 핸들과 버텍스 스냅은 지원하지 않고 프리셋·회전 버튼만 사용할 수 있습니다.",
                    MessageType.Info);
            }

            if (IsInPrefabStage(tool))
            {
                EditorGUILayout.HelpBox(
                    "프리팹 모드에서 편집 중입니다. 편집 중인 메시는 임시 메시라, 편집 도중 프리팹이 저장되면(자동 저장 포함) 프리팹에는 Missing 메시가 저장됩니다. " +
                    "Apply & Remove에서 '에셋으로 저장'으로 마치면 다음 저장 때 정상 메시로 바뀝니다.",
                    MessageType.Warning);
            }
            else if (IsPrefabRelated(tool))
            {
                EditorGUILayout.HelpBox(
                    "프리팹 인스턴스입니다. 편집 중인 메시는 씬에만 있는 임시 메시라, 편집 도중 Apply Overrides를 하면 프리팹과 다른 인스턴스의 메시가 Missing이 됩니다. " +
                    "Apply & Remove에서 '에셋으로 저장'을 고른 뒤에 적용하세요.",
                    MessageType.Warning);
            }
        }

        // 씬 핸들은 OnSceneGUI가 아니라 duringSceneGui에서 그린다.
        // Scene 뷰의 Gizmos를 끄면 Unity가 OnSceneGUI를 건너뛰지만 Tools.hidden은 그대로 적용되어
        // 기본 기즈모도 피벗 핸들도 없는 상태가 된다. duringSceneGui는 Gizmos 설정과 무관하게 호출된다.
        private void OnSceneViewGUI(SceneView view)
        {
            MeshPivotTool tool = target as MeshPivotTool;
            UpdateToolsHidden(tool);
            if (!IsSceneEditTarget(tool) || !IsSceneOwner(tool))
            {
                dragActive = false;
                return;
            }

            Styles.Ensure();
            Event e = Event.current;
            Transform t = tool.transform;
            bool editsTransform = tool.EditsTransform;
            int snapControlId = GUIUtility.GetControlID(SnapControlHash, FocusType.Passive);
            bool ctrlHeld = editsTransform && currentMode == HandleMode.Position && (e.control || e.command) && !e.alt;

            if (ctrlHeld != lastCtrlHeld)
            {
                lastCtrlHeld = ctrlHeld;
                hoverValid = false;
                dragActive = false;
                HandleUtility.Repaint();
            }

            // 바운드 와이어는 기즈모 성격이라 Gizmos 토글을 따른다 (핸들·안내는 기본 이동 도구처럼 항상 표시)
            if (view.drawGizmos) DrawBoundsWire(tool);

            if (editsTransform)
            {
                if (ctrlHeld)
                    HandleVertexSnap(tool, e, snapControlId);
                else if (currentMode == HandleMode.Position)
                    HandlePositionDrag(tool, t);
                else if (tool.CanRotatePivot)
                    HandleRotationDrag(tool, t);
            }

            DrawSceneOverlay(tool, ctrlHeld, e);
        }

        private void HandleVertexSnap(MeshPivotTool tool, Event e, int controlId)
        {
            // Ctrl을 누른 동안 씬 클릭이 선택 변경·사각형 선택으로 가지 않게 기본 컨트롤을 가져온다
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(controlId);

            if (!hoverValid || e.type == EventType.MouseMove || e.type == EventType.MouseDrag)
            {
                bool prevHas = hasHoverVertex;
                Vector3 prev = hoverVertexWorld;
                hasHoverVertex = TryGetNearestVertex(tool, e.mousePosition, out hoverVertexWorld);
                hoverValid = true;
                if (prevHas != hasHoverVertex || prev != hoverVertexWorld) HandleUtility.Repaint();
            }

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                Vector3 snapped;
                if (TryGetNearestVertex(tool, e.mousePosition, out snapped))
                {
                    Vector3 local = tool.transform.InverseTransformPoint(snapped);
                    PerformPivotChange(tool, "Vertex Snap Pivot", () => tool.SetPivotToLocalPoint(local));
                    hoverValid = false;
                }
                e.Use();
                return;
            }

            if (e.type == EventType.Repaint && hasHoverVertex)
            {
                float size = HandleUtility.GetHandleSize(hoverVertexWorld) * 0.1f;
                Handles.color = VertexSnapColor;
                Handles.SphereHandleCap(0, hoverVertexWorld, Quaternion.identity, size, EventType.Repaint);
                Camera cam = Camera.current;
                if (cam != null)
                {
                    Handles.color = VertexSnapColorFaded;
                    Handles.DrawWireDisc(hoverVertexWorld, cam.transform.forward, size * 1.5f);
                }
                Handles.color = Color.white;
            }
        }

        private void HandlePositionDrag(MeshPivotTool tool, Transform t)
        {
            Quaternion handleRot = UnityEditor.Tools.pivotRotation == PivotRotation.Local ? t.rotation : Quaternion.identity;
            Vector3 handlePos = dragActive ? dragPivot : t.position;

            EditorGUI.BeginChangeCheck();
            Vector3 newPos = Handles.PositionHandle(handlePos, handleRot);
            if (EditorGUI.EndChangeCheck())
            {
                float snap = tool.Snap;
                dragPivot = snap > 0f ? SnapWorld(newPos, snap) : newPos;
                dragActive = true;
            }

            if (!dragActive) return;

            if (GUIUtility.hotControl == 0)
            {
                // 드래그 종료: 한 번의 Undo 단계로 메시·transform에 반영
                dragActive = false;
                Vector3 target = dragPivot;
                if ((target - t.position).sqrMagnitude > 1e-12f)
                {
                    Vector3 local = t.InverseTransformPoint(target);
                    PerformPivotChange(tool, "Move Pivot", () => tool.SetPivotToLocalPoint(local));
                }
            }
            else if (Event.current.type == EventType.Repaint)
            {
                Handles.color = PositionColor;
                Handles.DrawDottedLine(t.position, dragPivot, 4f);
                Handles.color = Color.white;
            }
        }

        private void HandleRotationDrag(MeshPivotTool tool, Transform t)
        {
            Quaternion handleRot = dragActive ? dragRotation : t.rotation;

            EditorGUI.BeginChangeCheck();
            Quaternion newRot = Handles.RotationHandle(handleRot, t.position);
            if (EditorGUI.EndChangeCheck())
            {
                dragRotation = newRot;
                dragActive = true;
            }

            if (dragActive && GUIUtility.hotControl == 0)
            {
                dragActive = false;
                Quaternion deltaRot = Quaternion.Inverse(t.rotation) * dragRotation;
                if (Quaternion.Angle(Quaternion.identity, deltaRot) >= 0.001f)
                    PerformPivotChange(tool, "Rotate Pivot", () => tool.RotatePivotBy(deltaRot));
            }
        }

        private void DrawModeToggle()
        {
            Rect box = EditorGUILayout.BeginVertical();
            TelleRGUI.DrawBackground(box, TelleRGUI.PanelBg);
            GUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(10);
                Color oldBg = GUI.backgroundColor;
                GUI.backgroundColor = currentMode == HandleMode.Position ? TelleRGUI.AccentButton : oldBg;
                if (GUILayout.Button("⊕ Position", Styles.ToggleButton)) SetMode(HandleMode.Position);
                GUILayout.Space(4);
                GUI.backgroundColor = currentMode == HandleMode.Rotation ? TelleRGUI.SuccessButton : oldBg;
                if (GUILayout.Button("↻ Rotation", Styles.ToggleButton)) SetMode(HandleMode.Rotation);
                GUI.backgroundColor = oldBg;
                GUILayout.Space(10);
            }
            GUILayout.Space(4);
            GUILayout.Label("씬 뷰에서 핸들을 드래그해 피벗을 옮기거나 돌립니다. Ctrl+클릭은 버텍스 스냅입니다.", TelleRGUI.HintCentered);
            GUILayout.Space(6);
            EditorGUILayout.EndVertical();
        }

        private void SetMode(HandleMode mode)
        {
            if (currentMode == mode) return;
            currentMode = mode;
            dragActive = false;
            SceneView.RepaintAll();
        }

        private bool TryGetNearestVertex(MeshPivotTool tool, Vector2 mousePos, out Vector3 worldVertex)
        {
            worldVertex = Vector3.zero;
            EnsureSnapCache(GetMeshFromTool(tool));
            if (snapPositions == null || snapPositions.Length == 0) return false;

            Camera cam = Camera.current;
            if (cam == null) return false;
            Transform ct = cam.transform;
            Vector3 camPos = ct.position;
            Vector3 camFwd = ct.forward;
            bool ortho = cam.orthographic;

            Matrix4x4 l2w = tool.transform.localToWorldMatrix;
            Matrix4x4 normalMatrix = l2w.inverse.transpose;
            bool hasNormals = snapNormalStart != null;

            float bestFront = float.MaxValue, bestAny = float.MaxValue;
            Vector3 frontVertex = Vector3.zero, anyVertex = Vector3.zero;

            for (int i = 0; i < snapPositions.Length; i++)
            {
                Vector3 world = l2w.MultiplyPoint3x4(snapPositions[i]);
                Vector3 toVertex = world - camPos;
                if (!ortho && Vector3.Dot(toVertex, camFwd) <= 0f) continue; // 카메라 뒤
                float dist = Vector2.Distance(HandleUtility.WorldToGUIPoint(world), mousePos);
                if (dist >= SnapPickRadius) continue;

                if (dist < bestAny)
                {
                    bestAny = dist;
                    anyVertex = world;
                }

                // 같은 위치의 노말 중 하나라도 카메라를 향하면 앞면 버텍스로 본다 (뒷면 버텍스 오스냅 방지)
                bool front = !hasNormals;
                if (hasNormals)
                {
                    Vector3 view = ortho ? -camFwd : -toVertex;
                    int start = snapNormalStart[i], end = start + snapNormalCount[i];
                    for (int k = start; k < end; k++)
                    {
                        if (Vector3.Dot(normalMatrix.MultiplyVector(snapNormals[k]), view) > 0f)
                        {
                            front = true;
                            break;
                        }
                    }
                }
                if (front && dist < bestFront)
                {
                    bestFront = dist;
                    frontVertex = world;
                }
            }

            if (bestFront < float.MaxValue)
            {
                worldVertex = frontVertex;
                return true;
            }
            if (bestAny < float.MaxValue)
            {
                worldVertex = anyVertex;
                return true;
            }
            return false;
        }

        private void InvalidateSnapCache()
        {
            cachedMesh = null;
            snapPositions = null;
            hoverValid = false;
        }

        private void EnsureSnapCache(Mesh mesh)
        {
            if (mesh == null)
            {
                snapPositions = null;
                cachedMesh = null;
                return;
            }
            if (ReferenceEquals(cachedMesh, mesh) && snapPositions != null) return;

            Vector3[] verts = mesh.vertices;
            Vector3[] normals = mesh.normals;
            bool hasNormals = normals != null && normals.Length == verts.Length;

            var indexOf = new Dictionary<Vector3, int>(verts.Length);
            var positions = new List<Vector3>(verts.Length);
            var groupOf = new int[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                int g;
                if (!indexOf.TryGetValue(verts[i], out g))
                {
                    g = positions.Count;
                    indexOf.Add(verts[i], g);
                    positions.Add(verts[i]);
                }
                groupOf[i] = g;
            }
            snapPositions = positions.ToArray();

            if (hasNormals)
            {
                int groups = snapPositions.Length;
                snapNormalCount = new int[groups];
                snapNormalStart = new int[groups];
                for (int i = 0; i < verts.Length; i++) snapNormalCount[groupOf[i]]++;
                int acc = 0;
                for (int g = 0; g < groups; g++)
                {
                    snapNormalStart[g] = acc;
                    acc += snapNormalCount[g];
                }
                snapNormals = new Vector3[verts.Length];
                var fill = new int[groups];
                for (int i = 0; i < verts.Length; i++)
                {
                    int g = groupOf[i];
                    snapNormals[snapNormalStart[g] + fill[g]++] = normals[i];
                }
            }
            else
            {
                snapNormals = null;
                snapNormalStart = null;
                snapNormalCount = null;
            }
            cachedMesh = mesh;
        }

        private void DrawHeader(MeshPivotTool tool)
        {
            Rect rect = GUILayoutUtility.GetRect(1f, 32f);
            TelleRGUI.DrawBackground(rect, TelleRGUI.HeaderBg);
            EditorGUI.LabelField(rect, tool.UseSkinnedMesh ? "◈ PIVOT EDITOR (SKINNED)" : "◈ PIVOT EDITOR (MESH)", Styles.Title);
        }

        private void DrawSnapSlider(MeshPivotTool tool)
        {
            Rect box = EditorGUILayout.BeginVertical();
            TelleRGUI.DrawBackground(box, TelleRGUI.PanelBg);
            GUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(10);
                GUILayout.Label("Snap", EditorStyles.boldLabel, GUILayout.Width(36));
                EditorGUI.BeginChangeCheck();
                float newSnap = GUILayout.HorizontalSlider(tool.Snap, 0f, 1f, GUILayout.ExpandWidth(true));
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(tool, "Change Snap");
                    tool.Snap = newSnap;
                    EditorUtility.SetDirty(tool);
                }
                string label = tool.Snap > 0.001f ? tool.Snap.ToString("0.##") : "Off";
                GUILayout.Label(label, GUILayout.Width(32));
                GUILayout.Space(10);
            }
            GUILayout.Space(8);
            EditorGUILayout.EndVertical();
        }

        private void DrawPivotPresets(MeshPivotTool tool)
        {
            Rect box = EditorGUILayout.BeginVertical();
            TelleRGUI.DrawBackground(box, TelleRGUI.PanelBg);
            GUILayout.Space(10);
            EditorGUILayout.LabelField("Pivot Position", Styles.SectionLabel);
            GUILayout.Space(8);
            const float btnSize = 44f;
            const float smallBtn = 38f;
            const float spacing = 4f;
            GUIStyle cubeBtn = Styles.CubeButton;

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("TOP\n▲", cubeBtn, GUILayout.Width(btnSize), GUILayout.Height(smallBtn)))
                    ApplyPreset(tool, PivotPreset.Top);
                GUILayout.FlexibleSpace();
            }
            GUILayout.Space(spacing);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("BACK\n◆", cubeBtn, GUILayout.Width(btnSize), GUILayout.Height(smallBtn)))
                    ApplyPreset(tool, PivotPreset.Back);
                GUILayout.FlexibleSpace();
            }
            GUILayout.Space(spacing);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("◀\nLEFT", cubeBtn, GUILayout.Width(btnSize), GUILayout.Height(btnSize)))
                    ApplyPreset(tool, PivotPreset.Left);
                GUILayout.Space(spacing);
                Color oldBg = GUI.backgroundColor;
                GUI.backgroundColor = TelleRGUI.AccentButton;
                if (GUILayout.Button("●\nCENTER", cubeBtn, GUILayout.Width(btnSize + 8), GUILayout.Height(btnSize)))
                    ApplyPreset(tool, PivotPreset.Center);
                GUI.backgroundColor = oldBg;
                GUILayout.Space(spacing);
                if (GUILayout.Button("▶\nRIGHT", cubeBtn, GUILayout.Width(btnSize), GUILayout.Height(btnSize)))
                    ApplyPreset(tool, PivotPreset.Right);
                GUILayout.FlexibleSpace();
            }
            GUILayout.Space(spacing);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("FRONT\n◇", cubeBtn, GUILayout.Width(btnSize), GUILayout.Height(smallBtn)))
                    ApplyPreset(tool, PivotPreset.Front);
                GUILayout.FlexibleSpace();
            }
            GUILayout.Space(spacing);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("▼\nBOTTOM", cubeBtn, GUILayout.Width(btnSize), GUILayout.Height(smallBtn)))
                    ApplyPreset(tool, PivotPreset.Bottom);
                GUILayout.FlexibleSpace();
            }
            GUILayout.Space(10);
            EditorGUILayout.EndVertical();
        }

        private void DrawRotationSection(MeshPivotTool tool)
        {
            Rect box = EditorGUILayout.BeginVertical();
            TelleRGUI.DrawBackground(box, TelleRGUI.PanelBg);
            GUILayout.Space(10);
            EditorGUILayout.LabelField("Pivot Rotation", Styles.SectionLabel);
            GUILayout.Space(8);

            bool canRotate = tool.CanRotatePivot;
            if (!canRotate)
            {
                EditorGUILayout.HelpBox(
                    "Scale이 비균등(x·y·z 값이 다름)하면 피벗 회전 시 메시가 찌그러지므로 회전을 막았습니다. " +
                    "Scale을 균등하게 맞추거나 위치만 편집하세요.",
                    MessageType.Warning);
            }

            GUIStyle dirBtn = Styles.DirButton;
            using (new EditorGUI.DisabledScope(!canRotate))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(10);
                    Color oldBg = GUI.backgroundColor;
                    GUI.backgroundColor = TelleRGUI.SuccessButton;
                    if (GUILayout.Button(WorldAlignContent, dirBtn, GUILayout.Height(28)))
                        PerformPivotChange(tool, "Align to World", tool.AlignToWorld);
                    GUI.backgroundColor = oldBg;
                    GUILayout.Space(10);
                }
                GUILayout.Space(8);
                EditorGUILayout.LabelField("Forward Direction:", EditorStyles.miniBoldLabel);
                GUILayout.Space(4);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(10);
                    if (GUILayout.Button("+X", dirBtn, GUILayout.Width(40), GUILayout.Height(24)))
                        ApplyForwardPreset(tool, Vector3.right);
                    if (GUILayout.Button("-X", dirBtn, GUILayout.Width(40), GUILayout.Height(24)))
                        ApplyForwardPreset(tool, Vector3.left);
                    GUILayout.Space(8);
                    if (GUILayout.Button("+Y", dirBtn, GUILayout.Width(40), GUILayout.Height(24)))
                        ApplyForwardPreset(tool, Vector3.up);
                    if (GUILayout.Button("-Y", dirBtn, GUILayout.Width(40), GUILayout.Height(24)))
                        ApplyForwardPreset(tool, Vector3.down);
                    GUILayout.Space(8);
                    if (GUILayout.Button("+Z", dirBtn, GUILayout.Width(40), GUILayout.Height(24)))
                        ApplyForwardPreset(tool, Vector3.forward);
                    if (GUILayout.Button("-Z", dirBtn, GUILayout.Width(40), GUILayout.Height(24)))
                        ApplyForwardPreset(tool, Vector3.back);
                    GUILayout.FlexibleSpace();
                }
                GUILayout.Space(10);
                eulerFoldout = EditorGUILayout.Foldout(eulerFoldout, "Custom Euler Angles", true);
                if (eulerFoldout)
                {
                    GUILayout.Space(4);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Space(10);
                        customEuler = EditorGUILayout.Vector3Field(GUIContent.none, customEuler);
                        if (GUILayout.Button("Apply", GUILayout.Width(50)))
                        {
                            Quaternion targetRot = Quaternion.Euler(customEuler);
                            PerformPivotChange(tool, "Set Custom Rotation", () => tool.SetPivotRotation(targetRot));
                        }
                        GUILayout.Space(10);
                    }
                }
            }
            GUILayout.Space(10);
            EditorGUILayout.EndVertical();
        }

        private void ApplyForwardPreset(MeshPivotTool tool, Vector3 direction)
        {
            PerformPivotChange(tool, "Pivot Forward", () => tool.SetForwardDirection(direction));
        }

        private void DrawActions(MeshPivotTool tool)
        {
            Rect box = EditorGUILayout.BeginVertical();
            TelleRGUI.DrawBackground(box, TelleRGUI.PanelBg);
            GUILayout.Space(10);
            // 작업 메시가 사라진 상태에서도 Revert는 원본 메시·자세를 복구할 수 있으므로 켜 둔다
            bool revertEnabled = tool.HasWorkingMesh ? CanRevert(tool) : IsWorkingMeshLost(tool);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(10);
                Color oldBg = GUI.backgroundColor;
                GUI.backgroundColor = TelleRGUI.AccentButton;
                using (new EditorGUI.DisabledScope(!tool.HasWorkingMesh))
                {
                    if (GUILayout.Button(ApplyContent, Styles.ActionButton))
                        pendingAction = PendingAction.Apply;
                }
                GUILayout.Space(6);
                GUI.backgroundColor = TelleRGUI.DangerButton;
                using (new EditorGUI.DisabledScope(!revertEnabled))
                {
                    if (GUILayout.Button(RevertContent, Styles.ActionButton))
                        pendingAction = PendingAction.Revert;
                }
                GUI.backgroundColor = oldBg;
                GUILayout.Space(10);
            }
            GUILayout.Space(10);
            EditorGUILayout.EndVertical();
        }

        private void ProcessPendingAction(MeshPivotTool tool)
        {
            if (pendingAction == PendingAction.None) return;
            PendingAction action = pendingAction;
            pendingAction = PendingAction.None;

            if (action == PendingAction.Revert)
            {
                RevertAndRemove(tool);
            }
            else
            {
                string assetPath;
                if (!AskApplyTarget(tool, out assetPath)) GUIUtility.ExitGUI();
                ApplyAndRemoveCore(tool, assetPath);
            }
            SceneView.RepaintAll();
            GUIUtility.ExitGUI();
        }

        // 적용 방식 선택. false = 취소. assetPath가 null이면 씬에만 저장.
        private static bool AskApplyTarget(MeshPivotTool tool, out string assetPath)
        {
            assetPath = null;
            const string saveAsset = "에셋으로 저장";
            const string keepScene = "씬에만 저장";
            if (IsInPrefabStage(tool))
            {
                // 프리팹 모드의 씬 전용 메시는 프리팹에 저장될 수 없고 모드를 닫으면 사라진다 — 에셋 저장만 허용
                if (!TelleRGUI.Confirm("피벗 적용",
                        "프리팹 모드에서는 편집된 메시를 .asset 파일로 저장해야 프리팹에 남습니다.\n\n" +
                        "씬에만 저장하는 방식은 프리팹 모드에서 쓸 수 없습니다. 저장할 위치를 고른 뒤 적용합니다.",
                        saveAsset, "취소"))
                    return false;
            }
            else
            {
                bool prefab = IsPrefabRelated(tool);
                string message = prefab
                    ? "프리팹에 속한 오브젝트입니다.\n\n편집된 메시를 .asset 파일로 저장해야 프리팹 적용·저장 후에도 메시가 유지됩니다. " +
                      "씬에만 저장하면 프리팹 쪽에서는 메시가 Missing이 됩니다."
                    : "편집된 메시를 어디에 저장할까요?\n\n· 씬에만 저장: 이 씬 파일 안에만 저장됩니다. 다른 씬·프리팹에서는 쓸 수 없습니다.\n" +
                      "· 에셋으로 저장: .asset 파일로 저장해 프리팹·다른 씬에서도 쓸 수 있습니다.";
                int choice = EditorUtility.DisplayDialogComplex("피벗 적용", message,
                    prefab ? saveAsset : keepScene, "취소", prefab ? keepScene : saveAsset);
                if (choice == 1) return false;

                bool wantAsset = prefab ? choice == 0 : choice == 2;
                if (!wantAsset) return true;
            }

            Mesh working = tool.WorkingMesh;
            string defaultName = working != null ? MeshFBXBackupUtility.SanitizeFileName(working.name) : "PivotEditedMesh";
            string path = EditorUtility.SaveFilePanelInProject("메시 에셋 저장", defaultName, "asset",
                "편집된 메시를 저장할 위치를 고르세요.", GetDefaultSaveFolder(tool));
            if (string.IsNullOrEmpty(path)) return false;

            if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null &&
                !TelleRGUI.Confirm("메시 에셋 덮어쓰기",
                    $"'{path}'의 기존 메시 데이터를 편집된 메시로 덮어씁니다.\n이 메시를 쓰는 다른 오브젝트·프리팹도 함께 바뀝니다.",
                    "덮어쓰기", "취소"))
                return false;

            assetPath = path;
            return true;
        }

        private static string GetDefaultSaveFolder(MeshPivotTool tool)
        {
            string meshPath = tool.OriginalMesh != null ? AssetDatabase.GetAssetPath(tool.OriginalMesh) : null;
            if (!string.IsNullOrEmpty(meshPath) && meshPath.StartsWith("Assets/"))
                return System.IO.Path.GetDirectoryName(meshPath).Replace('\\', '/');

            PrefabStage stage = PrefabStageUtility.GetPrefabStage(tool.gameObject);
            if (stage != null && !string.IsNullOrEmpty(stage.assetPath) && stage.assetPath.StartsWith("Assets/"))
                return System.IO.Path.GetDirectoryName(stage.assetPath).Replace('\\', '/');

            return "Assets";
        }

        private enum PivotPreset
        {
            Center,
            Top,
            Bottom,
            Left,
            Right,
            Front,
            Back
        }

        private void ApplyPreset(MeshPivotTool tool, PivotPreset preset)
        {
            PerformPivotChange(tool, $"Pivot {preset}", () =>
            {
                Bounds b = tool.GetCurrentLocalBounds();
                Vector3 point = b.center;
                switch (preset)
                {
                    case PivotPreset.Top: point = new Vector3(b.center.x, b.max.y, b.center.z); break;
                    case PivotPreset.Bottom: point = new Vector3(b.center.x, b.min.y, b.center.z); break;
                    case PivotPreset.Left: point = new Vector3(b.min.x, b.center.y, b.center.z); break;
                    case PivotPreset.Right: point = new Vector3(b.max.x, b.center.y, b.center.z); break;
                    case PivotPreset.Front: point = new Vector3(b.center.x, b.center.y, b.max.z); break;
                    case PivotPreset.Back: point = new Vector3(b.center.x, b.center.y, b.min.z); break;
                }
                tool.SetPivotToLocalPoint(point);
            });
        }

        // 편집 1회 = Undo 1단계. 편집이 Undo로 취소된 상태면 같은 단계 안에서 다시 시작한다.
        private void PerformPivotChange(MeshPivotTool tool, string undoName, System.Action change)
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(undoName);
            InitializeToolWithUndo(tool, undoName);
            RecordState(tool, undoName, true);
            change();
            MarkDirty(tool);
            Undo.CollapseUndoOperations(group);
        }

        private static void ApplyAndRemoveCore(MeshPivotTool tool, string assetPath)
        {
            if (tool == null) return;
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            const string undoName = "Apply Pivot";
            Undo.SetCurrentGroupName(undoName);

            Mesh working = tool.WorkingMesh;
            if (!string.IsNullOrEmpty(assetPath) && working != null)
            {
                Mesh saved = MeshFBXBackupUtility.SaveMeshAsAsset(working, assetPath);
                if (saved == null)
                {
                    Undo.CollapseUndoOperations(group);
                    return;
                }

                MeshFilter mf = tool.GetComponent<MeshFilter>();
                SkinnedMeshRenderer smr = tool.GetComponent<SkinnedMeshRenderer>();
                MeshCollider mc = tool.GetComponent<MeshCollider>();
                if (mf != null && mf.sharedMesh == working)
                {
                    Undo.RecordObject(mf, undoName);
                    mf.sharedMesh = saved;
                    EditorUtility.SetDirty(mf);
                }
                if (smr != null && smr.sharedMesh == working)
                {
                    Undo.RecordObject(smr, undoName);
                    smr.sharedMesh = saved;
                    EditorUtility.SetDirty(smr);
                }
                if (mc != null && mc.sharedMesh == working)
                {
                    Undo.RecordObject(mc, undoName);
                    mc.sharedMesh = saved;
                    EditorUtility.SetDirty(mc);
                }
                Debug.Log($"[TelleR/MeshPivotTool] 편집된 메시를 에셋으로 저장했습니다: {assetPath}", saved);
            }

            tool.MarkPendingRemoval();
            Undo.DestroyObjectImmediate(tool);
            Undo.CollapseUndoOperations(group);
        }

        // 작업 메시가 유일한 형상인데 원본이 사라졌으면 되돌릴 대상이 없다
        private static bool CanRevert(MeshPivotTool tool)
        {
            return !tool.HasWorkingMesh || tool.OriginalMesh != null;
        }

        private static void RevertAndRemove(MeshPivotTool tool)
        {
            if (tool == null) return;
            if (!CanRevert(tool))
            {
                Debug.LogWarning("[TelleR/MeshPivotTool] 원본 메시를 찾을 수 없어(삭제되었거나 누락됨) 되돌리지 않았습니다. " +
                                 "편집 결과를 유지하려면 Apply & Remove를 사용하세요.", tool);
                return;
            }
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            const string undoName = "Revert Pivot";
            Undo.SetCurrentGroupName(undoName);

            RecordState(tool, undoName, false);
            tool.MarkPendingRemoval();
            // workingMesh는 Undo 스택에 기록되어 있으므로 plain DestroyImmediate가 아니라 Undo로 파괴한다
            Mesh detached = tool.DetachWorkingMesh();
            MeshFilter mf = tool.GetComponent<MeshFilter>();
            SkinnedMeshRenderer smr = tool.GetComponent<SkinnedMeshRenderer>();
            if (mf != null) EditorUtility.SetDirty(mf);
            if (smr != null) EditorUtility.SetDirty(smr);
            if (detached != null) Undo.DestroyObjectImmediate(detached);
            Undo.DestroyObjectImmediate(tool);
            // 파괴까지 한 단계로 묶어 Ctrl+Z 한 번에 편집 상태 전체가 돌아오게 한다
            Undo.CollapseUndoOperations(group);
        }

        private void MarkDirty(MeshPivotTool tool)
        {
            Mesh mesh = GetMeshFromTool(tool);
            MeshFilter mf = tool.GetComponent<MeshFilter>();
            SkinnedMeshRenderer smr = tool.GetComponent<SkinnedMeshRenderer>();
            EditorUtility.SetDirty(tool);
            if (mf != null) EditorUtility.SetDirty(mf);
            if (smr != null) EditorUtility.SetDirty(smr);
            if (mesh != null) EditorUtility.SetDirty(mesh);
            InvalidateSnapCache(); // 정점이 이동했으므로 버텍스 스냅 캐시 무효화
            SceneView.RepaintAll();
        }

        private void DrawBoundsWire(MeshPivotTool tool)
        {
            if (Event.current.type != EventType.Repaint) return;
            Bounds b = tool.GetRenderedBounds();
            Matrix4x4 matrix = tool.transform.localToWorldMatrix;
            if (tool.UseSkinnedMesh)
            {
                // SkinnedMeshRenderer.localBounds는 rootBone 기준이다
                SkinnedMeshRenderer smr = tool.GetComponent<SkinnedMeshRenderer>();
                if (smr != null && smr.rootBone != null) matrix = smr.rootBone.localToWorldMatrix;
            }
            Color wire = currentMode == HandleMode.Rotation && tool.EditsTransform ? RotationWireColor : PositionWireColor;
            using (new Handles.DrawingScope(wire, matrix))
            {
                Handles.DrawWireCube(b.center, b.size);
            }
        }

        private void DrawSceneOverlay(MeshPivotTool tool, bool ctrlHeld, Event e)
        {
            if (e.type != EventType.Repaint) return;

            Color bgColor;
            Color textColor;
            if (!tool.EditsTransform)
            {
                OverlayContent.text = "◈ Skinned · 씬 핸들 없음 (인스펙터 버튼 사용)";
                bgColor = OverlayBgNeutral;
                textColor = Color.white;
            }
            else if (currentMode == HandleMode.Rotation)
            {
                OverlayContent.text = tool.CanRotatePivot
                    ? "◈ Rotation · 링을 드래그해 피벗 축 회전"
                    : "◈ Rotation · 비균등 스케일이라 회전 불가";
                bgColor = OverlayBgRotation;
                textColor = RotationColor;
            }
            else if (ctrlHeld)
            {
                OverlayContent.text = "◈ Vertex Snap · 클릭한 버텍스로 피벗 이동";
                bgColor = OverlayBgSnap;
                textColor = VertexSnapColor;
            }
            else
            {
                OverlayContent.text = "◈ Position · Ctrl+클릭 = 버텍스 스냅";
                bgColor = OverlayBgNeutral;
                textColor = Color.white;
            }

            Handles.BeginGUI();
            Rect r = new Rect(10, 10, 320, 28);
            EditorGUI.DrawRect(r, bgColor);
            Styles.Overlay.normal.textColor = textColor;
            Styles.Overlay.Draw(r, OverlayContent, false, false, false, false);
            Handles.EndGUI();
        }

        private static Vector3 SnapWorld(Vector3 world, float snap)
        {
            return new Vector3(
                Mathf.Round(world.x / snap) * snap,
                Mathf.Round(world.y / snap) * snap,
                Mathf.Round(world.z / snap) * snap);
        }

        [MenuItem("CONTEXT/MeshFilter/Edit Mesh Pivot")]
        private static void AddToolFromMeshFilter(MenuCommand command)
        {
            MeshFilter mf = command.context as MeshFilter;
            if (mf == null) return;
            AddToolWithUndo(mf.gameObject);
        }

        [MenuItem("CONTEXT/SkinnedMeshRenderer/Edit Mesh Pivot")]
        private static void AddToolFromSkinnedMesh(MenuCommand command)
        {
            SkinnedMeshRenderer smr = command.context as SkinnedMeshRenderer;
            if (smr == null) return;
            AddToolWithUndo(smr.gameObject);
        }

        private static void AddToolWithUndo(GameObject go)
        {
            MeshPivotTool existing = go.GetComponent<MeshPivotTool>();
            if (existing != null)
            {
                Selection.activeObject = existing;
                return;
            }
            if (EditorUtility.IsPersistent(go))
            {
                TelleRGUI.Info("피벗 편집 불가", "프로젝트 창의 프리팹 에셋에서는 피벗을 편집할 수 없습니다.\n프리팹을 열어(Prefab Mode) 편집하거나 씬에 배치한 뒤 편집하세요.");
                return;
            }

            // 컴포넌트 추가 + 작업 메시 교체를 한 Undo 단계로 묶는다
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            const string undoName = "Edit Mesh Pivot";
            Undo.SetCurrentGroupName(undoName);
            MeshPivotTool tool = Undo.AddComponent<MeshPivotTool>(go);
            InitializeToolWithUndo(tool, undoName);
            Undo.CollapseUndoOperations(group);
        }
    }
}
#endif
