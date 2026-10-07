# Dustline Miku Mod

给 `Dustline.exe`（Unity 6000.3.23f1 / Mono 发布的 CS:GO 复刻版）做的运行时 Mod：
**T 方模型换成猫耳初音、CT 方换成初音立绘、V 键切换第一/第三人称、受击音效换成 27 条随机女声受击音。**

因为游戏只发布了编译后的 Windows 版本（没有源码），Mod 通过 **BepInEx 5 + Harmony 运行时注入**实现，
不改游戏任何原始文件（`Dustline.exe` / `Dustline_Data` 均保持原样，可随时删除 Mod 恢复原版）。

---

## 1. 已实现的功能

| 功能 | 实现方式 | 验证状态 |
|---|---|---|
| T 方身体模型 → `cat_hatsune_miku.glb` | 保留原版骨骼与动画，把模型骨骼每帧映射到游戏骨架 | ✅ 实测：模型正确、持枪、比例正常 |
| CT 方身体模型 → `miku.glb` | 同上（该模型无骨骼，为静态模型，自动缩放并跟随骨盆高度） | ✅ 实测：巨大双马尾初音立绘 |
| 第三人称（V 键切换） | 在游戏设置完相机后于 `LateUpdate` 沿视线后拉，`SphereCast` 贴墙收缩 | ✅ 实测：过肩视角、模型可见、可开枪 |
| 受击音效替换 | 在 `GameAudio.Clip()` 前置拦截命中音效路径，27 条 wav 随机取一条 | ✅ 实测：日志确认命中事件被替换 |

### 关键设计

游戏用的是**自研骨骼动画系统**（`SourceRig` 直接驱动骨骼 `Transform`，不是 Unity Animator）。
所以 Mod 的做法是「换皮不换动画」—— 保留原版骨架跑全部动画，每帧把骨骼姿态重定向到 Miku 骨架：

```
游戏骨架 t_leet / ct_idf （继续跑全部原版动画：移动/瞄准/开火/换弹/死亡物理）
        │  每帧重定向（LateUpdate）
        ▼
Miku 模型骨架（111 关节）
```

**重定向公式（定稿）**：

- **位置钉位骨**（骨盆、双手、双肘）：直接采用游戏骨骼的世界位置；
  旋转 = `游戏世界旋转 × RestOffset`
- **旋转骨**（脊柱/颈/头/肩/大臂/腿）：`初音世界旋转 = 游戏世界旋转 × RestOffset`，
  再换算为局部旋转（父骨先写，按骨骼深度排序）
- **RestOffset** = `inverse(游戏静置世界旋转) × 初音静置世界旋转`，
  在**第一帧姿态同步后**标定一次 —— 不能在 bind pose 时刻标定
  （这套 Source 骨架的 bind pose 是躺姿，会导致整体倾倒）

**踩过的坑（全部已修，详见 .workbuddy/memory/2026-10-07.md）**：

| 坑 | 症状 |
|---|---|
| `JOINTS_0` 走浮点归一化，关节索引全变 0 | 模型完全不动（蒙皮失效） |
| 骨架节点没做与顶点相同的坐标转换 | 模型炸开 |
| bindpose 少乘 `meshNodeWorld` | 模型扭曲 |
| 同名骨重复（肘/腕副本）选错 | 手臂不弯 |
| 重定向缺静置偏移 | 蜷缩 |
| bind pose 时刻标定 | 整体倾倒 |
| 用 `Renderer.bounds` 测身高 | 高度错误（蒙皮前是过期值，要用 `sharedMesh.bounds`） |

**自检日志**：启动时输出 `[selfcheck] verts=... weighted=... bindposeDetMin=... bones=...`。
判读：`weighted` 必须等于 `verts`；`bindposeDetMin` 应为 +1；`bones` 应等于模型骨骼数。
任何变形/炸开问题先看这行数字，不要靠截图猜。

**共 21 根骨骼一一映射**：pelvis→hips（位置+旋转）、spine_0→spine、spine_2→chest、
neck_0→neck、head_0→head、clavicle→shoulder、arm_upper→upper_arm、arm_lower→lower_arm（位置）、
hand→hand（位置）、leg_upper→upper_leg、leg_lower→lower_leg、ankle→foot、ball→toes。

---

## 2. 目录结构

```
D:/Dustline/dustline/
├── Dustline.exe / Dustline_Data/        ← 游戏本体，未修改
├── winhttp.dll + doorstop_config.ini    ← BepInEx 注入入口
├── BepInEx/
│   ├── LogOutput.log                ← Mod 运行日志（排错看这里）
│   ├── config/DustlineMikuMod.cfg   ← Mod 配置
│   └── plugins/DustlineMikuMod/
│       ├── DustlineMikuMod.dll
│       └── assets/
│           ├── cat_hatsune_miku.glb     ← T 方模型
│           ├── miku.glb                 ← CT 方模型
│           └── hit_female/*.wav         ← 27 条受击音效
└── moddev/                          ← Mod 源码与构建脚本（不影响游戏运行）
    ├── DustlineMikuMod/*.cs
    ├── build_and_deploy.sh          ← 一键编译+部署
    └── decompiled/                  ← 游戏程序集反编译源码（开发参考）
```

