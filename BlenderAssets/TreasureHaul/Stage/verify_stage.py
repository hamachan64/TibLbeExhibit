"""Reload the stage outputs and check them. Headless; run after the two checks:

  blender -b --factory-startup --python verify_stage.py
Writes Output/verification.json and fails (assert) on any problem.

- FBX (whole stage and every module): reimport, compare triangles / meshes, UVs, no degenerate faces, one material,
  inside the venue (x +-2.5, depth 0-11, height 0-2.52), the markers at their recorded positions.
- Blends: the runtime blend and the review blend load; same triangles; atlas images resolve to existing files.
- Budget: stage <= 150k triangles, stage + 23 preview treasures <= 300k.
- Clear width (real geometry): horizontal rays across each corridor every 0.1 m and across both doorways, at heights 0.1-1.8 m.
- Floating stones: every stone island of the wall modules rests on something (a ray down from its base reaches a
  mesh within 0.03 m), or is wedged (a lintel: rays sideways at both ends hit within 0.03 m).
- Layout: 30-36 slots, at least 10 that take M, half of them hidden-intent, every slot reachable, the 1.15 m loop, the search summary.
"""
import bpy, bmesh, json, math, runpy, sys
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree

HERE = Path(__file__).resolve().parent
OUT = HERE / 'Output'


def inspect(objs):
    meshes = [o for o in objs if o.type == 'MESH']
    for o in meshes: o.data.calc_loop_triangles()
    assert all(o.data.uv_layers for o in meshes), 'missing UVs'
    deg = sum(1 for o in meshes for t in o.data.loop_triangles if t.area < 1e-10)
    pts = [o.matrix_world @ v.co for o in meshes for v in o.data.vertices]
    lo = [min(p[i] for p in pts) for i in range(3)]; hi = [max(p[i] for p in pts) for i in range(3)]
    assert lo[0] >= -2.5001 and hi[0] <= 2.5001 and lo[1] >= -11.0001 and hi[1] <= .0001 and lo[2] >= -.0001 and hi[2] <= 2.52, (lo, hi)
    mats = {m.name.split('.')[0] for o in meshes for m in o.data.materials if m}
    return dict(triangles=sum(len(o.data.loop_triangles) for o in meshes), meshes=len(meshes), degenerate_triangles=deg,
                bounds_blender=[[round(v, 4) for v in lo], [round(v, 4) for v in hi]], materials=sorted(mats))


def clear_widths(scene):
    """Real clear width across the corridors (rays at several heights), ignoring the preview floor and treasures."""
    stage = [o for o in scene.objects if o.type == 'MESH' and o.get('module_id')]
    bm = bmesh.new()
    for o in stage:
        t = bmesh.new(); t.from_mesh(o.data); t.transform(o.matrix_world); me = bpy.data.meshes.new('t'); t.to_mesh(me); t.free(); bm.from_mesh(me); bpy.data.meshes.remove(me)
    tree = BVHTree.FromBMesh(bm); bm.free()
    out = {}
    # (name, fixed coordinate, axis of the rays, samples along the passage): corridors are measured across x,
    # doorways across d (between the jambs), at several heights
    lines = [('corridor_w', -1.68, 'x', [d / 10 for d in range(43, 80)]), ('corridor_e', 1.68, 'x', [d / 10 for d in range(43, 80)]),
             ('front_room_door', -.98, 'd', [5.2, 5.3, 5.4]), ('back_room_door', .98, 'd', [7.1, 7.2, 7.3])]
    for name, c, axis, ts in lines:
        worst = (9, None)
        for t in ts:
            for h in (.1, .5, .9, .97, 1.2, 1.5, 1.8):
                if axis == 'x': o = Vector((c, -t, h)); dirs = (Vector((1, 0, 0)), Vector((-1, 0, 0)))
                else: o = Vector((c, -t, h)); dirs = (Vector((0, 1, 0)), Vector((0, -1, 0)))
                ds = [tree.ray_cast(o, dv, 3)[3] for dv in dirs]
                if None in ds: continue
                if sum(ds) < worst[0]: worst = (sum(ds), [round(t, 2), h])
        out[name] = dict(min_clear_m=round(worst[0], 3), at_depth_height=worst[1])
    return out


def floating_stones(scene):
    walls = [o for o in scene.objects if o.type == 'MESH' and o.get('beam_blocking')]
    allm = [o for o in scene.objects if o.type == 'MESH' and o.get('module_id')]
    bm = bmesh.new()
    for o in allm:
        t = bmesh.new(); t.from_mesh(o.data); t.transform(o.matrix_world); me = bpy.data.meshes.new('t'); t.to_mesh(me); t.free(); bm.from_mesh(me); bpy.data.meshes.remove(me)
    tree = BVHTree.FromBMesh(bm); bm.free()
    bad = []; checked = 0
    for o in walls:
        b = bmesh.new(); b.from_mesh(o.data); b.verts.ensure_lookup_table()
        seen = set()
        for v in b.verts:
            if v.index in seen: continue
            comp = []; stack = [v]; seen.add(v.index)
            while stack:
                x = stack.pop(); comp.append(x)
                for e in x.link_edges:
                    y = e.other_vert(x)
                    if y.index not in seen: seen.add(y.index); stack.append(y)
            pts = [o.matrix_world @ x.co for x in comp]
            lo = Vector([min(p[i] for p in pts) for i in range(3)]); hi = Vector([max(p[i] for p in pts) for i in range(3)])
            size = hi - lo
            if lo.z < .005 or max(size.x, size.y) < .12 or min(size.x, size.y) < .06 or size.z < .1: continue   # on the floor, or a thin decal / lip / plate
            checked += 1
            c = (lo + hi) / 2; ok = False
            for fx, fy in ((0, 0), (-.3, 0), (.3, 0), (0, -.3), (0, .3)):
                p = Vector((c.x + fx * size.x, c.y + fy * size.y, lo.z + .004))
                hit = tree.ray_cast(p, Vector((0, 0, -1)), .035)
                if hit[0] is not None: ok = True; break
            if not ok:   # wedged lintel: both ends touch something sideways at mid height
                axis = Vector((1, 0, 0)) if size.x > size.y else Vector((0, 1, 0)); half = (size.x if size.x > size.y else size.y) / 2
                ends = [tree.ray_cast(c + axis * s * (half - .01), axis * s, .04)[0] is not None for s in (-1, 1)]
                ok = all(ends)
            if not ok: bad.append(dict(module=o.name, centre_unity=[round(c.x, 2), round(c.z, 2), round(-c.y, 2)], size=[round(v, 2) for v in size]))
        b.free()
    return dict(checked=checked, unsupported=bad)


