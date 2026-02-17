using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Rendering;

namespace TelleR
{
    [DisallowMultipleComponent]
    public class TrailEffect : MonoBehaviour
    {
        // ─────────────────────────────────────────────
        //  Settings
        // ─────────────────────────────────────────────
        public TrailEffectProfile Profile;
        public bool Active = true;
        public TrailMode Mode = TrailMode.Color;

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
        [Range(0.1f, 10f)] public float StampSize = 1f;
        public StampStyle StampStyle = StampStyle.Follow;
        [Range(1, 10)] public int StampCount = 4;
        [Range(1f, 30f)] public float StampFollowSpeed = 8f;
        [Range(0.1f, 5f)] public float StampSpacing = 0.5f;
        public bool PreventOverlap = true;
        [Range(4, 128)] public int MaxSnapshots = 32;
        [Range(0.001f, 1f)] public float MinDistance = 0.01f;

        // ─────────────────────────────────────────────
        //  Per-field override flags (managed by editor)
        // ─────────────────────────────────────────────
        [HideInInspector] public bool overrideMode;
        [HideInInspector] public bool overrideTrailColor;
        [HideInInspector] public bool overrideColorOverLifetime;
        [HideInInspector] public bool overrideDuration;
        [HideInInspector] public bool overrideSnapshotsPerSecond;
        [HideInInspector] public bool overrideScaleStart;
        [HideInInspector] public bool overrideScaleEnd;
        [HideInInspector] public bool overrideFresnelPower;
        [HideInInspector] public bool overrideFresnelIntensity;
        [HideInInspector] public bool overrideStampTexture;
        [HideInInspector] public bool overrideStampSize;
        [HideInInspector] public bool overrideStampStyle;
        [HideInInspector] public bool overrideStampCount;
        [HideInInspector] public bool overrideStampFollowSpeed;
        [HideInInspector] public bool overrideStampSpacing;
        [HideInInspector] public bool overridePreventOverlap;
        [HideInInspector] public bool overrideMaxSnapshots;
        [HideInInspector] public bool overrideMinDistance;

        [SerializeField] Material trailMaterial;

        // ─────────────────────────────────────────────
        //  Snapshot ring buffer (zero GC)
        // ─────────────────────────────────────────────

        struct Snapshot
        {
            public Matrix4x4 Matrix;
            public Vector3 Position;
            public float BirthTime;
            public bool IsActive;
        }

        Snapshot[] snapshots;
        int snapshotHead;
        int snapshotCount;
        float lastSnapshotTime;
        Vector3 lastSnapshotPos;

        // ─────────────────────────────────────────────
        //  Renderer references
        // ─────────────────────────────────────────────

        MeshFilter resolvedFilter;
        Renderer resolvedRenderer;
        Mesh sharedMesh;
        int submeshCount;

        Mesh stampQuadMesh;

        // ─────────────────────────────────────────────
        //  GPU Instancing data
        // ─────────────────────────────────────────────

        struct GpuInstanceData
        {
            public Vector4 color;
            public float alpha;
            public float fresnelPower;
            public float fresnelIntensity;
            public float padding;
        }

        const int GpuInstanceStride = sizeof(float) * 8;

        Material runtimeMaterial;
        MaterialPropertyBlock mpb;
        GraphicsBuffer instanceBuffer;
        GpuInstanceData[] instanceDataCpu;
        Matrix4x4[] instMatrices;

        SortEntry[] sortBuffer;
        int sortCount;

        struct SortEntry : IComparable<SortEntry>
        {
            public float DistanceSq;
            public int SnapshotIndex;
            public int CompareTo(SortEntry other) => other.DistanceSq.CompareTo(DistanceSq);
        }

        bool initialized;

        static int nextStencilId = 1;
        int stencilId;

        readonly List<TrailEffect> mergedChildren = new List<TrailEffect>(4);

        Camera cachedCamera;
        float cameraRefreshTime;

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
        static readonly int IdTrailBuffer = Shader.PropertyToID("_TrailBuffer");

        // ─────────────────────────────────────────────
        //  Effective values (profile > local)
        // ─────────────────────────────────────────────

