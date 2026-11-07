"""Check the cargo board against the spec (.claude/loops/20261107-th-cargo-board.md, A. model). Writes verification.json;
exit code 1 if anything fails.

  blender -b --factory-startup --python verify_board.py

FBX (re-imported): part names and parents, identity rotation / scale on every node, triangles <= 3000 (= stats.json),
materials <= 3 with the expected names, UVs on every mesh.
blend: box outside size about 1.9 x 0.65 x 0.12 m, model on the floor (y = 0), screen centre on the ScoreBoardAnchor
(VenueRoot (0, 1.5, 0.32)) once the root stands at stats.json root_venue_unity.
Stage (../Stage/Output/ExcavationDepot_5x11.blend + stage_layout.json): no triangle of the model intersects the delivery bay
machine (THStage_ExtractionBay) or the entry camp, no vertex is inside a stage collider or the CollectionVolume, the whole
model stays inside the venue (0 <= z, |x| <= 2.5), the practice spots (z = 2.5) are clear. Both x mirrorings are tested
(stage_layout.json maps Unity to Blender as (X, -Z, Y), the model FBX as (-x, z, -y); the board is near symmetric anyway).
Textures: <= 1024, MetallicSmoothness has an alpha channel.
"""
import bpy, json, math, sys
from pathlib import Path
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

HERE = Path(__file__).resolve().parent
STAGE_DIR = HERE.parent / 'Stage' / 'Output'
ST = json.loads((HERE / 'stats.json').read_text(encoding='utf-8'))
LAYOUT = json.loads((STAGE_DIR / 'stage_layout.json').read_text(encoding='utf-8'))
results = []


def check(name, ok, detail=''):
    results.append(dict(check=name, ok=bool(ok), detail=detail)); print(('PASS ' if ok else 'FAIL ') + name, detail)


def tris(o):
    o.data.calc_loop_triangles(); return len(o.data.loop_triangles)


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


# ---------------------------------------------------------------------------------------------- FBX
reset()
bpy.ops.import_scene.fbx(filepath=str(HERE / 'CargoBoard.fbx'), axis_forward='Y', axis_up='Z')
O = {o.name: o for o in bpy.data.objects}
need = {'CargoBoard_Root': None, 'CargoBoard_Body': 'CargoBoard_Root', 'CargoBoard_Screen': 'CargoBoard_Root',
        'CargoBoard_Lamp': 'CargoBoard_Root', 'CargoBoard_DigitsAnchor': 'CargoBoard_Root', 'CargoBoard_LabelAnchor': 'CargoBoard_Root'}
for n, p in need.items():
    o = O.get(n)
    check('fbx: node ' + n, o is not None and (p is None or (o.parent and o.parent.name == p)), 'parent=' + (o.parent.name if o and o.parent else '-'))
bad = [o.name for o in O.values() if o.rotation_euler.to_quaternion().angle > 1e-5 or any(abs(v - 1) > 1e-5 for v in o.scale)]
check('fbx: every node rotation 0 / scale 1 (Bake Axis Conversion gives rotation 0 in Unity)', not bad, str(bad))
meshes = [O[n] for n in ('CargoBoard_Body', 'CargoBoard_Screen', 'CargoBoard_Lamp') if n in O]
tc = {o.name: tris(o) for o in meshes}; total = sum(tc.values())
check('fbx: triangles <= 3000', total <= 3000, '%s total %d' % (tc, total))
check('fbx: triangles match stats.json', total == ST['triangles_total'], '%d / %d' % (total, ST['triangles_total']))
mats = sorted({m.name.split('.')[0] for o in meshes for m in o.data.materials if m})
check('fbx: materials <= 3 (atlas, screen, lamp)', mats == ['TH_CargoBoard', 'TH_CargoBoardLamp', 'TH_CargoBoardScreen'], str(mats))
check('fbx: one material per mesh', all(len(o.data.materials) == 1 for o in meshes), str({o.name: len(o.data.materials) for o in meshes}))
check('fbx: every mesh has UVs', all(len(o.data.uv_layers) > 0 for o in meshes))
# FBX file is Z-up with the model turned 180 deg: the screen faces +Y in the file (= Unity +Z after Bake Axis Conversion)
scr = O['CargoBoard_Screen']; n_scr = (scr.matrix_world.to_3x3() @ scr.data.polygons[0].normal).normalized()
check('fbx: screen faces +Y in the file (Unity front +Z)', n_scr.y > .99, 'normal %s' % str(tuple(round(v, 3) for v in n_scr)))
loc = scr.matrix_world.translation
off_fbx = [round(loc.x, 4), round(loc.z, 4), round(loc.y, 4)]          # file (x', y', z') -> Unity (x', z', y')
check('fbx: screen centre offset (Unity) = stats.json', all(abs(off_fbx[i] - ST['screen']['offset_root_to_screen_centre_unity'][i]) < 1e-3 for i in range(3)),
      '%s vs %s' % (off_fbx, ST['screen']['offset_root_to_screen_centre_unity']))

