using System;
using UnityEngine;

namespace TelleR.ConcaveCollider
{
    /// <summary>
    /// 코어 내부 전용 double 벡터. Unity Vector3는 float이고 Normalize가 1e-5 미만에서 0을 돌려주므로
    /// (본 공간 초소형 좌표 함정) 모든 기하 계산은 이 타입으로 한다. 순수 C#이라 워커 스레드에서도 안전하다.
    /// </summary>
    internal struct D3
    {
        public double X, Y, Z;

        public D3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static readonly D3 Zero = new D3(0, 0, 0);

        public double this[int axis] => axis == 0 ? X : axis == 1 ? Y : Z;

        public static D3 operator +(D3 a, D3 b) => new D3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static D3 operator -(D3 a, D3 b) => new D3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static D3 operator -(D3 a) => new D3(-a.X, -a.Y, -a.Z);
        public static D3 operator *(D3 a, double s) => new D3(a.X * s, a.Y * s, a.Z * s);
        public static D3 operator *(double s, D3 a) => new D3(a.X * s, a.Y * s, a.Z * s);
        public static D3 operator /(D3 a, double s) => new D3(a.X / s, a.Y / s, a.Z / s);

        public static double Dot(D3 a, D3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        public static D3 Cross(D3 a, D3 b) =>
            new D3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

        public double SqrLength => X * X + Y * Y + Z * Z;
        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

        public static D3 Min(D3 a, D3 b) => new D3(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
        public static D3 Max(D3 a, D3 b) => new D3(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));

        /// <summary>길이가 0에 가까워도 0 벡터로 뭉개지 않는 정규화. 실패 시 fallback을 돌려준다.</summary>
        public D3 NormalizedOr(D3 fallback)
        {
            double length = Length;
            if (!(length > 1e-300) || double.IsInfinity(length)) return fallback;
            return new D3(X / length, Y / length, Z / length);
        }

        public bool IsFinite => ConcaveMath.IsFinite(X) && ConcaveMath.IsFinite(Y) && ConcaveMath.IsFinite(Z);

        public static D3 From(Vector3 v) => new D3(v.x, v.y, v.z);
        public Vector3 ToVector3() => new Vector3((float)X, (float)Y, (float)Z);

        public override string ToString() => $"({X:0.#####}, {Y:0.#####}, {Z:0.#####})";
    }

    /// <summary>정규직교 3축 회전(열벡터 = 로컬 축의 부모 공간 방향). det = +1을 유지한다.</summary>
    internal struct Frame3
    {
        public D3 AxisX, AxisY, AxisZ;

        public static readonly Frame3 Identity = new Frame3
        {
            AxisX = new D3(1, 0, 0),
            AxisY = new D3(0, 1, 0),
            AxisZ = new D3(0, 0, 1)
        };

        public D3 Axis(int i) => i == 0 ? AxisX : i == 1 ? AxisY : AxisZ;

        /// <summary>부모 공간 벡터를 이 프레임 로컬 좌표로.</summary>
        public D3 ToLocal(D3 v) => new D3(D3.Dot(v, AxisX), D3.Dot(v, AxisY), D3.Dot(v, AxisZ));

        /// <summary>로컬 좌표를 부모 공간 벡터로.</summary>
        public D3 ToParent(D3 v) => AxisX * v.X + AxisY * v.Y + AxisZ * v.Z;

        /// <summary>두 축으로부터 오른손 정규직교 프레임을 만든다(z = x × y).</summary>
        public static Frame3 FromAxes(D3 x, D3 y)
        {
            x = x.NormalizedOr(new D3(1, 0, 0));
            y = y - x * D3.Dot(x, y);
            y = y.NormalizedOr(Perpendicular(x));
            D3 z = D3.Cross(x, y).NormalizedOr(new D3(0, 0, 1));
            y = D3.Cross(z, x);
            return new Frame3 { AxisX = x, AxisY = y, AxisZ = z };
        }

        public static D3 Perpendicular(D3 v)
        {
            D3 a = Math.Abs(v.X) < 0.6 ? new D3(1, 0, 0) : new D3(0, 1, 0);
            return D3.Cross(v, a).NormalizedOr(new D3(0, 0, 1));
        }

