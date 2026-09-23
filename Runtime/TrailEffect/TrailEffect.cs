using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Rendering;

namespace TelleR
{
    [DisallowMultipleComponent]
    [AddComponentMenu("TelleR/Trail Effect")]
    public class TrailEffect : MonoBehaviour
    {
        // ─────────────────────────────────────────────
        //  Settings
        // ─────────────────────────────────────────────
        public TrailEffectProfile Profile;
        public bool Active = true;
        public TrailMode Mode = TrailMode.Color;
        public TrailColorMode ColorMode = TrailColorMode.SolidColor;

        [HideInInspector]
        public MeshFilter TargetMeshFilter;

        [HideInInspector]
        public Transform SearchRoot;

        [HideInInspector]
        public TrailEffect MergeWithTrail;

        public Color TrailColor = new Color(0f, 0.5f, 1f, 0.6f);
        public Gradient ColorOverLifetime;
        [Range(0.05f, 5f)] public float Duration = 0.5f;
        [Range(1, 60)] public int SnapshotsPerSecond = 30;
        [Range(0f, 2f)] public float ScaleStart = 1f;
        [Range(0f, 2f)] public float ScaleEnd = 1f;
        [Range(0f, 5f)] public float FresnelPower = 3f;
        [Range(0f, 2f)] public float FresnelIntensity = 0f;
        public Texture2D StampTexture;
        [Range(0.1f, 10f)] public float StampSizeStart = 1f;
        [Range(0.1f, 10f)] public float StampSizeEnd = 1f;
        public StampStyle StampStyle = StampStyle.Follow;
        [Range(1, 10)] public int StampCount = 4;
        [Range(1f, 30f)] public float StampFollowSpeed = 8f;
        [Range(0.1f, 5f)] public float StampSpacing = 0.5f;
        public bool PreventOverlap = true;
        [Range(4, 128)] public int MaxSnapshots = 32;
        [Range(0.001f, 1f)] public float MinDistance = 0.01f;

        /// <summary>true면 Time.timeScale의 영향을 받지 않는다 (일시정지·슬로모션 중에도 잔상 유지).</summary>
        public bool UseUnscaledTime;

        // ─────────────────────────────────────────────
        //  Per-field override flags (managed by editor)
        // ─────────────────────────────────────────────
        [HideInInspector] public bool overrideMode;
        [HideInInspector] public bool overrideColorMode;
        [HideInInspector] public bool overrideTrailColor;
        [HideInInspector] public bool overrideColorOverLifetime;
        [HideInInspector] public bool overrideDuration;
        [HideInInspector] public bool overrideSnapshotsPerSecond;
        [HideInInspector] public bool overrideScaleStart;
        [HideInInspector] public bool overrideScaleEnd;
        [HideInInspector] public bool overrideFresnelPower;
        [HideInInspector] public bool overrideFresnelIntensity;
        [HideInInspector] public bool overrideStampTexture;
        [HideInInspector] public bool overrideStampSizeStart;
        [HideInInspector] public bool overrideStampSizeEnd;
        [HideInInspector] public bool overrideStampStyle;
        [HideInInspector] public bool overrideStampCount;
        [HideInInspector] public bool overrideStampFollowSpeed;
        [HideInInspector] public bool overrideStampSpacing;
        [HideInInspector] public bool overridePreventOverlap;
        [HideInInspector] public bool overrideMaxSnapshots;
        [HideInInspector] public bool overrideMinDistance;

        [SerializeField] Material trailMaterial;

        /// <summary>셰이더 이름. 머티리얼 검증에 쓴다.</summary>
        public const string ShaderName = "TelleR/Trail";

        /// <summary>패키지에 포함된 기본 머티리얼(Runtime/TrailEffect/Materials/TrailFX_Default.mat)의 GUID.</summary>
        public const string DefaultMaterialGuid = "6fb07ebafcf44944b13c67965e7f0323";

        const string LogPrefix = "[TelleR/TrailEffect] ";

        /// <summary>한 트레일이 잔상으로 복제하는 메쉬 파트 최대 수 (다중 파트 모델 보호용 상한).</summary>
        public const int MaxParts = 32;
        const int MinSnapshotCapacity = 4;
        const int MaxSnapshotCapacity = 512;
        const int StampChainCapacity = 10;

        // ─────────────────────────────────────────────
        //  Snapshot ring buffer (zero GC)
        // ─────────────────────────────────────────────

        struct Snapshot
        {
            public Vector3 Position;
            public float BirthTime;
            public bool IsActive;
        }

        Snapshot[] snapshots;
        int snapshotHead;
        int snapshotCount;
        float lastSnapshotTime;
        bool hasLastSnapshot;
        Vector3 lastSnapshotPos;

        // ─────────────────────────────────────────────
        //  Target parts (MeshFilter / SkinnedMeshRenderer)
        // ─────────────────────────────────────────────

        sealed class TrailPart
        {
            public Transform Transform;
            public Renderer Renderer;            // 표시 여부 확인용 (MeshRenderer 또는 SkinnedMeshRenderer)
            public bool HasRenderer;             // false: 렌더러 없이 명시한 TargetMeshFilter (오브젝트 활성 여부만 본다)
            public Mesh Mesh;                    // 정적 메쉬(MeshFilter.sharedMesh) 또는 스킨드 원본 메쉬(서브메쉬 수 확인용)
            public SkinnedMeshRenderer Skinned;  // null이면 정적 파트
            public int SubmeshCount;
            public Matrix4x4[] Matrices;         // 스냅샷 슬롯별 월드 행렬
            public Mesh[] SlotMeshes;            // 스킨드 파트 전용: 슬롯별로 구운 메쉬 (풀에서 대여)
        }

        readonly List<TrailPart> parts = new List<TrailPart>(4);
        readonly Stack<Mesh> bakedMeshPool = new Stack<Mesh>();

        // GetTargets의 LOD 필터 작업용 (인스펙터가 Layout마다 호출하므로 재사용)
        readonly List<LODGroup> lodGroupScratch = new List<LODGroup>();
        HashSet<Renderer> lodExcludeScratch;
        HashSet<Renderer> lod0Scratch;
        Transform anchor;
        bool hasSkinnedParts;

        Mesh stampQuadMesh;

        // ─────────────────────────────────────────────
        //  GPU Instancing data (MaterialPropertyBlock 배열 → 셰이더 UNITY_INSTANCING_BUFFER)
        // ─────────────────────────────────────────────

