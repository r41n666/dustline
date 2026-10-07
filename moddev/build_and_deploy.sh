#!/bin/bash
# 构建并部署 Dustline Miku Mod 到游戏目录
set -e
GAME="D:/Dustline/dustline"
PROJ="$GAME/moddev/DustlineMikuMod"
DOTNET="$GAME/moddev/dotnet/dotnet.exe"

echo "==> 编译插件"
cd "$PROJ"
"$DOTNET" build -c Release -v m | grep -E "error|警告 CS|成功" || true

echo "==> 停止运行中的游戏"
taskkill //F //IM Dustline.exe 2>/dev/null | head -1 || true
sleep 2

echo "==> 部署插件与资源"
cp "$PROJ/bin/Release/DustlineMikuMod.dll" "$GAME/BepInEx/plugins/DustlineMikuMod/"
mkdir -p "$GAME/BepInEx/plugins/DustlineMikuMod/assets/hit_female"
cp "$GAME/assets/cat_hatsune_miku.glb" "$GAME/assets/miku.glb" "$GAME/BepInEx/plugins/DustlineMikuMod/assets/"
cp "$GAME"/assets/hit_female/*.wav "$GAME/BepInEx/plugins/DustlineMikuMod/assets/hit_female/"

echo "==> 完成。启动游戏：$GAME/Dustline.exe"
echo "    日志：$GAME/BepInEx/LogOutput.log"
echo "    配置：$GAME/BepInEx/config/DustlineMikuMod.cfg"