"""Reload every treasure in Treasures/ (FBX and blend) and check it against stats.json.

Headless, nothing is saved except verification.json:
  blender -b --factory-startup --python verify_treasures.py
Checks: triangle count and budget (S 3,000 / M 4,000), dimensions, UVs, no degenerate faces,
base on z=0, one atlas material and no emission (grip handles were removed), aim zones
(S: 1, M: left/right) with their custom properties, packed images in the blend.
"""
import bpy, json
from pathlib import Path
from mathutils import Vector

HERE = Path(__file__).resolve().parent
OUT = HERE / 'Treasures'
BUDGET = {'S': 3000, 'M': 4000}


def inspect(objects, expected):
    bpy.context.view_layer.update()
    meshes = [o for o in objects if o.type == 'MESH']
    assert len(meshes) == expected['meshes'], (expected['model'], len(meshes))
    for o in meshes: o.data.calc_loop_triangles()
    tris = sum(len(o.data.loop_triangles) for o in meshes)
    assert tris == expected['triangles'] and tris <= BUDGET[expected['treasure_class']], (expected['model'], tris)
    assert all(o.data.uv_layers for o in meshes), expected['model']
    assert all(t.area > 1e-12 for o in meshes for t in o.data.loop_triangles), expected['model']
    pts = [o.matrix_world @ v.co for o in meshes for v in o.data.vertices]
    dims = [max(p[i] for p in pts) - min(p[i] for p in pts) for i in range(3)]
    assert all(abs(a - b) < .001 for a, b in zip(dims, expected['dimensions_m'])), (expected['model'], dims)
    assert abs(min(p.z for p in pts)) < .001, (expected['model'], 'base not on z=0')
    mats = {m for o in meshes for m in o.data.materials if m}
    assert len(mats) == 1, (expected['model'], [m.name for m in mats])
    for m in mats:
        bs = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
        assert bs.inputs['Emission Strength'].default_value == 0, (expected['model'], 'emissive material')
    aims = [o for o in objects if o.type == 'EMPTY' and '_Aim_' in o.name]
    want = 1 if expected['treasure_class'] == 'S' else 2
    assert len(aims) == want == expected['aim_zones'], (expected['model'], len(aims))
    assert all('aim_size_xyz' in o and 'aim_center_xyz' in o for o in aims), expected['model']
    return dict(model=expected['model'], triangles=tris, aim_zones=len(aims), emissive=False, base_on_floor=True, dimension_roundtrip=True)


def run():
    scene = bpy.context.scene; report = []
    for expected in json.loads((OUT / 'stats.json').read_text(encoding='utf-8')):
        for ext in ('fbx', 'blend'):
            before = set(bpy.data.objects); path = OUT / (expected['model'] + '.' + ext)
            if ext == 'fbx':
                bpy.ops.import_scene.fbx(filepath=str(path), use_anim=False, use_custom_props=True)
            else:
                with bpy.data.libraries.load(str(path), link=False) as (src, dst): dst.objects = src.objects
                for o in dst.objects:
                    if o: scene.collection.objects.link(o)
            added = set(bpy.data.objects) - before
            result = inspect(added, expected); result['format'] = ext; report.append(result)
            if ext == 'blend':
                for o in added:
                    if o.type == 'MESH':
                        for m in o.data.materials:
                            for n in m.node_tree.nodes:
                                if n.type == 'TEX_IMAGE': assert n.image and n.image.packed_file, expected['model']
            for o in added: bpy.data.objects.remove(o, do_unlink=True)
    (OUT / 'verification.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print('PASS:', len(report), 'round trips,', len(report) // 2, 'treasures')


run()
