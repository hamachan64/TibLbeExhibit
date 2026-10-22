"""Look sample for the 2026-10-21 direction: one excavated ruin room behind tall
walls, with expedition gear and five treasures placed on things that host them.

For image alignment only (not the stage build). Headless, throwaway session:
  blender -b --factory-startup --python sample_room.py
Writes Stage/Sample/*.jpg (and Sample/SampleRoom.blend, which is not committed). Procedural shader noise
is used for the look test; the real stage will bake to the shared atlas for Unity.
Logical coordinates (x across, d away from the entrance, h up) -> Blender (x, -d, h).
"""
import bpy, math, random
from pathlib import Path
from mathutils import Vector

HERE = Path(__file__).resolve().parent
OUT = HERE / 'Sample'; OUT.mkdir(exist_ok=True)
TREASURES = HERE.parent / 'Treasures'
WALL_H = 2.4          # starting value; open decision (plan 12)
DOOR_W = 1.2
rng = random.Random(1021)
scene = bpy.context.scene
for o in list(scene.objects): bpy.data.objects.remove(o, do_unlink=True)


# ---------------- materials (look test; procedural noise) ----------------
def mat(name, color, rough=.85, metal=0., noise=.0, scale=6., emit=0., emit_color=None):
    m = bpy.data.materials.new(name); m.use_nodes = True
    nt = m.node_tree; bs = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    bs.inputs['Roughness'].default_value = rough; bs.inputs['Metallic'].default_value = metal
    if noise:
        # World-space mottling (so neighbouring blocks differ) and grime toward the floor.
        geo = nt.nodes.new('ShaderNodeNewGeometry')
        tex = nt.nodes.new('ShaderNodeTexNoise'); tex.inputs['Scale'].default_value = scale * .6; tex.inputs['Detail'].default_value = 10
        tex.inputs['Roughness'].default_value = .65
        nt.links.new(geo.outputs['Position'], tex.inputs['Vector'])
        ramp = nt.nodes.new('ShaderNodeValToRGB'); ramp.color_ramp.elements[0].position = .35; ramp.color_ramp.elements[1].position = .7
        c = Vector(color[:3])
        ramp.color_ramp.elements[0].color = (*(c * (1 - noise)), 1); ramp.color_ramp.elements[1].color = (*(c * (1 + noise * .5)), 1)
        nt.links.new(tex.outputs['Fac'], ramp.inputs['Fac'])
        sep = nt.nodes.new('ShaderNodeSeparateXYZ'); nt.links.new(geo.outputs['Position'], sep.inputs['Vector'])
        grime = nt.nodes.new('ShaderNodeMapRange'); grime.inputs['From Min'].default_value = 0; grime.inputs['From Max'].default_value = .6
        grime.inputs['To Min'].default_value = .55; grime.inputs['To Max'].default_value = 1
        nt.links.new(sep.outputs['Z'], grime.inputs['Value'])
        mix = nt.nodes.new('ShaderNodeMix'); mix.data_type = 'RGBA'; mix.blend_type = 'MULTIPLY'; mix.inputs['Factor'].default_value = 1
        nt.links.new(ramp.outputs['Color'], mix.inputs['A'])
        g2 = nt.nodes.new('ShaderNodeCombineColor'); [nt.links.new(grime.outputs['Result'], g2.inputs[i]) for i in range(3)]
        nt.links.new(g2.outputs['Color'], mix.inputs['B'])
        nt.links.new(mix.outputs['Result'], bs.inputs['Base Color'])
    else:
        bs.inputs['Base Color'].default_value = (*color[:3], 1)
    if emit:
        bs.inputs['Emission Color'].default_value = (*(emit_color or color)[:3], 1); bs.inputs['Emission Strength'].default_value = emit
    return m