        /// <summary>parent ← this ← child 합성: 결과 축 = this.ToParent(child 축).</summary>
        public Frame3 Compose(Frame3 child)
        {
            return new Frame3
            {
                AxisX = ToParent(child.AxisX),
                AxisY = ToParent(child.AxisY),
                AxisZ = ToParent(child.AxisZ)
            };
        }

        /// <summary>순수 C# 회전행렬 → 쿼터니언 변환(워커 스레드 안전).</summary>
        public Quaternion ToQuaternion()
        {
            double m00 = AxisX.X, m10 = AxisX.Y, m20 = AxisX.Z;
            double m01 = AxisY.X, m11 = AxisY.Y, m21 = AxisY.Z;
            double m02 = AxisZ.X, m12 = AxisZ.Y, m22 = AxisZ.Z;
            double trace = m00 + m11 + m22;
            double x, y, z, w;
            if (trace > 0)
            {
                double s = Math.Sqrt(trace + 1.0) * 2.0;
                w = 0.25 * s;
                x = (m21 - m12) / s;
                y = (m02 - m20) / s;
                z = (m10 - m01) / s;
            }
            else if (m00 > m11 && m00 > m22)
            {
                double s = Math.Sqrt(1.0 + m00 - m11 - m22) * 2.0;
                w = (m21 - m12) / s;
                x = 0.25 * s;
                y = (m01 + m10) / s;
                z = (m02 + m20) / s;
            }
            else if (m11 > m22)
            {
                double s = Math.Sqrt(1.0 + m11 - m00 - m22) * 2.0;
                w = (m02 - m20) / s;
                x = (m01 + m10) / s;
                y = 0.25 * s;
                z = (m12 + m21) / s;
            }
            else
            {
                double s = Math.Sqrt(1.0 + m22 - m00 - m11) * 2.0;
                w = (m10 - m01) / s;
                x = (m02 + m20) / s;
                y = (m12 + m21) / s;
                z = 0.25 * s;
            }

            double length = Math.Sqrt(x * x + y * y + z * z + w * w);
            if (!(length > 1e-12) || !ConcaveMath.IsFinite(length)) return Quaternion.identity;
            return new Quaternion((float)(x / length), (float)(y / length), (float)(z / length), (float)(w / length));
        }
    }

    /// <summary>개발용 진단 추적(기본 null = 비활성). 메인 스레드에서만 호출한다.</summary>
    internal static class ConcaveTrace
    {
        public static Action<string> Sink;

        public static bool Enabled => Sink != null;

        public static void Write(string message)
        {
            Sink?.Invoke(message);
        }
    }

    internal static class ConcaveMath
    {
        public static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        public static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        public static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
        public static double Clamp(double value, double min, double max) => value < min ? min : value > max ? max : value;

        /// <summary>점군의 평균과 주축(분산 큰 순). Jacobi 고유분해, 반복 횟수 제한.</summary>
        public static void PrincipalAxes(D3[] points, int count, out D3 mean, out Frame3 frame, out D3 variances)
        {
            mean = D3.Zero;
            for (int i = 0; i < count; i++) mean += points[i];
            mean /= Math.Max(1, count);

            var c = new double[3, 3];
            for (int i = 0; i < count; i++)
            {
                D3 d = points[i] - mean;
                c[0, 0] += d.X * d.X;
                c[0, 1] += d.X * d.Y;
                c[0, 2] += d.X * d.Z;
                c[1, 1] += d.Y * d.Y;
                c[1, 2] += d.Y * d.Z;
                c[2, 2] += d.Z * d.Z;
            }

            c[1, 0] = c[0, 1];
            c[2, 0] = c[0, 2];
            c[2, 1] = c[1, 2];
            SymmetricEigen(c, out double[] values, out D3[] vectors);
            int[] order = { 0, 1, 2 };
            Array.Sort(order, (a, b) => values[b].CompareTo(values[a]));
            frame = Frame3.FromAxes(vectors[order[0]], vectors[order[1]]);
            double norm = Math.Max(1, count);
            variances = new D3(values[order[0]] / norm, values[order[1]] / norm, values[order[2]] / norm);
        }

