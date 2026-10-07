using BepInEx.Configuration;

namespace DustlineMikuMod
{
    /// <summary>Mod 配置（BepInEx.cfg），全部为静态字段，便于在 Harmony 补丁与组件中直接读取。</summary>
    internal static class MikuConfig
    {
        // 模型替换
        public static bool ModelReplacementEnabled = true;
        public static float ModelScale = 1.30f;
        public static float ModelYawOffset = 180f;
        public static float StaticModelHeight = 1.72f;
    public static float StaticModelHeightFactor = 1.0f;
        public static float PelvisRestHeight = 0.95f;

        // 第三人称
        public static bool ThirdPersonEnabled = true;
        public static string ThirdPersonKey = "V";
        public static float ThirdPersonDistance = 3.00f;
        public static float ThirdPersonHeight = 0.22f;
        public static float ThirdPersonShoulder = 0.35f;
        public static float ThirdPersonMinDistance = 0.45f;

        // 受击音效
        public static bool HitSoundEnabled = true;
        public static string HitSoundFolder = "hit_female";
        public static float HitSoundVolume = 1.0f;

        public static void Initialize(ConfigFile config)
        {
            ModelReplacementEnabled = config.Bind("Model", "Enabled", true,
                "是否用Miku 模型替换 T/CT 身体模型").Value;
            ModelScale = config.Bind("Model", "Scale", 1.30f,
                "模型整体缩放（1.30 约等于原角色身高比例）").Value;
            ModelYawOffset = config.Bind("Model", "YawOffset", 180f,
                "模型朝向补偿（度）").Value;
            StaticModelHeight = config.Bind("Model", "StaticHeight", 1.72f,
                "静态模型（CT）自动缩放后的目标身高（米）").Value;
            StaticModelHeightFactor = config.Bind("Model", "StaticHeightFactor", 1.0f,
                "静态模型（CT）高度跟随骨盆的比例").Value;
            PelvisRestHeight = config.Bind("Model", "PelvisRestHeight", 0.95f,
                "骨盆静置高度（米），用于缺少骨骼时的兜底").Value;

            ThirdPersonEnabled = config.Bind("ThirdPerson", "Enabled", true,
                "启用 V 键第一/第三人称切换").Value;
            ThirdPersonKey = config.Bind("ThirdPerson", "ToggleKey", "V",
                "切换第一/第三人称的按键").Value;
            ThirdPersonDistance = config.Bind("ThirdPerson", "Distance", 3.00f,
                "第三人称相机距离").Value;
            ThirdPersonHeight = config.Bind("ThirdPerson", "Height", 0.22f,
                "第三人称相机抬高").Value;
            ThirdPersonShoulder = config.Bind("ThirdPerson", "ShoulderOffset", 0.35f,
                "第三人称相机右肩偏移").Value;
            ThirdPersonMinDistance = config.Bind("ThirdPerson", "MinDistance", 0.45f,
                "第三人称相机最近距离（贴墙时）").Value;

            HitSoundEnabled = config.Bind("HitSound", "Enabled", true,
                "用自定义受击音效替换原版命中音效").Value;
            HitSoundFolder = config.Bind("HitSound", "Folder", "hit_female",
                "受击音效目录（位于插件 assets 下）").Value;
            HitSoundVolume = config.Bind("HitSound", "Volume", 1.0f,
                "受击音效音量倍率").Value;
        }
    }
}