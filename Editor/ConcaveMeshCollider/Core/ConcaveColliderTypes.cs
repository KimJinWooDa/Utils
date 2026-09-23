using System;
using System.Collections.Generic;
using UnityEngine;

namespace TelleR.ConcaveCollider
{
    /// <summary>분해 품질 프리셋. 복셀 해상도·오목도 허용치·분할 상한을 함께 정한다.</summary>
    public enum ConcaveQuality
    {
        Fast,
        Balanced,
        Precise
    }

    /// <summary>생성될 콜라이더 종류.</summary>
    public enum ConcavePieceKind
    {
        ConvexMesh,
        Box,
        Sphere,
        Capsule
    }

    /// <summary>
    /// Concave Mesh Collider 분해 설정. 에디터 창에 그대로 직렬화해서 쓸 수 있는 순수 데이터 클래스다.
    /// auto가 켜져 있으면 수치 항목은 메시 크기·복잡도로부터 자동으로 정해지고,
    /// 정책 항목(excludedBoneKeywords, groupBonesByRole, clipAtJoints)만 그대로 쓴다.
    /// </summary>
    [Serializable]
    public sealed class ConcaveColliderSettings
    {
        [Tooltip("켜면 Balanced 품질(복셀 150k)로 고정하고, 조각 수(4~12)만 메시의 오목한 정도로부터 자동으로 정합니다.")]
        public bool auto = true;

        [Tooltip("Fast: 빠른 미리보기, Balanced: 일반 소품, Precise: 오목한 부분이 많은 형상(컵, 링 등).")]
        public ConcaveQuality quality = ConcaveQuality.Balanced;

        [Tooltip("최종 조각(콜라이더) 최대 개수입니다.")]
        [Range(1, 32)] public int maxPieces = 8;

        [Tooltip("조각 안에 허용할 빈 공간 비율(전체 볼록 껍질 부피 대비). 작을수록 더 잘게 나눕니다.")]
        [Range(0.001f, 0.1f)] public float concavityTolerance = 0.01f;

        [Tooltip("전체 부피 대비 이보다 작은 조각은 다른 조각에 대부분 덮여 있을 때 제거합니다.")]
        [Range(0f, 0.1f)] public float minPieceVolumeFraction = 0.015f;

        [Tooltip("볼록 메시 조각의 최대 정점 수. PhysX 쿠킹 한도를 넘지 않도록 64 이하로 제한됩니다.")]
        [Range(8, 64)] public int maxHullVertices = 48;

        [Tooltip("조각이 상자에 충분히 꽉 차면 BoxCollider로 바꿉니다.")]
        public bool fitBoxes = true;

        [Tooltip("조각이 구에 충분히 꽉 차면 SphereCollider로 바꿉니다.")]
        public bool fitSpheres = true;

        [Tooltip("조각이 캡슐에 충분히 꽉 차면 CapsuleCollider로 바꿉니다.")]
        public bool fitCapsules = true;

        [Tooltip("조각 부피 / 프리미티브 부피가 이 값 이상일 때만 프리미티브를 씁니다.")]
        [Range(0.5f, 1f)] public float primitiveFillThreshold = 0.8f;

        [Tooltip("모든 조각을 바깥쪽으로 이만큼 키웁니다(대상 로컬 단위).")]
        [Min(0f)] public float padding;

        [Tooltip("합쳐도 빈 공간이 5% 이하로 늘어나는 조각 쌍은 하나로 합칩니다.")]
        public bool mergeTightPieces = true;

        [Tooltip("복셀 수 직접 지정(0이면 품질 프리셋 값). 높을수록 정밀하지만 느립니다.")]
        [Range(0, 400000)] public int voxelResolution;

        [Tooltip("스킨드 메시에서 본 조각 최대 개수입니다.")]
        [Range(1, 32)] public int maxBonePieces = 10;

        [Tooltip("본 이름(head, arm, leg, spine...)으로 부위를 묶어 좌우 대칭 조각을 만듭니다.")]
        public bool groupBonesByRole = true;

        [Tooltip("이름에 이 단어가 들어간 본은 콜라이더에서 제외합니다. 쉼표로 구분합니다(예: tail, hair).")]
        public string excludedBoneKeywords = "tail";

        [Tooltip("인접한 본 조각을 관절 평면에서 잘라 서로 겹치지 않게 합니다.")]
        public bool clipAtJoints = true;

        [Tooltip("전체 부피 대비 이보다 작은 본 그룹은 이웃 그룹에 합치거나 제거합니다.")]
        [Range(0f, 0.2f)] public float minBoneVolumeFraction = 0.03f;

        public ConcaveColliderSettings Clone()
        {
            return (ConcaveColliderSettings)MemberwiseClone();
        }

