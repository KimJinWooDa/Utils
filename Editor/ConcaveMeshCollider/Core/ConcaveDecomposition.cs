using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEngine;

namespace TelleR.ConcaveCollider
{
    /// <summary>
    /// Concave Mesh Collider 코어 진입점. 외부 패키지·네이티브 플러그인 없이 순수 C#으로
    /// 오목 메시를 볼록 조각(볼록 MeshCollider, 꽉 맞으면 Box/Sphere/Capsule)으로 근사 분해한다.
    /// 씬을 바꾸지 않는다. 호출 스레드(메인 스레드)에서 동기로 돌고, 무거운 루프만 내부에서 병렬 처리하며
    /// Unity API는 워커 스레드에서 부르지 않는다. progress 콜백은 호출 스레드에서만 불리며 false를 돌려주면 취소된다.
    /// </summary>
    public static class ConcaveDecomposition
    {
        /// <summary>
        /// 정적 메시 분해. vertices/triangles는 대상 루트 로컬 공간(여러 메시를 합친 것)이어야 하고,
        /// 결과 조각도 같은 공간으로 나온다. nonUniformScale이 true면(루트 lossyScale이 비균일)
        /// 회전된 프리미티브가 찌그러지므로 Sphere/Capsule을 끄고 Box는 로컬 축 정렬로만 만든다.
        /// </summary>
        public static ConcaveDecompositionResult Run(
            IList<Vector3> vertices,
            IList<int> triangles,
            ConcaveColliderSettings settings,
            Func<float, string, bool> progress = null,
            bool nonUniformScale = false)
        {
            var result = new ConcaveDecompositionResult();
            var watch = Stopwatch.StartNew();
            var reporter = new Reporter(progress);
            try
            {
                StaticPipeline.Execute(vertices, triangles, ResolvedSettings.From(settings), nonUniformScale, reporter, result);
            }
            catch (OperationCanceledException)
            {
                result.Cancelled = true;
                result.Pieces.Clear();
            }
            catch (Exception exception)
            {
                result.Pieces.Clear();
                result.Error = $"분해 중 오류가 발생했습니다: {exception.GetType().Name}: {exception.Message}";
                result.ErrorDetail = exception.ToString();
            }

            result.Seconds = watch.Elapsed.TotalSeconds;
            return result;
        }

        /// <summary>
        /// 스킨드 메시 분해(본별 조각). 결과 조각의 좌표는 BoneIndex 본의 로컬 공간(바인드 포즈 기준)이며
        /// 콜라이더를 그 본의 자식으로 두면 애니메이션을 따라간다.
        /// </summary>
        public static ConcaveDecompositionResult RunSkinned(
            ConcaveSkinnedInput input,
            ConcaveColliderSettings settings,
            Func<float, string, bool> progress = null)
        {
            var result = new ConcaveDecompositionResult();
            var watch = Stopwatch.StartNew();
            var reporter = new Reporter(progress);
            try
            {
                SkinnedPipeline.Execute(input, ResolvedSettings.From(settings), reporter, result);
            }
            catch (OperationCanceledException)
            {
                result.Cancelled = true;
                result.Pieces.Clear();
            }
            catch (Exception exception)
            {
                result.Pieces.Clear();
                result.Error = $"스킨드 분해 중 오류가 발생했습니다: {exception.GetType().Name}: {exception.Message}";
                result.ErrorDetail = exception.ToString();
            }

            result.Seconds = watch.Elapsed.TotalSeconds;
            return result;
        }
    }

    /// <summary>진행률 콜백 래퍼. 호출 스레드에서만 쓴다.</summary>
    internal sealed class Reporter
    {
        private readonly Func<float, string, bool> callback;
        private float lastProgress;
        private string lastStatus = string.Empty;

        public Reporter(Func<float, string, bool> callback)
        {
            this.callback = callback;
        }

        public void Report(float progress, string status)
        {
            lastProgress = Mathf.Clamp01(progress);
            lastStatus = status;
            if (callback != null && !callback(lastProgress, status)) throw new OperationCanceledException();
        }

        /// <summary>현재 단계 안에서 세부 진행률을 보고한다(취소 확인 겸용).</summary>
        public bool Tick(float from, float to, float fraction)
        {
            Report(from + (to - from) * Mathf.Clamp01(fraction), lastStatus);
            return true;
        }

