"""提取 cat_hatsune_miku.glb 的骨架信息：全部关节名、世界静置位置、整体包围盒。"""
import json, struct, math

def read_glb(path):
    data = open(path, 'rb').read()
    magic, version, length = struct.unpack_from('<4sII', data, 0)
    offset = 12
    gltf, bins = None, []
    while offset < length:
        clen, ctype = struct.unpack_from('<II', data, offset)
        chunk = data[offset+8:offset+8+clen]
        if ctype == 0x4E4F534A: gltf = json.loads(chunk.decode('utf-8'))
        elif ctype == 0x004E4942: bins.append(chunk)
        offset += 8 + clen
    return gltf, bins[0]

def qmul(a, b):
    ax, ay, az, aw = a; bx, by, bz, bw = b
    return (aw*bx + ax*bw + ay*bz - az*by, aw*by - ax*bz + ay*bw + az*bx,
            aw*bz + ax*by - ay*bx + az*bw, aw*bw - ax*bx - ay*by - az*bz)

def qrot(q, v):
    x, y, z, w = q; vx, vy, vz = v
    tx = 2*(y*vz - z*vy); ty = 2*(z*vx - x*vz); tz = 2*(x*vy - y*vx)
    return (vx + w*tx + (y*tz - z*ty), vy + w*ty + (z*tx - x*tz), vz + w*tz + (x*ty - y*tx))

g, binchunk = read_glb("D:/Dustline/dustline/assets/cat_hatsune_miku.glb")
nodes = g['nodes']

# 计算每个节点的世界变换
world = {}
def visit(i, parentWorld=None):
    if i in world: return world[i]
    n = nodes[i]
    t = n.get('translation', [0,0,0])
    r = n.get('rotation', [0,0,0,1])
    s = n.get('scale', [1,1,1])
    if parentWorld is None:
        wp, wr, ws = tuple(t), tuple(r), tuple(s)
    else:
        pwp, pwr, pws = parentWorld
        tp = qrot(pwr, tuple(t[i2]*pws[i2] for i2 in range(3)))
        wp = tuple(pwp[k]+tp[k] for k in range(3))
        wr = qmul(pwr, tuple(r))
        ws = tuple(pws[k]*s[k] for k in range(3))
    world[i] = (wp, wr, ws)
    for c in n.get('children', []): visit(c, world[i])
    return world[i]

for root in g['scenes'][0]['nodes']:
    visit(root)

# 找 skin
skin = g['skins'][0]
joints = skin['joints']
print(f"joints: {len(joints)}")
# 根 joint
root_joint = joints[0]
print("root joint:", nodes[root_joint].get('name'))
# 打印所有关节名（按层级顺序）及其世界位置
for j in joints:
    wp, wr, ws = world[j]
    print(f"  {nodes[j].get('name','?'):28s} wp=({wp[0]:+.4f},{wp[1]:+.4f},{wp[2]:+.4f}) scale={ws[0]:.4f}")

# 整体包围盒（由 POSITION accessor min/max + 世界变换近似；用 skin 的 mesh 顶点上限）
print()
print("=== accessor POSITION min/max (第一个网格) ===")
for mi, mesh in enumerate(g['meshes'][:1]):
    for p in mesh['primitives']:
        acc = g['accessors'][p['attributes']['POSITION']]
        print("mesh0 posmin", acc.get('min'), "posmax", acc.get('max'))