STONE = mat('Stone', (.27, .245, .205), noise=.5, scale=7)
STONE2 = mat('Stone2', (.32, .29, .24), noise=.5, scale=5)
STONE3 = mat('Stone3', (.22, .215, .19), noise=.55, scale=9)
STONE_DK = mat('StoneDark', (.14, .13, .11), noise=.5, scale=8)
MOSS = mat('Moss', (.12, .14, .07), noise=.5, scale=20)
ROOT = mat('Root', (.11, .08, .05), noise=.3, scale=10)
CRACK = mat('Crack', (.03, .03, .025), rough=1)
PLASTER = mat('Plaster', (.40, .36, .29), noise=.45, scale=4)
PAINT = mat('FadedPaint', (.36, .15, .09), noise=.4, scale=5)      # old wall-painting band
PAINT2 = mat('FadedPaint2', (.10, .22, .22), noise=.4, scale=5)
WOOD = mat('Wood', (.36, .24, .13), noise=.4, scale=14)
WOOD_LT = mat('WoodLight', (.52, .38, .22), noise=.35, scale=14)
STRAW = mat('Straw', (.62, .50, .26), noise=.5, scale=30)
CANVAS = mat('Canvas', (.36, .38, .30), noise=.25, scale=8)
METAL = mat('PaintedMetal', (.16, .20, .18), rough=.6, metal=.4)
BLACK = mat('Rubber', (.03, .03, .03), rough=.9)
TRAY = mat('TrayPlastic', (.10, .11, .12), rough=.6)
TAG = mat('Tag', (.85, .82, .70), rough=.9)
LAMP = mat('LampGlow', (1., .62, .25), emit=6.)
SCREEN = mat('ScreenGlow', (.05, .9, .75), emit=3.)
FLOOR = mat('PreviewRealFloor', (.26, .27, .28), rough=.9)      # stands in for the passthrough floor


def ob(o, m):
    o.data.materials.append(m)
    return o


def box(pos, size, m, rot=0, bevel=.012):
    x, d, h = pos
    bpy.ops.mesh.primitive_cube_add(size=1, location=(x, -d, h))
    o = bpy.context.object; o.scale = size; o.rotation_euler.z = rot
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel:
        mod = o.modifiers.new('bevel', 'BEVEL'); mod.width = min(bevel, min(size) * .25); mod.segments = 2
    return ob(o, m)


def cyl(pos, r, depth, m, verts=16, rot=(0, 0, 0)):
    x, d, h = pos
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=depth, location=(x, -d, h), rotation=rot)
    return ob(bpy.context.object, m)


# ---------------- tall ruin walls: irregular courses of ashlar ----------------
STONES = (STONE, STONE, STONE2, STONE3)


def courses(h):
    out, z = [], 0.0
    first = True
    while z < h - .05:
        ch = .22 if first else rng.choice((.26, .30, .34, .40))
        ch = min(ch, h - z); out.append((z, ch)); z += ch; first = False
    return out