        /// <summary>3x3 대칭행렬 Jacobi 고유분해. 입력 행렬은 파괴된다.</summary>
        public static void SymmetricEigen(double[,] a, out double[] values, out D3[] vectors)
        {
            var v = new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
            double scale = Math.Abs(a[0, 0]) + Math.Abs(a[1, 1]) + Math.Abs(a[2, 2]) + 1e-300;
            for (int sweep = 0; sweep < 50; sweep++)
            {
                double off = Math.Abs(a[0, 1]) + Math.Abs(a[0, 2]) + Math.Abs(a[1, 2]);
                if (off <= scale * 1e-15) break;
                for (int p = 0; p < 2; p++)
                {
                    for (int q = p + 1; q < 3; q++)
                    {
                        if (Math.Abs(a[p, q]) <= scale * 1e-18) continue;
                        double theta = (a[q, q] - a[p, p]) / (2.0 * a[p, q]);
                        double t = (theta >= 0 ? 1.0 : -1.0) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1.0));
                        double cs = 1.0 / Math.Sqrt(t * t + 1.0);
                        double sn = t * cs;
                        for (int k = 0; k < 3; k++)
                        {
                            double akp = a[k, p];
                            double akq = a[k, q];
                            a[k, p] = cs * akp - sn * akq;
                            a[k, q] = sn * akp + cs * akq;
                        }

                        for (int k = 0; k < 3; k++)
                        {
                            double apk = a[p, k];
                            double aqk = a[q, k];
                            a[p, k] = cs * apk - sn * aqk;
                            a[q, k] = sn * apk + cs * aqk;
                        }

                        for (int k = 0; k < 3; k++)
                        {
                            double vkp = v[k, p];
                            double vkq = v[k, q];
                            v[k, p] = cs * vkp - sn * vkq;
                            v[k, q] = sn * vkp + cs * vkq;
                        }
                    }
                }
            }

            values = new[] { a[0, 0], a[1, 1], a[2, 2] };
            vectors = new D3[3];
            for (int k = 0; k < 3; k++)
                vectors[k] = new D3(v[0, k], v[1, k], v[2, k]).NormalizedOr(k == 0 ? new D3(1, 0, 0) : k == 1 ? new D3(0, 1, 0) : new D3(0, 0, 1));
        }

        /// <summary>구 위에 고르게 퍼진 방향(피보나치 격자). 극값 점 샘플링용.</summary>
        public static D3[] FibonacciDirections(int count)
        {
            var result = new D3[count];
            double golden = Math.PI * (3.0 - Math.Sqrt(5.0));
            for (int i = 0; i < count; i++)
            {
                double y = 1.0 - 2.0 * (i + 0.5) / count;
                double r = Math.Sqrt(Math.Max(0.0, 1.0 - y * y));
                double phi = golden * i;
                result[i] = new D3(Math.Cos(phi) * r, y, Math.Sin(phi) * r);
            }

            return result;
        }

        /// <summary>EMBER 정점 축소 시드: 6축 + 8대각 방향.</summary>
        public static readonly D3[] SeedDirections =
        {
            new D3(1, 0, 0), new D3(-1, 0, 0), new D3(0, 1, 0), new D3(0, -1, 0), new D3(0, 0, 1), new D3(0, 0, -1),
            new D3(1, 1, 1), new D3(1, 1, -1), new D3(1, -1, 1), new D3(1, -1, -1),
            new D3(-1, 1, 1), new D3(-1, 1, -1), new D3(-1, -1, 1), new D3(-1, -1, -1)
        };

        /// <summary>결정적 난수(xorshift). 스레드마다 별도 인스턴스를 쓴다.</summary>
        internal struct Rng
        {
            private ulong state;

            public Rng(ulong seed)
            {
                state = seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;
                if (state == 0) state = 0x2545F4914F6CDD1DUL;
            }

            public double NextDouble()
            {
                state ^= state << 13;
                state ^= state >> 7;
                state ^= state << 17;
                return (state >> 11) * (1.0 / 9007199254740992.0);
            }
        }
    }
}
