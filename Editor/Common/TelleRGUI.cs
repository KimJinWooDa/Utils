using UnityEditor;
using UnityEngine;

namespace TelleR
{
    /// <summary>
    /// 패키지 공용 IMGUI 헬퍼. Light/Dark 스킨 양쪽 색상, 캐시된 GUIStyle, 드롭 영역, 다이얼로그 문구를 한곳에 모은다.
    /// 스타일은 스킨이 바뀌면 다시 만들고, 그 외에는 OnGUI마다 할당하지 않는다.
    /// </summary>
    public static class TelleRGUI
    {
        public static bool IsPro => EditorGUIUtility.isProSkin;

        public static Color Pick(Color dark, Color light) => IsPro ? dark : light;

        // ─── Palette ───
        public static Color PanelBg => Pick(new Color(0.20f, 0.20f, 0.20f), new Color(0.86f, 0.86f, 0.86f));
        public static Color RowBg => Pick(new Color(0.24f, 0.24f, 0.24f), new Color(0.80f, 0.80f, 0.80f));
        public static Color RowBgAlt => Pick(new Color(0.27f, 0.27f, 0.27f), new Color(0.76f, 0.76f, 0.76f));
        public static Color HeaderBg => Pick(new Color(0.16f, 0.16f, 0.16f), new Color(0.72f, 0.72f, 0.72f));
        public static Color Separator => Pick(new Color(0.12f, 0.12f, 0.12f), new Color(0.60f, 0.60f, 0.60f));
        public static Color Accent => Pick(new Color(0.35f, 0.62f, 1.00f), new Color(0.10f, 0.36f, 0.80f));
        public static Color Success => Pick(new Color(0.40f, 0.85f, 0.45f), new Color(0.05f, 0.50f, 0.12f));
        public static Color Warning => Pick(new Color(1.00f, 0.78f, 0.30f), new Color(0.62f, 0.40f, 0.00f));
        public static Color Danger => Pick(new Color(1.00f, 0.42f, 0.40f), new Color(0.72f, 0.10f, 0.10f));
        public static Color HintText => Pick(new Color(0.62f, 0.62f, 0.62f), new Color(0.32f, 0.32f, 0.32f));
        public static Color StrongText => Pick(new Color(0.92f, 0.92f, 0.92f), new Color(0.08f, 0.08f, 0.08f));

        /// <summary>버튼 배경 틴트. GUI.backgroundColor에 곱해지므로 스킨별로 채도를 다르게 둔다.</summary>
        public static Color AccentButton => Pick(new Color(0.45f, 0.70f, 1.00f), new Color(0.60f, 0.78f, 1.00f));
        public static Color SuccessButton => Pick(new Color(0.50f, 0.90f, 0.55f), new Color(0.62f, 0.95f, 0.66f));
        public static Color DangerButton => Pick(new Color(1.00f, 0.50f, 0.48f), new Color(1.00f, 0.66f, 0.64f));

        // ─── Styles (lazy, skin-aware cache) ───
        private static bool? stylesBuiltForPro;
        private static GUIStyle header, subHeader, hint, hintCentered, badge, centeredBold, dropLabel, richLabel, wrappedLabel;

        public static GUIStyle Header { get { EnsureStyles(); return header; } }
        public static GUIStyle SubHeader { get { EnsureStyles(); return subHeader; } }
        public static GUIStyle Hint { get { EnsureStyles(); return hint; } }
        public static GUIStyle HintCentered { get { EnsureStyles(); return hintCentered; } }
        public static GUIStyle Badge { get { EnsureStyles(); return badge; } }
        public static GUIStyle CenteredBold { get { EnsureStyles(); return centeredBold; } }
        public static GUIStyle DropLabel { get { EnsureStyles(); return dropLabel; } }
        public static GUIStyle RichLabel { get { EnsureStyles(); return richLabel; } }
        public static GUIStyle WrappedLabel { get { EnsureStyles(); return wrappedLabel; } }

        private static void EnsureStyles()
        {
            if (stylesBuiltForPro == IsPro && header != null) return;
            stylesBuiltForPro = IsPro;

            header = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
            header.normal.textColor = StrongText;

            subHeader = new GUIStyle(EditorStyles.boldLabel);
            subHeader.normal.textColor = StrongText;

            hint = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, richText = true };
            hint.normal.textColor = HintText;