def run():
    data = json.loads((OUT / 'stage_layout.json').read_text(encoding='utf-8'))
    argv = sys.argv; sys.argv = ['check_context_paths.py']
    try: paths = runpy.run_path(str(HERE / 'check_context_paths.py'), run_name='verify')['run']()
    finally: sys.argv = argv
    search = json.loads((OUT / 'search_visibility.json').read_text(encoding='utf-8'))['summary']
    result = dict(fbx=[], blend=[])
    for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
    for path, expected in [('ExcavationDepot_5x11.fbx', data['runtime_stats'])] + [('modules/%s.fbx' % m['name'], m) for m in data['module_stats']]:
        before = set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=str(OUT / path), use_anim=False)
        added = set(bpy.data.objects) - before; bpy.context.view_layer.update()
        got = inspect(added)
        assert got['triangles'] == expected['triangles'] and got['meshes'] == expected['meshes'], (path, got, expected)
        assert got['degenerate_triangles'] == 0 and len(got['materials']) == 1, (path, got)
        if path == 'ExcavationDepot_5x11.fbx':
            marks = {o.get('anchor_id'): o for o in added if o.get('anchor_id')}
            assert len(marks) == len(data['markers']), (len(marks), len(data['markers']))
            for m in data['markers']:
                assert (marks[m['name']].matrix_world.translation - Vector(m['position_blender_xyz'])).length < .001, m['name']
        result['fbx'].append(dict(path=path, **got))
        for o in added: bpy.data.objects.remove(o, do_unlink=True)
    for file in ('ExcavationDepot_5x11.blend', 'Stage_Review.blend'):
        with bpy.data.libraries.load(str(OUT / file), link=False) as (src, dst): dst.scenes = src.scenes
        sc = dst.scenes[0]
        stage = [o for o in sc.objects if o.type == 'MESH' and o.get('module_id')]
        got = inspect(stage); assert got['triangles'] == data['runtime_stats']['triangles'], (file, got)
        for o in stage:
            for m in o.data.materials:
                for n in m.node_tree.nodes:
                    if n.type == 'TEX_IMAGE': assert Path(bpy.path.abspath(n.image.filepath, library=n.image.library)).exists() or (OUT / 'textures' / Path(n.image.filepath).name).exists(), n.image.filepath
        entry = dict(path=file, **got)
        if file == 'Stage_Review.blend':
            entry['clear_widths'] = clear_widths(sc); entry['floating_stones'] = floating_stones(sc)
            assert sc.camera is not None
        result['blend'].append(entry)
    review = result['blend'][1]
    stage_tris = data['runtime_stats']['triangles']; combined = stage_tris + data['preview_treasure_stats']['triangles']
    assert stage_tris <= 150000 and combined <= 300000, (stage_tris, combined)
    for k, v in review['clear_widths'].items(): assert v['min_clear_m'] >= 1.15, (k, v)
    assert not review['floating_stones']['unsupported'], review['floating_stones']['unsupported'][:5]
    sl = data['slots']
    assert 30 <= sl['count'] <= 36 and sum(v for k, v in sl['by_class'].items() if 'M' in k) >= 10 and sl['hidden_intent'] * 2 == sl['count'], sl
    assert paths['slots_reachable'] == paths['slots_total'] and all(paths['main_loop_clear_1_15m'].values()), paths['main_loop_clear_1_15m']
    assert search['hidden_concealed'] == search['hidden_intent_placed'] and search['findable_standing'] == search['grabbable'] == search['placed'] == 23
    assert search['intersections'] == 0 and search['floor_placed'] == 0 and not search['not_resting'], search
    result.update(stage_triangles=stage_tris, combined_triangles=combined, modules=len(data['module_stats']), slots=sl['count'],
                  slots_reachable=paths['slots_reachable'], main_loop_clear_1_15m=paths['main_loop_clear_1_15m'], search=search,
                  unity_imported=False, pico_fps='not measured')
    (OUT / 'verification.json').write_text(json.dumps(result, indent=2, ensure_ascii=False), encoding='utf-8')
    print('PASS', json.dumps(dict(stage=stage_tris, combined=combined, fbx=len(result['fbx']), clear=review['clear_widths'],
                                  floating_checked=review['floating_stones']['checked']), ensure_ascii=False))


if __name__ == '__main__':
    run()
