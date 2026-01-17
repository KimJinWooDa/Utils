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

        public float Snap
        {
            get => snap;
            set => snap = Mathf.Max(0f, value);
        }

        public bool HasWorkingMesh => workingMesh != null;

        public void EnsureInitialized()
        {
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

            MeshFilter meshFilter = GetComponent<MeshFilter>();
            if (meshFilter == null || workingMesh == null)
                return;

            Transform t = transform;

            Vector3 currentPivotWorld = t.position;
            Vector3 deltaWorld = targetPivotWorld - currentPivotWorld;
            if (deltaWorld.sqrMagnitude <= 0f)
                return;

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

        public Bounds GetCurrentLocalBounds()
        {
            MeshFilter meshFilter = GetComponent<MeshFilter>();
            Mesh mesh = meshFilter != null ? meshFilter.sharedMesh : null;
            if (mesh == null)
                return new Bounds(Vector3.zero, Vector3.zero);

            return mesh.bounds;
        }

        public void RestoreOriginalMesh()
        {
            MeshFilter meshFilter = GetComponent<MeshFilter>();
            if (meshFilter == null)
                return;

            if (originalMesh != null)
                meshFilter.sharedMesh = originalMesh;

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
            if (meshCollider == null)
                return;

            MeshFilter meshFilter = GetComponent<MeshFilter>();
            Mesh mesh = meshFilter != null ? meshFilter.sharedMesh : null;
            if (mesh == null)
                return;

            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = mesh;
        }
    }
}