        Material sourceMaterial;
        Material runtimeMaterial;
        MaterialPropertyBlock mpb;
        Vector4[] instanceColors;
        Vector4[] instanceParams;   // x=alpha, y=fresnelPower, z=fresnelIntensity
        Matrix4x4[] instMatrices;

        // 스킨드 파트는 스냅샷마다 메쉬가 달라 인스턴스 1개씩 그린다 → 슬롯별 프로퍼티 블록
        MaterialPropertyBlock[] slotMpb;
        readonly Vector4[] singleColor = new Vector4[1];
        readonly Vector4[] singleParams = new Vector4[1];
        readonly Matrix4x4[] singleMatrix = new Matrix4x4[1];

        SortEntry[] sortBuffer;
        int sortCount;

        struct SortEntry : IComparable<SortEntry>
        {
            public float DistanceSq;
            public int SnapshotIndex;
            public int CompareTo(SortEntry other) => other.DistanceSq.CompareTo(DistanceSq);
        }

        bool initialized;
        bool initFailed;
        Material failedMaterial;

        // 머티리얼 누락 에러는 LateUpdate에서 한 번만 낸다.
        // AddComponent 직후(OnEnable → Initialize가 동기 실행) 같은 프레임에 SetMaterial/ApplyProfile을 부르면 에러가 나지 않는다.
        bool missingMaterialLogged;
        bool warnedNoTargets;

        static int nextStencilId = 1;
        int stencilId;

        // 현재 런타임 머티리얼에 반영된 상태 (변경 감지용)
        TrailMode appliedMode;
        Texture2D appliedStampTex;
        bool appliedUnscaled;

        static bool warnedNoInstancing;

        readonly List<TrailEffect> mergedChildren = new List<TrailEffect>(4);

        Camera cachedCamera;
        float cameraRefreshTime = -10f;
        static Camera[] cameraScratch = new Camera[8];

        // Stamp chain follow
        Vector3[] stampPositions;
        bool stampChainInitialized;

        // Shader IDs
        static readonly int IdMainTex = Shader.PropertyToID("_MainTex");
        static readonly int IdUseTexStamp = Shader.PropertyToID("_UseTexStamp");
        static readonly int IdCull = Shader.PropertyToID("_Cull");
        static readonly int IdStencilRef = Shader.PropertyToID("_StencilRef");
        static readonly int IdStencilComp = Shader.PropertyToID("_StencilComp");
        static readonly int IdStencilOp = Shader.PropertyToID("_StencilOp");
        static readonly int IdTrailColor = Shader.PropertyToID("_TrailColor");
        static readonly int IdTrailParams = Shader.PropertyToID("_TrailParams");

        // ─────────────────────────────────────────────
        //  Effective values (profile > local)
        // ─────────────────────────────────────────────

        TrailMode EffMode => UseLocal(overrideMode) ? Mode : Profile.Mode;
        TrailColorMode EffColorMode => UseLocal(overrideColorMode) ? ColorMode : Profile.ColorMode;
        Color EffColor => UseLocal(overrideTrailColor) ? TrailColor : Profile.TrailColor;
        Gradient EffGradient => UseLocal(overrideColorOverLifetime) ? ColorOverLifetime : Profile.ColorOverLifetime;
        float EffDuration => Mathf.Max(0.01f, UseLocal(overrideDuration) ? Duration : Profile.Duration);
        int EffSnapPerSec => Mathf.Max(1, UseLocal(overrideSnapshotsPerSecond) ? SnapshotsPerSecond : Profile.SnapshotsPerSecond);
        float EffScaleStart => UseLocal(overrideScaleStart) ? ScaleStart : Profile.ScaleStart;
        float EffScaleEnd => UseLocal(overrideScaleEnd) ? ScaleEnd : Profile.ScaleEnd;
        float EffFresnelPower => UseLocal(overrideFresnelPower) ? FresnelPower : Profile.FresnelPower;
        float EffFresnelIntensity => UseLocal(overrideFresnelIntensity) ? FresnelIntensity : Profile.FresnelIntensity;
        Texture2D EffStampTex => UseLocal(overrideStampTexture) ? StampTexture : Profile.StampTexture;
        float EffStampSizeStart => UseLocal(overrideStampSizeStart) ? StampSizeStart : Profile.StampSizeStart;
        float EffStampSizeEnd => UseLocal(overrideStampSizeEnd) ? StampSizeEnd : Profile.StampSizeEnd;
        StampStyle EffStampStyle => UseLocal(overrideStampStyle) ? StampStyle : Profile.StampStyle;
        int EffStampCount => UseLocal(overrideStampCount) ? StampCount : Profile.StampCount;
        float EffStampFollowSpeed => UseLocal(overrideStampFollowSpeed) ? StampFollowSpeed : Profile.StampFollowSpeed;
        float EffStampSpacing => UseLocal(overrideStampSpacing) ? StampSpacing : Profile.StampSpacing;
        bool EffPreventOverlap => UseLocal(overridePreventOverlap) ? PreventOverlap : Profile.PreventOverlap;
        int EffMaxSnap => Mathf.Clamp(UseLocal(overrideMaxSnapshots) ? MaxSnapshots : Profile.MaxSnapshots, MinSnapshotCapacity, MaxSnapshotCapacity);
        float EffMinDist => UseLocal(overrideMinDistance) ? MinDistance : Profile.MinDistance;

        // 컴포넌트에 지정한 머티리얼이 우선, 없으면 Profile의 머티리얼
        Material EffMaterial => trailMaterial != null ? trailMaterial : (Profile != null ? Profile.TrailMaterial : null);

        bool UseLocal(bool overrideFlag) => !Profile || overrideFlag;

        float CurrentTime => UseUnscaledTime ? Time.unscaledTime : Time.time;
        float CurrentDeltaTime => UseUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

        // ═════════════════════════════════════════════
        //  Lifecycle
        // ═════════════════════════════════════════════

        void OnEnable()
        {
            Initialize();
            AutoMerge();
        }

