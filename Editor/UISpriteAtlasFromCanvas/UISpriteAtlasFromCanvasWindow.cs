#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.U2D;

namespace TelleR
{
    public class UIAtlasDragDropWindow : EditorWindow
    {
        [SerializeField] private string atlasAssetPath = "Assets/UI/UIAtlas.spriteatlas";
        [SerializeField] private bool packAfterBuild = true;

        private Vector2 scroll;
        private readonly List<Object> entries = new List<Object>();
        private readonly HashSet<Object> entrySet = new HashSet<Object>();

        [MenuItem("Tools/TelleR/Tool/AtlasBuilder")]
        private static void Open()
        {
            GetWindow<UIAtlasDragDropWindow>("UI Atlas Builder");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Drag & Drop Images / Sprites / Textures / GameObjects here");
            Rect dropRect = GUILayoutUtility.GetRect(0f, 70f, GUILayout.ExpandWidth(true));
            GUI.Box(dropRect, "Drop Area", EditorStyles.helpBox);
            HandleDrop(dropRect);

            GUILayout.Space(8f);
            atlasAssetPath = EditorGUILayout.TextField("Atlas Asset Path", atlasAssetPath);
            packAfterBuild = EditorGUILayout.Toggle("Pack After Build", packAfterBuild);

            GUILayout.Space(8f);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Build / Update Atlas", GUILayout.Height(26f)))
                    BuildOrUpdateAtlas();

                if (GUILayout.Button("Clear", GUILayout.Width(80f), GUILayout.Height(26f)))
                    ClearEntries();
            }

            GUILayout.Space(8f);
            DrawEntryList();
        }

        private void HandleDrop(Rect dropRect)
        {
            Event e = Event.current;
            if (!dropRect.Contains(e.mousePosition))
                return;

            if (e.type == EventType.DragUpdated || e.type == EventType.DragPerform)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                if (e.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();

                    Object[] dropped = DragAndDrop.objectReferences;
                    for (int i = 0; i < dropped.Length; i++)
                        AddEntry(dropped[i]);
                }

                e.Use();
            }
        }

        private void AddEntry(Object obj)
        {
            if (obj == null)
                return;

            if (entrySet.Add(obj))
                entries.Add(obj);
        }

        private void ClearEntries()
        {
            entries.Clear();
            entrySet.Clear();
        }

        private void DrawEntryList()
        {
            EditorGUILayout.LabelField($"Entries ({entries.Count})", EditorStyles.boldLabel);

            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(120f));
            for (int i = 0; i < entries.Count; i++)
            {
                Object obj = entries[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.ObjectField(obj, typeof(Object), true);

                    if (GUILayout.Button("X", GUILayout.Width(22f)))
                    {
                        entrySet.Remove(obj);
                        entries.RemoveAt(i);
                        i--;
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void BuildOrUpdateAtlas()
        {
            string path = (atlasAssetPath ?? string.Empty).Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(path))
                return;

            if (!path.EndsWith(".spriteatlas"))
                path += ".spriteatlas";

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            SpriteAtlas atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
            bool isNew = atlas == null;

            if (isNew)
                atlas = new SpriteAtlas();

            HashSet<Sprite> sprites = CollectSpritesFromEntries(entries);
            Object[] packables = ToObjectArray(sprites);

            Object[] existing = SpriteAtlasExtensions.GetPackables(atlas);
            if (existing != null && existing.Length > 0)
                SpriteAtlasExtensions.Remove(atlas, existing);

            if (packables.Length > 0)
                SpriteAtlasExtensions.Add(atlas, packables);

            SpriteAtlasPackingSettings packing = atlas.GetPackingSettings();
            packing.enableRotation = false;
            packing.enableTightPacking = false;
            packing.padding = 2;
            atlas.SetPackingSettings(packing);

            SpriteAtlasTextureSettings texture = atlas.GetTextureSettings();
            texture.generateMipMaps = false;
            atlas.SetTextureSettings(texture);

            SpriteAtlasExtensions.SetIncludeInBuild(atlas, true);

            if (isNew)
                AssetDatabase.CreateAsset(atlas, path);

            EditorUtility.SetDirty(atlas);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (packAfterBuild)
                SpriteAtlasUtility.PackAtlases(new[] { atlas }, EditorUserBuildSettings.activeBuildTarget);
        }

        private static HashSet<Sprite> CollectSpritesFromEntries(List<Object> list)
        {
            HashSet<Sprite> sprites = new HashSet<Sprite>();

            for (int i = 0; i < list.Count; i++)
            {
                Object obj = list[i];
                if (obj == null)
                    continue;

                if (obj is Sprite sprite)
                {
                    sprites.Add(sprite);
                    continue;
                }

                if (obj is Image image)
                {
                    if (image.sprite != null)
                        sprites.Add(image.sprite);
                    continue;
                }

                if (obj is GameObject go)
                {
                    Image[] images = go.GetComponentsInChildren<Image>(true);
                    for (int j = 0; j < images.Length; j++)
                    {
                        Sprite s = images[j].sprite;
                        if (s != null)
                            sprites.Add(s);
                    }
                    continue;
                }

                if (obj is Texture2D)
                {
                    string assetPath = AssetDatabase.GetAssetPath(obj);
                    if (!string.IsNullOrEmpty(assetPath))
                    {
                        Object[] sub = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                        for (int j = 0; j < sub.Length; j++)
                        {
                            if (sub[j] is Sprite s)
                                sprites.Add(s);
                        }
                    }
                }
            }

            return sprites;
        }

        private static Object[] ToObjectArray(HashSet<Sprite> sprites)
        {
            Object[] arr = new Object[sprites.Count];
            int i = 0;

            foreach (Sprite s in sprites)
            {
                arr[i] = s;
                i++;
            }

            return arr;
        }
    }
}
#endif