# ---------------------------------------------------------------------------------------------- blend
reset()
with bpy.data.libraries.load(str(HERE / 'CargoBoard.blend'), link=False) as (src, dst): dst.objects = list(src.objects)
for o in dst.objects: bpy.context.scene.collection.objects.link(o)
bpy.context.view_layer.update()
B = {o.name: o for o in dst.objects}
body = B['CargoBoard_Body']
W = [body.matrix_world @ v.co for v in body.data.vertices]
allv = W + [B[n].matrix_world @ v.co for n in ('CargoBoard_Screen', 'CargoBoard_Lamp') for v in B[n].data.vertices]
boxv = [p for p in W if 1.2 < p.z < 1.9 and abs(p.x) < 1.0 and -.27 < p.y < -.10]
bw = max(p.x for p in boxv) - min(p.x for p in boxv); bh = max(p.z for p in boxv) - min(p.z for p in boxv)
shell = [p for p in boxv if abs(p.x) > .935]          # the side channels and rail ends: the steel shell without bolts / plate
bd = max(p.y for p in shell) - min(p.y for p in shell)
check('blend: box outside about 1.9 x 0.65 x 0.12 m (frame rails included)', abs(bw - 1.9) < .05 and abs(bh - .65) < .03 and abs(bd - .12) < .025,
      'w %.3f h %.3f d %.3f (body without the rails 1.90 x 0.59 x 0.12)' % (bw, bh, bd))
check('blend: model stands on the floor (min z = 0)', abs(min(p.z for p in allv)) < .002, 'min z %.4f' % min(p.z for p in allv))
sc = B['CargoBoard_Screen'].matrix_world.translation; rv = ST['root_venue_unity']
venue_scr = [round(-sc.x + rv[0], 4), round(sc.z + rv[1], 4), round(-sc.y + rv[2], 4)]
anchor = next(m for m in LAYOUT['markers'] if m['name'] == 'ScoreBoardAnchor')['position_unity_xyz']
check('blend: screen centre on ScoreBoardAnchor (VenueRoot local)', all(abs(venue_scr[i] - anchor[i]) < 1e-3 for i in range(3)), '%s vs %s' % (venue_scr, anchor))
sw, sh = ST['screen']['size_unity_wh']; dw, dh = ST['digits_area']['size_unity_wh']
check('blend: digits area fits inside the screen', dw < sw and dh < sh, 'digits %.3f x %.3f in screen %.3f x %.3f' % (dw, dh, sw, sh))
check('blend: no colliders in the model', not any(o.name.lower().startswith('collider') for o in dst.objects))

# ---------------------------------------------------------------------------------------------- stage clearance
model_tris = []
for o in (body, B['CargoBoard_Screen'], B['CargoBoard_Lamp']):
    o.data.calc_loop_triangles(); base = len(model_tris)
    vs = [o.matrix_world @ v.co for v in o.data.vertices]
    model_tris.append((vs, [tuple(t.vertices) for t in o.data.loop_triangles]))
with bpy.data.libraries.load(str(STAGE_DIR / 'ExcavationDepot_5x11.blend'), link=False) as (src, dst2):
    dst2.objects = [n for n in src.objects if n in ('THStage_ExtractionBay', 'THStage_EntryCamp')]
for o in dst2.objects: bpy.context.scene.collection.objects.link(o)
bpy.context.view_layer.update()


def stage_bvh(o):
    o.data.calc_loop_triangles()
    return BVHTree.FromPolygons([o.matrix_world @ v.co for v in o.data.vertices], [tuple(t.vertices) for t in o.data.loop_triangles])


