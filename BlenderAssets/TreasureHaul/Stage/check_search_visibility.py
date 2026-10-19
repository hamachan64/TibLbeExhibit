"""Measure whether treasures are hidden from the entrance yet findable up close.

Run headless on a review blend (does not modify or save it):
  blender -b Revision6/Stage_Review.blend --python check_search_visibility.py -- Revision6

Line of sight is ray cast against every stage mesh and the other treasures.
Sample points are the vertices of each treasure's body and of its glowing grip.
Viewpoints are walkable positions of a 0.30 m radius human proxy that can be
reached from the start area, using the exported collider envelopes.
"""
import bpy, json, math, sys
from collections import deque
from pathlib import Path
from mathutils import Vector

HERE = Path(__file__).resolve().parent
ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else ['Revision6']
OUT = HERE / ARGS[0]
GRID = .10
HUMAN_R = .30
STAND_EYES = (1.45, 1.65)        # shorter adult / typical adult
PEEK_EYES = (1.65, 1.45, 1.20, 1.00)  # standing first; crouch/lean only if needed (ties keep the higher eye)
ENTRANCE_D = (1.9, 3.2)          # practice row in front of the lift
NEAR_M = 1.30                    # within one step of the find
FAR_M = 2.00
BEAM_M = 1.50                    # planned beam reach (plan 5.2)
GRIP_WORDS = ('Grip', 'Handle')


def load():
    data = json.loads((OUT / 'stage_layout.json').read_text(encoding='utf-8'))
    return data, [m for m in data['markers'] if m['role'] == 'treasure_candidate']


def walkable(data):
    obs = [c for c in data['colliders'] if c['name'] != 'VenueFloor']
    half_w = data['venue']['width_m'] / 2 if 'width_m' in data.get('venue', {}) else 2.5
    depth = data['venue'].get('depth_m', 11) if isinstance(data.get('venue'), dict) else 11

    def clear(x, d, c):
        cx, _, cd = c['center_unity_xyz']; w, _, dd = c['size_unity_xyz']
        return math.hypot(max(abs(x - cx) - w / 2, 0), max(abs(d - cd) - dd / 2, 0)) >= HUMAN_R - 1e-6

    ni, nj = int(round(2 * half_w / GRID)), int(round(depth / GRID))
    valid = {(i, j) for i in range(ni + 1) for j in range(nj + 1)
             if abs(i * GRID - half_w) + HUMAN_R <= half_w and HUMAN_R <= j * GRID <= depth - HUMAN_R
             and all(clear(i * GRID - half_w, j * GRID, c) for c in obs)}
    start = min(valid, key=lambda n: math.hypot(n[0] * GRID - half_w, n[1] * GRID - 2.5))
    seen = {start}; q = deque([start])
    while q:
        i, j = q.popleft()
        for n in ((i + 1, j), (i - 1, j), (i, j + 1), (i, j - 1)):
            if n in valid and n not in seen: seen.add(n); q.append(n)
    return [(i * GRID - half_w, j * GRID) for i, j in seen]


def treasure_groups(slots):
    roots = [o for o in bpy.data.objects if o.type == 'EMPTY' and o.parent is None and o.get('preview_only')
             and not o.name.startswith('TH_Cart')]
    groups = {}
    for slot in slots:
        p = Vector(slot['position_blender_xyz'])
        root = min(roots, key=lambda r: (r.matrix_world.translation - p).length)
        assert (root.matrix_world.translation - p).length < .02, (slot['name'], root.name)
        meshes = [o for o in root.children_recursive if o.type == 'MESH']
        groups[slot['name']] = meshes
    return groups