def wall(x0, d0, x1, d1, thick, h=WALL_H, gaps=(), broken=0.0, seed=0, holes=0.0):
    """Wall along x (d0==d1) or d (x0==x1). gaps: (start, end, sill, head) along the wall.
    Openings are cut by whole courses, then closed exactly with a back stone (niches),
    a sill fill and a lintel, so no light leaks around them."""
    along_x = d0 == d1; length = abs(x1 - x0) if along_x else abs(d1 - d0)
    start = min(x0, x1) if along_x else min(d0, d1)
    local = random.Random(seed)
    cs = courses(h)
    removed = {}
    for r, (z0, ch) in enumerate(cs):
        t = -local.uniform(0, .45) if r else 0.0
        while t < length - 1e-6:
            span = local.uniform(.38, .95)
            a, b = max(0, t), min(length, t + span); t += span
            if b - a < .06: continue
            # Ragged broken top, worse toward one end.
            loss = broken * max(0.0, (a + b) / 2 / length - .35) * 1.6 + local.uniform(0, .12) * broken
            if r >= 3 and z0 + ch > h - loss: continue
            if 2 <= r < len(cs) - 2 and local.random() < holes: continue   # fallen-out block
            cz = z0 + ch / 2; pieces = [(a, b)]
            for g0, g1, sill, head in gaps:
                ga, gb = g0 - start, g1 - start
                if z0 < head and z0 + ch > sill:
                    removed.setdefault((g0, g1, sill, head), []).append((z0, z0 + ch))
                    nxt = []
                    for s0, s1 in pieces:
                        if s1 <= ga or s0 >= gb: nxt.append((s0, s1)); continue
                        if s0 < ga: nxt.append((s0, ga))
                        if s1 > gb: nxt.append((gb, s1))
                    pieces = nxt
            for s0, s1 in pieces:
                if s1 - s0 < .05: continue
                m = start + (s0 + s1) / 2; jit = local.uniform(-.015, .015); th = thick - local.uniform(0, .04)
                p = (m, d0 + jit, cz) if along_x else (x0 + jit, m, cz)
                sz = (s1 - s0 - .014, th, ch - .014) if along_x else (th, s1 - s0 - .014, ch - .014)
                mat_ = STONE_DK if r == 0 else local.choice(STONES)
                box(p, sz, mat_, bevel=local.uniform(.015, .04))
    # Projecting foundation course along the base (grime band).
    p = ((start + length / 2), d0, .05) if along_x else (x0, start + length / 2, .05)
    sz = (length, thick + .07, .10) if along_x else (thick + .07, length, .10)
    for g0, g1, sill, head in gaps:
        if sill < 0: break
    else:
        box(p, sz, STONE_DK, bevel=.02)
    # Close each opening exactly.
    for (g0, g1, sill, head), spans in removed.items():
        zr0, zr1 = min(s[0] for s in spans), max(s[1] for s in spans)
        mid = (g0 + g1) / 2; w = g1 - g0
        def put(z_lo, z_hi, depth, offset, m_, bev=.02):
            if z_hi - z_lo < .01: return
            p = (mid, d0 + offset, (z_lo + z_hi) / 2) if along_x else (x0 + offset, mid, (z_lo + z_hi) / 2)
            sz = (w + .02, depth, z_hi - z_lo) if along_x else (depth, w + .02, z_hi - z_lo)
            box(p, sz, m_, bevel=bev)
        if sill >= 0:  # niche: back stone, sill, lintel
            put(sill, head, .12, -(thick / 2 - .06), STONE_DK, 0)           # back (outer side, -x / -d)
            put(zr0, sill, thick, 0, STONE_DK, .015)                         # below the sill
            put(sill - .04, sill, thick + .04, .02, STONE, .01)              # sill stone, slight lip
        put(head, max(zr1, head + .18), thick + .04, 0, STONE_DK, .03)       # lintel over niche / door
    return


# Room: inner x -2.19..0.75, d 3.75..6.75. Corridor east of it, inner x 1.05..2.18 (venue edge 2.5).
T = .30
NICHES = [(4.25, 4.75, .95, 1.40), (5.35, 5.85, .95, 1.40), (6.0, 6.4, .50, .86)]
wall(-2.4, 3.6, 0.9, 3.6, T, broken=1.3, seed=1)                                 # south (entrance side) - closed
wall(-2.4, 6.9, 0.9, 6.9, T, broken=.7, seed=2, holes=.05)                                # north
wall(-2.4, 3.6, -2.4, 6.9, .42, gaps=NICHES, broken=.45, seed=3)                # west, niches
wall(0.9, 3.6, 0.9, 6.9, T, gaps=[(4.2, 4.2 + DOOR_W, -1, 2.05)], broken=.6, seed=4)  # east, doorway
wall(2.33, 3.6, 2.33, 6.9, T, broken=1.4, seed=5, holes=.06)                               # corridor outer wall
# Fallen blocks at the feet of the most broken walls (outside the walking line).
for x, d, r_ in [(2.12, 5.2, .3), (2.05, 6.3, -.5), (-.3, 3.38, .2), (.35, 3.33, -.8)]:
    box((x, d, .13), (.42, .28, .26), rng.choice(STONES), rot=r_, bevel=.04)
# Faded painted frieze and plaster remains on the north wall (inner face d 6.75).
for i in range(9):
    box((-1.95 + i * .32, 6.735, 1.62), (.28, .02, .16), PAINT if i % 2 == 0 else PAINT2, bevel=0)
box((-.65, 6.735, 1.50), (2.9, .02, .03), PAINT2, bevel=0)
for x, z, w, hh in [(-1.8, 1.0, .7, .6), (.2, .7, .5, .45)]: box((x, 6.735, z), (w, .02, hh), PLASTER, bevel=0)

# ---------------- ruin furniture ----------------
def altar(x, d):
    box((x, d, .06), (1.3, .7, .12), STONE_DK)
    box((x, d, .45), (1.1, .55, .66), STONE)
    box((x, d, .82), (1.24, .66, .08), STONE_DK)
    return .86
ALTAR_TOP = altar(-.35, 6.35)


