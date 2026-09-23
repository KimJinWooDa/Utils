using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TelleR.ConcaveCollider
{
    /// <summary>병합 단계의 조각(정확한 껍질 + 비용 평가용 대리 껍질).</summary>
    internal sealed class MergePiece
    {
        public HullShape Hull;
        public HullShape Proxy;

        /// <summary>합쳐진 원래 조각들의 대리 껍질 부피 합(누적 빈 공간 판정 기준, EMBER "원래 멤버 대비").</summary>
        public double BaseVolume;

        public int MemberCount = 1;

        public static MergePiece From(HullShape hull)
        {
            HullShape proxy = HullOps.Proxy(hull);
            return new MergePiece { Hull = hull, Proxy = proxy, BaseVolume = proxy.Volume };
        }
    }

    /// <summary>
    /// 탐욕 병합. 1단계: 조각 수가 maxPieces 이하가 될 때까지 늘어나는 껍질 부피가 가장 작은 쌍을 합친다(V-HACD).
    /// 2단계(선택): 합친 껍질의 빈 공간이 원래 멤버 대비 tightAir(5%) 이하인 쌍을 계속 합친다(EMBER).
    /// </summary>
    internal static class PieceMerger
    {
        public static List<MergePiece> Merge(List<MergePiece> pieces, int maxPieces, bool mergeTight, double tightAir, Func<bool> cancelled)
        {
            int n = pieces.Count;
            if (n <= 1) return pieces;

            var cost = new double[n, n];
            var air = new double[n, n];
            var merged = new HullShape[n, n];
            var pairs = new List<(int, int)>();
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++) pairs.Add((i, j));
            }

            EvaluatePairs(pieces, pairs, cost, air, merged);

            while (pieces.Count > 1)
            {
                if (cancelled()) throw new OperationCanceledException();
                int count = pieces.Count;
                int bestI = -1, bestJ = -1;
                bool forced = count > maxPieces;
                double best = double.PositiveInfinity;
                for (int i = 0; i < count; i++)
                {
                    for (int j = i + 1; j < count; j++)
                    {
                        if (merged[i, j] == null) continue;
                        double value = forced ? cost[i, j] : air[i, j];
                        if (value >= best) continue;
                        best = value;
                        bestI = i;
                        bestJ = j;
                    }
                }

                if (bestI < 0) break;
                if (!forced && (!mergeTight || best > tightAir)) break;

                if (ConcaveTrace.Enabled) ConcaveTrace.Write($"merge {(forced ? "forced" : "tight")} {bestI}+{bestJ} cost={cost[bestI, bestJ]:0.000000} air={air[bestI, bestJ]:0.0000} count={count}");
                MergePiece a = pieces[bestI], b = pieces[bestJ];
                HullShape exact = HullOps.Union(a.Hull, b.Hull) ?? merged[bestI, bestJ];
                var combined = new MergePiece
                {
                    Hull = exact,
                    Proxy = HullOps.Proxy(exact),
                    BaseVolume = a.BaseVolume + b.BaseVolume,
                    MemberCount = a.MemberCount + b.MemberCount
                };

                pieces[bestI] = combined;
                pieces.RemoveAt(bestJ);
                RemoveIndex(ref cost, bestJ, count);
                RemoveIndex(ref air, bestJ, count);
                RemoveIndex(ref merged, bestJ, count);

                var update = new List<(int, int)>();
                for (int k = 0; k < pieces.Count; k++)
                {
                    if (k == bestI) continue;
                    update.Add(k < bestI ? (k, bestI) : (bestI, k));
                }

                EvaluatePairs(pieces, update, cost, air, merged);
            }

            return pieces;
        }

        private static void EvaluatePairs(List<MergePiece> pieces, List<(int, int)> pairs, double[,] cost, double[,] air, HullShape[,] merged)
        {
            Parallel.For(0, pairs.Count, k =>
            {
                (int i, int j) = pairs[k];
                MergePiece a = pieces[i], b = pieces[j];
                HullShape union = HullOps.Union(a.Proxy, b.Proxy);
                if (union == null)
                {
                    merged[i, j] = null;
                    cost[i, j] = double.PositiveInfinity;
                    air[i, j] = double.PositiveInfinity;
                    return;
                }

                merged[i, j] = union;
                cost[i, j] = union.Volume - a.Proxy.Volume - b.Proxy.Volume;
                air[i, j] = Math.Max(0, (union.Volume - a.BaseVolume - b.BaseVolume) / union.Volume);
            });
        }

        private static void RemoveIndex<T>(ref T[,] matrix, int removed, int count)
        {
            var result = new T[count - 1, count - 1];
            for (int i = 0, ri = 0; i < count; i++)
            {
                if (i == removed) continue;
                for (int j = 0, rj = 0; j < count; j++)
                {
                    if (j == removed) continue;
                    result[ri, rj] = matrix[i, j];
                    rj++;
                }

                ri++;
            }

            matrix = result;
        }

        /// <summary>
        /// EMBER 먼지 규칙: 전체 부피의 dust 미만 조각은 제거, minFraction 미만 조각은 다른 조각에
        /// 60% 이상 덮여 있으면 제거. 가장 큰 조각은 항상 남긴다.
        /// </summary>
        public static List<MergePiece> DropDust(List<MergePiece> pieces, double minFraction, double dustFraction)
        {
            if (pieces.Count <= 1 || minFraction <= 0) return pieces;
            pieces.Sort((x, y) => y.Hull.Volume.CompareTo(x.Hull.Volume));
            double total = 0;
            for (int i = 0; i < pieces.Count; i++) total += pieces[i].Hull.Volume;
            for (int i = pieces.Count - 1; i > 0; i--)
            {
                double volume = pieces[i].Hull.Volume;
                if (volume >= total * minFraction) break;
                if (volume >= total * dustFraction)
                {
                    var others = new List<HullShape>(pieces.Count - 1);
                    for (int k = 0; k < pieces.Count; k++)
                    {
                        if (k != i) others.Add(pieces[k].Proxy);
                    }

                    if (HullOps.CoveredFraction(pieces[i].Hull, others, 3000, 11) < 0.6) continue;
                }

                pieces.RemoveAt(i);
            }

            return pieces;
        }
    }
}
