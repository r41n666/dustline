"""从 resources.assets 中定位字符串并打印上下文（用于提取 rig 骨骼名）。"""
import sys

path = "D:/Dustline/dustline/Dustline_Data/resources.assets"
needle = sys.argv[1].encode()
ctx = int(sys.argv[2]) if len(sys.argv) > 2 else 300
maxhits = int(sys.argv[3]) if len(sys.argv) > 3 else 3

with open(path, 'rb') as f:
    data = f.read()

import re
hits = 0
start = 0
while hits < maxhits:
    idx = data.find(needle, start)
    if idx < 0:
        break
    hits += 1
    start = idx + 1
    lo = max(0, idx - ctx)
    hi = min(len(data), idx + ctx)
    chunk = data[lo:hi]
    # 尝试提取可打印字符串
    strings = re.findall(rb'[\x20-\x7e]{4,}', chunk)
    print(f"--- hit {hits} at 0x{idx:x} ---")
    for s in strings[:40]:
        print("   ", s.decode('ascii', 'replace'))
    print()
