using System;
using System.Collections.Generic;

namespace TelleR.ConcaveCollider
{
    /// <summary>작업 공간 프리미티브(모든 값은 점들을 감싼다).</summary>
    internal struct PrimitiveFit
    {
        public ConcavePieceKind Kind;
        public D3 Center;
        public Frame3 Axes;
        public D3 HalfExtents;
        public double Radius;
        public double HalfSegment;
        public int Direction;
        public double Volume;
        public bool Valid;

        public bool Contains(D3 p)
        {
            switch (Kind)
            {
                case ConcavePieceKind.Box:
                {
                    D3 local = Axes.ToLocal(p - Center);
                    return Math.Abs(local.X) <= HalfExtents.X && Math.Abs(local.Y) <= HalfExtents.Y && Math.Abs(local.Z) <= HalfExtents.Z;
                }
                case ConcavePieceKind.Sphere:
                    return (p - Center).SqrLength <= Radius * Radius;
                case ConcavePieceKind.Capsule:
                {
                    D3 local = Axes.ToLocal(p - Center);
                    double axial = local[Direction];
                    double radial2 = local.SqrLength - axial * axial;
                    double outside = Math.Max(0, Math.Abs(axial) - HalfSegment);
                    return radial2 + outside * outside <= Radius * Radius;
                }
                default:
                    return false;
            }
        }

        public static double CapsuleVolume(double radius, double halfSegment) =>
            Math.PI * radius * radius * halfSegment * 2 + 4.0 / 3.0 * Math.PI * radius * radius * radius;
    }

    /// <summary>
    /// 껍질에 맞는 프리미티브 피팅. Box = 최소 부피 OBB 근사(좌표축, PCA, 껍질 면 법선 + 회전 캘리퍼스,
    /// 그 뒤 소각도 언덕 오르기), Sphere = 반복 횟수가 제한된 Badoiu-Clarkson + Ritter(Welzl 미사용),
    /// Capsule = OBB 축별 선분 길이 탐색(EMBER 방식).
    /// </summary>
    internal static class PrimitiveFitter
    {
        private static readonly D3[] FitPoolDirections = ConcaveMath.FibonacciDirections(160);

