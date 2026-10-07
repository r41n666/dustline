using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Dustline;

namespace DustlineMikuMod
{
    /// <summary>
    /// 受击音效替换：加载 assets/hit_female 下全部 wav，
    /// 每次命中事件触发时随机挑一个播放（替换原版身体/护甲/头盔/爆头与刀击音效）。
    /// 实现方式：Harmony 前置 GameAudio.Clip(path) —— 该方法是所有事件音效播放的必经之路。
    /// </summary>
    internal static class HitSounds
    {
        /// <summary>需要替换的原版事件名（命中人体的全部受击音）。</summary>
        private static readonly string[] HitEvents =
        {
            "Flesh.BulletImpact",   // 身体
            "Dustline.KevlarHit",   // 防弹衣
            "Dustline.Headshot",    // 爆头
            "Dustline.HelmetHit",   // 头盔
            "Weapon_Knife.Hit",     // 刀命中（轻）
            "Weapon_Knife.Stab",    // 刀命中（重）
        };

        private static readonly HashSet<string> ReplacePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static AudioClip[] clips = Array.Empty<AudioClip>();
        private static readonly System.Random Random = new System.Random(Environment.TickCount);
        private static int lastIndex = -1;
        private static bool volumeApplied;

        public static int LoadedClipCount => clips.Length;
        public static bool Ready => clips.Length > 0 && ReplacePaths.Count > 0;
        public static int ReplacedCount { get; private set; }

        /// <summary>记录一次命中音效替换（每5 次输出一次日志，便于实测确认是否生效）。</summary>
        public static void NoteReplaced(string path)
        {
            ReplacedCount++;
            if (ReplacedCount % 5 == 1 || ReplacedCount <= 3)
            {
                MikuModPlugin.Log?.LogInfo($"Hit sound replaced x{ReplacedCount} (e.g. {path})");
            }
        }

        public static void Initialize()
        {
            if (clips.Length > 0) return;
            string folder = Path.Combine(MikuModPlugin.PluginDirectory ?? string.Empty, "assets", MikuConfig.HitSoundFolder);
            if (!Directory.Exists(folder))
            {
                MikuModPlugin.Log?.LogError("Hit sound folder missing: " + folder);
                return;
            }
            List<AudioClip> loaded = new List<AudioClip>();
            foreach (string file in Directory.GetFiles(folder, "*.wav", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    AudioClip clip = WavLoader.Load(file);
                    if (clip != null) loaded.Add(clip);
                }
                catch (Exception e)
                {
                    MikuModPlugin.Log?.LogWarning($"Failed to load {Path.GetFileName(file)}: {e.Message}");
                }
            }
            clips = loaded.ToArray();
            MikuModPlugin.Log?.LogInfo($"Loaded {clips.Length} hit sounds from {folder}");
            CollectReplacePaths();
        }

        /// <summary>从游戏自带的 sound_events 资源里取出这些事件对应的音频路径。</summary>
        private static void CollectReplacePaths()
        {
            try
            {
                TextAsset asset = Resources.Load<TextAsset>("SourceContent/sound_events");
                if (asset == null)
                {
                    MikuModPlugin.Log?.LogWarning("sound_events resource not found; hit sound replacement disabled");
                    return;
                }
                JsonValue root = JsonValue.Parse(asset.text);
                JsonValue events = root["events"];
                int matched = 0;
                foreach (JsonValue entry in events.ArrayValue ?? new List<JsonValue>())
                {
                    string name = entry["name"].AsString(string.Empty);
                    bool isHit = false;
                    foreach (string candidate in HitEvents)
                    {
                        if (string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase)) { isHit = true; break; }
                    }
                    if (!isHit) continue;
                    matched++;
                    JsonValue list = entry["clips"];
                    foreach (JsonValue clip in list.ArrayValue ?? new List<JsonValue>())
                    {
                        string path = clip.AsString(string.Empty);
                        if (path.Length > 0) ReplacePaths.Add(path);
                    }
                }
                Resources.UnloadAsset(asset);
                MikuModPlugin.Log?.LogInfo($"Hit event paths: {ReplacePaths.Count} clip path(s) from {matched} event(s)");
            }
            catch (Exception e)
            {
                MikuModPlugin.Log?.LogError("Failed to collect hit sound paths: " + e);
            }
        }

        public static bool ShouldReplace(string path)
        {
            return !string.IsNullOrEmpty(path) && ReplacePaths.Contains(path);
        }