def sarcophagus(x, d):
    box((x, d, .05), (2.0, .82, .10), STONE_DK)
    for side in (-1, 1): box((x, d + side * .35, .38), (1.86, .10, .58), STONE)
    for side in (-1, 1): box((x + side * .88, d, .38), (.10, .62, .58), STONE)
    box((x, d, .13), (1.66, .60, .06), STONE_DK)
    # Carved side panels, rim and feet so it reads as a sarcophagus, not a box.
    for side in (-1, 1):
        for i in (-1, 0, 1):
            box((x + i * .56, d + side * .405, .38), (.44, .02, .36), STONE_DK, bevel=.005)
        box((x, d + side * .40, .66), (1.9, .03, .05), STONE2, bevel=.008)
    for sx in (-1, 1):
        for sd in (-1, 1): box((x + sx * .86, d + sd * .33, .05), (.22, .22, .10), STONE3)
    lid = box((x + .35, d - .18, .72), (1.9, .78, .10), STONE_DK, rot=.28)   # slid aside
    return .16
SARC_FLOOR = sarcophagus(-.55, 4.95)

# ---------------- expedition gear ----------------
def work_table(x, d):
    box((x, d, .76), (1.4, .7, .04), WOOD_LT)
    for sx in (-1, 1):
        for sd in (-1, 1): box((x + sx * .64, d + sd * .29, .38), (.04, .04, .74), METAL, bevel=.005)
    trays = []
    for i, tx in enumerate((-.35, .25)):
        box((x + tx, d - .02, .80), (.48, .34, .045), TRAY, bevel=.006)
        box((x + tx - .17, d - .17, .825), (.09, .002, .05), TAG, bevel=0)
        trays.append((x + tx, d - .02, .785))
    # field scanner with a bright readout (angular machine + bright display)
    box((x + .55, d + .18, .84), (.22, .16, .12), METAL)
    box((x + .55, d + .095, .86), (.16, .01, .07), SCREEN, bevel=0)
    return trays
TRAYS = work_table(0.0, 4.15)


def lamp(x, d, h=1.55):
    for a in range(3):
        ang = a * math.tau / 3
        tip = Vector((x, -d, h)); foot = Vector((x + .32 * math.cos(ang), -d + .32 * math.sin(ang), 0))
        mid = (tip + foot) / 2; v = tip - foot
        bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=.012, depth=v.length, location=mid)
        o = bpy.context.object; o.rotation_euler = v.to_track_quat('Z', 'Y').to_euler(); ob(o, METAL)
    box((x, d, h + .06), (.20, .12, .14), METAL)
    box((x, d - .065, h + .06), (.16, .01, .10), LAMP, bevel=0)
    light = bpy.data.lights.new('WorkLamp', 'SPOT'); light.energy = 480; light.color = (1, .7, .45); light.spot_size = 1.6
    lo = bpy.data.objects.new('WorkLamp', light); scene.collection.objects.link(lo); lo.location = (x, -d + .1, h + .06)
    lo.rotation_euler = (math.radians(65), 0, math.radians(200))
lamp(.55, 6.4)
lamp(-1.35, 3.95, 1.3)


def crate(x, d, z, s=(.62, .48, .44), open_top=False, rot=0):
    w, l, hh = s
    if open_top:
        box((x, d, z + .02), (w, l, .04), WOOD, rot=rot)
        for sx in (-1, 1): box((x + sx * (w / 2 - .02), d, z + hh / 2), (.04, l, hh), WOOD, rot=rot)
        for sd in (-1, 1): box((x, d + sd * (l / 2 - .02), z + hh / 2), (w, .04, hh), WOOD, rot=rot)
        box((x, d, z + hh * .55), (w - .08, l - .08, .04), STRAW, rot=rot, bevel=0)
        return z + hh * .57
    box((x, d, z + hh / 2), (w, l, hh), WOOD, rot=rot)
    for sd in (-1, 1): box((x, d + sd * (l / 2 + .005), z + hh / 2), (w - .1, .01, .05), WOOD_LT, rot=rot, bevel=0)
    box((x - w * .2, d - l / 2 - .006, z + hh * .7), (.12, .004, .07), TAG, rot=rot, bevel=0)
    return z + hh