        TrailMode EffMode => UseLocal(overrideMode) ? Mode : Profile.Mode;
        Color EffColor => UseLocal(overrideTrailColor) ? TrailColor : Profile.TrailColor;
        Gradient EffGradient => UseLocal(overrideColorOverLifetime) ? ColorOverLifetime : Profile.ColorOverLifetime;
        float EffDuration => UseLocal(overrideDuration) ? Duration : Profile.Duration;
        int EffSnapPerSec => UseLocal(overrideSnapshotsPerSecond) ? SnapshotsPerSecond : Profile.SnapshotsPerSecond;
        float EffScaleStart => UseLocal(overrideScaleStart) ? ScaleStart : Profile.ScaleStart;
        float EffScaleEnd => UseLocal(overrideScaleEnd) ? ScaleEnd : Profile.ScaleEnd;
        float EffFresnelPower => UseLocal(overrideFresnelPower) ? FresnelPower : Profile.FresnelPower;
        float EffFresnelIntensity => UseLocal(overrideFresnelIntensity) ? FresnelIntensity : Profile.FresnelIntensity;
        Texture2D EffStampTex => UseLocal(overrideStampTexture) ? StampTexture : Profile.StampTexture;
        float EffStampSize => UseLocal(overrideStampSize) ? StampSize : Profile.StampSize;
        StampStyle EffStampStyle => UseLocal(overrideStampStyle) ? StampStyle : Profile.StampStyle;
        int EffStampCount => UseLocal(overrideStampCount) ? StampCount : Profile.StampCount;
        float EffStampFollowSpeed => UseLocal(overrideStampFollowSpeed) ? StampFollowSpeed : Profile.StampFollowSpeed;
        float EffStampSpacing => UseLocal(overrideStampSpacing) ? StampSpacing : Profile.StampSpacing;
        bool EffPreventOverlap => UseLocal(overridePreventOverlap) ? PreventOverlap : Profile.PreventOverlap;
        int EffMaxSnap => UseLocal(overrideMaxSnapshots) ? MaxSnapshots : Profile.MaxSnapshots;
        float EffMinDist => UseLocal(overrideMinDistance) ? MinDistance : Profile.MinDistance;

        bool UseLocal(bool overrideFlag) => !Profile || overrideFlag;

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

            TrailEffect parent = null;
            foreach (var trail in allTrails)
            {
                if (trail == this) continue;
                if (trail.MergeWithTrail == null)
                {
                    parent = trail;
                    break;
                }
            }

            if (parent != null && parent != this)
            {
                MergeWithTrail = parent;
                parent.AddChild(this);
            }
        }

        void OnDisable()
        {
            if (MergeWithTrail != null)
                MergeWithTrail.RemoveChild(this);
            Cleanup();
        }

        void LateUpdate()
        {
            if (!Active || !initialized) return;

            float time = Time.time;

            if (EffMode == TrailMode.TextureStamp && EffStampStyle == StampStyle.Follow)
            {
                UpdateStampChain();
                DrawStampChain();
            }
            else
            {
                CaptureSnapshot(time);

                if (MergeWithTrail != null && MergeWithTrail.initialized) return;

                DrawSelf(time);
                DrawChildren(time);
            }
        }

