#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace TelleR.SceneFlow
{
    public struct NodeDragResult
    {
        public bool NodeHit;
        public bool EmptyHit;
        public SceneAssetRef HitScene;
        public bool DragStarted;
        public bool Dragging;
        public bool DragEnded;
        public SceneAssetRef DraggedScene;
        public Vector2 DragPosition;
    }

    public class SceneFlowRenderer
    {
        private const float NODE_WIDTH = 180f;
        private const float NODE_HEIGHT = 70f;
        private const float GAP_X = 40f;
        private const float GAP_Y = 100f;
        private const float MARGIN = 40f;
        private const float CORE_SECTION_HEIGHT = 120f;

        private const float STRAIGHT_TOL = 8f;
        private const float ARROW_SIZE = 8f;
        private const float ARROW_GAP = 2f;
        private const float MIN_SEG_LEN = 1.25f;
        
        private readonly SceneFlowStyleProvider style;
        private float contentWidth = 1200f;
        
        private SceneFlowLayoutData layoutData;
        private Dictionary<SceneAssetRef, Rect> nodeRects = new Dictionary<SceneAssetRef, Rect>();

        public void SetContentWidth(float width) => contentWidth = Mathf.Max(600f, width);
        public void SetLayoutData(SceneFlowLayoutData data) => layoutData = data;

        public SceneFlowRenderer(SceneFlowStyleProvider styleProvider) { style = styleProvider; }

        public Vector2 CalculateContentSize(int count)
        {
            int columns = GetColumns();
            int rows = Mathf.CeilToInt((float)count / Mathf.Max(1, columns));

            float totalRowW = columns * NODE_WIDTH + (columns - 1) * GAP_X;
            float minCore = NODE_WIDTH * 3 + GAP_X * 2;

            float width = Mathf.Max(MARGIN * 2 + totalRowW, MARGIN * 2 + minCore);
            float height = MARGIN * 2 + CORE_SECTION_HEIGHT + (NODE_HEIGHT + GAP_Y) * rows + 200f;
            return new Vector2(width, height);
        }

        public NodeDragResult DrawSceneFlow(
            List<SceneAssetRef> allScenes,
            (SceneAssetRef awake, SceneAssetRef lobby, SceneAssetRef room) coreScenes,
            SceneAssetRef selectedScene,
            SceneAssetRef connectingScene,
            Vector2 mousePos,
            SceneAssetRef draggingScene,
            Vector2 dragOffset)
        {
            var result = new NodeDragResult();
            nodeRects.Clear();

            if (allScenes == null || allScenes.Count == 0)
            {
                DrawEmptyState();
                return result;
            }

            int columns = GetColumns();
            float totalRowW = columns * NODE_WIDTH + (columns - 1) * GAP_X;
            float gridStartX = MARGIN + Mathf.Max(0, (contentWidth - MARGIN * 2 - totalRowW) / 2f);

            DrawCoreSection(coreScenes, selectedScene, mousePos, gridStartX, draggingScene, dragOffset, ref result);

            var others = GetNonCoreScenes(allScenes, coreScenes);
            var defaultPositions = CalculateDefaultPositions(others.Count, gridStartX, columns);

            for (int i = 0; i < others.Count; i++)
            {
                var scene = others[i];
                Vector2 pos = GetNodePosition(scene, defaultPositions[i]);
                
                if (draggingScene == scene)
                {
                    pos = mousePos - dragOffset;
                    pos.x = Mathf.Max(MARGIN, pos.x);
                    pos.y = Mathf.Max(MARGIN, pos.y);
                }
                
                nodeRects[scene] = new Rect(pos, new Vector2(NODE_WIDTH, NODE_HEIGHT));
            }

            DrawAllConnections(allScenes);

            for (int i = 0; i < others.Count; i++)
            {
                var scene = others[i];
                var rect = nodeRects[scene];
                bool hover = rect.Contains(mousePos);
                bool isDragging = draggingScene == scene;
                
                DrawSceneNode(rect, scene, scene == selectedScene, hover && !isDragging, scene == connectingScene, false, isDragging);

                if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && hover)
                {
                    result.NodeHit = true;
                    result.HitScene = scene;
                    result.DragStarted = true;
                    result.DraggedScene = scene;
                    result.DragPosition = mousePos - rect.position;
                    Event.current.Use();
                }
            }

            if (draggingScene != null)
            {
                if (Event.current.type == EventType.MouseDrag)
                {
                    result.Dragging = true;
                    result.DraggedScene = draggingScene;
                    Event.current.Use();
                }
                else if (Event.current.type == EventType.MouseUp)
                {
                    result.DragEnded = true;
                    result.DraggedScene = draggingScene;
                    
                    Vector2 finalPos = mousePos - dragOffset;
                    finalPos.x = Mathf.Max(MARGIN, finalPos.x);
                    finalPos.y = Mathf.Max(MARGIN, finalPos.y);
                    result.DragPosition = finalPos;
                    
                    Event.current.Use();
                }
            }

            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && !result.NodeHit)
            {
                result.EmptyHit = true;
            }

            return result;
        }

        private Vector2 GetNodePosition(SceneAssetRef scene, Vector2 defaultPos)
        {
            if (layoutData == null || scene == null) return defaultPos;
            
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(scene));
            var saved = layoutData.GetPosition(guid);
            return saved ?? defaultPos;
        }

        private void DrawCoreSection(
            (SceneAssetRef awake, SceneAssetRef lobby, SceneAssetRef room) coreScenes,
            SceneAssetRef selectedScene,
            Vector2 mousePos,
            float gridStartX,
            SceneAssetRef draggingScene,
            Vector2 dragOffset,
            ref NodeDragResult dragResult)
        {
            float step = NODE_WIDTH + GAP_X;
            float defaultY = MARGIN + 30f;

            Vector2 defaultAwake = new Vector2(gridStartX + 0 * step, defaultY);
            Vector2 defaultLobby = new Vector2(gridStartX + 1 * step, defaultY);
            Vector2 defaultRoom = new Vector2(gridStartX + 2 * step, defaultY);

            Vector2 awakePos = GetDraggedPosition(coreScenes.awake, defaultAwake, draggingScene, mousePos, dragOffset);
            Vector2 lobbyPos = GetDraggedPosition(coreScenes.lobby, defaultLobby, draggingScene, mousePos, dragOffset);
            Vector2 roomPos = GetDraggedPosition(coreScenes.room, defaultRoom, draggingScene, mousePos, dragOffset);

            var awakeRect = new Rect(awakePos, new Vector2(NODE_WIDTH, NODE_HEIGHT));
            var lobbyRect = new Rect(lobbyPos, new Vector2(NODE_WIDTH, NODE_HEIGHT));
            var roomRect = new Rect(roomPos, new Vector2(NODE_WIDTH, NODE_HEIGHT));

            float pad = 10f;
            float minX = Mathf.Min(awakeRect.xMin, lobbyRect.xMin, roomRect.xMin);
            float maxX = Mathf.Max(awakeRect.xMax, lobbyRect.xMax, roomRect.xMax);
            float minY = Mathf.Min(awakeRect.yMin, lobbyRect.yMin, roomRect.yMin);
            float maxY = Mathf.Max(awakeRect.yMax, lobbyRect.yMax, roomRect.yMax);
            
            var coreBg = new Rect(
                minX - pad,
                minY - (pad + 20f),
                (maxX - minX) + pad * 2f,
                (maxY - minY) + pad * 2f + 20f
            );
            EditorGUI.DrawRect(coreBg, style.CoreSectionBackgroundColor);

            var labelStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.7f, 0.7f, 0.7f) }
            };
            GUI.Label(new Rect(coreBg.x + 10f, coreBg.y + 2f, 150f, 18f), "Core Scenes", labelStyle);

            if (coreScenes.awake != null) nodeRects[coreScenes.awake] = awakeRect;
            if (coreScenes.lobby != null) nodeRects[coreScenes.lobby] = lobbyRect;
            if (coreScenes.room != null) nodeRects[coreScenes.room] = roomRect;

            DrawCoreNode(coreScenes.awake, awakeRect, selectedScene, mousePos, draggingScene, ref dragResult, "Awake");
            DrawCoreNode(coreScenes.lobby, lobbyRect, selectedScene, mousePos, draggingScene, ref dragResult, "Lobby");
            DrawCoreNode(coreScenes.room, roomRect, selectedScene, mousePos, draggingScene, ref dragResult, "Room");
        }

        private Vector2 GetDraggedPosition(SceneAssetRef scene, Vector2 defaultPos, SceneAssetRef draggingScene, Vector2 mousePos, Vector2 dragOffset)
        {
            if (scene == null) return defaultPos;
            
            if (draggingScene == scene)
            {
                Vector2 pos = mousePos - dragOffset;
                pos.x = Mathf.Max(MARGIN, pos.x);
                pos.y = Mathf.Max(MARGIN, pos.y);
                return pos;
            }
            
            return GetNodePosition(scene, defaultPos);
        }

        private void DrawCoreNode(SceneAssetRef scene, Rect rect, SceneAssetRef selected, Vector2 mousePos,
                                  SceneAssetRef draggingScene, ref NodeDragResult dragResult, string placeholder)
        {
            if (scene != null)
            {
                bool hover = rect.Contains(mousePos);
                bool isDragging = draggingScene == scene;
                DrawSceneNode(rect, scene, scene == selected, hover && !isDragging, false, true, isDragging);
                
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && hover)
                {
                    dragResult.NodeHit = true;
                    dragResult.HitScene = scene;
                    dragResult.DragStarted = true;
                    dragResult.DraggedScene = scene;
                    dragResult.DragPosition = mousePos - rect.position;
                    Event.current.Use();
                }
            }
            else
            {
                DrawPlaceholderNode(rect, placeholder);
            }
        }

        private void DrawSceneNode(Rect rect, SceneAssetRef scene, bool isSelected, bool isHovered, bool isConnecting, bool isCore, bool isDragging)
        {
            if (isDragging)
            {
                EditorGUI.DrawRect(new Rect(rect.x + 4, rect.y + 4, rect.width, rect.height), new Color(0, 0, 0, 0.3f));
            }
            else
            {
                EditorGUI.DrawRect(new Rect(rect.x + 2, rect.y + 2, rect.width, rect.height), style.NodeShadowColor);
            }

            Color bg = isSelected ? style.SelectedNodeColor :
                       isHovered ? style.HoveredNodeColor :
                       isCore ? style.CoreNodeColor : style.NodeBackgroundColor;
            
            if (isDragging)
            {
                bg = new Color(bg.r + 0.1f, bg.g + 0.1f, bg.b + 0.15f);
            }
            
            EditorGUI.DrawRect(rect, bg);

            Color borderColor = isSelected ? style.SelectedBorderColor : 
                               isDragging ? new Color(0.5f, 0.7f, 1f) :
                               isCore ? style.CoreBorderColor : style.NodeBorderColor;
            DrawBorder(rect, borderColor, isSelected || isDragging ? 2 : 1);
            
            if (isCore) EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3, rect.height), style.CoreIndicatorColor);

            var area = new Rect(rect.x + 10, rect.y + 10, rect.width - 20, rect.height - 20);
            var nameStyle = new GUIStyle(style.SceneNameStyle) { alignment = TextAnchor.UpperLeft };
            string name = scene.SceneName;
            if (name.Length > 20) name = name.Substring(0, 17) + "...";
            GUI.Label(new Rect(area.x, area.y, area.width, 25), name, nameStyle);

            var infoStyle = new GUIStyle(style.SceneInfoStyle);
            string prev = scene.PreviousAssetRef ? scene.PreviousAssetRef.SceneName : "None";
            string next = scene.NextAssetRef ? scene.NextAssetRef.SceneName : "None";
            if (prev.Length > 8) prev = prev[..6] + "..";
            if (next.Length > 8) next = next[..6] + "..";
            GUI.Label(new Rect(area.x, area.y + 25, area.width, 20), $"← {prev} | {next} →", infoStyle);

            bool inBuild = scene.CurrentSceneIndex >= 0;
            var r = new Rect(rect.xMax - 12, rect.y + 5, 7, 7);
            EditorGUI.DrawRect(r, inBuild ? new Color(0.3f, 0.8f, 0.3f) : new Color(0.8f, 0.3f, 0.3f));
        }

        private void DrawPlaceholderNode(Rect rect, string label)
        {
            EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.17f, 0.5f));
            DrawDashedBorder(rect, new Color(0.3f, 0.3f, 0.35f), 1);

            var labelStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Italic,
                normal = { textColor = new Color(0.4f, 0.4f, 0.4f) }
            };
            GUI.Label(rect, $"No '{label}' scene", labelStyle);
        }

        private void DrawAllConnections(List<SceneAssetRef> allScenes)
        {
            Handles.BeginGUI();
            Handles.color = style.ConnectionColor;

            foreach (var scene in allScenes)
            {
                if (scene == null) continue;
                if (scene.NextAssetRef == null) continue;
                if (!nodeRects.ContainsKey(scene)) continue;
                if (!nodeRects.ContainsKey(scene.NextAssetRef)) continue;

                DrawConnection(nodeRects[scene], nodeRects[scene.NextAssetRef]);
            }

            Handles.color = Color.white;
            Handles.EndGUI();
        }

        private void DrawConnection(Rect fromRect, Rect toRect)
        {
            Vector2 from = fromRect.center;
            Vector2 to = toRect.center;

            float dx = to.x - from.x;
            float dy = to.y - from.y;
            bool horizontal = Mathf.Abs(dx) > Mathf.Abs(dy);

            if (horizontal)
            {
                DrawHorizontalConnection(fromRect, toRect);
            }
            else
            {
                DrawVerticalConnection(fromRect, toRect);
            }
        }

        private void DrawHorizontalConnection(Rect fromRect, Rect toRect)
        {
            bool toRight = toRect.center.x >= fromRect.center.x;

            float startX = toRight ? fromRect.xMax : fromRect.xMin;
            float endX = toRight ? (toRect.xMin - ARROW_GAP) : (toRect.xMax + ARROW_GAP);

            float startY = fromRect.center.y;
            float endY = toRect.center.y;

            if (Mathf.Abs(startY - endY) < STRAIGHT_TOL)
            {
                DrawLineWithArrow(new Vector2(startX, startY), new Vector2(endX, endY));
            }
            else
            {
                float midX = (startX + endX) * 0.5f;
                var pts = new Vector2[]
                {
                    new Vector2(startX, startY),
                    new Vector2(midX, startY),
                    new Vector2(midX, endY),
                    new Vector2(endX, endY)
                };
                DrawPolylineWithArrow(pts);
            }
        }

        private void DrawVerticalConnection(Rect fromRect, Rect toRect)
        {
            bool toBelow = toRect.center.y > fromRect.center.y;
            float startX = fromRect.center.x;
            float startY = toBelow ? fromRect.yMax : fromRect.yMin;
            float endX = toRect.center.x;
            float endY = toBelow ? toRect.yMin - ARROW_GAP : toRect.yMax + ARROW_GAP;

            if (Mathf.Abs(startX - endX) < STRAIGHT_TOL)
            {
                DrawLineWithArrow(new Vector2(startX, startY), new Vector2(endX, endY));
            }
            else
            {
                float midY = (startY + endY) * 0.5f;
                var pts = new Vector2[]
                {
                    new Vector2(startX, startY),
                    new Vector2(startX, midY),
                    new Vector2(endX, midY),
                    new Vector2(endX, endY)
                };
                DrawPolylineWithArrow(pts);
            }
        }

        private void DrawPolylineWithArrow(IList<Vector2> pts)
        {
            var cleaned = CleanPolyline(pts);
            if (cleaned.Count < 2) return;

            Vector2 a = cleaned[cleaned.Count - 2];
            Vector2 b = cleaned[cleaned.Count - 1];
            Vector2 dir = (b - a).normalized;

            if ((b - a).sqrMagnitude < MIN_SEG_LEN * MIN_SEG_LEN) return;

            Vector2 arrowBase = b - dir * ARROW_SIZE;

            var drawPoints = new List<Vector2>(cleaned);
            drawPoints[drawPoints.Count - 1] = arrowBase;

            for (int i = 0; i < drawPoints.Count - 1; i++)
            {
                Handles.DrawAAPolyLine(2f, drawPoints[i], drawPoints[i + 1]);
            }

            DrawArrowHead(b, dir);
        }

        private void DrawLineWithArrow(Vector2 start, Vector2 end)
        {
            if ((end - start).sqrMagnitude < MIN_SEG_LEN * MIN_SEG_LEN) return;

            Vector2 dir = (end - start).normalized;
            Vector2 arrowBase = end - dir * ARROW_SIZE;

            Handles.DrawAAPolyLine(2f, start, arrowBase);
            DrawArrowHead(end, dir);
        }

        private void DrawArrowHead(Vector2 tip, Vector2 dir)
        {
            if (dir.sqrMagnitude < 0.001f) dir = Vector2.right;
            else dir = dir.normalized;

            Vector2 perp = new Vector2(-dir.y, dir.x);

            Vector2 left = tip - dir * ARROW_SIZE + perp * (ARROW_SIZE * 0.5f);
            Vector2 right = tip - dir * ARROW_SIZE - perp * (ARROW_SIZE * 0.5f);

            Handles.DrawAAConvexPolygon(tip, left, right);
        }

        private static List<Vector2> CleanPolyline(IList<Vector2> pts)
        {
            if (pts == null || pts.Count < 2) return new List<Vector2>();

            var result = new List<Vector2>(pts.Count) { pts[0] };

            for (int i = 1; i < pts.Count; i++)
            {
                var last = result[result.Count - 1];
                var curr = pts[i];

                if ((curr - last).sqrMagnitude > MIN_SEG_LEN * MIN_SEG_LEN)
                {
                    if (result.Count >= 2)
                    {
                        var prev = result[result.Count - 2];
                        bool collinearH = Mathf.Abs(prev.y - last.y) < 0.5f && Mathf.Abs(last.y - curr.y) < 0.5f;
                        bool collinearV = Mathf.Abs(prev.x - last.x) < 0.5f && Mathf.Abs(last.x - curr.x) < 0.5f;

                        if (collinearH || collinearV)
                        {
                            result[result.Count - 1] = curr;
                            continue;
                        }
                    }
                    result.Add(curr);
                }
            }

            return result;
        }

        private int GetColumns()
        {
            float usable = contentWidth - MARGIN * 2f;
            int max = Mathf.FloorToInt(usable / (NODE_WIDTH + GAP_X));
            return Mathf.Max(3, max);
        }

        private List<Vector2> CalculateDefaultPositions(int count, float startX, int columns)
        {
            var result = new List<Vector2>(count);
            float startY = MARGIN + CORE_SECTION_HEIGHT + 20f;
            float stepX = NODE_WIDTH + GAP_X;
            float stepY = NODE_HEIGHT + GAP_Y;

            for (int i = 0; i < count; i++)
            {
                int row = i / Mathf.Max(1, columns);
                int col = i % Mathf.Max(1, columns);
                float x = startX + col * stepX;
                float y = startY + row * stepY;
                result.Add(new Vector2(x, y));
            }
            return result;
        }

        private List<SceneAssetRef> GetNonCoreScenes(List<SceneAssetRef> all, (SceneAssetRef a, SceneAssetRef l, SceneAssetRef r) core)
        {
            var list = new List<SceneAssetRef>();
            foreach (var s in all)
                if (s != core.a && s != core.l && s != core.r) list.Add(s);
            return list;
        }

        private void DrawBorder(Rect r, Color c, int t)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, t, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - t, r.y, t, r.height), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, t), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - t, r.width, t), c);
        }

        private void DrawDashedBorder(Rect r, Color c, int t)
        {
            int dash = 5, gap = 3;
            for (float x = r.x; x < r.xMax; x += dash + gap)
            {
                float w = Mathf.Min(dash, r.xMax - x);
                EditorGUI.DrawRect(new Rect(x, r.y, w, t), c);
                EditorGUI.DrawRect(new Rect(x, r.yMax - t, w, t), c);
            }
            for (float y = r.y; y < r.yMax; y += dash + gap)
            {
                float h = Mathf.Min(dash, r.yMax - y);
                EditorGUI.DrawRect(new Rect(r.x, y, t, h), c);
                EditorGUI.DrawRect(new Rect(r.xMax - t, y, t, h), c);
            }
        }

        private void DrawEmptyState()
        {
            var r = new Rect(MARGIN, MARGIN, 400, 60);
            EditorGUI.HelpBox(r, "No scene references found. Click '+ New Scene' to create one.", MessageType.Info);
        }
    }
}
#endif