        /// <summary>범위를 벗어난 값을 허용 범위로 되돌린다.</summary>
        public void Validate()
        {
            maxPieces = ConcaveMath.Clamp(maxPieces, 1, 32);
            concavityTolerance = Mathf.Clamp(float.IsNaN(concavityTolerance) ? 0.01f : concavityTolerance, 0.001f, 0.1f);
            minPieceVolumeFraction = Mathf.Clamp(float.IsNaN(minPieceVolumeFraction) ? 0.015f : minPieceVolumeFraction, 0f, 0.1f);
            maxHullVertices = ConcaveMath.Clamp(maxHullVertices, 8, 64);
            primitiveFillThreshold = Mathf.Clamp(float.IsNaN(primitiveFillThreshold) ? 0.8f : primitiveFillThreshold, 0.5f, 1f);
            padding = Mathf.Max(0f, float.IsNaN(padding) ? 0f : padding);
            voxelResolution = ConcaveMath.Clamp(voxelResolution, 0, 400000);
            maxBonePieces = ConcaveMath.Clamp(maxBonePieces, 1, 32);
            minBoneVolumeFraction = Mathf.Clamp(float.IsNaN(minBoneVolumeFraction) ? 0.03f : minBoneVolumeFraction, 0f, 0.2f);
            if (excludedBoneKeywords == null) excludedBoneKeywords = string.Empty;
        }

        /// <summary>excludedBoneKeywords를 소문자 단어 배열로 나눈다.</summary>
        public string[] ParseExcludedBoneKeywords()
        {
            if (string.IsNullOrEmpty(excludedBoneKeywords)) return Array.Empty<string>();
            string[] raw = excludedBoneKeywords.Split(new[] { ',', ';', '\n', '|' }, StringSplitOptions.RemoveEmptyEntries);
            var result = new List<string>(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                string keyword = raw[i].Trim().ToLowerInvariant();
                if (keyword.Length > 0 && !result.Contains(keyword)) result.Add(keyword);
            }

            return result.ToArray();
        }
    }

    /// <summary>
    /// 분해 결과 조각 하나. 좌표는 입력 공간(정적 메시 = 대상 루트 로컬, 스킨드 = 해당 본의 로컬)이다.
    /// 콜라이더 GameObject를 localPosition = Center, localRotation = Rotation, localScale = 1로 두면
    /// 프리미티브 파라미터를 그대로 대입할 수 있다. ConvexMesh는 Vertices/Triangles가 이미 입력 공간이므로
    /// 해당 GameObject를 원점·항등 회전에 둔다(Center = 0, Rotation = identity로 채워져 있다).
    /// </summary>
    public sealed class ConcavePiece
    {
        public ConcavePieceKind Kind;

        /// <summary>
        /// 볼록 껍질(바깥쪽 감김). ConvexMesh에서는 콜라이더 메시 그대로이고(정점 ≤ maxHullVertices),
        /// 프리미티브에서는 피팅에 쓴 껍질(미리보기용)이다. 좌표는 콜라이더 GameObject 로컬
        /// (localScale = LocalScale이 적용된 공간)이다.
        /// </summary>
        public Vector3[] Vertices;

        public int[] Triangles;

        /// <summary>프리미티브 중심(콜라이더 GameObject의 localPosition). ConvexMesh는 0.</summary>
        public Vector3 Center;

        /// <summary>프리미티브 회전(localRotation). Sphere/ConvexMesh는 identity.</summary>
        public Quaternion Rotation = Quaternion.identity;

        /// <summary>BoxCollider.size.</summary>
        public Vector3 Size;

        /// <summary>SphereCollider.radius / CapsuleCollider.radius.</summary>
        public float Radius;

        /// <summary>CapsuleCollider.height(양 끝 반구 포함 전체 길이).</summary>
        public float Height;

        /// <summary>CapsuleCollider.direction (0 = X, 1 = Y, 2 = Z, Rotation 기준).</summary>
        public int Direction = 1;

        /// <summary>스킨드 결과에서 조각이 붙을 본 인덱스(입력 Bones 배열 기준). 정적 결과는 -1.</summary>
        public int BoneIndex = -1;

        public string BoneName;

        /// <summary>
        /// 콜라이더 GameObject의 균일 localScale. 1이 아니면 Vertices/Size/Radius/Height는 이 스케일이 적용된
        /// 로컬 공간 값이다(Center/Rotation은 부모 공간의 localPosition/localRotation 그대로).
        /// 스킨드 결과는 본 lossyScale을 상쇄하도록 자동으로 채워진다(x130 본의 극소 좌표를 PhysX가 잘못 굽는 문제 방지).
        /// </summary>
        public float LocalScale = 1f;

        /// <summary>껍질 부피(입력 공간 = 부모 로컬 단위).</summary>
        public float Volume;

        /// <summary>껍질 부피 / 콜라이더 부피. ConvexMesh는 1.</summary>
        public float Fill = 1f;

        public int VertexCount => Vertices != null ? Vertices.Length : 0;

