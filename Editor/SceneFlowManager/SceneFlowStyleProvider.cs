#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace TelleR.SceneFlow
{
    public class SceneFlowStyleProvider
    {
        // Colors
        public Color HeaderBackgroundColor { get; private set; }
        public Color GraphBackgroundColor { get; private set; }
        public Color NodeBackgroundColor { get; private set; }
        public Color NodeBorderColor { get; private set; }
        public Color NodeShadowColor { get; private set; }
        public Color SelectedNodeColor { get; private set; }
        public Color SelectedBorderColor { get; private set; }
        public Color HoveredNodeColor { get; private set; }
        public Color ConnectionColor { get; private set; }
        public Color CoreSectionBackgroundColor { get; private set; }
        public Color CoreNodeColor { get; private set; }
        public Color CoreBorderColor { get; private set; }
        public Color CoreIndicatorColor { get; private set; }
        
        // Styles
        public GUIStyle HeaderTitleStyle { get; private set; }
        public GUIStyle HeaderInfoStyle { get; private set; }
        public GUIStyle HeaderButtonStyle { get; private set; }
        public GUIStyle GraphAreaStyle { get; private set; }
        public GUIStyle GraphHeaderStyle { get; private set; }
        public GUIStyle GraphHeaderTextStyle { get; private set; }
        public GUIStyle GraphHeaderInfoStyle { get; private set; }
        public GUIStyle InspectorAreaStyle { get; private set; }
        public GUIStyle InspectorHeaderStyle { get; private set; }
        public GUIStyle InspectorSectionStyle { get; private set; }
        public GUIStyle InspectorSectionHeaderStyle { get; private set; }
        public GUIStyle SceneNameStyle { get; private set; }
        public GUIStyle SceneInfoStyle { get; private set; }
        public GUIStyle ActionButtonStyle { get; private set; }
        public GUIStyle DangerButtonStyle { get; private set; }

        public SceneFlowStyleProvider()
        {
            InitializeColors();
            InitializeStyles();
        }

        private void InitializeColors()
        {
            // Dark theme colors similar to QuestFlow
            HeaderBackgroundColor = new Color(0.09f, 0.09f, 0.11f);
            GraphBackgroundColor = new Color(0.15f, 0.15f, 0.17f);
            NodeBackgroundColor = new Color(0.24f, 0.24f, 0.28f);
            HoveredNodeColor = new Color(0.28f, 0.28f, 0.32f);
            SelectedNodeColor = new Color(0.25f, 0.35f, 0.55f);
            NodeBorderColor = new Color(0.35f, 0.35f, 0.4f);
            SelectedBorderColor = new Color(0.4f, 0.6f, 1f);
            NodeShadowColor = new Color(0.05f, 0.05f, 0.05f, 0.5f);
            ConnectionColor = new Color(0.5f, 0.5f, 0.6f, 0.8f);
            
            // Core scene specific colors
            CoreSectionBackgroundColor = new Color(0.12f, 0.12f, 0.14f, 0.3f);
            CoreNodeColor = new Color(0.22f, 0.26f, 0.32f);
            CoreBorderColor = new Color(0.4f, 0.5f, 0.7f);
            CoreIndicatorColor = new Color(0.4f, 0.6f, 1f, 0.8f);
        }

        private void InitializeStyles()
        {
            // Header styles
            HeaderTitleStyle = new GUIStyle(EditorStyles.largeLabel)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.9f, 0.9f, 0.9f) },
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(0, 0, 2, 0)
            };

            HeaderInfoStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11,
                normal = { textColor = new Color(0.6f, 0.6f, 0.6f) },
                alignment = TextAnchor.MiddleRight
            };

            HeaderButtonStyle = new GUIStyle("button")
            {
                fontSize = 11,
                fixedHeight = 24,
                normal = { 
                    textColor = new Color(0.85f, 0.85f, 0.85f),
                    background = CreateColorTexture(new Color(0.25f, 0.25f, 0.28f))
                },
                hover = { 
                    textColor = Color.white,
                    background = CreateColorTexture(new Color(0.3f, 0.3f, 0.33f))
                },
                active = {
                    background = CreateColorTexture(new Color(0.35f, 0.35f, 0.38f))
                },
                padding = new RectOffset(15, 15, 4, 4),
                margin = new RectOffset(3, 3, 0, 0)
            };

            // Graph area styles
            GraphAreaStyle = new GUIStyle()
            {
                margin = new RectOffset(0, 0, 0, 0),
                normal = { background = CreateColorTexture(GraphBackgroundColor) }
            };

            GraphHeaderStyle = new GUIStyle()
            {
                fixedHeight = 35,
                margin = new RectOffset(0, 0, 0, 0),
                normal = { background = CreateColorTexture(new Color(0.14f, 0.14f, 0.16f)) }
            };

            GraphHeaderTextStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(15, 0, 0, 0),
                normal = { textColor = new Color(0.85f, 0.85f, 0.85f) }
            };

            GraphHeaderInfoStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleRight,
                padding = new RectOffset(0, 15, 0, 0),
                normal = { textColor = new Color(0.6f, 0.6f, 0.6f) }
            };

            // Inspector styles
            InspectorAreaStyle = new GUIStyle()
            {
                margin = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(0, 0, 0, 0),
                normal = { background = CreateColorTexture(new Color(0.14f, 0.14f, 0.16f)) }
            };

            InspectorHeaderStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperLeft,
                padding = new RectOffset(20, 20, 15, 10),
                normal = { textColor = new Color(0.9f, 0.9f, 0.9f) }
            };

            InspectorSectionStyle = new GUIStyle()
            {
                padding = new RectOffset(20, 20, 10, 10),
                margin = new RectOffset(0, 0, 0, 1),
                normal = { background = CreateColorTexture(new Color(0.16f, 0.16f, 0.18f)) }
            };

            InspectorSectionHeaderStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                margin = new RectOffset(0, 0, 0, 8),
                normal = { textColor = new Color(0.8f, 0.8f, 0.8f) }
            };

            // Scene node styles
            SceneNameStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.95f, 0.95f, 0.95f) },
                wordWrap = false,
                clipping = TextClipping.Clip,
                padding = new RectOffset(0, 0, 0, 0),
                alignment = TextAnchor.MiddleLeft
            };

            SceneInfoStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 10,
                normal = { textColor = new Color(0.6f, 0.65f, 0.7f) },
                padding = new RectOffset(0, 0, 0, 0),
                alignment = TextAnchor.UpperLeft
            };

            // Button styles
            ActionButtonStyle = new GUIStyle("button")
            {
                fontSize = 11,
                fixedHeight = 24,
                normal = { 
                    textColor = new Color(0.85f, 0.85f, 0.85f),
                    background = CreateColorTexture(new Color(0.25f, 0.25f, 0.28f))
                },
                hover = { 
                    textColor = Color.white,
                    background = CreateColorTexture(new Color(0.3f, 0.3f, 0.33f))
                },
                active = {
                    background = CreateColorTexture(new Color(0.35f, 0.35f, 0.38f))
                },
                padding = new RectOffset(14, 14, 4, 4),
                margin = new RectOffset(4, 4, 2, 2)
            };

            DangerButtonStyle = new GUIStyle("button")
            {
                fontSize = 11,
                fixedHeight = 24,
                fontStyle = FontStyle.Bold,
                normal = { 
                    textColor = new Color(1f, 0.8f, 0.8f),
                    background = CreateColorTexture(new Color(0.5f, 0.2f, 0.2f))
                },
                hover = { 
                    textColor = Color.white,
                    background = CreateColorTexture(new Color(0.7f, 0.2f, 0.2f))
                },
                active = {
                    background = CreateColorTexture(new Color(0.8f, 0.3f, 0.3f))
                },
                padding = new RectOffset(15, 15, 4, 4),
                margin = new RectOffset(0, 0, 5, 0)
            };
        }

        private static Texture2D CreateColorTexture(Color color)
        {
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }
    }
}
#endif