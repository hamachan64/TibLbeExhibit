"""Check the enemy models against the spec (#67). Writes verification.json; exit code 1 if anything fails.

  blender -b --factory-startup --python verify_enemies.py

FBX (re-imported): part names and parents, height, triangle budgets, eye height, pivots, one material per part.
blend: the head turned 0 / 90 / 180 deg does not intersect the body; the phantom's vertex alpha reaches 0 at the
sleeve ends / cape edge; texture sizes <= 2048. Values are compared with stats.json.
"""
import bpy, json, math, sys
from pathlib import Path
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

HERE = Path(__file__).resolve().parent
STATS = {s['model']: s for s in json.loads((HERE / 'stats.json').read_text(encoding='utf-8'))}
results = []


def check(name, ok, detail=''):
    results.append(dict(check=name, ok=bool(ok), detail=detail)); print(('PASS ' if ok else 'FAIL ') + name, detail)


def tris(o):
    o.data.calc_loop_triangles(); return len(o.data.loop_triangles)


def world_bounds(objs):
    pts = [o.matrix_world @ v.co for o in objs for v in o.data.vertices]
    return Vector([min(p[i] for p in pts) for i in range(3)]), Vector([max(p[i] for p in pts) for i in range(3)])


def import_fbx(path):
    # The FBX is written Z-up (axis 'Z' / forward 'Y') with the model turned 180 deg about Z (build_enemies.export).
    # Import it with the same axes and turn it back, so the checks below keep the Blender conventions (front = -Y).
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(path), axis_forward='Y', axis_up='Z')
    bpy.context.view_layer.update()
    seen = set()
    for o in bpy.data.objects:
        if o.rotation_euler.to_quaternion().angle > 1e-5 or any(abs(v - 1.0) > 1e-5 for v in o.scale):
            check('fbx node %s has identity rotation and scale' % o.name, False, 'rot %s scale %s' % (tuple(o.rotation_euler), tuple(o.scale)))
        o.location = Vector((-o.location.x, -o.location.y, o.location.z))
        if o.type == 'MESH' and o.data.name not in seen:
            seen.add(o.data.name); o.data.transform(Matrix.Rotation(math.pi, 4, 'Z')); o.data.update()
    bpy.context.view_layer.update()
    return {o.name: o for o in bpy.data.objects}


