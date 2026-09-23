using UnityEngine;

namespace TelleR.Tools
{
    [DisallowMultipleComponent]
    [ExecuteAlways]
    [AddComponentMenu("TelleR/Mesh Pivot Tool")]
    public class MeshPivotTool : MonoBehaviour
    {
        [SerializeField] private float snap;
        [SerializeField] private Mesh originalMesh;
        [SerializeField] private Mesh workingMesh;
        [SerializeField] private bool useSkinnedMesh;

        // 원본 메시 공간 → 작업 메시 공간 누적 변환 M = T(pivotOffset) * R(pivotRotation).
        // 작업 메시 정점 = M * 원본 정점. Revert 시 transform·자식·콜라이더를 M으로 되돌린다.
        // (절대 자세를 저장하지 않으므로 편집 중에 오브젝트를 옮겨도 Revert가 그 이동을 지우지 않는다)
        [SerializeField] private bool pivotTracked;
        [SerializeField] private Vector3 pivotOffset;
        [SerializeField] private Quaternion pivotRotation = Quaternion.identity;

        // 마지막 피벗 편집 직후 transform의 로컬 자세. 작업 메시가 사라진 상태의 Revert는 루트가 아직 이 자세일 때만 M으로 되돌린다.
        // (프리팹 Apply Overrides는 인스턴스 루트의 위치·회전을 전파하지 않으므로 다른 인스턴스·프리팹 에셋의 루트는 옮겨진 적이 없다)
        [SerializeField] private bool editedPoseTracked;
        [SerializeField] private Vector3 editedLocalPosition;
        [SerializeField] private Quaternion editedLocalRotation = Quaternion.identity;

        // 본 없는 SkinnedMeshRenderer의 편집 전 localBounds. 편집 중에는 M을 적용한 값을 쓰고 Revert 시 그대로 되돌린다.
        // (mesh.bounds로 덮어쓰면 사용자가 넓혀 둔 bounds·블렌드셰이프 범위가 사라짐)
        [SerializeField] private bool localBoundsTracked;
        [SerializeField] private Bounds originalLocalBounds;

        public float Snap
        {
            get => snap;
            set => snap = Mathf.Max(0f, value);
        }

        public bool HasWorkingMesh => workingMesh != null;

        public bool UseSkinnedMesh => useSkinnedMesh;

        /// <summary>편집 중인 씬 전용 메시 사본. 편집 전이면 null.</summary>
        public Mesh WorkingMesh => workingMesh;

        /// <summary>편집을 시작할 때 연결돼 있던 원본 메시.</summary>
        public Mesh OriginalMesh => originalMesh;

        // 에디터가 sharedMesh 교체를 Undo에 기록할 수 있도록 초기화 필요 여부를 노출
        // 작업 메시가 사라진 복구 상태에서는 자동 초기화하지 않는다 (사용자가 넣은 메시를 원본 사본으로 덮어쓰고 M을 잃게 됨)
        public bool NeedsInitialization => workingMesh == null && !pendingRemoval && !IsWorkingMeshLost && GetSharedMesh() != null;

        /// <summary>
        /// 편집 중 프리팹 적용·저장 등으로 작업 메시가 사라진 복구 상태인지 여부. 렌더러에 다른 메시를 넣어도 복구 상태로 본다.
        /// 이 상태에서는 Revert로만 원본 메시·자세를 복구할 수 있다.
        /// </summary>
        public bool IsWorkingMeshLost => workingMesh == null && originalMesh != null && (pivotTracked || GetSharedMesh() == null);

        /// <summary>편집할 메시가 있는지 여부(작업 메시 또는 MeshFilter/SkinnedMeshRenderer의 메시). false면 피벗 편집이 아무것도 바꾸지 않는다.</summary>
        public bool HasSourceMesh => workingMesh != null || GetSharedMesh() != null;

        /// <summary>
        /// 피벗 편집이 transform을 옮기는 방식인지 여부.
        /// MeshFilter와 본이 없는 SkinnedMeshRenderer는 transform이 렌더링 기준이므로 transform을 옮긴다.
        /// 본이 있는 스킨드 메시는 본이 렌더링을 결정하므로 메시 공간과 bindpose만 보정한다(화면 변화 없음).
        /// </summary>
        public bool EditsTransform => !IsSkinnedTarget() || !HasBindposes();

