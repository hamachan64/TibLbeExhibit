"""Treasure Haul 5 x 11 m MR ruin stage. Execute through Blender MCP.

Builds the stage, 23 preview finds on stone supports (18 in niches closed toward
the entrance), the cart preview, and exports to Stage/Output/:
ExcavationDepot_5x11.fbx/.blend (runtime), modules/*.fbx, Stage_Review.blend,
stage_layout.json, textures/. Creates its own scenes and restores the user's scene.

No downloads, generators or edits to the user's scene. Unity assets are not touched.
Logical coordinates are (width X, depth D, height H), Blender (X, -D, H).
Version history lives in Git (earlier revisions: commits 07e74b8, d020226).
"""
import bpy, json, math, random
import numpy as np
from pathlib import Path
from mathutils import Vector

HERE = Path(__file__).resolve().parent
OUT = HERE / 'Output'
TREASURES = HERE.parent / 'ContextReviewed'
CART = HERE.parent / 'Cart/Revision2/ExcavationCart.blend'
RNG = random.Random(4618)

# 23 preview finds: 14 S + 9 M (plan 6.1, 4 players). Index = slot - 1.
TYPES = ['amphora', 'mask', 'inscribed_stele', 'cup', 'amphora', 'inscribed_stele', 'hand_mirror', 'amphora', 'statuette', 'inscribed_stele',
         'amphora', 'stamp_seal', 'coin_casket', 'inscribed_stele', 'scarab_amulet', 'ceremonial_lamp', 'amphora', 'cup', 'statuette', 'votive_bell',
         'perfume_jar', 'scarab_amulet', 'ceremonial_lamp']
