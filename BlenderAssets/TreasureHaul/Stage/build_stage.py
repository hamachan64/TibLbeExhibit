"""Treasure Haul 5 x 11 m MR stage: the inside of a ruin under excavation ("B. ring corridor").

Headless, nothing but files is written:
  blender -b --factory-startup --python build_stage.py
Writes Stage/Output/: ExcavationDepot_5x11.fbx/.blend (stage + markers), modules/*.fbx,
Stage_Review.blend (stage + 23 preview treasures + lights + camera), stage_layout.json,
textures/Stage_Atlas_{BaseColor,Metallic,Roughness,Emission}.png.

Layout (Unity X = across, Z = depth; Blender (X, -depth, up); origin = centre of the entrance edge, floor):
  entrance zone (depth 0-3.2, no tall walls): extraction lift, practice row, camp gear
  2.4 m ruin walls, no ceiling: a central block of two rooms (front room door west, back room door east),
  a corridor on each side, a cross corridor behind the block, and a hall at the back (two side bays).
  Treasure slots (EMPTY 'TreasureSlot_NN') sit on hosts that give a reason to be there:
  niches, altars, stone shelves, finds trays, open crates, a sarcophagus, an ossuary, plinths, pallets.
Look: dark weathered ashlar, broken tops, moss, faded murals; expedition gear with work lamps and bright
screens. One shared atlas material (1024 px, 128 px cells); emission only on lamps and screens.
No external assets, downloads or generators. Logical coordinates in this file are (x, d, h).
"""
import bpy, bmesh, json, math, random
import numpy as np
from pathlib import Path
from mathutils import Vector, Matrix

HERE = Path(__file__).resolve().parent
OUT = HERE / 'Output'
TEX = OUT / 'textures'
TREASURES = HERE.parent / 'Treasures'

# ---------------------------------------------------------------- layout
WALL_H = 2.4
OUTER_X, OUTER_T, OUTER_D0, BACK_D, BACK_T = 2.38, .20, 3.2, 10.9, .20
# Block outer face 1.08 (not 1.10): corridors are 1.20 m between nominal faces, so the clear width stays
# >= 1.15 m with the stone relief (about +-0.01 m) and the niche sill lips (0.025 m).
BLOCK_X, BLOCK_D0, BLOCK_D1, BLOCK_T, CHAMFER = 1.08, 3.9, 8.2, .20, .30
PART_D, PART_T = 6.05, .30                  # block partition (front room / back room)
FRONT_DOOR, BACK_DOOR = (4.7, 5.9), (6.6, 7.8)
HALL_D, HALL_T, HALL_OPEN = 9.5, .20, 1.05  # hall partitions: centre depth, thickness, half opening
NICHE_W, NICHE_H, NICHE_SILL, LIP = .40, .44, .95, .025
NEIGHBOUR_M = 1.5   # selection rule: no two finds of the same model on slots closer than this (horizontal)


def L(x, d, h=0.0):
    """Logical (x, depth, height) -> Blender vector."""
    return Vector((x, -d, h))


# ---------------------------------------------------------------- atlas
CELL, AW, AH = 128, 1024, 1024
# name: (col, row, width, height) in 128 px cells. Row 0 is the bottom of the image (UV v = 0).
TILES = dict(
    stone_a=(0, 0, 2, 2), stone_b=(2, 0, 2, 2), stone_c=(4, 0, 2, 2), stone_damp=(6, 0, 2, 2),
    stone_dust=(0, 2, 2, 2), stone_dark=(2, 2, 2, 2), relief=(4, 2, 2, 2), mural_a=(6, 2, 2, 1), mural_b=(6, 3, 2, 1),
    plaster=(0, 4, 1, 1), wood_dark=(1, 4, 1, 1), wood_light=(2, 4, 1, 1), straw=(3, 4, 1, 1),
    canvas=(4, 4, 1, 1), metal_paint=(5, 4, 1, 1), metal_dark=(6, 4, 1, 1), plastic=(7, 4, 1, 1),
    tag=(0, 5, 1, 1), metal_bare=(1, 5, 1, 1), soil=(2, 5, 1, 1), moss=(3, 5, 1, 1),
    paving=(4, 5, 1, 1), glow_lamp=(5, 5, 1, 1), glow_screen=(6, 5, 1, 1), hazard=(7, 5, 1, 1),
    amber=(0, 6, 1, 1), teal_paint=(1, 6, 1, 1))
GLOW = {'glow_lamp', 'glow_screen'}
INSET = 3.0  # px


def tile_uv(name, u, v):
    c, r, w, h = TILES[name]
    u = min(1.0, max(0.0, u)); v = min(1.0, max(0.0, v))
    u0 = (c * CELL + INSET) / AW; u1 = ((c + w) * CELL - INSET) / AW
    v0 = (r * CELL + INSET) / AH; v1 = ((r + h) * CELL - INSET) / AH
    return (u0 + (u1 - u0) * u, v0 + (v1 - v0) * v)


def vnoise(h, w, cy, cx, rng):
    """Tileable value noise, h x w pixels, cy x cx lattice."""
    g = rng.random((cy, cx))

    def axis(n, c):
        t = np.arange(n) * c / n; i0 = np.floor(t).astype(int); f = t - i0
        return i0 % c, (i0 + 1) % c, f * f * (3 - 2 * f)
    y0, y1, fy = axis(h, cy); x0, x1, fx = axis(w, cx)
    a, b, c, d = g[np.ix_(y0, x0)], g[np.ix_(y0, x1)], g[np.ix_(y1, x0)], g[np.ix_(y1, x1)]
    fx = fx[None, :]; fy = fy[:, None]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def fbm(h, w, rng, c=4, octaves=5):
    out = np.zeros((h, w)); amp = 1.0; tot = 0.0
    for o in range(octaves):
        out += amp * vnoise(h, w, c * 2 ** o, c * 2 ** o, rng); tot += amp; amp *= .5
    return out / tot


def sstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def build_atlas():
    """Paint the shared atlas. Colours are sRGB values (the image is sRGB)."""
    rng = np.random.default_rng(2026)
    base = np.zeros((AH, AW, 3), np.float32); metal = np.zeros((AH, AW), np.float32)
    rough = np.full((AH, AW), .9, np.float32); emit = np.zeros((AH, AW, 3), np.float32)

    def region(name):
        c, r, w, h = TILES[name]
        return slice(r * CELL, (r + h) * CELL), slice(c * CELL, (c + w) * CELL), h * CELL, w * CELL

    def coords(h, w):
        yy, xx = np.mgrid[0:h, 0:w]
        return (xx + .5) / w, (yy + .5) / h      # u, v in 0..1 (v = 0 at the bottom row)

    def put(name, col, met=0.0, rgh=.9, glow=None):
        ys, xs, h, w = region(name)
        base[ys, xs] = np.clip(col, 0, 1); metal[ys, xs] = met; rough[ys, xs] = np.clip(rgh, 0, 1)
        if glow is not None: emit[ys, xs] = np.clip(glow, 0, 1)

    # --- ashlar: each face maps the whole tile, so the dark border reads as the joint and the bevel faces
    # pick up the worn edge. Mottling, grain, pits; variants by tone, cracks, damp base, dust.
    def stone(name, tone, contrast=.30, crack=0, damp=0.0, dust=0.0, edge=.55, moss=0.0):
        ys, xs, h, w = region(name); U, V = coords(h, w)
        big = fbm(h, w, rng, 3, 5); mid = fbm(h, w, rng, 8, 4); fine = fbm(h, w, rng, 24, 3); grain = rng.random((h, w))
        val = 1 + contrast * (big - .5) * 2 + .16 * (mid - .5) * 2 + .08 * (fine - .5) * 2 + .04 * (grain - .5)
        val *= .66 + .34 * sstep(0, .65, V + .12 * (mid - .5))            # grime collects on the lower part of each face
        streak = vnoise(h, w, 2, 22, rng)                                   # water runs down from the top edge
        run = np.clip((streak - .70) * 4, 0, 1) * sstep(.55, 1.0, V)   # short stains under the top edge only
        val *= 1 - .12 * run
        bd = np.minimum.reduce([U, V * 1.3, 1 - U, 1 - V])
        border = .40 + .60 * sstep(0.0, .07 + .04 * fine, bd + .025 * (big - .5))
        val = val * (1 - edge + edge * border)
        flecks = vnoise(h, w, 60, 60, rng) > .9                             # sparse lighter mineral flecks
        val[flecks] *= 1.06
        col = np.array(tone)[None, None, :] * val[..., None]
        for k in range(crack):  # a dark, wandering hairline crack with a lit lip
            x0 = rng.uniform(.25, .75); amp = rng.uniform(.04, .09); ph = rng.uniform(0, 6)
            v0, v1 = sorted(rng.uniform(.1, .95, 2))
            line = np.abs(U - (x0 + amp * np.sin(V * 5 + ph) + .05 * (mid - .5))) < .0045
            line &= (V > v0) & (V < v1)
            col[line] *= .45
            col[np.roll(line, 1, axis=1) & ~line] *= 1.15
        if damp:   # rising damp, greenish-brown toward the floor
            m = (1 - sstep(.05, .75, V + .25 * (big - .5))) * damp
            col = col * (1 - .45 * m[..., None]) + np.array((.20, .22, .13)) * .45 * m[..., None]
        if moss:   # moss lodged in the lower edge
            m = sstep(.55, .75, fine + (1 - sstep(0, .2, V)) * .4) * moss
            col = col * (1 - m[..., None]) + np.array((.30, .36, .17)) * m[..., None] * (.8 + .4 * big[..., None])
        if dust:
            col = col * (1 - dust) + np.array((.66, .62, .54)) * dust * (.9 + .2 * big[..., None])
        put(name, col, 0.0, .86 + .1 * fine)

    stone('stone_a', (.35, .33, .29), crack=0, contrast=.36)
    stone('stone_b', (.38, .36, .31), crack=1, contrast=.34)
    stone('stone_c', (.30, .30, .28), crack=1, contrast=.38)
    stone('stone_damp', (.29, .27, .22), damp=1.0, moss=1.0)
    stone('stone_dust', (.52, .48, .41), contrast=.16, dust=.2, edge=.35)
    stone('stone_dark', (.22, .21, .19), contrast=.3, edge=.45)

    # carved relief: concentric "eye" roundels and a stepped border, incised (dark cut, lit lower lip)
    ys, xs, h, w = region('relief'); U, V = coords(h, w)
    big = fbm(h, w, rng, 3, 5)
    col = np.array((.32, .30, .26))[None, None, :] * (1 + .28 * (big - .5) * 2)[..., None]
    cut = np.zeros((h, w), bool)
    for cx in (.25, .75):
        r = np.hypot((U - cx) * 2, (V - .5) * 1.0)
        for rr in (.18, .30, .42): cut |= np.abs(r - rr) < .018
        cut |= r < .06
    step = np.floor(np.abs(((U * 4) % 1) - .5) * 8) / 8
    cut |= (np.abs(V - (.08 + step * .5)) < .012) | (np.abs(V - (.92 - step * .5)) < .012)
    cut |= (V < .03) | (V > .97) | (U < .015) | (U > .985)
    lip = np.roll(cut, -2, axis=0) & ~cut
    col[cut] *= .42; col[lip] *= 1.18
    bd = np.minimum.reduce([U, V, 1 - U, 1 - V]); col *= (.7 + .3 * sstep(0, .05, bd))[..., None]
    put('relief', col, 0, .88)

    # Painted friezes: faded mineral pigment on a thin lime wash over the stone, abstract and geometric
    # (the treasures' motifs: steps, concentric rings, wave-and-teeth). One tile = one 0.42 m period, so the
    # band continues across stones. Paint flakes off in patches; where it is gone the dark stone shows.
    def wash_ground(h, w):
        big = fbm(h, w, rng, 3, 5); fine = fbm(h, w, rng, 14, 3)
        col = np.array((.37, .34, .29))[None, None, :] * (1 + .18 * (big - .5) * 2 + .08 * (fine - .5))[..., None]
        return col, big

    ochre, slate, soot = np.array((.50, .30, .21)), np.array((.29, .36, .34)), np.array((.15, .13, .11))
    SS = 4; H4, W4 = CELL * SS, 2 * CELL * SS
    y4, x4 = np.mgrid[0:H4, 0:W4]; U4 = (x4 + .5) / W4; V4 = (y4 + .5) / H4; Uv = U4 * 2   # Uv: u in band-height units
    cover = lambda m: m.astype(np.float32).reshape(CELL, SS, 2 * CELL, SS).mean(axis=(1, 3))
    lines = ((V4 > .90) & (V4 < .94)) | ((V4 > .835) & (V4 < .855)) | ((V4 > .035) & (V4 < .06))
    teeth_ph = (U4 * 8) % 1.0
    teeth = (V4 > .08) & (V4 < .08 + .16 * (1 - np.abs(2 * teeth_ph - 1)))              # row of teeth (wave-and-teeth)
    wave = np.abs(V4 - (.29 + .035 * np.sin(2 * np.pi * U4 * 4))) < .018

    def paint(ground_big, layers):
        col, big = wash_ground(CELL, 2 * CELL)
        fade = sstep(.18, .40, fbm(CELL, 2 * CELL, rng, 6, 4)) * (.62 + .2 * big)
        for m, c in layers:
            k = (cover(m) * fade)[..., None]; col = col * (1 - k) + c * k
        lost = fbm(CELL, 2 * CELL, rng, 7, 4) < .24                                         # wash and paint gone
        col[lost] = col[lost] * .45 + np.array((.27, .25, .22)) * .55
        return col

    # A: stepped pyramids rising (ochre) and hanging (slate) between the border lines, over the wave-and-teeth
    ph = (U4 * 2) % 1.0; tri = 1 - np.abs(2 * ph - 1); st = np.floor(tri * 4) / 4
    tri2 = 1 - np.abs(2 * ((U4 * 2 + .5) % 1.0) - 1); st2 = np.floor(tri2 * 4) / 4
    rise = (V4 > .36) & (V4 < .36 + .44 * st); hang = (V4 < .80) & (V4 > .80 - .40 * st2) & ~rise
    edge = lambda m: m & ~(np.roll(m, 6, 0) & np.roll(m, -6, 0) & np.roll(m, 6, 1) & np.roll(m, -6, 1))   # outline of a shape
    put('mural_a', paint(None, [(rise, ochre * .9 + .04), (hang, slate), (edge(rise) | edge(hang) | lines | teeth, soot), (wave, ochre)]), 0, .93)
    # B: medallions of concentric rings between double bars, small steps at the bars, the same lower band
    r = np.hypot(((Uv % 1.0) - .5), V4 - .58)
    rings_o = np.abs(r - .21) < .028; rings_m = np.abs(r - .13) < .024; dot = r < .055
    bar = np.minimum(np.abs(Uv % 1.0), 1 - np.abs(Uv % 1.0)) * 1.0
    bars = ((bar > .015) & (bar < .045)) & (V4 > .34) & (V4 < .82)
    step = np.floor(np.clip(1 - bar / .16, 0, 1) * 3) / 3
    steps = ((V4 > .34) & (V4 < .34 + .14 * step)) | ((V4 < .82) & (V4 > .82 - .14 * step))
    put('mural_b', paint(None, [(rings_o, ochre), (rings_m, soot), (dot, slate), (bars | lines | teeth, soot), (steps & ~bars, slate), (wave, ochre)]), 0, .93)

    col = wash_ground(CELL, CELL)[0]
    put('plaster', col, 0, .94)

    # wood planks: grain along u, three boards per face, dark seams, worn light edges
    def wood(name, tone):
        ys, xs, h, w = region(name); U, V = coords(h, w)
        streak = vnoise(h, w, 24, 3, rng); fine = fbm(h, w, rng, 8, 3)
        grain = .5 + .5 * np.sin((V * 3 + .15 * streak) * 2 * np.pi * 7 + 3 * fine)
        val = .82 + .14 * grain + .12 * (streak - .5)
        seams = np.abs(((V * 3) % 1) - .5) > .47
        val[seams] *= .45
        knots = vnoise(h, w, 6, 6, rng) > .9
        val[knots] *= .7
        bd = np.minimum.reduce([U, V, 1 - U, 1 - V]); val *= .75 + .25 * sstep(0, .04, bd)
        put(name, np.array(tone)[None, None, :] * val[..., None], 0, .8 + .1 * fine)
    wood('wood_dark', (.48, .34, .21)); wood('wood_light', (.68, .54, .36))

    U, V = coords(CELL, CELL)
    streak = vnoise(CELL, CELL, 40, 5, rng) * .6 + vnoise(CELL, CELL, 90, 9, rng) * .4
    put('straw', np.array((.80, .66, .38))[None, None, :] * (.7 + .5 * streak)[..., None], 0, .95)
    weave = .5 + .25 * np.sin(U * 2 * np.pi * 40) * np.sin(V * 2 * np.pi * 40)
    put('canvas', np.array((.42, .43, .35))[None, None, :] * (.85 + .2 * weave + .15 * (fbm(CELL, CELL, rng, 4, 4) - .5))[..., None], 0, .92)

    def painted_metal(name, tone, chip=.15, met=.35, rgh=.6):
        U, V = coords(CELL, CELL); f = fbm(CELL, CELL, rng, 6, 4)
        bd = np.minimum.reduce([U, V, 1 - U, 1 - V])
        chipped = (f + (1 - sstep(0, .12, bd)) * .5) > 1 - chip
        col = np.array(tone)[None, None, :] * (.9 + .2 * f)[..., None]
        col[chipped] = np.array((.40, .40, .40)) * (.8 + .3 * f[chipped])[..., None]
        put(name, col, np.where(chipped, .8, met), np.where(chipped, .45, rgh))
    painted_metal('metal_paint', (.40, .46, .41))
    painted_metal('metal_dark', (.22, .28, .27), chip=.08, met=.45, rgh=.55)
    painted_metal('amber', (.86, .48, .12), chip=.06, met=.2, rgh=.55)
    painted_metal('teal_paint', (.18, .55, .50), chip=.06, met=.2, rgh=.55)
    f = fbm(CELL, CELL, rng, 6, 3)
    put('plastic', np.array((.12, .13, .14))[None, None, :] * (.9 + .2 * f)[..., None], 0, .62)
    put('metal_bare', np.array((.58, .59, .60))[None, None, :] * (.85 + .25 * vnoise(CELL, CELL, 40, 2, rng))[..., None], .9, .42)

    U, V = coords(CELL, CELL)
    col = np.full((CELL, CELL, 3), (.86, .83, .72), np.float32)
    text = (np.abs(((V * 6) % 1) - .5) < .12) & (V > .2) & (V < .8) & (U > .12) & (U < .12 + .7 * vnoise(CELL, CELL, 6, 1, rng))
    col[text] = (.20, .18, .16); col[(U < .06) | (U > .94) | (V < .06) | (V > .94)] *= .8
    put('tag', col, 0, .9)

    f = fbm(CELL, CELL, rng, 5, 5); peb = vnoise(CELL, CELL, 24, 24, rng) > .82
    col = np.array((.27, .23, .18))[None, None, :] * (.8 + .4 * f)[..., None]; col[peb] = col[peb] * .7 + np.array((.42, .40, .36)) * .3
    put('soil', col, 0, .97)
    f = fbm(CELL, CELL, rng, 6, 5)
    put('moss', np.array((.32, .38, .18))[None, None, :] * (.65 + .6 * f)[..., None], 0, .97)
    U, V = coords(CELL, CELL); f = fbm(CELL, CELL, rng, 4, 4)
    joint = (np.abs(((U * 2) % 1) - .5) > .47) | (np.abs(((V * 2 + .5 * (np.floor(U * 2) % 2)) % 1) - .5) > .47)
    col = np.array((.50, .47, .41))[None, None, :] * (.85 + .3 * f)[..., None]; col[joint] *= .5
    put('paving', col, 0, .9)

    # lamp lens: warm centre fading to the reflector rim (emission = colour)
    U, V = coords(CELL, CELL); r = np.hypot(U - .5, V - .5) * 2
    col = np.array((1.0, .92, .75))[None, None, :] * (1 - .35 * sstep(.4, 1.0, r))[..., None] + np.array((0, -.15, -.3)) * sstep(.4, 1, r)[..., None]
    put('glow_lamp', col, 0, .3, glow=col)
    # screen: dark teal glass with bright readouts (bar graph, text rows, a frame)
    col = np.full((CELL, CELL, 3), (.02, .10, .10), np.float32); g = np.zeros((CELL, CELL, 3), np.float32)
    bright = np.array((.30, 1.0, .85))
    bars = (U > .12) & (U < .55) & (V > .15) & (V < .15 + .55 * (np.floor(U * 12) % 3 + 1) / 3) & ((U * 12) % 1 < .7)
    rows = (U > .62) & (U < .9) & (np.abs(((V * 7) % 1) - .5) < .14) & (V > .2) & (V < .85)
    frame = ((U > .05) & (U < .95) & (V > .05) & (V < .95)) & ~((U > .07) & (U < .93) & (V > .07) & (V < .93))
    for m, k in ((bars, 1.0), (rows, .7), (frame, .5)):
        col[m] = bright * k; g[m] = bright * k
    put('glow_screen', col, 0, .2, glow=g)
    U, V = coords(CELL, CELL)
    stripe = ((U + V) * 4) % 1 < .5
    col = np.where(stripe[..., None], np.array((.88, .62, .10)), np.array((.08, .08, .08)))
    put('hazard', col * (.9 + .15 * fbm(CELL, CELL, rng, 5, 3))[..., None], .1, .6)

    TEX.mkdir(parents=True, exist_ok=True)
    images = {}
    for label, data in (('BaseColor', base), ('Metallic', metal), ('Roughness', rough), ('Emission', emit)):
        rgba = np.ones((AH, AW, 4), np.float32)
        rgba[..., :3] = data if data.ndim == 3 else data[..., None]
        im = bpy.data.images.new('Stage_Atlas_' + label, width=AW, height=AH, alpha=False)
        if label in ('Metallic', 'Roughness'): im.colorspace_settings.name = 'Non-Color'
        im.pixels.foreach_set(rgba.ravel())
        path = TEX / ('Stage_Atlas_' + label + '.png')
        im.filepath_raw = str(path); im.file_format = 'PNG'; im.save()
        images[label] = im
    mat = bpy.data.materials.new('THStage_Atlas'); mat.use_nodes = True
    nt = mat.node_tree; bs = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    for label, socket in (('BaseColor', 'Base Color'), ('Metallic', 'Metallic'), ('Roughness', 'Roughness'), ('Emission', 'Emission Color')):
        tx = nt.nodes.new('ShaderNodeTexImage'); tx.image = images[label]; tx.interpolation = 'Linear'
        nt.links.new(tx.outputs['Color'], bs.inputs[socket])
    bs.inputs['Emission Strength'].default_value = 2.5
    for key in ('Specular IOR Level', 'Specular'):
        if key in bs.inputs: bs.inputs[key].default_value = .35; break
    return mat


# ---------------------------------------------------------------- mesh building
class Part:
    """Polygons with per-corner UVs, joined into one module mesh at the end."""

    def __init__(self, name):
        self.name = name; self.verts = []; self.faces = []; self.uvs = []

    def add(self, verts, faces, uvs):
        b = len(self.verts); self.verts.extend(verts)
        for f, uv in zip(faces, uvs):
            self.faces.append(tuple(i + b for i in f)); self.uvs.append(uv)


def _box_uv(co, size, n, flip):
    s = [min(1, max(0, (co[i] + size[i] / 2) / size[i])) if size[i] > 1e-6 else .5 for i in range(3)]
    ax = max(range(3), key=lambda i: abs(n[i]))
    u, v = ((s[1], s[2]) if ax == 0 else (s[0], s[2]) if ax == 1 else (s[0], s[1]))
    if flip[0]: u = 1 - u
    if flip[1] and ax != 2: v = 1 - v
    return u, v


def block(part, c, size, tile, ang=0.0, bevel=.02, seg=2, flip=(False, False), rng=None, jitter=0.0, erode_top=0.0,
          drop_bottom=False, top_tile=None, face_tiles=None, tilt=None, window=None):
    """Bevelled box. c = logical centre (x, d, h); size along local (x, d, h); ang turns local x from +x toward +d.
    face_tiles: {'top'|'bottom'|'side_x'|'side_d': tile}. window: (u0, u1, v0, v1) sub-rectangle of the tile.
    tilt: ('x'|'y', radians), a rotation in Blender local space applied before the yaw."""
    sx, sd, sh = size
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts: v.co = Vector((v.co.x * sx, v.co.y * sd, v.co.z * sh))
    b = min(bevel, min(size) * .3)
    if b > 1e-4:
        bmesh.ops.bevel(bm, geom=list(bm.verts) + list(bm.edges), offset=b, segments=seg, profile=.5, affect='EDGES', clamp_overlap=True)
    if rng is not None and (jitter or erode_top):
        for v in bm.verts:
            if erode_top and v.co.z > sh / 2 - b * 1.2:
                v.co.z -= rng.uniform(0, erode_top)
                v.co.x *= rng.uniform(.985, 1.0); v.co.y *= rng.uniform(.96, 1.0)
            if jitter:
                v.co += Vector((rng.uniform(-jitter, jitter), rng.uniform(-jitter, jitter) * .5, rng.uniform(-jitter, jitter) * .6))
    bm.normal_update()
    if drop_bottom:
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.normal.z < -.9], context='FACES')
    rot = Matrix.Rotation(-ang, 3, 'Z')
    if tilt: rot = rot @ Matrix.Rotation(tilt[1], 3, tilt[0].upper())
    origin = L(*c)
    idx = {v: i for i, v in enumerate(bm.verts)}
    # clamp to the venue volume (jitter must not push stones through the floor or past the venue edge)
    verts = [Vector((min(2.5, max(-2.5, p.x)), min(0.0, max(-11.0, p.y)), max(0.0, p.z))) for p in (origin + rot @ v.co for v in bm.verts)]
    faces, uvs = [], []
    for f in bm.faces:
        n = f.normal; t = tile
        ax = max(range(3), key=lambda i: abs(n[i]))
        if face_tiles:
            key = ('top' if n.z > 0 else 'bottom') if ax == 2 else ('side_x' if ax == 0 else 'side_d')
            t = face_tiles.get(key, tile)
        if top_tile and ax == 2 and n.z > .7: t = top_tile
        fuv = []
        for lp in f.loops:
            u, v = _box_uv(lp.vert.co, size, n, flip)
            if window: u = window[0] + (window[1] - window[0]) * u; v = window[2] + (window[3] - window[2]) * v
            fuv.append(tile_uv(t, u, v))
        faces.append(tuple(idx[lp.vert] for lp in f.loops)); uvs.append(fuv)
    part.add(verts, faces, uvs); bm.free()


