"""提取 t_leet 与 ct_idf 的关键骨骼静置数据（位置/旋转），并计算身高参考。"""
import struct

PATH = "D:/Dustline/dustline/Dustline_Data/resources.assets"
data = open(PATH, 'rb').read()

TARGETS = {0x659d7b0: 't_leet(85)', 0x1097ba0: 'ct_idf(94)'}

def parse_rig(off):
    p = off
    magic = struct.unpack_from('<I', data, p)[0]; p += 4
    bone_count = struct.unpack_from('<i', data, p)[0]; p += 4
    bones = []
    for i in range(bone_count):
        ln = struct.unpack_from('<H', data, p)[0]; p += 2
        s = data[p:p+ln].decode('utf-8', 'replace'); p += ln
        parent = struct.unpack_from('<i', data, p)[0]; p += 4
        pos = struct.unpack_from('<3f', data, p); p += 12
        rot = struct.unpack_from('<4f', data, p); p += 16
        bones.append((s, parent, pos, rot))
    return bones

for off, name in TARGETS.items():
    bones = parse_rig(off)
    print(f"=== {name} @0x{off:x} ===")
    # 打印关键骨骼：位置 + 世界位置（沿父链累加，仅用于估计，忽略旋转）
    idx = {b[0]: i for i, b in enumerate(bones)}
    def world_pos(i):
        # 累加平移（粗略：忽略旋转，仅用于量级检查）
        x = y = z = 0.0
        j = i
        chain = []
        while j >= 0:
            chain.append(j)
            j = bones[j][1]
        for j in reversed(chain):
            x += bones[j][2][0]; y += bones[j][2][1]; z += bones[j][2][2]
        return (x, y, z)
    keys = ['pelvis', 'spine_0', 'spine_3', 'neck_0', 'head_0', 'clavicle_L', 'arm_upper_L', 'arm_lower_L', 'hand_L', 'leg_upper_L', 'leg_lower_L', 'ankle_L', 'ball_L']
    for k in keys:
        if k in idx:
            i = idx[k]
            s, par, pos, rot = bones[i]
            wp = world_pos(i)
            print(f"  {k:14s} parent={bones[par][0] if par>=0 else 'ROOT':14s} localPos=({pos[0]:.4f},{pos[1]:.4f},{pos[2]:.4f}) sumPos=({wp[0]:.3f},{wp[1]:.3f},{wp[2]:.3f})")
    # 找出全部带 leg/ankle 字样的骨骼
    print("  legs:", [b[0] for b in bones if 'leg' in b[0] or 'ankle' in b[0] or 'ball' in b[0] or 'foot' in b[0]])
    print()
