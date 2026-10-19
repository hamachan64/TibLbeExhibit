"""Revision 7: hidden finds that are actually hidden, and can still be reached.

Revision 6 measured (check_search_visibility.py): of 18 intended hidden finds only
7 were both concealed from the practice row and findable up close.
- Rear row (d 9.9) opened toward the back wall; the 0.75 m gap was not walkable.
- West column opened east, straight into the arrival sightline.
- Slot 18 opened east and showed its body from the entrance.
- Supports 22/23 overlapped, and support 14 intersected the rear room divider.
Fixes: rear row moved forward to leave a walkable back aisle, west column moved
in and opened toward the west aisle, slot 18 opened to the rear, every hidden
niche built from staggered masonry courses instead of one plain slab.
Execute through Blender MCP (creates its own scenes, restores the user's scene).
"""
import bpy, runpy, math, json, random
from pathlib import Path
HERE = Path(__file__).resolve().parent
v6 = runpy.run_path(str(HERE / 'build_stage_v6.py'), run_name='context6')
base = v6['base']; base.OUT = HERE / 'Revision7'
TYPES = v6['TYPES']
HIDDEN = v6['HIDDEN']
WEST = {19, 20, 21, 22}
ROW3_X = (-1.4, -.95, -.5, 0, .5, .95, 1.4)
ROW3_D = 9.55
WEST_X = -1.30
# North end <= 6.22 keeps the cart's turn into the west gap (radius 0.8 m at d 7.1);
# south end leaves a 0.6 m walk-in gap behind the entry altar (d 3.93).
WEST_D = (4.70, 5.15, 5.60, 6.05)