**卸载 Mod**：删除 `winhttp.dll`、`doorstop_config.ini`、`.doorstop_version`、`BepInEx/` 四个即可完全恢复原版。

---

## 3. 配置（BepInEx/config/DustlineMikuMod.cfg）

改完配置重启游戏生效。

```ini
[Model]
  Enabled = true          # 总开关：是否替换身体模型
  Scale = 1.30            # T 方（带骨骼）模型整体缩放，1.30 ≈ 原角色身高比例
  YawOffset = 180         # 模型朝向补偿（度）
  StaticHeight = 1.72     # CT 方（静态）模型自动缩放后的目标身高（米）
  StaticHeightFactor = 1.0 # 静态模型高度跟随骨盆的比例（蹲伏下沉）
  PelvisRestHeight = 0.95 # 骨盆静置高度兜底值

[ThirdPerson]
  Enabled = true
  ToggleKey = V           # 切换按键
  Distance = 3.00         # 相机后拉距离
  Height = 0.22           # 相机抬高
  ShoulderOffset = 0.35   # 右肩偏移
  MinDistance = 0.45      # 贴墙时最近距离

[HitSound]
  Enabled = true
  Folder = hit_female     # 音效目录（相对插件 assets/）
  Volume = 1.0            # 命中事件音量倍率
```

---

## 4. 已知边界（重要）

1. **CT 方模型是静态的**：`miku.glb` 来自 Sketchfab，是**没有骨骼、没有动画**的 OBJ 转换模型。
   因此 CT 角色在游戏里是「立绘跟着走」——不会跑动、不会开枪摆臂（模型本身是双臂展开的立绘姿势）。
   T 方模型有完整骨骼，跑跳/瞄准/开火/换弹/死亡倒地全部正常。
2. **CT 模型双马尾很长**（原模型横铺约 19.5 个单位），第三人称时可能擦到相机；
   觉得太大/太长可把 `StaticHeight` 调小（例如 1.4）。
3. **第三人称时本地枪口特效仍取第一人称视图模型锚点**（游戏内部逻辑决定），
   所以 muzzle flash/抛壳位置在第三人称下略微偏（不影响命中判定）。
4. **开镜（Scoped）时自动回到第一人称**，避免影响 AWP 瞄准；死亡与观战时也自动回到原逻辑。
5. 受击音效覆盖 6 个事件：身体 `Flesh.BulletImpact`、护甲 `Dustline.KevlarHit`、
   爆头 `Dustline.Headshot`、头盔 `Dustline.HelmetHit`、刀命中 `Weapon_Knife.Hit` / `Weapon_Knife.Stab`。
   打在墙上的刀声 `Weapon_Knife.HitWall` 保持原版。
6. 命中判定、命中盒、伤害数值**完全未改动**（仍是原版 CS:GO legacy 规则），只是外观与音效被替换。

---

## 5. 二次开发

```bash
# 修改源码后一键编译并部署
bash D:/Dustline/dustline/moddev/build_and_deploy.sh
```

源码结构：

| 文件 | 职责 |
|---|---|
| `Plugin.cs` | BepInEx 入口、配置初始化、Harmony 装配 |
| `Patches.cs` | 5 处 Harmony 补丁（模型挂载 / 可见性节流补偿 / 第三人称姿态 / 视图模型隐藏 / 音效替换） |
| `MikuBody.cs` | 每个角色上的模型替换组件：骨骼映射、每帧姿态镜像、隐藏原模型 |
| `MikuModelFactory.cs` | glTF → Unity GameObject（坐标转换、蒙皮、URP 材质、静态模型自动定标） |
| `Gltf.cs` | GLB/glTF 2.0 运行时解析器 |
| `HitSounds.cs` | WAV 解码 + 命中事件路径收集 + 随机播放 |
| `ThirdPerson.cs` | 第三人称相机与 V 键切换 |
| `MiniJson.cs` | 轻量 JSON 解析（glTF 元数据） |

游戏内部 API 全部来自对 `Dustline.Core.dll` / `Dustline.Runtime.dll` 的反编译，
参考源码在 `moddev/decompiled/`，关键锚点：

- `SceneView.CreateActor(id, team)` —— 角色创建入口，`t_leet`(T) / `ct_idf`(CT) 在此决定
- `SourceCharacter.Build` —— 原版身体 rig 创建与动画驱动
- `SourceRig.get_IsVisible` —— 原版把不可见角色姿态节流到 10Hz，替换模型后必须放行
- `SceneView.UpdatePlayers` —— 第一人称把本地 actor `SetActive(false)`，第三人称需补回并驱动姿态
- `WeaponView.Animate` / `Show` —— 第一人称视图模型（手臂+武器），第三人称需隐藏
- `GameAudio.Clip(path)` —— 所有事件音效播放的必经之路