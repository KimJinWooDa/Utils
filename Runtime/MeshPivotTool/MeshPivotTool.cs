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
        [SerializeField] private Vector3 originalPosition;
        [SerializeField] private Quaternion originalRotation = Quaternion.identity;
        [SerializeField] private bool transformStored;

        public float Snap
        {
            get => snap;
            set => snap = Mathf.Max(0f, value);
        }

        public bool HasWorkingMesh => workingMesh != null;

        public void EnsureInitialized()
        {
            if (!transformStored)
            {
                originalPosition = transform.position;
                originalRotation = transform.rotation;
                transformStored = true;
            }

            MeshFilter meshFilter = GetComponent<MeshFilter>();
            if (meshFilter == null)
                return;

            if (originalMesh == null)
                originalMesh = meshFilter.sharedMesh;

            if (originalMesh == null)
                return;

            if (workingMesh == null)
            {
                workingMesh = Instantiate(originalMesh);
                workingMesh.name = originalMesh.name + "_PivotEdited";
                meshFilter.sharedMesh = workingMesh;
                RefreshMeshCollider();
            }
        }

        public void MovePivotToWorld(Vector3 targetPivotWorld)
        {
            EnsureInitialized();

            Transform t = transform;
            Vector3 currentPivotWorld = t.position;
            Vector3 deltaWorld = targetPivotWorld - currentPivotWorld;
            if (deltaWorld.sqrMagnitude <= 0f)
                return;

            MeshFilter meshFilter = GetComponent<MeshFilter>();
            if (meshFilter == null || workingMesh == null)
            {
                t.position = targetPivotWorld;
                return;
            }

            Vector3 deltaLocal = t.InverseTransformVector(deltaWorld);

            Vector3[] verts = workingMesh.vertices;
            for (int i = 0; i < verts.Length; i++)
                verts[i] -= deltaLocal;

            workingMesh.vertices = verts;
            workingMesh.RecalculateBounds();
            workingMesh.RecalculateNormals();

            t.position = targetPivotWorld;

            RefreshMeshCollider();
        }

        public void SetPivotToLocalPoint(Vector3 localPoint)
        {
            Transform t = transform;
            Vector3 targetWorld = t.TransformPoint(localPoint);
            MovePivotToWorld(targetWorld);
        }

        public void SetPivotRotation(Quaternion targetWorldRotation)
        {
            EnsureInitialized();

            Transform t = transform;

            MeshFilter meshFilter = GetComponent<MeshFilter>();
            if (meshFilter == null || workingMesh == null)
            {
                t.rotation = targetWorldRotation;
                return;
            }

            Quaternion currentRot = t.rotation;
            Quaternion deltaRot = Quaternion.Inverse(currentRot) * targetWorldRotation;

            if (Quaternion.Angle(Quaternion.identity, deltaRot) < 0.001f)
                return;

            Quaternion inverseRot = Quaternion.Inverse(deltaRot);

            Vector3[] verts = workingMesh.vertices;
            for (int i = 0; i < verts.Length; i++)
                verts[i] = inverseRot * verts[i];

            workingMesh.vertices = verts;
            workingMesh.RecalculateBounds();
            workingMesh.RecalculateNormals();

            Vector3[] normals = workingMesh.normals;
            for (int i = 0; i < normals.Length; i++)
                normals[i] = inverseRot * normals[i];
            workingMesh.normals = normals;

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

            t.rotation = targetWorldRotation;

            RefreshMeshCollider();
        }

        public void RotatePivotBy(Quaternion deltaRotation)
        {
            Quaternion target = transform.rotation * deltaRotation;
            SetPivotRotation(target);
        }

        public void AlignToWorld()
        {
            SetPivotRotation(Quaternion.identity);
        }

        public void SetForwardDirection(Vector3 worldDirection)
        {
            if (worldDirection.sqrMagnitude < 0.001f)
                return;

            Quaternion targetRot = Quaternion.LookRotation(worldDirection, Vector3.up);
            SetPivotRotation(targetRot);
        }

        public bool TryGetCurrentLocalBounds(out Bounds bounds)
        {
            MeshFilter meshFilter = GetComponent<MeshFilter>();
            Mesh mesh = meshFilter != null ? meshFilter.sharedMesh : null;
            if (mesh == null)
            {
                bounds = default;
                return false;
            }

            bounds = mesh.bounds;
            return true;
        }

        public Bounds GetCurrentLocalBounds()
        {
            Bounds b;
            if (TryGetCurrentLocalBounds(out b))
                return b;

            return new Bounds(Vector3.zero, Vector3.zero);
        }

        public void RestoreOriginalMesh()
        {
            MeshFilter meshFilter = GetComponent<MeshFilter>();
            if (meshFilter != null)
            {
                if (originalMesh != null)
                    meshFilter.sharedMesh = originalMesh;
            }

            if (transformStored)
            {
                transform.position = originalPosition;
                transform.rotation = originalRotation;
            }

            if (workingMesh != null)
            {
                DestroyImmediate(workingMesh);
                workingMesh = null;
            }

            transformStored = false;
            RefreshMeshCollider();
        }

        private void RefreshMeshCollider()
        {
            MeshCollider meshCollider = GetComponent<MeshCollider>();
            if (meshCollider == null)
                return;

            MeshFilter meshFilter = GetComponent<MeshFilter>();
            Mesh mesh = meshFilter != null ? meshFilter.sharedMesh : null;
            if (mesh == null)
            {
                meshCollider.sharedMesh = null;
                return;
            }

            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = mesh;
        }
    }
}
