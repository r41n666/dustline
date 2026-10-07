"""扫描 resources.assets 中的 DSR1/DSR2 rig 二进制，解析骨骼表与部件材质，定位玩家身体模型。"""
import struct, sys

PATH = "D:/Dustline/dustline/Dustline_Data/resources.assets"
data = open(PATH, 'rb').read()
print(f"file size: {len(data):,}")

def parse_rig(off):
    """尝试解析一个 rig；失败返回 None。"""
    try:
        p = off
        magic = struct.unpack_from('<I', data, p)[0]; p += 4
        if magic not in (827478852, 844256068):
            return None
        bone_count = struct.unpack_from('<i', data, p)[0]; p += 4
        if not (1 <= bone_count <= 500):
            return None
        names = []
        for i in range(bone_count):
            ln = struct.unpack_from('<H', data, p)[0]; p += 2
            if ln == 0 or ln > 120:
                return None
            s = data[p:p+ln].decode('utf-8', 'replace'); p += ln
            names.append(s)
            parent = struct.unpack_from('<i', data, p)[0]; p += 4
            p += 12  # position
            p += 16  # rotation
        if not all(c.isprintable() for c in ''.join(names[:20])):
            return None
        part_count = struct.unpack_from('<i', data, p)[0]; p += 4
        if not (0 <= part_count <= 64):
            return None
        mats = []
        for j in range(part_count):
            if magic == 844256068:
                ln = struct.unpack_from('<H', data, p)[0]; p += 2
                if ln > 400: return None
                p += ln  # part name
            ln = struct.unpack_from('<H', data, p)[0]; p += 2
            if ln > 400: return None
            mat = data[p:p+ln].decode('utf-8', 'replace'); p += ln
            mats.append(mat)
            verts = struct.unpack_from('<i', data, p)[0]; p += 4
            idx = struct.unpack_from('<i', data, p)[0]; p += 4
            if not (0 <= verts <= 200000 and 0 <= idx <= 600000): return None
            p += verts * 32 + verts * 8 + verts * 16 + idx * 4
        return dict(off=off, magic=magic, bones=names, mats=mats, parts=part_count)
    except Exception:
        return None

seen = set()
cands = []
for magic_bytes in (b"DSR1", b"DSR2"):
    start = 0
    while True:
        idx = data.find(magic_bytes, start)
        if idx < 0:
            break
        start = idx + 1
        if idx in seen:
            continue
        seen.add(idx)
        r = parse_rig(idx)
        if r:
            cands.append(r)

print(f"parsed rigs: {len(cands)}")
for r in cands:
    player = any('player' in m or 'characters' in m for m in r['mats'])
    tag = "  <<< PLAYER" if player else ""
    print(f"@0x{r['off']:x} magic={'DSR2' if r['magic']==844256068 else 'DSR1'} bones={len(r['bones'])} parts={r['parts']} mats={r['mats'][:3]}{tag}")

# 详细输出玩家模型骨骼
print()
for r in cands:
    if any('player' in m or 'characters' in m for m in r['mats']):
        print(f"=== PLAYER RIG @0x{r['off']:x} parts={r['parts']} ===")
        print("MATS:", )
        for m in r['mats']:
            print("   ", m)
        print("BONES:", len(r['bones']))
        print(', '.join(r['bones']))
        print()