        void UpdateStampChain()
        {
            if (resolvedFilter == null) return;

            Vector3 targetPos = resolvedFilter.transform.position;
            int count = EffStampCount;
            float speed = EffStampFollowSpeed;
            float spacing = EffStampSpacing;
            float dt = Time.deltaTime;

            if (!stampChainInitialized)
            {
                for (int i = 0; i < stampPositions.Length; i++)
                    stampPositions[i] = targetPos;
                stampChainInitialized = true;
            }

            // 첫 번째 스탬프: 드론을 부드럽게 추적
            stampPositions[0] = Vector3.Lerp(stampPositions[0], targetPos, dt * speed);

            // 나머지: 앞 스탬프를 체인처럼 따라감
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

            int count = EffStampCount;
            float stampSize = EffStampSize;
            Color baseColor = EffColor;
            Gradient gradient = EffGradient;
            bool useGradient = gradient != null && gradient.colorKeys != null && gradient.colorKeys.Length > 1;
            int layer = gameObject.layer;

            Camera cam = GetRenderCamera();
            Quaternion camRot = cam != null ? cam.transform.rotation : Quaternion.identity;
            Vector3 targetPos = resolvedFilter != null ? resolvedFilter.transform.position : transform.position;

            sortCount = 0;
            for (int i = 0; i < count; i++)
            {
                float distToTarget = Vector3.Distance(stampPositions[i], targetPos);
                if (distToTarget < EffStampSpacing * 0.5f) continue;

                float t = (float)i / Mathf.Max(1, count - 1);

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

                instMatrices[sortCount] = Matrix4x4.TRS(stampPositions[i], camRot, Vector3.one * stampSize);
                instanceDataCpu[sortCount].color = new Vector4(color.r, color.g, color.b, color.a);
                instanceDataCpu[sortCount].alpha = alpha;
                instanceDataCpu[sortCount].fresnelPower = 0f;
                instanceDataCpu[sortCount].fresnelIntensity = 0f;
                sortCount++;
            }

            if (sortCount == 0) return;

            instanceBuffer.SetData(instanceDataCpu, 0, 0, sortCount);

            var renderParams = new RenderParams(runtimeMaterial)
            {
                layer = layer,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                matProps = mpb
            };

            Graphics.RenderMeshInstanced(renderParams, stampQuadMesh, 0, instMatrices, sortCount);
            sortCount = 0;
        }

        // ═════════════════════════════════════════════
        //  Initialization
        // ═════════════════════════════════════════════

        void Initialize()
        {
            initialized = false;

            if (trailMaterial == null)
            {
                Debug.LogError("[TrailEffect] Trail Material asset is not assigned.", this);
                return;
            }

            ResolveTarget();
            if (resolvedFilter == null || sharedMesh == null)
            {
                Debug.LogWarning("[TrailFX] No MeshFilter with valid mesh found.", this);
                return;
            }

            CreateStampQuad();

            int max = EffMaxSnap;

            snapshots = new Snapshot[max];
            snapshotHead = 0;
            snapshotCount = 0;
            lastSnapshotTime = -1f;
            lastSnapshotPos = transform.position;

            instMatrices = new Matrix4x4[max];
            instanceDataCpu = new GpuInstanceData[max];

            sortBuffer = new SortEntry[max];
            sortCount = 0;

            stampPositions = new Vector3[10];
            stampChainInitialized = false;

            mpb = new MaterialPropertyBlock();
            stencilId = EffPreventOverlap ? (nextStencilId++ % 255 + 1) : 0;

            instanceBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, max, GpuInstanceStride);

