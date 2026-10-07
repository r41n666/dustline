using System;
using HarmonyLib;
using UnityEngine;
using Dustline;
using Dustline.Core;

namespace DustlineMikuMod
{
    /// <summary>
    /// 全部 Harmony 补丁。每一处都锚定在反编译确认过的游戏内部 API 上：
    ///   SceneView.CreateActor            —— 角色创建入口（阵营在这里决定原版模型 t_leet / ct_idf）
    ///   SourceRig.get_IsVisible          —— 原版把不可见角色的姿态节流到 10Hz，替换模型后需要放行
    ///   SceneView.UpdatePlayers          —— 第一人称下本地 actor 被 SetActive(false)，第三人称需补回并驱动姿态
    ///   WeaponView.Animate / Show        —— 第一人称视图模型（手臂+武器），第三人称需要隐藏
    ///   GameAudio.Clip                   —— 所有事件音效播放的必经之路，受击音效在此替换
    /// </summary>
    internal static class Patches
    {
        [HarmonyPatch(typeof(SceneView), "CreateActor")]
        private static class CreateActorPatch
        {
            private static void Postfix(SceneView __instance, int id)
            {
                if (!MikuConfig.ModelReplacementEnabled) return;
                try
                {
                    Transform actor = __instance.Actor(id);
                    if (actor == null) return;
                    if (actor.GetComponent<MikuBody>() != null) return;
                    actor.gameObject.AddComponent<MikuBody>();
                }
                catch (Exception e)
                {
                    MikuModPlugin.Log?.LogError("CreateActor patch failed: " + e);
                }
            }
        }

        [HarmonyPatch(typeof(SourceRig), "get_IsVisible")]
        private static class IsVisiblePatch
        {
            private static void Postfix(SourceRig __instance, ref bool __result)
            {
                if (!__result && MikuBody.IsReplacedRig(__instance)) __result = true;
            }
        }

        [HarmonyPatch(typeof(SceneView), "UpdatePlayers")]
        private static class UpdatePlayersPatch
        {


            private static void Postfix(SceneView __instance, int local)
            {
                if (!ThirdPersonController.Active) return;
                Game game = Game.Instance;
                if (game == null) return;
                Player pose = game.ViewPose;
                if (pose == null) return;
                try
                {
                    Transform actor = __instance.Actor(local);
                    if (actor == null) return;
                    // 第一人称下游戏会把本地 actor SetActive(false)，第三人称需要恢复显示
                    if (!actor.gameObject.activeSelf) actor.gameObject.SetActive(true);
                    CharacterModel model = actor.GetComponent<CharacterModel>();
                    if (model != null) model.Pose(pose, Time.unscaledDeltaTime);
                }
                catch (Exception e)
                {
                    MikuModPlugin.Log?.LogError("UpdatePlayers patch failed: " + e);
                }
            }
        }

        [HarmonyPatch(typeof(WeaponView), "Animate")]
        private static class ViewmodelAnimatePatch
        {
            private static void Postfix(WeaponView __instance)
            {
                if (!ThirdPersonController.Active) return;
                HideViewmodel(__instance);
            }
        }

        [HarmonyPatch(typeof(WeaponView), "Show")]
        private static class ViewmodelShowPatch
        {
            private static void Postfix(WeaponView __instance, bool visible)
            {
                if (!ThirdPersonController.Active || !visible) return;
                HideViewmodel(__instance);
            }
        }

        private static readonly AccessTools.FieldRef<WeaponView, Transform> PivotRef =
            AccessTools.FieldRefAccess<WeaponView, Transform>("pivot");

        private static void HideViewmodel(WeaponView view)
        {
            try
            {
                Transform pivot = PivotRef(view);
                if (pivot != null && pivot.gameObject.activeSelf) pivot.gameObject.SetActive(false);
            }
            catch (Exception e)
            {
                MikuModPlugin.Log?.LogWarning("Hide viewmodel failed: " + e.Message);
            }
        }

        [HarmonyPatch(typeof(GameAudio), "Clip")]
        private static class HitSoundPatch
        {
            private static bool Prefix(GameAudio __instance, string path, ref AudioClip __result)
            {
                if (!MikuConfig.HitSoundEnabled) return true;
                if (!HitSounds.Ready) HitSounds.Initialize();
                if (!HitSounds.ShouldReplace(path)) return true;
                HitSounds.ApplyVolumeScale(__instance);
                AudioClip clip = HitSounds.Next();
                if (clip == null) return true;
                HitSounds.NoteReplaced(path);
                __result = clip;
                return false;
            }
        }
    }

    /// <summary>
    /// 控制台打开时屏蔽游戏按键输入（游戏用的是内部 UnityControlSource，按类型名 patch）。
    /// </summary>
    internal static class InputSuppression
    {
        public static void Apply(Harmony harmony)
        {
            System.Type type = AccessTools.TypeByName("Dustline.UnityControlSource");
            if (type == null)
            {
                MikuModPlugin.Log?.LogWarning("UnityControlSource not found; console input suppression disabled.");
                return;
            }
            harmony.Patch(AccessTools.Method(type, "Held"),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(InputSuppression), nameof(HeldPrefix))));
            harmony.Patch(AccessTools.Method(type, "Pressed"),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(InputSuppression), nameof(PressedPrefix))));
            harmony.Patch(AccessTools.Method(type, "get_Wheel"),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(InputSuppression), nameof(WheelPrefix))));
            MikuModPlugin.Log?.LogInfo("Console input suppression installed.");
        }

        private static bool HeldPrefix(ref bool __result)
        {
            if (!ConsoleWindow.SuppressGameInput) return true;
            __result = false;
            return false;
        }

        private static bool PressedPrefix(ref bool __result)
        {
            if (!ConsoleWindow.SuppressGameInput) return true;
            __result = false;
            return false;
        }

        private static bool WheelPrefix(ref float __result)
        {
            if (!ConsoleWindow.SuppressGameInput) return true;
            __result = 0f;
            return false;
        }
    }
}