        /// <summary>
        /// 부모(대상 루트 또는 본)의 균일 lossyScale s를 상쇄한 사본을 돌려준다: LocalScale /= s, 좌표·크기 *= s.
        /// 콜라이더를 localScale = LocalScale로 두면 월드 크기는 같고 메시 좌표는 월드 크기가 되어
        /// 극소/극대 좌표에서의 PhysX 쿠킹 오차를 피한다. s가 0·NaN이거나 1에 가까우면 그대로 돌려준다.
        /// </summary>
        public ConcavePiece WithParentScaleCompensation(float parentScale)
        {
            float s = Mathf.Abs(parentScale);
            if (!(s > 1e-12f) || float.IsInfinity(s) || Mathf.Abs(s - 1f) < 1e-4f) return this;
            var copy = (ConcavePiece)MemberwiseClone();
            copy.LocalScale = LocalScale / s;
            if (Vertices != null)
            {
                copy.Vertices = new Vector3[Vertices.Length];
                for (int i = 0; i < Vertices.Length; i++) copy.Vertices[i] = Vertices[i] * s;
            }

            copy.Triangles = Triangles != null ? (int[])Triangles.Clone() : null;
            copy.Size = Size * s;
            copy.Radius = Radius * s;
            copy.Height = Height * s;
            return copy;
        }
    }

    /// <summary>분해 결과와 측정값.</summary>
    public sealed class ConcaveDecompositionResult
    {
        public readonly List<ConcavePiece> Pieces = new List<ConcavePiece>();

        /// <summary>사용자에게 보여줄 한국어 경고(열린 메시, 셸 대체 등).</summary>
        public readonly List<string> Warnings = new List<string>();

        /// <summary>원본 내부 샘플 중 어떤 조각 안에 든 비율(0~1). 측정 못 하면 -1.</summary>
        public float Coverage = -1f;

        /// <summary>조각 내부 샘플 중 원본 밖에 있는 비율(0~1). 측정 못 하면 -1.</summary>
        public float OutsideFraction = -1f;

        /// <summary>true면 Coverage/OutsideFraction이 열린 메시라 근사값이다.</summary>
        public bool MetricsApproximate;

        public Vector3Int VoxelResolution;
        public int VoxelCount;

        /// <summary>복셀 한 변 길이(입력 공간 단위).</summary>
        public float VoxelSize;

        public double Seconds;
        public bool Cancelled;

        /// <summary>실패 사유(한국어). 성공이면 null.</summary>
        public string Error;

        /// <summary>실패 시 예외 전체 문자열(로그용).</summary>
        public string ErrorDetail;

        public bool Watertight;
        public int OpenEdgeCount;

        /// <summary>분할 단계가 만든 조각 수(병합 전).</summary>
        public int SplitPartCount;

        public ConcaveQuality ResolvedQuality;
        public int ResolvedMaxPieces;
        public float ResolvedTolerance;
        public int ResolvedMaxHullVertices;

        public bool Succeeded => Error == null && !Cancelled && Pieces.Count > 0;

        public int CountOf(ConcavePieceKind kind)
        {
            int count = 0;
            for (int i = 0; i < Pieces.Count; i++)
            {
                if (Pieces[i].Kind == kind) count++;
            }

            return count;
        }

        public int TotalHullVertices
        {
            get
            {
                int total = 0;
                for (int i = 0; i < Pieces.Count; i++)
                {
                    if (Pieces[i].Kind == ConcavePieceKind.ConvexMesh) total += Pieces[i].VertexCount;
                }

                return total;
            }
        }
    }

    /// <summary>스킨드 입력의 본 하나(순수 데이터).</summary>
    public sealed class ConcaveBone
    {
        public string Name;

        /// <summary>Bones 배열 안에서 가장 가까운 조상 본의 인덱스. 없으면 -1.</summary>
        public int ParentIndex = -1;

        /// <summary>대상 루트로부터의 Transform 깊이(루트 바로 아래 = 1).</summary>
        public int Depth;

        /// <summary>현재 포즈의 localToWorldMatrix.</summary>
        public Matrix4x4 LocalToWorld = Matrix4x4.identity;
    }

    /// <summary>본 아래에 붙은 강체 메시(예: 무기, 장신구)의 점들. 본 로컬 좌표.</summary>
    public sealed class ConcaveRigidPart
    {
        public int BoneIndex;
        public Vector3[] Points;
    }

    /// <summary>
    /// 스킨드 메시 분해 입력. <see cref="ConcaveMeshInput.TryCreateSkinnedInput"/>으로 SkinnedMeshRenderer에서 만들 수 있다.
    /// 조각은 바인드 포즈 기준 본 로컬 공간으로 나오므로 해당 본의 자식으로 두면 애니메이션을 따라간다.
    /// </summary>
    public sealed class ConcaveSkinnedInput
    {
        /// <summary>메시 공간 정점(바인드 포즈).</summary>
        public Vector3[] Vertices;

        /// <summary>측정용 삼각형(선택). 없으면 Coverage를 계산하지 않는다.</summary>
        public int[] Triangles;

        public BoneWeight[] BoneWeights;
        public Matrix4x4[] Bindposes;
        public ConcaveBone[] Bones;
        public readonly List<ConcaveRigidPart> RigidParts = new List<ConcaveRigidPart>();
    }
}