def cylinder(part, p0, p1, r0, tile, r1=None, n=12, caps=True, cap_tile=None):
    """Cylinder / cone between logical points p0 and p1. Side u is folded (no seam), v along the axis."""
    r1 = r0 if r1 is None else r1
    a, b = L(*p0), L(*p1); axis = b - a; length = axis.length
    q = Vector((0, 0, 1)).rotation_difference(axis.normalized()).to_matrix()
    ring = [(math.cos(2 * math.pi * k / n), math.sin(2 * math.pi * k / n)) for k in range(n)]
    verts = [a + q @ Vector((r0 * cx, r0 * sy, 0)) for cx, sy in ring] + [a + q @ Vector((r1 * cx, r1 * sy, length)) for cx, sy in ring]
    for v in verts: v.z = max(0.0, v.z)   # tripod feet stand on the floor, not through it
    faces, uvs = [], []
    for k in range(n):
        k2 = (k + 1) % n
        u0 = abs(((k / n) * 2) % 2 - 1); u1 = abs((((k + 1) / n) * 2) % 2 - 1)
        faces.append((k, k2, n + k2, n + k)); uvs.append([tile_uv(tile, u0, 0), tile_uv(tile, u1, 0), tile_uv(tile, u1, 1), tile_uv(tile, u0, 1)])
    if caps:
        ct = cap_tile or tile
        faces.append(tuple(reversed(range(n)))); uvs.append([tile_uv(ct, .5 + .5 * ring[k][0], .5 + .5 * ring[k][1]) for k in reversed(range(n))])
        faces.append(tuple(n + k for k in range(n))); uvs.append([tile_uv(ct, .5 + .5 * ring[k][0], .5 + .5 * ring[k][1]) for k in range(n)])
    part.add(verts, faces, uvs)


def flat_poly(part, pts, tile, h):
    """Flat upward-facing polygon at height h from logical (x, d) points (any winding)."""
    n = len(pts)
    area = sum(pts[i][0] * pts[(i + 1) % n][1] - pts[(i + 1) % n][0] * pts[i][1] for i in range(n))
    order = list(range(n)) if area < 0 else list(reversed(range(n)))  # clockwise in (x, d) faces up in Blender
    xs = [p[0] for p in pts]; ds = [p[1] for p in pts]
    lo_x, lo_d, sx, sd = min(xs), min(ds), max(1e-6, max(xs) - min(xs)), max(1e-6, max(ds) - min(ds))
    part.add([L(pts[i][0], pts[i][1], h) for i in order], [tuple(range(n))],
             [[tile_uv(tile, (pts[i][0] - lo_x) / sx, (pts[i][1] - lo_d) / sd) for i in order]])


def mound(part, cx, cd, rx, rd, h, tile, rng, n=10):
    """Low irregular heap of debris, a squashed cone with a jittered rim (faces point up)."""
    verts = [L(cx, cd, h)]; uvs_v = [(.5, .5)]
    for rr, z in ((1.0, 0.0), (.55, h * .62)):
        for k in range(n):
            t = 2 * math.pi * k / n; j = rng.uniform(.8, 1.1)
            verts.append(L(cx + math.cos(t) * rx * rr * j, cd + math.sin(t) * rd * rr * j, z * rng.uniform(.85, 1.1)))
            uvs_v.append((.5 + .5 * math.cos(t) * rr, .5 + .5 * math.sin(t) * rr))
    faces, uvs = [], []
    for k in range(n):
        k2 = (k + 1) % n
        o0, o1, i0, i1 = 1 + k, 1 + k2, 1 + n + k, 1 + n + k2
        for f in ((o0, i0, i1, o1), (i0, 0, i1)):
            faces.append(f); uvs.append([tile_uv(tile, *uvs_v[i]) for i in f])
    part.add(verts, faces, uvs)


def wall_patch(part, w, u0, u1, z0, z1, side, tile, rng, thick=.01, back=.006, n=14):
    """A thin, jagged-edged layer (surviving plaster) on one face of a Masonry wall: a front face and its edges."""
    cu, cz, hu, hz = (u0 + u1) / 2, (z0 + z1) / 2, (u1 - u0) / 2, (z1 - z0) / 2
    outline = []
    for k in range(n):
        t = 2 * math.pi * k / n; c, s = math.cos(t), math.sin(t)
        j = rng.uniform(.82, 1.0)
        j = rng.uniform(.72, 1.0); outline.append((cu + hu * j * math.copysign(abs(c) ** .6, c), cz + hz * j * math.copysign(abs(s) ** .6, s)))
    def P(u, z, o):
        x, d = w.world(u, side * (w.t / 2 + o)); return L(x, d, z)
    front = [P(u, z, back + thick) for u, z in outline]; rear = [P(u, z, back) for u, z in outline]
    uv = [tile_uv(tile, (u - u0) / (u1 - u0), (z - z0) / (z1 - z0)) for u, z in outline]
    nrm = sum(((front[i] - front[0]).cross(front[i + 1] - front[0]) for i in range(1, n - 1)), Vector())
    out = Vector((w.n.x * side, -w.n.y * side, 0))   # outward direction in Blender coords
    order = list(range(n)) if nrm.dot(out) > 0 else list(reversed(range(n)))
    verts = front + rear; faces = [tuple(order)]; uvs = [[uv[i] for i in order]]
    for a, b in zip(order, order[1:] + order[:1]):   # edges face outward: front a -> rear a -> rear b -> front b
        faces.append((a, n + a, n + b, b)); uvs.append([uv[a], uv[a], uv[b], uv[b]])
    part.add(verts, faces, uvs)


class Masonry:
    """A straight ruin wall of irregular ashlar courses between logical points p0 and p1.

    Openings (along-wall distance from p0): ('door', a0, a1, head) or ('niche', a0, a1, sill, head, side, depth).
    side = +1/-1: the niche opens toward the wall normal n = (-dir_d, dir_x) or away from it.
    Courses that an opening cuts are closed exactly: a lintel above, and for niches a sill lip, a fill below the
    sill and a back plate. Unsupported stones (after the broken top, holes and openings) are removed bottom-up,
    so nothing floats."""

    def __init__(self, p0, p1, t, seed, H=WALL_H, broken=0.0, holes=0.0, ext0=0.0, ext1=0.0, ragged0=False, ragged1=False,
                 min_top=1.8, openings=(), paint=None):
        self.p0, self.p1, self.t, self.H = Vector(p0), Vector(p1), t, H
        self.paint = paint or {}   # {side: mural tile}: a frieze painted on the course around 1.55 m
        d = self.p1 - self.p0; self.len0 = d.length; self.dir = d / self.len0
        self.n = Vector((-self.dir.y, self.dir.x)); self.ang = math.atan2(self.dir.y, self.dir.x)
        self.ext0, self.ext1 = ext0, ext1; self.length = self.len0 + ext0 + ext1
        self.rng = random.Random(seed); self.broken = broken; self.holes = holes
        self.ragged0, self.ragged1, self.min_top = ragged0, ragged1, min_top
        self.openings = [(o[0], o[1] + ext0, o[2] + ext0) + tuple(o[3:]) for o in openings]
        r = self.rng
        self.collapses = [(r.uniform(.15, .85) * self.length, r.uniform(.5, 1.4), r.uniform(.35, 1.0) * broken)
                          for _ in range(1 + int(self.length / 3))] if broken else []
        self.solids = []; self.layout()

    def top(self, u):
        top = self.H
        for c, w, depth in self.collapses: top -= depth * max(0.0, 1 - ((u - c) / w) ** 2)
        top = max(top, self.min_top)
        if self.ragged0: top = min(top, .7 + 1.7 * u)
        if self.ragged1: top = min(top, .7 + 1.7 * (self.length - u))
        return top

    def world(self, u, off=0.0):
        p = self.p0 + self.dir * (u - self.ext0) + self.n * off
        return p.x, p.y

    def layout(self):
        r = self.rng; z = 0.0; courses = []
        while z < self.H - .05:
            ch = .24 if not courses else r.choice((.26, .29, .32, .35, .38))
            ch = min(ch, self.H - z)
            if self.H - (z + ch) < .12: ch = self.H - z
            courses.append((z, ch)); z += ch
        self.courses = courses
        cuts = {}
        for row, (z0, ch) in enumerate(courses):
            u = -r.uniform(.05, .45) if row else 0.0
            while u < self.length - 1e-6:
                span = r.uniform(.34, .82)
                a, b = max(0.0, u), min(self.length, u + span); u += span
                if self.length - b < .12: b = self.length; u = self.length
                if b - a < .08: continue
                mid = (a + b) / 2
                if row >= 1 and z0 + ch > self.top(mid) + .03 * r.random(): continue
                pieces = [(a, b)]
                for o in self.openings:
                    kind, g0, g1 = o[0], o[1], o[2]
                    sill = -1.0 if kind == 'door' else o[3]; head = o[3] if kind == 'door' else o[4]
                    if z0 < head + .12 and z0 + ch > sill:
                        cuts.setdefault(o, []).append((z0, z0 + ch))
                        nxt = []
                        for s0, s1 in pieces:
                            if s1 <= g0 or s0 >= g1: nxt.append((s0, s1)); continue
                            if s0 < g0: nxt.append((s0, g0))
                            if s1 > g1: nxt.append((g1, s1))
                        pieces = nxt
                near_open = any(o[1] - .5 < mid < o[2] + .5 for o in self.openings)
                if 2 <= row < len(courses) - 2 and not near_open and r.random() < self.holes and len(pieces) == 1: continue
                for s0, s1 in pieces:
                    if s1 - s0 >= .05: self.solids.append(dict(a=s0, b=s1, z0=z0, z1=z0 + ch, kind='stone', row=row))
        self.extras = []
        for o, spans in cuts.items():
            kind, g0, g1 = o[0], o[1], o[2]
            zr0 = min(s[0] for s in spans); zr1 = max(s[1] for s in spans)
            if kind == 'door':
                self.solids.append(dict(a=g0, b=g1, z0=o[3], z1=zr1, kind='lintel', g=(g0, g1)))
            else:
                sill, head, side, depth = o[3], o[4], o[5], o[6]
                self.solids.append(dict(a=g0, b=g1, z0=head, z1=zr1, kind='lintel', g=(g0, g1)))
                self.solids.append(dict(a=g0, b=g1, z0=zr0, z1=sill, kind='fill', g=(g0, g1)))
                self.extras.append(dict(g0=g0, g1=g1, sill=sill, head=head, side=side, depth=depth))
        self.support()

    def support(self):
        """Keep a solid only if it stands on the floor or on solids below (>= 40 % of its span, covering its middle,
        or bearing on both halves). Lintels need jamb stones on both sides at their bottom height."""
        kept = []
        for s in sorted(self.solids, key=lambda s: (s['z0'], s['a'])):
            if s['z0'] < 1e-3: kept.append(s); continue
            if s['kind'] == 'lintel':
                g0, g1 = s['g']; z = s['z0'] + .01
                left = any(k['z0'] <= z <= k['z1'] and abs(k['b'] - g0) < .02 and k['kind'] == 'stone' for k in kept)
                right = any(k['z0'] <= z <= k['z1'] and abs(k['a'] - g1) < .02 and k['kind'] == 'stone' for k in kept)
                if left and right: kept.append(s)
                continue
            under = [(max(s['a'], k['a']), min(s['b'], k['b'])) for k in kept if abs(k['z1'] - s['z0']) < 1e-3 and min(s['b'], k['b']) - max(s['a'], k['a']) > .02]
            cover = sum(b - a for a, b in under); mid = (s['a'] + s['b']) / 2
            covers_mid = any(a <= mid <= b for a, b in under)
            both = any(a < mid for a, b in under) and any(b > mid for a, b in under)
            if cover >= .4 * (s['b'] - s['a']) and (covers_mid or both): kept.append(s)
        self.solids = kept
        for s in kept:  # exposed top: nothing rests on it
            s['exposed'] = not any(abs(k['z0'] - s['z1']) < 1e-3 and min(s['b'], k['b']) - max(s['a'], k['a']) > .05 for k in kept)

    def u_at(self, x, d):
        return (Vector((x, d)) - self.p0).dot(self.dir) + self.ext0

    def top_at(self, u):
        return max([s['z1'] for s in self.solids if s['a'] <= u <= s['b']] or [0.0])

    def build(self, part, base_tiles=('stone_a', 'stone_a', 'stone_c', 'stone_b')):
        r = self.rng; t = self.t
        cands = [(abs(z0 + ch / 2 - 1.55), i) for i, (z0, ch) in enumerate(self.courses) if ch >= .26 and abs(z0 + ch / 2 - 1.55) < .3]
        prow = min(cands)[1] if self.paint and cands else None
        half = min(.105, (self.courses[prow][1] - .06) / 2) if prow is not None else 0
        for s in self.solids:
            a, b, z0, z1 = s['a'], s['b'], s['z0'], s['z1']
            mid = (a + b) / 2
            painted = prow is not None and s['kind'] == 'stone' and s['row'] == prow and not s['exposed']
            if s['kind'] == 'stone' and painted:   # the painted course is dressed flat so the paint lies on the face
                x, d = self.world(mid)
                block(part, (x, d, (z0 + z1) / 2), (b - a, t, z1 - z0), r.choice(base_tiles), self.ang, bevel=.016, seg=2,
                      flip=(r.random() < .5, False))
                self.paint_stone(part, a + .02, b - .02, (z0 + z1) / 2, r, half=half)
            elif s['kind'] == 'stone':
                jit = r.uniform(-.006, .006); th = t - r.uniform(0, .02)
                if z0 < .01: tile = 'stone_damp'
                elif z0 < .5: tile = r.choice(('stone_damp', 'stone_c', 'stone_a'))
                else: tile = r.choice(base_tiles)
                top_tile = 'moss' if s['exposed'] and r.random() < .45 else None
                x, d = self.world(mid, jit)
                block(part, (x, d, (z0 + z1) / 2), (b - a, th, z1 - z0), tile, self.ang, bevel=r.uniform(.012, .024), seg=2,
                      flip=(r.random() < .5, r.random() < .3), rng=r, jitter=.005, erode_top=(.05 if s['exposed'] else .008),
                      drop_bottom=z0 < .01, top_tile=top_tile)
            elif s['kind'] == 'lintel':
                x, d = self.world(mid)
                block(part, (x, d, (z0 + z1) / 2), (b - a + .02, t + .02, z1 - z0), 'stone_dark', self.ang, bevel=.025, rng=r, jitter=.004,
                      erode_top=.03 if s['exposed'] else 0)
            else:  # fill below a niche sill
                x, d = self.world(mid)
                block(part, (x, d, (z0 + z1) / 2), (b - a + .01, t, z1 - z0), 'stone_c', self.ang, bevel=.015)
        for e in self.extras:  # niche: back plate (past the far face if the niche is as deep as the wall) and sill lip
            g0, g1, sill, head, side, depth = e['g0'], e['g1'], e['sill'], e['head'], e['side'], e['depth']
            mid = (g0 + g1) / 2; open_face = side * t / 2
            if depth >= t - .005:   # as deep as the wall: a thin plate past the far face (outer walls: at the venue edge)
                plate_t = .01; back = open_face - side * (depth + plate_t / 2)
                x, d = self.world(mid, back)
                block(part, (x, d, (sill + head) / 2), (g1 - g0 + .05, plate_t, head - sill + .05), 'stone_dark', self.ang, bevel=.003)
            else:   # shallower: the back is closed by half-depth stones that follow the courses, so the far face reads as wall
                bt = t - depth; back = open_face - side * (depth + bt / 2)
                x, d = self.world(mid, back)
                for z0, ch in self.courses:
                    lo, hi = max(z0, sill - .015), min(z0 + ch, head + .015)   # overlaps the fill, lintel and jambs: no gap to see through
                    if hi - lo < .02: continue
                    block(part, (x, d, (lo + hi) / 2), (g1 - g0 + .04, bt, hi - lo), r.choice(base_tiles), self.ang, bevel=.004,
                          flip=(r.random() < .5, False))
            lip_len = depth + LIP
            x, d = self.world(mid, open_face + side * (LIP - lip_len / 2))
            block(part, (x, d, sill - .016), (g1 - g0 + .02, lip_len, .04), 'stone_c', self.ang, bevel=.008)

    def paint_stone(self, part, a, b, zc, r, P=.42, half=.105):
        """The frieze on one dressed stone, each visible side: quads 1.2 mm off the face, split at the pattern period
        so the band runs on across the joints; a few pieces are missing where the paint fell away."""
        for side, tile in self.paint.items():
            k0 = math.floor(a / P); cuts = sorted({a, b, *[k * P for k in range(k0 + 1, int(b / P) + 1) if a < k * P < b]})
            for s0, s1 in zip(cuts, cuts[1:]):
                if s1 - s0 < .01 or r.random() < .12: continue
                u0 = (s0 / P) % 1.0; u1 = u0 + (s1 - s0) / P
                if u1 > 1.0001: u1 = 1.0
                off = side * (self.t / 2 + .0012)
                pts = [(s0, zc - half), (s1, zc - half), (s1, zc + half), (s0, zc + half)]
                uvs = [(u0, 0), (u1, 0), (u1, 1), (u0, 1)]
                verts = []
                for u, z in pts:
                    x, d = self.world(u, off); verts.append(L(x, d, z))
                nrm = (verts[1] - verts[0]).cross(verts[2] - verts[0]); out = Vector((self.n.x * side, -self.n.y * side, 0))
                order = [0, 1, 2, 3] if nrm.dot(out) > 0 else [3, 2, 1, 0]
                part.add(verts, [tuple(order)], [[tile_uv(tile, *uvs[i]) for i in order]])

    def colliders(self, name):
        """Solid boxes split around openings: (name, centre (x,d,h), size (along, across, up), ang)."""
        out = []; cuts = sorted([(o[1], o[2], o) for o in self.openings])
        u = 0.0; pieces = []
        for g0, g1, o in cuts: pieces.append((u, g0)); u = g1
        pieces.append((u, self.length))
        tops = lambda a, b: max([s['z1'] for s in self.solids if s['b'] > a and s['a'] < b] or [0])
        for a, b in pieces:
            if b - a > .02:
                x, d = self.world((a + b) / 2); h = tops(a, b)
                out.append((name, (x, d, h / 2), (b - a, self.t, h), self.ang))
        for g0, g1, o in cuts:
            mid = (g0 + g1) / 2; x, d = self.world(mid); h = max(tops(g0, g1), (o[3] if o[0] == 'door' else o[4]) + .12)
            if o[0] == 'door':
                out.append((name + '_Lintel', (x, d, (o[3] + h) / 2), (g1 - g0, self.t, h - o[3]), self.ang))
            else:
                sill, head, side, depth = o[3], o[4], o[5], o[6]
                out.append((name + '_NicheBelow', (x, d, sill / 2), (g1 - g0, self.t, sill), self.ang))
                out.append((name + '_NicheAbove', (x, d, (head + h) / 2), (g1 - g0, self.t, h - head), self.ang))
                bt = .01 if depth >= self.t - .005 else self.t - depth
                bx, bd = self.world(mid, side * self.t / 2 - side * (depth + bt / 2))
                out.append((name + '_NicheBack', (bx, bd, (sill + head) / 2), (g1 - g0, bt, head - sill), self.ang))
                lx, ld = self.world(mid, side * (self.t / 2 + LIP / 2))
                out.append((name + '_NicheLip', (lx, ld, sill - .016), (g1 - g0 + .02, LIP, .04), self.ang))
        return out

    def niche_slot(self, i):
        """Centre of the niche floor (logical), its facing (the open side) and usable size (width, depth, height)."""
        o = [o for o in self.openings if o[0] == 'niche'][i]
        g0, g1, sill, head, side, depth = o[1], o[2], o[3], o[4], o[5], o[6]
        open_face = side * self.t / 2
        x, d = self.world((g0 + g1) / 2, open_face + side * (LIP - (depth + LIP) / 2))
        f = self.n * side
        return (x, d, sill + .004), (f.x, f.y), (g1 - g0 - .02, depth + LIP - .01, head - sill - .02)


