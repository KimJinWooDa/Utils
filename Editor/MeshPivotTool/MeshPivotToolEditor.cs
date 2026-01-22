#if UNITY_EDITOR

using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace TelleR.Tools.Editor
{
    [CustomEditor(typeof(MeshPivotTool))]
    public class MeshPivotToolEditor : UnityEditor.Editor
    {
        private static readonly Color AccentColor = new Color(0.35f, 0.65f, 0.95f);
        private static readonly Color RotationColor = new Color(0.65f, 0.95f, 0.45f);
        private static readonly Color DangerColor = new Color(0.95f, 0.45f, 0.45f);
        private static readonly Color VertexSnapColor = new Color(0.95f, 0.75f, 0.25f);
        private static readonly Color HeaderBg = new Color(0.18f, 0.18f, 0.18f);
        private static readonly Color BoxBg = new Color(0.22f, 0.22f, 0.22f);
        private Vector3[] cachedVertices;
        private int cachedMeshId;

        private enum HandleMode
        {
            Position,
            Rotation
        }

        private HandleMode currentMode = HandleMode.Position;
        private Vector3 customEuler = Vector3.zero;
        private bool eulerFoldout;

        private void OnEnable()
        {
            MeshPivotTool tool = target as MeshPivotTool;
            if (tool != null)
            {
                tool.EnsureInitialized();
            }
            CacheVertices(tool);
        }

        private Mesh GetMeshFromTool(MeshPivotTool tool)
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

        private void CacheVertices(MeshPivotTool tool)
        {
            if (tool == null) return;
            Mesh mesh = GetMeshFromTool(tool);
            if (mesh == null)
            {
                cachedVertices = null;
                cachedMeshId = 0;
                return;
            }
            if (cachedMeshId == mesh.GetInstanceID() && cachedVertices != null) return;
            HashSet<Vector3> uniqueSet = new HashSet<Vector3>(mesh.vertices);
            cachedVertices = new Vector3[uniqueSet.Count];
            uniqueSet.CopyTo(cachedVertices);
            cachedMeshId = mesh.GetInstanceID();
        }

        public override void OnInspectorGUI()
        {
            MeshPivotTool tool = target as MeshPivotTool;
            if (tool == null) return;
            serializedObject.Update();
            DrawHeader(tool);
            EditorGUILayout.Space(6);
            DrawModeToggle();
            EditorGUILayout.Space(6);
            DrawSnapSlider(tool);
            EditorGUILayout.Space(6);
            DrawPivotPresets(tool);
            EditorGUILayout.Space(6);
            DrawRotationSection(tool);
            EditorGUILayout.Space(6);
            DrawActions(tool);
            serializedObject.ApplyModifiedProperties();
        }

        private void OnSceneGUI()
        {
            MeshPivotTool tool = target as MeshPivotTool;
            if (tool == null || Application.isPlaying) return;
            tool.EnsureInitialized();
            CacheVertices(tool);
            Event e = Event.current;
            bool ctrlHeld = e.control || e.command;

            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.R && !e.control && !e.shift && !e.alt)
            {
                currentMode = currentMode == HandleMode.Position ? HandleMode.Rotation : HandleMode.Position;
                e.Use();
                Repaint();
            }

            if (ctrlHeld && e.type == EventType.MouseDown && e.button == 0 && currentMode == HandleMode.Position)
            {
                Vector3 snappedVertex;
                if (TryGetNearestVertex(tool, e.mousePosition, out snappedVertex))
                {
                    Vector3 deltaWorld = snappedVertex - tool.transform.position;
                    Vector3 deltaLocal = tool.transform.InverseTransformVector(deltaWorld);
                    RecordPivotChange(tool, "Vertex Snap Pivot");
                    tool.SetPivotToLocalPoint(deltaLocal);
                    MarkDirty(tool);
                    e.Use();
                    return;
                }
            }

            if (ctrlHeld && currentMode == HandleMode.Position)
            {
                DrawNearestVertexHighlight(tool, e.mousePosition);
                HandleUtility.Repaint();
            }

            Transform t = tool.transform;
            if (currentMode == HandleMode.Position && !ctrlHeld)
            {
                Vector3 pivotWorld = t.position;
                EditorGUI.BeginChangeCheck();
                Quaternion handleRot = UnityEditor.Tools.pivotRotation == PivotRotation.Local
                    ? t.rotation
                    : Quaternion.identity;
                Vector3 newPivotWorld = Handles.PositionHandle(pivotWorld, handleRot);
                if (EditorGUI.EndChangeCheck())
                {
                    float snap = tool.Snap;
                    if (snap > 0f) newPivotWorld = SnapWorld(newPivotWorld, snap);
                    Vector3 deltaWorld = newPivotWorld - pivotWorld;
                    Vector3 deltaLocal = t.InverseTransformVector(deltaWorld);
                    RecordPivotChange(tool, "Move Pivot");
                    tool.SetPivotToLocalPoint(deltaLocal);
                    MarkDirty(tool);
                }
            }
            else if (currentMode == HandleMode.Rotation)
            {
                EditorGUI.BeginChangeCheck();
                Quaternion handleRot = t.rotation;
                Quaternion newRot = Handles.RotationHandle(handleRot, t.position);
                if (EditorGUI.EndChangeCheck())
                {
                    Quaternion deltaRot = Quaternion.Inverse(handleRot) * newRot;
                    RecordPivotChange(tool, "Rotate Pivot");
                    tool.RotatePivotBy(deltaRot);
                    MarkDirty(tool);
                }
            }

            DrawBoundsWire(tool);
            DrawSceneOverlay(tool, ctrlHeld);
        }

        private void DrawModeToggle()
        {
            Rect box = EditorGUILayout.BeginVertical();
            EditorGUI.DrawRect(box, BoxBg);
            GUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(10);
                GUIStyle toggleStyle = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold, fixedHeight = 26 };
                Color oldBg = GUI.backgroundColor;
                GUI.backgroundColor = currentMode == HandleMode.Position ? AccentColor : Color.gray;
                if (GUILayout.Button("⊕ Position", toggleStyle)) currentMode = HandleMode.Position;
                GUILayout.Space(4);
                GUI.backgroundColor = currentMode == HandleMode.Rotation ? RotationColor : Color.gray;
                if (GUILayout.Button("↻ Rotation", toggleStyle)) currentMode = HandleMode.Rotation;
                GUI.backgroundColor = oldBg;
                GUILayout.Space(10);
            }
            GUILayout.Space(4);
            GUIStyle hint = new GUIStyle(EditorStyles.miniLabel)
                { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.gray } };
            EditorGUILayout.LabelField("Press R in Scene to toggle", hint);
            GUILayout.Space(6);
            EditorGUILayout.EndVertical();
        }

        private bool TryGetNearestVertex(MeshPivotTool tool, Vector2 mousePos, out Vector3 worldVertex)
        {
            worldVertex = Vector3.zero;
            if (cachedVertices == null || cachedVertices.Length == 0) return false;
            Transform t = tool.transform;
            float closestDist = float.MaxValue;
            bool found = false;
            float maxScreenDist = 40f;
            for (int i = 0; i < cachedVertices.Length; i++)
            {
                Vector3 worldVert = t.TransformPoint(cachedVertices[i]);
                Vector2 screenPoint = HandleUtility.WorldToGUIPoint(worldVert);
                float screenDist = Vector2.Distance(screenPoint, mousePos);
                if (screenDist < maxScreenDist && screenDist < closestDist)
                {
                    closestDist = screenDist;
                    worldVertex = worldVert;
                    found = true;
                }
            }
            return found;
        }

        private void DrawNearestVertexHighlight(MeshPivotTool tool, Vector2 mousePos)
        {
            Vector3 nearest;
            if (TryGetNearestVertex(tool, mousePos, out nearest))
            {
                Handles.color = VertexSnapColor;
                float size = HandleUtility.GetHandleSize(nearest) * 0.1f;
                Handles.SphereHandleCap(0, nearest, Quaternion.identity, size, EventType.Repaint);
                Handles.color = new Color(VertexSnapColor.r, VertexSnapColor.g, VertexSnapColor.b, 0.5f);
                Handles.DrawWireDisc(nearest, Camera.current.transform.forward, size * 1.5f);
            }
        }

        private void DrawHeader(MeshPivotTool tool)
        {
            Rect rect = GUILayoutUtility.GetRect(1f, 32f);
            EditorGUI.DrawRect(rect, HeaderBg);
            string meshType = tool.UseSkinnedMesh ? "SKINNED" : "MESH";
            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
                { fontSize = 13, alignment = TextAnchor.MiddleCenter, normal = { textColor = AccentColor } };
            EditorGUI.LabelField(rect, $"◈ PIVOT EDITOR ({meshType})", titleStyle);
        }

        private void DrawSnapSlider(MeshPivotTool tool)
        {
            Rect box = EditorGUILayout.BeginVertical();
            EditorGUI.DrawRect(box, BoxBg);
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
            EditorGUI.DrawRect(box, BoxBg);
            GUILayout.Space(10);
            GUIStyle sectionLabel = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter };
            EditorGUILayout.LabelField("Pivot Position", sectionLabel);
            GUILayout.Space(8);
            float btnSize = 44f;
            float smallBtn = 38f;
            float spacing = 4f;
            GUIStyle cubeBtn = new GUIStyle(GUI.skin.button)
                { fontSize = 9, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };

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
                GUI.backgroundColor = AccentColor;
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
            EditorGUI.DrawRect(box, BoxBg);
            GUILayout.Space(10);
            GUIStyle sectionLabel = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter };
            EditorGUILayout.LabelField("Pivot Rotation", sectionLabel);
            GUILayout.Space(8);
            GUIStyle dirBtn = new GUIStyle(GUI.skin.button) { fontSize = 10, fontStyle = FontStyle.Bold };

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(10);
                Color oldBg = GUI.backgroundColor;
                GUI.backgroundColor = RotationColor;
                if (GUILayout.Button("WORLD ALIGN", dirBtn, GUILayout.Height(28)))
                {
                    RecordPivotChange(tool, "Align to World");
                    tool.AlignToWorld();
                    MarkDirty(tool);
                }
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
                        Quaternion deltaRot = Quaternion.Inverse(tool.transform.rotation) * targetRot;
                        RecordPivotChange(tool, "Set Custom Rotation");
                        tool.RotatePivotBy(deltaRot);
                        MarkDirty(tool);
                    }
                    GUILayout.Space(10);
                }
            }
            GUILayout.Space(10);
            EditorGUILayout.EndVertical();
        }

        private void ApplyForwardPreset(MeshPivotTool tool, Vector3 direction)
        {
            RecordPivotChange(tool, $"Forward {direction}");
            tool.SetForwardDirection(direction);
            MarkDirty(tool);
        }

        private void DrawActions(MeshPivotTool tool)
        {
            Rect box = EditorGUILayout.BeginVertical();
            EditorGUI.DrawRect(box, BoxBg);
            GUILayout.Space(10);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(10);
                GUIStyle applyStyle = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold, fixedHeight = 30 };
                Color oldBg = GUI.backgroundColor;
                GUI.backgroundColor = AccentColor;
                GUI.enabled = tool.HasWorkingMesh;
                if (GUILayout.Button("✓ Apply & Remove", applyStyle))
                {
                    ApplyAndRemove(tool);
                    GUILayout.Space(10);
                    EditorGUILayout.EndVertical();
                    return;
                }
                GUILayout.Space(6);
                GUI.backgroundColor = DangerColor;
                if (GUILayout.Button("✕ Revert", applyStyle))
                {
                    RevertAndRemove(tool);
                    GUI.backgroundColor = oldBg;
                    GUI.enabled = true;
                    GUILayout.Space(10);
                    EditorGUILayout.EndVertical();
                    return;
                }
                GUI.backgroundColor = oldBg;
                GUI.enabled = true;
                GUILayout.Space(10);
            }
            GUILayout.Space(10);
            EditorGUILayout.EndVertical();
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
            tool.EnsureInitialized();
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
            RecordPivotChange(tool, $"Pivot {preset}");
            tool.SetPivotToLocalPoint(point);
            MarkDirty(tool);
        }

        private void ApplyAndRemove(MeshPivotTool tool)
        {
            serializedObject.ApplyModifiedProperties();
            SafeDestroy(tool);
            SceneView.RepaintAll();
        }

        private void RevertAndRemove(MeshPivotTool tool)
        {
            MeshFilter mf = tool.GetComponent<MeshFilter>();
            SkinnedMeshRenderer smr = tool.GetComponent<SkinnedMeshRenderer>();
            MeshCollider mc = tool.GetComponent<MeshCollider>();
            if (mf != null) Undo.RecordObject(mf, "Revert Pivot");
            if (smr != null) Undo.RecordObject(smr, "Revert Pivot");
            if (mc != null) Undo.RecordObject(mc, "Revert Pivot");
            Undo.RecordObject(tool, "Revert Pivot");
            Undo.RecordObject(tool.transform, "Revert Pivot");
            tool.RestoreOriginalMesh();
            if (mf != null) EditorUtility.SetDirty(mf);
            if (smr != null) EditorUtility.SetDirty(smr);
            SafeDestroy(tool);
            SceneView.RepaintAll();
        }

        private void RecordPivotChange(MeshPivotTool tool, string name)
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            MeshFilter mf = tool.GetComponent<MeshFilter>();
            SkinnedMeshRenderer smr = tool.GetComponent<SkinnedMeshRenderer>();
            MeshCollider mc = tool.GetComponent<MeshCollider>();
            Mesh mesh = GetMeshFromTool(tool);
            if (mf != null) Undo.RecordObject(mf, name);
            if (smr != null) Undo.RecordObject(smr, name);
            if (mc != null) Undo.RecordObject(mc, name);
            if (mesh != null) Undo.RecordObject(mesh, name);
            Undo.RecordObject(tool, name);
            Undo.RecordObject(tool.transform, name);
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
            SceneView.RepaintAll();
        }

        private void DrawBoundsWire(MeshPivotTool tool)
        {
            Bounds b = tool.GetRenderedBounds();
            Transform t = tool.transform;
            Color wireColor = currentMode == HandleMode.Rotation ? RotationColor : AccentColor;
            Handles.color = new Color(wireColor.r, wireColor.g, wireColor.b, 0.6f);
            Handles.matrix = t.localToWorldMatrix;
            Handles.DrawWireCube(b.center, b.size);
            Handles.matrix = Matrix4x4.identity;
        }

        private void DrawSceneOverlay(MeshPivotTool tool, bool ctrlHeld)
        {
            Handles.BeginGUI();
            string text;
            Color bgColor;
            Color textColor;
            if (currentMode == HandleMode.Rotation)
            {
                text = "◈ Rotation Mode | R = Position";
                bgColor = new Color(RotationColor.r * 0.3f, RotationColor.g * 0.3f, RotationColor.b * 0.3f, 0.85f);
                textColor = RotationColor;
            }
            else if (ctrlHeld)
            {
                text = "◈ Vertex Snap | Click to snap";
                bgColor = new Color(VertexSnapColor.r * 0.3f, VertexSnapColor.g * 0.3f, VertexSnapColor.b * 0.3f, 0.85f);
                textColor = VertexSnapColor;
            }
            else
            {
                text = "◈ Position Mode | R = Rotation | Ctrl = Snap";
                bgColor = new Color(0, 0, 0, 0.7f);
                textColor = Color.white;
            }
            Rect r = new Rect(10, 10, 280, 28);
            EditorGUI.DrawRect(r, bgColor);
            GUIStyle style = new GUIStyle(EditorStyles.boldLabel)
                { alignment = TextAnchor.MiddleCenter, normal = { textColor = textColor } };
            EditorGUI.LabelField(r, text, style);
            Handles.EndGUI();
        }

        private Vector3 SnapWorld(Vector3 world, float snap)
        {
            return new Vector3(
                Mathf.Round(world.x / snap) * snap,
                Mathf.Round(world.y / snap) * snap,
                Mathf.Round(world.z / snap) * snap);
        }

        private void SafeDestroy(Object obj)
        {
            if (obj == null) return;
            Object captured = obj;
            EditorApplication.delayCall += () =>
            {
                if (captured != null) Undo.DestroyObjectImmediate(captured);
            };
        }

        [MenuItem("CONTEXT/MeshFilter/Edit Mesh Pivot")]
        private static void AddToolFromMeshFilter(MenuCommand command)
        {
            MeshFilter mf = command.context as MeshFilter;
            if (mf == null) return;
            MeshPivotTool existing = mf.GetComponent<MeshPivotTool>();
            if (existing != null)
            {
                Selection.activeObject = existing;
                return;
            }
            Undo.AddComponent<MeshPivotTool>(mf.gameObject);
        }

        [MenuItem("CONTEXT/SkinnedMeshRenderer/Edit Mesh Pivot")]
        private static void AddToolFromSkinnedMesh(MenuCommand command)
        {
            SkinnedMeshRenderer smr = command.context as SkinnedMeshRenderer;
            if (smr == null) return;
            MeshPivotTool existing = smr.GetComponent<MeshPivotTool>();
            if (existing != null)
            {
                Selection.activeObject = existing;
                return;
            }
            Undo.AddComponent<MeshPivotTool>(smr.gameObject);
        }
    }
}
#endif