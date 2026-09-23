using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace TelleR.ConcaveCollider
{
    /// <summary>
    /// 씬 오브젝트에서 분해 입력을 읽는 에디터 헬퍼(읽기 전용, 메인 스레드).
    /// 메시는 MeshUtility.AcquireReadOnlyMeshData로 읽으므로 모델 임포터의 Read/Write가 꺼져 있어도 된다.
    /// </summary>
    public static class ConcaveMeshInput
    {
        /// <summary>메시의 모든 삼각형 서브메시를 toTarget으로 변환해 목록에 붙인다. 음수 스케일이면 감김을 뒤집는다.</summary>
        public static bool AppendMesh(Mesh mesh, Matrix4x4 toTarget, List<Vector3> vertices, List<int> triangles)
        {
            if (mesh == null || vertices == null || triangles == null) return false;
            Mesh.MeshDataArray dataArray = default;
            NativeArray<Vector3> source = default;
            bool acquired = false;
            try
            {
                dataArray = MeshUtility.AcquireReadOnlyMeshData(mesh);
                acquired = true;
                if (dataArray.Length == 0) return false;
                Mesh.MeshData data = dataArray[0];
                source = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                data.GetVertices(source);
                int offset = vertices.Count;
                for (int i = 0; i < source.Length; i++) vertices.Add(toTarget.MultiplyPoint3x4(source[i]));
                bool flip = toTarget.determinant < 0f;
                int before = triangles.Count;
                if (data.indexFormat == IndexFormat.UInt16)
                {
                    NativeArray<ushort> indices = data.GetIndexData<ushort>();
                    for (int s = 0; s < data.subMeshCount; s++)
                    {
                        SubMeshDescriptor sub = data.GetSubMesh(s);
                        if (sub.topology != MeshTopology.Triangles) continue;
                        for (int i = 0; i + 2 < sub.indexCount; i += 3)
                        {
                            Add(offset + sub.baseVertex + indices[sub.indexStart + i],
                                offset + sub.baseVertex + indices[sub.indexStart + i + 1],
                                offset + sub.baseVertex + indices[sub.indexStart + i + 2]);
                        }
                    }
                }
                else
                {
                    NativeArray<int> indices = data.GetIndexData<int>();
                    for (int s = 0; s < data.subMeshCount; s++)
                    {
                        SubMeshDescriptor sub = data.GetSubMesh(s);
                        if (sub.topology != MeshTopology.Triangles) continue;
                        for (int i = 0; i + 2 < sub.indexCount; i += 3)
                        {
                            Add(offset + sub.baseVertex + indices[sub.indexStart + i],
                                offset + sub.baseVertex + indices[sub.indexStart + i + 1],
                                offset + sub.baseVertex + indices[sub.indexStart + i + 2]);
                        }
                    }
                }

                return triangles.Count > before;

                void Add(int a, int b, int c)
                {
                    if (a < offset || b < offset || c < offset || a >= vertices.Count || b >= vertices.Count || c >= vertices.Count) return;
                    triangles.Add(a);
                    triangles.Add(flip ? c : b);
                    triangles.Add(flip ? b : c);
                }
            }
            finally
            {
                if (source.IsCreated) source.Dispose();
                if (acquired) dataArray.Dispose();
            }
        }

        private static readonly List<Matrix4x4> BindposeScratch = new List<Matrix4x4>();

        /// <summary>
        /// SkinnedMeshRenderer가 본·가중치·바인드포즈를 제대로 갖췄는지(블렌드셰이프 전용 메시는 false).
        /// 창 목록을 갱신할 때마다 불리므로 가중치·바인드포즈 배열을 복사하지 않고 개수만 본다(실제 가중치는 TryCreateSkinnedInput에서 읽는다).
        /// </summary>
        public static bool HasUsableSkin(SkinnedMeshRenderer renderer)
        {
            if (renderer == null || renderer.sharedMesh == null) return false;
            Mesh mesh = renderer.sharedMesh;
            Transform[] bones = renderer.bones;
            if (bones == null || bones.Length == 0) return false;
            try
            {
                mesh.GetBindposes(BindposeScratch);
                if (BindposeScratch.Count != bones.Length) return false;

                // 메시 내부 메모리를 가리키는 NativeArray(복사·Dispose 없음). 길이는 vertexCount 또는 0(가중치 없음).
                return mesh.GetBonesPerVertex().Length == mesh.vertexCount;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                BindposeScratch.Clear();
            }
        }

        /// <summary>lossyScale이 비균일(1% 초과 차이)하거나 0에 가까운지.</summary>
        public static bool HasNonUniformScale(Vector3 scale)
        {
            float x = Mathf.Abs(scale.x), y = Mathf.Abs(scale.y), z = Mathf.Abs(scale.z);
            float largest = Mathf.Max(x, Mathf.Max(y, z));
            float smallest = Mathf.Min(x, Mathf.Min(y, z));
            return smallest < 1e-6f || largest / smallest > 1.01f;
        }

        /// <summary>
        /// SkinnedMeshRenderer에서 스킨드 입력을 만든다. root는 본 깊이 계산 기준(보통 대상 오브젝트)이며
        /// null이면 씬 루트까지 센다. 본 가중치를 읽을 수 없으면 false와 한국어 사유를 돌려준다.
        /// </summary>
        public static bool TryCreateSkinnedInput(SkinnedMeshRenderer renderer, Transform root, out ConcaveSkinnedInput input, out string error)
        {
            input = null;
            error = null;
            if (renderer == null || renderer.sharedMesh == null)
            {
                error = "SkinnedMeshRenderer 또는 메시가 없습니다.";
                return false;
            }

            Mesh mesh = renderer.sharedMesh;
            Transform[] bones = renderer.bones;
            if (bones == null || bones.Length == 0)
            {
                error = "본이 없는 SkinnedMeshRenderer입니다. 정적 메시로 처리하세요.";
                return false;
            }

            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            if (!AppendMesh(mesh, Matrix4x4.identity, vertices, triangles))
            {
                error = $"메시 '{mesh.name}'의 삼각형을 읽지 못했습니다.";
                return false;
            }

            BoneWeight[] weights;
            Matrix4x4[] bindposes;
            try
            {
                weights = mesh.boneWeights;
                bindposes = mesh.bindposes;
            }
            catch (Exception exception)
            {
                error = $"메시 '{mesh.name}'의 본 가중치를 읽지 못했습니다(모델 임포터 Read/Write를 켜 보세요): {exception.Message}";
                return false;
            }

            if (weights.Length != vertices.Count || bindposes.Length != bones.Length)
            {
                error = $"메시 '{mesh.name}'의 본 가중치({weights.Length})/정점({vertices.Count}) 또는 바인드포즈({bindposes.Length})/본({bones.Length}) 개수가 맞지 않습니다.";
                return false;
            }

            var index = new Dictionary<Transform, int>();
            for (int b = 0; b < bones.Length; b++)
            {
                if (bones[b] != null && !index.ContainsKey(bones[b])) index.Add(bones[b], b);
            }

            var boneInfos = new ConcaveBone[bones.Length];
            for (int b = 0; b < bones.Length; b++)
            {
                Transform bone = bones[b];
                var info = new ConcaveBone();
                if (bone != null)
                {
                    info.Name = bone.name;
                    info.LocalToWorld = bone.localToWorldMatrix;
                    Transform current = bone.parent;
                    while (current != null && current != root)
                    {
                        if (index.TryGetValue(current, out int parentIndex))
                        {
                            info.ParentIndex = parentIndex;
                            break;
                        }

                        current = current.parent;
                    }

                    int depth = 0;
                    current = bone;
                    while (current != null && current != root)
                    {
                        depth++;
                        current = current.parent;
                    }

                    info.Depth = depth;
                }
                else
                {
                    info.Name = string.Empty;
                    info.LocalToWorld = renderer.transform.localToWorldMatrix;
                }

                boneInfos[b] = info;
            }

            input = new ConcaveSkinnedInput
            {
                Vertices = vertices.ToArray(),
                Triangles = triangles.ToArray(),
                BoneWeights = weights,
                Bindposes = bindposes,
                Bones = boneInfos
            };
            return true;
        }
    }
}
