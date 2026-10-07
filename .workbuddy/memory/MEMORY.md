# Dustline 项目长期记忆

## 项目性质
- `D:/Dustline/dustline` 是**已发布的 Windows 可玩版**（Unity 6000.3.23f1 Mono），只有编译产物，**没有源码**。
- 内部程序集：`Dustline.Core.dll`（游戏逻辑/模拟）、`Dustline.Runtime.dll`（Unity 表现层，命名空间 `Dustline`）。
- 玩家模型/动画/音频全部是原版 CS:GO legacy 资源，以自定义二进制格式打包进 `resources.assets`
  （rig 魔数 `DSR1`/`DSR2`，可在 `Resources/SourceContent/<model>` 读到）。

## Mod 开发约定
- 改动一律通过 **BepInEx 5 + Harmony 运行时注入**，**不修改游戏原始文件**；
  卸载 = 删 `winhttp.dll` + `doorstop_config.ini` + `.doorstop_version` + `BepInEx/`。
- 编译链：`moddev/dotnet/dotnet.exe`（本地 SDK 8.0，已 gitignore），
  目标框架 netstandard2.1，引用 `BepInEx/core/BepInEx.dll`、`0Harmony.dll`、
  游戏 `Managed/UnityEngine*.dll` + `Managed/UnityEngine.dll`（门面程序集，BepInEx 依赖它）+ `Dustline.*.dll`。
- 一键构建部署：`bash moddev/build_and_deploy.sh`。
- 文档见仓库根 `MOD_README.md`；开发踩坑与内部 API 锚点见 `.workbuddy/memory/2026-10-07.md`。

## 环境事实
- 本机无 Unity 编辑器可用版本（Tuanjie 2022.3.62t16 与游戏的 Unity 6000.3 不兼容，无法打包 AssetBundle）。
- 游戏需直接双击/basht 启动，**不要用 PowerShell Start-Process**（会导致进程提前退出）。
- 测试时游戏进程随shell 会话结束被回收 → 需用长驻后台任务（`run_in_background` + `sleep`）保持。
- 本机已装 ilspycmd（`moddev/tools/`，已 gitignore），反编译用
  `dotnet moddev/tools/ilspycmd/tools/net8.0/any/ilspycmd.dll -o <dir> -p <dll>`。

## Git 纪律
- 本仓库是 **clone 自他人仓库** `mmsx4717/dustline`，当前账号 `r41n666` **没有 push 权限**
  （`git push` 返回 `remote: Permission denied ... 403`）。
- 因此：**只做本地 commit，不要尝试 push**，交付时说明「已本地提交，推送需仓库所有者权限」。
- Mod 相关提交（2026-10-07）：`dfb4d71 Add Dustline Miku Mod`。