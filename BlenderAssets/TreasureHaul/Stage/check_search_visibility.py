"""Hidden vs. visible finds, beam reach, light on each find, contact, on the review blend (not saved).

  blender -b Output/Stage_Review.blend --python check_search_visibility.py
Writes Output/search_visibility.json.

Viewpoints: walkable cells (0.10 m grid, 0.30 m human radius, 8-connected) reached from the practice row.
Sight: rays from the eye to vertices of the treasure body, against every stage mesh and the other treasures.
  entrance   eyes on the practice row area (depth 1.9-3.2 m) at 1.45 / 1.65 m
  concealed  (hidden-intent slots) at most 10 % of the body seen from any entrance eye
  findable   at least 30 % of the body seen standing (1.45 / 1.65 m) from a walkable point within 1.3 m
  far        share of walkable points >= 2.0 m away (eye 1.65 m) that see more than 10 %
Beam: a palm (0.8-1.2 m) at a walkable point within 1.3 m, at least 25 % of the body in a clear line through the
  beam-blocking meshes only (walls; furniture does not stop the beam), within 1.5 m.
Light (darkness proxy; the venue light falls on virtual objects too, the lamps are the warm accents):
  sky_open   share of 64 cosine-weighted upward rays from the find that leave without hitting the stage (3 m)
  lamp_lit   names of preview spot lamps whose cone reaches the find without occlusion
Contact: no treasure mesh intersects a stage mesh (0.0015 m lift), no find stands on the floor, and the find rests
  on its host (a downward ray from the base reaches a stage mesh within 0.01 m).
"""
import bpy, json, math, sys
from collections import deque
from pathlib import Path
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

HERE = Path(__file__).resolve().parent
OUT = HERE / 'Output'
GRID, HUMAN_R = .10, .30
STAND_EYES = (1.45, 1.65)
ENTRANCE_D = (1.9, 3.2)
NEAR_M, FAR_M, BEAM_M = 1.30, 2.00, 1.50


def load():
    data = json.loads((OUT / 'stage_layout.json').read_text(encoding='utf-8'))
    return data, [m for m in data['markers'] if m['role'] == 'treasure_slot']


def walkable(data):
    obs = []
    for c in data['colliders']:
        f = c['footprint_logical']
        if f['h1'] < .05 or f['h0'] > 1.8: continue
        obs.append((f['center_xd'], f['size_xd'], math.cos(f['angle_rad']), math.sin(f['angle_rad'])))

    def clear(x, d):
        for (cx, cd), (w, l), ca, sa in obs:
            dx, dd = x - cx, d - cd; lx, ld = dx * ca + dd * sa, -dx * sa + dd * ca
            if math.hypot(max(abs(lx) - w / 2, 0), max(abs(ld) - l / 2, 0)) < HUMAN_R - 1e-6: return False
        return True
    ni, nj = 50, 110
    valid = {(i, j) for i in range(ni + 1) for j in range(nj + 1)
             if 2.5 - abs(i * GRID - 2.5) >= HUMAN_R and HUMAN_R <= j * GRID <= 11 - HUMAN_R and clear(i * GRID - 2.5, j * GRID)}
    start = min(valid, key=lambda n: math.hypot(n[0] * GRID - 2.5, n[1] * GRID - 2.6))
    seen = {start}; q = deque([start])
    while q:
        i, j = q.popleft()
        for di in (-1, 0, 1):
            for dj in (-1, 0, 1):
                n = (i + di, j + dj)
                if n in valid and n not in seen: seen.add(n); q.append(n)
    return [(i * GRID - 2.5, j * GRID) for i, j in seen]


def tree_of(objs, lift=0.0):
    import bmesh
    bm = bmesh.new()
    dg = bpy.context.evaluated_depsgraph_get()
    for o in objs:
        t = bmesh.new(); t.from_object(o, dg); t.transform(Matrix.Translation((0, 0, lift)) @ o.matrix_world)
        me = bpy.data.meshes.new('tmp'); t.to_mesh(me); t.free(); bm.from_mesh(me); bpy.data.meshes.remove(me)
    tr = BVHTree.FromBMesh(bm); bm.free()
    return tr


