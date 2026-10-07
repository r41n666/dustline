using System;
using System.Collections.Generic;
using System.Text;
using Dustline;
using UnityEngine;

namespace DustlineMikuMod
{
    /// <summary>
    /// 仿 CS 的调试控制台：反引号键开关，输入行 + 历史（上下键）+ 滚动输出。
    /// 打开时用Harmony 屏蔽游戏按键输入，避免打字时角色乱跑。
    /// </summary>
    internal sealed class ConsoleWindow : MonoBehaviour
    {
        public static ConsoleWindow Instance { get; private set; }

        private const int MaxLines = 200;
        private readonly List<string> lines = new List<string>();
        private readonly List<string> history = new List<string>();
        private string input = string.Empty;
        private int historyIndex = -1;
        private bool open;
        private float scroll;
        private GUIStyle labelStyle;
        private GUIStyle inputStyle;
        private bool stylesReady;

        public static bool IsOpen => Instance != null && Instance.open;
        public static bool SuppressGameInput { get; set; }

        public static ConsoleWindow Ensure()
        {
            if (Instance != null) return Instance;
            GameObject host = new GameObject("DustlineMikuMod.Console");
            UnityEngine.Object.DontDestroyOnLoad(host);
            Instance = host.AddComponent<ConsoleWindow>();
            return Instance;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.BackQuote)) SetOpen(!open);
            if (!open) return;
            // 注意：IMGUI 文本框获得焦点时按键会被 GUI 先行消费，
            // 因此真正的回车/退出判定放在 OnGUI 里（见 HandleGuiKeys）。
            if (Input.GetKeyDown(KeyCode.Escape)) SetOpen(false);
            if (Input.GetKeyDown(KeyCode.UpArrow)) Recall(-1);
            if (Input.GetKeyDown(KeyCode.DownArrow)) Recall(1);
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) Submit();
        }

        /// <summary>在 IMGUI 事件里兜底处理回车/ESC/反引号，保证控制台一定能发送与退出。</summary>
        private void HandleGuiKeys()
        {
            Event e = Event.current;
            if (e == null || e.type != EventType.KeyDown) return;
            switch (e.keyCode)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    Submit();
                    e.Use();
                    break;
                case KeyCode.Escape:
                    SetOpen(false);
                    e.Use();
                    break;
                case KeyCode.BackQuote:
                    SetOpen(false);
                    e.Use();
                    break;
                case KeyCode.UpArrow:
                    Recall(-1);
                    e.Use();
                    break;
                case KeyCode.DownArrow:
                    Recall(1);
                    e.Use();
                    break;
            }
        }

        public void SetOpen(bool value)
        {
            if (open == value) return;
            open = value;
            SuppressGameInput = value;
            if (value)
            {
                scroll = float.MaxValue;
                frozenView = CameraRotation();
                Print("Dustline Miku Mod 调试控制台 —— 输入 help 查看命令，Esc 或 ` 关闭。");
            }
            else
            {
                RestoreViewAngles();
            }
        }

        public void ClearLines() => lines.Clear();

        // 控制台打开时游戏仍会读鼠标增量，这里锁住画面，回正时再把视角写回游戏状态
        private Quaternion frozenView;

        private static Quaternion CameraRotation()
        {
            Game game = Game.Instance;
            return game != null && game.Camera != null ? game.Camera.transform.rotation : Quaternion.identity;
        }

        private void LateUpdate()
        {
            if (!open) return;
            Game game = Game.Instance;
            if (game?.Camera == null) return;
            game.Camera.transform.rotation = frozenView;
        }

        private void RestoreViewAngles()
        {
            Game game = Game.Instance;
            if (game == null) return;
            try
            {
                Vector3 euler = frozenView.eulerAngles;
                SetPrivateField(game, "yaw", euler.y);
                SetPrivateField(game, "pitch", euler.x);
            }
            catch (Exception e)
            {
                MikuModPlugin.Log?.LogWarning("Restore view angles failed: " + e.Message);
            }
        }

        private static void SetPrivateField(object target, string field, float value)
        {
            System.Reflection.FieldInfo info = target.GetType().GetField(field,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            info?.SetValue(target, value);
        }

        public void Print(string text)
        {
            if (text == null) return;
            foreach (string line in text.Split('\n'))
            {
                lines.Add(line);
            }
            while (lines.Count > MaxLines) lines.RemoveAt(0);
            scroll = float.MaxValue;
        }

        private void Recall(int direction)
        {
            if (history.Count == 0) return;
            if (historyIndex < 0) historyIndex = history.Count;
            historyIndex = Mathf.Clamp(historyIndex + direction, 0, history.Count);
            input = historyIndex >= history.Count ? string.Empty : history[historyIndex];
        }

        private void Submit()
        {
            string text = input.Trim();
            input = string.Empty;
            historyIndex = -1;
            if (text.Length == 0) return;
            history.Add(text);
            ConsoleCommands.Execute(this, text);
        }

        private void OnGUI()
        {
            if (!open) return;
            HandleGuiKeys();
            EnsureStyles();

            float width = Mathf.Min(Screen.width - 40f, 900f);
            float height = Mathf.Min(Screen.height - 120f, 420f);
            Rect panel = new Rect(20f, 20f, width, height);

            GUI.Box(panel, GUIContent.none);
            Rect inner = new Rect(panel.x + 10f, panel.y + 8f, panel.width - 20f, panel.height - 16f);

            // 输出区
            float inputHeight = 30f;
            Rect output = new Rect(inner.x, inner.y, inner.width, inner.height - inputHeight - 6f);
            Vector2 contentSize = labelStyle.CalcSize(new GUIContent(string.Join("\n", lines)));
            Rect scrollArea = new Rect(output.x, output.y, output.width - 18f, output.height);
            GUI.BeginScrollView(scrollArea, new Vector2(0f, scroll), new Rect(0f, 0f, contentSize.x + 8f, contentSize.y + 8f));
            GUI.Label(new Rect(0f, 0f, contentSize.x + 8f, contentSize.y + 8f), string.Join("\n", lines), labelStyle);
            GUI.EndScrollView();

            // 输入行
            Rect prompt = new Rect(inner.x, inner.y + inner.height - inputHeight, inner.width, inputHeight);
            GUI.Label(new Rect(prompt.x, prompt.y, 20f, prompt.height), ">", inputStyle);
            GUI.SetNextControlName("MikuConsoleInput");
            input = GUI.TextField(new Rect(prompt.x + 20f, prompt.y, prompt.width - 20f, prompt.height), input, inputStyle);
            if (!GUI.GetNameOfFocusedControl().Equals("MikuConsoleInput") && input.Length == 0) GUI.FocusControl("MikuConsoleInput");
        }

        private void EnsureStyles()
        {
            if (stylesReady) return;
            labelStyle = new GUIStyle(GUI.skin.label)
            {
                richText = false,
                wordWrap = false,
                alignment = TextAnchor.UpperLeft,
                fontSize = 15
            };
            labelStyle.normal.textColor = new Color(0.92f, 0.92f, 0.92f);
            inputStyle = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 16
            };
            stylesReady = true;
        }
    }

    /// <summary>控制台命令表。全部命令都尽量只依赖可验证的游戏 API。</summary>
    internal static class ConsoleCommands
    {
        private static readonly (string name, string usage, string help)[] Catalog =
        {
            ("help", "help [命令]", "显示命令列表或某条命令的说明"),
            ("clear", "clear", "清空输出"),
            ("echo", "echo <文本>", "输出一段文本"),
            ("pos", "pos", "打印本地玩家坐标与视角"),
            ("tp", "tp <x> <y> <z>", "把本地玩家传送到指定坐标"),
            ("tp_to", "tp_to <enemy|ally>", "传送到最近的敌人或队友身边"),
            ("thirdperson", "thirdperson [on|off]", "开关第三人称（等同 V 键）"),
            ("fov", "fov <数值>", "设置第一人称视场角（80~110）"),
            ("model", "model [cat|static]", "切换模型方案：cat=两阵营猫耳初音，static=CT 用静态立绘"),
            ("reload", "reload", "重新读取 BepInEx.cfg 配置"),
            ("log", "log", "输出 BepInEx 日志文件路径"),
            ("version", "version", "显示 Mod 版本"),
        };

        public static void Execute(ConsoleWindow console, string line)
        {
            console.Print("> " + line);
            string[] parts = line.Split(' ');
            string cmd = parts[0].ToLowerInvariant();
            string[] args = new string[parts.Length - 1];
            Array.Copy(parts, 1, args, 0, args.Length);
            string rest = args.Length > 0 ? string.Join(" ", args).Trim() : string.Empty;

            try
            {
                switch (cmd)
                {
                    case "help": Help(console, rest); break;
                    case "clear": Clear(console); break;
                    case "echo": console.Print(rest.Length > 0 ? rest : "(空)"); break;
                    case "pos": Pos(console); break;
                    case "tp": Teleport(console, args); break;
                    case "tp_to": TeleportToNearest(console, rest); break;
                    case "thirdperson": ThirdPerson(console, rest); break;
                    case "fov": Fov(console, rest); break;
                    case "model": Model(console, rest); break;
                    case "reload": Reload(console); break;
                    case "log": console.Print("日志: " + (MikuModPlugin.PluginDirectory ?? "?") + "\\..\\BepInEx\\LogOutput.log"); break;
                    case "version": console.Print($"Dustline Miku Mod v{MikuModPlugin.PluginVersion}"); break;
                    default:
                        console.Print($"未知命令 '{cmd}'，输入 help 查看列表。");
                        break;
                }
            }
            catch (Exception e)
            {
                console.Print("命令执行出错: " + e.Message);
                MikuModPlugin.Log?.LogWarning($"Console command '{cmd}' failed: {e}");
            }
        }

        private static void Help(ConsoleWindow console, string name)
        {
            if (name.Length > 0)
            {
                foreach ((string n, string usage, string help) in Catalog)
                {
                    if (n == name.ToLowerInvariant())
                    {
                        console.Print($"  {usage}\n    {help}");
                        return;
                    }
                }
                console.Print($"没有命令 '{name}'。");
                return;
            }
            StringBuilder builder = new StringBuilder("可用命令：");
            foreach ((string n, string usage, string help) in Catalog) builder.Append("\n  ").Append(usage);
            console.Print(builder.ToString());
        }

        private static void Clear(ConsoleWindow console)
        {
            console.ClearLines();
            console.Print("(已清空)");
        }

        private static void Pos(ConsoleWindow console)
        {
            Game game = Game.Instance;
            if (game?.Local == null)
            {
                console.Print("当前不在对局中。");
                return;
            }
            Dustline.Core.V3 p = game.Local.Position;
            console.Print($"pos = {p.X:F2}, {p.Y:F2}, {p.Z:F2}  yaw={game.Local.Yaw:F1} pitch={game.Local.Pitch:F1}  hp={game.Local.Health:F0} armor={game.Local.Armor:F0}");
        }

        private static void Teleport(ConsoleWindow console, string[] args)
        {
            Game game = Game.Instance;
            if (game?.Local == null) { console.Print("当前不在对局中。"); return; }
            if (args.Length < 3 ||
                !float.TryParse(args[0], out float x) ||
                !float.TryParse(args[1], out float y) ||
                !float.TryParse(args[2], out float z))
            {
                console.Print("用法: tp <x> <y> <z>");
                return;
            }
            Dustline.Core.Player player = game.Local;
            player.Position = new Dustline.Core.V3(x, y, z);
            player.Velocity = default;
            console.Print($"已传送到 {x:F1}, {y:F1}, {z:F1}（下一 tick 会被模拟覆盖则为无效传送）");
        }

        private static void TeleportToNearest(ConsoleWindow console, string who)
        {
            Game game = Game.Instance;
            if (game?.State == null) { console.Print("当前不在对局中。"); return; }
            Dustline.Core.Player best = null;
            float bestDistance = float.MaxValue;
            foreach (Dustline.Core.Player other in game.State.Players)
            {
                if (other == null || other == game.Local || !other.Alive) continue;
                bool isAlly = other.Team == game.Local.Team;
                if (who.Equals("enemy", StringComparison.OrdinalIgnoreCase) && isAlly) continue;
                if (who.Equals("ally", StringComparison.OrdinalIgnoreCase) && !isAlly) continue;
                float d = Dustline.Core.V3.Distance(other.Position, game.Local.Position);
                if (d < bestDistance) { bestDistance = d; best = other; }
            }
            if (best == null) { console.Print("没有找到目标。"); return; }
            game.Local.Position = best.Position;
            console.Print($"已传送到 {(who.Length > 0 ? who : "最近目标")}（距离 {bestDistance:F1}m）");
        }

        private static void ThirdPerson(ConsoleWindow console, string arg)
        {
            if (arg.Length == 0)
            {
                console.Print("用法: thirdperson [on|off]（也可直接按 V）");
                return;
            }
            ThirdPersonController.SetThirdPerson(arg.Equals("on", StringComparison.OrdinalIgnoreCase));
        }

        private static void Fov(ConsoleWindow console, string arg)
        {
            if (!float.TryParse(arg, out float value) || value < 40f || value > 140f)
            {
                console.Print("用法: fov <80~110>");
                return;
            }
            MikuConfig.FovOverride = value;
            console.Print($"视场角覆盖为 {value:F0}（-1 恢复游戏设置）");
        }

        private static void Model(ConsoleWindow console, string arg)
        {
            if (arg.Length == 0)
            {
                console.Print($"当前方案: cat={MikuConfig.UseCatForBothTeams}（输入 model cat 或 model static 切换，下一回合生效）");
                return;
            }
            if (arg.Equals("cat", StringComparison.OrdinalIgnoreCase))
            {
                MikuConfig.UseCatForBothTeams = true;
                console.Print("已切换：两阵营均使用带骨骼的猫耳初音（下回合重生后生效）");
            }
            else if (arg.Equals("static", StringComparison.OrdinalIgnoreCase))
            {
                MikuConfig.UseCatForBothTeams = false;
                console.Print("已切换：T 用猫耳初音、CT 用静态立绘（下回合重生后生效）");
            }
            else
            {
                console.Print("用法: model [cat|static]");
            }
        }

        private static void Reload(ConsoleWindow console)
        {
            try
            {
                MikuConfig.Initialize(MikuModPlugin.ConfigFile);
                console.Print("配置已重新读取。");
            }
            catch (Exception e)
            {
                console.Print("重载失败: " + e.Message);
            }
        }
    }
}