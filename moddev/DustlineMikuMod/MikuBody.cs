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
            public Quaternion LocalOffset;
            public Quaternion WorldOffset;
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
            ("arm_lower_L", "lower_arm.l", MirrorMode.RotationOnly),
            ("hand_L",      "hand.l",      MirrorMode.WorldPose),
            ("leg_upper_L", "upper_leg.l", MirrorMode.RotationOnly),
            ("leg_lower_L", "lower_leg.l", MirrorMode.RotationOnly),
            ("ankle_L",     "foot.l",      MirrorMode.WorldPose),
            ("ball_L",      "toes.l",      MirrorMode.WorldPose),
            ("clavicle_R",  "shoulder.r",  MirrorMode.RotationOnly),
            ("arm_upper_R", "upper_arm.r", MirrorMode.RotationOnly),
            ("arm_lower_R", "lower_arm.r", MirrorMode.RotationOnly),
            ("hand_R",      "hand.r",      MirrorMode.WorldPose),
            ("leg_upper_R", "upper_leg.r", MirrorMode.RotationOnly),
            ("leg_lower_R", "lower_leg.r", MirrorMode.RotationOnly),
            ("ankle_R",     "foot.r",      MirrorMode.WorldPose),
            ("ball_R",      "toes.r",      MirrorMode.WorldPose),
        };

        private SourceRig bodyRig;
        private GameObject modelInstance;
        private Transform modelRoot;
        private Transform pelvisBone;
        private BonePair[] pairs = Array.Empty<BonePair>();
        private bool staticModel;
        private bool initialized;
        private Renderer[] hiddenRenderers = Array.Empty<Renderer>();

        public static bool IsReplacedRig(SourceRig rig)
        {
            return rig != null && ReplacedRigs.Contains(rig.GetInstanceID());
        }

        /// <summary>按需加载并缓存模型模板（整局只解析/构建一次，之后每个角色实例化副本）。</summary>
        private static ModelTemplate GetTemplate(string fileName)
        {
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
            string file = team == 1 ? TModelFile : CTModelFile;
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
                BuildBonePairs(template.Build);
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

        private void BuildBonePairs(MikuModelFactory.BuildResult build)
        {
            List<BonePair> list = new List<BonePair>(BoneMap.Length);
            foreach ((string gameBone, string mikuKey, MirrorMode mode) in BoneMap)
            {
                Transform game = FindBone(gameBone);
                Transform miku = null;
                if (build.NodesByKey != null && build.NodesByKey.TryGetValue(mikuKey, out Transform found)) miku = found;
                if (game == null || miku == null) continue;
                BonePair pair = new BonePair
                {
                    Game = game,
                    Miku = miku,
                    Mode = mode,
                    LocalOffset = Quaternion.Inverse(game.localRotation) * miku.localRotation,
                    WorldOffset = Quaternion.Inverse(game.rotation) * miku.rotation
                };
                list.Add(pair);
            }
            pairs = list.ToArray();
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
            for (int i = 0; i < pairs.Length; i++)
            {
                BonePair pair = pairs[i];
                if (pair.Game == null || pair.Miku == null) continue;
                if (pair.Mode == MirrorMode.WorldPose)
                {
                    pair.Miku.SetPositionAndRotation(pair.Game.position, pair.Game.rotation * pair.WorldOffset);
                }
                else
                {
                    pair.Miku.localRotation = pair.Game.localRotation * pair.LocalOffset;
                }
            }
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