# ---------------------------------------------------------------- furniture vocabulary
def altar(P, x, d, w, dep, h, r, pillars=False):
    """Plinth, carved body, overhanging top slab."""
    block(P, (x, d, .05), (w + .08, dep + .08, .10), 'stone_dark', bevel=.02, rng=r, jitter=.004, drop_bottom=True)
    block(P, (x, d, .10 + (h - .18) / 2), (w - .06, dep - .06, h - .18), 'stone_a', bevel=.02, rng=r, jitter=.003, face_tiles={'side_d': 'relief'})
    block(P, (x, d, h - .04), (w + .06, dep + .06, .08), 'stone_b', bevel=.018, rng=r, jitter=.003)
    if pillars:
        for sx in (-1, 1): block(P, (x + sx * (w / 2 - .06), d, .10 + (h - .18) / 2), (.13, dep + .01, h - .18), 'stone_c', bevel=.02)


def crate_closed(P, x, d, z, s, ang):
    w, l, hh = s; ca, sa = math.cos(ang), math.sin(ang)
    loc = lambda lx, ld: (x + lx * ca - ld * sa, d + lx * sa + ld * ca)
    block(P, (x, d, z + hh / 2), (w, l, hh), 'wood_dark', ang=ang, bevel=.012, drop_bottom=z < .01)
    for sd in (-1, 1):   # battens on the long faces
        px, pd = loc(0, sd * (l / 2 + .006))
        block(P, (px, pd, z + hh * .25), (w - .06, .012, .05), 'wood_light', ang=ang, bevel=.003)
        block(P, (px, pd, z + hh * .75), (w - .06, .012, .05), 'wood_light', ang=ang, bevel=.003)
    px, pd = loc(w * .2, -(l / 2 + .013))
    block(P, (px, pd, z + hh * .5), (.13, .004, .08), 'tag', ang=ang, bevel=0)
    return z + hh


def crate_open(P, x, d, z, s, ang):
    """Open-topped crate, straw bedding at 60 % height; returns the straw surface height."""
    w, l, hh = s; t = .03; ca, sa = math.cos(ang), math.sin(ang)
    loc = lambda lx, ld: (x + lx * ca - ld * sa, d + lx * sa + ld * ca)
    block(P, (x, d, z + .015), (w, l, .03), 'wood_dark', ang=ang, bevel=.004, drop_bottom=True)
    for sx in (-1, 1):
        px, pd = loc(sx * (w / 2 - t / 2), 0); block(P, (px, pd, z + hh / 2), (t, l, hh), 'wood_dark', ang=ang, bevel=.006)
    for sd in (-1, 1):
        px, pd = loc(0, sd * (l / 2 - t / 2)); block(P, (px, pd, z + hh / 2), (w - 2 * t + .002, t, hh), 'wood_dark', ang=ang, bevel=.006)
        px, pd = loc(0, sd * (l / 2 + .004)); block(P, (px, pd, z + hh * .8), (w - .04, .01, .05), 'wood_light', ang=ang, bevel=.003)
    sh = z + hh * .6
    block(P, (x, d, (z + .03 + sh) / 2), (w - 2 * t, l - 2 * t, sh - z - .03), 'straw', ang=ang, bevel=.01)
    for sx, sd in ((-1, -1), (1, 1)):   # straw pushed up into two inner corners (outside any find's footprint)
        px, pd = loc(sx * (w / 2 - t - .02), sd * (l / 2 - t - .02))
        block(P, (px, pd, sh + .012), (.05, .05, .03), 'straw', ang=ang + .7, bevel=.01)
    px, pd = loc(-w * .25, -(l / 2 + .011)); block(P, (px, pd, z + hh * .45), (.12, .004, .07), 'tag', ang=ang, bevel=0)
    return sh + .004


def sarcophagus(P, x, d, l, w, r, ang=0.0, open_end=1, lean_side=1, h=.62):
    """Stone coffin, length l along local x (turned by ang), width w. The lid is broken: one piece still covers
    the far end, the other leans against a long side (lean_side = +-1 along local d; 0 = no leaning piece).
    The inside is lined with light, dusty stone so a find there is not lost in the dark.
    Returns the centre of the open part of the inner floor."""
    t = .08; floor_h = .24; ca, sa = math.cos(ang), math.sin(ang)
    loc = lambda lx, ld: (x + lx * ca - ld * sa, d + lx * sa + ld * ca)
    block(P, (x, d, .05), (l + .06, w + .06, .10), 'stone_dark', ang=ang, bevel=.02, rng=r, jitter=.003, drop_bottom=True)
    block(P, (x, d, (floor_h + .10) / 2), (l - .02, w - .02, floor_h - .10), 'stone_dust', ang=ang, bevel=.01)
    for sd in (-1, 1):   # long sides, carved
        px, pd = loc(0, sd * (w / 2 - t / 2))
        block(P, (px, pd, .10 + (h - .10) / 2), (l, t, h - .10), 'stone_a', ang=ang, bevel=.015, rng=r, jitter=.003, face_tiles={'side_d': 'relief'})
    for sx in (-1, 1):
        px, pd = loc(sx * (l / 2 - t / 2), 0)
        block(P, (px, pd, .10 + (h - .10) / 2), (t, w - 2 * t + .004, h - .10), 'stone_a', ang=ang, bevel=.015, rng=r, jitter=.003)
    for sd in (-1, 1):   # light inner lining
        px, pd = loc(0, sd * (w / 2 - t - .004)); block(P, (px, pd, (floor_h + h - .02) / 2), (l - 2 * t, .008, h - floor_h - .02), 'stone_dust', ang=ang, bevel=0)
    for sx in (-1, 1):
        px, pd = loc(sx * (l / 2 - t - .004), 0); block(P, (px, pd, (floor_h + h - .02) / 2), (.008, w - 2 * t, h - floor_h - .02), 'stone_dust', ang=ang, bevel=0)
    cover = l * .42
    px, pd = loc(-open_end * (l / 2 - cover / 2 + .01), 0)
    block(P, (px, pd, h + .045), (cover, w + .04, .09), 'stone_b', ang=ang, bevel=.02, rng=r, jitter=.004, face_tiles={'top': 'relief'})
    if lean_side:
        # broken half leaning on the long side: inner top edge touches the coffin wall, outer bottom edge on the floor
        phi = math.radians(32); lw = min(.42, h * .7); tk = .08; ll = l * .46
        off = w / 2 + (lw / 2) * math.sin(phi) + (tk / 2) * math.cos(phi)
        zc = (lw / 2) * math.cos(phi) + (tk / 2) * math.sin(phi)
        px, pd = loc(-open_end * l * .22, lean_side * off)
        block(P, (px, pd, zc), (ll, lw, tk), 'stone_b', ang=ang, bevel=.02, tilt=('x', lean_side * (math.pi / 2 - phi)))
    open_len = l - t - cover
    return loc(open_end * (l / 2 - t - open_len / 2), 0) + (floor_h,)