        void AutoMerge()
        {
            Transform root = transform.root;
            var allTrails = root.GetComponentsInChildren<TrailEffect>();
            if (allTrails.Length <= 1) return;

            // 사용자가 명시적으로 지정한 병합 대상은 유지하고 재등록만 한다
            if (MergeWithTrail != null && MergeWithTrail != this)
            {
                MergeWithTrail.AddChild(this);
                return;
            }

            // 계층 순서상 첫 번째 트레일을 그룹의 단일 루트로 삼는다.
            // "MergeWithTrail이 null인 첫 트레일"을 고르면 활성화 순서에 따라 체인(A→B→C)이 만들어지고,
            // 체인 중간 노드는 LateUpdate에서 조기 return하므로 그 자식 트레일이 그려지지 않는다.
            TrailEffect canonical = allTrails[0];
            if (canonical == this) return;

            MergeWithTrail = canonical;
            canonical.AddChild(this);
        }

        // 병합 부모가 실제로 자식을 그려줄 수 있는 상태인지 (Stamp+Follow 모드는 DrawChildren을 타지 않음)
        bool CanDrawChildren =>
            initialized && Active &&
            !(EffMode == TrailMode.TextureStamp && EffStampStyle == StampStyle.Follow) &&
            MergeWithTrail == null;

        void OnDisable()
        {
            if (MergeWithTrail != null)
                MergeWithTrail.RemoveChild(this);
            Cleanup();
        }

        void LateUpdate()
        {
            if (!initialized)
            {
                // 머티리얼 누락으로 실패했다면, 머티리얼이 새로 지정됐을 때만 다시 시도한다 (매 프레임 로그 방지)
                if (initFailed && EffMaterial != null && EffMaterial != failedMaterial)
                    Initialize();

                if (!initialized)
                {
                    if (initFailed && EffMaterial == null && !missingMaterialLogged)
                    {
                        missingMaterialLogged = true;
                        Debug.LogError(LogPrefix + "Trail Material이 지정되지 않아 잔상을 그리지 않습니다. " +
                                       "인스펙터의 Trail Material 항목, Profile의 Trail Material, 또는 SetMaterial()로 '" + ShaderName + "' 셰이더 머티리얼을 지정하세요.", this);
                    }
                    return;
                }
            }

            if (!SyncSettings()) return;
            WarnIfNoTargets();
            if (!Active) return;

            float time = CurrentTime;

            if (EffMode == TrailMode.TextureStamp && EffStampStyle == StampStyle.Follow)
            {
                UpdateStampChain();
                DrawStampChain();
            }
            else
            {
                CaptureSnapshot(time);

                // 부모가 실제로 자식을 그려줄 수 있을 때만 위임 — 아니면 스스로 그린다 (미출력 방지)
                if (MergeWithTrail != null && MergeWithTrail.CanDrawChildren) return;

                DrawSelf();
                DrawChildren();
            }
        }

        /// <summary>
        /// 런타임 중 바뀐 설정(프로필 교체·필드 변경)을 반영한다.
        /// 버퍼 크기·스텐실·머티리얼이 바뀌면 재초기화, 모드·텍스쳐·시간 기준이 바뀌면 머티리얼만 갱신한다.
        /// </summary>
        bool SyncSettings()
        {
            if (snapshots.Length != EffMaxSnap ||
                (stencilId > 0) != EffPreventOverlap ||
                EffMaterial != sourceMaterial)
            {
                Reinitialize();
                return initialized;
            }

            if (UseUnscaledTime != appliedUnscaled)
            {
                // 시간 기준이 바뀌면 기존 스냅샷의 BirthTime을 비교할 수 없다
                appliedUnscaled = UseUnscaledTime;
                Clear();
            }

            TrailMode mode = EffMode;
            if (mode != appliedMode || EffStampTex != appliedStampTex)
            {
                bool modeChanged = mode != appliedMode;
                ApplyMaterialState();
                if (modeChanged) Clear();
            }
            return true;
        }

        void UpdateStampChain()
        {
            Vector3 targetPos = AnchorPosition;
            int count = Mathf.Clamp(EffStampCount, 1, stampPositions.Length);
            float speed = EffStampFollowSpeed;
            float spacing = EffStampSpacing;
            float dt = CurrentDeltaTime;

            if (!stampChainInitialized)
            {
                for (int i = 0; i < stampPositions.Length; i++)
                    stampPositions[i] = targetPos;
                stampChainInitialized = true;
            }

            stampPositions[0] = Vector3.Lerp(stampPositions[0], targetPos, dt * speed);

            for (int i = 1; i < count; i++)
            {
                Vector3 leader = stampPositions[i - 1];
                Vector3 toLeader = leader - stampPositions[i];
                float dist = toLeader.magnitude;

                if (dist > spacing)
                {
                    Vector3 target = leader - toLeader.normalized * spacing;
                    stampPositions[i] = Vector3.Lerp(stampPositions[i], target, dt * speed);
                }
            }
        }

        void DrawStampChain()
        {
            if (runtimeMaterial == null || stampQuadMesh == null) return;

            int count = Mathf.Clamp(EffStampCount, 1, stampPositions.Length);
            float sizeStart = EffStampSizeStart;
            float sizeEnd = EffStampSizeEnd;
            float spacing = EffStampSpacing;
            Color baseColor = EffColor;
            Gradient gradient = EffGradient;
            // 런타임 AddComponent/프로필 적용 시 gradient가 null일 수 있음 (Reset()은 에디터 전용)
            bool useGradient = EffColorMode == TrailColorMode.Gradient && gradient != null;
            Vector3 targetPos = AnchorPosition;

            sortCount = 0;
            for (int i = 0; i < count; i++)
            {
                float distToTarget = Vector3.Distance(stampPositions[i], targetPos);
                if (distToTarget < spacing * 0.5f) continue;

                float t = (float)i / Mathf.Max(1, count - 1);
                float size = Mathf.Lerp(sizeStart, sizeEnd, t);

                Color color;
                float alpha;
                if (useGradient)
                {
                    color = gradient.Evaluate(t);
                    alpha = color.a;
                }
                else
                {
                    color = baseColor;
                    alpha = baseColor.a;
                }

                // 회전은 셰이더가 뷰 행렬로 빌보드 처리한다 (카메라별로 정확)
                instMatrices[sortCount] = BillboardMatrix(stampPositions[i], size);
                instanceColors[sortCount] = new Vector4(color.r, color.g, color.b, color.a);
                instanceParams[sortCount] = new Vector4(alpha, 0f, 0f, 0f);
                sortCount++;
            }

            if (sortCount == 0) return;

            mpb.SetVectorArray(IdTrailColor, instanceColors);
            mpb.SetVectorArray(IdTrailParams, instanceParams);

            Graphics.RenderMeshInstanced(MakeRenderParams(mpb), stampQuadMesh, 0, instMatrices, sortCount);
            sortCount = 0;
        }