        public Func<bool> CancelCheck => () =>
        {
            Report(lastProgress, lastStatus);
            return false;
        };
    }

    /// <summary>작업 공간 → 출력 공간 변환: out = Translation + Rotation(w * Scale).</summary>
    internal struct OutputMap
    {
        public Frame3 Rotation;
        public D3 Translation;
        public double Scale;

        public D3 Point(D3 w) => Translation + Rotation.ToParent(w * Scale);
        public Vector3 ToVector(D3 w) => Point(w).ToVector3();
    }

    /// <summary>작업 공간 최종 조각.</summary>
    internal sealed class WorkPiece
    {
        public ConcavePieceKind Kind;
        public HullShape FullHull;
        public HullShape OutputHull;
        public PrimitiveFit Primitive;
        public double Fill = 1;
        public int BoneIndex = -1;
        public OutputMap Map;

        public bool Contains(D3 p)
        {
            return Kind == ConcavePieceKind.ConvexMesh ? OutputHull.Contains(p) : Primitive.Contains(p);
        }

        public ConcavePiece ToPiece(string boneName)
        {
            var piece = new ConcavePiece
            {
                Kind = Kind,
                BoneIndex = BoneIndex,
                BoneName = boneName,
                Volume = (float)(FullHull.Volume * Map.Scale * Map.Scale * Map.Scale),
                Fill = (float)Fill
            };

            HullShape hull = OutputHull;
            piece.Vertices = new Vector3[hull.Vertices.Length];
            for (int i = 0; i < hull.Vertices.Length; i++) piece.Vertices[i] = Map.ToVector(hull.Vertices[i]);
            piece.Triangles = (int[])hull.Triangles.Clone();

            double scale = Map.Scale;
            switch (Kind)
            {
                case ConcavePieceKind.Box:
                    piece.Center = Map.ToVector(Primitive.Center);
                    piece.Rotation = Map.Rotation.Compose(Primitive.Axes).ToQuaternion();
                    piece.Size = (Primitive.HalfExtents * (2 * scale)).ToVector3();
                    break;
                case ConcavePieceKind.Sphere:
                    piece.Center = Map.ToVector(Primitive.Center);
                    piece.Radius = (float)(Primitive.Radius * scale);
                    break;
                case ConcavePieceKind.Capsule:
                    piece.Center = Map.ToVector(Primitive.Center);
                    piece.Rotation = Map.Rotation.Compose(Primitive.Axes).ToQuaternion();
                    piece.Direction = Primitive.Direction;
                    piece.Radius = (float)(Primitive.Radius * scale);
                    piece.Height = (float)(2 * (Primitive.HalfSegment + Primitive.Radius) * scale);
                    break;
                default:
                    piece.Center = Vector3.zero;
                    piece.Rotation = Quaternion.identity;
                    break;
            }

            return piece;
        }
    }

    internal static class PieceFinisher
    {
        /// <summary>프리미티브 피팅(전체 껍질 기준) → 안 맞으면 정점 축소한 볼록 메시. 패딩은 작업 단위.</summary>
        public static WorkPiece Finish(HullShape hull, ResolvedSettings settings, Frame3? forcedAxes, double paddingWork)
        {
            var piece = new WorkPiece { FullHull = hull };
            PrimitiveFit primitive = PrimitiveFitter.Choose(hull, settings.FitBox, settings.FitSphere, settings.FitCapsule, settings.FillThreshold, forcedAxes);
            HullShape reduced = HullOps.Reduce(hull, settings.MaxHullVertices) ?? hull;
            if (primitive.Valid)
            {
                piece.Kind = primitive.Kind;
                piece.Fill = hull.Volume / primitive.Volume;
                if (paddingWork > 0)
                {
                    primitive.HalfExtents += new D3(paddingWork, paddingWork, paddingWork);
                    primitive.Radius += paddingWork;
                }

                piece.Primitive = primitive;
                piece.OutputHull = reduced;
                return piece;
            }

            piece.Kind = ConcavePieceKind.ConvexMesh;
            if (paddingWork > 0)
            {
                HullShape inflated = Inflate(reduced, paddingWork);
                if (inflated != null)
                {
                    if (inflated.Vertices.Length > settings.MaxHullVertices) inflated = HullOps.Reduce(inflated, settings.MaxHullVertices, 1.0) ?? inflated;
                    reduced = inflated;
                }
            }

            piece.OutputHull = reduced;
            return piece;
        }