def load_blend(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    with bpy.data.libraries.load(str(path), link=False) as (src, dst): dst.objects = list(src.objects)
    for o in dst.objects: bpy.context.scene.collection.objects.link(o)
    bpy.context.view_layer.update()
    return {o.name: o for o in dst.objects}


def bvh(objs):
    dg = bpy.context.evaluated_depsgraph_get(); verts = []; polys = []
    for o in objs:
        base = len(verts); verts += [o.matrix_world @ v.co for v in o.data.vertices]
        o.data.calc_loop_triangles(); polys += [[base + i for i in t.vertices] for t in o.data.loop_triangles]
    return BVHTree.FromPolygons(verts, polys)


# ---------------------------------------------------------------------------------------------- statue
st = STATS['KingStatue']
O = import_fbx(HERE / 'Statue' / 'KingStatue.fbx')
need = {'KingStatue_Root': None, 'KingStatue_Body': 'KingStatue_Root', 'KingStatue_HeadPivot': 'KingStatue_Root',
        'KingStatue_Head': 'KingStatue_HeadPivot', 'KingStatue_Eye_L': 'KingStatue_Head', 'KingStatue_Eye_R': 'KingStatue_Head'}
for n, p in need.items():
    o = O.get(n)
    check('statue: part ' + n, o is not None and (p is None or (o.parent and o.parent.name == p)), 'parent=' + (o.parent.name if o and o.parent else '-'))
meshes = [O[n] for n in ('KingStatue_Body', 'KingStatue_Head', 'KingStatue_Eye_L', 'KingStatue_Eye_R') if n in O]
lo, hi = world_bounds(meshes)
check('statue: height 3.6 +-0.1 m', abs(hi.z - lo.z - 3.6) <= .1, '%.4f m' % (hi.z - lo.z))
check('statue: bottom at z=0', abs(lo.z) < .002, 'min z %.4f' % lo.z)
total = sum(tris(o) for o in meshes)
check('statue: triangles <= 15000', total <= 15000, str({o.name: tris(o) for o in meshes}) + ' total %d' % total)
check('statue: triangles match stats.json', total == st['triangles_total'], '%d / %d' % (total, st['triangles_total']))
hp = O['KingStatue_HeadPivot'].matrix_world.translation
neck = Vector((-st['head_pivot_unity'][0], -st['head_pivot_unity'][2], st['head_pivot_unity'][1]))
check('statue: head pivot at the neck', (hp - neck).length < .005 and 2.4 < hp.z < 2.7, 'pivot %s' % [round(x, 4) for x in hp])
for n, m in (('KingStatue_Body', 'TH_Statue'), ('KingStatue_Head', 'TH_Statue'), ('KingStatue_Eye_L', 'TH_StatueEye'), ('KingStatue_Eye_R', 'TH_StatueEye')):
    mats = [s.material.name.split('.')[0] for s in O[n].material_slots if s.material]
    check('statue: material ' + n, mats == [m], str(mats))
ez = [O[n].matrix_world.translation.z for n in ('KingStatue_Eye_L', 'KingStatue_Eye_R')]
check('statue: eye height matches stats.json', all(abs(z - st['eye_height_m']) < .002 for z in ez), str([round(z, 4) for z in ez]))
for c in st['colliders']:
    cy = c['center_unity'][1]; sy = c['size_unity'][1]
    check('statue: collider ' + c['name'] + ' within 0..2.1 m', cy - sy / 2 > -.01 and cy + sy / 2 < 2.11, 'y %.3f..%.3f' % (cy - sy / 2, cy + sy / 2))

# head turn clearance (blend)
O = load_blend(HERE / 'Statue' / 'KingStatue.blend')
pivot = O['KingStatue_HeadPivot']; body = O['KingStatue_Body']
bt = bvh([body])
for deg in (0, 45, -45, 90, 135, 180, -90):
    pivot.rotation_euler = (0, 0, math.radians(deg)); bpy.context.view_layer.update()
    ov = bt.overlap(bvh([O['KingStatue_Head'], O['KingStatue_Eye_L'], O['KingStatue_Eye_R']]))
    check('statue: head turned %d deg does not intersect the body' % deg, not ov, '%d triangle pairs' % len(ov))
pivot.rotation_euler = (0, 0, 0)
for im in bpy.data.images:
    if im.size[0]: check('statue: texture %s <= 2048' % im.name, max(im.size) <= 2048, '%dx%d' % tuple(im.size))
check('statue: atlas files exist', all((HERE / 'Statue' / t).exists() for t in st['textures']), ', '.join(st['textures']))

# ---------------------------------------------------------------------------------------------- phantom
ph = STATS['GraveKeeper']
O = import_fbx(HERE / 'Phantom' / 'GraveKeeper.fbx')
need = {'GraveKeeper_Root': None, 'GraveKeeper_Body': 'GraveKeeper_Root', 'GraveKeeper_Void': 'GraveKeeper_Root', 'GraveKeeper_Eye_L': 'GraveKeeper_Root',
        'GraveKeeper_Eye_R': 'GraveKeeper_Root', 'GraveKeeper_LanternPivot': 'GraveKeeper_Root', 'GraveKeeper_LanternFrame': 'GraveKeeper_LanternPivot',
        'GraveKeeper_LanternGlow': 'GraveKeeper_LanternPivot'}
for n, p in need.items():
    o = O.get(n)
    check('phantom: part ' + n, o is not None and (p is None or (o.parent and o.parent.name == p)), 'parent=' + (o.parent.name if o and o.parent else '-'))
meshes = [O[n] for n in need if O.get(n) is not None and O[n].type == 'MESH']
total = sum(tris(o) for o in meshes)
check('phantom: triangles <= 4000', total <= 4000, str({o.name: tris(o) for o in meshes}) + ' total %d' % total)
lo, hi = world_bounds(meshes)
check('phantom: origin on the floor (nothing below z=0)', lo.z >= -.001, 'min z %.4f' % lo.z)
ez = [O[n].matrix_world.translation.z for n in ('GraveKeeper_Eye_L', 'GraveKeeper_Eye_R')]
check('phantom: eye centre height 1.50 +-0.01 m', all(abs(z - 1.5) <= .01 for z in ez), str([round(z, 4) for z in ez]))
eyc = [O[n].matrix_world.translation for n in ('GraveKeeper_Eye_L', 'GraveKeeper_Eye_R')]
check('phantom: eyes in front (-Y in Blender = +Z in Unity)', all(e.y < 0 for e in eyc), str([round(e.y, 3) for e in eyc]))
lp = O['GraveKeeper_LanternPivot'].matrix_world.translation
gl = O['GraveKeeper_LanternGlow']; gw = [gl.matrix_world @ v.co for v in gl.data.vertices]; gc = sum(gw, Vector()) / len(gw)
check('phantom: lantern 0.6-0.9 m in front, 1.6-1.9 m high', .6 <= -gc.y <= .9 and 1.6 <= gc.z <= 1.9, 'forward %.3f, height %.3f' % (-gc.y, gc.z))
check('phantom: lantern parts have their origin on the hang point',
      all((O[n].matrix_world.translation - lp).length < 1e-4 for n in ('GraveKeeper_LanternFrame', 'GraveKeeper_LanternGlow')), 'pivot %s' % [round(x, 3) for x in lp])
for n, m in (('GraveKeeper_Body', 'TH_PhantomBody'), ('GraveKeeper_Void', 'TH_PhantomVoid'), ('GraveKeeper_Eye_L', 'TH_PhantomEye'), ('GraveKeeper_Eye_R', 'TH_PhantomEye'),
             ('GraveKeeper_LanternGlow', 'TH_PhantomEye'), ('GraveKeeper_LanternFrame', 'TH_PhantomLantern')):
    mats = [s.material.name.split('.')[0] for s in O[n].material_slots if s.material]
    check('phantom: material ' + n, mats == [m], str(mats))
body = O['GraveKeeper_Body']
ca = body.data.color_attributes[0] if len(body.data.color_attributes) else None
check('phantom: body has vertex colours in the FBX', ca is not None, ca.name if ca else 'none')
if ca:
    al = [d.color[3] for d in ca.data]
    zs = {}
    me = body.data
    for li, d in enumerate(ca.data): zs.setdefault(round(d.color[3], 2), 0)
    low = [(body.matrix_world @ me.vertices[me.loops[i].vertex_index].co, a) for i, a in enumerate(al) if a < .05]
    check('phantom: vertex alpha reaches 0 (sleeve ends, cape edge, pole tail)', len(low) > 0 and max(al) > .99, '%d corners < 0.05, max %.2f' % (len(low), max(al)))
    sleeve = [c for c, a in low if abs(c.x) > .18 and c.z > .7]
    check('phantom: faded corners at the sleeve ends', len(sleeve) > 0, '%d' % len(sleeve))
    cape = [c for c, a in low if 1.05 < c.z < 1.25]
    check('phantom: faded corners on the hood / cape edge', len(cape) > 0, '%d' % len(cape))
check('phantom: stats.json eye height', abs(ph['eye_height_m'] - 1.5) < 1e-3, str(ph['eye_height_m']))

ok = all(r['ok'] for r in results)
(HERE / 'verification.json').write_text(json.dumps(dict(passed=ok, checks=results), indent=2, ensure_ascii=False), encoding='utf-8')
print('ALL PASSED' if ok else 'FAILED')
sys.exit(0 if ok else 1)
