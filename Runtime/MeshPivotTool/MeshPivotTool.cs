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
            if (localPoint.sqrMagnitude <= 0.0001f) return;

            Vector3[] verts = workingMesh.vertices;
            for (int i = 0; i < verts.Length; i++) verts[i] -= localPoint;
            workingMesh.vertices = verts;
            workingMesh.RecalculateBounds();
            workingMesh.RecalculateNormals();
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
            workingMesh.RecalculateNormals();

            Vector3[] normals = workingMesh.normals;
            for (int i = 0; i < normals.Length; i++) normals[i] = inverseRot * normals[i];
            workingMesh.normals = normals;

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