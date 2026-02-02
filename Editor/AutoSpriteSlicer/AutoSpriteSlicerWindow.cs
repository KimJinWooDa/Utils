#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TelleR
{
    public class AutoSpriteSlicerWindow : EditorWindow
    {
        private static readonly string[] SupportedExtensions = { ".png", ".jpg", ".jpeg", ".tga", ".bmp", ".psd", ".gif", ".hdr", ".exr", ".tif", ".tiff" };

        private List<string> dropPaths = new List<string>();
        private Vector2 scroll;

        [MenuItem("Tools/TelleR/Tool/Auto Sprite Slicer")]
        private static void Open()
        {
            var win = GetWindow<AutoSpriteSlicerWindow>(false, "Auto Sprite Slicer");
            win.minSize = new Vector2(420, 360);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Single Full-Image Sprite", EditorStyles.boldLabel);

            var dropRect = GUILayoutUtility.GetRect(0, 64, GUILayout.ExpandWidth(true));
            GUI.Box(dropRect, "여기에 이미지(또는 폴더)를 드래그\n현재: " + dropPaths.Count + "개", EditorStyles.helpBox);
            HandleDragAndDrop(dropRect);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add Selected Images/Folders")) AddPathsFromObjects(Selection.objects, true);
                if (GUILayout.Button("Clear List")) dropPaths.Clear();
            }

            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("대상 목록", EditorStyles.boldLabel);
                scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MaxHeight(180));
                for (int i = 0; i < dropPaths.Count; i++)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(dropPaths[i], GUILayout.ExpandWidth(true));
                        if (GUILayout.Button("X", GUILayout.Width(28)))
                        {
                            dropPaths.RemoveAt(i);
                            i--;
                        }
                    }
                }

                EditorGUILayout.EndScrollView();

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    EditorGUI.BeginDisabledGroup(dropPaths.Count == 0);
                    if (GUILayout.Button("Convert to Single Sprite (" + dropPaths.Count + ")", GUILayout.Height(28)))
                    {
                        ConvertToSingleSprite();
                    }

                    EditorGUI.EndDisabledGroup();
                }
            }

            EditorGUILayout.HelpBox("PNG, JPG, TGA, BMP, PSD, GIF, HDR, EXR, TIF 지원", MessageType.Info);
        }

        private void HandleDragAndDrop(Rect r)
        {
            var e = Event.current;
            if (!r.Contains(e.mousePosition)) return;
            if (e.type == EventType.DragUpdated || e.type == EventType.DragPerform)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                if (e.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    AddPathsFromObjects(DragAndDrop.objectReferences, true);
                }

                e.Use();
            }
        }

        private void AddPathsFromObjects(UnityEngine.Object[] objs, bool includeFolders)
        {
            var newPaths = new List<string>();
            foreach (var obj in objs)
            {
                if (obj == null) continue;
                var path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path)) continue;

                if (includeFolders && Directory.Exists(path))
                {
                    var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { path });
                    foreach (var guid in guids)
                    {
                        var p = AssetDatabase.GUIDToAssetPath(guid);
                        if (IsSupportedImage(p)) newPaths.Add(p);
                    }
                }
                else
                {
                    if (IsSupportedImage(path)) newPaths.Add(path);
                }
            }

            foreach (var p in newPaths)
            {
                if (!dropPaths.Contains(p))
                {
                    dropPaths.Add(p);
                }
            }
        }

        private static bool IsSupportedImage(string path)
        {
            foreach (var ext in SupportedExtensions)
            {
                if (path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        private void ConvertToSingleSprite()
        {
            int processed = 0;
            int skipped = 0;

            try
            {
                AssetDatabase.StartAssetEditing();

                foreach (var path in dropPaths)
                {
                    if (!IsSupportedImage(path))
                    {
                        skipped++;
                        continue;
                    }

                    var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (importer == null)
                    {
                        skipped++;
                        continue;
                    }

                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.alphaIsTransparency = true;
                    importer.spritePixelsPerUnit = 100f;

                    TextureImporterSettings settings = new TextureImporterSettings();
                    importer.ReadTextureSettings(settings);
                    settings.spriteAlignment = (int)SpriteAlignment.Center;
                    settings.spritePivot = new Vector2(0.5f, 0.5f);
                    importer.SetTextureSettings(settings);

                    importer.SaveAndReimport();
                    processed++;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[AutoSpriteSlicer] " + ex.Message);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            Debug.Log("[AutoSpriteSlicer] 완료 - 성공: " + processed + ", 스킵: " + skipped);
        }
    }
}
#endif