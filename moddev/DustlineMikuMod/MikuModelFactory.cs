using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DustlineMikuMod
{
    /// <summary>
    /// 把解析后的 glTF 构建为 Unity GameObject 层级。
    /// 坐标转换：glTF 为右手系（模型正面 +Z），Unity 为左手系，
    /// 因此顶点/节点 Z 取反、四元数 (x,y,z,w) → (-x,-y,z,w)、三角形绕序反转；
    /// 最后容器绕 Y 轴旋转，使模型正面朝向角色前方。
    /// </summary>
    internal static class MikuModelFactory
    {
        public sealed class BuildResult
        {
            public GameObject Root;
            public Transform Pivot;          // 用于整体位移跟随（静态模型）
            public Transform[] NodeTransforms;
            public Dictionary<string, Transform> NodesByName;
            public Dictionary<string, Transform> NodesByKey;
            public bool HasSkin;
            public int TriangleCount;
            public float AutoFitScale = 1f;
        }

        public static BuildResult Build(Gltf gltf, string modelName, float uniformScale, float yawOffsetDegrees)
        {
            BuildResult result = new BuildResult
            {
                NodeTransforms = new Transform[gltf.Nodes.Length],
                NodesByName = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase),
                NodesByKey = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase)
            };

            GameObject container = new GameObject(modelName + " [Mikumod]");
            container.transform.localPosition = Vector3.zero;
            container.transform.localRotation = Quaternion.Euler(0f, yawOffsetDegrees, 0f);
            container.transform.localScale = Vector3.one * uniformScale;
            result.Root = container;

            GameObject pivot = new GameObject("FollowPivot");
            pivot.transform.SetParent(container.transform, worldPositionStays: false);
            result.Pivot = pivot.transform;
            Transform pivotTransform = pivot.transform;

            int count = gltf.Nodes.Length;
            int[] parentOf = BuildParentMap(gltf);
            Matrix4x4[] worldMatrices = ComputeWorldMatrices(gltf, parentOf);

            // 1) 创建全部节点对象
            for (int i = 0; i < count; i++)
            {
                Gltf.NodeDef def = gltf.Nodes[i];
                GameObject go = new GameObject(SanitizeName(def.Name, i));
                go.transform.localPosition = def.Translation;
                go.transform.localRotation = def.Rotation;
                go.transform.localScale = def.Scale;
                result.NodeTransforms[i] = go.transform;
                if (!result.NodesByName.ContainsKey(def.Name)) result.NodesByName[def.Name] = go.transform;
                string key = NormalizeBoneKey(def.Name);
                if (key.Length > 0 && !result.NodesByKey.ContainsKey(key)) result.NodesByKey[key] = go.transform;
            }

            // 2) 挂接层级（父先于子）
            for (int i = 0; i < count; i++)
            {
                Transform parent = parentOf[i] >= 0 ? result.NodeTransforms[parentOf[i]] : pivotTransform;
                result.NodeTransforms[i].SetParent(parent, worldPositionStays: false);
            }

            // 3) 场景根节点
            if (gltf.SceneRoots.Length == 0)
            {
                for (int i = 0; i < count; i++)
                {
                    if (parentOf[i] < 0) result.NodeTransforms[i].SetParent(pivotTransform, worldPositionStays: false);
                }
            }

            // 4) 网格
            List<Mesh> staticMeshes = new List<Mesh>();
            for (int nodeIndex = 0; nodeIndex < count; nodeIndex++)
            {
                Gltf.NodeDef node = gltf.Nodes[nodeIndex];
                if (node.Mesh < 0 || node.Mesh >= gltf.Meshes.Length) continue;
                Gltf.MeshDef mesh = gltf.Meshes[node.Mesh];
                Transform nodeTransform = result.NodeTransforms[nodeIndex];
                Gltf.SkinDef skin = node.Skin >= 0 && node.Skin < gltf.Skins.Length ? gltf.Skins[node.Skin] : null;
                result.HasSkin |= skin != null;

                foreach (Gltf.PrimitiveDef prim in mesh.Primitives)
                {
                    if (prim.PositionsAccessor < 0 || prim.VertexCount <= 0) continue;
                    GameObject part = new GameObject(SanitizeName(mesh.Name, nodeIndex) + "_part");
                    part.transform.SetParent(nodeTransform, worldPositionStays: false);
                    Mesh built = BuildMesh(gltf, prim, out int triangleCount);
                    result.TriangleCount += triangleCount;
                    Material material = BuildMaterial(gltf, prim.Material, modelName);

                    if (skin != null && prim.WeightsAccessor >= 0 && prim.JointsAccessor >= 0)
                    {
                        SkinnedMeshRenderer renderer = part.AddComponent<SkinnedMeshRenderer>();
                        Transform[] bones = new Transform[skin.Joints.Length];
                        Matrix4x4[] bindPoses = new Matrix4x4[skin.Joints.Length];
                        for (int b = 0; b < skin.Joints.Length; b++)
                        {
                            int jointNode = skin.Joints[b];
                            bool valid = jointNode >= 0 && jointNode < count;
                            bones[b] = valid ? result.NodeTransforms[jointNode] : nodeTransform;
                            bindPoses[b] = valid ? worldMatrices[jointNode].inverse : Matrix4x4.identity;
                        }
                        ApplyBoneWeights(gltf, built, prim);
                        built.bindposes = bindPoses;
                        renderer.sharedMesh = built;
                        renderer.bones = bones;
                        renderer.rootBone = skin.SkeletonRoot >= 0 && skin.SkeletonRoot < result.NodeTransforms.Length
                            ? result.NodeTransforms[skin.SkeletonRoot]
                            : (bones.Length > 0 ? bones[0] : nodeTransform);
                        renderer.quality = SkinQuality.Bone4;
                        renderer.updateWhenOffscreen = true;
                        renderer.localBounds = Expand(built.bounds, 2f);
                        ConfigureRenderer(renderer, material);
                    }
                    else
                    {
                        MeshFilter filter = part.AddComponent<MeshFilter>();
                        filter.sharedMesh = built;
                        MeshRenderer renderer = part.AddComponent<MeshRenderer>();
                        ConfigureRenderer(renderer, material);
                        staticMeshes.Add(built);
                    }
                }
            }

            // 5) 静态模型自动定标与居中：
//    Sketchfab 导出的静态模型顶点坐标往往原点在包围盒角上（有的高达 20 个单位），
//    直接缩放会变成巨型。这里按目标身高缩放，并把底部对齐到角色脚下、水平居中。
            if (!result.HasSkin && staticMeshes.Count > 0)
            {
                Bounds combined = staticMeshes[0].bounds;
                for (int i = 1; i < staticMeshes.Count; i++) combined.Encapsulate(staticMeshes[i].bounds);

                float height = Mathf.Max(0.01f, combined.size.y);
                float fitScale = MikuConfig.StaticModelHeight / height;
                GameObject fit = new GameObject("AutoFit");
                fit.transform.SetParent(pivotTransform, worldPositionStays: false);

                for (int i = 0; i < count; i++)
                {
                    if (parentOf[i] < 0) result.NodeTransforms[i].SetParent(fit.transform, worldPositionStays: false);
                }
                fit.transform.localScale = Vector3.one * fitScale;
                fit.transform.localPosition = new Vector3(
                    -combined.center.x * fitScale,
                    -combined.min.y * fitScale,
                    -combined.center.z * fitScale);
                result.AutoFitScale = uniformScale * fitScale;
                MikuModPlugin.Log?.LogInfo($"Static model auto-fit: bounds={combined.size} scale={fitScale:F4} total={result.AutoFitScale:F4}");
            }

            return result;
        }

        private static void ApplyBoneWeights(Gltf gltf, Mesh mesh, Gltf.PrimitiveDef prim)
        {
            int count = prim.VertexCount;
            int[][] joints = gltf.ReadJoints(prim.JointsAccessor, count);
            Vector4[] weights = gltf.ReadWeights(prim.WeightsAccessor, count);
            BoneWeight[] result = new BoneWeight[count];
            for (int i = 0; i < count; i++)
            {
                int[] j = joints[i];
                Vector4 w = weights[i];
                result[i] = new BoneWeight
                {
                    boneIndex0 = j[0], weight0 = w.x,
                    boneIndex1 = j[1], weight1 = w.y,
                    boneIndex2 = j[2], weight2 = w.z,
                    boneIndex3 = j[3], weight3 = w.w
                };
            }
            mesh.boneWeights = result;
        }

        private static void ConfigureRenderer(Renderer renderer, Material material)
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.Off;
        }

        private static Bounds Expand(Bounds bounds, float factor)
        {
            bounds.size *= factor;
            return bounds;
        }

        private static int[] BuildParentMap(Gltf gltf)
        {
            int count = gltf.Nodes.Length;
            int[] parentOf = new int[count];
            for (int i = 0; i < count; i++) parentOf[i] = -1;
            for (int parent = 0; parent < count; parent++)
            {
                foreach (int child in gltf.Nodes[parent].Children)
                {
                    if (child >= 0 && child < count && parentOf[child] < 0) parentOf[child] = parent;
                }
            }
            return parentOf;
        }

        private static Matrix4x4[] ComputeWorldMatrices(Gltf gltf, int[] parentOf)
        {
            int count = gltf.Nodes.Length;
            Matrix4x4[] world = new Matrix4x4[count];
            Matrix4x4[] local = new Matrix4x4[count];
            for (int i = 0; i < count; i++)
            {
                Gltf.NodeDef def = gltf.Nodes[i];
                local[i] = def.HasMatrix ? def.Matrix : Matrix4x4.TRS(def.Translation, def.Rotation, def.Scale);
            }
            bool[] done = new bool[count];
            int remaining = count;
            bool progress = true;
            while (remaining > 0 && progress)
            {
                progress = false;
                for (int i = 0; i < count; i++)
                {
                    if (done[i]) continue;
                    int parent = parentOf[i];
                    if (parent < 0 || done[parent])
                    {
                        world[i] = parent < 0 ? local[i] : world[parent] * local[i];
                        done[i] = true;
                        remaining--;
                        progress = true;
                    }
                }
            }
            return world;
        }

        private static Mesh BuildMesh(Gltf gltf, Gltf.PrimitiveDef prim, out int triangleCount)
        {
            Vector3[] positions = gltf.ReadPositions(prim.PositionsAccessor);
            Vector3[] normals = prim.NormalsAccessor >= 0 ? gltf.ReadNormals(prim.NormalsAccessor) : null;
            Vector2[] uvs = prim.UvAccessors[0] >= 0 ? gltf.ReadUvs(prim.UvAccessors[0]) : null;
            int[] sourceIndices = prim.IndicesAccessor >= 0 ? gltf.ReadIndices(prim.IndicesAccessor) : null;

            int vertexCount = positions.Length;
            Vector3[] vertices = new Vector3[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                Vector3 p = positions[i];
                vertices[i] = new Vector3(p.x, p.y, -p.z);
            }
            Vector3[] convertedNormals = null;
            if (normals != null && normals.Length == vertexCount)
            {
                convertedNormals = new Vector3[vertexCount];
                for (int i = 0; i < vertexCount; i++)
                {
                    Vector3 n = normals[i];
                    convertedNormals[i] = new Vector3(n.x, n.y, -n.z);
                }
            }
            Vector2[] convertedUvs = null;
            if (uvs != null && uvs.Length == vertexCount)
            {
                convertedUvs = new Vector2[vertexCount];
                for (int i = 0; i < vertexCount; i++) convertedUvs[i] = new Vector2(uvs[i].x, 1f - uvs[i].y);
            }

            int indexCount = sourceIndices != null ? sourceIndices.Length : vertexCount;
            int[] triangles = new int[indexCount];
            for (int i = 0; i < indexCount; i++)
            {
                int v = sourceIndices != null ? sourceIndices[i] : i;
                triangles[indexCount - 1 - i] = v;   // 绕序反转
            }

            Mesh mesh = new Mesh
            {
                name = "Mikumod mesh",
                indexFormat = vertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
            };
            mesh.vertices = vertices;
            if (convertedNormals != null) mesh.normals = convertedNormals;
            else mesh.RecalculateNormals();
            if (convertedUvs != null) mesh.uv = convertedUvs;
            mesh.triangles = triangles;
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            triangleCount = indexCount / 3;
            return mesh;
        }

        private static Material BuildMaterial(Gltf gltf, int materialIndex, string modelName)
        {
            Gltf.MaterialDef def = materialIndex >= 0 && materialIndex < gltf.Materials.Length
                ? gltf.Materials[materialIndex]
                : new Gltf.MaterialDef { Name = "default" };

            Shader shader = Shader.Find(def.Unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            Material material = new Material(shader)
            {
                name = modelName + " " + def.Name,
                enableInstancing = true
            };

            Texture2D baseMap = gltf.ReadTexture(def.BaseColorTexture);
            if (baseMap != null)
            {
                material.SetTexture("_BaseMap", baseMap);
                material.SetTexture("_MainTex", baseMap);
            }
            material.SetColor("_BaseColor", def.BaseColorFactor);
            material.SetColor("_Color", def.BaseColorFactor);
            if (!def.Unlit)
            {
                material.SetFloat("_Metallic", Mathf.Clamp01(def.Metallic));
                material.SetFloat("_Smoothness", Mathf.Clamp01(def.Smoothness * 0.4f));
            }
            material.SetFloat("_Cull", def.DoubleSided ? 0f : 2f);

            if (def.AlphaMask)
            {
                material.EnableKeyword("_ALPHATEST_ON");
                material.SetFloat("_AlphaClip", 1f);
                material.SetFloat("_Cutoff", 0.35f);
                material.SetFloat("_Surface", 0f);
                material.renderQueue = 2450;
            }
            else if (def.AlphaBlend || def.BaseColorFactor.a < 0.999f)
            {
                material.SetOverrideTag("RenderType", "Transparent");
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 0f);
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.DisableKeyword("_ALPHATEST_ON");
                material.renderQueue = (int)RenderQueue.Transparent;
            }
            return material;
        }

        public static string SanitizeName(string name, int fallbackIndex)
        {
            return string.IsNullOrEmpty(name) ? "node_" + fallbackIndex : name;
        }

        /// <summary>规整 glTF 关节名：去掉 Sketchfab 导出的 "_序号" 后缀与空白，便于与游戏骨骼名匹配。</summary>
        public static string NormalizeBoneKey(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            string trimmed = name.Trim();
            int cut = trimmed.LastIndexOf('_');
            if (cut > 0 && cut + 1 < trimmed.Length)
            {
                bool allDigits = true;
                for (int i = cut + 1; i < trimmed.Length; i++)
                {
                    if (!char.IsDigit(trimmed[i])) { allDigits = false; break; }
                }
                if (allDigits) trimmed = trimmed.Substring(0, cut);
            }
            return trimmed.Replace(" ", string.Empty).ToLowerInvariant();
        }
    }
}