box((-1.75, 6.1, .07), (1.0, .8, .14), WOOD, bevel=.01)                    # pallet
top = crate(-1.95, 6.1, .14); crate(-1.95, 6.1, top, s=(.5, .4, .36), rot=.15)
STRAW_TOP = crate(-1.45, 6.15, .14, s=(.56, .50, .46), open_top=True)
box((.45, 5.9, .02), (.9, .6, .03), CANVAS, rot=.2, bevel=0)                # tarp on the floor patch
cable = None

# Corridor: a generator with a lit readout and coiled cable.
box((1.6, 4.0, .22), (.38, .55, .44), METAL)
box((1.6, 3.72, .30), (.24, .01, .12), SCREEN, bevel=0)
cyl((1.6, 4.0, .48), .06, .08, BLACK)


# ---------------- ruin damage (2nd look pass: more ruined, darker) ----------------
def rubble(cx, cd, w, l, h, n, seed):
    r_ = random.Random(seed)
    for i in range(n):
        t = i / max(1, n - 1)
        x = cx + r_.uniform(-w / 2, w / 2); d = cd + r_.uniform(-l / 2, l / 2)
        size = (r_.uniform(.18, .45), r_.uniform(.15, .32), r_.uniform(.12, .26))
        z = size[2] / 2 + h * (1 - abs(x - cx) / (w / 2 + .01)) * r_.uniform(.2, .9)
        box((x, d, z), size, r_.choice(STONES + (STONE_DK,)), rot=r_.uniform(-.8, .8), bevel=r_.uniform(.02, .05))
rubble(1.65, 6.45, .95, .55, .55, 16, 7)       # collapsed corridor end
rubble(-1.95, 3.98, .40, .35, .30, 7, 8)       # room corner fall
# Small stones and sherds along wall feet (kept off the walking line).
r2 = random.Random(9)
for x, d in [(-2.1, 4.9), (-2.12, 5.2), (-2.05, 6.6), (.62, 5.6), (.66, 5.9), (-.8, 3.82), (1.15, 5.8), (2.1, 4.4), (2.12, 3.9), (1.2, 4.0)]:
    box((x, d, .03), (r2.uniform(.05, .14), r2.uniform(.05, .12), r2.uniform(.03, .06)), r2.choice(STONES), rot=r2.uniform(0, 3), bevel=.01)
# Damp moss stains low on the wall faces (thin, on the surface; nothing floats).
for x, d, z, w, hh, along in [(-2.185, 4.0, .35, .7, .5, False), (-2.185, 6.5, .25, .5, .35, False), (-1.3, 6.745, .3, .9, .45, True),
                              (.885, 5.9, .4, .6, .6, False), (2.175, 4.6, .3, .8, .5, False), (-.2, 3.755, .3, .6, .4, True)]:
    box((x, d, z), (w, .012, hh) if along else (.012, w, hh), MOSS, bevel=0)
# Cracks on wall faces.
for x, d, z, hh, along in [(-2.18, 5.0, 1.15, .7, False), (-.9, 6.74, .9, 1.2, True), (.89, 6.0, 1.3, .8, False), (-.4, 3.76, 1.1, .7, True), (2.18, 4.4, 1.0, 1.1, False)]:
    box((x, d, z), (.012, .02, hh) if along else (.02, .012, hh), CRACK, bevel=0)
# Toppled crate and loose planks.
box((-1.0, 6.25, .2), (.5, .4, .4), WOOD, rot=.9)
for i, (x, d, r_) in enumerate([(-.55, 5.75, .3), (-.35, 5.85, 1.2), (.15, 6.0, -.4)]):
    box((x, d, .015), (.7, .1, .025), WOOD, rot=r_, bevel=.004)

# Preview floor (stands in for the real venue floor seen in passthrough).
box((0, 5.25, -.02), (5, 4.5, .04), FLOOR, bevel=0)


# ---------------- treasures on their hosts ----------------
def place(name, host_xyz, rot=0.0, face=None):
    with bpy.data.libraries.load(str(TREASURES / (name + '.blend')), link=False) as (src, dst): dst.objects = src.objects
    objs = [o for o in dst.objects if o]
    for o in objs: scene.collection.objects.link(o)
    root = next(o for o in objs if o.type == 'EMPTY' and o.parent is None)
    root.rotation_euler.z = rot; root.location = (0, 0, 0); bpy.context.view_layer.update()
    pts = [o.matrix_world @ Vector(c) for o in objs if o.type == 'MESH' for c in o.bound_box]
    cx = (min(p.x for p in pts) + max(p.x for p in pts)) / 2; cy = (min(p.y for p in pts) + max(p.y for p in pts)) / 2
    x, d, h = host_xyz
    root.location = (x - cx, -d - cy, h - min(p.z for p in pts))
    return root