        /// <summary>随机取一个受击音效（避免连续重复同一条）。</summary>
        public static AudioClip Next()
        {
            if (clips.Length == 0) return null;
            if (clips.Length == 1) return clips[0];
            int index;
            lock (Random)
            {
                do
                {
                    index = Random.Next(clips.Length);
                } while (index == lastIndex);
                lastIndex = index;
            }
            return clips[index];
        }

        /// <summary>把命中事件的音量按配置整体缩放（只做一次）。</summary>
        public static void ApplyVolumeScale(GameAudio audio)
        {
            if (volumeApplied || audio == null || MikuConfig.HitSoundVolume <= 0f) return;
            try
            {
                System.Reflection.FieldInfo field = typeof(GameAudio).GetField("events",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (field?.GetValue(audio) is System.Collections.IDictionary dictionary)
                {
                    foreach (System.Collections.DictionaryEntry entry in dictionary)
                    {
                        if (!(entry.Key is string key)) continue;
                        bool isHit = false;
                        foreach (string candidate in HitEvents)
                        {
                            if (string.Equals(key, candidate, StringComparison.OrdinalIgnoreCase)) { isHit = true; break; }
                        }
                        if (!isHit) continue;
                        System.Reflection.FieldInfo gainField = entry.Value.GetType().GetField("gain");
                        if (gainField != null && gainField.FieldType == typeof(float))
                        {
                            float gain = (float)gainField.GetValue(entry.Value);
                            gainField.SetValue(entry.Value, gain * MikuConfig.HitSoundVolume);
                        }
                    }
                }
                volumeApplied = true;
            }
            catch (Exception e)
            {
                MikuModPlugin.Log?.LogWarning("Hit sound volume scaling failed: " + e.Message);
            }
        }
    }

    /// <summary>极简 PCM WAV 解码器（支持 8/16/24/32 位整数与 32 位浮点）。</summary>
    internal static class WavLoader
    {
        public static AudioClip Load(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length < 44) throw new InvalidDataException("WAV too small");

            int channels = 2;
            int sampleRate = 44100;
            int bitsPerSample = 16;
            int format = 1;
            int offset = 12;
            byte[] data = null;

            while (offset + 8 <= bytes.Length)
            {
                string chunkId = System.Text.Encoding.ASCII.GetString(bytes, offset, 4);
                int chunkSize = BitConverter.ToInt32(bytes, offset + 4);
                int payload = offset + 8;
                if (chunkId == "fmt " && payload + 16 <= bytes.Length)
                {
                    format = BitConverter.ToInt16(bytes, payload);
                    channels = BitConverter.ToInt16(bytes, payload + 2);
                    sampleRate = BitConverter.ToInt32(bytes, payload + 4);
                    bitsPerSample = BitConverter.ToInt16(bytes, payload + 14);
                }
                else if (chunkId == "data")
                {
                    int length = Math.Min(chunkSize, bytes.Length - payload);
                    data = new byte[length];
                    Buffer.BlockCopy(bytes, payload, data, 0, length);
                }
                offset = payload + chunkSize + (chunkSize % 2);
            }

            if (data == null) throw new InvalidDataException("WAV without data chunk");
            channels = Math.Max(1, channels);
            int bytesPerSample = bitsPerSample / 8;
            if (bytesPerSample <= 0) throw new InvalidDataException("Unsupported bit depth");
            int frameCount = data.Length / (bytesPerSample * channels);
            float[] samples = new float[frameCount * channels];
            int index = 0;
            for (int frame = 0; frame < frameCount; frame++)
            {
                for (int channel = 0; channel < channels; channel++)
                {
                    int p = (frame * channels + channel) * bytesPerSample;
                    float value;
                    if (format == 3 && bitsPerSample == 32)
                    {
                        value = BitConverter.ToSingle(data, p);
                    }
                    else if (bitsPerSample == 8)
                    {
                        value = (sbyte)data[p] / 128f;
                    }
                    else if (bitsPerSample == 16)
                    {
                        value = BitConverter.ToInt16(data, p) / 32768f;
                    }
                    else if (bitsPerSample == 24)
                    {
                        int raw = data[p] | (data[p + 1] << 8) | (data[p + 2] << 16);
                        if ((raw & 0x800000) != 0) raw |= ~0xFFFFFF;
                        value = raw / 8388608f;
                    }
                    else if (bitsPerSample == 32)
                    {
                        value = BitConverter.ToInt32(data, p) / 2147483648f;
                    }
                    else
                    {
                        value = 0f;
                    }
                    samples[index++] = Mathf.Clamp(value, -1f, 1f);
                }
            }

            AudioClip clip = AudioClip.Create(Path.GetFileNameWithoutExtension(path), frameCount, channels, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}