        /// <summary>
        /// 현재 상태에서 피벗 회전이 형상을 보존하는지 여부.
        /// transform 자체의 스케일이 비균등하면 회전과 스케일이 교환되지 않아 메시가 찌그러지므로 막는다.
        /// </summary>
        public bool CanRotatePivot => !EditsTransform || IsUniformScale(transform.localScale);

        /// <summary>현재 피벗 축의 월드 회전. 본 스킨드 메시는 메시 공간 회전을 반영한 가상 피벗 축이다.</summary>
        public Quaternion PivotWorldRotation =>
            EditsTransform ? transform.rotation : transform.rotation * Quaternion.Inverse(SafePivotRotation);

        private Quaternion SafePivotRotation
        {
            get
            {
                Quaternion q = pivotRotation;
                float sq = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
                return sq < 1e-6f ? Quaternion.identity : q;
            }
        }

        private bool pendingRemoval;

        [System.NonSerialized] private Mesh bindposeCacheMesh;
        [System.NonSerialized] private bool bindposeCacheValue;
        [System.NonSerialized] private bool warnedColliderAxis;
        [System.NonSerialized] private bool warnedNonUniform;

        // Revert/Apply 후 파괴 대기 중 재초기화를 막는다
        public void MarkPendingRemoval() => pendingRemoval = true;

        private bool IsSkinnedTarget()
        {
            if (useSkinnedMesh) return true;
            return GetComponent<MeshFilter>() == null && GetComponent<SkinnedMeshRenderer>() != null;
        }

        private bool HasBindposes()
        {
            // 작업 메시가 사라진 상태(프리팹 적용·저장 등)에서도 편집 방식을 알 수 있게 원본 메시로 판단한다
            Mesh m = IsWorkingMeshLost ? originalMesh : GetSharedMesh();
            if (m == null) m = originalMesh;
            if (m == null) return false;
            if (!ReferenceEquals(m, bindposeCacheMesh))
            {
                bindposeCacheMesh = m;
                bindposeCacheValue = m.bindposes.Length > 0;
            }
            return bindposeCacheValue;
        }