def run():
    data, slots = load()
    scene = bpy.context.scene
    for o in scene.objects:
        if 'PREVIEW_RealFloor' in o.name: o.hide_viewport = True; o.hide_render = True
    stage = [o for o in scene.objects if o.type == 'MESH' and o.get('module_id')]
    walls = [o for o in stage if o.get('beam_blocking')]
    roots = {o['slot']: o for o in scene.objects if o.type == 'EMPTY' and o.get('slot')}
    groups = {k: [c for c in r.children_recursive if c.type == 'MESH'] for k, r in roots.items()}
    all_t = [m for g in groups.values() for m in g]
    sight = tree_of(stage + all_t); beam_tree = tree_of(walls); stage_tree = tree_of(stage)
    own_trees = {k: tree_of(g) for k, g in groups.items()}
    walk = walkable(data)
    lamps = [o for o in scene.objects if o.type == 'LIGHT' and o.data.type == 'SPOT']

    def samples(meshes, limit=90):
        pts = []
        for o in meshes:
            vs = o.data.vertices; step = max(1, len(vs) // limit)
            pts += [o.matrix_world @ vs[i].co for i in range(0, len(vs), step)]
        return pts

    def frac(tree_all, own, eye, pts):
        """Share of points whose first hit from the eye is the treasure itself (or nothing)."""
        if not pts: return 0.0
        hit = 0
        for p in pts:
            v = p - eye; dist = v.length
            loc, nrm, idx, dd = tree_all.ray_cast(eye, v.normalized(), dist + .002)
            if loc is None or dd >= dist - .004: hit += 1
            else:
                l2 = own.ray_cast(eye, v.normalized(), dist + .002)[0]
                if l2 is not None and (l2 - eye).length <= dd + 1e-4: hit += 1
        return hit / len(pts)

    report = []
    dirs = []
    for i in range(64):
        u = (i + .5) / 64; r = math.sqrt(u); ph = i * 2.39996
        dirs.append(Vector((r * math.cos(ph), r * math.sin(ph), math.sqrt(1 - u))))
    for s in slots:
        name = s['name']
        if name not in groups: continue   # slot not used by the preview
        meshes = groups[name]; own = own_trees[name]; pts = samples(meshes)
        sx, sh, sd = s['position_unity_xyz']
        bb = [m.matrix_world @ Vector(c) for m in meshes for c in m.bound_box]
        top = max(p.z for p in bb); low = min(p.z for p in bb)
        centre = Vector(((min(p.x for p in bb) + max(p.x for p in bb)) / 2, (min(p.y for p in bb) + max(p.y for p in bb)) / 2, (low + top) / 2))
        ent = [p for p in walk if ENTRANCE_D[0] <= p[1] <= ENTRANCE_D[1]]
        ent_best = max((frac(sight, own, Vector((x, -d, h)), pts) for x, d in ent[::2] for h in STAND_EYES), default=0)
        near = [p for p in walk if math.hypot(p[0] - sx, p[1] - sd) <= NEAR_M]
        best = None
        for x, d in near:
            for h in STAND_EYES:
                f = frac(sight, own, Vector((x, -d, h)), pts)
                if best is None or f > best['visible'] + 1e-9: best = dict(visible=round(f, 3), eye_unity_xyz=[round(x, 2), h, round(d, 2)])
        far = [p for p in walk if math.hypot(p[0] - sx, p[1] - sd) >= FAR_M][::5]
        far_seen = sum(1 for x, d in far if frac(sight, own, Vector((x, -d, 1.65)), pts) > .10)
        grab = None
        for x, d in sorted(near, key=lambda p: math.hypot(p[0] - sx, p[1] - sd)):
            for h in (.8, 1.0, 1.2):
                palm = Vector((x, -d, h))
                if min((p - palm).length for p in pts) > BEAM_M: continue
                clear = sum(1 for p in pts if beam_tree.ray_cast(palm, (p - palm).normalized(), (p - palm).length - .003)[0] is None) / len(pts)
                if clear >= .25: grab = dict(palm_unity_xyz=[round(x, 2), h, round(d, 2)], clear=round(clear, 3)); break
            if grab: break
        origin = centre + Vector((0, 0, .02))
        opened = sum(1 for dv in dirs if stage_tree.ray_cast(origin, dv, 3.0)[0] is None) / len(dirs)
        lit = []
        for L in lamps:
            v = centre - L.matrix_world.translation; axis = L.matrix_world.to_quaternion() @ Vector((0, 0, -1))
            if v.length < 4 and v.normalized().dot(axis) > math.cos(L.data.spot_size / 2) and \
                    stage_tree.ray_cast(L.matrix_world.translation, v.normalized(), v.length - .05)[0] is None:
                lit.append(L.name.replace('THStage_PREVIEW_', ''))
        # contact
        inter = [o.name for o in stage if tree_of(meshes, .0015).overlap(tree_of([o]))]
        base = Vector((centre.x, centre.y, low + .001))
        hitd = stage_tree.ray_cast(base, Vector((0, 0, -1)), .2)
        gap = None if hitd[0] is None else round(hitd[3] - .001, 4)
        report.append(dict(slot=name, zone=s['zone'], host=s['host_type'], hidden_intent=s['hidden_intent'], model=roots[name].name.replace('_Root', ''),
                           entrance_max_visible=round(ent_best, 3), concealed_from_entrance=ent_best <= .10,
                           best_near_view=best, findable_standing=bool(best and best['visible'] >= .30),
                           far_viewpoints_seeing='%d/%d' % (far_seen, len(far)), far_seen_ratio=round(far_seen / max(1, len(far)), 3),
                           beam_grab=grab, grabbable=grab is not None, sky_open=round(opened, 3), lamp_lit=lit,
                           intersects=inter, base_height_m=round(low, 4), rest_gap_m=gap))
    hidden = [r for r in report if r['hidden_intent']]
    summary = dict(placed=len(report), hidden_intent_placed=len(hidden),
                   hidden_concealed=sum(r['concealed_from_entrance'] for r in hidden),
                   visible_cues_seen_from_entrance=sum(not r['concealed_from_entrance'] for r in report if not r['hidden_intent']),
                   findable_standing=sum(r['findable_standing'] for r in report), grabbable=sum(r['grabbable'] for r in report),
                   intersections=sum(1 for r in report if r['intersects']), floor_placed=sum(1 for r in report if r['base_height_m'] < .05),
                   not_resting=[r['slot'] for r in report if r['rest_gap_m'] is None or r['rest_gap_m'] > .01],
                   darkest_sky_open=min(r['sky_open'] for r in report), unlit_by_lamps=[r['slot'] for r in report if not r['lamp_lit']],
                   walkable_points=len(walk))
    result = dict(summary=summary, method=__doc__.strip().splitlines()[3:], slots=report)
    (OUT / 'search_visibility.json').write_text(json.dumps(result, indent=2, ensure_ascii=False), encoding='utf-8')
    print('SUMMARY', json.dumps(summary, ensure_ascii=False))
    for r in report:
        print(r['slot'], r['zone'][:10].ljust(10), r['host'][:8].ljust(8), 'H' if r['hidden_intent'] else '-', 'ent', r['entrance_max_visible'],
              'near', r['best_near_view'] and r['best_near_view']['visible'], 'far', r['far_viewpoints_seeing'], 'grab', r['grabbable'],
              'sky', r['sky_open'], 'lamps', len(r['lamp_lit']), 'gap', r['rest_gap_m'], 'X' if r['intersects'] else '')


run()
