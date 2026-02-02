using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace TelleR
{
    public class SkinnedMeshColliderWindow : EditorWindow
    {
        private enum SimplificationPreset
        {
            VeryLow,
            Low,
            Medium,
            High,
            VeryHigh,
            Custom
        }

        private GameObject targetObject;
        private SkinnedMeshRenderer selectedRenderer;
        private List<SkinnedMeshRenderer> skinnedMeshRenderers = new List<SkinnedMeshRenderer>();
        private string saveFolderPath = "Assets/Mesh/ColliderMesh";
        private string meshName = "";
        private bool replaceExistingColliders = true;
        private bool makeConvex = true;
        private bool combineAllMeshes = false;
        private Vector2 scrollPos;

        private Mesh previewMesh;
        private Editor meshPreviewEditor;

        private bool enableSimplification = false;
        private SimplificationPreset selectedPreset = SimplificationPreset.Medium;
        private float quality = 0.5f;
        private int targetTriCount;
        private int originalTriCount;
        private bool preserveBorderEdges = false;
        private bool recalculateNormals = true;

        private readonly float[] presetValues = { 0.1f, 0.25f, 0.5f, 0.75f, 0.9f };

        private readonly string[] presetLabels =
        {
            "Very Low (90% 감소)",
            "Low (75% 감소)",
            "Medium (50% 감소)",
            "High (25% 감소)",
            "Very High (10% 감소)",
            "Custom (직접 설정)"
        };

        [MenuItem("Tools/TelleR/Tool/Skinned Mesh Collider Creator")]
        public static void ShowWindow()
        {
            var window = GetWindow<SkinnedMeshColliderWindow>("Skinned Mesh Collider");
            window.minSize = new Vector2(400, 650);
        }

        private void OnDisable()
        {
            ClearPreview();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Skinned Mesh Collider Creator", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("SkinnedMeshRenderer → MeshCollider 변환", EditorStyles.miniLabel);

            EditorGUILayout.Space(10);

            DrawTargetSelection();

            if (selectedRenderer != null || (combineAllMeshes && skinnedMeshRenderers.Count > 0))
            {
                EditorGUILayout.Space(10);
                DrawSimplificationSettings();

                EditorGUILayout.Space(10);
                DrawOutputSettings();

                EditorGUILayout.Space(10);
                DrawPreview();
            }
        }

        private void DrawTargetSelection()
        {
            EditorGUILayout.LabelField("대상 선택 (Target)", EditorStyles.boldLabel);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUI.BeginChangeCheck();
            selectedRenderer = (SkinnedMeshRenderer)EditorGUILayout.ObjectField(
                "SkinnedMeshRenderer",
                selectedRenderer,
                typeof(SkinnedMeshRenderer),
                true);

            if (EditorGUI.EndChangeCheck() && selectedRenderer != null)
            {
                targetObject = selectedRenderer.gameObject;
                meshName = $"{selectedRenderer.name}_Collider";
                combineAllMeshes = false;
                skinnedMeshRenderers.Clear();
                UpdateOriginalTriCount();
                UpdatePreview();
            }

            EditorGUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Scene 선택 사용", GUILayout.Height(24)))
            {
                if (Selection.activeGameObject != null)
                {
                    SetTargetFromGameObject(Selection.activeGameObject);
                }
                else
                {
                    EditorUtility.DisplayDialog("Error", "Scene에서 오브젝트를 선택해주세요.", "OK");
                }
            }

            if (GUILayout.Button("X", GUILayout.Width(28), GUILayout.Height(24)))
            {
                ClearTarget();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();

            if (skinnedMeshRenderers.Count > 0)
            {
                EditorGUILayout.Space(5);
                DrawMeshList();
            }

            if (selectedRenderer != null || combineAllMeshes)
            {
                DrawCompactInfo();
            }
        }

        private void DrawMeshList()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField($"발견된 메시 ({skinnedMeshRenderers.Count}개)", EditorStyles.miniBoldLabel);

            scrollPos = EditorGUILayout.BeginScrollView(scrollPos,
                GUILayout.Height(Mathf.Min(100, skinnedMeshRenderers.Count * 26)));

            for (int i = 0; i < skinnedMeshRenderers.Count; i++)
            {
                var smr = skinnedMeshRenderers[i];
                if (smr == null || smr.sharedMesh == null) continue;

                bool isSelected = (smr == selectedRenderer);

                EditorGUILayout.BeginHorizontal();

                GUI.backgroundColor = isSelected ? new Color(0.3f, 0.6f, 0.3f) : Color.white;

                if (GUILayout.Button(isSelected ? "V" : " ", GUILayout.Width(24)))
                {
                    selectedRenderer = smr;
                    meshName = $"{smr.name}_Collider";
                    combineAllMeshes = false;
                    UpdateOriginalTriCount();
                    UpdatePreview();
                }

                GUI.backgroundColor = Color.white;

                EditorGUILayout.LabelField(smr.name, GUILayout.MinWidth(100));
                EditorGUILayout.LabelField($"{smr.sharedMesh.triangles.Length / 3:N0} tris", EditorStyles.miniLabel,
                    GUILayout.Width(70));

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(3);

            EditorGUI.BeginChangeCheck();
            combineAllMeshes = EditorGUILayout.ToggleLeft("모든 메시 합치기 (Combine All)", combineAllMeshes);

            if (EditorGUI.EndChangeCheck())
            {
                if (combineAllMeshes)
                {
                    selectedRenderer = null;
                    meshName = $"{targetObject.name}_Combined_Collider";
                }

                UpdateOriginalTriCount();
                UpdatePreview();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawCompactInfo()
        {
            int verts = 0, tris = 0;

            if (selectedRenderer != null && selectedRenderer.sharedMesh != null)
            {
                verts = selectedRenderer.sharedMesh.vertexCount;
                tris = selectedRenderer.sharedMesh.triangles.Length / 3;
            }
            else if (combineAllMeshes)
            {
                foreach (var smr in skinnedMeshRenderers)
                {
                    if (smr != null && smr.sharedMesh != null)
                    {
                        verts += smr.sharedMesh.vertexCount;
                        tris += smr.sharedMesh.triangles.Length / 3;
                    }
                }
            }

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"원본: {verts:N0} vertices, {tris:N0} triangles", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSimplificationSettings()
        {
            EditorGUILayout.LabelField("메시 단순화 (Simplification)", EditorStyles.boldLabel);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUI.BeginChangeCheck();
            enableSimplification = EditorGUILayout.ToggleLeft("단순화 사용 (Enable)", enableSimplification);
            if (EditorGUI.EndChangeCheck())
            {
                if (enableSimplification)
                {
                    ApplyPreset(selectedPreset);
                }

                UpdatePreview();
            }

            if (enableSimplification && originalTriCount > 0)
            {
                EditorGUILayout.Space(8);

                EditorGUILayout.LabelField("품질 프리셋 (Quality Preset)", EditorStyles.miniBoldLabel);

                EditorGUI.BeginChangeCheck();
                selectedPreset = (SimplificationPreset)GUILayout.SelectionGrid(
                    (int)selectedPreset,
                    presetLabels,
                    2,
                    GUILayout.Height(75));

                if (EditorGUI.EndChangeCheck())
                {
                    ApplyPreset(selectedPreset);
                    UpdatePreview();
                }

                if (selectedPreset == SimplificationPreset.Custom)
                {
                    EditorGUILayout.Space(5);

                    EditorGUI.BeginChangeCheck();
                    quality = EditorGUILayout.Slider("품질 (Quality)", quality, 0.01f, 1f);
                    if (EditorGUI.EndChangeCheck())
                    {
                        targetTriCount = Mathf.Max(4, Mathf.RoundToInt(originalTriCount * quality));
                        UpdatePreview();
                    }
                }

                EditorGUILayout.Space(8);

                EditorGUI.BeginChangeCheck();
                recalculateNormals = EditorGUILayout.ToggleLeft("노말 재계산 (Recalculate Normals)", recalculateNormals);
                if (EditorGUI.EndChangeCheck())
                {
                    UpdatePreview();
                }

                EditorGUILayout.Space(8);
                DrawSimplificationResult();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawSimplificationResult()
        {
            if (previewMesh == null) return;

            int resultTris = previewMesh.triangles.Length / 3;
            float savedPercent = originalTriCount > 0 ? (1f - (float)resultTris / originalTriCount) * 100f : 0f;

            Color boxColor = savedPercent > 0 ? new Color(0.2f, 0.4f, 0.2f) : new Color(0.3f, 0.3f, 0.3f);

            Rect rect = EditorGUILayout.BeginVertical();
            EditorGUI.DrawRect(rect, boxColor);

            EditorGUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            string arrow = savedPercent > 0 ? "->" : "=";
            string resultText = $"{originalTriCount:N0} {arrow} {resultTris:N0} triangles";

            GUIStyle centerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12
            };

            EditorGUILayout.LabelField(resultText, centerStyle);

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (savedPercent > 0)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();

                GUIStyle savedStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = new Color(0.5f, 1f, 0.5f) },
                    fontSize = 14
                };

                EditorGUILayout.LabelField($"{savedPercent:F1}% 감소!", savedStyle);

                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.EndVertical();
        }

        private void ApplyPreset(SimplificationPreset preset)
        {
            if (preset == SimplificationPreset.Custom) return;

            quality = presetValues[(int)preset];
            targetTriCount = Mathf.Max(4, Mathf.RoundToInt(originalTriCount * quality));
        }

        private void DrawOutputSettings()
        {
            EditorGUILayout.LabelField("출력 설정 (Output)", EditorStyles.boldLabel);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            meshName = EditorGUILayout.TextField("파일명 (Name)", meshName);

            EditorGUILayout.BeginHorizontal();
            saveFolderPath = EditorGUILayout.TextField("저장 경로 (Path)", saveFolderPath);
            if (GUILayout.Button("...", GUILayout.Width(28)))
            {
                string selected = EditorUtility.OpenFolderPanel("Select Save Folder", "Assets", "");
                if (!string.IsNullOrEmpty(selected) && selected.StartsWith(Application.dataPath))
                {
                    saveFolderPath = "Assets" + selected.Substring(Application.dataPath.Length);
                }
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();
            replaceExistingColliders =
                EditorGUILayout.ToggleLeft("기존 콜라이더 교체", replaceExistingColliders, GUILayout.Width(140));
            makeConvex = EditorGUILayout.ToggleLeft("Convex (Rigidbody용)", makeConvex);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            GUI.backgroundColor = new Color(0.3f, 0.7f, 0.3f);
            if (GUILayout.Button("MeshCollider 생성", GUILayout.Height(32)))
            {
                CreateMeshCollider();
            }

            GUI.backgroundColor = Color.white;

            if (GUILayout.Button("메시만 저장 (Save Asset Only)"))
            {
                BakeMeshOnly();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawPreview()
        {
            EditorGUILayout.LabelField("미리보기 (Preview)", EditorStyles.boldLabel);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (previewMesh != null)
            {
                if (meshPreviewEditor == null)
                {
                    meshPreviewEditor = Editor.CreateEditor(previewMesh);
                }

                if (meshPreviewEditor != null)
                {
                    meshPreviewEditor.OnInteractivePreviewGUI(
                        GUILayoutUtility.GetRect(200, 180),
                        EditorStyles.helpBox);
                }

                EditorGUILayout.LabelField(
                    $"{previewMesh.vertexCount:N0} verts, {previewMesh.triangles.Length / 3:N0} tris",
                    EditorStyles.centeredGreyMiniLabel);
            }
            else
            {
                EditorGUILayout.HelpBox("대상을 선택하면 미리보기가 표시됩니다.", MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private void UpdateOriginalTriCount()
        {
            if (combineAllMeshes)
            {
                originalTriCount = 0;
                foreach (var smr in skinnedMeshRenderers)
                {
                    if (smr != null && smr.sharedMesh != null)
                    {
                        originalTriCount += smr.sharedMesh.triangles.Length / 3;
                    }
                }
            }
            else if (selectedRenderer != null && selectedRenderer.sharedMesh != null)
            {
                originalTriCount = selectedRenderer.sharedMesh.triangles.Length / 3;
            }
            else
            {
                originalTriCount = 0;
            }

            targetTriCount = Mathf.Max(4, Mathf.RoundToInt(originalTriCount * quality));
        }

        private void UpdatePreview()
        {
            ClearPreview();

            Mesh bakedMesh = null;

            if (combineAllMeshes && skinnedMeshRenderers.Count > 0)
            {
                bakedMesh = BakeAllMeshesInternal();
            }
            else if (selectedRenderer != null && selectedRenderer.sharedMesh != null)
            {
                bakedMesh = new Mesh();
                bakedMesh.name = "Preview";
                selectedRenderer.BakeMesh(bakedMesh);
                bakedMesh.RecalculateBounds();
                bakedMesh.RecalculateNormals();
            }

            if (bakedMesh != null)
            {
                int bakedTriCount = bakedMesh.triangles.Length / 3;

                if (enableSimplification && targetTriCount < bakedTriCount)
                {
                    previewMesh = SimplifyMesh(bakedMesh, targetTriCount);
                    DestroyImmediate(bakedMesh);
                }
                else
                {
                    previewMesh = bakedMesh;
                }
            }

            if (previewMesh != null)
            {
                meshPreviewEditor = Editor.CreateEditor(previewMesh);
            }

            Repaint();
        }

        private Mesh BakeAllMeshesInternal()
        {
            List<CombineInstance> combineInstances = new List<CombineInstance>();
            Transform rootTransform =
                targetObject != null ? targetObject.transform : skinnedMeshRenderers[0].transform.root;

            foreach (var smr in skinnedMeshRenderers)
            {
                if (smr == null || smr.sharedMesh == null) continue;

                Mesh bakedMesh = new Mesh();
                smr.BakeMesh(bakedMesh);

                CombineInstance ci = new CombineInstance();
                ci.mesh = bakedMesh;
                ci.transform = rootTransform.worldToLocalMatrix * smr.transform.localToWorldMatrix;
                combineInstances.Add(ci);
            }

            if (combineInstances.Count == 0) return null;

            Mesh combinedMesh = new Mesh();
            combinedMesh.name = "Preview_Combined";
            combinedMesh.CombineMeshes(combineInstances.ToArray(), true, true);
            combinedMesh.RecalculateBounds();
            combinedMesh.RecalculateNormals();

            foreach (var ci in combineInstances)
            {
                DestroyImmediate(ci.mesh);
            }

            return combinedMesh;
        }

        private Mesh SimplifyMesh(Mesh original, int targetTris)
        {
            if (original == null) return null;

            Vector3[] vertices = original.vertices;
            int[] triangles = original.triangles;
            Vector3[] normals = original.normals;
            Vector2[] uvs = original.uv;

            int currentTriCount = triangles.Length / 3;

            if (targetTris >= currentTriCount)
            {
                Mesh copy = new Mesh();
                copy.name = "Simplified";
                copy.vertices = vertices;
                copy.triangles = triangles;
                copy.normals = normals;
                copy.uv = uvs;
                copy.RecalculateBounds();
                return copy;
            }

            var simplifier = new MeshDecimator(vertices, triangles, normals, uvs);
            simplifier.Simplify(targetTris);

            Mesh result = new Mesh();
            result.name = "Simplified";
            result.vertices = simplifier.GetVertices();
            result.triangles = simplifier.GetTriangles();

            if (recalculateNormals)
            {
                result.RecalculateNormals();
            }
            else if (simplifier.GetNormals() != null)
            {
                result.normals = simplifier.GetNormals();
            }

            if (simplifier.GetUVs() != null)
            {
                result.uv = simplifier.GetUVs();
            }

            result.RecalculateBounds();
            result.RecalculateTangents();

            return result;
        }

        private void ClearPreview()
        {
            if (meshPreviewEditor != null)
            {
                DestroyImmediate(meshPreviewEditor);
                meshPreviewEditor = null;
            }

            if (previewMesh != null)
            {
                DestroyImmediate(previewMesh);
                previewMesh = null;
            }
        }

        private void SetTargetFromGameObject(GameObject go)
        {
            targetObject = go;
            skinnedMeshRenderers.Clear();
            selectedRenderer = null;

            var renderers = go.GetComponentsInChildren<SkinnedMeshRenderer>();

            foreach (var renderer in renderers)
            {
                if (renderer.sharedMesh != null)
                {
                    skinnedMeshRenderers.Add(renderer);
                }
            }

            if (skinnedMeshRenderers.Count == 0)
            {
                EditorUtility.DisplayDialog("Warning", "SkinnedMeshRenderer를 찾을 수 없습니다.", "OK");
                ClearTarget();
                return;
            }

            int maxVertices = 0;
            foreach (var smr in skinnedMeshRenderers)
            {
                if (smr.sharedMesh.vertexCount > maxVertices)
                {
                    maxVertices = smr.sharedMesh.vertexCount;
                    selectedRenderer = smr;
                }
            }

            if (selectedRenderer != null)
            {
                meshName = $"{selectedRenderer.name}_Collider";
            }

            UpdateOriginalTriCount();
            UpdatePreview();
        }

        private void ClearTarget()
        {
            targetObject = null;
            selectedRenderer = null;
            skinnedMeshRenderers.Clear();
            meshName = "";
            combineAllMeshes = false;
            originalTriCount = 0;
            ClearPreview();
        }

        private void CreateMeshCollider()
        {
            if (selectedRenderer == null && !combineAllMeshes)
            {
                EditorUtility.DisplayDialog("Error", "SkinnedMeshRenderer를 선택해주세요.", "OK");
                return;
            }

            if (string.IsNullOrEmpty(meshName))
            {
                EditorUtility.DisplayDialog("Error", "파일명을 입력해주세요.", "OK");
                return;
            }

            try
            {
                Mesh bakedMesh = combineAllMeshes ? BakeAllMeshes() : BakeSingleMesh();

                if (bakedMesh == null) return;

                GameObject colliderTarget = targetObject;
                if (colliderTarget == null && selectedRenderer != null)
                {
                    colliderTarget = selectedRenderer.transform.root.gameObject;
                }

                if (colliderTarget == null)
                {
                    EditorUtility.DisplayDialog("Error", "Collider를 추가할 대상이 없습니다.", "OK");
                    return;
                }

                if (replaceExistingColliders)
                {
                    var existingColliders = colliderTarget.GetComponents<Collider>();
                    foreach (var col in existingColliders)
                    {
                        Undo.DestroyObjectImmediate(col);
                    }
                }

                var meshCollider = Undo.AddComponent<MeshCollider>(colliderTarget);
                meshCollider.sharedMesh = bakedMesh;
                meshCollider.convex = makeConvex;

                EditorUtility.SetDirty(colliderTarget);

                EditorUtility.DisplayDialog(
                    "완료!",
                    $"MeshCollider 생성 완료\n\n" +
                    $"대상: {colliderTarget.name}\n" +
                    $"Triangles: {bakedMesh.triangles.Length / 3:N0}",
                    "OK");
            }
            catch (System.Exception e)
            {
                EditorUtility.DisplayDialog("Error", $"실패:\n{e.Message}", "OK");
                Debug.LogError($"[SkinnedMeshCollider] {e}");
            }
        }

        private void BakeMeshOnly()
        {
            if (selectedRenderer == null && !combineAllMeshes)
            {
                EditorUtility.DisplayDialog("Error", "SkinnedMeshRenderer를 선택해주세요.", "OK");
                return;
            }

            Mesh bakedMesh = combineAllMeshes ? BakeAllMeshes() : BakeSingleMesh();

            if (bakedMesh != null)
            {
                Selection.activeObject = bakedMesh;
                EditorGUIUtility.PingObject(bakedMesh);

                EditorUtility.DisplayDialog(
                    "완료!",
                    $"메시 저장 완료\n\n" +
                    $"경로: {saveFolderPath}/{meshName}.asset\n" +
                    $"Triangles: {bakedMesh.triangles.Length / 3:N0}",
                    "OK");
            }
        }

        private Mesh BakeSingleMesh()
        {
            if (selectedRenderer == null || selectedRenderer.sharedMesh == null)
            {
                EditorUtility.DisplayDialog("Error", "선택된 메시가 없습니다.", "OK");
                return null;
            }

            Mesh bakedMesh = new Mesh();
            bakedMesh.name = meshName;
            selectedRenderer.BakeMesh(bakedMesh);
            bakedMesh.RecalculateBounds();
            bakedMesh.RecalculateNormals();

            if (enableSimplification && targetTriCount < bakedMesh.triangles.Length / 3)
            {
                Mesh simplified = SimplifyMesh(bakedMesh, targetTriCount);
                simplified.name = meshName;
                DestroyImmediate(bakedMesh);
                bakedMesh = simplified;
            }

            return SaveMesh(bakedMesh);
        }

        private Mesh BakeAllMeshes()
        {
            if (skinnedMeshRenderers.Count == 0)
            {
                EditorUtility.DisplayDialog("Error", "합칠 메시가 없습니다.", "OK");
                return null;
            }

            Mesh combinedMesh = BakeAllMeshesInternal();
            if (combinedMesh == null)
            {
                EditorUtility.DisplayDialog("Error", "합칠 메시가 없습니다.", "OK");
                return null;
            }

            combinedMesh.name = meshName;

            if (enableSimplification && targetTriCount < combinedMesh.triangles.Length / 3)
            {
                Mesh simplified = SimplifyMesh(combinedMesh, targetTriCount);
                simplified.name = meshName;
                DestroyImmediate(combinedMesh);
                combinedMesh = simplified;
            }

            return SaveMesh(combinedMesh);
        }

        private Mesh SaveMesh(Mesh mesh)
        {
            if (!Directory.Exists(saveFolderPath))
            {
                Directory.CreateDirectory(saveFolderPath);
                AssetDatabase.Refresh();
            }

            string meshPath = $"{saveFolderPath}/{meshName}.asset";

            var existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (existingMesh != null)
            {
                if (!EditorUtility.DisplayDialog(
                        "덮어쓰기?",
                        $"'{meshName}.asset' 파일이 이미 존재합니다.\n덮어쓰시겠습니까?",
                        "예", "아니오"))
                {
                    return null;
                }

                AssetDatabase.DeleteAsset(meshPath);
            }

            AssetDatabase.CreateAsset(mesh, meshPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        }
    }

    public class MeshDecimator
    {
        private List<Vector3> vertices;
        private List<int> triangles;
        private List<Vector3> normals;
        private List<Vector2> uvs;

        private List<List<int>> vertexTriangles;
        private List<List<int>> vertexNeighbors;
        private bool[] removedTriangles;
        private int[] vertexRemap;
        private bool[] removedVertices;
        private int activeTriangleCount;

        private SortedSet<EdgeCost> edgeQueue;
        private Dictionary<long, float> edgeCostCache;

        private struct EdgeCost : IComparable<EdgeCost>
        {
            public float Cost;
            public int V1;
            public int V2;
            public int Version;

            public int CompareTo(EdgeCost other)
            {
                int costCompare = Cost.CompareTo(other.Cost);
                if (costCompare != 0) return costCompare;

                int v1Compare = V1.CompareTo(other.V1);
                if (v1Compare != 0) return v1Compare;

                return V2.CompareTo(other.V2);
            }
        }

        private int[] vertexVersion;

        public MeshDecimator(Vector3[] verts, int[] tris, Vector3[] norms, Vector2[] uv)
        {
            vertices = new List<Vector3>(verts);
            triangles = new List<int>(tris);
            normals = norms != null && norms.Length == verts.Length ? new List<Vector3>(norms) : null;
            uvs = uv != null && uv.Length == verts.Length ? new List<Vector2>(uv) : null;

            removedTriangles = new bool[tris.Length / 3];
            removedVertices = new bool[verts.Length];
            vertexRemap = new int[verts.Length];
            vertexVersion = new int[verts.Length];
            activeTriangleCount = tris.Length / 3;

            for (int i = 0; i < vertexRemap.Length; i++)
            {
                vertexRemap[i] = i;
            }

            BuildConnectivity();
            BuildEdgeQueue();
        }

        private void BuildConnectivity()
        {
            vertexTriangles = new List<List<int>>();
            vertexNeighbors = new List<List<int>>();

            for (int i = 0; i < vertices.Count; i++)
            {
                vertexTriangles.Add(new List<int>());
                vertexNeighbors.Add(new List<int>());
            }

            int triCount = triangles.Count / 3;

            for (int t = 0; t < triCount; t++)
            {
                int i0 = triangles[t * 3];
                int i1 = triangles[t * 3 + 1];
                int i2 = triangles[t * 3 + 2];

                vertexTriangles[i0].Add(t);
                vertexTriangles[i1].Add(t);
                vertexTriangles[i2].Add(t);

                AddNeighbor(i0, i1);
                AddNeighbor(i0, i2);
                AddNeighbor(i1, i0);
                AddNeighbor(i1, i2);
                AddNeighbor(i2, i0);
                AddNeighbor(i2, i1);
            }
        }

        private void AddNeighbor(int v1, int v2)
        {
            if (!vertexNeighbors[v1].Contains(v2))
            {
                vertexNeighbors[v1].Add(v2);
            }
        }

        private void BuildEdgeQueue()
        {
            edgeQueue = new SortedSet<EdgeCost>();
            edgeCostCache = new Dictionary<long, float>();

            for (int v = 0; v < vertices.Count; v++)
            {
                foreach (int neighbor in vertexNeighbors[v])
                {
                    if (v < neighbor)
                    {
                        float cost = CalculateEdgeCost(v, neighbor);
                        edgeQueue.Add(new EdgeCost { Cost = cost, V1 = v, V2 = neighbor, Version = 0 });
                        edgeCostCache[GetEdgeKey(v, neighbor)] = cost;
                    }
                }
            }
        }

        private long GetEdgeKey(int v1, int v2)
        {
            if (v1 > v2) (v1, v2) = (v2, v1);
            return ((long)v1 << 32) | (uint)v2;
        }

        public void Simplify(int targetTriCount)
        {
            while (activeTriangleCount > targetTriCount && edgeQueue.Count > 0)
            {
                EdgeCost best = edgeQueue.Min;
                edgeQueue.Remove(best);

                int v1 = GetRemap(best.V1);
                int v2 = GetRemap(best.V2);

                if (v1 == v2 || removedVertices[v1] || removedVertices[v2])
                    continue;

                if (best.Version < vertexVersion[best.V1] || best.Version < vertexVersion[best.V2])
                    continue;

                CollapseEdge(v1, v2);
            }
        }

        private float CalculateEdgeCost(int v1, int v2)
        {
            float distance = Vector3.Distance(vertices[v1], vertices[v2]);

            int sharedTris = 0;
            foreach (int t in vertexTriangles[v1])
            {
                if (removedTriangles[t]) continue;

                int i0 = GetRemap(triangles[t * 3]);
                int i1 = GetRemap(triangles[t * 3 + 1]);
                int i2 = GetRemap(triangles[t * 3 + 2]);

                if (i0 == v2 || i1 == v2 || i2 == v2)
                {
                    sharedTris++;
                }
            }

            float curvature = 0f;
            if (normals != null && v1 < normals.Count && v2 < normals.Count)
            {
                Vector3 n1 = normals[v1];
                Vector3 n2 = normals[v2];
                if (n1.sqrMagnitude > 0 && n2.sqrMagnitude > 0)
                {
                    curvature = 1f - Vector3.Dot(n1.normalized, n2.normalized);
                }
            }

            return distance * (1f + curvature * 2f) * (1f + 1f / (sharedTris + 1));
        }

        private void CollapseEdge(int v1, int v2)
        {
            Vector3 newPos = (vertices[v1] + vertices[v2]) * 0.5f;
            vertices[v1] = newPos;

            if (normals != null && v1 < normals.Count && v2 < normals.Count)
            {
                normals[v1] = (normals[v1] + normals[v2]).normalized;
            }

            if (uvs != null && v1 < uvs.Count && v2 < uvs.Count)
            {
                uvs[v1] = (uvs[v1] + uvs[v2]) * 0.5f;
            }

            vertexRemap[v2] = v1;
            removedVertices[v2] = true;

            foreach (int t in vertexTriangles[v2])
            {
                if (removedTriangles[t]) continue;

                int i0 = GetRemap(triangles[t * 3]);
                int i1 = GetRemap(triangles[t * 3 + 1]);
                int i2 = GetRemap(triangles[t * 3 + 2]);

                if (i0 == i1 || i1 == i2 || i2 == i0)
                {
                    removedTriangles[t] = true;
                    activeTriangleCount--;
                }
                else
                {
                    if (!vertexTriangles[v1].Contains(t))
                    {
                        vertexTriangles[v1].Add(t);
                    }
                }
            }

            HashSet<int> affectedVertices = new HashSet<int>();

            foreach (int n in vertexNeighbors[v2])
            {
                int rn = GetRemap(n);
                if (rn != v1)
                {
                    if (!vertexNeighbors[v1].Contains(rn))
                    {
                        vertexNeighbors[v1].Add(rn);
                    }

                    affectedVertices.Add(rn);
                }
            }

            foreach (int n in vertexNeighbors[v1])
            {
                affectedVertices.Add(GetRemap(n));
            }

            vertexVersion[v1]++;

            foreach (int affected in affectedVertices)
            {
                if (affected == v1 || removedVertices[affected]) continue;

                int a = v1 < affected ? v1 : affected;
                int b = v1 < affected ? affected : v1;

                float cost = CalculateEdgeCost(a, b);
                edgeQueue.Add(new EdgeCost
                {
                    Cost = cost,
                    V1 = a,
                    V2 = b,
                    Version = vertexVersion[a]
                });
            }
        }

        private int GetRemap(int v)
        {
            if (vertexRemap[v] == v) return v;

            int root = v;
            while (vertexRemap[root] != root)
            {
                root = vertexRemap[root];
            }

            while (vertexRemap[v] != root)
            {
                int next = vertexRemap[v];
                vertexRemap[v] = root;
                v = next;
            }

            return root;
        }

        public Vector3[] GetVertices()
        {
            Dictionary<int, int> vertexMap = new Dictionary<int, int>();
            List<Vector3> resultVerts = new List<Vector3>();

            int triCount = triangles.Count / 3;

            for (int t = 0; t < triCount; t++)
            {
                if (removedTriangles[t]) continue;

                for (int i = 0; i < 3; i++)
                {
                    int oldIdx = GetRemap(triangles[t * 3 + i]);

                    if (!vertexMap.ContainsKey(oldIdx))
                    {
                        vertexMap[oldIdx] = resultVerts.Count;
                        resultVerts.Add(vertices[oldIdx]);
                    }
                }
            }

            return resultVerts.ToArray();
        }

        public int[] GetTriangles()
        {
            Dictionary<int, int> vertexMap = new Dictionary<int, int>();
            List<Vector3> resultVerts = new List<Vector3>();
            List<int> resultTris = new List<int>();

            int triCount = triangles.Count / 3;

            for (int t = 0; t < triCount; t++)
            {
                if (removedTriangles[t]) continue;

                for (int i = 0; i < 3; i++)
                {
                    int oldIdx = GetRemap(triangles[t * 3 + i]);

                    if (!vertexMap.ContainsKey(oldIdx))
                    {
                        vertexMap[oldIdx] = resultVerts.Count;
                        resultVerts.Add(vertices[oldIdx]);
                    }

                    resultTris.Add(vertexMap[oldIdx]);
                }
            }

            return resultTris.ToArray();
        }

        public Vector3[] GetNormals()
        {
            if (normals == null) return null;

            Dictionary<int, int> vertexMap = new Dictionary<int, int>();
            List<Vector3> resultNormals = new List<Vector3>();

            int triCount = triangles.Count / 3;

            for (int t = 0; t < triCount; t++)
            {
                if (removedTriangles[t]) continue;

                for (int i = 0; i < 3; i++)
                {
                    int oldIdx = GetRemap(triangles[t * 3 + i]);

                    if (!vertexMap.ContainsKey(oldIdx))
                    {
                        vertexMap[oldIdx] = resultNormals.Count;
                        resultNormals.Add(normals[oldIdx]);
                    }
                }
            }

            return resultNormals.ToArray();
        }

        public Vector2[] GetUVs()
        {
            if (uvs == null) return null;

            Dictionary<int, int> vertexMap = new Dictionary<int, int>();
            List<Vector2> resultUVs = new List<Vector2>();

            int triCount = triangles.Count / 3;

            for (int t = 0; t < triCount; t++)
            {
                if (removedTriangles[t]) continue;

                for (int i = 0; i < 3; i++)
                {
                    int oldIdx = GetRemap(triangles[t * 3 + i]);

                    if (!vertexMap.ContainsKey(oldIdx))
                    {
                        vertexMap[oldIdx] = resultUVs.Count;
                        resultUVs.Add(uvs[oldIdx]);
                    }
                }
            }

            return resultUVs.ToArray();
        }
    }
}