        private static bool IsUniformScale(Vector3 s)
        {
            float max = Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)));
            float tol = Mathf.Max(1e-5f, max * 1e-4f);
            return Mathf.Abs(s.x - s.y) <= tol && Mathf.Abs(s.x - s.z) <= tol;
        }

        private Mesh GetSharedMesh()
        {
            if (IsSkinnedTarget())
            {
                SkinnedMeshRenderer smr = GetComponent<SkinnedMeshRenderer>();
                return smr != null ? smr.sharedMesh : null;
            }
            MeshFilter mf = GetComponent<MeshFilter>();
            return mf != null ? mf.sharedMesh : null;
        }

        private void SetSharedMesh(Mesh mesh)
        {
            if (IsSkinnedTarget())
            {
                SkinnedMeshRenderer smr = GetComponent<SkinnedMeshRenderer>();
                if (smr != null) smr.sharedMesh = mesh;
            }
            else
            {
                MeshFilter mf = GetComponent<MeshFilter>();
                if (mf != null) mf.sharedMesh = mesh;
            }
        }

        public void EnsureInitialized()
        {
            if (pendingRemoval) return;

            if (!useSkinnedMesh)
            {
                MeshFilter mf = GetComponent<MeshFilter>();
                SkinnedMeshRenderer smr = GetComponent<SkinnedMeshRenderer>();
                if (mf == null && smr != null) useSkinnedMesh = true;
            }

            // 편집 중에는 originalMesh를 다시 정하지 않는다.
            // (원본 에셋이 삭제된 뒤 여기서 현재 메시=작업 메시를 원본으로 삼으면 Revert가 작업 메시를 파괴함)
            if (workingMesh != null) return;
            // 작업 메시가 사라진 복구 상태: M이 남아 있으므로 새로 시작하지 않는다 (Revert로만 복구)
            if (IsWorkingMeshLost) return;

            Mesh currentMesh = GetSharedMesh();
            if (currentMesh == null) return;

            // 복구 상태가 아니면 현재 연결된 메시가 원본이다 (남아 있는 옛 originalMesh로 사용자가 넣은 메시를 덮어쓰지 않음)
            originalMesh = currentMesh;

            pivotTracked = true;
            pivotOffset = Vector3.zero;
            pivotRotation = Quaternion.identity;
            editedPoseTracked = false;
            localBoundsTracked = false;
            CaptureSkinnedLocalBounds();

            workingMesh = Instantiate(originalMesh);
            workingMesh.name = originalMesh.name + "_PivotEdited";
            SetSharedMesh(workingMesh);
            RefreshSkinnedLocalBounds();
            RefreshMeshCollider(null);
        }

        // 원본 메시 공간 → 작업 메시 공간 변환 M
        private Matrix4x4 PivotMatrix => Matrix4x4.TRS(pivotOffset, SafePivotRotation, Vector3.one);

        /// <summary>
        /// 복제(Ctrl+D) 등으로 다른 컴포넌트와 공유하게 된 작업 메시를 새 사본으로 바꾼다.
        /// 렌더러와(렌더 메시를 쓰던) MeshCollider 참조도 함께 교체한다.
        /// </summary>
        public void ReplaceWorkingMesh(Mesh newWorkingMesh)
        {
            if (newWorkingMesh == null || workingMesh == null || newWorkingMesh == workingMesh) return;
            Mesh previous = workingMesh;
            workingMesh = newWorkingMesh;
            SetSharedMesh(workingMesh);
            RefreshMeshCollider(previous);
        }

        public void MovePivotToWorld(Vector3 targetPivotWorld)
        {
            EnsureInitialized();
            if (workingMesh == null) return;

            Transform t = transform;
            Vector3 currentPivotWorld = t.position;
            Vector3 deltaWorld = targetPivotWorld - currentPivotWorld;
            if (deltaWorld.sqrMagnitude <= 0f) return;

            Vector3 deltaLocal = t.InverseTransformVector(deltaWorld);
            SetPivotToLocalPoint(deltaLocal);
        }

        public void SetPivotToLocalPoint(Vector3 localPoint)
        {
            EnsureInitialized();
            if (workingMesh == null) return;
            if (localPoint.sqrMagnitude <= 1e-12f) return;

            Vector3[] verts = workingMesh.vertices;
            for (int i = 0; i < verts.Length; i++) verts[i] -= localPoint;
            workingMesh.vertices = verts;
            workingMesh.RecalculateBounds();
            // 평행이동은 노말·블렌드셰이프 델타에 영향이 없으므로 그대로 둔다 (임포트 노말 보존)

            CaptureSkinnedLocalBounds();
            pivotOffset -= localPoint;

            if (!EditsTransform)
            {
                // 본 스킨드: bindpose를 함께 이동해야 스키닝 결과가 보존됨 (버텍스만 옮기면 애니메이션 시 왜곡)
                Matrix4x4[] bindposes = workingMesh.bindposes;
                Matrix4x4 offset = Matrix4x4.Translate(localPoint);
                for (int i = 0; i < bindposes.Length; i++) bindposes[i] = bindposes[i] * offset;
                workingMesh.bindposes = bindposes;
            }
            else
            {
                // 피벗 이동의 표준 semantics: 메시는 월드에 고정되고 transform(피벗)이 이동한다.
                // 로컬 공간에서 계산해 부모 스케일이 비균등해도 정확하다: L' = L * T(p)
                Transform t = transform;
                t.localPosition += t.localRotation * Vector3.Scale(t.localScale, localPoint);
                // 자식은 월드 자세 유지: L_child' = T(-p) * L_child
                for (int i = 0; i < t.childCount; i++)
                    t.GetChild(i).localPosition -= localPoint;

                // 프리미티브 콜라이더의 center는 로컬 오프셋이라 transform과 함께 월드에서 움직인다.
                // 버텍스와 같은 보정을 적용해 콜라이더가 메시를 계속 감싸게 한다.
                TranslatePrimitiveColliderCenters(localPoint);
                RefreshSkinnedLocalBounds();
                RecordEditedPose();
            }

            RefreshMeshCollider(null);
        }

        private void RecordEditedPose()
        {
            Transform t = transform;
            editedLocalPosition = t.localPosition;
            editedLocalRotation = t.localRotation;
            editedPoseTracked = true;
        }

        // 루트가 마지막 편집 직후 자세 그대로인지 (= 이 transform이 피벗 편집으로 옮겨진 상태인지)
        private bool IsAtEditedPose()
        {
            if (!editedPoseTracked) return true; // 자세 기록 전(이동 없음) — M만큼 되돌려도 무해
            Transform t = transform;
            Vector3 p = t.localPosition;
            float tol = 1e-4f * Mathf.Max(1f, editedLocalPosition.magnitude);
            return (p - editedLocalPosition).sqrMagnitude <= tol * tol &&
                   Quaternion.Angle(t.localRotation, editedLocalRotation) < 0.01f;
        }

        private void TranslatePrimitiveColliderCenters(Vector3 localDelta)
        {
            foreach (var col in GetComponents<Collider>())
            {
                switch (col)
                {
                    case BoxCollider box: box.center -= localDelta; break;
                    case SphereCollider sphere: sphere.center -= localDelta; break;
                    case CapsuleCollider capsule: capsule.center -= localDelta; break;
                }
            }
        }

        public void SetPivotRotation(Quaternion targetWorldRotation)
        {
            EnsureInitialized();
            if (workingMesh == null) return;

            Quaternion deltaRot = Quaternion.Inverse(PivotWorldRotation) * targetWorldRotation;
            if (Quaternion.Angle(Quaternion.identity, deltaRot) < 0.001f) return;

            RotateMeshVertices(deltaRot);
        }

        public void RotatePivotBy(Quaternion deltaRotation)
        {
            EnsureInitialized();
            if (workingMesh == null) return;
            if (Quaternion.Angle(Quaternion.identity, deltaRotation) < 0.001f) return;

            RotateMeshVertices(deltaRotation);
        }

        private void RotateMeshVertices(Quaternion deltaRot)
        {
            if (!CanRotatePivot)
            {
                if (!warnedNonUniform)
                {
                    warnedNonUniform = true;
                    Debug.LogWarning("[TelleR/MeshPivotTool] 비균등 스케일(Scale x·y·z가 다름)에서는 피벗 회전 시 메시가 찌그러지므로 회전을 건너뜁니다. " +
                                     "Scale을 균등하게 맞춘 뒤 회전하세요.", this);
                }
                return;
            }

            Quaternion inverseRot = Quaternion.Inverse(deltaRot);
            Vector3[] verts = workingMesh.vertices;
            for (int i = 0; i < verts.Length; i++) verts[i] = inverseRot * verts[i];
            workingMesh.vertices = verts;
            workingMesh.RecalculateBounds();

            // 원본 노말을 버텍스와 같은 회전으로 변환한다.
            // RecalculateNormals 후 다시 회전시키면 노말이 지오메트리와 어긋나 셰이딩이 파괴됨.
            Vector3[] normals = workingMesh.normals;
            if (normals != null && normals.Length > 0)
            {
                for (int i = 0; i < normals.Length; i++) normals[i] = inverseRot * normals[i];
                workingMesh.normals = normals;
            }

            Vector4[] tangents = workingMesh.tangents;
            if (tangents != null && tangents.Length > 0)
            {
                for (int i = 0; i < tangents.Length; i++)
                {
                    Vector3 tan = new Vector3(tangents[i].x, tangents[i].y, tangents[i].z);
                    tan = inverseRot * tan;
                    tangents[i] = new Vector4(tan.x, tan.y, tan.z, tangents[i].w);
                }
                workingMesh.tangents = tangents;
            }

            // 블렌드셰이프 델타도 메시 공간 벡터이므로 같은 회전을 적용한다
            RotateBlendShapes(inverseRot);

            CaptureSkinnedLocalBounds();
            pivotOffset = inverseRot * pivotOffset;
            pivotRotation = (inverseRot * SafePivotRotation).normalized;

            if (!EditsTransform)
            {
                // 본 스킨드: bindpose에 같은 회전을 곱해 스키닝 결과를 보존
                Matrix4x4[] bindposes = workingMesh.bindposes;
                Matrix4x4 rot = Matrix4x4.Rotate(deltaRot);
                for (int i = 0; i < bindposes.Length; i++) bindposes[i] = bindposes[i] * rot;
                workingMesh.bindposes = bindposes;
            }
            else
            {
                // 메시는 월드에 고정되고 피벗 축이 회전한다: L' = L * R.
                // 자식은 월드 자세 유지: L_child' = R^-1 * L_child (로컬 계산이라 부모 비균등 스케일에서도 정확)
                Transform t = transform;
                t.localRotation = t.localRotation * deltaRot;
                for (int i = 0; i < t.childCount; i++)
                {
                    Transform c = t.GetChild(i);
                    c.localPosition = inverseRot * c.localPosition;
                    c.localRotation = inverseRot * c.localRotation;
                }

                RotatePrimitiveColliderCenters(inverseRot);
                RefreshSkinnedLocalBounds();
                RecordEditedPose();
            }

            RefreshMeshCollider(null);
        }

        private void RotateBlendShapes(Quaternion inverseRot)
        {
            int shapeCount = workingMesh.blendShapeCount;
            if (shapeCount == 0) return;

            SkinnedMeshRenderer smr = IsSkinnedTarget() ? GetComponent<SkinnedMeshRenderer>() : null;
            float[] weights = null;
            if (smr != null && smr.sharedMesh == workingMesh)
            {
                weights = new float[shapeCount];
                for (int s = 0; s < shapeCount; s++) weights[s] = smr.GetBlendShapeWeight(s);
            }

            int vertexCount = workingMesh.vertexCount;
            var names = new string[shapeCount];
            var frameWeights = new float[shapeCount][];
            var dv = new Vector3[shapeCount][][];
            var dn = new Vector3[shapeCount][][];
            var dt = new Vector3[shapeCount][][];

            for (int s = 0; s < shapeCount; s++)
            {
                names[s] = workingMesh.GetBlendShapeName(s);
                int frameCount = workingMesh.GetBlendShapeFrameCount(s);
                frameWeights[s] = new float[frameCount];
                dv[s] = new Vector3[frameCount][];
                dn[s] = new Vector3[frameCount][];
                dt[s] = new Vector3[frameCount][];
                for (int f = 0; f < frameCount; f++)
                {
                    frameWeights[s][f] = workingMesh.GetBlendShapeFrameWeight(s, f);
                    Vector3[] v = new Vector3[vertexCount];
                    Vector3[] n = new Vector3[vertexCount];
                    Vector3[] tg = new Vector3[vertexCount];
                    workingMesh.GetBlendShapeFrameVertices(s, f, v, n, tg);
                    for (int i = 0; i < vertexCount; i++)
                    {
                        v[i] = inverseRot * v[i];
                        n[i] = inverseRot * n[i];
                        tg[i] = inverseRot * tg[i];
                    }
                    dv[s][f] = v;
                    dn[s][f] = n;
                    dt[s][f] = tg;
                }
            }

            workingMesh.ClearBlendShapes();
            for (int s = 0; s < shapeCount; s++)
                for (int f = 0; f < frameWeights[s].Length; f++)
                    workingMesh.AddBlendShapeFrame(names[s], frameWeights[s][f], dv[s][f], dn[s][f], dt[s][f]);

            if (weights != null)
                for (int s = 0; s < shapeCount; s++) smr.SetBlendShapeWeight(s, weights[s]);
        }

        private void RotatePrimitiveColliderCenters(Quaternion inverseRot)
        {
            bool hasAxisCollider = false;
            foreach (var col in GetComponents<Collider>())
            {
                switch (col)
                {
                    case BoxCollider box: box.center = inverseRot * box.center; hasAxisCollider = true; break;
                    case SphereCollider sphere: sphere.center = inverseRot * sphere.center; break;
                    case CapsuleCollider capsule: capsule.center = inverseRot * capsule.center; hasAxisCollider = true; break;
                }
            }

            // Box/Capsule의 축 방향은 API상 회전시킬 수 없어 center만 보정된다 — 1회만 경고
            if (hasAxisCollider && !warnedColliderAxis)
            {
                warnedColliderAxis = true;
                Debug.LogWarning("[TelleR/MeshPivotTool] Box/Capsule 콜라이더의 축 방향은 피벗 회전을 따라가지 못합니다. 회전 후 콜라이더 크기·방향을 확인하세요.", this);
            }
        }

        // 본 없고 rootBone도 없는 SkinnedMeshRenderer만 localBounds가 transform 기준이다
        private SkinnedMeshRenderer GetTransformSpaceSkinnedRenderer()
        {
            if (!IsSkinnedTarget() || !EditsTransform) return null;
            SkinnedMeshRenderer smr = GetComponent<SkinnedMeshRenderer>();
            return smr != null && smr.rootBone == null ? smr : null;
        }

        // 편집 전 localBounds를 기록한다. 이전 버전에서 시작된 편집은 첫 변경 직전에 현재 값을 M^-1로 되돌려 기록한다.
        private void CaptureSkinnedLocalBounds()
        {
            if (localBoundsTracked) return;
            SkinnedMeshRenderer smr = GetTransformSpaceSkinnedRenderer();
            if (smr == null) return;
            originalLocalBounds = TransformBounds(PivotMatrix.inverse, smr.localBounds);
            localBoundsTracked = true;
        }

        // 메시와 같은 변환 M을 편집 전 localBounds에 적용한다 (누적 오차·반복 회전에 따른 팽창 없음)
        private void RefreshSkinnedLocalBounds()
        {
            if (!localBoundsTracked) return;
            SkinnedMeshRenderer smr = GetTransformSpaceSkinnedRenderer();
            if (smr != null) smr.localBounds = TransformBounds(PivotMatrix, originalLocalBounds);
        }

        private static Bounds TransformBounds(Matrix4x4 m, Bounds b)
        {
            Vector3 c = b.center, e = b.extents;
            var result = new Bounds(m.MultiplyPoint3x4(c), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                result.Encapsulate(m.MultiplyPoint3x4(corner));
            }
            return result;
        }

        /// <summary>피벗 축을 월드 축에 맞춘다. 여러 번 눌러도 결과가 같다.</summary>
        public void AlignToWorld()
        {
            SetPivotRotation(Quaternion.identity);
        }

        public void SetForwardDirection(Vector3 worldDirection)
        {
            if (worldDirection.sqrMagnitude < 0.001f) return;
            Vector3 dir = worldDirection.normalized;
            // 전방이 ±Y이면 up=Vector3.up과 평행해 LookRotation이 불안정하므로 보조 up을 쓴다
            Vector3 up = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.999f
                ? (dir.y > 0f ? Vector3.back : Vector3.forward)
                : Vector3.up;
            SetPivotRotation(Quaternion.LookRotation(dir, up));
        }

        public Bounds GetCurrentLocalBounds()
        {
            Mesh mesh = GetSharedMesh();
            if (mesh == null) return new Bounds(Vector3.zero, Vector3.zero);
            return mesh.bounds;
        }

        public Bounds GetRenderedBounds()
        {
            if (IsSkinnedTarget())
            {
                SkinnedMeshRenderer smr = GetComponent<SkinnedMeshRenderer>();
                if (smr != null) return smr.localBounds;
            }
            Mesh mesh = GetSharedMesh();
            if (mesh == null) return new Bounds(Vector3.zero, Vector3.zero);
            return mesh.bounds;
        }

        /// <summary>원본 메시와 편집 전 transform·자식·콜라이더 상태로 되돌리고 작업 메시를 파괴한다. 원본 메시가 없으면 아무것도 바꾸지 않는다.</summary>
        public void RestoreOriginalMesh()
        {
            Mesh detached = DetachWorkingMesh();
            if (detached != null) DestroyImmediate(detached);
        }

        // 에디터용: 원본(메시·transform·자식·콜라이더) 복원 후 workingMesh를 파괴하지 않고 반환한다.
        // (Undo 스택에 기록된 메시를 plain DestroyImmediate로 파괴하면 Ctrl+Z 시 파괴된 메시를 참조하게 됨)
        // 원본 메시를 찾을 수 없으면(에셋 삭제 등) 아무것도 바꾸지 않고 null을 반환한다 — 작업 메시가 유일한 형상이다.
        // 작업 메시가 사라진 상태면 원본 메시·자식·콜라이더를 복원하고 null을 반환한다.
        // 이때 transform은 마지막 편집 직후 자세 그대로일 때만 되돌린다 (프리팹 적용으로 M만 전파된 다른 인스턴스·프리팹 루트는 옮겨진 적이 없음).
        public Mesh DetachWorkingMesh()
        {
            Mesh detached = workingMesh;

            if (detached != null && originalMesh == null)
            {
                Debug.LogWarning("[TelleR/MeshPivotTool] 원본 메시를 찾을 수 없어(삭제되었거나 누락됨) 되돌리지 않았습니다. " +
                                 "편집 결과를 유지하려면 Apply & Remove를 사용하세요.", this);
                return null;
            }

            // 이동량(M)은 컴포넌트에 직렬화되어 있으므로 작업 메시가 사라진 상태(프리팹 적용·저장으로 씬 전용 메시 누락)에서도 복원한다
            if (EditsTransform)
            {
                if (pivotTracked)
                {
                    bool restoreRoot = detached != null || IsAtEditedPose();
                    if (!restoreRoot)
                        Debug.Log("[TelleR/MeshPivotTool] 오브젝트 위치·회전이 피벗 편집 직후와 달라(프리팹 적용으로 편집 결과만 전달됐거나 편집 후 옮김) " +
                                  "위치·회전은 그대로 두고 메시·자식·콜라이더만 되돌렸습니다.", this);
                    RestorePose(restoreRoot);
                }
                else if (detached != null || (originalMesh != null && GetSharedMesh() == null))
                    Debug.LogWarning("[TelleR/MeshPivotTool] 이전 버전에서 시작된 편집이라 transform 이동량을 알 수 없어 메시만 되돌립니다. " +
                                     "오브젝트 위치를 확인하세요.", this);
            }

            if (originalMesh != null) SetSharedMesh(originalMesh);

            if (localBoundsTracked)
            {
                SkinnedMeshRenderer smr = GetTransformSpaceSkinnedRenderer();
                if (smr != null) smr.localBounds = originalLocalBounds;
            }

            workingMesh = null;
            pivotTracked = false;
            pivotOffset = Vector3.zero;
            pivotRotation = Quaternion.identity;
            editedPoseTracked = false;
            localBoundsTracked = false;
            RefreshMeshCollider(detached);
            return detached;
        }

        // L_new = L * M,  L_child_new = M^-1 * L_child,  center_new = M^-1 * center
        // restoreRoot=false: 루트가 옮겨진 적 없는 경우 — 자식·콜라이더만 되돌린다
        private void RestorePose(bool restoreRoot)
        {
            Quaternion r = SafePivotRotation;
            Quaternion ri = Quaternion.Inverse(r);
            Vector3 o = pivotOffset;

            Transform t = transform;
            if (restoreRoot)
            {
                t.localPosition += t.localRotation * Vector3.Scale(t.localScale, o);
                t.localRotation = t.localRotation * r;
            }

            for (int i = 0; i < t.childCount; i++)
            {
                Transform c = t.GetChild(i);
                c.localPosition = ri * (c.localPosition - o);
                c.localRotation = ri * c.localRotation;
            }

            foreach (var col in GetComponents<Collider>())
            {
                switch (col)
                {
                    case BoxCollider box: box.center = ri * (box.center - o); break;
                    case SphereCollider sphere: sphere.center = ri * (sphere.center - o); break;
                    case CapsuleCollider capsule: capsule.center = ri * (capsule.center - o); break;
                }
            }
        }

        private void RefreshMeshCollider(Mesh previousWorkingMesh)
        {
            MeshCollider meshCollider = GetComponent<MeshCollider>();
            if (meshCollider == null) return;

            Mesh mesh = GetSharedMesh();
            if (mesh == null) return;

            // 렌더 메시와 무관한 커스텀 콜라이더 메시(간소화 메시 등)는 덮어쓰지 않는다
            Mesh current = meshCollider.sharedMesh;
            bool ours = current == null ||
                        current == workingMesh ||
                        current == originalMesh ||
                        (previousWorkingMesh != null && current == previousWorkingMesh);
            if (!ours) return;

            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = mesh;
        }
    }
}