def stone_shelf(P, x, d, w, dep, r):
    """Two uprights, two slabs and a back slab against a wall (back toward +d). Returns the tier surface heights."""
    for sx in (-1, 1): block(P, (x + sx * (w / 2 - .05), d, .60), (.10, dep, 1.20), 'stone_a', bevel=.02, rng=r, jitter=.004, drop_bottom=True)
    tiers = []
    for z in (.50, 1.02):
        block(P, (x, d, z), (w, dep + .02, .06), 'stone_b', bevel=.012, rng=r, jitter=.003)
        tiers.append(z + .03)
    block(P, (x, d + dep / 2 - .02, .62), (w - .2, .03, 1.1), 'stone_dark', bevel=.005)
    block(P, (x, d, .05), (w - .2, dep - .04, .10), 'stone_dark', bevel=.01, drop_bottom=True)
    return tiers


def pallet(P, x, d, w, l, r, ang=0.0):
    """Wooden pallet with a folded padding blanket on top. Returns the blanket surface height."""
    ca, sa = math.cos(ang), math.sin(ang)
    loc = lambda lx, ld: (x + lx * ca - ld * sa, d + lx * sa + ld * ca)
    for sx in (-1, 0, 1):
        px, pd = loc(sx * (w / 2 - .05), 0); block(P, (px, pd, .045), (.08, l, .09), 'wood_dark', ang=ang, bevel=.006, drop_bottom=True)
    for k in range(5):
        px, pd = loc(0, -l / 2 + .05 + k * (l - .1) / 4); block(P, (px, pd, .105), (w, .085, .03), 'wood_light', ang=ang, bevel=.004)
    block(P, (x, d, .135), (w - .08, l - .1, .03), 'canvas', ang=ang, bevel=.01)
    return .151


def plinth(P, x, d, s, h, r):
    """Square stone pedestal with a stepped base and a cap. Returns the top height."""
    block(P, (x, d, .05), (s + .1, s + .1, .10), 'stone_dark', bevel=.02, rng=r, jitter=.004, drop_bottom=True)
    block(P, (x, d, .10 + (h - .18) / 2), (s - .06, s - .06, h - .18), 'stone_a', bevel=.02, rng=r, jitter=.003, face_tiles={'side_d': 'relief', 'side_x': 'relief'})
    block(P, (x, d, h - .04), (s + .04, s + .04, .08), 'stone_b', bevel=.015)
    return h


def offset_polygon(pts, dist):
    """Offset a convex polygon (logical x, d) inward by dist; returns the new vertices (vertex i starts edge i)."""
    n = len(pts); lines = []
    area = sum(pts[i][0] * pts[(i + 1) % n][1] - pts[(i + 1) % n][0] * pts[i][1] for i in range(n))
    s = 1 if area > 0 else -1
    for i in range(n):
        a, b = Vector(pts[i]), Vector(pts[(i + 1) % n]); d = (b - a).normalized()
        lines.append((a + Vector((-d.y, d.x)) * s * dist, d))
    out = []
    for i in range(n):
        (p1, d1), (p2, d2) = lines[i - 1], lines[i]
        den = d1.x * d2.y - d1.y * d2.x
        t = ((p2.x - p1.x) * d2.y - (p2.y - p1.y) * d2.x) / den
        out.append((p1.x + d1.x * t, p1.y + d1.y * t))
    return out


def pebbles(P, pts, r):
    """A few fallen chips of stone at a wall foot (rooms and hall only, never in the corridors)."""
    for x, d in pts:
        for k in range(r.randint(2, 4)):
            sz = (r.uniform(.05, .13), r.uniform(.04, .10), r.uniform(.03, .07))
            block(P, (x + r.uniform(-.07, .07), d + r.uniform(-.05, .05), sz[2] / 2), sz, r.choice(('stone_c', 'stone_a', 'stone_dark')),
                  ang=r.uniform(0, 3), bevel=.012, rng=r, jitter=.005, drop_bottom=True)


def stats_of(objs):
    meshes = [o for o in objs if o.type == 'MESH']
    for o in meshes: o.data.calc_loop_triangles()
    return dict(meshes=len(meshes), triangles=sum(len(o.data.loop_triangles) for o in meshes),
                vertices=sum(len(o.data.vertices) for o in meshes), material_slots=sum(len(o.data.materials) for o in meshes))


MURAL_WALLS = {   # wall key -> mural tile per side (+1 / -1 of the wall normal); rooms, hall and the block front only
    1: {'Block_Partition': 'mural_a', 'HallPartition_W': 'mural_b', 'HallPartition_E': 'mural_b', 'Block_West': 'mural_a', 'Block_East': 'mural_b'},
    -1: {'Block_Partition': 'mural_b', 'BackWall': 'mural_a', 'Block_Front': 'mural_b', 'Block_Back': 'mural_a'},
}
INSIDE_ONLY = {'OuterWall_W': -1, 'OuterWall_E': 1, 'BackWall': -1}   # the only visible side of the boundary walls