class HiddenNicheBuilder(v6['SearchBuilder']):
    def placement(self):
        for i, x in enumerate((-1.2, -.4, .4, 1.2)): self.marker('Tutorial_%02d' % (i + 1), (x, 2.5, 0), 'tutorial')
        coords = [(x, 3.8) for x in (-1.5, -1.05, -.6, -.15, .3)]
        coords += [(x, 8.1) for x in (-.3, .15, .6, 1.05, 1.5)]
        coords += [(x, ROW3_D) for x in ROW3_X]
        coords += [(-.36, 5.71), (.15, 5.71)]  # 19 clears CentralReturn (x 0.34)
        coords += [(WEST_X, d) for d in WEST_D]
        for i, (x, d) in enumerate(coords):
            self.marker('TreasureSlot_%02d' % (i + 1), (x, d, 0), 'treasure_candidate', clearance_radius_m=.25, allowed_classes=['S', 'M'])

    def course_face(self, rng, cx, cd, along_x, length, h0, h1, thick, rust_share):
        """A closed niche face as 2-3 staggered ashlar courses inside its envelope."""
        # Recessed mortar core: the course seams read as masonry but admit no sightline.
        core = (length, thick - .014, h1 - h0) if along_x else (thick - .014, length, h1 - h0)
        self.box('MortarCore', (cx, cd, (h0 + h1) / 2), core, 7, 0)
        courses = 3 if h1 - h0 > .38 else 2
        ch = (h1 - h0) / courses
        for row in range(courses):
            n = max(1, round(length / .17)) + (row % 2)
            span = length / n
            for k in range(n):
                a = (k - (n - 1) / 2) * span
                inset = rng.uniform(0, .006)
                size = (span - .006, thick - inset, ch - .004) if along_x else (thick - inset, span - .006, ch - .004)
                pos = (cx + a, cd, h0 + ch * (row + .5)) if along_x else (cx, cd + a, h0 + ch * (row + .5))
                # Mostly weathered grey ashlar; occasional rust-stained block, no regular checker.
                self.box('Ashlar', pos, size, 4 if rng.random() < rust_share else 1, .007)

    def preview_items(self):
        self.support_checks = []; self.find_records = []
        rng = random.Random(7019)
        candidates = [m for m in self.markers if m['role'] == 'treasure_candidate']
        row_edge = {}  # rows 1-3: pack supports left to right with a 20 mm gap (keeps the 0.7 m edge margin)
        for i, (name, slot) in enumerate(zip(TYPES, candidates)):
            x, _, d = slot['position_unity_xyz']; hidden = i in HIDDEN
            with bpy.data.libraries.load(str(base.TREASURES / (name + '.blend')), link=False) as (src, dst): dst.objects = src.objects
            objects = [o for o in dst.objects if o and o.type in ('MESH', 'EMPTY')]
            for o in objects: self.preview.objects.link(o); o['preview_only'] = True
            root = next(o for o in objects if o.type == 'EMPTY' and o.parent is None)
            # Rotation 0 faces +D (away from the entrance); -pi/2 faces west (-X).
            opening = ('west' if i in WEST else 'rear') if hidden else 'top'
            root.rotation_euler.z = -math.pi / 2 if opening == 'west' else 0 if hidden else math.pi
            root.location = (0, 0, 0); bpy.context.view_layer.update(); bb = base.bounds(objects)
            width = bb[0][1] - bb[0][0]; depth = bb[1][1] - bb[1][0]; height = bb[2][1] - bb[2][0]
            h = .18 if d in (3.8, 8.1, 5.71) else .10
            suspended = .035 if name in ('mask', 'hand_mirror') and hidden else 0
            w = max(.33, width + .14); length = max(.33, depth + .12); t = .05
            row = 0 if i < 5 else 1 if i < 10 else 2 if i < 17 else None
            if row is not None:
                if row in row_edge: x = row_edge[row] + .02 + w / 2
                row_edge[row] = x + w / 2
            # Centre the item's real bounds (model origins are not centred) in the
            # niche interior, nudged 10 mm toward the opening.
            if opening == 'rear': cx, cd = x, d + t / 2 + .01
            elif opening == 'west': cx, cd = x - t / 2 - .01, d
            else: cx, cd = x, d
            root.location = (cx - (bb[0][0] + bb[0][1]) / 2, -cd - (bb[1][0] + bb[1][1]) / 2, h + suspended - bb[2][0]); bpy.context.view_layer.update()
            item_bb = base.bounds(objects)
            self.begin('THStage_FindSupport_%02d' % (i + 1))
            self.box('BrokenStoneBase', (x, d, h / 2), (w, length, h), 1, .012)
            if hidden:
                # Full headroom (below the 0.80 m cap incl. the cap fragment) so a
                # standing visitor behind the niche can see the top grip.
                top = min(.775, max(item_bb[2][1] + .068, .775 - rng.uniform(0, .025))); roof = .04; inner_top = top - roof
                assert inner_top - item_bb[2][1] > .025, (name, item_bb, top)
                tiles = .18  # share of rust-stained blocks
                # Closed faces: everything except the opening. South (entrance) face is always closed.
                faces = {'south': (x, d - length / 2 + t / 2, True, w), 'north': (x, d + length / 2 - t / 2, True, w),
                         'west': (x - w / 2 + t / 2, d, False, length - 2 * t), 'east': (x + w / 2 - t / 2, d, False, length - 2 * t)}
                closed = ['south', 'west', 'east'] if opening == 'rear' else ['south', 'north', 'east']
                for f in closed:
                    cx, cd, along_x, span = faces[f]
                    self.course_face(rng, cx, cd, along_x, span, h, inner_top, t, tiles)
                if opening == 'west':
                    # Broken buttress continues the south face into the aisle and
                    # shades the opening from the entrance's south-west corner.
                    # Two stepped courses, the upper one broken shorter.
                    bx, bd = x - w / 2 - .06, d - length / 2 + t / 2
                    self.box('Buttress', (bx, bd, inner_top * .30), (.12, t, inner_top * .60), 1, .008)
                    self.box('Buttress', (bx + .015, bd, inner_top * .78), (.09, t - .004, inner_top * .36), 4 if i % 2 else 1, .008)
                # Lintel of two stones over the entrance side only. The broken-back
                # edge leaves the opening side unroofed (cut) so the grip shows from behind.
                cut = .30 * (length if opening == 'rear' else w)
                if opening == 'rear':
                    d0, d1 = d - length / 2 - .006, d + length / 2 - cut
                    for side in (-1, 1):
                        self.box('LintelStone', (x + side * w / 4, (d0 + d1) / 2, top - roof / 2), (w / 2 + .004, d1 - d0, roof), 4 if rng.random() < .3 else 1, .006)
                else:
                    x0, x1 = x - w / 2 + cut, x + w / 2 + .006
                    for side in (-1, 1):
                        self.box('LintelStone', ((x0 + x1) / 2, d + side * length / 4, top - roof / 2), (x1 - x0, length / 2 + .004, roof), 4 if rng.random() < .3 else 1, .006)
                if top + .018 < .795:
                    self.box('CapFragment', (x - w * .16, d - length * .1, top + .009), (w * .5, length * .6, .018), 4, .003)
                if name in ('mask', 'hand_mirror'):
                    self.box('WallPeg', (x, d - length / 2 + t + .02, h + suspended + height * .56), (.04, .04, .025), 5, .003)
                kind = 'west_aisle_wall_niche' if opening == 'west' else 'reverse_open_stone_niche'
                discovery = [x - .7, d, 1.2] if opening == 'west' else [x, d + .7, 1.2]
            else:
                kind = 'exposed_stone_find'; top = h
                self.box('CrackedCap', (x, d, h + .012), (w, length, .024), 4, .004)
                root.location.z += .024; h += .024
                discovery = [x, d - .65, 1.65]
            support = self.finish(); module = support['module_id']
            sb = base.bounds([support])
            self.collider(module, ((sb[0][0] + sb[0][1]) / 2, -(sb[1][0] + sb[1][1]) / 2, (sb[2][0] + sb[2][1]) / 2), (sb[0][1] - sb[0][0], sb[1][1] - sb[1][0], sb[2][1] - sb[2][0]))
            slot.update(position_unity_xyz=[root.location.x, root.location.z, -root.location.y], support_center_unity_xz=[x, d], position_blender_xyz=list(root.location), model=name, support_module=module, placement_kind=kind,
                        support_surface_height_m=h, rotation_blender_xyz=list(root.rotation_euler), hidden_intent=hidden, opening=opening,
                        discovery_eye_unity_xyz=[discovery[0], discovery[2], discovery[1]], allowed_classes=['M' if name in ('amphora', 'inscribed_stele') else 'S'])
            marker = next(o for o in self.runtime.objects if o.get('anchor_id') == slot['name']); marker.location = root.location
            marker['hidden_intent'] = hidden; marker['placement_kind'] = kind; marker['support_module'] = module
            self.sample_placements.append(dict(model=name, slot=slot['name'], class_name=slot['allowed_classes'][0], root_name=root.name))
            self.preview_treasures.extend(objects); bpy.context.view_layer.update(); actual = base.bounds(objects)
            self.support_checks.append(dict(slot=slot['name'], model=name, kind=kind, base_height_m=actual[2][0], top_height_m=actual[2][1], support_height_m=h, suspended_offset_m=suspended))
            self.find_records.append(dict(slot=slot['name'], objects=objects, root=root, hidden=hidden, discovery=discovery))

    def export(self):
        super().export()
        p = base.OUT / 'stage_layout.json'; data = json.loads(p.read_text(encoding='utf-8'))
        data['revision'] = 7
        data['search_design'] = {'hidden_target_count': len(HIDDEN), 'exposed_cues': 23 - len(HIDDEN),
                                 'mechanism': 'Roofed masonry niches whose entrance-facing side is closed. Rear-row niches open onto a walkable back aisle; west-column niches open onto the west aisle.',
                                 'verification': 'search_visibility.json (check_search_visibility.py)'}
        p.write_text(json.dumps(data, indent=2, ensure_ascii=False), encoding='utf-8')


def run():
    old = bpy.context.window.scene; selected = list(bpy.context.selected_objects); active = bpy.context.view_layer.objects.active; original = set(bpy.data.objects)
    try:
        b = HiddenNicheBuilder(); b.lift(); b.maze(); b.placement(); b.preview_items(); b.cart_preview(); b.preview_setup(); b.export()
        return b
    finally:
        bpy.context.window.scene = old; bpy.ops.object.select_all(action='DESELECT')
        for o in selected: o.select_set(True)
        bpy.context.view_layer.objects.active = active
        assert original.issubset(set(bpy.data.objects))


if __name__ == '__main__': run()