def samples(obj, limit):
    m = obj.matrix_world; verts = obj.data.vertices
    step = max(1, len(verts) // limit)
    return [m @ verts[i].co for i in range(0, len(verts), step)]


def visible_fraction(scene, dg, eye, points, own):
    if not points: return 0.0
    hit = 0
    for p in points:
        v = p - eye; dist = v.length
        ok, loc, _, _, obj, _ = scene.ray_cast(dg, eye, v.normalized(), distance=dist - .004)
        if not ok or obj in own: hit += 1
    return hit / len(points)


def bvh(obj, lift=0.0):
    from mathutils.bvhtree import BVHTree
    from mathutils import Matrix
    import bmesh
    bm = bmesh.new(); bm.from_object(obj, bpy.context.evaluated_depsgraph_get())
    bm.transform(Matrix.Translation((0, 0, lift)) @ obj.matrix_world)
    tree = BVHTree.FromBMesh(bm); bm.free()
    return tree


def contact_checks(slots, groups):
    """Treasure meshes must not intersect stage meshes; support envelopes must not overlap walls or each other."""
    data, _ = load()
    stage = [o for o in bpy.context.scene.objects if o.type == 'MESH' and o.name.startswith('THStage_') and 'PreviewFloor' not in o.name
             and 'GroundFragments' not in o.name]
    stage_trees = {o.name: bvh(o) for o in stage}
    inter = []
    for slot in slots:
        for t in groups[slot['name']]:
            tt = bvh(t, lift=.0015)  # resting contact on the support top is not penetration
            for name, st in stage_trees.items():
                if tt.overlap(st): inter.append(dict(slot=slot['name'], treasure_mesh=t.name, stage_mesh=name))
    cols = [c for c in data['colliders'] if c['name'] != 'VenueFloor']
    overlaps = []; stacked = []
    for i, a in enumerate(cols):
        for b in cols[i + 1:]:
            pen = [min(a['center_unity_xyz'][k] + a['size_unity_xyz'][k] / 2, b['center_unity_xyz'][k] + b['size_unity_xyz'][k] / 2)
                   - max(a['center_unity_xyz'][k] - a['size_unity_xyz'][k] / 2, b['center_unity_xyz'][k] - b['size_unity_xyz'][k] / 2) for k in range(3)]
            if min(pen) > .002 and ('FindSupport' in a['name'] or 'FindSupport' in b['name']):
                other = b if 'FindSupport' in a['name'] else a
                # Supports deliberately stand on the 0.16 m altar foundations (low partitions).
                if 'FindSupport' not in other['name'] and other['size_unity_xyz'][1] <= .17:
                    stacked.append(dict(support=a['name'] if other is b else b['name'], foundation=other['name']))
                else:
                    overlaps.append(dict(a=a['name'], b=b['name'], penetration_m=round(min(pen), 4)))
    floor = []
    for slot in slots:
        low = min((t.matrix_world @ Vector(c)).z for t in groups[slot['name']] for c in t.bound_box)
        if low < .05: floor.append(dict(slot=slot['name'], lowest_m=round(low, 4)))
    return dict(treasure_intersections=inter, envelope_overlaps=overlaps, floor_placed=floor, stacked_on_altar_foundation=stacked)


def run():
    data, slots = load()
    scene = bpy.context.scene
    floor = [o for o in scene.objects if 'PreviewFloor' in o.name]
    for o in floor: o.hide_viewport = True; o.hide_render = True  # floor never occludes; samples sit above it
    dg = bpy.context.evaluated_depsgraph_get()
    walk = walkable(data)
    groups = treasure_groups(slots)
    report = []
    for slot in slots:
        meshes = groups[slot['name']]; own = set(meshes)
        grip = [p for o in meshes if any(w in o.name for w in GRIP_WORDS) for p in samples(o, 40)]
        body = [p for o in meshes if not any(w in o.name for w in GRIP_WORDS) for p in samples(o, 80)]
        x, _, d = slot['position_unity_xyz']

        def view(px, pd, h):
            eye = Vector((px, -pd, h))
            return visible_fraction(scene, dg, eye, body, own), visible_fraction(scene, dg, eye, grip, own)

        entrance = [p for p in walk if ENTRANCE_D[0] <= p[1] <= ENTRANCE_D[1]]
        ent_best = (0.0, 0.0)
        for px, pd in entrance[::3]:
            for h in STAND_EYES:
                b, g = view(px, pd, h); ent_best = (max(ent_best[0], b), max(ent_best[1], g))
        far = [p for p in walk if math.hypot(p[0] - x, p[1] - d) >= FAR_M][::6]
        far_seen = sum(1 for px, pd in far if any(max(view(px, pd, h)) > .10 for h in (1.65,)))
        near = [p for p in walk if math.hypot(p[0] - x, p[1] - d) <= NEAR_M]
        best_near = None
        for px, pd in near:
            for h in PEEK_EYES:
                b, g = view(px, pd, h)
                score = g + b
                if best_near is None or score > best_near['score']:
                    best_near = dict(score=score, eye_unity_xyz=[round(px, 2), h, round(pd, 2)],
                                     body_visible=round(b, 3), grip_visible=round(g, 3))
        # Beam grab: a palm at waist/chest height must have a clear line to the grip.
        grab = None
        for px, pd in sorted(near, key=lambda p: math.hypot(p[0] - x, p[1] - d)):
            for h in (.8, 1.0, 1.2):
                palm = Vector((px, -pd, h))
                if grip and min((g - palm).length for g in grip) <= BEAM_M:
                    f = visible_fraction(scene, dg, palm, grip, own)
                    if f >= .25: grab = dict(palm_unity_xyz=[round(px, 2), h, round(pd, 2)], grip_clear=round(f, 3)); break
            if grab: break
        standing = max((view(px, pd, h)[1] for px, pd in near for h in STAND_EYES), default=0)
        concealed = ent_best[1] == 0 and ent_best[0] <= .10
        discoverable = best_near is not None and best_near['grip_visible'] >= .30
        report.append(dict(slot=slot['name'], model=slot.get('model'), hidden_intent=slot.get('hidden_intent'),
                           placement_kind=slot.get('placement_kind'),
                           entrance_max_body_visible=round(ent_best[0], 3), entrance_max_grip_visible=round(ent_best[1], 3),
                           far_viewpoints_seeing=f'{far_seen}/{len(far)}', far_seen_ratio=round(far_seen / max(1, len(far)), 3),
                           concealed_from_entrance=concealed, best_near_view=best_near, discoverable_up_close=discoverable,
                           standing_near_max_grip_visible=round(standing, 3), discoverable_without_crouching=standing >= .30,
                           beam_grab=grab, grabbable=grab is not None))
    contact = contact_checks(slots, groups)
    hidden = [r for r in report if r['hidden_intent']]
    summary = dict(
        hidden_intent=len(hidden),
        hidden_concealed_from_entrance=sum(r['concealed_from_entrance'] for r in hidden),
        hidden_concealed_and_discoverable=sum(r['concealed_from_entrance'] and r['discoverable_up_close'] for r in hidden),
        all_discoverable=sum(r['discoverable_up_close'] for r in report), all_grabbable=sum(r['grabbable'] for r in report),
        discoverable_without_crouching=sum(r['discoverable_without_crouching'] for r in report),
        treasure_mesh_intersections=len(contact['treasure_intersections']), support_envelope_overlaps=len(contact['envelope_overlaps']),
        floor_placed=len(contact['floor_placed']),
        total=len(report),
        exposed_visible_from_entrance=sum(not r['concealed_from_entrance'] for r in report if not r['hidden_intent']),
        walkable_points=len(walk))
    result = dict(summary=summary, method=dict(
        viewpoints='Walkable cells (0.10 m grid, 0.30 m human radius) connected to the practice area; entrance = depth %.1f-%.1f m at eye %s m' % (ENTRANCE_D + (STAND_EYES,)),
        concealed='No grip vertex and <=10%% of body vertices visible from any entrance viewpoint',
        discoverable='>=30%% grip vertices visible from a walkable point within %.2f m at eye 1.0-1.65 m' % NEAR_M,
        grabbable='Palm at 0.8-1.2 m within %.1f m of grip with >=25%% grip vertices in clear line (beam)' % BEAM_M,
        far='Share of walkable points >= %.1f m away (eye 1.65 m) seeing >10%% of body or grip' % FAR_M,
        limitation='Geometric proxy in Blender. Not a Unity/PICO test; ignores lighting, passthrough contrast, value labels, other players.'),
        contact=contact, slots=report)
    (OUT / 'search_visibility.json').write_text(json.dumps(result, indent=2, ensure_ascii=False), encoding='utf-8')
    print('SUMMARY', json.dumps(summary))
    print('CONTACT', json.dumps(contact))
    for r in report:
        print(r['slot'], r['model'], 'H' if r['hidden_intent'] else '-', 'ent b/g', r['entrance_max_body_visible'], r['entrance_max_grip_visible'],
              'far', r['far_viewpoints_seeing'], 'near', r['best_near_view'] and (r['best_near_view']['body_visible'], r['best_near_view']['grip_visible'], r['best_near_view']['eye_unity_xyz']),
              'grab', r['grabbable'])


run()
