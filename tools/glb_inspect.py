"""GLB 结构侦察脚本：解析 glTF 2.0 二进制容器，输出节点/骨骼/网格/材质/动画概要。"""
import json, struct, sys

def read_glb(path):
    with open(path, 'rb') as f:
        data = f.read()
    magic, version, length = struct.unpack_from('<4sII', data, 0)
    assert magic == b'glTF', f"not glb: {magic}"
    offset = 12
    gltf = None
    bins = []
    while offset < length:
        clen, ctype = struct.unpack_from('<II', data, offset)
        chunk = data[offset+8:offset+8+clen]
        if ctype == 0x4E4F534A:
            gltf = json.loads(chunk.decode('utf-8'))
        elif ctype == 0x004E4942:
            bins.append(chunk)
        offset += 8 + clen
    return gltf, bins

def summarize(path):
    g, bins = read_glb(path)
    print(f"=== {path} ===")
    print(f"asset: {g.get('asset', {})}")
    print(f"scenes: {len(g.get('scenes', []))}, nodes: {len(g.get('nodes', []))}, meshes: {len(g.get('meshes', []))}")
    print(f"skins: {len(g.get('skins', []))}, materials: {len(g.get('materials', []))}, textures: {len(g.get('textures', []))}, images: {len(g.get('images', []))}")
    print(f"animations: {len(g.get('animations', []))}")
    print(f"extensionsUsed: {g.get('extensionsUsed', [])}")
    # 节点名样本
    nodes = g.get('nodes', [])
    names = [n.get('name', f'node{i}') for i, n in enumerate(nodes)]
    print(f"--- node names (first 80 of {len(names)}) ---")
    print(', '.join(names[:80]))
    # 骨骼
    for si, skin in enumerate(g.get('skins', [])):
        joints = skin.get('joints', [])
        jn = [nodes[j].get('name', f'n{j}') for j in joints]
        print(f"--- skin[{si}] '{skin.get('name','')}' joints={len(joints)} ---")
        print(', '.join(jn[:60]))
        if len(jn) > 60:
            print(f"... and {len(jn)-60} more")
    # 网格
    for mi, mesh in enumerate(g.get('meshes', [])):
        prims = mesh.get('primitives', [])
        attrs = set()
        tri_total = 0
        for p in prims:
            attrs.update(p.get('attributes', {}).keys())
            if 'indices' in p:
                acc = g['accessors'][p['indices']]
                tri_total += acc.get('count', 0) // 3
        print(f"mesh[{mi}] '{mesh.get('name','')}' prims={len(prims)} tris={tri_total} attrs={sorted(attrs)}")
    # 材质
    for mi, m in enumerate(g.get('materials', [])):
        pbr = m.get('pbrMetallicRoughness', {})
        base = pbr.get('baseColorTexture', {}).get('index')
        print(f"material[{mi}] '{m.get('name','')}' baseTex={base} doubleSided={m.get('doubleSided')} alphaMode={m.get('alphaMode')}")
    # 图像
    for ii, img in enumerate(g.get('images', [])):
        print(f"image[{ii}] name='{img.get('name','')}' mime={img.get('mimeType')} bufferView={img.get('bufferView')} uri={img.get('uri','')[:50]}")
    # 动画
    for ai, a in enumerate(g.get('animations', [])):
        print(f"anim[{ai}] '{a.get('name','')}' channels={len(a.get('channels', []))} samplers={len(a.get('samplers', []))}")

if __name__ == '__main__':
    for p in sys.argv[1:]:
        summarize(p)
        print()