        // ═════════════════════════════════════════════
        //  Initialization
        // ═════════════════════════════════════════════

        void Initialize()
        {
            initialized = false;
            initFailed = false;

            if (!SystemInfo.supportsInstancing)
            {
                if (!warnedNoInstancing)
                {
                    warnedNoInstancing = true;
                    Debug.LogWarning(LogPrefix + "이 기기는 GPU Instancing을 지원하지 않아 잔상 효과를 끕니다.", this);
                }
                return;
            }

            Material source = EffMaterial;
            if (source == null)
            {
                // 여기서 바로 로그하지 않는다: 런타임 AddComponent는 OnEnable이 먼저 돌아 SetMaterial 기회가 없다.
                // 같은 프레임 안에 머티리얼이 들어오지 않으면 LateUpdate에서 한 번 알린다.
                initFailed = true;
                failedMaterial = null;
                return;
            }

            if (source.shader == null || !source.shader.isSupported)
            {
                initFailed = true;
                failedMaterial = source;
                Debug.LogError(LogPrefix + $"머티리얼 '{source.name}'의 셰이더가 현재 플랫폼·렌더 파이프라인에서 지원되지 않아 잔상을 그리지 않습니다.", this);
                return;
            }

            ResolveTargets();
            if (parts.Count > 0) warnedNoTargets = false;

            CreateStampQuad();

            int max = EffMaxSnap;

            snapshots = new Snapshot[max];
            snapshotHead = 0;
            snapshotCount = 0;
            hasLastSnapshot = false;
            lastSnapshotTime = 0f;
            lastSnapshotPos = AnchorPosition;

            for (int p = 0; p < parts.Count; p++)
            {
                parts[p].Matrices = new Matrix4x4[max];
                if (parts[p].Skinned != null)
                    parts[p].SlotMeshes = new Mesh[max];
            }

            // Stamp(Follow) 모드는 같은 배열에 최대 StampChainCapacity개를 기록하므로
            // MaxSnapshots가 그보다 작아도 모자라지 않게 크기를 보장한다
            int bufferSize = Mathf.Max(max, StampChainCapacity);
            instMatrices = new Matrix4x4[bufferSize];
            instanceColors = new Vector4[bufferSize];
            instanceParams = new Vector4[bufferSize];

            sortBuffer = new SortEntry[bufferSize];
            sortCount = 0;

            stampPositions = new Vector3[StampChainCapacity];
            stampChainInitialized = false;

            // MaterialPropertyBlock의 배열 길이는 처음 설정한 값으로 고정되므로 크기가 바뀔 때마다 새로 만든다
            mpb = new MaterialPropertyBlock();
            slotMpb = hasSkinnedParts ? new MaterialPropertyBlock[max] : null;
            stencilId = EffPreventOverlap ? (nextStencilId++ % 255 + 1) : 0;

            SetupRuntimeMaterial(source);
            appliedUnscaled = UseUnscaledTime;
            missingMaterialLogged = false;
            initialized = true;
        }

        // Color 모드는 대상 메쉬가 없으면 아무것도 그리지 않는다. Texture Stamp 모드는 빈 오브젝트도 정상 구성이므로 경고하지 않는다.
        // LateUpdate에서 확인하므로 AddComponent 직후 같은 프레임에 Mode를 바꾸면 경고가 나지 않는다. 컴포넌트당 한 번만 알린다.
        void WarnIfNoTargets()
        {
            if (parts.Count > 0 || warnedNoTargets || EffMode != TrailMode.Color) return;

            warnedNoTargets = true;
            Debug.LogWarning(LogPrefix + "잔상으로 복제할 MeshFilter/SkinnedMeshRenderer를 찾지 못해 Color 모드 잔상이 그려지지 않습니다. " +
                             "대상 메쉬를 자식으로 두거나 인스펙터 Target 섹션의 Search Root/Target Mesh Filter를 지정하세요 (스크립트에서 바꿨다면 RefreshTargets() 호출).", this);
        }

        void Reinitialize()
        {
            Cleanup();
            Initialize();
        }

        void SetupRuntimeMaterial(Material source)
        {
            if (runtimeMaterial != null)
                SafeDestroy(runtimeMaterial);

            sourceMaterial = source;
            runtimeMaterial = new Material(source);
            runtimeMaterial.name = $"TrailFX_Runtime_{name}";
            runtimeMaterial.enableInstancing = true;
            runtimeMaterial.hideFlags = HideFlags.DontSave;

            ApplyMaterialState();
        }

        void ApplyMaterialState()
        {
            TrailMode mode = EffMode;
            Texture2D stamp = EffStampTex;
            appliedMode = mode;
            appliedStampTex = stamp;

            if (runtimeMaterial == null) return;

            bool isStamp = mode == TrailMode.TextureStamp;
            runtimeMaterial.SetFloat(IdUseTexStamp, isStamp ? 1f : 0f);
            runtimeMaterial.SetFloat(IdCull, isStamp ? (float)CullMode.Off : (float)CullMode.Back);

            if (runtimeMaterial.HasProperty(IdMainTex))
            {
                Texture tex = isStamp && stamp != null
                    ? stamp
                    : (sourceMaterial != null && sourceMaterial.HasProperty(IdMainTex) ? sourceMaterial.GetTexture(IdMainTex) : null);
                runtimeMaterial.SetTexture(IdMainTex, tex);
            }

            if (stencilId > 0)
            {
                runtimeMaterial.SetFloat(IdStencilRef, stencilId);
                runtimeMaterial.SetFloat(IdStencilComp, (float)CompareFunction.NotEqual);
                runtimeMaterial.SetFloat(IdStencilOp, (float)StencilOp.Replace);
            }
            else
            {
                runtimeMaterial.SetFloat(IdStencilRef, 0f);
                runtimeMaterial.SetFloat(IdStencilComp, (float)CompareFunction.Always);
                runtimeMaterial.SetFloat(IdStencilOp, (float)StencilOp.Keep);
            }
        }

        Vector3 AnchorPosition => anchor != null ? anchor.position : transform.position;

