using System;
using System.Collections.Generic;
using UnityEngine;
using Dustline;

namespace DustlineMikuMod
{
    /// <summary>
    /// 挂在每个角色（SceneView 创建的 Operator）上，把 CS:GO 原版身体模型替换为 Miku 模型。
    /// 关键点：游戏使用自研骨骼动画系统（SourceRig 直接驱动骨骼 Transform），
    /// 因此保留原骨架运行全部动画（移动/瞄准/开火/换弹/死亡物理），
    /// 每帧把游戏骨骼的姿态映射到 Miku 骨骼上，实现"换皮不换动画"。
    /// </summary>
    internal sealed class MikuBody : MonoBehaviour
    {
        private enum MirrorMode
        {
            RotationOnly,   // 只复制局部旋转：保留 Miku 自身肢体长度
            WorldPose       // 复制世界位置+旋转：用于手（武器对齐）与脚（贴地/尸体物理）
        }

        private struct BonePair
        {
            public Transform Game;
            public Transform Miku;
            public MirrorMode Mode;
            public Quaternion RestOffset;   // inverse(游戏静置世界旋转) × 初音静置世界旋转
        }

        private sealed class ModelTemplate
        {
            public GameObject Template;
            public MikuModelFactory.BuildResult Build;
            public bool Loaded;
        }

        // T 方：带骨骼的猫娘初音；CT 方：静态模型
        private const string TModelFile = "cat_hatsune_miku.glb";
        private const string CTModelFile = "miku.glb";

        private static readonly Dictionary<string, ModelTemplate> Templates = new Dictionary<string, ModelTemplate>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<int> ReplacedRigs = new HashSet<int>();

        /// <summary>游戏骨骼名 → Miku 关节键（已归一化）。</summary>
        private static readonly (string gameBone, string mikuKey, MirrorMode mode)[] BoneMap =
        {
            ("pelvis",      "hips",        MirrorMode.WorldPose),
            ("spine_0",     "spine",       MirrorMode.RotationOnly),
            ("spine_2",     "chest",       MirrorMode.RotationOnly),
            ("neck_0",      "neck",        MirrorMode.RotationOnly),
            ("head_0",      "head",        MirrorMode.RotationOnly),
            ("clavicle_L",  "shoulder.l",  MirrorMode.RotationOnly),
            ("arm_upper_L", "upper_arm.l", MirrorMode.RotationOnly),
            ("arm_lower_L", "lower_arm.l", MirrorMode.WorldPose),
            ("hand_L",      "hand.l",      MirrorMode.WorldPose),
            ("leg_upper_L", "upper_leg.l", MirrorMode.RotationOnly),
            ("leg_lower_L", "lower_leg.l", MirrorMode.RotationOnly),
            ("ankle_L",     "foot.l",      MirrorMode.RotationOnly),
            ("ball_L",      "toes.l",      MirrorMode.RotationOnly),
            ("clavicle_R",  "shoulder.r",  MirrorMode.RotationOnly),
            ("arm_upper_R", "upper_arm.r", MirrorMode.RotationOnly),
            ("arm_lower_R", "lower_arm.r", MirrorMode.WorldPose),
            ("hand_R",      "hand.r",      MirrorMode.WorldPose),
            ("leg_upper_R", "upper_leg.r", MirrorMode.RotationOnly),
            ("leg_lower_R", "lower_leg.r", MirrorMode.RotationOnly),
            ("ankle_R",     "foot.r",      MirrorMode.RotationOnly),
            ("ball_R",      "toes.r",      MirrorMode.RotationOnly),
        };

        private SourceRig bodyRig;
        private GameObject modelInstance;
        private Transform modelRoot;
        private Transform pelvisBone;
        private Transform mikuHead;
        private bool heightAligned;
        private bool offsetsCalibrated;
        private BonePair[] pairs = Array.Empty<BonePair>();
        private bool staticModel;
        private bool initialized;
        private Renderer[] hiddenRenderers = Array.Empty<Renderer>();

        public static bool IsReplacedRig(SourceRig rig)
        {
            return rig != null && ReplacedRigs.Contains(rig.GetInstanceID());
        }

