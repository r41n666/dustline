"""正确计算 rig 静置世界姿态（四元数），输出关键骨骼的世界位置，作为映射缩放参考。"""
import struct, math

PATH = "D:/Dustline/dustline/Dustline_Data/resources.assets"
data = open(PATH, 'rb').read()

def qmul(a, b):
    ax, ay, az, aw = a; bx, by, bz, bw = b
    return (aw*bx + ax*bw + ay*bz - az*by,
            aw*by - ax*bz + ay*bw + az*bx,
            aw*bz + ax*by - ay*bx + az*bw,
            aw*bw - ax*bx - ay*by - az*bz)

def qrot(q, v):
    # v' = q * v * q^-1
    x, y, z, w = q; vx, vy, vz = v
    # t = 2 * cross(q.xyz, v)
    tx = 2*(y*vz - z*vy); ty = 2*(z*vx - x*vz); tz = 2*(x*vy - y*vx)
    return (vx + w*tx + (y*tz - z*ty),
            vy + w*ty + (z*tx - x*tz),
            vz + w*tz + (x*ty - y*tx))

def parse_rig(off):
    p = off
    p += 4
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

def rest_world(bones):
    """返回每根骨骼的 (worldPos, worldRot)。"""
    n = len(bones)
    wp = [None]*n; wr = [None]*n
    order = []
    # 拓扑序：parents 先于 children
    done = [False]*n
    def visit(i):
        if done[i]: return
        par = bones[i][1]
        if par >= 0: visit(par)
        s, p, pos, rot = bones[i]
        if par < 0:
            wp[i] = pos; wr[i] = rot
        else:
            pw = qrot(wr[par], pos)
            wp[i] = (wp[par][0]+pw[0], wp[par][1]+pw[1], wp[par][2]+pw[2])
            wr[i] = qmul(wr[par], rot)
        done[i] = True
    for i in range(n): visit(i)
    return wp, wr

for off, name in {0x659d7b0: 't_leet', 0x1097ba0: 'ct_idf'}.items():
    bones = parse_rig(off)
    wp, wr = rest_world(bones)
    idx = {b[0]: i for i, b in enumerate(bones)}
    print(f"=== {name} 世界静置位置（rig 根空间）===")
    for k in ['pelvis', 'spine_3', 'head_0', 'hand_L', 'hand_R', 'ankle_L', 'ankle_R', 'ball_L', 'ball_R']:
        if k in idx:
            i = idx[k]
            print(f"  {k:10s} worldPos=({wp[i][0]:.4f}, {wp[i][1]:.4f}, {wp[i][2]:.4f})  worldRot=({wr[i][0]:.3f},{wr[i][1]:.3f},{wr[i][2]:.3f},{wr[i][3]:.3f})")
    print()