        /// <summary>정점을 인접 면 법선 방향으로 밀어 모든 면이 최소 distance만큼 바깥으로 나가게 한다.</summary>
        public static HullShape Inflate(HullShape hull, double distance)
        {
            int n = hull.Vertices.Length;
            var normalSum = new D3[n];
            var faceNormals = new List<D3>[n];
            for (int i = 0; i < n; i++) faceNormals[i] = new List<D3>();
            for (int t = 0; t + 2 < hull.Triangles.Length; t += 3)
            {
                D3 a = hull.Vertices[hull.Triangles[t]];
                D3 cross = D3.Cross(hull.Vertices[hull.Triangles[t + 1]] - a, hull.Vertices[hull.Triangles[t + 2]] - a);
                double length = cross.Length;
                if (!(length > 0)) continue;
                D3 normal = cross / length;
                for (int k = 0; k < 3; k++)
                {
                    int v = hull.Triangles[t + k];
                    normalSum[v] += cross;
                    faceNormals[v].Add(normal);
                }
            }

            var points = new D3[n];
            for (int i = 0; i < n; i++)
            {
                D3 dir = normalSum[i].NormalizedOr(D3.Zero);
                double minDot = 1;
                for (int k = 0; k < faceNormals[i].Count; k++) minDot = Math.Min(minDot, D3.Dot(faceNormals[i][k], dir));
                points[i] = hull.Vertices[i] + dir * (distance / Math.Max(0.3, minDot));
            }

            return HullShape.FromPoints(points, points.Length);
        }
    }

    internal static class Metrics
    {
        /// <summary>몬테카를로 커버리지/바깥 비율(작업 공간). insideSolid는 스레드 안전해야 한다.</summary>
        public static void Measure(D3 min, D3 max, int samples, Func<D3, List<int>, bool> insideSolid, IList<WorkPiece> pieces, out float coverage, out float outside)
        {
            Measure(min, max, samples, insideSolid, p =>
            {
                for (int k = 0; k < pieces.Count; k++)
                {
                    if (pieces[k].Contains(p)) return true;
                }

                return false;
            }, out coverage, out outside);
        }

        public static void Measure(D3 min, D3 max, int samples, Func<D3, List<int>, bool> insideSolid, Func<D3, bool> insidePieces, out float coverage, out float outside)
        {
            const int chunks = 64;
            var solidCount = new int[chunks];
            var coveredCount = new int[chunks];
            var pieceCount = new int[chunks];
            var outsideCount = new int[chunks];
            int perChunk = Math.Max(1, samples / chunks);
            D3 size = max - min;
            Parallel.For(0, chunks, chunk =>
            {
                var rng = new ConcaveMath.Rng((ulong)(chunk * 7919 + 17));
                var scratch = new List<int>();
                for (int i = 0; i < perChunk; i++)
                {
                    var p = new D3(min.X + rng.NextDouble() * size.X, min.Y + rng.NextDouble() * size.Y, min.Z + rng.NextDouble() * size.Z);
                    bool solid = insideSolid(p, scratch);
                    bool inPiece = insidePieces(p);

                    if (solid)
                    {
                        solidCount[chunk]++;
                        if (inPiece) coveredCount[chunk]++;
                    }

                    if (inPiece)
                    {
                        pieceCount[chunk]++;
                        if (!solid) outsideCount[chunk]++;
                    }
                }
            });

            int s = 0, c = 0, pc = 0, o = 0;
            for (int i = 0; i < chunks; i++)
            {
                s += solidCount[i];
                c += coveredCount[i];
                pc += pieceCount[i];
                o += outsideCount[i];
            }

            coverage = s > 0 ? c / (float)s : -1f;
            outside = pc > 0 ? o / (float)pc : -1f;
        }
    }

