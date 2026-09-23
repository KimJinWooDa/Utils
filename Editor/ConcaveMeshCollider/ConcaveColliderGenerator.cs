using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace TelleR.ConcaveCollider
{
    /// <summary>Remove Generated 뒤 헐 메시 에셋 처리 방식.</summary>
    public enum ConcaveAssetRemoval
    {
        /// <summary>대화상자로 묻는다(배치 모드에서는 유지).</summary>
        Ask,
        Keep,

        /// <summary>다른 곳에서 쓰지 않으면 휴지통으로 옮긴다.</summary>
        Delete
    }

    /// <summary>씬에 놓일 조각 하나와 그 부모(정적 = 대상 루트, 스킨드 = 본).</summary>
    public sealed class ConcavePlacedPiece
    {
        public ConcavePiece Piece;

        /// <summary>정적 조각은 대상 루트(실제로는 그 아래 컨테이너), 본 조각은 해당 본.</summary>
        public Transform Parent;

        public bool IsBonePiece;
    }

    /// <summary>창 목록에 보여줄 대상 요약.</summary>
    public sealed class ConcaveTargetInfo
    {
        public GameObject Target;
        public int StaticMeshCount;
        public int SkinnedMeshCount;
        public int RigidPartCount;

        /// <summary>본 가중치를 읽을 수 없어(Read/Write 꺼짐 등) 건너뛸 스킨드 메시 수.</summary>
        public int UnreadableSkinnedCount;

        public int TriangleCount;

        /// <summary>켜져 있는 기존 콜라이더 수(Trigger·이 도구가 만든 것 제외).</summary>
        public int ExistingColliderCount;

        public bool Generated;
        public bool NonUniformScale;
        public bool IsPrefabAsset;
        public string Summary = string.Empty;

        public bool HasMesh => StaticMeshCount + SkinnedMeshCount + RigidPartCount + UnreadableSkinnedCount > 0;
    }

    /// <summary>대상 하나의 계산(및 적용) 결과.</summary>
    public sealed class ConcaveTargetResult
    {
        public GameObject Target;
        public string Name = string.Empty;

        /// <summary>Generate가 씬에 반영했는지.</summary>
        public bool Applied;

        public bool Cancelled;

        /// <summary>실패 사유(한국어). 성공이면 null.</summary>
        public string Error;

        public string ErrorDetail;
        public readonly List<string> Warnings = new List<string>();
        public readonly List<ConcavePlacedPiece> Pieces = new List<ConcavePlacedPiece>();
        public ConcaveDecompositionResult StaticResult;
        public ConcaveDecompositionResult SkinnedResult;
        public int SourceMeshCount;
        public int SourceTriangleCount;
        public double Seconds;
        public string HullAssetPath = string.Empty;

        public bool Succeeded => Error == null && !Cancelled && Pieces.Count > 0;

        public int CountOf(ConcavePieceKind kind)
        {
            int count = 0;
            for (int i = 0; i < Pieces.Count; i++)
            {
                if (Pieces[i].Piece.Kind == kind) count++;
            }

            return count;
        }

        public int PrimitiveCount => Pieces.Count - CountOf(ConcavePieceKind.ConvexMesh);

        public int BonePieceCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Pieces.Count; i++)
                {
                    if (Pieces[i].IsBonePiece) count++;
                }

                return count;
            }
        }

        public int TotalHullVertices
        {
            get
            {
                int total = 0;
                for (int i = 0; i < Pieces.Count; i++)
                {
                    if (Pieces[i].Piece.Kind == ConcavePieceKind.ConvexMesh) total += Pieces[i].Piece.VertexCount;
                }

                return total;
            }
        }

        /// <summary>정적/스킨드 중 더 낮은 커버리지(0~1). 측정값이 없으면 -1.</summary>
        public float Coverage
        {
            get
            {
                float worst = -1f;
                Worst(StaticResult);
                Worst(SkinnedResult);
                return worst;

                void Worst(ConcaveDecompositionResult result)
                {
                    if (result == null || result.Coverage < 0f) return;
                    worst = worst < 0f ? result.Coverage : Mathf.Min(worst, result.Coverage);
                }
            }
        }

        /// <summary>정적/스킨드 중 더 큰 바깥 비율(0~1). 측정값이 없으면 -1.</summary>
        public float OutsideFraction
        {
            get
            {
                float worst = -1f;
                if (StaticResult != null && StaticResult.OutsideFraction >= 0f) worst = StaticResult.OutsideFraction;
                if (SkinnedResult != null && SkinnedResult.OutsideFraction >= 0f) worst = Mathf.Max(worst, SkinnedResult.OutsideFraction);
                return worst;
            }
        }

        public bool MetricsApproximate =>
            (StaticResult != null && StaticResult.MetricsApproximate) || (SkinnedResult != null && SkinnedResult.MetricsApproximate);
    }

    /// <summary>
    /// Concave Mesh Collider 생성기. 대상 GameObject(여러 개)의 MeshFilter/SkinnedMeshRenderer를 읽어 코어로 분해하고,
    /// 결과를 "Concave Colliders" 컨테이너(정적)와 본 아래 조각(스킨드)으로 씬에 적용한다.
    /// 대상마다 예외를 따로 잡아 한 대상이 실패해도 나머지는 계속 처리하고, 대상마다 Undo 그룹 하나로 묶는다.
    /// 모두 메인 스레드에서 동기로 돌며 진행 막대에서 취소할 수 있다.
    /// </summary>
    public static class ConcaveColliderGenerator
    {
        public const string LogPrefix = "[TelleR/ConcaveMeshCollider] ";
        private const string ProgressTitle = "Concave Mesh Collider";
        private const string UndoGenerate = "Generate Concave Colliders";
        private const string UndoRemove = "Remove Concave Colliders";
        private const string TempOwnerPrefix = "tmp:";
        private const string HullMeshPrefix = "Hull_";

        // ─── 공개 진입점 ───

        /// <summary>계산만 한다(씬·에셋 변경 없음). 창의 Preview가 쓴다.</summary>
        public static List<ConcaveTargetResult> Preview(IList<GameObject> targets, ConcaveColliderSettings settings)
        {
            return RunJobs(MakeJobs(targets, settings, null), false);
        }

        /// <summary>계산하고 씬에 적용한다. 대상마다 Undo 그룹 하나.</summary>
        public static List<ConcaveTargetResult> Generate(IList<GameObject> targets, ConcaveColliderSettings settings, ConcaveColliderOutputOptions options)
        {
            return RunJobs(MakeJobs(targets, settings, options ?? ConcaveColliderPrefs.LoadOptions()), true);
        }

        /// <summary>마커마다 그 마커에 기록된 설정(없으면 마지막 사용 설정)으로 다시 만든다.</summary>
        public static List<ConcaveTargetResult> Rebuild(IList<ConcaveColliderRoot> roots)
        {
            var jobs = new List<Job>();
            var seen = new HashSet<GameObject>();
            if (roots != null)
            {
                for (int i = 0; i < roots.Count; i++)
                {
                    ConcaveColliderRoot root = roots[i];
                    if (root == null || !seen.Add(root.gameObject)) continue;
                    jobs.Add(new Job
                    {
                        Target = root.gameObject,
                        Settings = ConcaveColliderPrefs.SettingsFromRoot(root),
                        Options = ConcaveColliderPrefs.OptionsFromRoot(root)
                    });
                }
            }

            return RunJobs(jobs, true);
        }

        /// <summary>
        /// 생성된 컨테이너·본 조각을 지우고(Undo 가능), 끈 기존 콜라이더를 다시 켜고, 마커를 제거한다.
        /// 헐 메시 에셋은 assetRemoval에 따라 유지하거나 휴지통으로 옮긴다. 제거한 대상 수를 돌려준다.
        /// </summary>
        public static int RemoveGenerated(IList<GameObject> targets, ConcaveAssetRemoval assetRemoval)
        {
            if (targets == null) return 0;
            int removed = 0;
            var assetPaths = new List<string>();
            var removedContainers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<GameObject>();
            for (int i = 0; i < targets.Count; i++)
            {
                GameObject target = targets[i];
                if (target == null || !seen.Add(target)) continue;
                ConcaveColliderRoot marker = target.GetComponent<ConcaveColliderRoot>();
                if (marker == null) continue;
                if (EditorUtility.IsPersistent(target))
                {
                    Debug.LogError(LogPrefix + PrefabAssetMessage(target), target);
                    continue;
                }

                string blocked = FindPrefabOwnedGenerated(marker);
                if (blocked != null)
                {
                    Debug.LogError(LogPrefix + $"'{target.name}': {blocked}", target);
                    continue;
                }

                string assetPath = CurrentHullAssetPath(marker);
                bool assetShared = !string.IsNullOrEmpty(assetPath) && IsSharedWithPrefabSource(marker, assetPath);
                string containerPath = OwnerContainerPath(target);

                Undo.IncrementCurrentGroup();
                int group = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName(UndoRemove);
                try
                {
                    IReadOnlyList<Collider> disabled = marker.DisabledColliders;
                    for (int d = 0; d < disabled.Count; d++)
                    {
                        Collider source = disabled[d];
                        if (source == null || source.enabled) continue;
                        Undo.RecordObject(source, UndoRemove);
                        source.enabled = true;
                        RecordPrefabOverride(source);
                    }

                    IReadOnlyList<GameObject> bonePieces = marker.BonePieces;
                    var bones = new List<GameObject>(bonePieces.Count);
                    for (int b = 0; b < bonePieces.Count; b++)
                    {
                        if (bonePieces[b] != null) bones.Add(bonePieces[b]);
                    }

                    for (int b = 0; b < bones.Count; b++)
                    {
                        if (bones[b] != null) Undo.DestroyObjectImmediate(bones[b]);
                    }

                    if (marker.Container != null) Undo.DestroyObjectImmediate(marker.Container);
                    Undo.DestroyObjectImmediate(marker);
                    MarkDirty(target);
                    Undo.CollapseUndoOperations(group);
                    removed++;
                    Debug.Log(LogPrefix + $"'{target.name}'의 생성된 콜라이더를 제거했습니다.", target);
                    if (!string.IsNullOrEmpty(assetPath) && !assetShared && !assetPaths.Contains(assetPath)) assetPaths.Add(assetPath);
                    if (!string.IsNullOrEmpty(containerPath)) removedContainers.Add(containerPath);
                }
                catch (Exception exception)
                {
                    Undo.RevertAllDownToGroup(group);
                    Debug.LogError(LogPrefix + $"'{target.name}' 콜라이더 제거 실패: {exception.Message}\n{exception}", target);
                }
            }

            if (assetPaths.Count > 0)
            {
                try
                {
                    HandleAssetRemoval(assetPaths, removedContainers, assetRemoval);
                }
                finally
                {
                    referrerIndex = null;
                }
            }

            if (removed > 0) SceneView.RepaintAll();
            return removed;
        }

        /// <summary>창 목록용 대상 요약(읽기 전용).</summary>
        public static ConcaveTargetInfo Inspect(GameObject target)
        {
            var info = new ConcaveTargetInfo { Target = target };
            if (target == null)
            {
                info.Summary = "없음";
                return info;
            }

            if (EditorUtility.IsPersistent(target))
            {
                info.IsPrefabAsset = true;
                info.Summary = "Project 창의 프리팹 에셋";
                return info;
            }

            SourceSet sources = CollectSources(target, null);
            info.StaticMeshCount = sources.Static.Count;
            info.SkinnedMeshCount = sources.Skinned.Count;
            info.RigidPartCount = sources.Rigid.Count;
            info.UnreadableSkinnedCount = sources.Skipped.Count;
            int triangles = 0;
            for (int i = 0; i < sources.Static.Count; i++) triangles += TriangleCount(sources.Static[i].Mesh);
            for (int i = 0; i < sources.Skinned.Count; i++) triangles += TriangleCount(sources.Skinned[i].sharedMesh);
            for (int i = 0; i < sources.Rigid.Count; i++) triangles += TriangleCount(sources.Rigid[i].Mesh);
            info.TriangleCount = triangles;

            List<Collider> existing = FindExistingColliders(target);
            for (int i = 0; i < existing.Count; i++)
            {
                if (existing[i].enabled) info.ExistingColliderCount++;
            }

            ConcaveColliderRoot marker = target.GetComponent<ConcaveColliderRoot>();
            info.Generated = marker != null && marker.HasGenerated;
            info.NonUniformScale = ConcaveMeshInput.HasNonUniformScale(target.transform.lossyScale);

            var builder = new StringBuilder();
            if (info.StaticMeshCount > 0) builder.Append("Mesh ").Append(info.StaticMeshCount);
            if (info.SkinnedMeshCount > 0)
            {
                if (builder.Length > 0) builder.Append(" · ");
                builder.Append("Skinned ").Append(info.SkinnedMeshCount);
            }

            if (info.RigidPartCount > 0)
            {
                if (builder.Length > 0) builder.Append(" · ");
                builder.Append("Bone Mesh ").Append(info.RigidPartCount);
            }

            if (info.UnreadableSkinnedCount > 0)
            {
                if (builder.Length > 0) builder.Append(" · ");
                builder.Append("Skinned ").Append(info.UnreadableSkinnedCount).Append(" (Read/Write 필요)");
            }

            if (builder.Length > 0) builder.Append(" · ").Append(triangles.ToString("N0", CultureInfo.InvariantCulture)).Append(" tris");
            info.Summary = builder.Length > 0 ? builder.ToString() : "메시 없음";
            return info;
        }

        /// <summary>
        /// 대상이 가진 기존 콜라이더(Trigger 제외). 이 도구가 만든 조각, 자기 Rigidbody를 가진 자식, 다른 대상(마커)의 하위,
        /// 비활성 자식은 제외한다.
        /// </summary>
        public static List<Collider> FindExistingColliders(GameObject target)
        {
            var result = new List<Collider>();
            if (target == null || EditorUtility.IsPersistent(target)) return result;
            Transform root = target.transform;
            ConcaveColliderRoot marker = target.GetComponent<ConcaveColliderRoot>();
            HashSet<Transform> generated = GeneratedObjectsInScene(target.scene);
            Collider[] colliders = target.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null || collider.isTrigger) continue;
                if (marker != null && marker.Owns(collider)) continue;
                if (IsUnder(collider.transform, generated)) continue;
                if (!IsOwned(root, collider.transform, marker, null)) continue;
                result.Add(collider);
            }

            return result;
        }

        /// <summary>대상 하나를 계산한다(씬 변경 없음). progress가 false를 돌려주면 취소된다.</summary>
        public static ConcaveTargetResult Compute(GameObject target, ConcaveColliderSettings settings, Func<float, string, bool> progress = null)
        {
            return Compute(target, settings, progress, null);
        }

        // ─── 배치 실행 ───

        private sealed class Job
        {
            public GameObject Target;
            public ConcaveColliderSettings Settings;
            public ConcaveColliderOutputOptions Options;
        }

        private static List<Job> MakeJobs(IList<GameObject> targets, ConcaveColliderSettings settings, ConcaveColliderOutputOptions options)
        {
            var jobs = new List<Job>();
            var seen = new HashSet<GameObject>();
            if (targets == null) return jobs;
            ConcaveColliderSettings shared = (settings ?? ConcaveColliderPrefs.LoadSettings()).Clone();
            shared.Validate();
            for (int i = 0; i < targets.Count; i++)
            {
                GameObject target = targets[i];
                if (target == null || !seen.Add(target)) continue;
                jobs.Add(new Job { Target = target, Settings = shared, Options = options });
            }

            return jobs;
        }

        private sealed class BatchProgress
        {
            private readonly string title;
            private readonly int count;
            private int index;
            private string label = string.Empty;
            private double lastShown = -1;

            public bool Cancelled { get; private set; }

            public BatchProgress(int count, string title)
            {
                this.count = Mathf.Max(1, count);
                this.title = title;
            }

            public void Begin(int targetIndex, string targetName)
            {
                index = targetIndex;
                label = count > 1 ? $"{targetName} ({targetIndex + 1}/{count})" : targetName;
                lastShown = -1;
            }

            public bool Report(float fraction, string status)
            {
                if (Cancelled) return false;
                double now = EditorApplication.timeSinceStartup;
                if (lastShown >= 0 && now - lastShown < 0.05) return true;
                lastShown = now;
                float overall = (index + Mathf.Clamp01(fraction)) / count;
                if (EditorUtility.DisplayCancelableProgressBar(title, $"{label} — {status}", overall)) Cancelled = true;
                return !Cancelled;
            }
        }

        private static List<ConcaveTargetResult> RunJobs(List<Job> jobs, bool apply)
        {
            var results = new List<ConcaveTargetResult>(jobs.Count);
            if (jobs.Count == 0) return results;

            // 함께 선택된 대상끼리는 서로의 하위를 가져가지 않는다(이중 콜라이더 방지).
            var jobRoots = new HashSet<Transform>();
            for (int i = 0; i < jobs.Count; i++) jobRoots.Add(jobs[i].Target.transform);

            var progress = new BatchProgress(jobs.Count, apply ? ProgressTitle + " — 생성 중" : ProgressTitle + " — 미리보기 계산 중");
            referrerIndex = null;
            try
            {
                for (int i = 0; i < jobs.Count; i++)
                {
                    Job job = jobs[i];
                    ConcaveTargetResult result;
                    if (job.Target == null)
                    {
                        result = new ConcaveTargetResult { Error = "대상이 삭제되었습니다." };
                        results.Add(result);
                        continue;
                    }

                    if (progress.Cancelled)
                    {
                        result = new ConcaveTargetResult { Target = job.Target, Name = job.Target.name, Cancelled = true };
                        results.Add(result);
                        continue;
                    }

                    progress.Begin(i, job.Target.name);
                    result = Compute(job.Target, job.Settings, progress.Report, jobRoots);
                    if (apply && result.Succeeded)
                    {
                        progress.Report(0.97f, "콜라이더 적용 중");
                        try
                        {
                            Apply(result, job.Settings, job.Options);
                        }
                        catch (Exception exception)
                        {
                            result.Error = $"콜라이더를 적용하지 못했습니다: {exception.Message}";
                            result.ErrorDetail = exception.ToString();
                        }
                    }

                    if (apply) LogResult(result);
                    results.Add(result);
                }
            }
            finally
            {
                referrerIndex = null;
                EditorUtility.ClearProgressBar();
            }

            if (apply) SceneView.RepaintAll();
            return results;
        }

        private static void LogResult(ConcaveTargetResult result)
        {
            Object context = result.Target;
            if (result.Cancelled)
            {
                Debug.Log(LogPrefix + $"'{result.Name}' 생성을 취소했습니다. 이 대상은 바뀌지 않았습니다.", context);
                return;
            }

            if (result.Error != null)
            {
                bool unexpected = result.ErrorDetail != null && !result.ErrorDetail.StartsWith("System.ArgumentException", StringComparison.Ordinal);
                Debug.LogError(LogPrefix + $"'{result.Name}' 생성 실패: {result.Error}" + (unexpected ? "\n" + result.ErrorDetail : string.Empty), context);
                return;
            }

            if (!result.Applied) return;
            Debug.Log(LogPrefix + $"'{result.Name}' 생성 완료: {Describe(result)}", context);
            if (result.Warnings.Count > 0)
                Debug.LogWarning(LogPrefix + $"'{result.Name}' 경고:\n- " + string.Join("\n- ", result.Warnings), context);
        }

        /// <summary>결과 한 줄 요약(한국어).</summary>
        public static string Describe(ConcaveTargetResult result)
        {
            var builder = new StringBuilder();
            builder.Append("조각 ").Append(result.Pieces.Count).Append("개 (Box ").Append(result.CountOf(ConcavePieceKind.Box))
                .Append(", Sphere ").Append(result.CountOf(ConcavePieceKind.Sphere))
                .Append(", Capsule ").Append(result.CountOf(ConcavePieceKind.Capsule))
                .Append(", Hull ").Append(result.CountOf(ConcavePieceKind.ConvexMesh)).Append(')');
            if (result.TotalHullVertices > 0) builder.Append(", 헐 정점 ").Append(result.TotalHullVertices);
            if (result.Coverage >= 0f) builder.Append(", 커버리지 ").Append(Percent(result.Coverage));
            if (result.OutsideFraction >= 0f) builder.Append(", 바깥 ").Append(Percent(result.OutsideFraction));
            if (result.MetricsApproximate) builder.Append("(근사)");
            builder.Append(", ").Append(result.Seconds.ToString("0.00", CultureInfo.InvariantCulture)).Append("초");
            return builder.ToString();
        }

        public static string Percent(float value)
        {
            return value < 0f ? "-" : (value * 100f).ToString("0.0", CultureInfo.InvariantCulture) + "%";
        }

        // ─── 계산 ───

        private sealed class StaticSource
        {
            public Mesh Mesh;
            public Transform Transform;
        }

        private sealed class RigidSource
        {
            public Mesh Mesh;
            public Transform Transform;
            public Transform Bone;
        }

        private sealed class SourceSet
        {
            public readonly List<StaticSource> Static = new List<StaticSource>();
            public readonly List<SkinnedMeshRenderer> Skinned = new List<SkinnedMeshRenderer>();
            public readonly List<RigidSource> Rigid = new List<RigidSource>();

            /// <summary>본은 있지만 가중치를 읽을 수 없어 건너뛴 스킨드 메시(한국어 사유).</summary>
            public readonly List<string> Skipped = new List<string>();

            public int Count => Static.Count + Skinned.Count + Rigid.Count;
        }

        private static ConcaveTargetResult Compute(GameObject target, ConcaveColliderSettings settings, Func<float, string, bool> progress,
            HashSet<Transform> otherTargets)
        {
            var result = new ConcaveTargetResult { Target = target, Name = target != null ? target.name : string.Empty };
            var watch = Stopwatch.StartNew();
            try
            {
                if (target == null)
                {
                    result.Error = "대상이 없습니다.";
                    return result;
                }

                if (EditorUtility.IsPersistent(target))
                {
                    result.Error = PrefabAssetMessage(target);
                    return result;
                }

                Func<float, string, bool> report = progress ?? ((p, s) => true);
                if (!report(0f, "메시 읽는 중"))
                {
                    result.Cancelled = true;
                    return result;
                }

                ConcaveColliderSettings resolved = (settings ?? new ConcaveColliderSettings()).Clone();
                resolved.Validate();
                Transform root = target.transform;
                SourceSet sources = CollectSources(target, otherTargets);
                result.SourceMeshCount = sources.Count;
                if (sources.Static.Count == 0 && sources.Skinned.Count == 0)
                {
                    result.Error = sources.Skipped.Count > 0
                        ? string.Join("\n", sources.Skipped)
                        : "사용할 메시가 없습니다. 대상이나 활성 자식에 메시가 지정된 MeshFilter 또는 SkinnedMeshRenderer가 있는지 확인하세요(자기 Rigidbody를 가진 자식은 별도 대상입니다).";
                    return result;
                }

                result.Warnings.AddRange(sources.Skipped);

                bool both = sources.Static.Count > 0 && sources.Skinned.Count > 0;
                string staticError = null;

                // 정적 메시: 대상 루트 로컬로 합친다.
                if (sources.Static.Count > 0)
                {
                    var vertices = new List<Vector3>();
                    var triangles = new List<int>();
                    Matrix4x4 worldToRoot = root.worldToLocalMatrix;
                    for (int i = 0; i < sources.Static.Count; i++)
                    {
                        StaticSource source = sources.Static[i];
                        try
                        {
                            if (!ConcaveMeshInput.AppendMesh(source.Mesh, worldToRoot * source.Transform.localToWorldMatrix, vertices, triangles))
                                result.Warnings.Add($"메시 '{source.Mesh.name}'({source.Transform.name})에 삼각형이 없어 건너뛰었습니다.");
                        }
                        catch (Exception exception)
                        {
                            result.Warnings.Add($"메시 '{source.Mesh.name}'({source.Transform.name})을(를) 읽지 못해 건너뛰었습니다: {exception.Message}");
                        }
                    }

                    result.SourceTriangleCount += triangles.Count / 3;
                    if (triangles.Count == 0)
                    {
                        staticError = "정적 메시에서 삼각형을 읽지 못했습니다.";
                    }
                    else
                    {
                        bool nonUniform = ConcaveMeshInput.HasNonUniformScale(root.lossyScale);
                        ConcaveDecompositionResult run = ConcaveDecomposition.Run(vertices, triangles, resolved,
                            (p, s) => report(both ? p * 0.5f : p * 0.95f, s), nonUniform);
                        result.StaticResult = run;
                        if (run.Cancelled)
                        {
                            result.Cancelled = true;
                            return result;
                        }

                        if (run.Error != null)
                        {
                            result.Error = run.Error;
                            result.ErrorDetail = run.ErrorDetail;
                            return result;
                        }

                        result.Warnings.AddRange(run.Warnings);
                        if (nonUniform)
                            result.Warnings.Add("대상 스케일이 비균일해서 Sphere/Capsule 대신 축 정렬 Box와 볼록 메시만 사용했습니다.");
                        float scale = Mathf.Abs(root.lossyScale.x);
                        for (int i = 0; i < run.Pieces.Count; i++)
                        {
                            ConcavePiece piece = nonUniform ? run.Pieces[i] : run.Pieces[i].WithParentScaleCompensation(scale);
                            AddPiece(result, piece, root, false);
                        }
                    }
                }

                // 스킨드 메시: 본별 조각.
                if (sources.Skinned.Count > 0)
                {
                    if (!TryBuildSkinnedInput(root, sources, result.Warnings, out ConcaveSkinnedInput input, out Transform[] bones, out string skinError))
                    {
                        if (result.Pieces.Count == 0)
                        {
                            result.Error = skinError;
                            return result;
                        }

                        result.Warnings.Add(skinError);
                    }
                    else
                    {
                        if (input.Triangles != null) result.SourceTriangleCount += input.Triangles.Length / 3;
                        ConcaveDecompositionResult run = ConcaveDecomposition.RunSkinned(input, resolved,
                            (p, s) => report(both ? 0.5f + p * 0.45f : p * 0.95f, s));
                        result.SkinnedResult = run;
                        if (run.Cancelled)
                        {
                            result.Cancelled = true;
                            return result;
                        }

                        if (run.Error != null)
                        {
                            if (result.Pieces.Count == 0)
                            {
                                result.Error = run.Error;
                                result.ErrorDetail = run.ErrorDetail;
                                return result;
                            }

                            result.Warnings.Add("스킨드 메시 분해 실패: " + run.Error);
                        }
                        else
                        {
                            result.Warnings.AddRange(run.Warnings);
                            int converted = 0;
                            for (int i = 0; i < run.Pieces.Count; i++)
                            {
                                ConcavePiece piece = run.Pieces[i];
                                Transform bone = piece.BoneIndex >= 0 && piece.BoneIndex < bones.Length ? bones[piece.BoneIndex] : null;
                                if (bone == null)
                                {
                                    result.Warnings.Add($"본 '{piece.BoneName}'을(를) 찾을 수 없어 조각 하나를 건너뛰었습니다.");
                                    continue;
                                }

                                if (piece.Kind != ConcavePieceKind.ConvexMesh && ConcaveMeshInput.HasNonUniformScale(bone.lossyScale))
                                {
                                    ConcavePiece hull = ToHullPiece(piece);
                                    if (hull != null)
                                    {
                                        piece = hull;
                                        converted++;
                                    }
                                }

                                AddPiece(result, piece, bone, true);
                            }

                            if (converted > 0)
                                result.Warnings.Add($"스케일이 비균일한 본의 프리미티브 조각 {converted}개를 볼록 메시로 바꿨습니다.");
                        }
                    }
                }

                if (result.Pieces.Count == 0 && result.Error == null)
                    result.Error = staticError ?? "만들어진 조각이 없습니다.";
                else if (staticError != null)
                    result.Warnings.Add(staticError);
                report(0.95f, "계산 완료");
            }
            catch (Exception exception)
            {
                result.Error = $"예기치 않은 오류가 발생했습니다: {exception.GetType().Name}: {exception.Message}";
                result.ErrorDetail = exception.ToString();
            }
            finally
            {
                result.Seconds = watch.Elapsed.TotalSeconds;
            }

            return result;
        }

        private static void AddPiece(ConcaveTargetResult result, ConcavePiece piece, Transform parent, bool bonePiece)
        {
            if (!IsValidPiece(piece))
            {
                result.Warnings.Add("크기가 잘못된(NaN/0) 조각 하나를 건너뛰었습니다.");
                return;
            }

            result.Pieces.Add(new ConcavePlacedPiece { Piece = piece, Parent = parent, IsBonePiece = bonePiece });
        }

        private static bool IsValidPiece(ConcavePiece piece)
        {
            if (piece == null || !IsFinite(piece.Center) || !IsFinite(piece.Rotation) || !IsFinite(piece.LocalScale) || !(Mathf.Abs(piece.LocalScale) > 1e-12f))
                return false;
            switch (piece.Kind)
            {
                case ConcavePieceKind.Box:
                    return IsFinite(piece.Size) && piece.Size.x > 0f && piece.Size.y > 0f && piece.Size.z > 0f;
                case ConcavePieceKind.Sphere:
                    return IsFinite(piece.Radius) && piece.Radius > 0f;
                case ConcavePieceKind.Capsule:
                    return IsFinite(piece.Radius) && IsFinite(piece.Height) && piece.Radius > 0f && piece.Direction >= 0 && piece.Direction <= 2;
                default:
                    if (piece.Vertices == null || piece.Triangles == null || piece.Vertices.Length < 4 || piece.Triangles.Length < 12) return false;
                    for (int i = 0; i < piece.Vertices.Length; i++)
                    {
                        if (!IsFinite(piece.Vertices[i])) return false;
                    }

                    return true;
            }
        }

        /// <summary>프리미티브 조각을 미리보기 껍질로 만든 볼록 메시 조각으로 바꾼다(비균일 스케일 본용).</summary>
        private static ConcavePiece ToHullPiece(ConcavePiece piece)
        {
            if (piece.Vertices == null || piece.Triangles == null || piece.Vertices.Length < 4 || piece.Triangles.Length < 12) return null;
            return new ConcavePiece
            {
                Kind = ConcavePieceKind.ConvexMesh,
                Vertices = (Vector3[])piece.Vertices.Clone(),
                Triangles = (int[])piece.Triangles.Clone(),
                Center = Vector3.zero,
                Rotation = Quaternion.identity,
                BoneIndex = piece.BoneIndex,
                BoneName = piece.BoneName,
                LocalScale = piece.LocalScale,
                Volume = piece.Volume,
                Fill = 1f
            };
        }

        // ─── 소스 수집 ───

        private static SourceSet CollectSources(GameObject target, HashSet<Transform> otherTargets)
        {
            var sources = new SourceSet();
            Transform root = target.transform;
            ConcaveColliderRoot marker = target.GetComponent<ConcaveColliderRoot>();

            // LOD1 이상 렌더러는 제외(LOD0만 콜라이더 기준).
            var excludedLod = new HashSet<Renderer>();
            LODGroup[] lodGroups = target.GetComponentsInChildren<LODGroup>(true);
            for (int i = 0; i < lodGroups.Length; i++)
            {
                if (!IsOwned(root, lodGroups[i].transform, marker, otherTargets)) continue;
                LOD[] lods = lodGroups[i].GetLODs();
                for (int l = 1; l < lods.Length; l++)
                {
                    Renderer[] renderers = lods[l].renderers;
                    if (renderers == null) continue;
                    for (int r = 0; r < renderers.Length; r++)
                    {
                        if (renderers[r] != null) excludedLod.Add(renderers[r]);
                    }
                }
            }

            var bones = new HashSet<Transform>();
            SkinnedMeshRenderer[] skinned = target.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < skinned.Length; i++)
            {
                SkinnedMeshRenderer renderer = skinned[i];
                if (renderer == null || renderer.sharedMesh == null || !renderer.enabled || excludedLod.Contains(renderer)) continue;
                if (!IsOwned(root, renderer.transform, marker, otherTargets)) continue;
                bool usable;
                try
                {
                    usable = ConcaveMeshInput.HasUsableSkin(renderer);
                }
                catch (Exception)
                {
                    usable = false;
                }

                if (usable)
                {
                    sources.Skinned.Add(renderer);
                    Transform[] rendererBones = renderer.bones;
                    for (int b = 0; b < rendererBones.Length; b++)
                    {
                        if (rendererBones[b] != null) bones.Add(rendererBones[b]);
                    }
                }
                else if (renderer.bones.Length > 0)
                {
                    // 본이 있는데 가중치를 못 읽으면 바인드 포즈 모양이 엉뚱한 곳에 생기므로 정적 메시로 대신하지 않는다.
                    Mesh mesh = renderer.sharedMesh;
                    sources.Skipped.Add(!mesh.isReadable
                        ? $"스킨드 메시 '{mesh.name}'({renderer.name})의 본 가중치를 읽을 수 없어 건너뛰었습니다. 모델 임포터에서 Read/Write를 켠 뒤 다시 시도하세요."
                        : $"스킨드 메시 '{mesh.name}'({renderer.name})의 본 가중치·바인드포즈 개수가 본 목록과 맞지 않아 건너뛰었습니다.");
                }
                else
                {
                    // 본이 없는 스킨드 메시(블렌드셰이프 전용 등)는 렌더러 위치의 정적 메시로 다룬다.
                    sources.Static.Add(new StaticSource { Mesh = renderer.sharedMesh, Transform = renderer.transform });
                }
            }

            MeshFilter[] filters = target.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                MeshFilter filter = filters[i];
                if (filter == null || filter.sharedMesh == null) continue;
                if (!IsOwned(root, filter.transform, marker, otherTargets)) continue;
                Renderer renderer = filter.GetComponent<Renderer>();
                if (renderer != null && (!renderer.enabled || excludedLod.Contains(renderer))) continue;
                Transform bone = bones.Count > 0 ? FindBoneAncestor(filter.transform, bones) : null;
                if (bone != null)
                    sources.Rigid.Add(new RigidSource { Mesh = filter.sharedMesh, Transform = filter.transform, Bone = bone });
                else
                    sources.Static.Add(new StaticSource { Mesh = filter.sharedMesh, Transform = filter.transform });
            }

            return sources;
        }

        /// <summary>
        /// t가 대상 root에 속하는지: root까지 모든 조상이 activeSelf이고, 중간에 자기 Rigidbody·다른 마커·함께 처리 중인
        /// 다른 대상이 없으며, 이 마커가 만든 오브젝트가 아니어야 한다.
        /// </summary>
        private static bool IsOwned(Transform root, Transform t, ConcaveColliderRoot marker, HashSet<Transform> otherTargets)
        {
            if (t == null) return false;
            if (marker != null && marker.OwnsObject(t)) return false;
            for (Transform current = t; current != null; current = current.parent)
            {
                if (current == root) return true;
                if (!current.gameObject.activeSelf) return false;
                if (otherTargets != null && otherTargets.Contains(current)) return false;
                if (current.GetComponent<Rigidbody>() != null) return false;
                if (current.GetComponent<ConcaveColliderRoot>() != null) return false;
            }

            return false;
        }

        private static Transform FindBoneAncestor(Transform t, HashSet<Transform> bones)
        {
            for (Transform current = t; current != null; current = current.parent)
            {
                if (bones.Contains(current)) return current;
            }

            return null;
        }

        private static bool IsUnder(Transform t, HashSet<Transform> set)
        {
            if (set == null || set.Count == 0) return false;
            for (Transform current = t; current != null; current = current.parent)
            {
                if (set.Contains(current)) return true;
            }

            return false;
        }

        private static int TriangleCount(Mesh mesh)
        {
            if (mesh == null) return 0;
            long count = 0;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                if (mesh.GetTopology(s) == MeshTopology.Triangles) count += mesh.GetIndexCount(s) / 3;
            }

            return (int)Math.Min(count, int.MaxValue);
        }

        // ─── 스킨드 입력 ───

        private static bool TryBuildSkinnedInput(Transform root, SourceSet sources, List<string> warnings, out ConcaveSkinnedInput input,
            out Transform[] boneTransforms, out string error)
        {
            input = null;
            boneTransforms = null;
            error = null;

            if (sources.Skinned.Count == 1)
            {
                SkinnedMeshRenderer renderer = sources.Skinned[0];
                if (!ConcaveMeshInput.TryCreateSkinnedInput(renderer, root, out input, out error))
                {
                    error = $"'{renderer.name}': {error}";
                    return false;
                }

                Transform[] rendererBones = renderer.bones;
                boneTransforms = new Transform[rendererBones.Length];
                for (int b = 0; b < rendererBones.Length; b++) boneTransforms[b] = rendererBones[b] != null ? rendererBones[b] : renderer.transform;
                AddRigidParts(input, boneTransforms, sources.Rigid, warnings);
                return true;
            }

            return TryBuildCombinedSkinnedInput(root, sources, warnings, out input, out boneTransforms, out error);
        }

        /// <summary>
        /// 스킨드 메시 여러 개(같은 뼈대를 공유하는 몸통+옷 등)를 하나의 입력으로 합친다. 렌더러마다 바인드포즈가 다를 수 있으므로
        /// 현재 포즈에서 정점을 월드로 스키닝하고, 합친 본 목록의 바인드포즈를 현재 worldToLocal로 둔다(스킨 행렬 = 단위).
        /// </summary>
        private static bool TryBuildCombinedSkinnedInput(Transform root, SourceSet sources, List<string> warnings, out ConcaveSkinnedInput input,
            out Transform[] boneTransforms, out string error)
        {
            input = null;
            boneTransforms = null;
            error = null;
            var boneList = new List<Transform>();
            var boneIndex = new Dictionary<Transform, int>();
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var weights = new List<BoneWeight>();
            var failures = new List<string>();

            for (int r = 0; r < sources.Skinned.Count; r++)
            {
                SkinnedMeshRenderer renderer = sources.Skinned[r];
                if (!ConcaveMeshInput.TryCreateSkinnedInput(renderer, root, out ConcaveSkinnedInput single, out string reason))
                {
                    failures.Add($"'{renderer.name}': {reason}");
                    continue;
                }

                Transform[] rendererBones = renderer.bones;
                var skin = new Matrix4x4[rendererBones.Length];
                var map = new int[rendererBones.Length];
                for (int b = 0; b < rendererBones.Length; b++)
                {
                    Transform bone = rendererBones[b] != null ? rendererBones[b] : renderer.transform;
                    Matrix4x4 bindpose = b < single.Bindposes.Length ? single.Bindposes[b] : Matrix4x4.identity;
                    skin[b] = rendererBones[b] != null ? bone.localToWorldMatrix * bindpose : renderer.transform.localToWorldMatrix;
                    if (!boneIndex.TryGetValue(bone, out int index))
                    {
                        index = boneList.Count;
                        boneList.Add(bone);
                        boneIndex.Add(bone, index);
                    }

                    map[b] = index;
                }

                int offset = vertices.Count;
                Matrix4x4 fallback = renderer.transform.localToWorldMatrix;
                for (int v = 0; v < single.Vertices.Length; v++)
                {
                    BoneWeight w = single.BoneWeights[v];
                    Vector3 local = single.Vertices[v];
                    Vector3 world = Vector3.zero;
                    float sum = 0f;
                    Accumulate(w.boneIndex0, w.weight0);
                    Accumulate(w.boneIndex1, w.weight1);
                    Accumulate(w.boneIndex2, w.weight2);
                    Accumulate(w.boneIndex3, w.weight3);
                    vertices.Add(sum > 1e-6f ? world / sum : fallback.MultiplyPoint3x4(local));
                    weights.Add(new BoneWeight
                    {
                        boneIndex0 = Remap(w.boneIndex0), weight0 = w.weight0,
                        boneIndex1 = Remap(w.boneIndex1), weight1 = w.weight1,
                        boneIndex2 = Remap(w.boneIndex2), weight2 = w.weight2,
                        boneIndex3 = Remap(w.boneIndex3), weight3 = w.weight3
                    });

                    void Accumulate(int bone, float weight)
                    {
                        if (weight <= 0f || bone < 0 || bone >= skin.Length) return;
                        world += skin[bone].MultiplyPoint3x4(local) * weight;
                        sum += weight;
                    }
                }

                if (single.Triangles != null)
                {
                    for (int t = 0; t < single.Triangles.Length; t++) triangles.Add(single.Triangles[t] + offset);
                }

                int Remap(int bone)
                {
                    return bone >= 0 && bone < map.Length ? map[bone] : 0;
                }
            }

            if (failures.Count > 0 && vertices.Count > 0) warnings.AddRange(failures);
            if (vertices.Count == 0 || boneList.Count == 0)
            {
                error = failures.Count > 0 ? string.Join("\n", failures) : "스킨드 메시를 읽지 못했습니다.";
                return false;
            }

            var bindposes = new Matrix4x4[boneList.Count];
            var infos = new ConcaveBone[boneList.Count];
            for (int b = 0; b < boneList.Count; b++)
            {
                Transform bone = boneList[b];
                bindposes[b] = bone.worldToLocalMatrix;
                var info = new ConcaveBone { Name = bone.name, LocalToWorld = bone.localToWorldMatrix };
                for (Transform current = bone.parent; current != null && current != root; current = current.parent)
                {
                    if (boneIndex.TryGetValue(current, out int parentIndex))
                    {
                        info.ParentIndex = parentIndex;
                        break;
                    }
                }

                int depth = 0;
                for (Transform current = bone; current != null && current != root; current = current.parent) depth++;
                info.Depth = depth;
                infos[b] = info;
            }

            input = new ConcaveSkinnedInput
            {
                Vertices = vertices.ToArray(),
                Triangles = triangles.ToArray(),
                BoneWeights = weights.ToArray(),
                Bindposes = bindposes,
                Bones = infos
            };
            boneTransforms = boneList.ToArray();
            AddRigidParts(input, boneTransforms, sources.Rigid, warnings);
            return true;
        }

        /// <summary>본 아래 MeshFilter(무기·장신구 등)를 본 로컬 점으로 추가한다.</summary>
        private static void AddRigidParts(ConcaveSkinnedInput input, Transform[] boneTransforms, List<RigidSource> rigid, List<string> warnings)
        {
            if (rigid.Count == 0) return;
            var index = new Dictionary<Transform, int>();
            for (int b = 0; b < boneTransforms.Length; b++)
            {
                if (boneTransforms[b] != null && !index.ContainsKey(boneTransforms[b])) index.Add(boneTransforms[b], b);
            }

            var points = new List<Vector3>();
            var scratch = new List<int>();
            for (int i = 0; i < rigid.Count; i++)
            {
                RigidSource source = rigid[i];
                if (!index.TryGetValue(source.Bone, out int bone)) continue;
                points.Clear();
                scratch.Clear();
                try
                {
                    if (!ConcaveMeshInput.AppendMesh(source.Mesh, source.Bone.worldToLocalMatrix * source.Transform.localToWorldMatrix, points, scratch)) continue;
                }
                catch (Exception exception)
                {
                    warnings.Add($"본 메시 '{source.Mesh.name}'을(를) 읽지 못해 건너뛰었습니다: {exception.Message}");
                    continue;
                }

                input.RigidParts.Add(new ConcaveRigidPart { BoneIndex = bone, Points = points.ToArray() });
            }
        }

        // ─── 적용 ───

        private static void Apply(ConcaveTargetResult result, ConcaveColliderSettings settings, ConcaveColliderOutputOptions options)
        {
            GameObject target = result.Target;
            Transform root = target.transform;
            options = options ?? ConcaveColliderPrefs.LoadOptions();
            ConcaveColliderRoot marker = target.GetComponent<ConcaveColliderRoot>();
            if (marker != null)
            {
                string blocked = FindPrefabOwnedGenerated(marker);
                if (blocked != null) throw new InvalidOperationException(blocked);
            }

            var hullPieces = new List<ConcavePiece>();
            bool anyStatic = false;
            for (int i = 0; i < result.Pieces.Count; i++)
            {
                if (result.Pieces[i].Piece.Kind == ConcavePieceKind.ConvexMesh) hullPieces.Add(result.Pieces[i].Piece);
                if (!result.Pieces[i].IsBonePiece) anyStatic = true;
            }

            List<Collider> existing = FindExistingColliders(target);
            int layer = options.layer >= 0 && options.layer < 32 ? options.layer : target.layer;

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(UndoGenerate);
            Mesh mainAsset = marker != null ? marker.HullAsset : null;
            string assetPath = marker != null ? CurrentHullAssetPath(marker) : string.Empty;
            string owner = marker != null ? marker.HullAssetOwner : string.Empty;
            try
            {
                // 1) 헐 메시 에셋(같은 경로·GUID를 유지하며 내용만 교체)
                Mesh[] hullMeshes = null;
                if (hullPieces.Count > 0)
                {
                    string ownerKey = OwnerKey(target);
                    assetPath = ResolveHullAssetPath(target, marker, ownerKey, options.assetFolder);
                    hullMeshes = WriteHullMeshes(assetPath, hullPieces, out mainAsset);
                    owner = ownerKey;
                }

                // 2) 이전 생성 상태
                GameObject oldContainer = marker != null ? marker.Container : null;
                var oldBonePieces = new List<GameObject>();
                var previousDisabled = new List<Collider>();
                if (marker != null)
                {
                    IReadOnlyList<GameObject> previousBonePieces = marker.BonePieces;
                    for (int i = 0; i < previousBonePieces.Count; i++)
                    {
                        if (previousBonePieces[i] != null) oldBonePieces.Add(previousBonePieces[i]);
                    }

                    IReadOnlyList<Collider> disabledList = marker.DisabledColliders;
                    for (int i = 0; i < disabledList.Count; i++)
                    {
                        if (disabledList[i] != null) previousDisabled.Add(disabledList[i]);
                    }
                }

                // 3) 새 계층(이전 것은 새 것이 모두 만들어진 뒤에 지운다)
                GameObject container = null;
                if (anyStatic)
                {
                    container = new GameObject(ConcaveColliderRoot.ContainerName) { layer = layer };
                    container.transform.SetParent(root, false);
                    Undo.RegisterCreatedObjectUndo(container, UndoGenerate);
                }

                var colliders = new List<Collider>(result.Pieces.Count);
                var bonePieces = new List<GameObject>();
                int hullIndex = 0;
                for (int i = 0; i < result.Pieces.Count; i++)
                {
                    ConcavePlacedPiece placed = result.Pieces[i];
                    ConcavePiece piece = placed.Piece;
                    Mesh hullMesh = piece.Kind == ConcavePieceKind.ConvexMesh ? hullMeshes[hullIndex++] : null;
                    Transform parent = placed.IsBonePiece ? placed.Parent : container.transform;
                    if (parent == null)
                    {
                        result.Warnings.Add("부모 본이 사라져 조각 하나를 건너뛰었습니다.");
                        continue;
                    }

                    var go = new GameObject(PieceName(i, piece.Kind, placed.IsBonePiece)) { layer = layer };
                    go.transform.SetParent(parent, false);
                    go.transform.localPosition = piece.Center;
                    go.transform.localRotation = Normalize(piece.Rotation);
                    go.transform.localScale = Vector3.one * piece.LocalScale;
                    Collider collider = AddCollider(go, piece, hullMesh);
                    collider.isTrigger = options.isTrigger;
                    ConcavePhysicsMaterials.Assign(collider, options.physicsMaterial);
                    Undo.RegisterCreatedObjectUndo(go, UndoGenerate);
                    colliders.Add(collider);
                    if (placed.IsBonePiece) bonePieces.Add(go);
                }

                if (colliders.Count == 0) throw new InvalidOperationException("적용할 조각이 없습니다.");

                // 4) 기존 콜라이더 끄기/복원
                var disabled = new List<Collider>();
                if (options.disableExistingColliders)
                {
                    for (int i = 0; i < previousDisabled.Count; i++)
                    {
                        if (!previousDisabled[i].enabled && !disabled.Contains(previousDisabled[i])) disabled.Add(previousDisabled[i]);
                    }

                    for (int i = 0; i < existing.Count; i++)
                    {
                        Collider collider = existing[i];
                        if (collider == null || !collider.enabled) continue;
                        Undo.RecordObject(collider, UndoGenerate);
                        collider.enabled = false;
                        RecordPrefabOverride(collider);
                        if (!disabled.Contains(collider)) disabled.Add(collider);
                    }
                }
                else
                {
                    for (int i = 0; i < previousDisabled.Count; i++)
                    {
                        Collider collider = previousDisabled[i];
                        if (collider == null || collider.enabled) continue;
                        Undo.RecordObject(collider, UndoGenerate);
                        collider.enabled = true;
                        RecordPrefabOverride(collider);
                    }
                }

                // 5) 마커 기록. RecordObject 뒤에 다른 Undo 작업(생성·삭제)이 끼면 기록이 그 시점에 확정돼 변경이 빠지므로
                //    기록 → 변경 → Flush를 붙여서 한다.
                if (marker == null) marker = Undo.AddComponent<ConcaveColliderRoot>(target);
                else Undo.RecordObject(marker, UndoGenerate);
                marker.EditorSetGenerated(container, colliders, bonePieces, disabled, BuildMetrics(result));
                marker.EditorSetHullAsset(mainAsset, mainAsset != null ? AssetDatabase.GetAssetPath(mainAsset) : assetPath, owner);
                marker.EditorSetOptions(ConcaveColliderPrefs.SettingsToJson(settings), options.isTrigger, ConcavePhysicsMaterials.Filter(options.physicsMaterial), options.layer,
                    options.disableExistingColliders, options.assetFolder);
                RecordPrefabOverride(marker);

                // 6) 이전 생성물 제거(마커 기록을 확정한 뒤). Undo 때 옛 컨테이너가 먼저 되살아나고 마커 참조가 그것으로 돌아간다.
                Undo.FlushUndoRecordObjects();
                if (oldContainer != null) Undo.DestroyObjectImmediate(oldContainer);
                for (int i = 0; i < oldBonePieces.Count; i++)
                {
                    if (oldBonePieces[i] != null) Undo.DestroyObjectImmediate(oldBonePieces[i]);
                }

                MarkDirty(target);
                Undo.CollapseUndoOperations(group);
                if (mainAsset != null) AssetDatabase.SaveAssetIfDirty(mainAsset);
                result.Applied = true;
                result.HullAssetPath = hullPieces.Count > 0 && mainAsset != null ? AssetDatabase.GetAssetPath(mainAsset) : string.Empty;
            }
            catch
            {
                Undo.RevertAllDownToGroup(group);
                throw;
            }
        }

        private static Collider AddCollider(GameObject go, ConcavePiece piece, Mesh hullMesh)
        {
            switch (piece.Kind)
            {
                case ConcavePieceKind.Box:
                    var box = go.AddComponent<BoxCollider>();
                    box.center = Vector3.zero;
                    box.size = piece.Size;
                    return box;
                case ConcavePieceKind.Sphere:
                    var sphere = go.AddComponent<SphereCollider>();
                    sphere.center = Vector3.zero;
                    sphere.radius = piece.Radius;
                    return sphere;
                case ConcavePieceKind.Capsule:
                    var capsule = go.AddComponent<CapsuleCollider>();
                    capsule.center = Vector3.zero;
                    capsule.direction = piece.Direction;
                    capsule.radius = piece.Radius;
                    capsule.height = Mathf.Max(piece.Height, piece.Radius * 2f);
                    return capsule;
                default:
                    var meshCollider = go.AddComponent<MeshCollider>();
                    meshCollider.convex = true;
                    meshCollider.sharedMesh = hullMesh;
                    return meshCollider;
            }
        }

        private static string PieceName(int index, ConcavePieceKind kind, bool bonePiece)
        {
            string kindName = kind == ConcavePieceKind.ConvexMesh ? "Hull" : kind.ToString();
            return bonePiece
                ? $"Concave Piece {index:00} ({kindName})"
                : $"Piece {index:00} ({kindName})";
        }

        private static ConcaveColliderMetrics BuildMetrics(ConcaveTargetResult result)
        {
            return new ConcaveColliderMetrics
            {
                pieceCount = result.Pieces.Count,
                boxCount = result.CountOf(ConcavePieceKind.Box),
                sphereCount = result.CountOf(ConcavePieceKind.Sphere),
                capsuleCount = result.CountOf(ConcavePieceKind.Capsule),
                hullCount = result.CountOf(ConcavePieceKind.ConvexMesh),
                bonePieceCount = result.BonePieceCount,
                totalHullVertices = result.TotalHullVertices,
                sourceMeshCount = result.SourceMeshCount,
                sourceTriangleCount = result.SourceTriangleCount,
                coverage = result.Coverage,
                outsideFraction = result.OutsideFraction,
                metricsApproximate = result.MetricsApproximate,
                seconds = (float)result.Seconds,
                generatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                warnings = result.Warnings.ToArray()
            };
        }

        // ─── 헐 메시 에셋 ───

        /// <summary>
        /// 이번 생성에 쓸 에셋 경로. 마커가 가진 에셋이 이 대상 소유이고 다른 곳(열린 씬의 복제본, 프리팹 원본, 디스크의 다른 씬·프리팹)에서
        /// 쓰지 않으면 그 경로를 그대로 쓴다(GUID 유지). 아니면 폴더에 '이름_해시.asset'을 새로 만든다(다른 곳의 헐을 덮어쓰지 않게).
        /// </summary>
        private static string ResolveHullAssetPath(GameObject target, ConcaveColliderRoot marker, string ownerKey, string folder)
        {
            string container = OwnerContainerPath(target);
            string splitFrom = null;
            List<string> splitReferrers = null;
            if (marker != null)
            {
                string current = CurrentHullAssetPath(marker);
                if (!string.IsNullOrEmpty(current))
                {
                    string owner = marker.HullAssetOwner;
                    bool ownerMatches = owner == ownerKey || string.IsNullOrEmpty(owner) || owner.StartsWith(TempOwnerPrefix, StringComparison.Ordinal);
                    if (ownerMatches && !IsSharedWithPrefabSource(marker, current) && !IsReferencedByOtherRoots(marker, current))
                    {
                        List<string> referrers = OnDiskReferrers(current, container);
                        if (referrers.Count == 0) return current;
                        splitFrom = current;
                        splitReferrers = referrers;
                    }
                }
            }

            if (!ConcaveColliderOutputOptions.TryNormalizeAssetFolder(folder, out string normalized))
                normalized = ConcaveColliderOutputOptions.DefaultAssetFolder;
            EnsureFolder(normalized);
            string path = $"{normalized}/{SanitizeFileName(target.name)}_{Hash8(ownerKey)}.asset";
            Object existing = AssetDatabase.LoadMainAssetAtPath(path);
            if (existing != null)
            {
                // 같은 대상이 제거 후 다시 만드는 경우만 같은 파일(GUID)을 재사용한다. 임시 소유 키(저장 안 한 씬)는 다른 씬의
                // 같은 이름·위치 오브젝트와 겹칠 수 있으므로 재사용하지 않고, 어디서든(열린 씬·디스크의 씬·프리팹) 쓰는 파일도 재사용하지 않는다.
                bool reusable = existing is Mesh
                                && !ownerKey.StartsWith(TempOwnerPrefix, StringComparison.Ordinal)
                                && !IsSharedWithPrefabSource(marker, path)
                                && !IsReferencedByOtherRoots(marker, path)
                                && OnDiskReferrers(path, container).Count == 0;
                if (!reusable) path = AssetDatabase.GenerateUniqueAssetPath(path);
            }

            if (splitFrom != null)
            {
                Debug.Log(LogPrefix + $"'{splitFrom}'을(를) 다른 씬·프리팹도 쓰고 있어 '{target.name}'의 헐 메시를 새 파일 '{path}'에 저장합니다. " +
                          $"기존 파일은 그대로 둡니다. 함께 쓰는 곳: {JoinPaths(splitReferrers)}", target);
            }

            return path;
        }

        /// <summary>대상의 편집 내용이 저장될 파일(프리팹 스테이지면 프리팹 에셋, 아니면 씬). 저장 안 한 씬이면 빈 문자열.</summary>
        private static string OwnerContainerPath(GameObject target)
        {
            if (target == null) return string.Empty;
            PrefabStage stage = PrefabStageUtility.GetPrefabStage(target);
            if (stage != null && !string.IsNullOrEmpty(stage.assetPath)) return stage.assetPath;
            return target.scene.path ?? string.Empty;
        }

        /// <summary>
        /// 배치(Generate·Rebuild·Remove) 동안만 쓰는 역참조 색인: .asset 경로 → 그 파일을 직접 참조하는 디스크의 씬·프리팹.
        /// 배치 중에는 씬·프리팹 파일을 저장하지 않으므로 한 번만 만든다. 배치가 끝나면 비운다.
        /// </summary>
        private static Dictionary<string, List<string>> referrerIndex;

        /// <summary>
        /// 디스크의 씬·프리팹 중 assetPath를 직접 참조하는 것(excluded에 든 경로는 제외). 대상 자신의 씬·프리팹은 메모리 상태가 우선이라
        /// 제외하고, 열린 씬의 메모리 상태는 IsReferencedByOtherRoots가 따로 본다. 프리팹 변형·중첩처럼 원본에서 물려받는 참조는
        /// 원본을 따라가는 것이 맞으므로 직접 참조만 센다.
        /// </summary>
        private static List<string> OnDiskReferrers(string assetPath, string excluded)
        {
            return OnDiskReferrers(assetPath, string.IsNullOrEmpty(excluded) ? null : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { excluded });
        }

        private static List<string> OnDiskReferrers(string assetPath, HashSet<string> excluded)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(assetPath)) return result;
            if (referrerIndex == null) referrerIndex = BuildReferrerIndex();
            if (!referrerIndex.TryGetValue(assetPath, out List<string> referrers)) return result;
            for (int i = 0; i < referrers.Count; i++)
            {
                if (excluded != null && excluded.Contains(referrers[i])) continue;
                result.Add(referrers[i]);
            }

            return result;
        }

        private static Dictionary<string, List<string>> BuildReferrerIndex()
        {
            var index = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] filters = { "t:Scene", "t:Prefab" };
            for (int f = 0; f < filters.Length; f++)
            {
                string[] guids = AssetDatabase.FindAssets(filters[f]);
                for (int g = 0; g < guids.Length; g++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[g]);
                    if (string.IsNullOrEmpty(path) || !seen.Add(path)) continue;
                    string[] dependencies = AssetDatabase.GetDependencies(path, false);
                    for (int d = 0; d < dependencies.Length; d++)
                    {
                        string dependency = dependencies[d];
                        if (!dependency.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!index.TryGetValue(dependency, out List<string> list))
                        {
                            list = new List<string>();
                            index.Add(dependency, list);
                        }

                        list.Add(path);
                    }
                }
            }

            return index;
        }

        private static string JoinPaths(List<string> paths)
        {
            if (paths == null || paths.Count == 0) return string.Empty;
            const int shown = 3;
            var builder = new StringBuilder();
            for (int i = 0; i < paths.Count && i < shown; i++)
            {
                if (i > 0) builder.Append(", ");
                builder.Append(paths[i]);
            }

            if (paths.Count > shown) builder.Append(" 외 ").Append(paths.Count - shown).Append("개");
            return builder.ToString();
        }

        /// <summary>
        /// 에셋 파일 하나 = 빈 주 메시(파일 이름, 묶음 역할) + 서브 메시 Hull_00... 기존 서브 메시는 Undo 기록 후 내용만 바꾸고
        /// (파일 GUID·서브 에셋 fileID 유지), 이번에 쓰지 않는 것은 비운다.
        /// </summary>
        private static Mesh[] WriteHullMeshes(string path, List<ConcavePiece> pieces, out Mesh mainAsset)
        {
            var existing = new Dictionary<string, Mesh>();
            mainAsset = AssetDatabase.LoadMainAssetAtPath(path) as Mesh;
            if (mainAsset == null)
            {
                // 주 에셋 이름은 Unity가 파일 이름으로 맞추므로 콜라이더 메시로 쓰지 않는 빈 묶음 메시로 둔다.
                mainAsset = new Mesh { name = System.IO.Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(mainAsset, path);
            }
            else
            {
                Object[] all = AssetDatabase.LoadAllAssetsAtPath(path);
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] is Mesh mesh && mesh != mainAsset && !existing.ContainsKey(mesh.name)) existing.Add(mesh.name, mesh);
                }
            }

            var meshes = new Mesh[pieces.Count];
            for (int i = 0; i < pieces.Count; i++)
            {
                string name = HullMeshPrefix + i.ToString("00", CultureInfo.InvariantCulture);
                if (existing.TryGetValue(name, out Mesh mesh))
                {
                    Undo.RecordObject(mesh, UndoGenerate);
                    FillHullMesh(mesh, pieces[i]);
                    EditorUtility.SetDirty(mesh);
                }
                else
                {
                    mesh = new Mesh { name = name };
                    FillHullMesh(mesh, pieces[i]);
                    AssetDatabase.AddObjectToAsset(mesh, mainAsset);
                }

                meshes[i] = mesh;
            }

            // 이번에 쓰지 않는 서브 메시는 지우지 않고 비운다(Undo로 이전 콜라이더를 되살렸을 때 참조가 끊기지 않게).
            foreach (KeyValuePair<string, Mesh> pair in existing)
            {
                if (!pair.Key.StartsWith(HullMeshPrefix, StringComparison.Ordinal)) continue;
                if (!int.TryParse(pair.Key.Substring(HullMeshPrefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)) continue;
                if (index < pieces.Count || pair.Value.vertexCount == 0) continue;
                Undo.RecordObject(pair.Value, UndoGenerate);
                pair.Value.Clear();
                EditorUtility.SetDirty(pair.Value);
            }

            EditorUtility.SetDirty(mainAsset);
            return meshes;
        }

        private static void FillHullMesh(Mesh mesh, ConcavePiece piece)
        {
            mesh.Clear();
            mesh.indexFormat = piece.Vertices.Length > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(piece.Vertices);
            mesh.SetTriangles(piece.Triangles, 0, true);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        private static string CurrentHullAssetPath(ConcaveColliderRoot marker)
        {
            if (marker == null) return string.Empty;
            if (marker.HullAsset != null)
            {
                string path = AssetDatabase.GetAssetPath(marker.HullAsset);
                if (!string.IsNullOrEmpty(path)) return path;
            }

            string stored = marker.HullAssetPath;
            return !string.IsNullOrEmpty(stored) && AssetDatabase.LoadMainAssetAtPath(stored) is Mesh ? stored : string.Empty;
        }

        private static bool SameAsset(ConcaveColliderRoot root, string path)
        {
            return root != null && !string.IsNullOrEmpty(path) && string.Equals(CurrentHullAssetPath(root), path, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>마커가 프리팹 인스턴스의 일부이고 그 에셋 참조가 프리팹 원본(또는 그 위 단계)에서 물려받은 것인지.</summary>
        private static bool IsSharedWithPrefabSource(ConcaveColliderRoot marker, string path)
        {
            if (marker == null || !PrefabUtility.IsPartOfPrefabInstance(marker)) return false;
            ConcaveColliderRoot source = PrefabUtility.GetCorrespondingObjectFromSource(marker);
            int guard = 0;
            while (source != null && guard++ < 16)
            {
                if (SameAsset(source, path)) return true;
                source = PrefabUtility.GetCorrespondingObjectFromSource(source);
            }

            return false;
        }

        /// <summary>열린 씬·프리팹 스테이지의 다른 마커가 같은 에셋을 쓰는지(프리팹에서 물려받은 참조는 제외).</summary>
        private static bool IsReferencedByOtherRoots(ConcaveColliderRoot marker, string path)
        {
            List<ConcaveColliderRoot> roots = AllLoadedRoots();
            for (int i = 0; i < roots.Count; i++)
            {
                ConcaveColliderRoot other = roots[i];
                if (other == null || other == marker) continue;
                if (!SameAsset(other, path)) continue;
                if (IsSharedWithPrefabSource(other, path)) continue;
                return true;
            }

            return false;
        }

        private static List<ConcaveColliderRoot> AllLoadedRoots()
        {
            var result = new List<ConcaveColliderRoot>();
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                Scene scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                GameObject[] roots = scene.GetRootGameObjects();
                for (int r = 0; r < roots.Length; r++) result.AddRange(roots[r].GetComponentsInChildren<ConcaveColliderRoot>(true));
            }

            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.prefabContentsRoot != null)
                result.AddRange(stage.prefabContentsRoot.GetComponentsInChildren<ConcaveColliderRoot>(true));
            return result;
        }

        /// <summary>같은 씬의 모든 마커가 만든 오브젝트(컨테이너·본 조각).</summary>
        private static HashSet<Transform> GeneratedObjectsInScene(Scene scene)
        {
            var set = new HashSet<Transform>();
            if (!scene.IsValid() || !scene.isLoaded) return set;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int r = 0; r < roots.Length; r++)
            {
                ConcaveColliderRoot[] markers = roots[r].GetComponentsInChildren<ConcaveColliderRoot>(true);
                for (int m = 0; m < markers.Length; m++)
                {
                    if (markers[m].Container != null) set.Add(markers[m].Container.transform);
                    IReadOnlyList<GameObject> bonePieces = markers[m].BonePieces;
                    for (int b = 0; b < bonePieces.Count; b++)
                    {
                        if (bonePieces[b] != null) set.Add(bonePieces[b].transform);
                    }
                }
            }

            return set;
        }

        /// <param name="removedContainers">제거한 대상들의 씬·프리팹 경로. 디스크의 그 파일은 아직 저장 전이라 옛 참조가 남아 있으므로 참조 검사에서 뺀다.</param>
        private static void HandleAssetRemoval(List<string> paths, HashSet<string> removedContainers, ConcaveAssetRemoval mode)
        {
            if (mode == ConcaveAssetRemoval.Keep) return;

            // 아직 다른 마커(열린 씬) 또는 디스크의 다른 씬·프리팹이 쓰는 에셋은 지우지 않는다.
            var deletable = new List<string>();
            for (int i = 0; i < paths.Count; i++)
            {
                if (IsReferencedByOtherRoots(null, paths[i])) continue;
                List<string> referrers = OnDiskReferrers(paths[i], removedContainers);
                if (referrers.Count > 0)
                {
                    Debug.Log(LogPrefix + $"헐 메시 에셋 '{paths[i]}'은(는) 다른 씬·프리팹이 아직 쓰고 있어 삭제하지 않았습니다: {JoinPaths(referrers)}");
                    continue;
                }

                deletable.Add(paths[i]);
            }

            if (deletable.Count == 0) return;
            if (mode == ConcaveAssetRemoval.Ask)
            {
                if (Application.isBatchMode) return;
                var builder = new StringBuilder();
                builder.Append("생성된 콜라이더를 제거했습니다. 헐 메시 에셋도 삭제할까요?\n\n");
                for (int i = 0; i < deletable.Count && i < 10; i++) builder.Append("• ").Append(deletable[i]).Append('\n');
                if (deletable.Count > 10) builder.Append("… 외 ").Append(deletable.Count - 10).Append("개\n");
                builder.Append("\n삭제한 파일은 휴지통으로 이동합니다. 삭제 후 Undo로 콜라이더를 되살리면 볼록 메시 조각의 메시가 비어 있게 됩니다.");
                if (!TelleRGUI.Confirm("헐 메시 에셋 삭제", builder.ToString(), "삭제", "유지")) return;
            }
            else if (mode != ConcaveAssetRemoval.Delete)
            {
                return;
            }

            for (int i = 0; i < deletable.Count; i++)
            {
                if (AssetDatabase.MoveAssetToTrash(deletable[i]))
                    Debug.Log(LogPrefix + $"헐 메시 에셋을 휴지통으로 옮겼습니다: {deletable[i]}");
                else
                    Debug.LogWarning(LogPrefix + $"헐 메시 에셋을 삭제하지 못했습니다: {deletable[i]}");
            }
        }

        // ─── 공통 ───

        /// <summary>이전 생성물이 프리팹 원본에 포함돼 있어 인스턴스에서 지울 수 없으면 한국어 사유, 아니면 null.</summary>
        private static string FindPrefabOwnedGenerated(ConcaveColliderRoot marker)
        {
            if (marker.Container != null && IsPrefabOwned(marker.Container)) return PrefabOwnedMessage;
            IReadOnlyList<GameObject> bonePieces = marker.BonePieces;
            for (int i = 0; i < bonePieces.Count; i++)
            {
                if (bonePieces[i] != null && IsPrefabOwned(bonePieces[i])) return PrefabOwnedMessage;
            }

            return null;
        }

        private const string PrefabOwnedMessage =
            "생성된 콜라이더가 프리팹 에셋에 포함돼 있어 인스턴스에서는 바꾸거나 지울 수 없습니다. 프리팹을 열어(Prefab Mode) 다시 만들거나 제거하세요.";

        private static bool IsPrefabOwned(GameObject go)
        {
            return PrefabUtility.IsPartOfPrefabInstance(go) && !PrefabUtility.IsAddedGameObjectOverride(go);
        }

        private static string PrefabAssetMessage(GameObject target)
        {
            return $"'{target.name}'은(는) Project 창의 프리팹 에셋입니다. 프리팹을 열어(Prefab Mode) 편집하거나 씬에 배치한 뒤 생성하세요.";
        }

        private static string OwnerKey(GameObject target)
        {
            PrefabStage stage = PrefabStageUtility.GetPrefabStage(target);
            if (stage != null && stage.prefabContentsRoot != null)
                return "prefab:" + stage.assetPath + ":" + HierarchyPath(target.transform, stage.prefabContentsRoot.transform);

            GlobalObjectId id = GlobalObjectId.GetGlobalObjectIdSlow(target);
            if (id.identifierType == 2 && id.targetObjectId != 0 && !string.IsNullOrEmpty(target.scene.path)) return "gid:" + id;
            return TempOwnerPrefix + target.scene.name + ":" + HierarchyPath(target.transform, null);
        }

        private static string HierarchyPath(Transform t, Transform stopAt)
        {
            var builder = new StringBuilder();
            for (Transform current = t; current != null; current = current.parent)
            {
                builder.Insert(0, "/" + current.GetSiblingIndex().ToString(CultureInfo.InvariantCulture) + current.name);
                if (current == stopAt) break;
            }

            return builder.ToString();
        }

        private static string Hash8(string text)
        {
            uint hash = 2166136261;
            for (int i = 0; i < text.Length; i++)
            {
                hash ^= text[i];
                hash *= 16777619;
            }

            return hash.ToString("x8", CultureInfo.InvariantCulture);
        }

        private static string SanitizeFileName(string name)
        {
            char[] invalid = System.IO.Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(name.Length);
            for (int i = 0; i < name.Length && builder.Length < 48; i++)
            {
                char ch = name[i];
                builder.Append(ch == ' ' || ch == '.' || Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
            }

            return builder.Length > 0 ? builder.ToString() : "Concave";
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static void MarkDirty(GameObject owner)
        {
            EditorUtility.SetDirty(owner);
            if (owner.scene.IsValid()) EditorSceneManager.MarkSceneDirty(owner.scene);
        }

        private static void RecordPrefabOverride(Object target)
        {
            if (target == null) return;
            EditorUtility.SetDirty(target);
            if (PrefabUtility.IsPartOfPrefabInstance(target)) PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        }

        private static Quaternion Normalize(Quaternion q)
        {
            float magnitude = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (!(magnitude > 1e-6f) || !IsFinite(magnitude)) return Quaternion.identity;
            float inverse = 1f / magnitude;
            return new Quaternion(q.x * inverse, q.y * inverse, q.z * inverse, q.w * inverse);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
        }
    }
}