        void ResolveTargets()
        {
            ReleaseAllSlotMeshes();
            parts.Clear();
            hasSkinnedParts = false;

            var filters = new List<MeshFilter>();
            var skinned = new List<SkinnedMeshRenderer>();
            GetTargets(filters, skinned);

            for (int i = 0; i < filters.Count && parts.Count < MaxParts; i++)
            {
                Mesh mesh = filters[i].sharedMesh;
                filters[i].TryGetComponent(out MeshRenderer meshRenderer);
                parts.Add(new TrailPart
                {
                    Transform = filters[i].transform,
                    Renderer = meshRenderer,
                    HasRenderer = meshRenderer != null,
                    Mesh = mesh,
                    SubmeshCount = mesh.subMeshCount
                });
            }

            for (int i = 0; i < skinned.Count && parts.Count < MaxParts; i++)
            {
                Mesh mesh = skinned[i].sharedMesh;
                parts.Add(new TrailPart
                {
                    Transform = skinned[i].transform,
                    Renderer = skinned[i],
                    HasRenderer = true,
                    Mesh = mesh,
                    Skinned = skinned[i],
                    SubmeshCount = mesh.subMeshCount
                });
                hasSkinnedParts = true;
            }

            if (filters.Count + skinned.Count > MaxParts)
            {
                Debug.LogWarning(LogPrefix + $"메쉬 파트가 {filters.Count + skinned.Count}개라 앞의 {MaxParts}개만 잔상으로 그립니다. " +
                                 "인스펙터 Target 섹션의 Search Root 또는 Target Mesh Filter로 대상을 좁히세요.", this);
            }

            anchor = parts.Count > 0 ? parts[0].Transform : transform;
        }

        /// <summary>
        /// 잔상 대상 메쉬를 수집한다. TargetMeshFilter가 있으면 그것 하나(MeshRenderer가 없어도 됨), 없으면 SearchRoot(없으면 이 오브젝트) 아래
        /// 활성 게임오브젝트의 MeshFilter(MeshRenderer가 붙은 것)·SkinnedMeshRenderer 전부. 다른 TrailEffect가 붙은 하위 계층은 그 트레일 몫이므로 제외하고,
        /// LODGroup이 있으면 LOD0(과 LOD에 등록되지 않은) 렌더러만 쓴다.
        /// 렌더러가 꺼져 있어도 포함한다: 표시 여부는 스냅샷마다 다시 확인하므로, 숨긴 채 시작해 나중에 켠 파트도 잔상이 남는다.
        /// </summary>
        public void GetTargets(List<MeshFilter> filters, List<SkinnedMeshRenderer> skinnedRenderers)
        {
            filters?.Clear();
            skinnedRenderers?.Clear();

            if (TargetMeshFilter != null)
            {
                if (filters != null && TargetMeshFilter.sharedMesh != null)
                    filters.Add(TargetMeshFilter);
                return;
            }

            Transform root = SearchRoot != null ? SearchRoot : transform;
            HashSet<Renderer> lowerLods = CollectLowerLodRenderers(root);

            if (filters != null)
            {
                root.GetComponentsInChildren(false, filters);
                for (int i = filters.Count - 1; i >= 0; i--)
                {
                    MeshFilter mf = filters[i];
                    bool valid = mf.sharedMesh != null &&
                                 mf.TryGetComponent(out MeshRenderer mr) &&
                                 (lowerLods == null || !lowerLods.Contains(mr)) &&
                                 !OwnedByOtherTrail(mf.transform, root);
                    if (!valid) filters.RemoveAt(i);
                }
            }

            if (skinnedRenderers != null)
            {
                root.GetComponentsInChildren(false, skinnedRenderers);
                for (int i = skinnedRenderers.Count - 1; i >= 0; i--)
                {
                    SkinnedMeshRenderer smr = skinnedRenderers[i];
                    bool valid = smr.sharedMesh != null &&
                                 (lowerLods == null || !lowerLods.Contains(smr)) &&
                                 !OwnedByOtherTrail(smr.transform, root);
                    if (!valid) skinnedRenderers.RemoveAt(i);
                }
            }

            lowerLods?.Clear();
        }

        // LODGroup은 렌더러를 컬링만 하고 enabled를 끄지 않으므로, 그대로 모으면 LOD0·LOD1·LOD2 잔상이 겹쳐 그려진다.
        // root 아래(root 포함) LODGroup의 LOD1 이하에만 속한 렌더러를 모아 제외한다.
        // LOD0에 속하거나 어느 LOD에도 등록되지 않은(항상 보이는) 렌더러는 유지한다. 초기화·인스펙터 갱신 때만 호출된다.
        HashSet<Renderer> CollectLowerLodRenderers(Transform root)
        {
            lodGroupScratch.Clear();
            root.GetComponentsInChildren(false, lodGroupScratch);
            if (lodGroupScratch.Count == 0) return null;

            if (lodExcludeScratch == null) lodExcludeScratch = new HashSet<Renderer>();
            if (lod0Scratch == null) lod0Scratch = new HashSet<Renderer>();
            lodExcludeScratch.Clear();

            for (int g = 0; g < lodGroupScratch.Count; g++)
            {
                LOD[] lods = lodGroupScratch[g].GetLODs();
                if (lods.Length < 2) continue;

                lod0Scratch.Clear();
                Renderer[] lod0 = lods[0].renderers;
                if (lod0 != null)
                {
                    for (int r = 0; r < lod0.Length; r++)
                        if (lod0[r] != null) lod0Scratch.Add(lod0[r]);
                }

                for (int l = 1; l < lods.Length; l++)
                {
                    Renderer[] rs = lods[l].renderers;
                    if (rs == null) continue;
                    for (int r = 0; r < rs.Length; r++)
                    {
                        if (rs[r] != null && !lod0Scratch.Contains(rs[r]))
                            lodExcludeScratch.Add(rs[r]);
                    }
                }
            }

            lodGroupScratch.Clear();
            lod0Scratch.Clear();
            return lodExcludeScratch.Count > 0 ? lodExcludeScratch : null;
        }

        bool OwnedByOtherTrail(Transform t, Transform root)
        {
            for (Transform cur = t; cur != null && cur != root; cur = cur.parent)
            {
                if (cur.TryGetComponent(out TrailEffect other) && other != this)
                    return true;
            }
            return false;
        }