        /// <summary>按需加载并缓存模型模板（整局只解析/构建一次，之后每个角色实例化副本）。</summary>
        private static ModelTemplate GetTemplate(string fileName)        {
            if (Templates.TryGetValue(fileName, out ModelTemplate existing)) return existing;
            ModelTemplate template = new ModelTemplate();
            try
            {
                string assetsDir = System.IO.Path.Combine(MikuModPlugin.PluginDirectory ?? string.Empty, "assets");
                string path = System.IO.Path.Combine(assetsDir, fileName);
                if (!System.IO.File.Exists(path))
                {
                    MikuModPlugin.Log?.LogError("Model file missing: " + path);
                    Templates[fileName] = template;
                    return template;
                }
                System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                Gltf gltf = Gltf.LoadFromFile(path);
                MikuModelFactory.BuildResult build = MikuModelFactory.Build(gltf, fileName, MikuConfig.ModelScale, MikuConfig.ModelYawOffset);
                build.Root.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(build.Root);
                template.Build = build;
                template.Template = build.Root;
                template.Loaded = true;
                MikuModPlugin.Log?.LogInfo($"Loaded {fileName}: nodes={gltf.Nodes.Length} skinned={build.HasSkin} " +
                    $"tris={build.TriangleCount} in {watch.ElapsedMilliseconds}ms");
            }
            catch (Exception e)
            {
                MikuModPlugin.Log?.LogError($"Failed to build model {fileName}: {e}");
                template.Loaded = false;
            }
            Templates[fileName] = template;
            return template;
        }

        private void Awake()
        {
            try
            {
                Initialize();
            }
            catch (Exception e)
            {
                MikuModPlugin.Log?.LogError("MikuBody init failed: " + e);
            }
        }

        private void Initialize()
        {
            if (initialized) return;
            initialized = true;

            bodyRig = FindBodyRig();
            if (bodyRig == null)
            {
                MikuModPlugin.Log?.LogWarning("MikuBody: no player rig found on " + name);
                return;
            }

            byte team = ResolveTeam();
            string file = (team == 1 || MikuConfig.UseCatForBothTeams) ? TModelFile : CTModelFile;
            ModelTemplate template = GetTemplate(file);
            if (template == null || !template.Loaded)
            {
                MikuModPlugin.Log?.LogWarning("MikuBody: model not available: " + file);
                return;
            }

            modelInstance = UnityEngine.Object.Instantiate(template.Template, transform, false);
            modelInstance.name = template.Template.name;
            modelInstance.SetActive(true);
            // 静态模型通过 FollowPivot 做整体位移跟随（蹲伏时一起下沉），避免覆盖自动定标偏移
            Transform pivot = modelInstance.transform.Find("FollowPivot");
            modelRoot = pivot != null ? pivot : modelInstance.transform;

            staticModel = !template.Build.HasSkin;
            pelvisBone = FindBone("pelvis");

            if (!staticModel)
            {
                // 注意：必须索引【实例】里的骨骼。模板是所有角色共用的，
                // 引用模板骨骼会导致每个实例都停在 bind pose（T-pose）。
                IndexInstanceNodes();
                BuildBonePairs();
                instanceNodes.TryGetValue("head", out mikuHead);
            }

            HideOriginalBody();
            ReplacedRigs.Add(bodyRig.GetInstanceID());

            MikuModPlugin.Log?.LogInfo($"MikuBody attached: team={team} model={file} skinned={!staticModel} " +
                $"pairs={pairs.Length} tris={template.Build.TriangleCount}");
        }

        private static SourceRig FindBodyRigOn(GameObject actor)
        {
            SourceRig[] rigs = actor.GetComponentsInChildren<SourceRig>(true);
            foreach (SourceRig rig in rigs)
            {
                if (rig.Model == "t_leet" || rig.Model == "ct_idf") return rig;
            }
            return null;
        }

        private SourceRig FindBodyRig() => FindBodyRigOn(gameObject);

