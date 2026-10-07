using System;
using System.Collections;
using System.IO;
using System.Reflection;
using BepInEx;
using Dustline;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace DustlineMikuMod
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class MikuModPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "dustline.mikumod";
        public const string PluginName = "Dustline Miku Mod";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log;
        internal static string PluginDirectory;
        internal static ConfigFile ConfigFile;

        private Harmony harmony;

        private void Awake()
        {
            Log = Logger;
            ConfigFile = Config;
            PluginDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            try
            {
                MikuConfig.Initialize(Config);
            }
            catch (Exception e)
            {
                Log.LogError("Config init failed: " + e);
            }

            Log.LogInfo($"Dustline Miku Mod v{PluginVersion} loaded from {PluginDirectory}");
            Log.LogInfo($"  model scale={MikuConfig.ModelScale} yaw={MikuConfig.ModelYawOffset} " +
                $"thirdPerson={MikuConfig.ThirdPersonEnabled} key={MikuConfig.ThirdPersonKey} " +
                $"hitSound={MikuConfig.HitSoundEnabled}");

            try
            {
                harmony = new Harmony(PluginGuid);
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                Log.LogInfo("Harmony patches applied.");
            }
            catch (Exception e)
            {
                Log.LogError("Patch failure: " + e);
            }

            try
            {
                ThirdPersonController.Instance.StartCoroutine(DelayedInitialize());
            }
            catch (Exception e)
            {
                Log.LogError("Third person controller init failed: " + e);
            }
        }

        private IEnumerator DelayedInitialize()
        {
            // 等待游戏自身资源（sound_events）可用后再准备受击音效
            yield return new WaitForSecondsRealtime(6f);
            while (Game.Instance != null && !Game.Instance.Playing)
            {
                yield return new WaitForSecondsRealtime(2f);
            }
            yield return new WaitForSecondsRealtime(1f);
            try
            {
                HitSounds.Initialize();
                Log.LogInfo($"Hit sounds ready: {HitSounds.LoadedClipCount} clip(s)");
            }
            catch (Exception e)
            {
                Log.LogError("Hit sound init failed: " + e);
            }
        }

        private void OnDestroy()
        {
            Log?.LogInfo("Dustline Miku Mod unloaded.");
        }
    }
}