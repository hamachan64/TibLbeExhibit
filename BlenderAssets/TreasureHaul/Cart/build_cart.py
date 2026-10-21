"""Original salvage-bin excavation cart. Execute through Blender MCP.

Exports to Cart/Output/: ExcavationCart.fbx/.blend (runtime), Cart_Review.blend,
cart_stats.json, Cart_WornPaint.png, empty.png / loaded.png previews.
Uses the primitive helpers and original material atlas that the old stage builder had (copied in below).
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


# ---- helpers formerly imported from Stage/build_stage.py (that file was rewritten on 2026-10-21) ----
def activate(objects):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects: obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]


def bounds(objects):
    points = [o.matrix_world @ Vector(c) for o in objects if o.type == 'MESH' for c in o.bound_box]
    return [[min(p[i] for p in points), max(p[i] for p in points)] for i in range(3)]


def stats(objects):
    meshes = [o for o in objects if o.type == 'MESH']
    for o in meshes: o.data.calc_loop_triangles()
    return dict(meshes=len(meshes), triangles=sum(len(o.data.loop_triangles) for o in meshes),
                vertices=sum(len(o.data.vertices) for o in meshes),
                material_slots=sum(len(o.data.materials) for o in meshes))


class StageBuilder:
    """Primitive helpers and the original 8-swatch atlas, kept from the pre-2026-10-21 stage builder (the stage no longer uses them)."""
    def make_base_materials(self, texture_dir=None):
        """Original atlas and solid materials. The cart uses this without the stage
        recolours; texture_dir=None packs the images without writing PNG files."""
        colors = [(.16, .205, .20), (.34, .37, .32), (.10, .16, .15), (.055, .067, .07),
                  (.28, .13, .067), (.47, .43, .30), (.54, .34, .09), (.09, .10, .085)]
        metallic = [0, 0, .65, .75, .28, .35, .2, 0]
        roughness = [.9, .85, .65, .55, .88, .66, .72, .96]
        n = 128; rng = np.random.default_rng(4618)
        basec = np.zeros((n, n * 8, 4), dtype=np.float32); metal = basec.copy(); rough = basec.copy()
        y, x = np.mgrid[0:n, 0:n]
        for tile, color in enumerate(colors):
            coarse = rng.random((16, 16)); noise = np.repeat(np.repeat(coarse, 8, 0), 8, 1)
            noise = .65 * noise + .35 * rng.random((n, n))
            border = np.minimum.reduce([x, y, n - 1 - x, n - 1 - y]) / n
            grime = np.clip((.2 - border) * 2.5, 0, .3)
            value = .75 + .35 * noise - grime
            rgb = np.array(color)[None, None, :] * value[..., None]
            scratch = (rng.random((n, n)) > .992)  # small mineral inclusions
            rgb[scratch] *= 1.3
            basec[:, tile * n:(tile + 1) * n, :3] = np.clip(rgb, 0, 1)
            metal[:, tile * n:(tile + 1) * n, :3] = metallic[tile]
            rough[:, tile * n:(tile + 1) * n, :3] = np.clip(roughness[tile] + (noise - .5) * .12, 0, 1)[..., None]
        images = []
        for label, data in [('BaseColor', basec), ('Metallic', metal), ('Roughness', rough)]:
            data[:, :, 3] = 1
            im = bpy.data.images.new('THStage_' + label, width=n * 8, height=n, alpha=False)
            if label != 'BaseColor': im.colorspace_settings.name = 'Non-Color'
            im.pixels.foreach_set(data.ravel())
            if texture_dir:
                im.filepath_raw = str(texture_dir / ('Stage_' + label + '.png')); im.file_format = 'PNG'; im.save()
            im.pack(); images.append(im)
        self.surface = bpy.data.materials.new('THStage_SharedSurface'); self.surface.use_nodes = True
        bs = next(n for n in self.surface.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
        for im, socket in zip(images, ('Base Color', 'Metallic', 'Roughness')):
            tex = self.surface.node_tree.nodes.new('ShaderNodeTexImage'); tex.image = im
            self.surface.node_tree.links.new(tex.outputs['Color'], bs.inputs[socket])
        self.amber = self.solid('THStage_AmberLocator', (.8, .32, .055, 1), 1.8)
        self.teal = self.solid('THStage_ExtractionSignal', (.08, .6, .45, 1), 1.8)
        self.dark = self.solid('THStage_DarkRecess', (.018, .023, .022, 1))
        self.floor = self.solid('THStage_PREVIEW_RealFloor', (.24, .26, .26, 1))


    def solid(self, name, color, emission=0):
        m = bpy.data.materials.new(name); m.use_nodes = True
        bs = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
        bs.inputs['Base Color'].default_value = color; bs.inputs['Roughness'].default_value = .8
        if emission:
            bs.inputs['Emission Color'].default_value = color; bs.inputs['Emission Strength'].default_value = emission
        return m

    # ---- primitives; each module is a group of parts joined in finish() ----
    def begin(self, name):
        self.parts = []; self.name = name

    def own(self, obj, label, tile, material=None):
        obj.name = self.name + '_' + label
        for c in list(obj.users_collection): c.objects.unlink(obj)
        self.runtime.objects.link(obj); obj.parent = self.root
        obj.data.materials.append(material or self.surface)
        # Planar per-face UVs into the atlas tile.
        uv = obj.data.uv_layers.new(name='UVMap') if not obj.data.uv_layers else obj.data.uv_layers.active
        for poly in obj.data.polygons:
            axis = max(range(3), key=lambda i: abs(poly.normal[i])); axes = [i for i in range(3) if i != axis]
            coords = [obj.data.vertices[obj.data.loops[li].vertex_index].co for li in poly.loop_indices]
            low = [min(v[a] for v in coords) for a in axes]; high = [max(v[a] for v in coords) for a in axes]
            for li, v in zip(poly.loop_indices, coords):
                u = (v[axes[0]] - low[0]) / max(1e-6, high[0] - low[0]); w = (v[axes[1]] - low[1]) / max(1e-6, high[1] - low[1])
                uv.data[li].uv = ((tile + .07 + .86 * u) / 8, .07 + .86 * w)
        self.parts.append(obj)
        return obj

    def box(self, label, pos, size, tile=0, bevel=.015, mat=None, rotation=0):
        x, d, h = pos
        bpy.ops.mesh.primitive_cube_add(size=1, location=(x, -d, h))
        obj = bpy.context.object; obj.scale = size
        activate([obj]); bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        if bevel:
            mod = obj.modifiers.new('Worn bevel', 'BEVEL'); mod.width = min(bevel, min(size) * .2); mod.segments = 1
            bpy.ops.object.modifier_apply(modifier=mod.name)
        obj.rotation_euler.z = rotation
        return self.own(obj, label, tile, mat)

    def cylinder(self, label, pos, radius, depth, tile=0, vertices=12, rotation=(0, 0, 0), mat=None):
        x, d, h = pos
        bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=depth, location=(x, -d, h), rotation=rotation)
        return self.own(bpy.context.object, label, tile, mat)

    def patch(self, label, vertices, tile):
        # Convex chipped patch, normal points out of the visible surface.
        mesh = bpy.data.meshes.new(label)
        mesh.from_pydata(vertices, [], [tuple(range(len(vertices)))]); mesh.update()
        obj = bpy.data.objects.new(label, mesh); self.runtime.objects.link(obj)
        return self.own(obj, label, tile)

    def collider(self, name, pos, size):
        self.colliders.append(dict(name=name, center_unity_xyz=[pos[0], pos[2], pos[1]], size_unity_xyz=[size[0], size[2], size[1]], rotation_unity_xyz=[0, 0, 0]))

    def finish(self):
        activate(self.parts); bpy.ops.object.join(); obj = bpy.context.object; obj.name = self.name
        obj['module_id'] = self.name
        old = list(obj.data.materials); unique = list(dict.fromkeys(old))
        ids = [unique.index(old[p.material_index]) for p in obj.data.polygons]
        obj.data.materials.clear()
        for mat in unique: obj.data.materials.append(mat)
        for poly, mi in zip(obj.data.polygons, ids): poly.material_index = mi
        activate([obj]); bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        self.modules.append(obj)
        return obj



class _Stage:   # keeps the stage.<name> call sites below unchanged
    StageBuilder = StageBuilder
    activate = staticmethod(activate); bounds = staticmethod(bounds); stats = staticmethod(stats)


stage = _Stage


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