        public static PrimitiveFit FitBox(HullShape hull, Frame3? forcedAxes)
        {
            D3[] all = hull.Vertices;
            if (forcedAxes.HasValue) return BoxFromFrame(all, forcedAxes.Value);

            D3[] pool = all.Length > FitPoolDirections.Length ? HullOps.ExtremeSubset(all, FitPoolDirections, ConcaveMath.SeedDirections) : all;
            var candidates = new List<Frame3> { Frame3.Identity };
            ConcaveMath.PrincipalAxes(pool, pool.Length, out D3 _, out Frame3 pca, out D3 _);
            candidates.Add(pca);

            // 면적 큰 껍질 면 법선 + 2D 최소 사각형
            var faces = new List<(double area, D3 normal)>();
            for (int t = 0; t + 2 < hull.Triangles.Length; t += 3)
            {
                D3 a = hull.Vertices[hull.Triangles[t]];
                D3 cross = D3.Cross(hull.Vertices[hull.Triangles[t + 1]] - a, hull.Vertices[hull.Triangles[t + 2]] - a);
                double length = cross.Length;
                if (!(length > 0)) continue;
                faces.Add((length, cross / length));
            }

            faces.Sort((x, y) => y.area.CompareTo(x.area));
            var usedNormals = new List<D3>();
            for (int i = 0; i < faces.Count && usedNormals.Count < 40; i++)
            {
                D3 n = faces[i].normal;
                bool duplicate = false;
                for (int k = 0; k < usedNormals.Count; k++)
                {
                    if (Math.Abs(D3.Dot(usedNormals[k], n)) > 0.9995)
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (duplicate) continue;
                usedNormals.Add(n);
                if (TryMinRectFrame(pool, n, out Frame3 frame)) candidates.Add(frame);
            }

            Frame3 best = candidates[0];
            double bestVolume = double.PositiveInfinity;
            for (int i = 0; i < candidates.Count; i++)
            {
                double volume = FrameVolume(pool, candidates[i]);
                if (volume >= bestVolume) continue;
                bestVolume = volume;
                best = candidates[i];
            }

            // 소각도 언덕 오르기
            double[] steps = { 6.0, 3.0, 1.5, 0.75, 0.35 };
            for (int s = 0; s < steps.Length; s++)
            {
                double angle = steps[s] * Math.PI / 180.0;
                bool improved = true;
                int guard = 0;
                while (improved && guard++ < 24)
                {
                    improved = false;
                    for (int axis = 0; axis < 3; axis++)
                    {
                        for (int sign = -1; sign <= 1; sign += 2)
                        {
                            Frame3 rotated = Rotate(best, axis, angle * sign);
                            double volume = FrameVolume(pool, rotated);
                            if (volume >= bestVolume * (1 - 1e-9)) continue;
                            bestVolume = volume;
                            best = rotated;
                            improved = true;
                        }
                    }
                }
            }

            return BoxFromFrame(all, best);
        }

        private static Frame3 Rotate(Frame3 f, int axis, double angle)
        {
            double c = Math.Cos(angle), s = Math.Sin(angle);
            switch (axis)
            {
                case 0: return Frame3.FromAxes(f.AxisX, f.AxisY * c + f.AxisZ * s);
                case 1: return Frame3.FromAxes(f.AxisX * c - f.AxisZ * s, f.AxisY);
                default: return Frame3.FromAxes(f.AxisX * c + f.AxisY * s, f.AxisY * c - f.AxisX * s);
            }
        }

        private static double FrameVolume(D3[] points, Frame3 frame)
        {
            D3 min = frame.ToLocal(points[0]), max = min;
            for (int i = 1; i < points.Length; i++)
            {
                D3 local = frame.ToLocal(points[i]);
                min = D3.Min(min, local);
                max = D3.Max(max, local);
            }

            D3 size = max - min;
            return size.X * size.Y * size.Z;
        }

        public static PrimitiveFit BoxFromFrame(D3[] points, Frame3 frame)
        {
            D3 min = frame.ToLocal(points[0]), max = min;
            for (int i = 1; i < points.Length; i++)
            {
                D3 local = frame.ToLocal(points[i]);
                min = D3.Min(min, local);
                max = D3.Max(max, local);
            }

            D3 half = (max - min) * 0.5;
            D3 centerLocal = (max + min) * 0.5;
            return new PrimitiveFit
            {
                Kind = ConcavePieceKind.Box,
                Center = frame.ToParent(centerLocal),
                Axes = frame,
                HalfExtents = half,
                Volume = 8 * half.X * half.Y * half.Z,
                Valid = half.X > 0 && half.Y > 0 && half.Z > 0
            };
        }

        // 법선 n에 수직인 평면으로 투영한 점들의 최소 면적 사각형(회전 캘리퍼스) → 프레임
        private static bool TryMinRectFrame(D3[] points, D3 n, out Frame3 frame)
        {
            frame = Frame3.Identity;
            D3 e1 = Frame3.Perpendicular(n);
            D3 e2 = D3.Cross(n, e1);
            var projected = new List<(double x, double y)>(points.Length);
            for (int i = 0; i < points.Length; i++) projected.Add((D3.Dot(points[i], e1), D3.Dot(points[i], e2)));
            List<(double x, double y)> hull2 = ConvexHull2D(projected);
            if (hull2.Count < 3) return false;

            double bestArea = double.PositiveInfinity;
            double bestX = 1, bestY = 0;
            for (int i = 0; i < hull2.Count; i++)
            {
                var a = hull2[i];
                var b = hull2[(i + 1) % hull2.Count];
                double dx = b.x - a.x, dy = b.y - a.y;
                double length = Math.Sqrt(dx * dx + dy * dy);
                if (!(length > 0)) continue;
                dx /= length;
                dy /= length;
                double minU = double.PositiveInfinity, maxU = double.NegativeInfinity, minV = double.PositiveInfinity, maxV = double.NegativeInfinity;
                for (int k = 0; k < hull2.Count; k++)
                {
                    double u = hull2[k].x * dx + hull2[k].y * dy;
                    double v = -hull2[k].x * dy + hull2[k].y * dx;
                    if (u < minU) minU = u;
                    if (u > maxU) maxU = u;
                    if (v < minV) minV = v;
                    if (v > maxV) maxV = v;
                }

                double area = (maxU - minU) * (maxV - minV);
                if (area >= bestArea) continue;
                bestArea = area;
                bestX = dx;
                bestY = dy;
            }

            D3 axisU = e1 * bestX + e2 * bestY;
            frame = Frame3.FromAxes(axisU, D3.Cross(n, axisU));
            return true;
        }

        private static List<(double x, double y)> ConvexHull2D(List<(double x, double y)> points)
        {
            points.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            var hull = new List<(double x, double y)>(points.Count + 1);
            for (int pass = 0; pass < 2; pass++)
            {
                int start = hull.Count;
                for (int k = 0; k < points.Count; k++)
                {
                    var p = pass == 0 ? points[k] : points[points.Count - 1 - k];
                    while (hull.Count >= start + 2)
                    {
                        var o = hull[hull.Count - 2];
                        var a = hull[hull.Count - 1];
                        double cross = (a.x - o.x) * (p.y - o.y) - (a.y - o.y) * (p.x - o.x);
                        if (cross > 0) break;
                        hull.RemoveAt(hull.Count - 1);
                    }

                    hull.Add(p);
                }

                hull.RemoveAt(hull.Count - 1);
            }

            return hull;
        }

        /// <summary>반복 횟수 제한 구 피팅(Ritter 초기값 + Badoiu-Clarkson 100회). 결과는 모든 점을 감싼다.</summary>
        public static PrimitiveFit FitSphere(D3[] points)
        {
            // Ritter
            D3 p0 = points[0];
            D3 p1 = Farthest(points, p0);
            D3 p2 = Farthest(points, p1);
            D3 center = (p1 + p2) * 0.5;
            double radius = (p2 - p1).Length * 0.5;
            for (int i = 0; i < points.Length; i++)
            {
                double d = (points[i] - center).Length;
                if (d <= radius) continue;
                double newRadius = (radius + d) * 0.5;
                center += (points[i] - center) * ((newRadius - radius) / d);
                radius = newRadius;
            }

            radius = MaxDistance(points, center);

            // Badoiu-Clarkson 코어셋 반복
            D3 c = D3.Zero;
            for (int i = 0; i < points.Length; i++) c += points[i];
            c /= points.Length;
            for (int iteration = 1; iteration <= 100; iteration++)
            {
                D3 far = Farthest(points, c);
                c += (far - c) * (1.0 / (iteration + 1));
            }

            double r2 = MaxDistance(points, c);
            if (r2 < radius)
            {
                radius = r2;
                center = c;
            }

            return new PrimitiveFit
            {
                Kind = ConcavePieceKind.Sphere,
                Center = center,
                Axes = Frame3.Identity,
                Radius = radius,
                Volume = 4.0 / 3.0 * Math.PI * radius * radius * radius,
                Valid = radius > 0 && ConcaveMath.IsFinite(radius)
            };
        }

        private static D3 Farthest(D3[] points, D3 from)
        {
            D3 best = points[0];
            double bestDistance = -1;
            for (int i = 0; i < points.Length; i++)
            {
                double d = (points[i] - from).SqrLength;
                if (d <= bestDistance) continue;
                bestDistance = d;
                best = points[i];
            }

            return best;
        }

        private static double MaxDistance(D3[] points, D3 center)
        {
            double max = 0;
            for (int i = 0; i < points.Length; i++) max = Math.Max(max, (points[i] - center).SqrLength);
            return Math.Sqrt(max);
        }

        /// <summary>
        /// 캡슐 피팅(EMBER): 기준 프레임 세 축 각각에 대해 선분 반길이를 샘플링하며 필요 반지름을 계산,
        /// 부피가 가장 작은 조합을 고른다. 결과는 모든 점을 감싼다.
        /// </summary>
        public static PrimitiveFit FitCapsule(D3[] points, Frame3 basis)
        {
            int n = points.Length;
            var local = new D3[n];
            D3 min = basis.ToLocal(points[0]), max = min;
            for (int i = 0; i < n; i++)
            {
                local[i] = basis.ToLocal(points[i]);
                min = D3.Min(min, local[i]);
                max = D3.Max(max, local[i]);
            }

            D3 localCenter = (min + max) * 0.5;
            for (int i = 0; i < n; i++) local[i] -= localCenter;

            var axial = new double[n];
            var radial2 = new double[n];
            int bestDirection = 1;
            double bestHalf = 0, bestRadius = double.PositiveInfinity, bestVolume = double.PositiveInfinity;
            const int samples = 64;
            for (int direction = 0; direction < 3; direction++)
            {
                double maxHalf = 0;
                for (int i = 0; i < n; i++)
                {
                    axial[i] = local[i][direction];
                    radial2[i] = local[i].SqrLength - axial[i] * axial[i];
                    maxHalf = Math.Max(maxHalf, Math.Abs(axial[i]));
                }

                for (int s = 0; s <= samples; s++)
                {
                    double half = maxHalf * s / samples;
                    double required = 0;
                    for (int i = 0; i < n; i++)
                    {
                        double outside = Math.Max(0, Math.Abs(axial[i]) - half);
                        double r2 = radial2[i] + outside * outside;
                        if (r2 > required) required = r2;
                    }

                    double radius = Math.Sqrt(required);
                    double volume = PrimitiveFit.CapsuleVolume(radius, half);
                    if (volume >= bestVolume) continue;
                    bestVolume = volume;
                    bestRadius = radius;
                    bestHalf = half;
                    bestDirection = direction;
                }
            }

            return new PrimitiveFit
            {
                Kind = ConcavePieceKind.Capsule,
                Center = basis.ToParent(localCenter),
                Axes = basis,
                Radius = bestRadius,
                HalfSegment = bestHalf,
                Direction = bestDirection,
                Volume = bestVolume,
                Valid = bestRadius > 0 && ConcaveMath.IsFinite(bestVolume)
            };
        }

        /// <summary>
        /// 채움 비율(껍질 부피 / 프리미티브 부피)이 임계값 이상인 프리미티브 중 가장 꽉 찬 것.
        /// 비슷하면(0.01 이내) Sphere > Capsule > Box 순으로 싼 것을 고른다. 없으면 Valid = false.
        /// </summary>
        public static PrimitiveFit Choose(HullShape hull, bool box, bool sphere, bool capsule, double threshold, Frame3? forcedAxes)
        {
            var best = new PrimitiveFit();
            double bestScore = double.NegativeInfinity;
            if (hull == null || hull.Volume <= 0) return best;

            PrimitiveFit boxFit = default;
            bool haveBox = false;
            if (box || (capsule && !forcedAxes.HasValue))
            {
                boxFit = FitBox(hull, forcedAxes);
                haveBox = boxFit.Valid;
            }

            if (forcedAxes.HasValue)
            {
                sphere = false;
                capsule = false;
            }

            if (sphere)
            {
                PrimitiveFit fit = FitSphere(hull.Vertices);
                Consider(fit, 0.02);
            }

            if (capsule && haveBox)
            {
                // OBB 축과 PCA 축(패싯 원기둥에서 OBB가 살짝 기울어도 PCA 장축은 정확) 중 부피가 작은 쪽
                PrimitiveFit fit = FitCapsule(hull.Vertices, boxFit.Axes);
                ConcaveMath.PrincipalAxes(hull.Vertices, hull.Vertices.Length, out D3 _, out Frame3 pca, out D3 _);
                PrimitiveFit pcaFit = FitCapsule(hull.Vertices, pca);
                if (pcaFit.Valid && (!fit.Valid || pcaFit.Volume < fit.Volume)) fit = pcaFit;
                Consider(fit, 0.01);
            }

            if (box && haveBox) Consider(boxFit, 0);
            return best;

            void Consider(PrimitiveFit fit, double preference)
            {
                if (!fit.Valid || !(fit.Volume > 0)) return;
                double fill = hull.Volume / fit.Volume;
                if (fill < threshold) return;
                double score = fill + preference;
                if (score <= bestScore) return;
                bestScore = score;
                best = fit;
            }
        }
    }
}