            hintCentered = new GUIStyle(hint) { alignment = TextAnchor.MiddleCenter };

            badge = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(6, 6, 1, 1)
            };

            centeredBold = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, wordWrap = true };

            dropLabel = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                richText = true,
                fontStyle = FontStyle.Bold
            };
            dropLabel.normal.textColor = HintText;

            richLabel = new GUIStyle(EditorStyles.label) { richText = true };
            wrappedLabel = new GUIStyle(EditorStyles.label) { wordWrap = true, richText = true };
        }

        // ─── Drawing helpers ───

        public static void DrawSeparator(float thickness = 1f, float padding = 4f)
        {
            Rect r = GUILayoutUtility.GetRect(1f, thickness + padding * 2f, GUILayout.ExpandWidth(true));
            r.y += padding;
            r.height = thickness;
            EditorGUI.DrawRect(r, Separator);
        }

        /// <summary>제목 + 구분선. 섹션 시작에 쓴다.</summary>
        public static void Section(string title)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(title, SubHeader);
            DrawSeparator(1f, 1f);
        }

        /// <summary>글자색이 배경색 위에서 읽히도록 배경 박스를 그린다(Repaint 이벤트에서만).</summary>
        public static void DrawBackground(Rect rect, Color color)
        {
            if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(rect, color);
        }

        /// <summary>배지(작은 라벨)를 색 배경 위에 그린다.</summary>
        public static void DrawBadge(Rect rect, string text, Color background)
        {
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, background);
                var prev = Badge.normal.textColor;
                Badge.normal.textColor = background.grayscale > 0.5f ? Color.black : Color.white;
                Badge.Draw(rect, text, false, false, false, false);
                Badge.normal.textColor = prev;
            }
        }

        // ─── Drop zone ───

        /// <summary>
        /// 드롭 영역을 그리고 처리한다. 드래그가 영역 위에 있으면 테두리·배경을 강조한다.
        /// 드롭이 일어난 이벤트에서만 true를 반환하고 objects/paths를 채운다.
        /// </summary>
        public static bool DropZone(Rect rect, string label, out Object[] objects, out string[] paths)
        {
            objects = null;
            paths = null;

            Event evt = Event.current;
            bool dragging = DragAndDrop.objectReferences.Length > 0 || DragAndDrop.paths.Length > 0;
            bool hover = dragging && rect.Contains(evt.mousePosition) &&
                         (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform || evt.type == EventType.Repaint);

            if (evt.type == EventType.Repaint)
            {
                Color bg = hover ? new Color(Accent.r, Accent.g, Accent.b, IsPro ? 0.22f : 0.18f) : RowBg;
                EditorGUI.DrawRect(rect, bg);
                Color border = hover ? Accent : Separator;
                float t = hover ? 2f : 1f;
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, t), border);
                EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - t, rect.width, t), border);
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, t, rect.height), border);
                EditorGUI.DrawRect(new Rect(rect.xMax - t, rect.y, t, rect.height), border);
                var prev = DropLabel.normal.textColor;
                DropLabel.normal.textColor = hover ? Accent : HintText;
                DropLabel.Draw(rect, label, false, false, false, false);
                DropLabel.normal.textColor = prev;
            }

            if (!rect.Contains(evt.mousePosition)) return false;

            if (evt.type == EventType.DragUpdated)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                evt.Use();
                HandleUtilityRepaint();
            }
            else if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                objects = DragAndDrop.objectReferences;
                paths = DragAndDrop.paths;
                evt.Use();
                return true;
            }
            return false;
        }

        private static void HandleUtilityRepaint()
        {
            // 드래그 중 강조 표시가 즉시 갱신되도록 현재 창을 다시 그린다.
            if (EditorWindow.mouseOverWindow != null) EditorWindow.mouseOverWindow.Repaint();
        }

        // ─── Dialogs (문구 통일: 확인/취소) ───

        public static bool Confirm(string title, string message, string ok = "확인", string cancel = "취소")
            => EditorUtility.DisplayDialog(title, message, ok, cancel);

        public static void Info(string title, string message, string ok = "확인")
            => EditorUtility.DisplayDialog(title, message, ok);
    }
}
