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
            EditorGUILayout.HelpBox(
                "캔버스(또는 UI 프리팹)를 통째로 드래그하면 하위 Image가 쓰는 스프라이트를 모아 SpriteAtlas로 묶어 드로우콜을 줄입니다.\n" +
                "1) Drop Area에 Canvas / 스프라이트 / 텍스처 드래그   2) Atlas Asset Path 확인   3) Build / Update Atlas\n" +
                "※ 텍스처는 Sprite 타입으로 임포트돼 있어야 수집됩니다. Entries 목록은 컴파일·플레이 진입 시 초기화됩니다.",
                MessageType.Info);
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
            {
                EditorUtility.DisplayDialog("UI Atlas Builder", "Atlas Asset Path를 입력해 주세요.\n예: Assets/UI/UIAtlas.spriteatlas", "OK");
                return;
            }

            if (!path.EndsWith(".spriteatlas"))
                path += ".spriteatlas";

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                // 새 폴더를 AssetDatabase가 인식하기 전에 CreateAsset하면 실패하므로 먼저 Refresh
                AssetDatabase.Refresh();
            }

            SpriteAtlas atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
            bool isNew = atlas == null;

            if (isNew)
                atlas = new SpriteAtlas();

            HashSet<Sprite> sprites = CollectSpritesFromEntries(entries);
            Object[] packables = ToObjectArray(sprites);

            if (packables.Length == 0)
            {
                // 도메인 리로드로 목록이 비워진 상태에서 Build를 누르면 기존 아틀라스가 빈 깡통이 되는 사고 방지
                EditorUtility.DisplayDialog("UI Atlas Builder",
                    "수집된 스프라이트가 없습니다.\n(Drop 목록은 도메인 리로드/플레이 진입 시 초기화됩니다 — 다시 드롭해 주세요)", "OK");
                return;
            }

            Object[] existing = SpriteAtlasExtensions.GetPackables(atlas);
            if (existing != null && existing.Length > 0)
            {
                if (!EditorUtility.DisplayDialog("UI Atlas Builder",
                    $"'{Path.GetFileName(path)}'의 기존 packable {existing.Length}개를 현재 목록 {packables.Length}개로 교체합니다.\n계속할까요?",
                    "교체", "취소"))
                    return;
                SpriteAtlasExtensions.Remove(atlas, existing);
            }

            if (packables.Length > 0)
                SpriteAtlasExtensions.Add(atlas, packables);

            // 기본 설정은 새 아틀라스에만 적용 — 기존 아틀라스의 수동 튜닝(패딩·회전 등)을 리셋하지 않음
            if (isNew)
            {
                SpriteAtlasPackingSettings packing = atlas.GetPackingSettings();
                packing.enableRotation = false;
                packing.enableTightPacking = false;
                packing.padding = 2;
                atlas.SetPackingSettings(packing);

                SpriteAtlasTextureSettings texture = atlas.GetTextureSettings();
                texture.generateMipMaps = false;
                atlas.SetTextureSettings(texture);

                SpriteAtlasExtensions.SetIncludeInBuild(atlas, true);

                AssetDatabase.CreateAsset(atlas, path);
            }

            EditorUtility.SetDirty(atlas);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (packAfterBuild)
                SpriteAtlasUtility.PackAtlases(new[] { atlas }, EditorUserBuildSettings.activeBuildTarget);

            EditorUtility.DisplayDialog("UI Atlas Builder",
                $"'{Path.GetFileName(path)}' {(isNew ? "생성" : "업데이트")} 완료\n등록된 스프라이트: {packables.Length}개", "OK");
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
