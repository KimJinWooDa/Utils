using System.Collections.Generic;
using System.Text;
using TelleR.ConcaveCollider;
using UnityEditor;
using UnityEngine;

namespace TelleR
{
    /// <summary>
    /// Concave Mesh Collider 컨텍스트 메뉴. 여러 오브젝트를 선택해 실행하면 Unity가 메뉴를 오브젝트마다 부르므로
    /// 한 프레임 동안 모았다가 한 번에(진행 막대 하나로) 처리한다. 생성은 마지막으로 쓴 설정(없으면 Auto)을,
    /// Rebuild는 각 마커에 기록된 설정을 쓴다.
    /// </summary>
    internal static class ConcaveColliderMenus
    {
        private const string GenerateLabel = "Generate Concave Collider";

        private static readonly List<GameObject> pendingGenerate = new List<GameObject>();
        private static readonly List<ConcaveColliderRoot> pendingRebuild = new List<ConcaveColliderRoot>();
        private static readonly List<GameObject> pendingRemove = new List<GameObject>();
        private static bool scheduled;

        [MenuItem("CONTEXT/MeshFilter/" + GenerateLabel)]
        private static void FromMeshFilter(MenuCommand command)
        {
            if (command.context is Component component) Queue(pendingGenerate, component.gameObject);
        }

        [MenuItem("CONTEXT/MeshFilter/" + GenerateLabel, true)]
        private static bool ValidateMeshFilter(MenuCommand command)
        {
            return command.context is MeshFilter filter && filter.sharedMesh != null;
        }

        [MenuItem("CONTEXT/SkinnedMeshRenderer/" + GenerateLabel)]
        private static void FromSkinnedMeshRenderer(MenuCommand command)
        {
            if (command.context is Component component) Queue(pendingGenerate, component.gameObject);
        }

        [MenuItem("CONTEXT/SkinnedMeshRenderer/" + GenerateLabel, true)]
        private static bool ValidateSkinnedMeshRenderer(MenuCommand command)
        {
            return command.context is SkinnedMeshRenderer renderer && renderer.sharedMesh != null;
        }

        [MenuItem("CONTEXT/ConcaveColliderRoot/Rebuild")]
        private static void RebuildRoot(MenuCommand command)
        {
            if (command.context is ConcaveColliderRoot root) Queue(pendingRebuild, root);
        }

        [MenuItem("CONTEXT/ConcaveColliderRoot/Remove")]
        private static void RemoveRoot(MenuCommand command)
        {
            if (command.context is ConcaveColliderRoot root) Queue(pendingRemove, root.gameObject);
        }

        [MenuItem("GameObject/TelleR/" + GenerateLabel, false, 49)]
        private static void FromGameObject(MenuCommand command)
        {
            if (command.context is GameObject go)
            {
                Queue(pendingGenerate, go);
                return;
            }

            GameObject[] selected = Selection.gameObjects;
            for (int i = 0; i < selected.Length; i++) Queue(pendingGenerate, selected[i]);
        }

        [MenuItem("GameObject/TelleR/" + GenerateLabel, true)]
        private static bool ValidateGameObject()
        {
            GameObject[] selected = Selection.gameObjects;
            for (int i = 0; i < selected.Length; i++)
            {
                if (HasMesh(selected[i])) return true;
            }

            return false;
        }

        /// <summary>대상이나 자식에 메시가 지정된 MeshFilter/SkinnedMeshRenderer가 있는지.</summary>
        internal static bool HasMesh(GameObject go)
        {
            if (go == null) return false;
            MeshFilter[] filters = go.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                if (filters[i].sharedMesh != null) return true;
            }

            SkinnedMeshRenderer[] skinned = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < skinned.Length; i++)
            {
                if (skinned[i].sharedMesh != null) return true;
            }

            return false;
        }

        private static void Queue<T>(List<T> list, T item) where T : Object
        {
            if (item == null || list.Contains(item)) return;
            list.Add(item);
            if (scheduled) return;
            scheduled = true;
            EditorApplication.delayCall += Flush;
        }

        private static void Flush()
        {
            scheduled = false;
            var generate = new List<GameObject>(pendingGenerate);
            var rebuild = new List<ConcaveColliderRoot>(pendingRebuild);
            var remove = new List<GameObject>(pendingRemove);
            pendingGenerate.Clear();
            pendingRebuild.Clear();
            pendingRemove.Clear();

            if (generate.Count > 0)
            {
                List<ConcaveTargetResult> results = ConcaveColliderGenerator.Generate(generate, ConcaveColliderPrefs.LoadSettings(), ConcaveColliderPrefs.LoadOptions());
                ReportFailures(results);
            }

            if (rebuild.Count > 0)
            {
                List<ConcaveTargetResult> results = ConcaveColliderGenerator.Rebuild(rebuild);
                ReportFailures(results);
            }

            if (remove.Count > 0) ConcaveColliderGenerator.RemoveGenerated(remove, ConcaveAssetRemoval.Ask);
        }

        /// <summary>실패한 대상이 있으면 대화상자로 알린다(자세한 내용은 Console).</summary>
        internal static void ReportFailures(List<ConcaveTargetResult> results)
        {
            if (Application.isBatchMode || results == null) return;
            int failed = 0;
            var builder = new StringBuilder();
            for (int i = 0; i < results.Count; i++)
            {
                ConcaveTargetResult result = results[i];
                if (result.Error == null) continue;
                failed++;
                if (failed <= 5) builder.Append("• ").Append(result.Name).Append(": ").Append(result.Error).Append('\n');
            }

            if (failed == 0) return;
            if (failed > 5) builder.Append("… 외 ").Append(failed - 5).Append("개\n");
            TelleRGUI.Info("콜라이더 생성 실패",
                $"{results.Count}개 대상 중 {failed}개를 만들지 못했습니다. 나머지는 정상 처리했습니다.\n\n{builder}\n자세한 내용은 Console을 확인하세요.");
        }
    }
}