# ---------------------------------------------------------------- the stage
class Stage:
    def __init__(self):
        OUT.mkdir(exist_ok=True); (OUT / 'modules').mkdir(exist_ok=True)
        self.scene = bpy.context.scene
        for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
        for c in list(bpy.data.collections): bpy.data.collections.remove(c)
        self.scene.name = 'TH_Stage_Review_5x11'
        self.scene.unit_settings.system = 'METRIC'; self.scene.unit_settings.scale_length = 1
        self.runtime = bpy.data.collections.new('TH_STAGE_Runtime'); self.scene.collection.children.link(self.runtime)
        self.preview = bpy.data.collections.new('TH_STAGE_PreviewOnly'); self.scene.collection.children.link(self.preview)
        self.root = bpy.data.objects.new('VenueRoot', None); self.runtime.objects.link(self.root)
        self.root['venue_width_m'] = 5.0; self.root['venue_length_m'] = 11.0
        self.root['logical_axes'] = 'X across, D away from the lift, H up; Blender=(X,-D,H); Unity=(X,H,D)'
        self.mat = build_atlas()
        self.modules = []; self.colliders = []; self.markers = []; self.slots = []; self.lamps = []; self.walls = {}; self.bulbs = []
        self.part = None

    # ---- modules, colliders, markers
    def begin(self, name, beam_blocking=False, kind='furniture'):
        self.part = Part('THStage_' + name); self.part.beam_blocking = beam_blocking; self.part.kind = kind
        return self.part

    def finish(self, sharp_deg=40.0):
        p = self.part; bm = bmesh.new(); uvl = bm.loops.layers.uv.new('UVMap')
        bv = [bm.verts.new(v) for v in p.verts]
        for f, uv in zip(p.faces, p.uvs):
            try: face = bm.faces.new([bv[i] for i in f])
            except ValueError: continue
            face.smooth = True
            for lp, (u, v) in zip(face.loops, uv): lp[uvl].uv = (u, v)
        bm.normal_update()
        lim = math.radians(sharp_deg)
        for e in bm.edges: e.smooth = len(e.link_faces) == 2 and e.calc_face_angle(0.0) <= lim
        mesh = bpy.data.meshes.new(p.name); bm.to_mesh(mesh); bm.free()
        mesh.materials.append(self.mat)
        obj = bpy.data.objects.new(p.name, mesh); self.runtime.objects.link(obj)
        mod = obj.modifiers.new('wn', 'WEIGHTED_NORMAL'); mod.mode = 'FACE_AREA'; mod.weight = 50; mod.keep_sharp = True
        dg = bpy.context.evaluated_depsgraph_get()
        baked = bpy.data.meshes.new_from_object(obj.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)
        obj.modifiers.clear(); old = obj.data; obj.data = baked; baked.name = p.name; bpy.data.meshes.remove(old)
        if not baked.materials: baked.materials.append(self.mat)
        obj.parent = self.root
        obj['module_id'] = p.name; obj['beam_blocking'] = bool(p.beam_blocking); obj['module_kind'] = p.kind
        if p.kind == 'decor_ground': obj['do_not_add_collider'] = True; obj.visible_shadow = False
        self.modules.append(obj); self.part = None
        return obj

    def collider(self, name, c, size, ang=0.0, beam=False, kind='solid'):
        x, d, h = c; sx, sd, sh = size
        self.colliders.append(dict(name=name, module=self.part.name if self.part else None, kind=kind, beam_blocking=beam,
                                   height_m=round(h + sh / 2, 4), center_unity_xyz=[x, h, d], size_unity_xyz=[sx, sh, sd],
                                   rotation_unity_xyz=[0, -math.degrees(ang), 0],
                                   footprint_logical=dict(center_xd=[x, d], size_xd=[sx, sd], angle_rad=ang, h0=h - sh / 2, h1=h + sh / 2)))

    def marker(self, name, pos, role, **extra):
        obj = bpy.data.objects.new(name, None); self.runtime.objects.link(obj); obj.parent = self.root
        obj.location = L(*pos); obj.empty_display_size = .08
        obj['role'] = role; obj['anchor_id'] = name
        for k, v in extra.items():
            if isinstance(v, (int, float, str, bool)) or (isinstance(v, (list, tuple)) and v and all(isinstance(i, (int, float)) for i in v)) \
                    or (isinstance(v, (list, tuple)) and v and all(isinstance(i, str) for i in v)):
                obj[k] = v
        rec = dict(name=name, role=role, position_unity_xyz=[pos[0], pos[2], pos[1]], position_blender_xyz=list(obj.location), **extra)
        self.markers.append(rec)
        return obj

    def slot(self, pos, allowed, host, zone, hidden, facing, max_size, host_module=None, note=''):
        name = 'TreasureSlot_%02d' % (len(self.slots) + 1)
        fx, fd = facing; length = math.hypot(fx, fd); fx, fd = fx / length, fd / length
        rec = dict(allowed_classes=list(allowed), host_type=host, surface_height_m=round(pos[2], 4), zone=zone, hidden_intent=hidden,
                   facing_unity_xz=[round(fx, 4), round(fd, 4)], rotation_blender_z=round(math.atan2(fx, fd), 5),
                   max_item_size_m=[round(v, 3) for v in max_size], host_module=host_module or (self.part.name if self.part else ''))
        if note: rec['note'] = note
        self.marker(name, pos, 'treasure_slot', **rec)
        self.slots.append(dict(name=name, pos=pos, **rec))

    # ---- walls
    def wall(self, key, p0, p1, t, seed, **kw):
        paint = {side: m[key] for side, m in MURAL_WALLS.items() if key in m}
        w = Masonry(p0, p1, t, seed, paint=paint, **kw); self.walls[key] = w
        return w

    def build_walls(self):
        # Outer walls run +d, n = (-1, 0): side -1 opens toward +x (west wall), +1 toward -x (east wall).
        # Their niches reach the outer face; a 10 mm back plate closes them at the venue edge (x = +-2.50).
        Dn = .21
        ow = dict(t=OUTER_T, broken=.8, holes=.07, ragged0=True, ext1=.10)
        self.wall('OuterWall_W', (-OUTER_X, OUTER_D0), (-OUTER_X, BACK_D), seed=11, **ow,
                  openings=[('niche', d - .2 - OUTER_D0, d + .2 - OUTER_D0, NICHE_SILL + s, NICHE_SILL + s + NICHE_H, -1, Dn)
                            for d, s in ((4.5, 0), (6.4, .08), (10.05, .1))])
        self.wall('OuterWall_E', (OUTER_X, OUTER_D0), (OUTER_X, BACK_D), seed=12, **ow,
                  openings=[('niche', d - .2 - OUTER_D0, d + .2 - OUTER_D0, NICHE_SILL + s, NICHE_SILL + s + NICHE_H, 1, Dn)
                            for d, s in ((7.3, -.05), (9.95, .05))])
        self.wall('BackWall', (-OUTER_X + OUTER_T / 2, BACK_D), (OUTER_X - OUTER_T / 2, BACK_D), BACK_T, 13, broken=.5, holes=.06)
        # Central block: octagonal outer face (corners cut CHAMFER), walls along the offset centreline.
        X, D0, D1, C, t = BLOCK_X, BLOCK_D0, BLOCK_D1, CHAMFER, BLOCK_T
        outer = [(-X + C, D0), (X - C, D0), (X, D0 + C), (X, D1 - C), (X - C, D1), (-X + C, D1), (-X, D1 - C), (-X, D0 + C)]
        cl = offset_polygon(outer, t / 2)
        e = t / 2 * math.tan(math.radians(22.5)) + .005
        names = ['Block_Front', 'Block_ChamferFE', 'Block_East', 'Block_ChamferBE', 'Block_Back', 'Block_ChamferBW', 'Block_West', 'Block_ChamferFW']
        Db = .185
        # East wall runs +d (n -> -x, into the block): side -1 opens to the corridor. West wall runs -d (n -> +x).
        # Back wall runs -x (n -> -d, into the block): side -1 opens to the cross corridor.
        open_ = {
            'Block_Front': [('niche', -.40 - cl[0][0] - .2, -.40 - cl[0][0] + .2, NICHE_SILL + .05, NICHE_SILL + .05 + NICHE_H, 1, Db)],
            'Block_East': [('niche', 4.75 - .2 - cl[2][1], 4.75 + .2 - cl[2][1], NICHE_SILL, NICHE_SILL + NICHE_H, -1, Db),
                           ('niche', 5.55 - .2 - cl[2][1], 5.55 + .2 - cl[2][1], NICHE_SILL + .1, NICHE_SILL + .1 + NICHE_H, 1, Db),
                           ('door', BACK_DOOR[0] - cl[2][1], BACK_DOOR[1] - cl[2][1], 2.02)],
            'Block_West': [('door', cl[6][1] - FRONT_DOOR[1], cl[6][1] - FRONT_DOOR[0], 2.02),
                           ('niche', cl[6][1] - 7.15 - .2, cl[6][1] - 7.15 + .2, NICHE_SILL + .05, NICHE_SILL + .05 + NICHE_H, -1, Db)],
            'Block_Back': [('niche', cl[4][0] - x - .2, cl[4][0] - x + .2, NICHE_SILL, NICHE_SILL + NICHE_H, -1, Db) for x in (.45, -.40)],
        }
        for i, nm in enumerate(names):
            self.wall(nm, cl[i], cl[(i + 1) % 8], t, 30 + i, ext0=e, ext1=e, broken=(0 if 'Chamfer' in nm else .5), openings=open_.get(nm, ()))
        # Partition inside the block, runs +x (n -> +d): niches (side -1) open to the front room.
        px0 = -X + t - .01
        self.wall('Block_Partition', (px0, PART_D), (-px0, PART_D), PART_T, 40, broken=.15,
                  openings=[('niche', x - px0 - .2, x - px0 + .2, NICHE_SILL - .05, NICHE_SILL - .05 + NICHE_H, -1, .24) for x in (-.45, .45)])
        # Hall partitions, central opening 2 m; both run +x (n -> +d): niches (side -1) open to the cross corridor.
        x0 = -OUTER_X + OUTER_T / 2 - .01
        self.wall('HallPartition_W', (x0, HALL_D), (-HALL_OPEN, HALL_D), HALL_T, 41, broken=.3,
                  openings=[('niche', -1.45 - x0 - .2, -1.45 - x0 + .2, NICHE_SILL, NICHE_SILL + NICHE_H, -1, .185)])
        self.wall('HallPartition_E', (HALL_OPEN, HALL_D), (-x0, HALL_D), HALL_T, 42, broken=.3,
                  openings=[('niche', 1.45 - HALL_OPEN - .2, 1.45 - HALL_OPEN + .2, NICHE_SILL + .05, NICHE_SILL + .05 + NICHE_H, -1, .185)])

        groups = [('OuterWall_W', ['OuterWall_W']), ('OuterWall_E', ['OuterWall_E']), ('BackWall', ['BackWall']),
                  ('Block_Shell', names), ('Block_Partition', ['Block_Partition']), ('HallPartitions', ['HallPartition_W', 'HallPartition_E'])]
        for gname, keys in groups:
            self.begin(gname, beam_blocking=True, kind='wall')
            for k in keys:
                w = self.walls[k]; w.build(self.part)
                for (cn, c, size, ang) in w.colliders(k):
                    lip = cn.endswith('_NicheLip')
                    self.collider(cn, c, size, ang, beam=not lip, kind='niche_lip' if lip else 'wall')
            self.wall_decor(keys)
            self.finish()

    def visible_sides(self, key):
        return [INSIDE_ONLY[key]] if key in INSIDE_ONLY else [-1, 1]

    def wall_decor(self, keys):
        """Faded mural friezes on surviving plaster, and moss at the foot, on the visible faces of the given walls."""
        for k in keys:
            w = self.walls[k]; r = random.Random(sum(map(ord, k)) + 7)
            if w.length < .8: continue
            for side in self.visible_sides(k):
                gaps = [(o[1] - .1, o[2] + .1) for o in w.openings]
                free = lambda a, b: all(b < g0 or a > g1 for g0, g1 in gaps)
                # moss and damp tongues at the foot (thin, on the surface)
                for i in range(int(w.length / .65) + 1):
                    u0 = r.uniform(.1, w.length - .5); u1 = u0 + r.uniform(.25, .6)
                    if not free(u0, u1): continue
                    x, d = w.world((u0 + u1) / 2, side * (w.t / 2 + .006))
                    hgt = r.uniform(.12, .32)
                    block(self.part, (x, d, .02 + hgt / 2), (u1 - u0, .008, hgt), 'moss', w.ang, bevel=.003, rng=r, jitter=.003)

    # ---- entrance zone: extraction lift (the previous design, re-textured), practice row, camp gear
    def lift(self):
        self.begin('ExtractionBay')
        P = self.part
        block(P, (0, 1.20, .012), (2.30, 1.56, .024), 'metal_dark', bevel=.004, drop_bottom=True)
        for x in (-1.12, 1.12): block(P, (x, 1.20, .027), (.025, 1.50, .006), 'glow_screen', bevel=0)
        block(P, (0, 2.03, .003), (2.30, .10, .006), 'hazard', bevel=0, drop_bottom=True)
        block(P, (0, .30, .36), (3, .24, .72), 'metal_dark', bevel=.025, drop_bottom=True)
        for sign in (-1, 1):
            block(P, (sign * 1.34, 1.20, .20), (.30, 1.52, .40), 'metal_dark', bevel=.02, drop_bottom=True)
            block(P, (sign * 1.34, 1.20, .42), (.30, 1.52, .04), 'metal_paint', bevel=.008)
            for i in range(8): block(P, (sign * 1.34, .55 + i * .17, .446), (.22, .07, .008), 'hazard', ang=.18, bevel=0)
            self.collider('ReceiverSide_%+d' % sign, (sign * 1.34, 1.20, .225), (.30, 1.52, .45))
        block(P, (0, .448, .56), (1.18, .08, .31), 'metal_paint', bevel=.015)
        block(P, (0, .494, .57), (1.02, .014, .19), 'plastic', bevel=.004)
        for i in range(5): block(P, (-.36 + i * .18, .505, .57), (.085, .008, .025), 'glow_screen', bevel=.001)
        block(P, (0, .46, .24), (.54, .08, .18), 'metal_paint', bevel=.01)
        block(P, (0, .507, .24), (.40, .012, .055), 'plastic', bevel=.001)
        for x in (-1.1, 1.1):
            for i in range(5): block(P, (x, .428, .27 + i * .06), (.38, .014, .022), 'plastic', bevel=.002)
        block(P, (-1.34, 1.82, .50), (.30, .28, .52), 'metal_paint', bevel=.018)
        block(P, (-1.34, 1.97, .59), (.24, .02, .25), 'plastic', bevel=.003)
        cylinder(P, (-1.34, 1.98, .60), (-1.34, 1.998, .60), .065, 'amber', n=12)
        self.collider('ReceiverBack', (0, .35, .36), (3, .34, .72))
        self.collider('ControlPod', (-1.34, 1.85, .39), (.32, .32, .78))
        self.finish()
        self.marker('CollectionVolume', (0, 1.20, .49), 'collection_volume', size=[2.30, .92, 1.56])
        self.marker('ScoreBoardAnchor', (0, .32, 1.5), 'nonphysical_ui_anchor')
        for i, x in enumerate((-1.2, -.4, .4, 1.2)): self.marker('Tutorial_%02d' % (i + 1), (x, 2.5, 0), 'tutorial')

    def tripod_lamp(self, P, x, d, h, aim, legs=.26, energy=320, name=None):
        """Expedition work lamp on a tripod; aim = logical point. Adds a preview spot light and a light marker."""
        for k in range(3):
            a = k * 2 * math.pi / 3 + .3
            cylinder(P, (x + legs * math.cos(a), d + legs * math.sin(a), 0), (x, d, h - .02), .011, 'metal_bare', n=6, caps=False)
        cylinder(P, (x, d, h - .25), (x, d, h), .016, 'metal_bare', n=6)
        self.lamp_head(P, (x, d, h + .07), aim, energy, name)

    def lamp_head(self, P, pos, aim, energy=320, name=None, size=(.20, .14, .14)):
        x, d, h = pos; ax, ad, ah = aim
        yaw = math.atan2(ad - d, ax - x)   # the lens is on the head's +local-x face
        block(P, (x, d, h), size, 'metal_paint', ang=yaw, bevel=.012)
        fx, fd = math.cos(yaw), math.sin(yaw)
        block(P, (x + fx * (size[0] / 2 + .004), d + fd * (size[0] / 2 + .004), h), (.008, size[1] * .85, size[2] * .8), 'glow_lamp', ang=yaw, bevel=0)
        self.lamps.append(dict(name=name or 'WorkLamp_%02d' % (len(self.lamps) + 1), pos=(x + fx * .12, d + fd * .12, h), aim=aim, energy=energy))

    def clamp_lamp(self, P, wall_key, u, side, aim, energy=260):
        """A small lamp clamped on top of a wall (on the stone there), hanging over one side, pointing down."""
        w = self.walls[wall_key]; top = w.top_at(u)
        x, d = w.world(u, 0)
        block(P, (x, d, top + .005), (.10, w.t + .04, .05), 'metal_dark', ang=w.ang, bevel=.006)
        nx, nd = w.n.x * side, w.n.y * side
        block(P, (x + nx * (w.t / 2 + .02), d + nd * (w.t / 2 + .02), top - .03), (.03, .03, .12), 'metal_dark', ang=w.ang, bevel=.004)
        self.lamp_head(P, (x + nx * (w.t / 2 + .07), d + nd * (w.t / 2 + .07), top - .08), aim, energy, size=(.14, .11, .10))

    def camp(self):
        """Expedition gear in the entrance zone (low, out of the practice row and the corridor mouths)."""
        self.begin('EntryCamp'); P = self.part; r = random.Random(5)
        block(P, (2.0, 1.0, .25), (.40, .62, .48), 'metal_paint', bevel=.02, drop_bottom=True)    # generator
        block(P, (2.0, .685, .33), (.24, .01, .12), 'glow_screen', bevel=0)
        cylinder(P, (2.0, 1.0, .49), (2.0, 1.0, .56), .06, 'plastic', n=10)
        block(P, (2.0, 1.0, .03), (.46, .70, .06), 'plastic', bevel=.008, drop_bottom=True)
        self.collider('Generator', (2.0, 1.0, .28), (.46, .70, .56))
        for (x, d, z, s, ang) in ((-2.0, .75, .0, (.62, .48, .44), .05), (-2.0, .76, .44, (.50, .40, .36), -.12), (-2.05, 1.45, 0, (.56, .44, .40), .2)):
            crate_closed(P, x, d, z, s, ang)
        self.collider('CampCrates', (-2.02, 1.1, .4), (.66, 1.24, .8))
        self.tripod_lamp(P, -2.2, 2.72, 1.45, (-.1, 3.65, .75), legs=.2, name='Lamp_EntryW')
        self.tripod_lamp(P, 2.2, 2.72, 1.45, (1.7, 5.5, .6), legs=.2, name='Lamp_EntryE')
        self.collider('Lamp_EntryW', (-2.2, 2.72, .8), (.42, .42, 1.6)); self.collider('Lamp_EntryE', (2.2, 2.72, .8), (.42, .42, 1.6))
        for sx in (-1, 1):   # fallen blocks at the broken wall ends
            for (dx, d, s, a) in ((.0, 3.09, (.20, .16, .12), .4), (.08, 2.99, (.12, .10, .08), 1.2)):
                block(P, (sx * (2.36 - dx), d, s[2] / 2), s, 'stone_c', ang=a, bevel=.02, rng=r, jitter=.006, erode_top=.02, drop_bottom=True)
        pts = [(2.0, 1.3, .012), (2.3, 1.6, .012), (2.4, 2.4, .012), (2.4, 3.15, .012)]   # cable along the edge
        for a, b in zip(pts, pts[1:]): cylinder(P, a, b, .012, 'plastic', n=6, caps=False)
        self.finish()

    # ---- the forecourt face of the block: a low altar with two visible offerings
    def block_front(self):
        self.begin('BlockFront_Altar'); P = self.part; r = random.Random(21)
        x0, x1, d0, d1 = -.62, .62, 3.43, 3.86
        altar(P, (x0 + x1) / 2, (d0 + d1) / 2, x1 - x0, d1 - d0, .72, r)
        self.collider('BlockFrontAltar', (0, (d0 + d1) / 2, .37), (x1 - x0 + .08, d1 - d0 + .08, .74))
        for x, allowed in ((-.30, ('S', 'M')), (.30, ('S',))):
            self.slot((x, (d0 + d1) / 2, .721), allowed, 'altar', 'block_front', False, (0, -1), (.48, .40, .9))
        for (x, d, s, a) in ((-.80, 3.70, (.30, .22, .20), .5), (.82, 3.62, (.26, .20, .16), -.3)):
            block(P, (x, d, s[2] / 2), s, 'stone_c', ang=a, bevel=.03, rng=r, jitter=.01, erode_top=.03, drop_bottom=True)
        self.finish()

    # ---- front room: finds table, sarcophagus, two niches in the partition (the door is in the west wall)
    def front_room(self):
        self.begin('FrontRoom_Furniture'); P = self.part; r = random.Random(31)
        inner = sarcophagus(P, .50, 5.29, 1.14, .64, r, ang=math.pi / 2, open_end=1, lean_side=0)
        self.collider('FR_Sarcophagus', (.50, 5.29, .33), (.70, 1.20, .66))
        for (x, d, s_, a) in ((.10, 4.62, (.36, .30, .08), .2), (-.05, 4.35, (.22, .18, .07), 1.0)):   # broken lid pieces at the foot
            block(P, (x, d, s_[2] / 2), s_, 'stone_b', ang=a, bevel=.015, rng=r, jitter=.006, drop_bottom=True)
        self.slot(inner, ('S',), 'sarcophagus', 'front_room', True, (-1, 0), (.50, .44, .5), note='inside, the open end')
        self.clamp_lamp(P, 'Block_Partition', 1.25, -1, (.50, 5.45, .25), energy=300)
        mound(P, -.66, 5.74, .17, .12, .09, 'soil', r)
        for (x, d, s, a) in ((-.76, 5.58, (.20, .15, .12), .3), (-.15, 5.80, (.18, .14, .10), 1.1)):
            block(P, (x, d, s[2] / 2), s, 'stone_a', ang=a, bevel=.025, rng=r, jitter=.008, drop_bottom=True)
        pebbles(P, [(-.80, 4.20), (-.70, 4.16), (.10, 4.17), (-.82, 4.95)], r)
        self.finish()
        for i in range(2):
            pos, f, size = self.walls['Block_Partition'].niche_slot(i)
            self.slot(pos, ('S',), 'niche', 'front_room', True, f, size, host_module='THStage_Block_Partition')
        pos, f, size = self.walls['Block_Front'].niche_slot(0)
        self.slot(pos, ('S',), 'niche', 'front_room', True, f, size, host_module='THStage_Block_Shell')
        pos, f, size = self.walls['Block_East'].niche_slot(1)
        self.slot(pos, ('S',), 'niche', 'front_room', True, f, size, host_module='THStage_Block_Shell', note='above the sarcophagus')

    # ---- back room: altar along the partition, open crate, stone shelf, crate with a tray (door in the east wall)
    def back_room(self):
        self.begin('BackRoom_Furniture'); P = self.part; r = random.Random(41)
        ax0, ax1, ad0, ad1 = -.84, .26, 6.22, 6.74
        altar(P, (ax0 + ax1) / 2, (ad0 + ad1) / 2, ax1 - ax0, ad1 - ad0, .80, r)
        self.collider('BR_Altar', ((ax0 + ax1) / 2, (ad0 + ad1) / 2, .41), (ax1 - ax0 + .08, ad1 - ad0 + .08, .82))
        self.slot((-.56, 6.48, .801), ('S', 'M'), 'altar', 'back_room', True, (0, 1), (.48, .40, .9))
        self.slot((-.02, 6.48, .801), ('M',), 'altar', 'back_room', True, (0, 1), (.48, .40, .9))
        top = crate_open(P, .60, 6.42, 0, (.48, .40, .46), 0)
        self.collider('BR_Crate', (.60, 6.42, .23), (.48, .40, .46))
        self.slot((.60, 6.42, top), ('S', 'M'), 'crate', 'back_room', True, (-1, .3), (.42, .34, .9))
        tiers = stone_shelf(P, -.40, 7.85, .88, .28, r)
        self.collider('BR_Shelf', (-.40, 7.85, .6), (.88, .30, 1.2))
        for (x, z) in ((-.62, tiers[0]), (-.18, tiers[0]), (-.40, tiers[1])):
            self.slot((x, 7.85, z + .001), ('S',), 'shelf', 'back_room', True, (0, -1), (.34, .24, .40 if z < 1 else .5))
        pebbles(P, [(-.82, 6.95), (-.78, 7.30), (.80, 7.92), (.30, 7.95)], r)
        bb = self.walls['Block_Back']
        self.clamp_lamp(P, 'Block_Back', bb.u_at(.25, BLOCK_D1), 1, (-.35, 6.55, .8), energy=260)
        self.clamp_lamp(P, 'Block_Partition', self.walls['Block_Partition'].u_at(-.15, PART_D), 1, (-.40, 7.85, .5), energy=220)
        self.finish()

    # ---- corridors and the cross corridor
    def corridors(self):
        W, E = self.walls['OuterWall_W'], self.walls['OuterWall_E']
        for i in range(3):   # west outer wall: 4.5, 6.4 and 10.05 (hall bay)
            pos, f, size = W.niche_slot(i); hall = pos[1] > 9.6
            self.slot(pos, ('S',), 'niche', 'hall' if hall else 'corridor_w', hall, f, size, host_module='THStage_OuterWall_W')
        pos, f, size = self.walls['Block_West'].niche_slot(0)
        self.slot(pos, ('S',), 'niche', 'corridor_w', False, f, size, host_module='THStage_Block_Shell')
        pos, f, size = self.walls['Block_East'].niche_slot(0)
        self.slot(pos, ('S',), 'niche', 'corridor_e', False, f, size, host_module='THStage_Block_Shell')
        for i in range(2):   # east outer wall: 7.3 and 9.95 (hall bay)
            pos, f, size = E.niche_slot(i); hall = pos[1] > 9.6
            self.slot(pos, ('S',), 'niche', 'hall' if hall else 'corridor_e', hall, f, size, host_module='THStage_OuterWall_E')
        for i in range(2):
            pos, f, size = self.walls['Block_Back'].niche_slot(i)
            self.slot(pos, ('S',), 'niche', 'cross', False, f, size, host_module='THStage_Block_Shell')
        for key in ('HallPartition_W', 'HallPartition_E'):
            pos, f, size = self.walls[key].niche_slot(0)
            self.slot(pos, ('S',), 'niche', 'cross', False, f, size, host_module='THStage_HallPartitions')

        self.begin('Corridor_Props'); P = self.part; r = random.Random(51)
        # cross-corridor outer corners: an open crate (west) and a pallet with a padding blanket (east)
        top = crate_open(P, -2.01, 9.15, 0, (.50, .42, .46), .04)
        self.collider('X_CrateW', (-2.01, 9.15, .23), (.52, .44, .46), .04)
        self.slot((-2.01, 9.15, top), ('S', 'M'), 'crate', 'cross', False, (.6, -1), (.42, .34, .9))
        ptop = pallet(P, 2.01, 9.15, .50, .44, r, ang=-.04)
        self.collider('X_PalletE', (2.01, 9.15, .08), (.52, .46, .16), -.04)
        self.slot((2.01, 9.15, ptop), ('M',), 'pallet', 'cross', False, (-.6, -1), (.46, .38, .9))
        # no rubble in the 1.20 m corridors (walking width); a little debris behind the corner hosts
        for (x, d, rx, rd, h) in ((-1.62, 9.30, .09, .07, .06), (1.62, 9.29, .08, .07, .05)):
            mound(P, x, d, rx, rd, h, 'soil', r)
        # festoon lights strung along the outer wall tops (the corridors' light): bulbs hang on short cords
        for key in ('OuterWall_W', 'OuterWall_E'):
            w = self.walls[key]; side = INSIDE_ONLY[key]
            for u in np.arange(.9, w.length - .6, 1.25):
                top = w.top_at(u)
                x, d = w.world(u, side * (w.t / 2 + .03))
                block(P, (x, d, top + .01), (.05, w.t + .06, .02), 'plastic', ang=w.ang, bevel=.003)
                cylinder(P, (x, d, top), (x, d, top - .14), .004, 'plastic', n=4, caps=False)
                cylinder(P, (x, d, top - .14), (x, d, top - .20), .028, 'glow_lamp', r1=.02, n=8)
                self.bulbs.append((x, d, top - .19))
        self.clamp_lamp(P, 'Block_Back', .35, -1, (1.9, 9.0, .3), energy=220)
        self.clamp_lamp(P, 'Block_Back', 1.35, -1, (-1.9, 9.0, .3), energy=220)
        self.finish()

    # ---- hall: stepped altar for the high-value finds, two side bays behind the partitions
    def hall(self):
        self.begin('Hall_Furniture'); P = self.part; r = random.Random(61)
        # stepped altar against the back wall (kept narrow: 0.6 m clear to the partition ends for the way into the bays)
        altar(P, 0, 10.59, 1.00, .34, .82, r, pillars=True)
        block(P, (0, 10.30, .21), (1.00, .20, .42), 'stone_a', bevel=.02, rng=r, jitter=.004, face_tiles={'side_d': 'relief'}, drop_bottom=True)
        block(P, (0, 10.30, .435), (1.06, .24, .03), 'stone_dark', bevel=.01)
        self.collider('Hall_Altar', (0, 10.49, .42), (1.10, .62, .84))
        for x in (-.25, .25):
            self.slot((x, 10.55, .821), ('M',), 'altar', 'hall', False, (0, -1), (.46, .30, 1.0))
        for x in (-.30, .30):
            self.slot((x, 10.30, .451), ('S',), 'altar', 'hall', False, (0, -1), (.28, .19, .6), note='front step of the altar')
        # carved stela slabs set against the back wall behind the altar, lit by two lamps clamped on the wall top
        for sx in (-1, 1): block(P, (sx * .26, 10.77, 1.30), (.48, .03, .56), 'stone_dark', bevel=.012, face_tiles={'side_d': 'relief'}, rng=r, jitter=.003)
        block(P, (0, 10.765, 1.62), (1.08, .04, .08), 'stone_c', bevel=.012, rng=r, jitter=.003)
        bw = self.walls['BackWall']
        for x in (-.62, .62):
            self.clamp_lamp(P, 'BackWall', bw.u_at(x, BACK_D), -1, (x * .3, 10.4, .85), energy=240)
        # east bay: plinth in the corner and an open crate against the back wall
        ptop = plinth(P, 2.02, 10.56, .40, .62, r)
        self.collider('HallE_Plinth', (2.02, 10.56, ptop / 2), (.50, .50, ptop))
        self.slot((2.02, 10.56, ptop + .001), ('M',), 'plinth', 'hall', True, (-1, -.8), (.40, .40, 1.0))
        top = crate_open(P, 1.40, 10.57, 0, (.50, .40, .46), -.03)
        self.collider('HallE_Crate', (1.40, 10.57, .23), (.52, .42, .46), -.03)
        self.slot((1.40, 10.57, top), ('S', 'M'), 'crate', 'hall', True, (0, -1), (.42, .32, .9))
        # west bay: ossuary chest along the west wall (the bay niche is above it), a pallet against the back wall
        inner = sarcophagus(P, -2.0, 10.22, 1.0, .48, r, ang=math.pi / 2, open_end=1, lean_side=0, h=.56)
        self.collider('HallW_Ossuary', (-2.0, 10.22, .29), (.54, 1.06, .58))
        self.slot(inner, ('S',), 'sarcophagus', 'hall', True, (1, 0), (.40, .30, .45), note='ossuary chest, the open end')
        ptop = pallet(P, -1.40, 10.56, .52, .42, r, ang=.03)
        self.collider('HallW_Pallet', (-1.40, 10.56, .08), (.54, .44, .16), .03)
        self.slot((-1.40, 10.56, ptop), ('M',), 'pallet', 'hall', True, (0, -1), (.46, .36, .9))
        self.clamp_lamp(P, 'HallPartition_W', .45, 1, (-1.9, 10.3, .3), energy=240)
        self.clamp_lamp(P, 'HallPartition_E', .9, 1, (1.8, 10.5, .4), energy=240)
        for (x, d, rx, rd, h) in ((-1.10, 10.70, .10, .08, .07), (2.16, 9.78, .10, .12, .07)):
            mound(P, x, d, rx, rd, h, 'soil', r)
        self.finish()

    # ---- ground fragments at wall feet (flat, no colliders, no shadows)
    def ground(self):
        self.begin('GroundFragments', kind='decor_ground'); P = self.part; r = random.Random(71)
        for key, w in self.walls.items():
            if w.length < .6: continue
            for side in self.visible_sides(key):
                u = .15
                while u < w.length - .3:
                    span = r.uniform(.5, 1.2)
                    if r.random() < .6 and not any(o[0] == 'door' and o[1] - .1 < u + span and u < o[2] + .1 for o in w.openings):
                        # a strip of soil swept against the wall foot: straight on the wall side, ragged outside
                        n = 8; pts = []
                        for k in range(n + 1):
                            uu = u + span * k / n; wid = (.03 + .09 * math.sin(math.pi * k / n) ** .7) * r.uniform(.7, 1.1)
                            x, d = w.world(uu, side * (w.t / 2 + .005 + wid)); pts.append((x, d))
                        for k in (n, 0):
                            x, d = w.world(u + span * k / n, side * (w.t / 2 - .01)); pts.append((x, d))
                        pts = [(max(-2.49, min(2.49, x)), max(.01, min(10.99, d))) for x, d in pts]
                        flat_poly(P, pts, 'soil', .002 + r.random() * .001)
                    u += span
        self.finish()

    # ---- preview: treasures on 23 slots (14 S + 9 M, fixed seed), floor, lights, camera
    def preview_items(self, seed=2311):
        rng = random.Random(seed)
        stats = {s['model']: s for s in json.loads((TREASURES / 'stats.json').read_text(encoding='utf-8'))}
        m_models = [m for m, s in stats.items() if s['treasure_class'] == 'M']
        s_models = [m for m, s in stats.items() if s['treasure_class'] == 'S']

        def fits(model, slot):
            dx, dy, dz = stats[model]['dimensions_m']; w, dep, h = slot['max_item_size_m']
            return dx <= w + 1e-3 and dy <= dep + 1e-3 and dz <= h + 1e-3
        m_slots = [s for s in self.slots if 'M' in s['allowed_classes']]
        plan = None
        for attempt in range(500):
            chosen_m = rng.sample(m_slots, 9)
            rest = [s for s in self.slots if s not in chosen_m and 'S' in s['allowed_classes']]
            chosen_s = rng.sample(rest, 14)
            # deeper = pricier: the most valuable M models go to the deepest chosen M slots
            order_m = sorted(chosen_m, key=lambda s: -s['pos'][1])
            ranked = ['gilded_sun_disc', 'gilded_sun_disc', 'bronze_tripod_cauldron', 'bronze_tripod_cauldron', 'stone_stele', 'stone_stele', 'clay_amphora', 'clay_amphora', 'clay_amphora']
            trial = []; ok = True

            def clash(model, slot):   # the same model on a neighbouring slot (within NEIGHBOUR_M)
                return any(m2 == model and math.dist(slot['pos'][:2], s2['pos'][:2]) < NEIGHBOUR_M for s2, m2 in trial)
            for s in order_m:
                cand = [m for m in ranked + m_models if fits(m, s) and not clash(m, s)]
                if not cand: ok = False; break
                if cand[0] in ranked: ranked.remove(cand[0])
                trial.append((s, cand[0]))
            pool_s = s_models + rng.sample(s_models, 6); rng.shuffle(pool_s)
            for s in chosen_s:
                cand = [m for m in pool_s if fits(m, s) and not clash(m, s)]
                if not cand: ok = False; break
                pool_s.remove(cand[0]); trial.append((s, cand[0]))
            if ok: plan = trial; break
        assert plan, 'no preview plan fits'
        self.preview_objects = []; self.sample_placements = []
        for s, model in plan:
            with bpy.data.libraries.load(str(TREASURES / (model + '.blend')), link=False) as (src, dst): dst.objects = src.objects
            objs = [o for o in dst.objects if o]
            for o in objs: self.preview.objects.link(o); o['preview_only'] = True
            root = next(o for o in objs if o.type == 'EMPTY' and o.parent is None)
            root.rotation_euler.z = s['rotation_blender_z']; root.location = (0, 0, 0); bpy.context.view_layer.update()
            pts = [o.matrix_world @ Vector(c) for o in objs if o.type == 'MESH' for c in o.bound_box]
            cx = (min(p.x for p in pts) + max(p.x for p in pts)) / 2; cy = (min(p.y for p in pts) + max(p.y for p in pts)) / 2
            x, d, h = s['pos']
            root.location = (x - cx, -d - cy, h - min(p.z for p in pts))
            root['slot'] = s['name']
            self.preview_objects += objs
            self.sample_placements.append(dict(slot=s['name'], model=model, class_name=stats[model]['treasure_class'], root_name=root.name))
        bpy.context.view_layer.update()

    def preview_setup(self):
        fm = bpy.data.materials.new('THStage_PREVIEW_RealFloor'); fm.use_nodes = True
        bs = next(n for n in fm.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'); bs.inputs['Base Color'].default_value = (.26, .27, .28, 1); bs.inputs['Roughness'].default_value = .9
        mesh = bpy.data.meshes.new('THStage_PREVIEW_RealFloor')
        mesh.from_pydata([L(-2.5, 0, 0), L(2.5, 0, 0), L(2.5, 11, 0), L(-2.5, 11, 0)], [], [(3, 2, 1, 0)]); mesh.update()
        mesh.materials.append(fm); obj = bpy.data.objects.new(mesh.name, mesh); self.preview.objects.link(obj); obj['preview_only'] = True
        obj.location.z = -.001
        sc = self.scene; world = bpy.data.worlds.new('THStage_PreviewWorld'); sc.world = world; world.use_nodes = True
        bg = next(n for n in world.node_tree.nodes if n.type == 'BACKGROUND'); bg.inputs['Color'].default_value = (.42, .46, .52, 1); bg.inputs['Strength'].default_value = .09
        sun = bpy.data.lights.new('THStage_PREVIEW_VenueLight', 'SUN'); sun.energy = .22; sun.angle = .5
        so = bpy.data.objects.new(sun.name, sun); self.preview.objects.link(so); so.rotation_euler = (math.radians(30), math.radians(12), math.radians(25))
        for lp in self.lamps:
            li = bpy.data.lights.new('THStage_PREVIEW_' + lp['name'], 'SPOT'); li.energy = lp['energy'] * .9; li.color = (1, .72, .45)
            li.spot_size = math.radians(90); li.spot_blend = .35; li.shadow_soft_size = .08
            lo = bpy.data.objects.new(li.name, li); self.preview.objects.link(lo); lo.location = L(*lp['pos'])
            lo.rotation_euler = (L(*lp['aim']) - lo.location).to_track_quat('-Z', 'Y').to_euler()
            self.marker('Light_' + lp['name'], lp['pos'], 'light_anchor', aim_unity_xyz=[lp['aim'][0], lp['aim'][2], lp['aim'][1]],
                        suggested='spot, warm ~3000K, range ~3 m, cone ~100 deg', blender_watts=lp['energy'])
        for i, b in enumerate(self.bulbs):   # festoon bulbs: weak warm point lights
            li = bpy.data.lights.new('THStage_PREVIEW_Bulb_%02d' % (i + 1), 'POINT'); li.energy = 18; li.color = (1, .7, .42); li.shadow_soft_size = .03
            lo = bpy.data.objects.new(li.name, li); self.preview.objects.link(lo); lo.location = L(*b)
            self.marker('Light_Bulb_%02d' % (i + 1), b, 'light_anchor', suggested='point, warm ~2700K, range ~1.5 m, weak', blender_watts=18)
        cam = bpy.data.cameras.new('THStage_PREVIEW_Camera'); co = bpy.data.objects.new(cam.name, cam); self.preview.objects.link(co); sc.camera = co
        co.location = L(6.5, -3.5, 8.5); co.rotation_euler = (L(0, 5.6, 0) - co.location).to_track_quat('-Z', 'Y').to_euler(); cam.lens = 28
        try: sc.render.engine = 'BLENDER_EEVEE'
        except TypeError: sc.render.engine = 'BLENDER_EEVEE_NEXT'
        sc.view_settings.view_transform = 'Standard'
        for attr in ('use_gtao', 'use_shadows', 'use_raytracing'):
            try: setattr(sc.eevee, attr, True)
            except AttributeError: pass
        try: sc.eevee.taa_render_samples = 48
        except AttributeError: pass

    # ---- export
    def export(self):
        bpy.context.view_layer.update()
        problems = []
        for obj in self.modules:
            pts = [obj.matrix_world @ v.co for v in obj.data.vertices]
            lo = [min(p[i] for p in pts) for i in range(3)]; hi = [max(p[i] for p in pts) for i in range(3)]
            if lo[0] < -2.5 - 1e-4 or hi[0] > 2.5 + 1e-4 or lo[1] < -11 - 1e-4 or hi[1] > 1e-4 or lo[2] < -1e-3 or hi[2] > WALL_H + .12:
                problems.append((obj.name, [round(v, 4) for v in lo], [round(v, 4) for v in hi]))
        assert not problems, problems
        fbx = dict(axis_forward='-Z', axis_up='Y', apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS', use_custom_props=True,
                   path_mode='RELATIVE', embed_textures=False, bake_anim=False, add_leaf_bones=False, mesh_smooth_type='OFF')
        for o in bpy.context.view_layer.objects: o.select_set(False)
        runtime = list(self.runtime.objects)
        for o in runtime: o.select_set(True)
        bpy.context.view_layer.objects.active = self.root
        bpy.ops.export_scene.fbx(filepath=str(OUT / 'ExcavationDepot_5x11.fbx'), use_selection=True, object_types={'MESH', 'EMPTY'}, **fbx)
        for f in (OUT / 'modules').glob('*.fbx'): f.unlink()
        for obj in self.modules:
            for o in bpy.context.view_layer.objects: o.select_set(False)
            obj.select_set(True); bpy.context.view_layer.objects.active = obj
            bpy.ops.export_scene.fbx(filepath=str(OUT / 'modules' / (obj['module_id'] + '.fbx')), use_selection=True, object_types={'MESH'}, **fbx)

        def count(key):
            out = {}
            for s in self.slots:
                k = '/'.join(s['allowed_classes']) if key == 'cls' else str(s[key])
                out[k] = out.get(k, 0) + 1
            return dict(sorted(out.items()))
        meta = dict(
            venue=dict(width_m=5, length_m=11, depth_m=11, origin='centre of the entrance edge, floor', blender_from_unity='(X, -Z, Y)', unity_from_logical='(x, h, d)'),
            layout=dict(type='B. ring corridor', wall_height_m=WALL_H, ceiling=False,
                        outer_walls=dict(center_x=OUTER_X, thickness=OUTER_T, depth=[OUTER_D0, BACK_D + BACK_T / 2]),
                        block=dict(outer_x=BLOCK_X, depth=[BLOCK_D0, BLOCK_D1], thickness=BLOCK_T, chamfer=CHAMFER, partition_depth=PART_D, partition_thickness=PART_T,
                                   front_room_door=dict(face='west', depth=list(FRONT_DOOR)), back_room_door=dict(face='east', depth=list(BACK_DOOR))),
                        corridor_width_nominal_m=round(OUTER_X - OUTER_T / 2 - BLOCK_X, 3), cross_corridor_depth=[BLOCK_D1, HALL_D - HALL_T / 2],
                        hall=dict(depth=[HALL_D + HALL_T / 2, BACK_D - BACK_T / 2], partition_center_depth=HALL_D, opening_half_width=HALL_OPEN),
                        entrance_zone=dict(depth=[0, OUTER_D0], tall_walls=False)),
            runtime_stats=stats_of(runtime), preview_treasure_stats=stats_of(self.preview_objects),
            module_stats=[dict(name=o['module_id'], beam_blocking=bool(o['beam_blocking']), kind=o['module_kind'], **stats_of([o])) for o in self.modules],
            material=dict(name='THStage_Atlas', count=1, textures=['textures/Stage_Atlas_%s.png' % k for k in ('BaseColor', 'Metallic', 'Roughness', 'Emission')],
                          atlas_px=[AW, AH], cell_px=CELL, tiles={k: dict(cell=list(v[:2]), size_cells=list(v[2:]), emissive=k in GLOW) for k, v in TILES.items()},
                          emission='Only the glow_lamp and glow_screen tiles are non-black in the Emission map.'),
            colliders=self.colliders,
            collider_status='Boxes with yaw: wall pieces split around doors and niches (beam_blocking=True), and furniture envelopes (beam_blocking=False).',
            markers=self.markers,
            slots=dict(count=len(self.slots), by_class=count('cls'), by_zone=count('zone'), by_host=count('host_type'),
                       hidden_intent=sum(1 for s in self.slots if s['hidden_intent']),
                       selection_rule='Each round pick 23 slots (4 players: 14 S + 9 M). An M needs a slot whose allowed_classes has M. '
                                      'Check max_item_size_m ([width, depth, height] in the item frame, front = facing) against the model; '
                                      'place the model base centre on the slot point and turn it so its front faces facing_unity_xz '
                                      '(Blender: rotation_blender_z on the model root). Never place the same model on two slots closer than '
                                      'neighbour_rule_m (horizontal); the preview also gives the deepest M slots the most valuable models.',
                       neighbour_rule_m=NEIGHBOUR_M),
            sample_placements=self.sample_placements,
            preview_only='floor, 23 treasures, lights, camera (Stage_Review.blend only; not in the stage FBX)',
            unity_imported=False, pico_fps_measured=False)
        (OUT / 'stage_layout.json').write_text(json.dumps(meta, indent=2, ensure_ascii=False), encoding='utf-8')
        runtime_scene = bpy.data.scenes.new('TH_Stage_Runtime_5x11'); runtime_scene.collection.children.link(self.runtime)
        runtime_scene.unit_settings.system = 'METRIC'
        bpy.data.libraries.write(str(OUT / 'ExcavationDepot_5x11.blend'), {runtime_scene}, fake_user=True, compress=True, path_remap='RELATIVE')
        bpy.data.scenes.remove(runtime_scene)
        bpy.data.libraries.write(str(OUT / 'Stage_Review.blend'), {self.scene}, fake_user=True, compress=True, path_remap='RELATIVE')
        print('STAGE', json.dumps(dict(stage=meta['runtime_stats'], treasures=meta['preview_treasure_stats'], modules=len(self.modules), slots=meta['slots']), ensure_ascii=False))


def run():
    s = Stage()
    s.build_walls(); s.lift(); s.camp(); s.block_front(); s.front_room(); s.back_room(); s.corridors(); s.hall(); s.ground()
    s.preview_items(); s.preview_setup(); s.export()
    return s


if __name__ == '__main__':
    run()