place('silver_mirror', (-2.34, 4.5, .95), rot=math.pi / 2)      # wall niche, faces into the room (+x)
place('bronze_amulet_disc', (-2.34, 5.6, .95), rot=math.pi / 2) # second niche
place('bronze_figure', (-.78, 6.33, ALTAR_TOP), rot=math.pi)     # on the altar, faces back toward the room
place('gilded_sun_disc', (-.12, 6.36, ALTAR_TOP), rot=math.pi)   # on the altar beside the figure
place('gold_mask', (-1.24, 4.98, SARC_FLOOR), rot=math.pi)        # inside the opened sarcophagus, at the end the lid uncovers
place('clay_offering_bowl', (TRAYS[1][0], TRAYS[1][1], TRAYS[1][2]), rot=math.pi)  # in a finds tray on the work table
place('clay_pot', (TRAYS[0][0], TRAYS[0][1], TRAYS[0][2]), rot=math.pi)  # in the other finds tray
place('clay_amphora', (-1.45, 6.15, STRAW_TOP), rot=0)           # in an open crate on straw

# ---------------- light and cameras ----------------
world = bpy.data.worlds.new('VenueLight'); scene.world = world; world.use_nodes = True
bg = next(n for n in world.node_tree.nodes if n.type == 'BACKGROUND'); bg.inputs['Color'].default_value = (.42, .46, .52, 1); bg.inputs['Strength'].default_value = .16
sun = bpy.data.lights.new('HallLight', 'SUN'); sun.energy = .45; sun.angle = .3; so = bpy.data.objects.new('HallLight', sun); scene.collection.objects.link(so)
so.rotation_euler = (math.radians(35), math.radians(10), math.radians(30))
try: scene.render.engine = 'BLENDER_EEVEE'
except TypeError: scene.render.engine = 'BLENDER_EEVEE_NEXT'
try: scene.eevee.taa_render_samples = 48
except AttributeError: pass
scene.view_settings.view_transform = 'Standard'
scene.render.image_settings.file_format = 'JPEG'; scene.render.image_settings.quality = 85
for attr in ('use_gtao', 'use_shadows', 'use_raytracing'):
    try: setattr(scene.eevee, attr, True)
    except AttributeError: pass
cam_data = bpy.data.cameras.new('Cam'); cam = bpy.data.objects.new('Cam', cam_data); scene.collection.objects.link(cam); scene.camera = cam


def shot(name, loc, target, lens=20, size=(1600, 1000), ortho=None):
    cam.location = Vector((loc[0], -loc[1], loc[2]))
    cam.rotation_euler = (Vector((target[0], -target[1], target[2])) - cam.location).to_track_quat('-Z', 'Y').to_euler()
    cam_data.type = 'ORTHO' if ortho else 'PERSP'
    if ortho: cam_data.ortho_scale = ortho
    else: cam_data.lens = lens
    scene.render.resolution_x, scene.render.resolution_y = size
    scene.render.filepath = str(OUT / (name + '.jpg')); bpy.ops.render.render(write_still=True)


shot('1_corridor_from_entrance', (1.78, 2.6, 1.6), (1.6, 6.5, 1.2), lens=18)       # tall walls hide the room
shot('2_doorway_into_room', (1.45, 4.8, 1.6), (-1.2, 5.3, .8), lens=16)            # what you see at the door
shot('3_inside_niche_wall', (.62, 4.75, 1.6), (-2.4, 5.2, 1.0), lens=18)             # niches and sarcophagus
shot('4_inside_altar', (.45, 4.9, 1.6), (-.6, 6.6, .9), lens=18)                     # altar, crates, lamp pool
shot('5_overview', (5.5, -1.0, 6.5), (-.3, 5.3, .5), lens=24, size=(1600, 1100))  # no ceiling, layout
bpy.ops.wm.save_as_mainfile(filepath=str(OUT / 'SampleRoom.blend'), compress=True)
print('sample done')
