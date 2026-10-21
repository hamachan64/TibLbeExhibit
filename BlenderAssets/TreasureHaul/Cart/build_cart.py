"""Original salvage-bin excavation cart. Execute through Blender MCP.

Exports to Cart/Output/: ExcavationCart.fbx/.blend (runtime), Cart_Review.blend,
cart_stats.json, Cart_WornPaint.png, empty.png / loaded.png previews.
Reuses the stage's primitive helpers and original material atlas (Stage/build_stage.py).
Keeps the user's active scene and objects intact. No external assets.
Logical X=width, D=forward, H=up; Blender=(X,-D,H).
Version history lives in Git (first draft: commit 07e74b8, Cart/ root files).
"""
import bpy, json, math, importlib.util
import numpy as np
from pathlib import Path
from mathutils import Vector, Matrix

HERE = Path(__file__).resolve().parent
OUT = HERE / 'Output'
TREASURES = HERE.parent / 'Treasures'
spec = importlib.util.spec_from_file_location('th_local_stage_tools', HERE.parent / 'Stage/build_stage.py')
stage = importlib.util.module_from_spec(spec); spec.loader.exec_module(stage)


class CartBuilder(stage.StageBuilder):
    def __init__(self):
        OUT.mkdir(exist_ok=True)
        self.scene = bpy.data.scenes.new('TH_Cart_Review'); bpy.context.window.scene = self.scene
        self.scene.unit_settings.system = 'METRIC'; self.scene.unit_settings.scale_length = 1
        self.runtime = bpy.data.collections.new('TH_Cart_Runtime'); self.scene.collection.children.link(self.runtime)
        self.preview = bpy.data.collections.new('TH_Cart_PreviewOnly'); self.scene.collection.children.link(self.preview)
        self.root = bpy.data.objects.new('TH_Cart_Root', None); self.runtime.objects.link(self.root)
        self.root['asset_id'] = 'TH_ExcavationCart'; self.root['origin'] = 'ground beneath deck center'
        self.modules = []; self.parts = []; self.markers = []; self.colliders = []; self.preview_treasures = []
        self.pivots = []
        # Stage atlas (surface) and teal signal, without the stage's ruin recolours.
        self.make_base_materials()
        self.floor = self.solid('THCart_PreviewFloor', (.19, .22, .22, 1))
        self.root['revision'] = 2
        self.paint = self.solid('THCart_WornEnamel', (.16, .20, .18, 1))
        # Original coarse paint chips; eight repeated swatches match existing UV helpers.
        rng = np.random.default_rng(19026)
        coarse = np.repeat(np.repeat(rng.random((64, 64)), 2, 0), 2, 1)
        chip = coarse < .025; pixels = np.ones((128, 128, 4), dtype=np.float32)
        for i, c in enumerate((.20, .25, .22)): pixels[:, :, i] = c * (.90 + .10 * coarse)
        pixels[chip, :3] = (.10, .115, .09)
        pixels = np.tile(pixels, (1, 8, 1))
        img = bpy.data.images.new('THCart_OriginalWornPaint', width=1024, height=128)
        img.pixels.foreach_set(pixels.ravel()); img.pack()
        img.filepath_raw = str(OUT / 'Cart_WornPaint.png'); img.file_format = 'PNG'; img.save()
        tex = self.paint.node_tree.nodes.new('ShaderNodeTexImage'); tex.image = img
        bs = next(n for n in self.paint.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
        self.paint.node_tree.links.new(tex.outputs['Color'], bs.inputs['Base Color'])
        bs.inputs['Metallic'].default_value = .35; bs.inputs['Roughness'].default_value = .82
        self.dark = self.solid('THCart_Charcoal', (.025, .033, .029, 1))
        self.screen = self.solid('THCart_Screen', (.006, .009, .024, 1))
        self.digits = self.solid('THCart_Phosphor', (.9, .75, .12, 1))
        bs = next(n for n in self.digits.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
        bs.inputs['Emission Color'].default_value = (.9, .75, .12, 1); bs.inputs['Emission Strength'].default_value = 1.2

    def pivot(self, name, pos, parent=None, axis=None):
        obj = bpy.data.objects.new('THCart_' + name, None); self.runtime.objects.link(obj)
        obj.parent = parent or self.root; obj.location = (pos[0], -pos[1], pos[2])
        obj['part_id'] = name
        if axis: obj['rotation_axis_blender'] = axis
        obj.empty_display_size = .06; self.pivots.append(obj)
        return obj

    def attach(self, obj, pivot):
        # finish() leaves world-space mesh vertices. Move them to the pivot frame.
        bpy.context.view_layer.update()
        obj.data.transform(pivot.matrix_world.inverted())
        obj.parent = pivot; obj.matrix_parent_inverse = Matrix.Identity(4); obj.matrix_basis = Matrix.Identity(4)

    def rod(self, name, a, b, radius=.022, tile=2):
        va = Vector((a[0], -a[1], a[2])); vb = Vector((b[0], -b[1], b[2])); delta = vb - va
        mid = (va + vb) / 2
        bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=radius, depth=delta.length, location=mid)
        obj = bpy.context.object; obj.rotation_euler = delta.to_track_quat('Z', 'Y').to_euler()
        return self.own(obj, name, tile)

    def body(self):
        self.begin('THCart_Chassis')
        self.box('Underpan', (0, 0, .25), (.72, 1, .10), 3, .025, self.dark)
        for side in (-1, 1): self.box('RecessedDriveHousing', (side * .29, 0, .19), (.19, .94, .22), 3, .02, self.dark)
        self.box('LoadFloor', (0, 0, .303), (.66, .94, .034), 3, .006, self.dark)
        # Broad panels and heavy bumpers instead of narrow rails.
        for side in (-1, 1):
            self.box('BinWall', (side * .35, 0, .475), (.06, 1, .31), 0, .012, self.paint)
            self.box('Rim', (side * .35, 0, .64), (.075, 1.025, .045), 3, .012, self.dark)
            self.box('RecessPanel', (side * .382, 0, .46), (.008, .69, .19), 3, .002, self.dark)
            self.box('InsetPanel', (side * .389, 0, .47), (.008, .61, .13), 0, .002, self.paint)
            for d in (-.43, .43):
                self.box('CornerShoe', (side * .35, d, .35), (.10, .15, .20), 3, .016, self.dark)
                self.box('WearTab', (side * .404, d, .39), (.008, .08, .08), 4, .001)
            for d in (-.33, .33): self.box('WheelMount', (side * .29, d, .23), (.15, .16, .06), 3, .008, self.dark)
        for d in (-.48, .48):
            self.box('EndWall', (0, d, .475), (.66, .05, .31), 0, .01, self.paint)
            self.box('EndRim', (0, d, .64), (.70, .07, .045), 3, .01, self.dark)
        for x in (-.27, .27):
            self.box('FrontBuffer', (x, .525, .35), (.14, .10, .14), 3, .02, self.dark)
            self.box('WarningPatch', (x, .58, .37), (.085, .008, .045), 6, .001)
        # Deliberately asymmetric machine details, no copied logo or game typography.
        for i in range(5): self.box('Vent', (-.15 + i * .045, .51, .47), (.023, .008, .065), 3, .001, self.dark)
        self.box('ServiceHatch', (.21, .515, .50), (.14, .02, .14), 3, .008, self.dark)
        self.finish()
        self.collider('Deck', (0, 0, .28), (.66, .94, .08))
        for s in (-1, 1): self.collider('Wall' + str(s), (s * .35, 0, .475), (.06, 1, .31))
        for d in (-.48, .48): self.collider('End' + str(d), (0, d, .475), (.66, .05, .31))

    def handle(self):
        pivot = self.pivot('PushHandlePivot', (0, -.50, .64), axis='X')
        self.begin('THCart_PushHandle')
        for side in (-1, 1):
            self.rod('ShortStanchion', (side * .30, -.47, .59), (side * .30, -.59, .75), .026, 3)
            self.rod('Grip', (side * .18, -.59, .75), (side * .32, -.59, .75), .03, 7)
        self.rod('PushBar', (-.30, -.59, .75), (.30, -.59, .75), .023, 3)
        self.attach(self.finish(), pivot)
        for side in (-1, 1):
            p = self.pivot('PushGrip_' + ('L' if side < 0 else 'R'), (side * .25, -.59, .75))
            bpy.context.view_layer.update(); world = p.matrix_world.copy(); p.parent = pivot; p.matrix_world = world
        self.begin('THCart_ValueConsole')
        self.box('Console', (0, -.535, .64), (.49, .12, .23), 3, .022, self.dark)
        self.box('ScreenGlass', (0, -.601, .65), (.405, .008, .132), 3, .002, self.screen)
        self.box('StatusLamp', (.22, -.603, .65), (.015, .008, .035), 0, .001, self.digits)
        self.finish()
        anchor = self.pivot('ValueDisplay', (0, -.610, .65)); anchor['purpose'] = 'current cargo sum in G; runtime text, not baked price'
        anchor['text_width_m'] = .38; anchor['text_height_m'] = .11
        volume = self.pivot('CargoVolume', (0, 0, .49)); volume['size_unity_xyz'] = [.64, .34, .88]

    def tire(self, label, x, d):
        profile = [(-.032, .055), (-.037, .075), (-.028, .097), (-.018, .10), (.018, .10), (.028, .097), (.037, .075), (.032, .055)]
        n = 20
        vs = [(x + ax, -d + r * math.sin(i * math.tau / n), .10 + r * math.cos(i * math.tau / n)) for ax, r in profile for i in range(n)]
        faces = []
        for j in range(len(profile)):
            for i in range(n): faces.append((j * n + i, j * n + (i + 1) % n, ((j + 1) % len(profile)) * n + (i + 1) % n, ((j + 1) % len(profile)) * n + i))
        mesh = bpy.data.meshes.new(label); mesh.from_pydata(vs, [], faces); mesh.update()
        obj = bpy.data.objects.new(label, mesh); self.runtime.objects.link(obj)
        self.own(obj, 'Tire', 7)

    def wheels(self):
        for front, d in ((True, .33), (False, -.37)):
            for side in (-1, 1):
                x = side * .29; tag = ('Front' if front else 'Rear') + ('L' if side < 0 else 'R')
                caster = None
                if not front:
                    caster = self.pivot('Caster_' + tag, (x, d + .04, .23), axis='Z')
                self.begin('THCart_Fork_' + tag)
                for sx in (-1, 1): self.box('Fork', (x + sx * .05, d, .155), (.018, .13, .15), 3, .008)
                self.box('Bridge', (x, d + .015, .225), (.13, .14, .025), 3, .006)
                if caster: self.cylinder('SwivelBearing', (x, d + .04, .235), .057, .022, 5, 16)
                fork = self.finish()
                if caster: self.attach(fork, caster)
                wheel = self.pivot('Wheel_' + tag, (x, d, .10), axis='X')
                if caster:
                    bpy.context.view_layer.update()
                    matrix = wheel.matrix_world.copy(); wheel.parent = caster; wheel.matrix_world = matrix
                self.begin('THCart_Wheel_' + tag)
                self.tire(tag, x, d)
                self.cylinder('Rim', (x, d, .10), .058, .078, 2, 20, (0, math.pi / 2, 0))
                self.cylinder('Axle', (x, d, .10), .022, .095, 5, 12, (0, math.pi / 2, 0))
                self.attach(self.finish(), wheel)
                wheel['radius_m'] = .1
        # Fixed front forks are structural: merge into the body to reduce renderers.
        body = next(o for o in self.modules if o.get('module_id') == 'THCart_Chassis')
        fixed = [o for o in self.modules if o.get('module_id', '').startswith('THCart_Fork_Front')]
        self.modules = [o for o in self.modules if o not in fixed]
        stage.activate([body, *fixed]); bpy.ops.object.join()
        self.compact(body)

    def compact(self, obj):
        old = list(obj.data.materials); unique = list(dict.fromkeys(old)); ids = [unique.index(old[p.material_index]) for p in obj.data.polygons]
        obj.data.materials.clear()
        for mat in unique: obj.data.materials.append(mat)
        for p, i in zip(obj.data.polygons, ids): p.material_index = i

    def cargo(self):
        # Review-only load; not exported with the cart.
        for name, x, d in [('clay_amphora', 0, .19), ('clay_pot', -.17, -.20), ('bronze_figure', .16, -.20)]:
            with bpy.data.libraries.load(str(TREASURES / (name + '.blend')), link=False) as (src, dst): dst.objects = src.objects
            objects = [o for o in dst.objects if o and o.type in ('EMPTY', 'MESH')]
            for o in objects: self.preview.objects.link(o)
            root = next(o for o in objects if o.type == 'EMPTY' and o.parent is None)
            root.location = (x, -d, 0); root.rotation_euler.z = math.pi
            bpy.context.view_layer.update()
            low = min((o.matrix_world @ v.co).z for o in objects if o.type == 'MESH' for v in o.data.vertices)
            root.location.z = .32 - low
            self.preview_treasures.extend(objects)

    def setup_preview(self):
        self.begin('THCart_Preview')
        floor = self.box('Floor', (0, 0, -.025), (3, 3, .05), 0, .003, self.floor)
        self.runtime.objects.unlink(floor); self.preview.objects.link(floor); floor.parent = None
        self.scene.world = bpy.data.worlds.new('THCart_World'); self.scene.world.use_nodes = True
        bg = next(n for n in self.scene.world.node_tree.nodes if n.type == 'BACKGROUND'); bg.inputs['Color'].default_value = (.12, .15, .17, 1); bg.inputs['Strength'].default_value = .45
        try: self.scene.render.engine = 'CYCLES'; self.scene.cycles.samples = 32
        except TypeError: pass
        for label, loc, power, size, color in [('Key', (2, -3, 4), 500, 3, (1, .83, .63)), ('Fill', (-3, -1, 2), 350, 3, (.6, .8, 1)), ('Rim', (1, 3, 3), 450, 2, (.7, 1, .9))]:
            data = bpy.data.lights.new('THCart_' + label, 'AREA'); data.energy = power; data.shape = 'DISK'; data.size = size; data.color = color
            ob = bpy.data.objects.new(data.name, data); self.preview.objects.link(ob); ob.location = loc
            ob.rotation_euler = (Vector((0, 0, .3)) - ob.location).to_track_quat('-Z', 'Y').to_euler()
        data = bpy.data.cameras.new('THCart_Camera'); ob = bpy.data.objects.new(data.name, data); self.preview.objects.link(ob); self.scene.camera = ob
        ob.location = (1.6, 2.3, 1.7); ob.rotation_euler = (Vector((0, 0, .4)) - ob.location).to_track_quat('-Z', 'Y').to_euler()
        data.type = 'ORTHO'; data.ortho_scale = 1.65
        self.scene.render.resolution_x = 1350; self.scene.render.resolution_y = 1150; self.scene.render.resolution_percentage = 100
        self.scene.render.image_settings.file_format = 'PNG'
        self.preview_digits = []

    def export(self):
        bpy.context.view_layer.update(); objects = list(self.runtime.objects)
        stats = stage.stats(objects); bb = stage.bounds(objects)
        assert stats['triangles'] <= 5000, stats
        assert bb[2][0] > -.0001 and bb[2][1] < .8, bb
        stage.activate(objects)
        bpy.ops.export_scene.fbx(filepath=str(OUT / 'ExcavationCart.fbx'), use_selection=True, object_types={'MESH', 'EMPTY'}, axis_forward='-Z', axis_up='Y', apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS', use_custom_props=True, path_mode='COPY', embed_textures=True, bake_anim=False, add_leaf_bones=False)
        scene = bpy.data.scenes.new('TH_Cart_Asset'); scene.collection.children.link(self.runtime); scene.unit_settings.system = 'METRIC'
        bpy.data.libraries.write(str(OUT / 'ExcavationCart.blend'), {scene}, fake_user=True, compress=True); bpy.data.scenes.remove(scene)
        report = dict(**stats, dimensions_m=[round(b - a, 5) for a, b in bb], bounds_blender=bb,
                      load_floor_height_m=.32, load_clear_size_m=[.64, .88], wheel_radius_m=.1,
                      turn_envelope_diameter_m=round(2 * max(math.hypot(x, y) for x in bb[0] for y in bb[1]), 3),
                      stage_min_lane_m=1.26, straight_side_margin_m=round((1.26 - (bb[0][1] - bb[0][0])) / 2, 3),
                      colliders=self.colliders, parts=[dict(id=o.get('module_id'), **stage.stats([o])) for o in self.modules],
                      pivots=[dict(id=o['part_id'], parent=o.parent.get('part_id', o.parent.name), location_blender=list(o.location), axis=o.get('rotation_axis_blender')) for o in self.pivots],
                      unity_imported=False, gameplay='model only; movable cart is future option U-4')
        (OUT / 'cart_stats.json').write_text(json.dumps(report, indent=2), encoding='utf-8'); print(json.dumps(report, indent=2))

    def render(self, name):
        # Seven-segment preview digits drawn from primitives, no font/image downloads.
        for o in self.preview_digits: bpy.data.objects.remove(o, do_unlink=True)
        self.preview_digits = []; self.begin('PreviewDigits')
        value = '  3600' if name == 'loaded' else '     0'
        codes = {'0': 'abcdef', '1': 'bc', '2': 'abged', '3': 'abgcd', '4': 'fgbc', '5': 'afgcd', '6': 'afgecd', '7': 'abc', '8': 'abcdefg', '9': 'abfgcd'}
        segs = {'a': (0, .040, .030, .006), 'g': (0, 0, .030, .006), 'd': (0, -.040, .030, .006), 'f': (-.018, .021, .006, .032), 'b': (.018, .021, .006, .032), 'e': (-.018, -.021, .006, .032), 'c': (.018, -.021, .006, .032)}
        previous = bpy.context.window.scene; bpy.context.window.scene = self.scene
        try:
            for i, ch in enumerate(value):
                if ch == ' ': continue
                for seg in codes[ch]:
                    x, z, w, h = segs[seg]
                    o = self.box('PreviewDigit', (-(-.18 + i * .052 + x), -.612, .65 + z), (w, .002, h), 0, 0, self.digits)
                    self.runtime.objects.unlink(o); self.preview.objects.link(o); self.preview_digits.append(o)
            curve = bpy.data.curves.new('PreviewUnitG', 'FONT'); curve.body = 'G'; curve.size = .095; curve.align_x = 'CENTER'
            o = bpy.data.objects.new('PreviewUnitG', curve); self.preview.objects.link(o)
            o.location = (-.15, .613, .614); o.rotation_euler = (math.pi / 2, 0, math.pi); curve.materials.append(self.digits)
            self.preview_digits.append(o)
            for obj in self.preview_treasures: obj.hide_render = name != 'loaded'
            self.scene.render.filepath = str(OUT / (name + '.png'))
            bpy.ops.render.render(write_still=True, scene=self.scene.name)
            for obj in self.preview_treasures: obj.hide_render = False
        finally: bpy.context.window.scene = previous


def run(render=True):
    old = bpy.context.window.scene; selection = list(bpy.context.selected_objects); active = bpy.context.view_layer.objects.active
    try:
        b = CartBuilder(); b.body(); b.handle(); b.wheels(); b.cargo(); b.setup_preview(); b.export()
        if render: b.render('empty'); b.render('loaded')
        bpy.data.libraries.write(str(OUT / 'Cart_Review.blend'), {b.scene}, fake_user=True, compress=True)
        return b
    finally:
        bpy.context.window.scene = old; bpy.ops.object.select_all(action='DESELECT')
        for o in selection: o.select_set(True)
        bpy.context.view_layer.objects.active = active


if __name__ == '__main__': run()