        void CreateStampQuad()
        {
            if (stampQuadMesh != null) return;

            stampQuadMesh = new Mesh
            {
                name = "TrailFX_StampQuad",
                hideFlags = HideFlags.DontSave,
                vertices = new[]
                {
                    new Vector3(-0.5f, -0.5f, 0f),
                    new Vector3( 0.5f, -0.5f, 0f),
                    new Vector3( 0.5f,  0.5f, 0f),
                    new Vector3(-0.5f,  0.5f, 0f)
                },
                normals = new[]
                {
                    Vector3.back, Vector3.back, Vector3.back, Vector3.back
                },
                uv = new[]
                {
                    new Vector2(0f, 0f),
                    new Vector2(1f, 0f),
                    new Vector2(1f, 1f),
                    new Vector2(0f, 1f)
                },
                triangles = new[] { 0, 2, 1, 0, 3, 2 }
            };
            // 셰이더에서 어느 방향으로든 빌보드되므로 컬링 바운드를 정육면체로 잡는다
            stampQuadMesh.bounds = new Bounds(Vector3.zero, Vector3.one);
            stampQuadMesh.UploadMeshData(true);
        }

        void Cleanup()
        {
            initialized = false;

            if (runtimeMaterial != null)
            {
                SafeDestroy(runtimeMaterial);
                runtimeMaterial = null;
            }
            sourceMaterial = null;

            if (stampQuadMesh != null)
            {
                SafeDestroy(stampQuadMesh);
                stampQuadMesh = null;
            }

            ReleaseAllSlotMeshes();
            while (bakedMeshPool.Count > 0)
            {
                Mesh m = bakedMeshPool.Pop();
                if (m != null) SafeDestroy(m);
            }
        }

        static void SafeDestroy(UnityEngine.Object obj)
        {
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }

        // ═════════════════════════════════════════════
        //  Skinned mesh pool
        // ═════════════════════════════════════════════

        Mesh AcquireBakedMesh()
        {
            if (bakedMeshPool.Count > 0) return bakedMeshPool.Pop();
            var mesh = new Mesh { name = "TrailFX_Baked", hideFlags = HideFlags.DontSave };
            mesh.MarkDynamic();
            return mesh;
        }

        void ReleaseSlotMeshes(int slot)
        {
            if (!hasSkinnedParts) return;
            for (int p = 0; p < parts.Count; p++)
            {
                Mesh[] slotMeshes = parts[p].SlotMeshes;
                if (slotMeshes == null || slot >= slotMeshes.Length || slotMeshes[slot] == null) continue;
                bakedMeshPool.Push(slotMeshes[slot]);
                slotMeshes[slot] = null;
            }
        }

        void ReleaseAllSlotMeshes()
        {
            for (int p = 0; p < parts.Count; p++)
            {
                Mesh[] slotMeshes = parts[p].SlotMeshes;
                if (slotMeshes == null) continue;
                for (int s = 0; s < slotMeshes.Length; s++)
                {
                    if (slotMeshes[s] == null) continue;
                    bakedMeshPool.Push(slotMeshes[s]);
                    slotMeshes[s] = null;
                }
            }
        }

        // ═════════════════════════════════════════════
        //  Merge
        // ═════════════════════════════════════════════

        void AddChild(TrailEffect child)
        {
            if (!mergedChildren.Contains(child))
                mergedChildren.Add(child);
        }

        void RemoveChild(TrailEffect child)
        {
            mergedChildren.Remove(child);
        }

        // ═════════════════════════════════════════════
        //  Snapshot Capture
        // ═════════════════════════════════════════════

        void CaptureSnapshot(float time)
        {
            float interval = 1f / EffSnapPerSec;
            if (hasLastSnapshot && time - lastSnapshotTime < interval) return;

            Vector3 currentPos = AnchorPosition;
            float distSq = Vector3.SqrMagnitude(currentPos - lastSnapshotPos);
            float minDist = EffMinDist;
            if (hasLastSnapshot && distSq < minDist * minDist) return;

            int max = snapshots.Length;
            int idx = snapshotHead;
            bool bakeSkinned = hasSkinnedParts && EffMode == TrailMode.Color;

            for (int p = 0; p < parts.Count; p++)
            {
                TrailPart part = parts[p];

                // 파괴됐거나 숨겨진 파트(SetActive(false)·renderer.enabled=false)는 이 슬롯에서 그리지 않는다.
                // 영행렬은 모든 정점을 한 점으로 모아 아무것도 래스터화하지 않는다 (이전 슬롯의 행렬이 유령처럼 남지 않게 반드시 덮어쓴다).
                if (!IsPartVisible(part))
                {
                    part.Matrices[idx] = default;
                    if (part.SlotMeshes != null && part.SlotMeshes[idx] != null)
                    {
                        bakedMeshPool.Push(part.SlotMeshes[idx]);
                        part.SlotMeshes[idx] = null;
                    }
                    continue;
                }

                part.Matrices[idx] = part.Transform.localToWorldMatrix;

                if (part.Skinned != null)
                {
                    if (bakeSkinned)
                    {
                        // 슬롯이 재사용되면 기존 메쉬에 덮어 굽는다 (풀링, 매 프레임 할당 없음)
                        // useScale = true: 스케일을 빼고 순수 로컬 공간으로 굽는다. 기본값(false)은 lossyScale을 정점에 넣어 굽기 때문에
                        // localToWorldMatrix로 그리면 스케일이 두 번 곱해진다.
                        if (part.SlotMeshes[idx] == null) part.SlotMeshes[idx] = AcquireBakedMesh();
                        part.Skinned.BakeMesh(part.SlotMeshes[idx], true);
                    }
                    else if (part.SlotMeshes[idx] != null)
                    {
                        bakedMeshPool.Push(part.SlotMeshes[idx]);
                        part.SlotMeshes[idx] = null;
                    }
                }
            }

            snapshots[idx].Position = currentPos;
            snapshots[idx].BirthTime = time;
            snapshots[idx].IsActive = true;

            snapshotHead = (snapshotHead + 1) % max;
            if (snapshotCount < max) snapshotCount++;

            lastSnapshotTime = time;
            lastSnapshotPos = currentPos;
            hasLastSnapshot = true;
        }