            SetupRuntimeMaterial();
            initialized = true;
        }

        void SetupRuntimeMaterial()
        {
            if (runtimeMaterial != null)
                Destroy(runtimeMaterial);

            runtimeMaterial = new Material(trailMaterial);
            runtimeMaterial.name = $"TrailFX_Runtime_{name}";
            runtimeMaterial.enableInstancing = true;

            TrailMode mode = EffMode;
            bool isStamp = mode == TrailMode.TextureStamp;

            if (isStamp)
            {
                runtimeMaterial.SetFloat(IdUseTexStamp, 1f);
                runtimeMaterial.SetFloat(IdCull, 0f);
                Texture2D stamp = EffStampTex;
                if (stamp != null)
                    runtimeMaterial.SetTexture(IdMainTex, stamp);
            }
            else
            {
                runtimeMaterial.SetFloat(IdUseTexStamp, 0f);
                runtimeMaterial.SetFloat(IdCull, 2f);
            }

            if (stencilId > 0)
            {
                runtimeMaterial.SetFloat(IdStencilRef, stencilId);
                runtimeMaterial.SetFloat(IdStencilComp, 6f);
                runtimeMaterial.SetFloat(IdStencilOp, 2f);
            }

            runtimeMaterial.SetBuffer(IdTrailBuffer, instanceBuffer);
        }

        void ResolveTarget()
        {
            resolvedFilter = null;
            resolvedRenderer = null;
            sharedMesh = null;

            MeshFilter mf = TargetMeshFilter;
            if (mf == null)
            {
                Transform root = SearchRoot != null ? SearchRoot : transform;
                mf = root.GetComponentInChildren<MeshFilter>();
            }

            if (mf != null && mf.sharedMesh != null)
            {
                resolvedFilter = mf;
                resolvedRenderer = mf.GetComponent<Renderer>();
                sharedMesh = mf.sharedMesh;
                submeshCount = sharedMesh.subMeshCount;
            }
        }

        void CreateStampQuad()
        {
            if (stampQuadMesh != null) return;

            stampQuadMesh = new Mesh
            {
                name = "TrailFX_StampQuad",
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
            stampQuadMesh.UploadMeshData(true);
        }

        void Cleanup()
        {
            initialized = false;

            if (instanceBuffer != null)
            {
                instanceBuffer.Dispose();
                instanceBuffer = null;
            }

            if (runtimeMaterial != null)
            {
                Destroy(runtimeMaterial);
                runtimeMaterial = null;
            }

            if (stampQuadMesh != null)
            {
                Destroy(stampQuadMesh);
                stampQuadMesh = null;
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
            if (resolvedFilter == null) return;

            float interval = 1f / EffSnapPerSec;
            if (time - lastSnapshotTime < interval) return;

            Vector3 currentPos = resolvedFilter.transform.position;
            float distSq = Vector3.SqrMagnitude(currentPos - lastSnapshotPos);
            float minDist = EffMinDist;
            if (distSq < minDist * minDist && lastSnapshotTime > 0f) return;

            int max = EffMaxSnap;
            int idx = snapshotHead;

            snapshots[idx].Matrix = resolvedFilter.transform.localToWorldMatrix;
            snapshots[idx].Position = currentPos;
            snapshots[idx].BirthTime = time;
            snapshots[idx].IsActive = true;

            snapshotHead = (snapshotHead + 1) % max;
            if (snapshotCount < max) snapshotCount++;

            lastSnapshotTime = time;
            lastSnapshotPos = currentPos;
        }

        // ═════════════════════════════════════════════
        //  Rendering
        // ═════════════════════════════════════════════

        Camera GetRenderCamera()
        {
            float time = Time.time;
            if (cachedCamera != null && cachedCamera.isActiveAndEnabled && time - cameraRefreshTime < 1f)
                return cachedCamera;

            cameraRefreshTime = time;

            cachedCamera = Camera.main;
            if (cachedCamera != null && cachedCamera.isActiveAndEnabled)
                return cachedCamera;

            cachedCamera = Camera.current;
            if (cachedCamera != null && cachedCamera.isActiveAndEnabled)
                return cachedCamera;

            foreach (var cam in FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (cam.isActiveAndEnabled)
                {
                    cachedCamera = cam;
                    return cachedCamera;
                }
            }

            return null;
        }

        void DrawSelf(float time)
        {
            DrawInstanced(time);
        }

        void DrawChildren(float time)
        {
            for (int c = 0; c < mergedChildren.Count; c++)
            {
                var child = mergedChildren[c];
                if (child == null || !child.initialized || !child.Active) continue;
                child.DrawInstanced(time);
            }
        }

        void DrawInstanced(float time)
        {
            if (snapshotCount == 0 || runtimeMaterial == null) return;

            TrailMode mode = EffMode;
            bool isStamp = mode == TrailMode.TextureStamp;

            if (!isStamp && sharedMesh == null) return;
            if (isStamp && stampQuadMesh == null) return;

            float duration = EffDuration;
            float invDuration = 1f / duration;
            Gradient gradient = EffGradient;
            Color baseColor = EffColor;
            float fresnelP = EffFresnelPower;
            float fresnelI = EffFresnelIntensity;
            float scaleS = EffScaleStart;
            float scaleE = EffScaleEnd;
            bool useScale = !Mathf.Approximately(scaleS, 1f) || !Mathf.Approximately(scaleE, 1f);
            int max = snapshots.Length;
            int layer = gameObject.layer;
            bool useGradient = gradient != null && gradient.colorKeys != null && gradient.colorKeys.Length > 1;
            float stampSize = isStamp ? EffStampSize : 1f;

            Camera cam = GetRenderCamera();
            Vector3 camPos = cam != null ? cam.transform.position : transform.position;
            Quaternion camRot = cam != null ? cam.transform.rotation : Quaternion.identity;

            // ── Collect active snapshots ──
            sortCount = 0;
            for (int i = 0; i < snapshotCount; i++)
            {
                int idx = ((snapshotHead - 1 - i) % max + max) % max;
                if (!snapshots[idx].IsActive) continue;

                float age = time - snapshots[idx].BirthTime;
                if (age > duration)
                {
                    snapshots[idx].IsActive = false;
                    continue;
                }

                sortBuffer[sortCount].SnapshotIndex = idx;
                sortBuffer[sortCount].DistanceSq = (snapshots[idx].Position - camPos).sqrMagnitude;
                sortCount++;
            }

            if (sortCount == 0) return;

            Array.Sort(sortBuffer, 0, sortCount);

            // ── Fill instancing arrays ──
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

                Matrix4x4 mat;

                if (isStamp)
                {
                    float s = stampSize;
                    if (useScale)
                        s *= Mathf.Lerp(scaleS, scaleE, t);
                    mat = Matrix4x4.TRS(snapshots[idx].Position, camRot, Vector3.one * s);
                }
                else
                {
                    mat = snapshots[idx].Matrix;
                    if (useScale)
                    {
                        float s = Mathf.Lerp(scaleS, scaleE, t);
                        Vector4 col3 = mat.GetColumn(3);
                        Vector3 pos = new Vector3(col3.x, col3.y, col3.z);
                        mat = Matrix4x4.TRS(pos, mat.rotation, mat.lossyScale * s);
                    }
                }

                instMatrices[si] = mat;
                instanceDataCpu[si].color = new Vector4(color.r, color.g, color.b, color.a);
                instanceDataCpu[si].alpha = alpha;
                instanceDataCpu[si].fresnelPower = fresnelP;
                instanceDataCpu[si].fresnelIntensity = fresnelI;
            }

            // ── Upload to GPU & Draw ──
            instanceBuffer.SetData(instanceDataCpu, 0, 0, sortCount);

            Mesh drawMesh = isStamp ? stampQuadMesh : sharedMesh;
            int drawSubmeshCount = isStamp ? 1 : submeshCount;

            var renderParams = new RenderParams(runtimeMaterial)
            {
                layer = layer,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                matProps = mpb
            };

            for (int sub = 0; sub < drawSubmeshCount; sub++)
            {
                Graphics.RenderMeshInstanced(renderParams, drawMesh, sub, instMatrices, sortCount);
            }
        }

        // ═════════════════════════════════════════════
        //  Public API
        // ═════════════════════════════════════════════

        public void SetActive(bool active)
        {
            Active = active;
        }

        public void Clear()
        {
            if (snapshots == null) return;
            for (int i = 0; i < snapshots.Length; i++)
                snapshots[i].IsActive = false;
            snapshotCount = 0;
            snapshotHead = 0;
        }

        public void SetMode(TrailMode newMode)
        {
            Mode = newMode;
            if (initialized) SetupRuntimeMaterial();
        }

        public void ApplyProfile(TrailEffectProfile profile, Transform searchRoot = null)
        {
            Profile = profile;
            SearchRoot = searchRoot;
            if (initialized)
            {
                ResolveTarget();
                SetupRuntimeMaterial();
                Clear();
            }
            else if (isActiveAndEnabled)
            {
                Initialize();
            }
        }

        // ═════════════════════════════════════════════
        //  Editor
        // ═════════════════════════════════════════════

#if UNITY_EDITOR
        TrailMode lastValidatedMode;

        void OnValidate()
        {
            TrailMode currentEffMode = EffMode;
            if (initialized && currentEffMode != lastValidatedMode)
            {
                lastValidatedMode = currentEffMode;
                SetupRuntimeMaterial();
                Clear();
            }
        }

        void Reset()
        {
            ColorOverLifetime = new Gradient();
            ColorOverLifetime.SetKeys(
                new[] { new GradientColorKey(Color.cyan, 0f), new GradientColorKey(Color.blue, 1f) },
                new[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0f, 1f) }
            );
        }
#endif
    }
}