bay = next(o for o in dst2.objects if o.name.startswith('THStage_ExtractionBay')); camp = next(o for o in dst2.objects if o.name.startswith('THStage_EntryCamp'))
T_bay, T_camp = stage_bvh(bay), stage_bvh(camp)
clear = {}
for mirror in (1, -1):
    # model .blend (x, y, z) -> Unity (-x, z, -y) + root -> stage blend (X, -Z, Y); mirror=-1 is the literal mapping
    def to_stage(p):
        ux, uy, uz = -p.x + rv[0], p.z + rv[1], -p.y + rv[2]
        return Vector((-ux if mirror == 1 else ux, -uz, uy))
    verts, polys = [], []
    for vs, ts in model_tris:
        b0 = len(verts); verts += [to_stage(p) for p in vs]; polys += [tuple(b0 + i for i in t) for t in ts]
    T_m = BVHTree.FromPolygons(verts, polys)
    hits_bay = len(T_m.overlap(T_bay)); hits_camp = len(T_m.overlap(T_camp))
    low = [v for v in verts if v.z < 1.0]          # gantry feet, braces, base plates, cable
    dmin = min(T_bay.find_nearest(v)[3] for v in low)
    allmin = min(T_bay.find_nearest(v)[3] for v in verts)
    tag = 'as placed' if mirror == 1 else 'x mirrored'
    check('stage (%s): no intersection with THStage_ExtractionBay' % tag, hits_bay == 0, '%d triangle pairs' % hits_bay)
    check('stage (%s): no intersection with THStage_EntryCamp' % tag, hits_camp == 0, '%d triangle pairs' % hits_camp)
    clear[tag] = dict(min_gap_gantry_feet_to_bay_m=round(dmin, 4), min_gap_any_to_bay_m=round(allmin, 4))
    # Unity-space boxes
    U = [Vector((-v.x if mirror == 1 else v.x, v.z, -v.y)) for v in verts]       # back to Unity VenueRoot local
    inside = []
    for c in LAYOUT['colliders']:
        cc, ss = Vector(c['center_unity_xyz']), Vector(c['size_unity_xyz'])
        if cc.z - ss.z / 2 > 3.3: continue
        n_in = sum(1 for u in U if all(abs(u[i] - cc[i]) < ss[i] / 2 for i in range(3)))
        if n_in: inside.append((c['name'], n_in))
    check('stage (%s): no vertex inside a stage collider of the entrance zone' % tag, not inside, str(inside))
    cv = next(m for m in LAYOUT['markers'] if m['name'] == 'CollectionVolume')
    cc, ss = Vector(cv['position_unity_xyz']), Vector(cv['size'])
    n_in = sum(1 for u in U if all(abs(u[i] - cc[i]) < ss[i] / 2 for i in range(3)))
    check('stage (%s): nothing inside the CollectionVolume (2.3 x 0.92 x 1.56 at (0, 0.49, 1.2))' % tag, n_in == 0, '%d vertices' % n_in)
    zmin = min(u.z for u in U); zmax = max(u.z for u in U); xmax = max(abs(u.x) for u in U)
    check('stage (%s): inside the venue (z >= 0, |x| <= 2.5)' % tag, zmin >= -1e-4 and xmax <= 2.5, 'z %.4f..%.4f, |x| max %.3f' % (zmin, zmax, xmax))
    tut = [Vector(m['position_unity_xyz']) for m in LAYOUT['markers'] if m['name'].startswith('Tutorial_')]
    dz = min(t.z for t in tut) - zmax
    check('stage (%s): practice spots (z = 2.5) at least 2 m in front of the whole model' % tag, dz >= 2.0, '%.3f m' % dz)
    clear[tag]['model_z_range_venue'] = [round(zmin, 4), round(zmax, 4)]
    clear[tag]['gap_model_to_practice_line_m'] = round(dz, 4)
# post feet against the back of the bay (Unity z): bay mesh starts at z = 0.18
posts_back = ST['gantry']
bay_z0 = min(-(bay.matrix_world @ v.co).y for v in bay.data.vertices)
foot_z1 = max(-(v.y) + rv[2] for v in W if v.z < .05)
check('stage: gantry feet end before the bay starts (Unity z)', foot_z1 < bay_z0, 'feet up to z %.4f, bay from z %.4f (gap %.4f m)' % (foot_z1, bay_z0, bay_z0 - foot_z1))
clear['feet_max_z_venue'] = round(foot_z1, 4); clear['bay_min_z_venue'] = round(bay_z0, 4)
board_bottom = min(p.z for p in W if abs(p.x) < 1.0 and -.27 < p.y < -.10)
bay_top_under = max((bay.matrix_world @ v.co).z for v in bay.data.vertices if abs((bay.matrix_world @ v.co).x) < 1.0 and -(bay.matrix_world @ v.co).y < .6)
check('stage: board bottom above the back of the bay', board_bottom > bay_top_under + .3, 'board bottom %.3f m, bay back top %.3f m' % (board_bottom, bay_top_under))
clear['board_bottom_y'] = round(board_bottom, 4); clear['bay_back_top_y'] = round(bay_top_under, 4)

# ---------------------------------------------------------------------------------------------- textures
for name in ('CargoBoard_BaseColor.png', 'CargoBoard_MetallicSmoothness.png'):
    im = bpy.data.images.load(str(HERE / 'textures' / name))
    check('texture %s <= 1024' % name, max(im.size) <= 1024, '%dx%d' % tuple(im.size))
    if 'Metallic' in name: check('texture %s has alpha (smoothness)' % name, im.channels == 4, '%d channels' % im.channels)

ok = all(r['ok'] for r in results)
(HERE / 'verification.json').write_text(json.dumps(dict(ok=ok, checks=results, clearance=clear), indent=2, ensure_ascii=False), encoding='utf-8')
print('ALL OK' if ok else 'SOME CHECKS FAILED')
sys.exit(0 if ok else 1)
