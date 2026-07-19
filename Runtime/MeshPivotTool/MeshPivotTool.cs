using UnityEngine;

namespace TelleR.Tools
{
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public class MeshPivotTool : MonoBehaviour
    {
        [SerializeField] private float snap;
        [SerializeField] private Mesh originalMesh;
        [SerializeField] private Mesh workingMesh;
        [SerializeField] private bool useSkinnedMesh;

        public float Snap
        {
            get => snap;
            set => snap = Mathf.Max(0f, value);
        }

        public bool HasWorkingMesh => workingMesh != null;

        public bool UseSkinnedMesh => useSkinnedMesh;

        // 에디터가 sharedMesh 교체를 Undo에 기록할 수 있도록 초기화 필요 여부를 노출
        public bool NeedsInitialization => workingMesh == null && !pendingRemoval && GetSharedMesh() != null;

        private bool pendingRemoval;

        // Revert/Apply 후 delayCall 파괴 대기 중 OnSceneGUI가 재초기화하는 레이스 방지
        public void MarkPendingRemoval() => pendingRemoval = true;

        private Mesh GetSharedMesh()
        {
            if (useSkinnedMesh)
            {
                SkinnedMeshRenderer smr = GetComponent<SkinnedMeshRenderer>();
                return smr != null ? smr.sharedMesh : null;
            }
            MeshFilter mf = GetComponent<MeshFilter>();
            return mf != null ? mf.sharedMesh : null;
        }

        private void SetSharedMesh(Mesh mesh)
        {
            if (useSkinnedMesh)
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

            Mesh currentMesh = GetSharedMesh();
            if (currentMesh == null) return;

            if (originalMesh == null) originalMesh = currentMesh;
            if (originalMesh == null) return;

            if (workingMesh == null)
            {
                workingMesh = Instantiate(originalMesh);
                workingMesh.name = originalMesh.name + "_PivotEdited";
                SetSharedMesh(workingMesh);
                RefreshMeshCollider();
            }
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
            // 평행이동은 노말에 영향이 없으므로 RecalculateNormals를 호출하지 않는다 (임포트 노말 보존)

            if (useSkinnedMesh)
            {
                // 스킨드: bindpose를 함께 이동해야 스키닝 결과가 보존됨 (버텍스만 옮기면 애니메이션 시 왜곡)
                Matrix4x4[] bindposes = workingMesh.bindposes;
                if (bindposes != null && bindposes.Length > 0)
                {
                    Matrix4x4 offset = Matrix4x4.Translate(localPoint);
                    for (int i = 0; i < bindposes.Length; i++) bindposes[i] = bindposes[i] * offset;
                    workingMesh.bindposes = bindposes;
                }
            }
            else
            {
                // 피벗 이동의 표준 semantics: 메시는 월드에 고정되고 transform(피벗)이 이동한다.
                // transform을 함께 옮기지 않으면 배치된 오브젝트가 화면에서 움직여 버린다.
                Transform t = transform;
                Vector3 worldDelta = t.TransformVector(localPoint);
                t.position += worldDelta;
                for (int i = 0; i < t.childCount; i++)
                    t.GetChild(i).position -= worldDelta;
            }

            RefreshMeshCollider();
        }

        public void SetPivotRotation(Quaternion targetWorldRotation)
        {
            EnsureInitialized();
            if (workingMesh == null) return;

            Transform t = transform;
            Quaternion currentRot = t.rotation;
            Quaternion deltaRot = Quaternion.Inverse(currentRot) * targetWorldRotation;
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

            if (workingMesh.tangents != null && workingMesh.tangents.Length > 0)
            {
                Vector4[] tangents = workingMesh.tangents;
                for (int i = 0; i < tangents.Length; i++)
                {
                    Vector3 tan = new Vector3(tangents[i].x, tangents[i].y, tangents[i].z);
                    tan = inverseRot * tan;
                    tangents[i] = new Vector4(tan.x, tan.y, tan.z, tangents[i].w);
                }
                workingMesh.tangents = tangents;
            }

            if (useSkinnedMesh)
            {
                // 스킨드: bindpose에 같은 회전을 곱해 스키닝 결과를 보존
                Matrix4x4[] bindposes = workingMesh.bindposes;
                if (bindposes != null && bindposes.Length > 0)
                {
                    Matrix4x4 rot = Matrix4x4.Rotate(deltaRot);
                    for (int i = 0; i < bindposes.Length; i++) bindposes[i] = bindposes[i] * rot;
                    workingMesh.bindposes = bindposes;
                }
            }
            else
            {
                // 메시는 월드에 고정되고 피벗 축이 회전한다. 자식은 월드 자세를 유지시킨다.
                Transform t = transform;
                int childCount = t.childCount;
                if (childCount > 0)
                {
                    var childPos = new Vector3[childCount];
                    var childRot = new Quaternion[childCount];
                    for (int i = 0; i < childCount; i++)
                    {
                        Transform c = t.GetChild(i);
                        childPos[i] = c.position;
                        childRot[i] = c.rotation;
                    }
                    t.rotation = t.rotation * deltaRot;
                    for (int i = 0; i < childCount; i++)
                    {
                        Transform c = t.GetChild(i);
                        c.position = childPos[i];
                        c.rotation = childRot[i];
                    }
                }
                else
                {
                    t.rotation = t.rotation * deltaRot;
                }
            }

            RefreshMeshCollider();
        }

        public void AlignToWorld()
        {
            RotatePivotBy(Quaternion.Inverse(transform.rotation));
        }

        public void SetForwardDirection(Vector3 worldDirection)
        {
            if (worldDirection.sqrMagnitude < 0.001f) return;
            Quaternion targetRot = Quaternion.LookRotation(worldDirection, Vector3.up);
            Quaternion deltaRot = Quaternion.Inverse(transform.rotation) * targetRot;
            RotatePivotBy(deltaRot);
        }

        public Bounds GetCurrentLocalBounds()
        {
            Mesh mesh = GetSharedMesh();
            if (mesh == null) return new Bounds(Vector3.zero, Vector3.zero);
            return mesh.bounds;
        }

        public Bounds GetRenderedBounds()
        {
            if (useSkinnedMesh)
            {
                SkinnedMeshRenderer smr = GetComponent<SkinnedMeshRenderer>();
                if (smr != null) return smr.localBounds;
            }
            Mesh mesh = GetSharedMesh();
            if (mesh == null) return new Bounds(Vector3.zero, Vector3.zero);
            return mesh.bounds;
        }

        public void RestoreOriginalMesh()
        {
            if (originalMesh != null) SetSharedMesh(originalMesh);

            if (workingMesh != null)
            {
                DestroyImmediate(workingMesh);
                workingMesh = null;
            }

            RefreshMeshCollider();
        }

        // 에디터용: 원본 복원 후 workingMesh를 파괴하지 않고 반환한다.
        // (Undo 스택에 기록된 메시를 plain DestroyImmediate로 파괴하면 Ctrl+Z 시 파괴된 메시를 참조하게 됨)
        public Mesh DetachWorkingMesh()
        {
            if (originalMesh != null) SetSharedMesh(originalMesh);

            Mesh detached = workingMesh;
            workingMesh = null;
            RefreshMeshCollider();
            return detached;
        }

        private void RefreshMeshCollider()
        {
            MeshCollider meshCollider = GetComponent<MeshCollider>();
            if (meshCollider == null) return;

            Mesh mesh = GetSharedMesh();
            if (mesh == null) return;

            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = mesh;
        }
    }
}