        static bool IsPartVisible(TrailPart part)
        {
            // UnityEngine.Object의 == null은 파괴된 오브젝트도 null로 본다
            Transform tr = part.Transform;
            if (tr == null) return false;

            // 렌더러 없이 명시한 TargetMeshFilter는 오브젝트가 켜져 있으면 그린다
            if (!part.HasRenderer) return tr.gameObject.activeInHierarchy;

            // 렌더러가 있던 파트는 렌더러가 파괴되거나 꺼지면 그리지 않는다
            Renderer r = part.Renderer;
            return r != null && r.enabled && r.gameObject.activeInHierarchy;
        }

        // ═════════════════════════════════════════════
        //  Rendering
        // ═════════════════════════════════════════════

        // 투명 정렬 기준 카메라. 빌보드 방향은 셰이더가 카메라별로 처리하므로 여기서는 정렬에만 쓴다.
        Camera GetSortCamera()
        {
            float now = Time.unscaledTime;
            if (cachedCamera != null && cachedCamera.isActiveAndEnabled && now - cameraRefreshTime < 1f)
                return cachedCamera;

            cameraRefreshTime = now;

            cachedCamera = Camera.main;
            if (cachedCamera != null && cachedCamera.isActiveAndEnabled)
                return cachedCamera;

            // Camera.GetAllCameras는 할당 없이 활성 카메라만 채운다 (FindObjectsByType 버전 의존 회피)
            int count = Camera.allCamerasCount;
            if (count > cameraScratch.Length) cameraScratch = new Camera[count];
            count = Camera.GetAllCameras(cameraScratch);
            cachedCamera = null;
            for (int i = 0; i < count; i++)
            {
                if (cameraScratch[i] != null && cameraScratch[i].isActiveAndEnabled)
                {
                    cachedCamera = cameraScratch[i];
                    break;
                }
            }
            Array.Clear(cameraScratch, 0, cameraScratch.Length);
            return cachedCamera;
        }

        RenderParams MakeRenderParams(MaterialPropertyBlock props)
        {
            return new RenderParams(runtimeMaterial)
            {
                layer = gameObject.layer,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                matProps = props
            };
        }

        static Matrix4x4 BillboardMatrix(Vector3 position, float size)
        {
            Matrix4x4 m = Matrix4x4.identity;
            m.m00 = size; m.m11 = size; m.m22 = size;
            m.m03 = position.x; m.m13 = position.y; m.m23 = position.z;
            return m;
        }

        // 행렬 m을 pivot 기준으로 균등 배율 s만큼 키운다 (다중 파트 모델이 흩어지지 않도록 앵커 기준)
        static Matrix4x4 ScaleAbout(Matrix4x4 m, Vector3 pivot, float s)
        {
            m.m00 *= s; m.m10 *= s; m.m20 *= s;
            m.m01 *= s; m.m11 *= s; m.m21 *= s;
            m.m02 *= s; m.m12 *= s; m.m22 *= s;
            m.m03 = pivot.x + (m.m03 - pivot.x) * s;
            m.m13 = pivot.y + (m.m13 - pivot.y) * s;
            m.m23 = pivot.z + (m.m23 - pivot.z) * s;
            return m;
        }

        void DrawSelf()
        {
            DrawInstanced();
        }

        void DrawChildren()
        {
            for (int c = 0; c < mergedChildren.Count; c++)
            {
                var child = mergedChildren[c];
                if (child == null || !child.initialized || !child.Active) continue;
                child.DrawInstanced();
            }
        }

        void DrawInstanced()
        {
            if (snapshotCount == 0 || runtimeMaterial == null) return;

            float time = CurrentTime;
            TrailMode mode = EffMode;
            bool isStamp = mode == TrailMode.TextureStamp;

            if (isStamp && stampQuadMesh == null) return;
            if (!isStamp && parts.Count == 0) return;

            float duration = EffDuration;
            float invDuration = 1f / duration;
            Gradient gradient = EffGradient;
            Color baseColor = EffColor;
            float fresnelP = isStamp ? 0f : EffFresnelPower;
            float fresnelI = isStamp ? 0f : EffFresnelIntensity;
            int max = snapshots.Length;
            bool useGradient = EffColorMode == TrailColorMode.Gradient && gradient != null;

            float scaleS, scaleE;
            if (isStamp)
            {
                scaleS = EffStampSizeStart;
                scaleE = EffStampSizeEnd;
            }
            else
            {
                scaleS = EffScaleStart;
                scaleE = EffScaleEnd;
            }
            bool useScale = !(Mathf.Approximately(scaleS, 1f) && Mathf.Approximately(scaleE, 1f));

            Camera cam = GetSortCamera();
            Vector3 camPos = cam != null ? cam.transform.position : transform.position;

            // ── Collect active snapshots ──
            sortCount = 0;
            for (int i = 0; i < snapshotCount; i++)
            {
                int idx = ((snapshotHead - 1 - i) % max + max) % max;
                if (!snapshots[idx].IsActive) continue;

                float age = time - snapshots[idx].BirthTime;
                if (age > duration || age < 0f)
                {
                    snapshots[idx].IsActive = false;
                    ReleaseSlotMeshes(idx);
                    continue;
                }

                sortBuffer[sortCount].SnapshotIndex = idx;
                sortBuffer[sortCount].DistanceSq = (snapshots[idx].Position - camPos).sqrMagnitude;
                sortCount++;
            }

            if (sortCount == 0) return;

            Array.Sort(sortBuffer, 0, sortCount);

            // ── Per-instance color/alpha/fresnel ──
            for (int si = 0; si < sortCount; si++)
            {
                int idx = sortBuffer[si].SnapshotIndex;
                float age = time - snapshots[idx].BirthTime;
                float t = age * invDuration;

                Color color;
                float alpha;
                if (useGradient)
                {
                    color = gradient.Evaluate(t);
                    alpha = color.a;
                }
                else
                {
                    color = baseColor;
                    alpha = baseColor.a * (1f - t);
                }

                float fadeIn = Mathf.Clamp01(age / (duration * 0.15f));
                alpha *= fadeIn;

                instanceColors[si] = new Vector4(color.r, color.g, color.b, color.a);
                instanceParams[si] = new Vector4(alpha, fresnelP, fresnelI, 0f);
            }

            mpb.SetVectorArray(IdTrailColor, instanceColors);
            mpb.SetVectorArray(IdTrailParams, instanceParams);
            RenderParams renderParams = MakeRenderParams(mpb);

            if (isStamp)
            {
                for (int si = 0; si < sortCount; si++)
                {
                    int idx = sortBuffer[si].SnapshotIndex;
                    float t = (time - snapshots[idx].BirthTime) * invDuration;
                    instMatrices[si] = BillboardMatrix(snapshots[idx].Position, Mathf.Lerp(scaleS, scaleE, t));
                }
                Graphics.RenderMeshInstanced(renderParams, stampQuadMesh, 0, instMatrices, sortCount);
                return;
            }

            // ── Static parts: 파트마다 스냅샷 전체를 한 번에 인스턴싱 ──
            for (int p = 0; p < parts.Count; p++)
            {
                TrailPart part = parts[p];
                if (part.Skinned != null || part.Mesh == null) continue;

                bool anyVisible = false;
                for (int si = 0; si < sortCount; si++)
                {
                    int idx = sortBuffer[si].SnapshotIndex;
                    Matrix4x4 mat = part.Matrices[idx];
                    // 숨겨진 슬롯은 영행렬(m33 = 0), 보이는 슬롯은 localToWorld(m33 = 1)
                    if (mat.m33 != 0f) anyVisible = true;
                    if (useScale)
                    {
                        float t = (time - snapshots[idx].BirthTime) * invDuration;
                        mat = ScaleAbout(mat, snapshots[idx].Position, Mathf.Lerp(scaleS, scaleE, t));
                    }
                    instMatrices[si] = mat;
                }

                // 모든 슬롯에서 숨겨져 있던 파트(꺼진 렌더러 등)는 드로우콜을 내지 않는다
                if (!anyVisible) continue;

                for (int sub = 0; sub < part.SubmeshCount; sub++)
                    Graphics.RenderMeshInstanced(renderParams, part.Mesh, sub, instMatrices, sortCount);
            }

            if (!hasSkinnedParts) return;

            // ── Skinned parts: 스냅샷마다 구운 메쉬가 달라 1개씩 그린다 ──
            for (int si = 0; si < sortCount; si++)
            {
                int idx = sortBuffer[si].SnapshotIndex;
                MaterialPropertyBlock block = slotMpb[idx];
                if (block == null) block = slotMpb[idx] = new MaterialPropertyBlock();
                singleColor[0] = instanceColors[si];
                singleParams[0] = instanceParams[si];
                block.SetVectorArray(IdTrailColor, singleColor);
                block.SetVectorArray(IdTrailParams, singleParams);
                RenderParams singleParamsRp = MakeRenderParams(block);

                float s = useScale ? Mathf.Lerp(scaleS, scaleE, (time - snapshots[idx].BirthTime) * invDuration) : 1f;

                for (int p = 0; p < parts.Count; p++)
                {
                    TrailPart part = parts[p];
                    if (part.Skinned == null) continue;
                    Mesh baked = part.SlotMeshes[idx];
                    if (baked == null) continue;

                    Matrix4x4 mat = part.Matrices[idx];
                    if (useScale) mat = ScaleAbout(mat, snapshots[idx].Position, s);
                    singleMatrix[0] = mat;

                    int subCount = Mathf.Min(part.SubmeshCount, baked.subMeshCount);
                    for (int sub = 0; sub < subCount; sub++)
                        Graphics.RenderMeshInstanced(singleParamsRp, baked, sub, singleMatrix, 1);
                }
            }
        }

