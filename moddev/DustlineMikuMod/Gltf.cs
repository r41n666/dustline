using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace DustlineMikuMod
{
    /// <summary>
    /// glTF 2.0 / GLB 运行时解析器。只读取构建 Unity 模型所需的子集：
    /// 节点层级、网格（POSITION/NORMAL/TEXCOORD/JOINTS/WEIGHTS/indices）、蒙皮、材质、贴图。
    /// 不支持 sparse accessor、Draco 压缩、动画。
    /// </summary>
    internal sealed class Gltf
    {
        public sealed class NodeDef
        {
            public string Name;
            public int[] Children = Array.Empty<int>();
            public Vector3 Translation;
            public Quaternion Rotation = Quaternion.identity;
            public Vector3 Scale = Vector3.one;
            public bool HasMatrix;
            public Matrix4x4 Matrix = Matrix4x4.identity;
            public int Mesh = -1;
            public int Skin = -1;
        }

        public sealed class PrimitiveDef
        {
            public int PositionsAccessor = -1;
            public int NormalsAccessor = -1;
            public int[] UvAccessors = new int[8];
            public int JointsAccessor = -1;
            public int WeightsAccessor = -1;
            public int IndicesAccessor = -1;
            public int Material = -1;
            public int VertexCount;
            public int IndexCount;
        }

        public sealed class MeshDef
        {
            public string Name;
            public PrimitiveDef[] Primitives;
        }

        public sealed class SkinDef
        {
            public int[] Joints = Array.Empty<int>();
            public Matrix4x4[] InverseBindPoses;
            public int SkeletonRoot = -1;
        }

        public sealed class MaterialDef
        {
            public string Name;
            public int BaseColorTexture = -1;
            public Color BaseColorFactor = Color.white;
            public bool AlphaBlend;
            public bool AlphaMask;
            public bool DoubleSided;
            public bool Unlit;
            public float Metallic;
            public float Smoothness;
        }

        public NodeDef[] Nodes;
        public MeshDef[] Meshes;
        public SkinDef[] Skins;
        public MaterialDef[] Materials;
        public JsonValue Root;
        public int[] SceneRoots;

        private byte[] binary;
        private JsonValue[] accessors;
        private JsonValue[] bufferViews;
        private JsonValue[] textures;
        private JsonValue[] images;
        private JsonValue[] samplers;
        private readonly Dictionary<int, Texture2D> textureCache = new Dictionary<int, Texture2D>();

        private const int ComponentByte = 5120;
        private const int ComponentUnsignedByte = 5121;
        private const int ComponentShort = 5122;
        private const int ComponentUnsignedShort = 5123;
        private const int ComponentUnsignedInt = 5125;
        private const int ComponentFloat = 5126;
        private const int TargetArrayBuffer = 34962;
        private const int TargetElementArrayBuffer = 34963;

        public static Gltf LoadFromFile(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            if (data.Length < 12) throw new InvalidDataException("GLB too small: " + path);
            if (data[0] != (byte)'g' || data[1] != (byte)'l' || data[2] != (byte)'T' || data[3] != (byte)'F')
                throw new InvalidDataException("Not a GLB file: " + path);

            uint version = BitConverter.ToUInt32(data, 4);
            if (version != 2) MikuModPlugin.Log?.LogWarning($"GLB version {version} (expected 2): {path}");

            byte[] json = null;
            byte[] bin = null;
            int offset = 12;
            while (offset + 8 <= data.Length)
            {
                uint chunkLength = BitConverter.ToUInt32(data, offset);
                uint chunkType = BitConverter.ToUInt32(data, offset + 4);
                int start = offset + 8;
                int length = (int)Math.Min(chunkLength, (uint)(data.Length - start));
                if (chunkType == 0x4E4F534A) json = Slice(data, start, length);       // JSON
                else if (chunkType == 0x004E4942) bin = Slice(data, start, length);    // BIN
                offset = start + length;
                if (length <= 0) break;
            }

            if (json == null) throw new InvalidDataException("GLB without JSON chunk: " + path);
            return Parse(Encoding.UTF8.GetString(json), bin);
        }

        private static byte[] Slice(byte[] source, int start, int length)
        {
            byte[] result = new byte[length];
            Buffer.BlockCopy(source, start, result, 0, length);
            return result;
        }

        private static Gltf Parse(string json, byte[] bin)
        {
            Gltf gltf = new Gltf { binary = bin, Root = JsonValue.Parse(json) };
            JsonValue root = gltf.Root;

            gltf.accessors = ToArray(root["accessors"]);
            gltf.bufferViews = ToArray(root["bufferViews"]);
            gltf.textures = ToArray(root["textures"]);
            gltf.images = ToArray(root["images"]);
            gltf.samplers = ToArray(root["samplers"]);

            JsonValue nodes = root["nodes"];
            int nodeCount = nodes.Count;
            gltf.Nodes = new NodeDef[nodeCount];
            for (int i = 0; i < nodeCount; i++)
            {
                JsonValue n = nodes[i];
                NodeDef node = new NodeDef { Name = n["name"].AsString("node_" + i) };
                JsonValue children = n["children"];
                if (children.Count > 0)
                {
                    node.Children = new int[children.Count];
                    for (int c = 0; c < children.Count; c++) node.Children[c] = children[c].AsInt();
                }
                JsonValue t = n["translation"];
                if (t.Count >= 3) node.Translation = new Vector3(t[0].AsFloat(), t[1].AsFloat(), t[2].AsFloat());
                JsonValue r = n["rotation"];
                if (r.Count >= 4) node.Rotation = new Quaternion(r[0].AsFloat(), r[1].AsFloat(), r[2].AsFloat(), r[3].AsFloat());
                JsonValue s = n["scale"];
                if (s.Count >= 3) node.Scale = new Vector3(s[0].AsFloat(), s[1].AsFloat(), s[2].AsFloat());
                JsonValue m = n["matrix"];
                if (m.Count >= 16)
                {
                    node.HasMatrix = true;
                    for (int c = 0; c < 4; c++)
                        for (int rr = 0; rr < 4; rr++)
                            node.Matrix[rr, c] = m[c * 4 + rr].AsFloat();
                }
                node.Mesh = n.Has("mesh") ? n["mesh"].AsInt(-1) : -1;
                node.Skin = n.Has("skin") ? n["skin"].AsInt(-1) : -1;
                gltf.Nodes[i] = node;
            }

            JsonValue meshes = root["meshes"];
            gltf.Meshes = new MeshDef[meshes.Count];
            for (int i = 0; i < meshes.Count; i++)
            {
                JsonValue m = meshes[i];
                JsonValue prims = m["primitives"];
                PrimitiveDef[] list = new PrimitiveDef[prims.Count];
                for (int p = 0; p < prims.Count; p++)
                {
                    JsonValue prim = prims[p];
                    JsonValue attrs = prim["attributes"];
                    PrimitiveDef def = new PrimitiveDef
                    {
                        Material = prim.Has("material") ? prim["material"].AsInt(-1) : -1,
                        IndicesAccessor = prim.Has("indices") ? prim["indices"].AsInt(-1) : -1
                    };
                    def.PositionsAccessor = attrs.Has("POSITION") ? attrs["POSITION"].AsInt(-1) : -1;
                    def.NormalsAccessor = attrs.Has("NORMAL") ? attrs["NORMAL"].AsInt(-1) : -1;
                    def.JointsAccessor = attrs.Has("JOINTS_0") ? attrs["JOINTS_0"].AsInt(-1) : -1;
                    def.WeightsAccessor = attrs.Has("WEIGHTS_0") ? attrs["WEIGHTS_0"].AsInt(-1) : -1;
                    for (int uv = 0; uv < 8; uv++)
                    {
                        string key = "TEXCOORD_" + uv;
                        if (attrs.Has(key)) def.UvAccessors[uv] = attrs[key].AsInt(-1);
                    }
                    def.VertexCount = def.PositionsAccessor >= 0 ? gltf.AccessorCount(def.PositionsAccessor) : 0;
                    def.IndexCount = def.IndicesAccessor >= 0 ? gltf.AccessorCount(def.IndicesAccessor) : def.VertexCount;
                    list[p] = def;
                }
                gltf.Meshes[i] = new MeshDef { Name = m["name"].AsString("mesh_" + i), Primitives = list };
            }

            JsonValue skins = root["skins"];
            gltf.Skins = new SkinDef[skins.Count];
            for (int i = 0; i < skins.Count; i++)
            {
                JsonValue s = skins[i];
                JsonValue joints = s["joints"];
                SkinDef skin = new SkinDef
                {
                    Joints = new int[joints.Count],
                    SkeletonRoot = s.Has("skeleton") ? s["skeleton"].AsInt(-1) : -1
                };
                for (int j = 0; j < joints.Count; j++) skin.Joints[j] = joints[j].AsInt();
                int ibm = s.Has("inverseBindMatrices") ? s["inverseBindMatrices"].AsInt(-1) : -1;
                if (ibm >= 0) skin.InverseBindPoses = gltf.ReadMatrices(ibm);
                gltf.Skins[i] = skin;
            }

            JsonValue materials = root["materials"];
            gltf.Materials = new MaterialDef[materials.Count];
            for (int i = 0; i < materials.Count; i++)
            {
                JsonValue m = materials[i];
                JsonValue pbr = m["pbrMetallicRoughness"];
                MaterialDef def = new MaterialDef { Name = m["name"].AsString("material_" + i) };
                if (pbr.Has("baseColorTexture")) def.BaseColorTexture = pbr["baseColorTexture"]["index"].AsInt(-1);
                JsonValue factor = pbr["baseColorFactor"];
                if (factor.Count >= 4)
                {
                    def.BaseColorFactor = new Color(factor[0].AsFloat(1f), factor[1].AsFloat(1f), factor[2].AsFloat(1f), factor[3].AsFloat(1f));
                }
                def.Metallic = pbr.Has("metallicFactor") ? pbr["metallicFactor"].AsFloat() : 0f;
                float roughness = pbr.Has("roughnessFactor") ? pbr["roughnessFactor"].AsFloat(1f) : 1f;
                def.Smoothness = 1f - roughness;
                string alphaMode = m["alphaMode"].AsString("OPAQUE");
                def.AlphaBlend = alphaMode == "BLEND";
                def.AlphaMask = alphaMode == "MASK";
                def.DoubleSided = m["doubleSided"].AsBool();
                def.Unlit = m["extensions"] != null && m["extensions"].Has("KHR_materials_unlit");
                gltf.Materials[i] = def;
            }

            JsonValue scenes = root["scenes"];
            int sceneIndex = root.Has("scene") ? root["scene"].AsInt() : 0;
            if (scenes.Count > 0 && sceneIndex < scenes.Count)
            {
                JsonValue sceneNodes = scenes[sceneIndex]["nodes"];
                gltf.SceneRoots = new int[sceneNodes.Count];
                for (int i = 0; i < sceneNodes.Count; i++) gltf.SceneRoots[i] = sceneNodes[i].AsInt();
            }
            else
            {
                gltf.SceneRoots = Array.Empty<int>();
            }

            return gltf;
        }

        private static JsonValue[] ToArray(JsonValue value)
        {
            if (value == null || !value.IsArray) return Array.Empty<JsonValue>();
            return value.ArrayValue.ToArray();
        }

        // ---- accessor helpers -------------------------------------------------

        private int AccessorCount(int accessor)
        {
            if (accessor < 0 || accessor >= accessors.Length) return 0;
            return accessors[accessor]["count"].AsInt();
        }

        private JsonValue Accessor(int accessor) => accessors[accessor];

        /// <summary>返回某个 accessor 的字节片段（已应用 bufferView 与 accessor 的偏移）。</summary>
        private ReadOnlySpan<byte> AccessorBytes(int accessorIndex, out int stride, out int componentType, out int components)
        {
            stride = 4;
            componentType = ComponentFloat;
            components = 1;
            if (accessorIndex < 0 || accessorIndex >= accessors.Length) return ReadOnlySpan<byte>.Empty;
            JsonValue accessor = Accessor(accessorIndex);
            int view = accessor["bufferView"].AsInt(-1);
            if (view < 0) return ReadOnlySpan<byte>.Empty;
            JsonValue bufferView = bufferViews[view];
            int viewOffset = bufferView["byteOffset"].AsInt();
            int viewLength = bufferView["byteLength"].AsInt();
            int accessorOffset = accessor["byteOffset"].AsInt();
            componentType = accessor["componentType"].AsInt();
            int typeComponents = TypeComponents(accessor["type"].AsString("SCALAR"));
            components = typeComponents;
            int count = accessor["count"].AsInt();
            stride = bufferView["byteStride"].AsInt();
            if (stride <= 0) stride = componentByteSize(componentType) * components;
            int start = viewOffset + accessorOffset;
            int length = stride * count;
            if (binary == null || start < 0 || start + length > binary.Length)
            {
                // 越界保护：退化到最短可用长度
                if (binary == null || start >= binary.Length) return ReadOnlySpan<byte>.Empty;
                length = Math.Max(0, Math.Min(length, binary.Length - start));
            }
            return new ReadOnlySpan<byte>(binary, start, length);
        }

        private static int TypeComponents(string type)
        {
            switch (type)
            {
                case "SCALAR": return 1;
                case "VEC2": return 2;
                case "VEC3": return 3;
                case "VEC4": return 4;
                case "MAT2": return 4;
                case "MAT3": return 9;
                case "MAT4": return 16;
                default: return 1;
            }
        }

        public Vector3[] ReadPositions(int accessor)
        {
            ReadOnlySpan<byte> span = AccessorBytes(accessor, out int stride, out int componentType, out int components);
            int count = span.Length / Math.Max(1, stride);
            Vector3[] result = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                float x = ReadFloat(span.Slice(i * stride, Math.Min(stride, span.Length - i * stride)), 0, componentType, components);
                float y = ReadFloat(span.Slice(i * stride, Math.Min(stride, span.Length - i * stride)), 1, componentType, components);
                float z = ReadFloat(span.Slice(i * stride, Math.Min(stride, span.Length - i * stride)), 2, componentType, components);
                result[i] = new Vector3(x, y, z);
            }
            return result;
        }

        public Vector3[] ReadNormals(int accessor) => ReadPositions(accessor);

        public Vector2[] ReadUvs(int accessor)
        {
            ReadOnlySpan<byte> span = AccessorBytes(accessor, out int stride, out int componentType, out int components);
            int count = components > 0 ? span.Length / Math.Max(1, stride) : 0;
            Vector2[] result = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                int offset = i * stride;
                int avail = Math.Min(stride, Math.Max(0, span.Length - offset));
                result[i] = new Vector2(
                    ReadFloat(span.Slice(offset, avail), 0, componentType, components),
                    ReadFloat(span.Slice(offset, avail), 1, componentType, components));
            }
            return result;
        }

        public int[] ReadIndices(int accessor)
        {
            ReadOnlySpan<byte> span = AccessorBytes(accessor, out int stride, out int componentType, out int components);
            int count = Math.Max(1, components);
            int[] result = new int[span.Length / Math.Max(1, stride)];
            for (int i = 0; i < result.Length; i++)
            {
                int offset = i * stride;
                int avail = Math.Min(stride, Math.Max(0, span.Length - offset));
                result[i] = (int)ReadFloat(span.Slice(offset, avail), 0, componentType, count);
            }
            return result;
        }

        /// <summary>JOINTS_0：每顶点 4 个关节索引。</summary>
        public int[][] ReadJoints(int accessor, int vertexCount)
        {
            ReadOnlySpan<byte> span = AccessorBytes(accessor, out int stride, out int componentType, out int components);
            int[][] result = new int[vertexCount][];
            for (int i = 0; i < vertexCount; i++)
            {
                result[i] = new int[4];
                int offset = i * stride;
                if (offset >= span.Length) continue;
                int avail = Math.Min(stride, span.Length - offset);
                for (int k = 0; k < 4; k++) result[i][k] = ReadIndex(span.Slice(offset, avail), k, componentType);
            }
            return result;
        }

        /// <summary>WEIGHTS_0：每顶点 4 个权重，归一化。</summary>
        public Vector4[] ReadWeights(int accessor, int vertexCount)
        {
            ReadOnlySpan<byte> span = AccessorBytes(accessor, out int stride, out int componentType, out int components);
            Vector4[] result = new Vector4[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                int offset = i * stride;
                float w0 = 0f, w1 = 0f, w2 = 0f, w3 = 0f;
                if (offset < span.Length)
                {
                    int avail = Math.Min(stride, span.Length - offset);
                    w0 = ReadFloat(span.Slice(offset, avail), 0, componentType, components);
                    w1 = ReadFloat(span.Slice(offset, avail), 1, componentType, components);
                    w2 = ReadFloat(span.Slice(offset, avail), 2, componentType, components);
                    w3 = ReadFloat(span.Slice(offset, avail), 3, componentType, components);
                }
                float sum = w0 + w1 + w2 + w3;
                if (sum <= 0f) w0 = 1f;
                else { w0 /= sum; w1 /= sum; w2 /= sum; w3 /= sum; }
                result[i] = new Vector4(w0, w1, w2, w3);
            }
            return result;
        }

        private Matrix4x4[] ReadMatrices(int accessor)
        {
            ReadOnlySpan<byte> span = AccessorBytes(accessor, out int stride, out int componentType, out int components);
            int count = span.Length / Math.Max(1, stride);
            Matrix4x4[] result = new Matrix4x4[count];
            for (int i = 0; i < count; i++)
            {
                Matrix4x4 m = Matrix4x4.identity;
                int offset = i * stride;
                int avail = Math.Min(stride, Math.Max(0, span.Length - offset));
                for (int c = 0; c < 4; c++)
                {
                    for (int r = 0; r < 4; r++)
                    {
                        m[r, c] = ReadFloat(span.Slice(offset, avail), c * 4 + r, componentType, 16);
                    }
                }
                result[i] = m;
            }
            return result;
        }

        /// <summary>
        /// 读取整数分量（JOINTS_0 用 unsigned byte / unsigned short，不能走浮点归一化路径，
        /// 否则 3/255 会被截断成 0，导致所有顶点绑定到 0 号骨、蒙皮完全失效）。
        /// </summary>
        private static int ReadIndex(ReadOnlySpan<byte> data, int index, int componentType)
        {
            int size = componentByteSize(componentType);
            int offset = index * size;
            if (offset + size > data.Length) return 0;
            switch (componentType)
            {
                case ComponentUnsignedByte: return data[offset];
                case ComponentByte: return (sbyte)data[offset];
                case ComponentUnsignedShort: return BitConverter.ToUInt16(data.Slice(offset, 2).ToArray(), 0);
                case ComponentShort: return BitConverter.ToInt16(data.Slice(offset, 2).ToArray(), 0);
                case ComponentUnsignedInt: return (int)BitConverter.ToUInt32(data.Slice(offset, 4).ToArray(), 0);
                case ComponentFloat: return (int)BitConverter.ToSingle(data.Slice(offset, 4).ToArray(), 0);
                default: return 0;
            }
        }

        private static float ReadFloat(ReadOnlySpan<byte> data, int index, int componentType, int components)
        {
            int offset = index * componentByteSize(componentType);
            if (offset + componentByteSize(componentType) > data.Length) return 0f;
            switch (componentType)
            {
                case ComponentFloat:
                    return BitConverter.ToSingle(data.Slice(offset, 4).ToArray(), 0);
                case ComponentUnsignedByte:
                    return data[offset] / 255f;
                case ComponentByte:
                    return (sbyte)data[offset] / 127f;
                case ComponentUnsignedShort:
                    return BitConverter.ToUInt16(data.Slice(offset, 2).ToArray(), 0) / 65535f;
                case ComponentShort:
                    return BitConverter.ToInt16(data.Slice(offset, 2).ToArray(), 0) / 32767f;
                case ComponentUnsignedInt:
                    return BitConverter.ToUInt32(data.Slice(offset, 4).ToArray(), 0);
                default:
                    return 0f;
            }
        }

        private static int componentByteSize(int componentType)
        {
            switch (componentType)
            {
                case ComponentByte:
                case ComponentUnsignedByte: return 1;
                case ComponentShort:
                case ComponentUnsignedShort: return 2;
                case ComponentUnsignedInt:
                case ComponentFloat: return 4;
                default: return 4;
            }
        }

        /// <summary>读取贴图（返回 null 表示无贴图）。贴图按 index 缓存共享。</summary>
        public Texture2D ReadTexture(int textureIndex)
        {
            if (textureIndex < 0 || textureIndex >= textures.Length) return null;
            if (textureCache.TryGetValue(textureIndex, out Texture2D cached)) return cached;
            JsonValue texture = textures[textureIndex];
            int imageIndex = texture.Has("source") ? texture["source"].AsInt(-1) : -1;
            if (imageIndex < 0 && texture["extensions"].Has("KHR_texture_basisu")) imageIndex = -1;
            if (imageIndex < 0 || imageIndex >= images.Length) return null;

            JsonValue image = images[imageIndex];
            byte[] bytes = null;
            if (image.Has("bufferView"))
            {
                int view = image["bufferView"].AsInt(-1);
                if (view >= 0 && view < bufferViews.Length && binary != null)
                {
                    int offset = bufferViews[view]["byteOffset"].AsInt();
                    int length = bufferViews[view]["byteLength"].AsInt();
                    if (offset >= 0 && offset + length <= binary.Length) bytes = Slice(binary, offset, length);
                }
            }
            else if (image.Has("uri") && !image["uri"].IsNull)
            {
                string uri = image["uri"].AsString();
                if (!uri.StartsWith("data:", StringComparison.Ordinal))
                {
                    MikuModPlugin.Log?.LogWarning("External image URIs are not supported: " + uri);
                }
            }
            if (bytes == null) return null;

            Texture2D texture2D = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: true, linear: false)
            {
                name = "MikuMod texture " + textureIndex,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };
            if (!texture2D.LoadImage(bytes, markNonReadable: false))
            {
                MikuModPlugin.Log?.LogWarning($"Failed to decode texture {textureIndex} ({bytes.Length} bytes)");
                UnityEngine.Object.Destroy(texture2D);
                return null;
            }
            ApplySampler(textureIndex, texture2D);
            textureCache[textureIndex] = texture2D;
            return texture2D;
        }

        private void ApplySampler(int textureIndex, Texture2D texture2D)
        {
            JsonValue texture = textures[textureIndex];
            if (!texture.Has("sampler")) return;
            int samplerIndex = texture["sampler"].AsInt(-1);
            if (samplerIndex < 0 || samplerIndex >= samplers.Length) return;
            int wrap = samplers[samplerIndex]["wrapS"].AsInt(10497);
            texture2D.wrapMode = wrap == 33071 ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
        }
    }
}