HIDDEN = {0, 1, 4, 5, 6, 7, 8, 10, 11, 12, 14, 15, 16, 17, 19, 20, 21, 22}
WEST = {19, 20, 21, 22}
ROW3_X = (-1.4, -.95, -.5, 0, .5, .95, 1.4)
ROW3_D = 9.55
WEST_X = -1.30
# North end <= 6.22 keeps the cart's turn into the west gap (radius 0.8 m at d 7.1);
# south end leaves a 0.6 m walk-in gap behind the entry altar (d 3.93).
WEST_D = (4.70, 5.15, 5.60, 6.05)
# Low 0.16 m altar foundations that the front, rear and central finds stand on.
ALTARS = ('EntryPartition', 'RearPartition', 'CentralCrosswall')


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
    def __init__(self):
        OUT.mkdir(exist_ok=True); (OUT / 'textures').mkdir(exist_ok=True)
        self.scene = bpy.data.scenes.new('TH_Stage_ExcavationDepot_5x11')
        bpy.context.window.scene = self.scene
        self.scene.unit_settings.system = 'METRIC'; self.scene.unit_settings.scale_length = 1
        self.runtime = bpy.data.collections.new('TH_STAGE_Runtime'); self.scene.collection.children.link(self.runtime)
        self.preview = bpy.data.collections.new('TH_STAGE_PreviewOnly'); self.scene.collection.children.link(self.preview)
        self.root = bpy.data.objects.new('VenueRoot', None); self.runtime.objects.link(self.root)
        self.root['venue_width_m'] = 5.; self.root['venue_length_m'] = 11.
        self.root['logical_axes'] = 'X across, D away from lift, H up; Blender=(X,-D,H)'
        self.modules = []; self.colliders = []; self.markers = []; self.parts = []; self.preview_treasures = []; self.sample_placements = []
        self.make_materials()

    # ---- shared material: eight original 128 px swatches in a 1024 x 128 atlas ----
    def make_materials(self):
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
            im.pixels.foreach_set(data.ravel()); im.filepath_raw = str(OUT / 'textures' / ('Stage_' + label + '.png'))
            im.file_format = 'PNG'; im.save(); im.pack(); images.append(im)
        self.surface = bpy.data.materials.new('THStage_SharedSurface'); self.surface.use_nodes = True
        bs = next(n for n in self.surface.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
        for im, socket in zip(images, ('Base Color', 'Metallic', 'Roughness')):
            tex = self.surface.node_tree.nodes.new('ShaderNodeTexImage'); tex.image = im
            self.surface.node_tree.links.new(tex.outputs['Color'], bs.inputs[socket])
        self.amber = self.solid('THStage_AmberLocator', (.8, .32, .055, 1), 1.8)
        self.teal = self.solid('THStage_ExtractionSignal', (.08, .6, .45, 1), 1.8)
        self.dark = self.solid('THStage_DarkRecess', (.018, .023, .022, 1))
        self.floor = self.solid('THStage_PREVIEW_RealFloor', (.24, .26, .26, 1))
        # Two recolour passes through the 8-bit images, in this order, so the atlas
        # stays byte-identical to the committed Revision7 output.
        self.recolor_archive(); self.recolor_ruin()

    def recolor_archive(self):
        old = [(.16, .205, .20), (.34, .37, .32), (.10, .16, .15), (.055, .067, .07), (.28, .13, .067), (.47, .43, .30), (.54, .34, .09), (.09, .10, .085)]
        new = [(.34, .29, .22), (.24, .25, .23), (.10, .145, .135), (.10, .052, .032), (.22, .075, .045), (.37, .29, .13), (.49, .32, .08), (.13, .035, .04)]
        for node in self.surface.node_tree.nodes:
            if node.type != 'TEX_IMAGE': continue
            im = node.image; data = np.array(im.pixels[:], dtype=np.float32).reshape(128, 1024, 4)
            if 'BaseColor' in im.name:
                for tile in range(8): data[:, tile * 128:(tile + 1) * 128, :3] *= np.array(new[tile]) / np.array(old[tile])
            if 'Metallic' in im.name:
                for tile in (3, 7): data[:, tile * 128:(tile + 1) * 128, :3] = 0
            im.pixels.foreach_set(data.ravel()); im.save(); im.pack()

    def recolor_ruin(self):
        # Weathered stone tiles 0, 1, 4, 7: coarse mottling, one crack, darker top band.
        colors = [(.42, .38, .30), (.28, .265, .22), (.10, .145, .135), (.10, .052, .032), (.29, .25, .19), (.37, .29, .13), (.49, .32, .08), (.16, .125, .085)]
        rng = np.random.default_rng(4019); yy, xx = np.mgrid[0:128, 0:128]
        for node in self.surface.node_tree.nodes:
            if node.type != 'TEX_IMAGE': continue
            im = node.image; data = np.array(im.pixels[:], dtype=np.float32).reshape(128, 1024, 4)
            for tile in (0, 1, 4, 7):
                region = data[:, tile * 128:(tile + 1) * 128, :3]
                if 'BaseColor' in im.name:
                    coarse = np.repeat(np.repeat(rng.random((32, 32)), 4, 0), 4, 1)
                    coarse = (coarse + np.roll(coarse, 2, 0) + np.roll(coarse, 2, 1)) / 3
                    value = .76 + .19 * coarse + .09 * rng.random((128, 128))
                    crack = abs(xx - (55 + 11 * np.sin(yy * .09) + yy * .2)) < .6
                    value[crack] *= .65
                    value[yy < 18] *= .72
                    region[:] = np.array(colors[tile]) * value[..., None]
                elif 'Metallic' in im.name: region[:] = 0
                else: region[:] = .94
            im.pixels.foreach_set(data.ravel()); im.save(); im.pack()
        self.ruin_rng = random.Random(4019); self.wall_specs = []

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

    def marker(self, name, pos, role, **extra):
        obj = bpy.data.objects.new(name, None); self.runtime.objects.link(obj); obj.parent = self.root
        obj.location = (pos[0], -pos[1], pos[2]); obj.empty_display_size = .08
        obj['role'] = role
        obj['anchor_id'] = name
        record = dict(name=name, role=role, position_unity_xyz=[pos[0], pos[2], pos[1]], position_blender_xyz=list(obj.location), **extra)
        self.markers.append(record)
        return obj

    # ---- extraction bay: low receiver floor with side machines, roll-in from the front ----
    def lift(self):
        self.begin('THStage_ExtractionBay')
        # Local receiver floor only. Front threshold 6mm; no raised loading bench.
        self.box('ReceiverFloor', (0, 1.20, .012), (2.30, 1.56, .024), 2, .004)
        for x in (-1.12, 1.12):
            self.box('FloorGuide', (x, 1.20, .027), (.025, 1.50, .006), 0, 0, self.teal)
        self.box('EntryThreshold', (0, 2.03, .003), (2.30, .10, .006), 2, 0)
        self.box('BackMachine', (0, .30, .36), (3, .24, .72), 2, .025)
        for sign in (-1, 1):
            self.box('SideReceiver', (sign * 1.34, 1.20, .20), (.30, 1.52, .40), 2, .02)
            self.box('TopRail', (sign * 1.34, 1.20, .42), (.30, 1.52, .04), 2, .008)
            for i in range(8):
                self.box('WarningMark', (sign * 1.34, .55 + i * .17, .446), (.22, .07, .008), 6, 0, rotation=.18)
            self.collider('ReceiverSide_' + str(sign), (sign * 1.34, 1.20, .225), (.30, 1.52, .45))
        self.box('MeterHousing', (0, .448, .56), (1.18, .08, .31), 2, .015)
        self.box('MeterWindow', (0, .494, .57), (1.02, .014, .19), 3, .004, self.dark)
        for i in range(5): self.box('MeterPlaceholder', (-.36 + i * .18, .505, .57), (.085, .008, .025), 0, .001, self.teal)
        self.box('Actuator', (0, .46, .24), (.54, .08, .18), 2, .01)
        self.box('IntakeSlot', (0, .507, .24), (.40, .012, .055), 3, .001, self.dark)
        for x in (-1.1, 1.1):
            for i in range(5): self.box('Vent', (x, .428, .27 + i * .06), (.38, .014, .022), 3, .002, self.dark)
        self.box('ControlPod', (-1.34, 1.82, .50), (.30, .28, .52), 2, .018)
        self.box('ControlFace', (-1.34, 1.97, .59), (.24, .02, .25), 3, .003, self.dark)
        self.cylinder('Button', (-1.34, 1.989, .60), .065, .018, vertices=12, rotation=(math.pi / 2, 0, 0), mat=self.amber)
        self.finish()
        self.collider('ReceiverBack', (0, .35, .36), (3, .34, .72))
        self.collider('ControlPod', (-1.34, 1.85, .39), (.32, .32, .78))
        self.marker('CollectionVolume', (0, 1.20, .49), 'collection_volume', size=[2.30, .92, 1.56])
        self.marker('ScoreBoardAnchor', (0, .32, 1.5), 'nonphysical_ui_anchor')
        self.marker('CartDock', (0, 1.20, .024), 'cart_dock', preview_only=True)

    # ---- ruin walls and low altar foundations ----
    def wall(self, name, x, d, w, length):
        self.wall_specs.append((name, x, d, w, length))
        self.begin('THStage_' + name)
        if name in ALTARS:
            count = max(1, round(w / .42)); span = w / count
            for i in range(count): self.box('AltarFoundation', (x + (i - (count - 1) / 2) * span, d, .08), (span - .009, length, .16), 1, .014)
            self.finish(); self.collider(name, (x, d, .08), (w, length, .16))
            return
        rng = self.ruin_rng; horizontal = w > length; long = max(w, length); thin = min(w, length)
        # Dark recessed mortar; staggered individual blocks remain inside the bounds.
        self.box('Mortar', (x, d, .215), (w - .028, length - .028, .41), 1, .006)
        for row in range(3):
            count = max(1, round(long / .46) + (row % 2)); span = long / count
            for i in range(count):
                a = (i - (count - 1) / 2) * span
                height = rng.uniform(.07, .23) if row == 2 else rng.uniform(.19, .22)
                if row == 2 and i % 5 == 2: continue
                h = .02 + row * .23 + height / 2
                size = (span - .012, thin - .006, height) if horizontal else (thin - .006, span - .012, height)
                pos = (x + a, d, h) if horizontal else (x, d + a, h)
                obj = self.box('ChippedStone', pos, size, 1 if rng.random() < .65 else 4, .012)
                # Asymmetric erosion of the top corners; no point expands the bounds.
                for vertex in obj.data.vertices:
                    if vertex.co.z > 0:
                        vertex.co.z -= rng.uniform(0, .025)
                        vertex.co.x *= rng.uniform(.97, 1); vertex.co.y *= rng.uniform(.97, 1)
        # Broken plaster islands, irregular outlines, not a continuous neat panel.
        for i in range(max(1, int(long / .75))):
            a = (i + .5) * long / max(1, int(long / .75)) - long / 2
            width = min(.48, long * .65) * rng.uniform(.65, 1); height = rng.uniform(.15, .31)
            center = .34 + rng.uniform(-.06, .05)
            outline = [(-.5, -.3), (-.30, -.5), (.23, -.43), (.5, -.18), (.39, .37), (.04, .5), (-.46, .22)]
            for side in (-1, 1):
                if horizontal: verts = [(x + a + u * width, -(d + side * (thin / 2 - .001)), center + v * height) for u, v in outline]
                else: verts = [(x + side * (thin / 2 - .001), -(d + a + u * width), center + v * height) for u, v in outline]
                if (horizontal and side < 0) or (not horizontal and side > 0): verts.reverse()
                self.patch('PeelingPlaster', verts, 0)
        self.finish(); self.collider(name, (x, d, .375), (w, length, .75))

    def maze(self):
        for args in [('WestBoundary', -2.41, 6.9, .18, 8.2), ('EastBoundary', 2.41, 6.9, .18, 8.2),
                     ('BackBoundary', 0, 10.91, 4.64, .18), ('EntryPartition', -.85, 3.8, 2.94, .26),
                     ('RearPartition', .85, 8.1, 2.94, .26), ('RearRoomDivider', 0, 10.36, .26, .92),
                     ('CentralCrosswall', 0, 5.71, 1.20, .26), ('CentralReturn', .47, 6.06, .26, .44)]: self.wall(*args)
        self.ground()

    def ground(self):
        self.begin('THStage_RuinGroundFragments'); rng = self.ruin_rng; patch_index = 0
        # Flat fractured ground patches along wall feet. No raised rubble in routes.
        # All patches are opaque small polygons, not a full floor or alpha layers.
        for name, x, d, w, length in self.wall_specs:
            horizontal = w > length; long = max(w, length); thin = min(w, length)
            count = max(1, round(long / .48)); span = long / count
            for i in range(count):
                a = (i + .5) * span - long / 2
                for side in (-1, 1):
                    if rng.random() < .18: continue
                    cx = x + a if horizontal else x + side * (thin / 2 + .07)
                    cd = d + side * (thin / 2 + .07) if horizontal else d + a
                    if not (-2.46 < cx < 2.46 and .1 < cd < 10.96): continue
                    patch_index += 1
                    sx = span * rng.uniform(.6, 1.1) if horizontal else rng.uniform(.17, .40)
                    sd = rng.uniform(.17, .40) if horizontal else span * rng.uniform(.6, 1.1)
                    verts = []
                    for k in range(8):
                        ang = 2 * math.pi * k / 8; rr = rng.uniform(.78, 1)
                        px = max(-2.495, min(2.495, cx + math.cos(ang) * sx * rr))
                        pd = max(.005, min(10.995, cd + math.sin(ang) * sd * rr))
                        verts.append((px, -pd, .001 + patch_index * .000002))
                    self.patch('SoilApron', list(reversed(verts)), 7)
                    if rng.random() < .8:
                        # A separate small shard at a slightly higher planar level.
                        stone = [(cx - sx * .45, -(cd - sd * .30), .003), (cx + sx * .32, -(cd - sd * .35), .003),
                                 (cx + sx * .43, -(cd + sd * .13), .003), (cx + sx * .12, -(cd + sd * .34), .003),
                                 (cx - sx * .42, -(cd + sd * .20), .003)]
                        stone = [(max(-2.495, min(2.495, px)), max(-10.995, min(-.005, py)), z + patch_index * .000002) for px, py, z in stone]
                        self.patch('BrokenPaving', list(reversed(stone)), 1 if rng.random() < .7 else 4)
        obj = self.finish(); obj['ground_decoration_only'] = True; obj['do_not_add_collider'] = True
        obj.visible_shadow = False; obj['disable_shadow_casting_in_unity'] = True

    # ---- treasure slots and their stone supports / niches ----
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
            with bpy.data.libraries.load(str(TREASURES / (name + '.blend')), link=False) as (src, dst): dst.objects = src.objects
            objects = [o for o in dst.objects if o and o.type in ('MESH', 'EMPTY')]
            for o in objects: self.preview.objects.link(o); o['preview_only'] = True
            root = next(o for o in objects if o.type == 'EMPTY' and o.parent is None)
            # Rotation 0 faces +D (away from the entrance); -pi/2 faces west (-X).
            opening = ('west' if i in WEST else 'rear') if hidden else 'top'
            root.rotation_euler.z = -math.pi / 2 if opening == 'west' else 0 if hidden else math.pi
            root.location = (0, 0, 0); bpy.context.view_layer.update(); bb = bounds(objects)
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
            item_bb = bounds(objects)
            self.begin('THStage_FindSupport_%02d' % (i + 1))
            self.box('BrokenStoneBase', (x, d, h / 2), (w, length, h), 1, .012)
            if hidden:
                # Full headroom (below the 0.80 m cap incl. the cap fragment) so a
                # standing visitor behind the niche can see the top grip.
                top = min(.775, max(item_bb[2][1] + .068, .775 - rng.uniform(0, .025))); roof = .04; inner_top = top - roof
                assert inner_top - item_bb[2][1] > .025, (name, item_bb, top)
                rust = .18  # share of rust-stained blocks
                # Closed faces: everything except the opening. South (entrance) face is always closed.
                faces = {'south': (x, d - length / 2 + t / 2, True, w), 'north': (x, d + length / 2 - t / 2, True, w),
                         'west': (x - w / 2 + t / 2, d, False, length - 2 * t), 'east': (x + w / 2 - t / 2, d, False, length - 2 * t)}
                closed = ['south', 'west', 'east'] if opening == 'rear' else ['south', 'north', 'east']
                for f in closed:
                    fx, fd, along_x, span = faces[f]
                    self.course_face(rng, fx, fd, along_x, span, h, inner_top, t, rust)
                if opening == 'west':
                    # Broken buttress continues the south face into the aisle and shades
                    # the opening from the entrance's south-west corner. Two stepped courses.
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
                kind = 'exposed_stone_find'
                self.box('CrackedCap', (x, d, h + .012), (w, length, .024), 4, .004)
                root.location.z += .024; h += .024
                discovery = [x, d - .65, 1.65]
            support = self.finish(); module = support['module_id']
            sb = bounds([support])
            self.collider(module, ((sb[0][0] + sb[0][1]) / 2, -(sb[1][0] + sb[1][1]) / 2, (sb[2][0] + sb[2][1]) / 2), (sb[0][1] - sb[0][0], sb[1][1] - sb[1][0], sb[2][1] - sb[2][0]))
            slot.update(position_unity_xyz=[root.location.x, root.location.z, -root.location.y], support_center_unity_xz=[x, d], position_blender_xyz=list(root.location), model=name, support_module=module, placement_kind=kind,
                        support_surface_height_m=h, rotation_blender_xyz=list(root.rotation_euler), hidden_intent=hidden, opening=opening,
                        discovery_eye_unity_xyz=[discovery[0], discovery[2], discovery[1]], allowed_classes=['M' if name in ('amphora', 'inscribed_stele') else 'S'])
            marker = next(o for o in self.runtime.objects if o.get('anchor_id') == slot['name']); marker.location = root.location
            marker['hidden_intent'] = hidden; marker['placement_kind'] = kind; marker['support_module'] = module
            self.sample_placements.append(dict(model=name, slot=slot['name'], class_name=slot['allowed_classes'][0], root_name=root.name))
            self.preview_treasures.extend(objects); bpy.context.view_layer.update(); actual = bounds(objects)
            self.support_checks.append(dict(slot=slot['name'], model=name, kind=kind, base_height_m=actual[2][0], top_height_m=actual[2][1], support_height_m=h, suspended_offset_m=suspended))
            self.find_records.append(dict(slot=slot['name'], objects=objects, root=root, hidden=hidden, discovery=discovery))

    # ---- review-only content ----
    def cart_preview(self):
        with bpy.data.libraries.load(str(CART), link=False) as (src, dst): dst.objects = src.objects
        objects = [o for o in dst.objects if o and o.type in ('MESH', 'EMPTY')]
        for o in objects:
            self.preview.objects.link(o); o['preview_only'] = True
            if 'module_id' in o: del o['module_id']
        roots = [o for o in objects if o.parent is None]
        assert len(roots) == 1
        roots[0].location = (0, -1.20, .024); roots[0].rotation_euler.z = math.pi
        self.preview_cart = objects

    def preview_setup(self):
        self.begin('THStage_PreviewFloor')
        obj = self.box('Floor', (0, 5.5, -.04), (5, 11, .08), 0, .015, self.floor)
        self.runtime.objects.unlink(obj); self.preview.objects.link(obj); obj.parent = None
        self.scene.world = bpy.data.worlds.new('THStage_PreviewWorld'); self.scene.world.use_nodes = True
        bg = next(n for n in self.scene.world.node_tree.nodes if n.type == 'BACKGROUND')
        bg.inputs['Color'].default_value = (.12, .15, .17, 1); bg.inputs['Strength'].default_value = .45
        try: self.scene.render.engine = 'CYCLES'; self.scene.cycles.samples = 24
        except TypeError: pass
        for name, pos, power, size, color in [('SoftSky', (0, -4, 7), 1700, 8, (.66, .8, 1)), ('LiftWarm', (-3, 0, 4), 800, 4, (1, .65, .35)), ('RearRim', (2, -11, 4), 1100, 5, (.5, .8, .78))]:
            data = bpy.data.lights.new('THStage_' + name, 'AREA'); data.energy = power; data.shape = 'DISK'; data.size = size; data.color = color
            obj = bpy.data.objects.new(data.name, data); self.preview.objects.link(obj); obj.location = pos
            obj.rotation_euler = (Vector((0, -5, .2)) - obj.location).to_track_quat('-Z', 'Y').to_euler()
        camera = bpy.data.cameras.new('THStage_PreviewCamera'); obj = bpy.data.objects.new(camera.name, camera); self.preview.objects.link(obj); self.scene.camera = obj
        obj.location = (9, 6, 10); obj.rotation_euler = (Vector((0, -5.1, 0)) - obj.location).to_track_quat('-Z', 'Y').to_euler()
        camera.type = 'ORTHO'; camera.ortho_scale = 13.9
        self.scene.render.resolution_x = 1550; self.scene.render.resolution_y = 1250
        self.scene.render.image_settings.file_format = 'PNG'; self.scene.render.resolution_percentage = 100

    # ---- export ----
    def export(self):
        bpy.context.view_layer.update()
        for obj in self.modules:
            box = bounds([obj]); assert box[0][0] >= -2.5 - 1e-5 and box[0][1] <= 2.5 + 1e-5, (obj.name, box)
            assert box[1][0] >= -11 - 1e-5 and box[1][1] <= 1e-5, (obj.name, box)
            assert box[2][1] < .8, (obj.name, box)
        runtime = list(self.runtime.objects)
        activate(runtime)
        fbx = dict(axis_forward='-Z', axis_up='Y', apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS', use_custom_props=True,
                   path_mode='COPY', embed_textures=True, bake_anim=False, add_leaf_bones=False)
        bpy.ops.export_scene.fbx(filepath=str(OUT / 'ExcavationDepot_5x11.fbx'), use_selection=True, object_types={'MESH', 'EMPTY'}, **fbx)
        # Each module is separately replaceable; preserve assembled local coordinates.
        (OUT / 'modules').mkdir(exist_ok=True)
        for obj in self.modules:
            activate([obj])
            bpy.ops.export_scene.fbx(filepath=str(OUT / 'modules' / (obj['module_id'] + '.fbx')), use_selection=True, object_types={'MESH'}, **fbx)
        self.colliders.append(dict(name='VenueFloor', center_unity_xyz=[0, -.025, 5.5], size_unity_xyz=[5, .05, 11], rotation_unity_xyz=[0, 0, 0]))
        metadata = dict(venue=dict(width_m=5, length_m=11, origin='center of entrance edge', blender_from_unity='(X, -Z, Y)',
                                   warning_distance_authoring_assumption_m=.4, spawn_edge_extra_margin_m=.3),
                        runtime_stats=stats(runtime), preview_treasure_stats=stats(self.preview_treasures),
                        module_stats=[dict(name=o['module_id'], **stats([o])) for o in self.modules],
                        colliders=self.colliders, markers=self.markers,
                        collider_status='Conservative placement envelopes only; split to fit visible solids with Unity Editor API before gameplay use.',
                        sample_placements=self.sample_placements,
                        preview_only='floor, 23 treasures, existing cart, camera, lights; not in stage FBX',
                        unity_imported=False, pico_fps_measured=False,
                        revision=7,
                        navigation={'type': 'Low offering altars, central ruin, west caches and rear offerings; cart delivery route via offset openings',
                                    'door_clear_width_m': 1.70, 'cart_dimensions_m': [.816, 1.204],
                                    'cart_clearance_proxy_radius_m': .80, 'maze_is_visual_and_cargo_geometry_only': True,
                                    'human_collision_or_forced_locomotion_implemented': False},
                        preview_cart_stats=stats(self.preview_cart),
                        collection_change='0.024m receiver floor, frontal roll-in; no scoring or interaction implementation',
                        ground_decoration={'max_height_m': .004, 'collider': False, 'cast_shadows': False, 'full_floor': False,
                                           'description': 'Local opaque soil and paving fragments at wall feet; keep passthrough in open paths.'},
                        supported_placements=self.support_checks,
                        spawn_rule='23 authored model-specific supported anchors. Do not randomly swap incompatible shapes.',
                        navigation_status='Recheck with added support solids; previous revision cart paths are not a guarantee.',
                        search_design={'hidden_target_count': len(HIDDEN), 'exposed_cues': 23 - len(HIDDEN),
                                       'mechanism': 'Roofed masonry niches whose entrance-facing side is closed. Rear-row niches open onto a walkable back aisle; west-column niches open onto the west aisle.',
                                       'verification': 'search_visibility.json (check_search_visibility.py)'})
        (OUT / 'stage_layout.json').write_text(json.dumps(metadata, indent=2, ensure_ascii=False), encoding='utf-8')
        # A clean standalone runtime scene and a separately labelled review scene.
        runtime_scene = bpy.data.scenes.new('TH_Stage_Runtime_5x11'); runtime_scene.collection.children.link(self.runtime)
        runtime_scene.unit_settings.system = 'METRIC'; runtime_scene.unit_settings.scale_length = 1
        bpy.data.libraries.write(str(OUT / 'ExcavationDepot_5x11.blend'), {runtime_scene}, fake_user=True, compress=True)
        bpy.data.scenes.remove(runtime_scene)
        bpy.data.libraries.write(str(OUT / 'Stage_Review.blend'), {self.scene}, fake_user=True, compress=True)
        print(json.dumps(dict(stage=metadata['runtime_stats'], treasures=metadata['preview_treasure_stats'], modules=len(self.modules)), indent=2))
        # floorplan.svg is written by check_context_paths.py from the measured routes.


def run():
    old = bpy.context.window.scene; selected = list(bpy.context.selected_objects); active = bpy.context.view_layer.objects.active; original = set(bpy.data.objects)
    try:
        b = StageBuilder(); b.lift(); b.maze(); b.placement(); b.preview_items(); b.cart_preview(); b.preview_setup(); b.export()
        return b
    finally:
        bpy.context.window.scene = old; bpy.ops.object.select_all(action='DESELECT')
        for o in selected: o.select_set(True)
        bpy.context.view_layer.objects.active = active
        assert original.issubset(set(bpy.data.objects))


if __name__ == '__main__': run()