    /// <summary>정적 메시 파이프라인.</summary>
    internal static class StaticPipeline
    {
        public static void Execute(IList<Vector3> vertices, IList<int> triangles, ResolvedSettings settings, bool nonUniformScale, Reporter reporter, ConcaveDecompositionResult result)
        {
            reporter.Report(0f, "입력 메시 확인 중");
            if (vertices == null || triangles == null || vertices.Count < 4 || triangles.Count < 12)
                throw new ArgumentException("삼각형이 4개 미만이라 분해할 수 없습니다.");

            // 입력 정리: 범위 밖 인덱스·NaN·퇴화 삼각형 제거
            var raw = new D3[vertices.Count];
            for (int i = 0; i < raw.Length; i++) raw[i] = D3.From(vertices[i]);
            var cleanTriangles = new List<int>(triangles.Count);
            int dropped = 0;
            for (int t = 0; t + 2 < triangles.Count; t += 3)
            {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                if (a < 0 || b < 0 || c < 0 || a >= raw.Length || b >= raw.Length || c >= raw.Length || a == b || b == c || a == c ||
                    !raw[a].IsFinite || !raw[b].IsFinite || !raw[c].IsFinite)
                {
                    dropped++;
                    continue;
                }

                cleanTriangles.Add(a);
                cleanTriangles.Add(b);
                cleanTriangles.Add(c);
            }

            if (dropped > 0) result.Warnings.Add($"잘못된 삼각형 {dropped}개(범위 밖 인덱스, NaN, 중복 꼭짓점)를 건너뛰었습니다.");
            if (cleanTriangles.Count < 12) throw new ArgumentException("유효한 삼각형이 4개 미만이라 분해할 수 없습니다.");
            int[] tris = cleanTriangles.ToArray();

            // 사용된 정점만으로 프레임 선택: 회전된 메시는 OBB 축에서 복셀화하면 격자가 훨씬 촘촘해진다.
            var usedMask = new bool[raw.Length];
            var usedPoints = new List<D3>();
            for (int i = 0; i < tris.Length; i++)
            {
                if (usedMask[tris[i]]) continue;
                usedMask[tris[i]] = true;
                usedPoints.Add(raw[tris[i]]);
            }

            Frame3 frame = ChooseFrame(usedPoints.ToArray());

            // 작업 공간: 프레임 로컬 → 중심 0, 최대 변 1
            var rotated = new D3[raw.Length];
            D3 min = new D3(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity);
            D3 max = -min;
            for (int i = 0; i < raw.Length; i++)
            {
                rotated[i] = frame.ToLocal(raw[i]);
                if (!usedMask[i]) continue;
                min = D3.Min(min, rotated[i]);
                max = D3.Max(max, rotated[i]);
            }

            D3 size = max - min;
            double extent = Math.Max(size.X, Math.Max(size.Y, size.Z));
            if (!(extent > 0) || !ConcaveMath.IsFinite(extent)) throw new ArgumentException("메시 크기가 0이거나 유한하지 않습니다.");
            D3 center = (min + max) * 0.5;
            var work = new D3[raw.Length];
            for (int i = 0; i < raw.Length; i++) work[i] = (rotated[i] - center) / extent;

            var map = new OutputMap { Rotation = frame, Translation = frame.ToParent(center), Scale = extent };
            Frame3? forcedAxes = null;
            if (nonUniformScale)
            {
                forcedAxes = new Frame3
                {
                    AxisX = frame.ToLocal(new D3(1, 0, 0)),
                    AxisY = frame.ToLocal(new D3(0, 1, 0)),
                    AxisZ = frame.ToLocal(new D3(0, 0, 1))
                };
            }

            // 복셀화
            reporter.Report(0.03f, "복셀화 중");
            VoxelModel model = VoxelModel.Build(work, tris, settings.VoxelTarget, settings.MaxAxisVoxels, reporter.CancelCheck);
            int pad = VoxelModel.PaddingVoxels * 2;
            result.VoxelResolution = new Vector3Int(model.NX - pad, model.NY - pad, model.NZ - pad);
            result.VoxelCount = model.Count;
            result.VoxelSize = (float)(model.H * extent);
            result.Watertight = model.Watertight;
            result.OpenEdgeCount = model.OpenEdges;
            if (model.Count == 0) throw new InvalidOperationException("복셀화 결과가 비어 있습니다.");
            if (model.ShellOnly)
                result.Warnings.Add($"메시가 닫혀 있지 않아(열린 가장자리 {model.OpenEdges}개) 내부를 채우지 못했습니다. 표면 셸 기준으로 얇은 조각을 만들었습니다.");
            else if (model.Sealed)
                result.Warnings.Add($"메시에 작은 구멍이 있어(열린 가장자리 {model.OpenEdges}개) 복셀 단계에서 메운 뒤 분해했습니다.");
            else if (!model.Watertight)
                result.Warnings.Add($"메시에 열린 가장자리가 {model.OpenEdges}개 있지만 부피는 닫혀 있어 그대로 분해했습니다.");

            // 분할
            reporter.Report(0.15f, "오목한 부분 분할 중");
            var splitter = new VoxelSplitter(model, work, tris);
            List<VoxelPart> parts = splitter.InitialParts();
            settings.ResolveAutoPieces(splitter.InitialConcavityRatio);
            var parameters = new SplitParameters
            {
                MaxParts = Math.Max(settings.SplitLimit, parts.Count),
                Tolerance = settings.Tolerance,
                CandidatesPerAxis = settings.CandidatesPerAxis
            };

            parts = splitter.Split(parts, parameters, f => reporter.Tick(0.15f, 0.55f, f));
            result.SplitPartCount = parts.Count;

            // 정확한 껍질
            reporter.Report(0.55f, "조각 껍질 계산 중");
            HullShape[] hulls = ExactHullBuilder.Build(model, parts, splitter.CutPlanes, work, tris, reporter.CancelCheck);
            var pieces = new List<MergePiece>();
            for (int i = 0; i < hulls.Length; i++)
            {
                if (hulls[i] != null && hulls[i].Volume > 0) pieces.Add(MergePiece.From(hulls[i]));
            }

            if (pieces.Count == 0) throw new InvalidOperationException("유효한 볼록 조각을 만들지 못했습니다.");

            // 병합 + 먼지 제거
            reporter.Report(0.7f, "조각 병합 중");
            pieces = PieceMerger.Merge(pieces, settings.MaxPieces, settings.MergeTight, ResolvedSettings.TightMergeAir, reporter.CancelCheck);
            pieces = PieceMerger.DropDust(pieces, settings.MinPieceFraction, settings.DustFraction);

            // 프리미티브 피팅 / 정점 축소
            reporter.Report(0.82f, "프리미티브 피팅 중");
            double paddingWork = settings.Padding / extent;
            var finished = new WorkPiece[pieces.Count];
            Parallel.For(0, pieces.Count, i =>
            {
                finished[i] = PieceFinisher.Finish(pieces[i].Hull, settings, forcedAxes, paddingWork);
                finished[i].Map = map;
            });

            var ordered = new List<WorkPiece>(finished);
            ordered.Sort((a, b) => b.FullHull.Volume.CompareTo(a.FullHull.Volume));

            // 측정
            reporter.Report(0.92f, "커버리지 측정 중");
            D3 workMin = (min - center) / extent, workMax = (max - center) / extent;
            Metrics.Measure(workMin, workMax, settings.MetricSamples, (p, scratch) => model.InsideSolid(p, scratch), ordered, out float coverage, out float outside);
            result.Coverage = coverage;
            result.OutsideFraction = outside;
            result.MetricsApproximate = !model.Watertight;

            for (int i = 0; i < ordered.Count; i++) result.Pieces.Add(ordered[i].ToPiece(null));
            result.ResolvedQuality = settings.Quality;
            result.ResolvedMaxPieces = settings.MaxPieces;
            result.ResolvedTolerance = (float)settings.Tolerance;
            result.ResolvedMaxHullVertices = settings.MaxHullVertices;
            reporter.Report(1f, "완료");
        }

        private static Frame3 ChooseFrame(D3[] points)
        {
            if (points.Length < 4) return Frame3.Identity;
            D3[] subset = HullOps.ExtremeSubset(points, ConcaveMath.FibonacciDirections(256), ConcaveMath.SeedDirections);
            HullShape hull = HullShape.FromPoints(subset, subset.Length);
            if (hull == null) return Frame3.Identity;
            D3 size = hull.Size;
            double aabb = size.X * size.Y * size.Z;
            PrimitiveFit box = PrimitiveFitter.FitBox(hull, null);
            if (!box.Valid || !(aabb > 0)) return Frame3.Identity;
            return box.Volume < aabb * 0.85 ? box.Axes : Frame3.Identity;
        }
    }
}