        private byte ResolveTeam()
        {
            // 游戏内部约定：SourceRig.Model == "t_leet" 为 T 方，"ct_idf" 为 CT 方
            // （与 SourceCharacter.Build 中 side==0 ? "ct_idf" : "t_leet" 一致）
            if (bodyRig != null)
            {
                if (string.Equals(bodyRig.Model, "t_leet", StringComparison.OrdinalIgnoreCase)) return 1;
                if (string.Equals(bodyRig.Model, "ct_idf", StringComparison.OrdinalIgnoreCase)) return 0;
            }
            SourceCharacter character = GetComponent<SourceCharacter>();
            if (character != null)
            {
                try
                {
                    System.Reflection.FieldInfo field = typeof(SourceCharacter).GetField("team",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    if (field != null && field.GetValue(character) is byte b) return b;
                }
                catch (Exception e)
                {
                    MikuModPlugin.Log?.LogWarning("ResolveTeam reflection failed: " + e.Message);
                }
            }
            return 1;
        }

        private Transform FindBone(string boneName)
        {
            if (bodyRig != null && bodyRig.Named != null && bodyRig.Named.TryGetValue(boneName, out Transform t)) return t;
            return null;
        }

        private readonly Dictionary<string, Transform> instanceNodes = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<Transform>> instanceCandidates = new Dictionary<string, List<Transform>>(StringComparer.OrdinalIgnoreCase);

        private void IndexInstanceNodes()
        {
            instanceNodes.Clear();
            instanceCandidates.Clear();
            foreach (Transform t in modelInstance.GetComponentsInChildren<Transform>(true))
            {
                string key = MikuModelFactory.NormalizeBoneKey(t.name);
                if (key.Length == 0) continue;
                if (!instanceCandidates.TryGetValue(key, out List<Transform> list))
                {
                    list = new List<Transform>();
                    instanceCandidates[key] = list;
                }
                list.Add(t);
            }
            // 同一键可能有多根骨（模型里 lower_arm 同时有肘部与腕部副本）。
            // 选骨规则：优先「祖先链包含已解析的上游骨」的那一根，保证骨链连续、肘部能弯。
            foreach ((string gameBone, string mikuKey, MirrorMode mode) in BoneMap)
            {
                if (!instanceCandidates.TryGetValue(mikuKey, out List<Transform> candidates) || candidates.Count == 0) continue;
                instanceNodes[mikuKey] = PickBone(candidates, mikuKey);
            }
        }

        /// <summary>BoneMap 里紧邻的上游键（用于骨链连续性判定）。</summary>
        private static string ParentKeyOf(string mikuKey)
        {
            switch (mikuKey)
            {
                case "spine": return "hips";
                case "chest": return "spine";
                case "neck": return "chest";
                case "head": return "neck";
                case "shoulder.l": return "chest";
                case "upper_arm.l": return "shoulder.l";
                case "lower_arm.l": return "upper_arm.l";
                case "hand.l": return "lower_arm.l";
                case "upper_leg.l": return "hips";
                case "lower_leg.l": return "upper_leg.l";
                case "foot.l": return "lower_leg.l";
                case "toes.l": return "foot.l";
                case "shoulder.r": return "chest";
                case "upper_arm.r": return "shoulder.r";
                case "lower_arm.r": return "upper_arm.r";
                case "hand.r": return "lower_arm.r";
                case "upper_leg.r": return "hips";
                case "lower_leg.r": return "upper_leg.r";
                case "foot.r": return "lower_leg.r";
                case "toes.r": return "foot.r";
                default: return null;
            }
        }

        private Transform PickBone(List<Transform> candidates, string mikuKey)
        {
            if (candidates.Count == 1) return candidates[0];
            string parentKey = ParentKeyOf(mikuKey);
            if (parentKey != null && instanceNodes.TryGetValue(parentKey, out Transform parentBone))
            {
                foreach (Transform candidate in candidates)
                {
                    Transform walker = candidate.parent;
                    for (int i = 0; i < 24 && walker != null; i++)
                    {
                        if (walker == parentBone) return candidate;
                        walker = walker.parent;
                    }
                }
            }
            return candidates[0];
        }

        private void BuildBonePairs()
        {
            List<BonePair> list = new List<BonePair>(BoneMap.Length);
            foreach ((string gameBone, string mikuKey, MirrorMode mode) in BoneMap)
            {
                Transform game = FindBone(gameBone);
                instanceNodes.TryGetValue(mikuKey, out Transform miku);
                if (game == null || miku == null) continue;
                // RestOffset 延迟到第一帧姿态同步时标定：
                // 这套骨架的 bind pose 是「躺姿」，在 bind 时刻标定会把站姿映射成躺姿。
                list.Add(new BonePair { Game = game, Miku = miku, Mode = mode });
            }
            // 父骨必须先写：子骨的局部旋转由父骨世界朝向推导
            list.Sort((a, b) => GameDepth(a.Game).CompareTo(GameDepth(b.Game)));
            pairs = list.ToArray();
        }

        private static int GameDepth(Transform bone)
        {
            int depth = 0;
            Transform current = bone;
            while (current != null && depth < 32)
            {
                depth++;
                current = current.parent;
            }
            return depth;
        }

        private void HideOriginalBody()
        {
            List<Renderer> hidden = new List<Renderer>();
            foreach (Renderer renderer in bodyRig.GetComponentsInChildren<Renderer>(true))
            {
                // 只隐藏身体网格，保留枪械等其它模型
                if (renderer is SkinnedMeshRenderer)
                {
                    renderer.enabled = false;
                    hidden.Add(renderer);
                }
            }
            hiddenRenderers = hidden.ToArray();
        }

        private void OnDestroy()
        {
            if (bodyRig != null) ReplacedRigs.Remove(bodyRig.GetInstanceID());
        }

        private void LateUpdate()
        {
            if (!initialized || bodyRig == null || modelRoot == null) return;
            if (staticModel)
            {
                FollowStatic();
                return;
            }
            if (!offsetsCalibrated)
            {
                offsetsCalibrated = true;
                for (int i = 0; i < pairs.Length; i++)
                {
                    if (pairs[i].Game == null || pairs[i].Miku == null) continue;
                    pairs[i].RestOffset = Quaternion.Inverse(pairs[i].Game.rotation) * pairs[i].Miku.rotation;
                }
            }
            for (int i = 0; i < pairs.Length; i++)
            {
                BonePair pair = pairs[i];
                if (pair.Game == null || pair.Miku == null) continue;
                if (pair.Mode == MirrorMode.WorldPose)
                {
                    // 位置钉住游戏骨骼；旋转同样要加静置偏移，
                    // 否则根骨（骨盆）会比其他骨头少转一个静置差，整个人向一侧倒。
                    pair.Miku.SetPositionAndRotation(pair.Game.position, pair.Game.rotation * pair.RestOffset);
                }
                else
                {
                    // 直接采用游戏骨骼的世界朝向，再换算成局部旋转。
                    // 这样不依赖两套骨架的坐标系/绑定姿势约定，避免出现 T-pose。
                    Transform parent = pair.Miku.parent;
                    Quaternion parentWorld = parent != null ? parent.rotation : modelInstance.transform.rotation;
                    Quaternion desiredWorld = pair.Game.rotation * pair.RestOffset;
                    pair.Miku.localRotation = Quaternion.Inverse(parentWorld) * desiredWorld;
                }
            }
            if (!heightAligned && MikuConfig.AutoAlignHeight)
            {
                AlignHeightToCharacter();
                heightAligned = true;
            }
        }


        /// <summary>
        /// 第三人称时兜底：确保本地角色 actor 处于激活状态。
        /// 姿态驱动只保留 SceneView.UpdatePlayers 补丁里的那一次——
        /// 重复驱动会让原版骨架停在 bind pose（表现为 T-pose）。
        /// </summary>
        private void EnsureLocalActorVisible()
        {
            if (!ThirdPersonController.Active) return;
            Game game = Game.Instance;
            if (game?.Local == null) return;
            if (!gameObject.activeSelf) gameObject.SetActive(true);
        }

        private CharacterModel characterModel;
        private SceneView sceneView;

        /// <summary>
        /// 用「模型 bind pose 包围盒高度」对齐原角色身高。
        /// 不能用头关节对齐：初音是 Q 版（头大身短），按头高对齐会把四肢拉长近一倍，
        /// 手部被钉在原角色手位时手臂会被迫伸直，看起来就是 T-pose。
        /// </summary>
        private void AlignHeightToCharacter()
        {
            Transform headBone = FindBone("head_0");
            Transform footBone = FindBone("ball_L");
            if (headBone == null || footBone == null || modelInstance == null) return;

            float characterHeight = headBone.position.y - footBone.position.y;
            Bounds combined = GetModelBounds();
            if (characterHeight < 0.5f || combined.size.y < 0.05f) return;

            float factor = characterHeight / combined.size.y;
            Transform container = modelInstance.transform;
            container.localScale = container.localScale * factor;
            MikuModPlugin.Log?.LogInfo($"Height aligned: character={characterHeight:F3}m model={combined.size.y:F3}m " +
                $"scale x{factor:F3} -> {container.localScale.y:F3}");
        }

        /// <summary>
        /// 取模型 bind pose 的网格包围盒（模型空间，未含容器缩放）。
        /// 不能用 Renderer.bounds：蒙皮更新前它是过期的（会明显偏小）。
        /// </summary>
        private Bounds GetModelBounds()
        {
            Bounds bounds = new Bounds();
            bool first = true;
            foreach (Renderer renderer in modelInstance.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = null;
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter != null) mesh = filter.sharedMesh;
                else if (renderer is SkinnedMeshRenderer skinned) mesh = skinned.sharedMesh;
                if (mesh == null || mesh.vertexCount == 0) continue;
                if (first) { bounds = mesh.bounds; first = false; }
                else bounds.Encapsulate(mesh.bounds);
            }
            return bounds;
        }

        private void FollowStatic()
        {
            // 静态模型：整体跟随角色朝向与骨盆高度（蹲伏时一起下沉）
            float pelvisHeight = MikuConfig.PelvisRestHeight;
            if (pelvisBone != null)
            {
                pelvisHeight = pelvisBone.position.y - transform.position.y;
            }
            Vector3 local = modelRoot.localPosition;
            modelRoot.localPosition = new Vector3(local.x, pelvisHeight * MikuConfig.StaticModelHeightFactor, local.z);
        }
    }
}