        // ═════════════════════════════════════════════
        //  Public API
        // ═════════════════════════════════════════════

        /// <summary>이 컴포넌트에 직접 지정한 머티리얼. 비어 있으면 Profile의 Trail Material을 쓴다.</summary>
        public Material TrailMaterial
        {
            get => trailMaterial;
            set => SetMaterial(value);
        }

        /// <summary>실제로 사용하는 머티리얼 (컴포넌트 → Profile 순).</summary>
        public Material ResolvedMaterial => EffMaterial;

        /// <summary>초기화에 성공해 잔상을 그릴 수 있는 상태인지.</summary>
        public bool IsInitialized => initialized;

        /// <summary>현재 잔상 대상으로 잡힌 메쉬 파트 수 (MeshFilter + SkinnedMeshRenderer).</summary>
        public int TargetPartCount => parts.Count;

        public void SetActive(bool active)
        {
            Active = active;
        }

        public void Clear()
        {
            if (snapshots == null) return;
            for (int i = 0; i < snapshots.Length; i++)
                snapshots[i].IsActive = false;
            ReleaseAllSlotMeshes();
            snapshotCount = 0;
            snapshotHead = 0;
            hasLastSnapshot = false;
        }

        public void SetMode(TrailMode newMode)
        {
            Mode = newMode;
            if (initialized) ApplyMaterialState();
        }

        /// <summary>
        /// 머티리얼을 지정한다 ('TelleR/Trail' 셰이더). 런타임에 AddComponent로 붙인 경우 이 메서드나 Profile로 머티리얼을 넣어야 한다.
        /// AddComponent와 같은 프레임에 호출하면 머티리얼 누락 에러가 나지 않는다.
        /// </summary>
        public void SetMaterial(Material material)
        {
            trailMaterial = material;
            if (Application.isPlaying && isActiveAndEnabled)
                Reinitialize();
        }

        /// <summary>대상 메쉬를 다시 찾는다 (모델 파트를 교체·추가한 뒤 호출).</summary>
        public void RefreshTargets()
        {
            if (Application.isPlaying && isActiveAndEnabled)
                Reinitialize();
        }

        public void ApplyProfile(TrailEffectProfile profile, Transform searchRoot = null)
        {
            Profile = profile;
            SearchRoot = searchRoot;
            // 버퍼 크기·스텐실·머티리얼·대상이 모두 바뀔 수 있으므로 통째로 다시 만든다
            if (Application.isPlaying && isActiveAndEnabled)
                Reinitialize();
        }

        // ═════════════════════════════════════════════
        //  Editor
        // ═════════════════════════════════════════════

#if UNITY_EDITOR
        void Reset()
        {
            ColorOverLifetime = new Gradient();
            ColorOverLifetime.SetKeys(
                new[] { new GradientColorKey(Color.cyan, 0f), new GradientColorKey(Color.blue, 1f) },
                new[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0f, 1f) }
            );

            // 에디터에서 새로 붙일 때만 패키지 기본 머티리얼을 연결한다 (빌드에는 씬 참조로 포함됨)
            if (trailMaterial == null && !Application.isPlaying)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(DefaultMaterialGuid);
                if (!string.IsNullOrEmpty(path))
                    trailMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
            }
        }
#endif
    }
}
