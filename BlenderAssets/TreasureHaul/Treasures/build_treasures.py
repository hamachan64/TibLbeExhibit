"""Treasure models: one script, one shared atlas, no grip handles (the whole body is the beam target).

Fictional ancient civilisation ("the stepped kingdom", working name). Four motifs, repeated:
  1. steps (stepped plinths, crowns, finials)   2. concentric "eye" discs
  3. wave-and-teeth zigzag band (painted pottery) 4. horned bird (horns, wing brackets)
Material ladder (the value reads from the material): clay < stone < bronze < silver < gold.

Headless, nothing but files is written:
  blender -b --factory-startup --python build_treasures.py [-- model ...]
Writes <model>.blend / <model>.fbx, textures/Treasure_Atlas_*.png and merges entries into stats.json
(old entries without 'treasure_class' are kept). Front of every model faces -Y (Blender); origin = centre of
the base footprint on z=0. Aim zones (what the hand beam may hit) are EMPTY children named <model>_Aim_*,
with 'aim_center_xyz' / 'aim_size_xyz' in local metres (S: one zone, M: left/right halves).

Shading: every face is smooth; only edges sharper than the model's sharp angle (steps, rims, chamfers) are
hard, and face-area weighted normals keep big flat faces flat. The FBX carries these split normals
(mesh_smooth_type='OFF'). S models are 1.2x the first drafts (user decision). Budgets: S 3,000 / M 4,000 tris.

Atlas (1024 x 256, 8 x 2 tiles of 128 px, BaseColor / Metallic / Roughness):
  Most tiles are *material gradients*: inside the tile, v runs from cavity (0: grime, verdigris, tarnish)
  through the plain material (~0.5) to worn, polished edges (1). Each face corner gets its v from a baked
  ambient occlusion + convexity value, so one shared atlas still gives every model its own dirt and wear.
  u is a planar position across the model (mottling only). Painted bands / mirror face use explicit UVs.
"""
import bpy, bmesh, json, math, sys
import numpy as np
from pathlib import Path
from mathutils import Vector, Matrix, Euler, noise
from mathutils.bvhtree import BVHTree

HERE = Path(__file__).resolve().parent
TEX = HERE / 'textures'
CM = 0.01
TS, COLS, ROWS = 128, 8, 2
MARGIN = 6 / TS
# 0-14 used; 15 free (new models mostly reuse the material tiles).
TILE = dict(clay=0, band_zigzag=1, slip=2, stone=3, bronze=4, silver=5, gold=6, turquoise=7,
            dark=8, mirror=9, band_steps=10, alabaster=11, glyphs=12, lacquer=13, band_stone=14)


# ----------------------------------------------------------------------------------------------
# Atlas
# ----------------------------------------------------------------------------------------------
def _vnoise(n, cy, cx, rng):
    g = rng.random((cy, cx))

    def axis(c):
        t = np.arange(n) * c / n; i0 = np.floor(t).astype(int); f = t - i0
        return i0 % c, (i0 + 1) % c, f * f * (3 - 2 * f)
    y0, y1, fy = axis(cy); x0, x1, fx = axis(cx)
    a, b, c, d = g[np.ix_(y0, x0)], g[np.ix_(y0, x1)], g[np.ix_(y1, x0)], g[np.ix_(y1, x1)]
    fx = fx[None, :]; fy = fy[:, None]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def _fbm(rng, cy=4, cx=4, octaves=4):
    out = np.zeros((TS, TS)); amp = 1; tot = 0
    for o in range(octaves):
        out += amp * _vnoise(TS, cy * 2 ** o, cx * 2 ** o, rng); tot += amp; amp *= .5
    return out / tot


def _gradient(stops):
    v = np.clip((np.arange(TS) / (TS - 1) - MARGIN) / (1 - 2 * MARGIN), 0, 1)
    vs = [s[0] for s in stops]
    col = np.stack([np.interp(v, vs, [s[1][i] for s in stops]) for i in range(3)], -1)
    met = np.interp(v, vs, [s[2] for s in stops]); rgh = np.interp(v, vs, [s[3] for s in stops])
    full = lambda a: np.repeat(a[:, None, ...], TS, 1)
    return v, full(col), full(met), full(rgh)


# Gradient tiles: (v, sRGB colour, metallic, roughness). v=0 cavity, ~.5 plain, 1 worn edge.
# Inside a gradient tile only u carries texture variation; v is the baked shade, so 2-D detail (grit, pits)
# would be smeared into streaks. Surface mottling comes from noise added to v per face corner instead.
GRADIENTS = {
    'clay': [(0, (.11, .075, .055), 0, .97), (.28, (.27, .17, .115), 0, .95), (.5, (.44, .275, .18), 0, .92),
             (.74, (.53, .345, .235), 0, .9), (.9, (.61, .45, .33), 0, .88), (1, (.72, .60, .47), 0, .86)],
    'slip': [(0, (.15, .115, .085), 0, .96), (.3, (.35, .29, .215), 0, .93), (.55, (.54, .46, .35), 0, .9),
             (.8, (.62, .55, .43), 0, .88), (1, (.72, .66, .55), 0, .86)],
    'stone': [(0, (.07, .065, .055), 0, .97), (.3, (.19, .18, .16), 0, .95), (.55, (.31, .295, .265), 0, .92),
              (.8, (.40, .38, .34), 0, .9), (1, (.52, .50, .45), 0, .88)],
    'bronze': [(0, (.06, .12, .10), .0, .92), (.16, (.18, .38, .31), .05, .86), (.27, (.26, .42, .32), .2, .78),
               (.4, (.20, .13, .07), .75, .56), (.6, (.33, .215, .11), .85, .46), (.84, (.54, .37, .20), .95, .34),
               (1, (.76, .58, .36), 1, .26)],
    'silver': [(0, (.07, .065, .06), .6, .6), (.2, (.32, .31, .30), .75, .46), (.42, (.70, .71, .73), .8, .32),
               (.72, (.86, .87, .89), .85, .24), (1, (.97, .97, .98), .9, .16)],
    'gold': [(0, (.18, .08, .025), .5, .72), (.22, (.50, .28, .07), .9, .48), (.45, (.86, .60, .20), 1, .32),
             (.75, (1.0, .76, .33), 1, .22), (1, (1.0, .90, .62), 1, .13)],
    'turquoise': [(0, (.03, .12, .12), 0, .55), (.4, (.08, .36, .35), 0, .42), (.75, (.18, .50, .47), 0, .36),
                  (1, (.40, .66, .60), 0, .3)],
    'dark': [(0, (.022, .018, .014), 0, 1), (.6, (.09, .06, .045), 0, .97), (1, (.20, .13, .09), 0, .95)],
    # milky to grey-green polished stone (unguent jar)
    'alabaster': [(0, (.14, .15, .12), 0, .7), (.3, (.36, .38, .32), 0, .55), (.55, (.55, .57, .49), 0, .45),
                  (.8, (.66, .67, .59), 0, .4), (1, (.80, .79, .71), 0, .36)],
    # dark ground under gilding (sun disc recesses)
    'lacquer': [(0, (.025, .012, .008), 0, .75), (.5, (.09, .04, .025), 0, .6), (1, (.20, .10, .06), 0, .5)],
}


def build_atlas():
    rng = np.random.default_rng(1021)
    W, H = TS * COLS, TS * ROWS
    base = np.zeros((H, W, 3), np.float32); metal = np.zeros((H, W), np.float32); rough = np.ones((H, W), np.float32)
    yy, xx = np.mgrid[0:TS, 0:TS]
    U = np.clip((xx / (TS - 1) - MARGIN) / (1 - 2 * MARGIN), 0, 1); V = np.clip((yy / (TS - 1) - MARGIN) / (1 - 2 * MARGIN), 0, 1)

    def put(tile, col, met, rgh):
        c0, r0 = (tile % COLS) * TS, (tile // COLS) * TS
        base[r0:r0 + TS, c0:c0 + TS] = np.clip(col, 0, 1); metal[r0:r0 + TS, c0:c0 + TS] = np.clip(met, 0, 1)
        rough[r0:r0 + TS, c0:c0 + TS] = np.clip(rgh, 0, 1)

    def u_noise(c1=5, c2=17):   # varies along u only
        return .6 * _vnoise(TS, 1, c1, rng) + .4 * _vnoise(TS, 1, c2, rng)

    for name, stops in GRADIENTS.items():
        v, col, met, rgh = _gradient(stops)
        mott = u_noise()
        if name in ('clay', 'slip', 'stone', 'dark'):
            col = col * (1 + .16 * (mott - .5))[..., None]
        elif name == 'bronze':
            col = col * (1 + .12 * (mott - .5))[..., None]
            m = np.clip((u_noise(4, 11) - .45) * 5, 0, 1) * np.clip((.66 - V) * 5, 0, 1)    # verdigris creeping higher in places
            green = np.array((.22, .42, .34))
            col = col * (1 - m[..., None]) + green * m[..., None]; met = met * (1 - m) + .05 * m; rgh = rgh * (1 - m) + .85 * m
        elif name == 'silver':
            m = np.clip((u_noise(4, 13) - .52) * 5, 0, 1) * np.clip((.66 - V) * 4, 0, 1)    # warm tarnish
            col = col * (1 - .5 * m[..., None]) + np.array((.22, .18, .14)) * .5 * m[..., None]; rgh = rgh + .15 * m
        elif name == 'gold':
            col = col * (1 + .05 * (mott - .5))[..., None]
            m = np.clip((u_noise(5, 15) - .55) * 5, 0, 1) * np.clip((.5 - V) * 3, 0, 1)    # reddish grime in recesses
            col = col * (1 - .5 * m[..., None]) + np.array((.30, .12, .04)) * .5 * m[..., None]
        elif name == 'alabaster':   # soft green / cream clouds
            m = u_noise(3, 9)
            col = col * (1 - .25 * m[..., None]) + np.array((.52, .60, .50)) * .25 * m[..., None]
        elif name == 'turquoise':
            vein = _fbm(rng, 5, 5)
            m = np.abs(vein - .5) < .02                                                  # dark matrix veins
            col = col * (1 + .15 * (_fbm(rng, 3, 3) - .5))[..., None]; col[m] = np.array((.10, .08, .06))
        put(TILE[name], col, met, rgh)

    # Satin-polished mirror face (planar disc): bright centre, tarnish creeping in from the rim. Kept slightly
    # rough and not fully metallic so it still reads as bright silver in a dark room (a true mirror goes black).
    r = np.hypot(U - .5, V - .5) * 2
    f = _fbm(rng, 4, 4)
    t = np.clip((r + .22 * (f - .5) - .6) / .38, 0, 1) ** 1.4
    col = np.array((.90, .91, .93)) * (1 - t[..., None]) + np.array((.42, .38, .33)) * t[..., None]
    sheen = .06 * np.clip(1 - np.abs((U - V) * 3), 0, 1)                         # soft diagonal sheen
    col = col + sheen[..., None]
    put(TILE['mirror'], col, .82 + .1 * t, .26 + .3 * t + .04 * (f - .5))

    paint = np.array((.11, .065, .045)); red = np.array((.52, .19, .10))
    # Band patterns: 2 periods per tile (the lathe wraps the tile several times, so one period gets 64 px),
    # rasterised at 4x and averaged so the edges are anti-aliased instead of stair-stepped.
    SS = 4; y4, x4 = np.mgrid[0:TS * SS, 0:TS * SS]
    U4 = np.clip(((x4 + .5) / (TS * SS) - MARGIN) / (1 - 2 * MARGIN), 0, 1); V4 = np.clip(((y4 + .5) / (TS * SS) - MARGIN) / (1 - 2 * MARGIN), 0, 1)
    cover = lambda m: m.astype(np.float32).reshape(TS, SS, TS, SS).mean(axis=(1, 3))

    def painted(bg_stops, cov_dark, cov_red=None, ink=paint, ink2=red):
        _, col, met, rgh = _gradient(bg_stops)
        col = col * (1 + .14 * (_fbm(rng, 3, 3) - .5))[..., None]
        fade = (1 + .2 * (_fbm(rng, 4, 4) - .5))[..., None]       # clean paint, only a soft tonal wear
        for cov, c in ((cov_dark, ink), (cov_red, ink2)):
            if cov is None: continue
            col = col * (1 - cov[..., None]) + c * fade * cov[..., None]
        col = col * (.84 + .16 * np.clip(np.minimum(V, 1 - V) * 6, 0, 1))[..., None]   # a little dirt at the edges
        return col, met, rgh * np.ones((TS, TS))

    buff = [(0, (.64, .50, .35), 0, .9), (1, (.71, .57, .41), 0, .88)]
    P = 2

    def zigzag(Uu, Vv):    # wave and teeth (motif 3)
        ph = (Uu * P) % 1.0
        lines = ((Vv > .04) & (Vv < .12)) | ((Vv > .88) & (Vv < .96))
        wave = np.abs(Vv - (.70 + .075 * np.sin(2 * np.pi * Uu * P))) < .045
        teeth = (Vv > .12) & (Vv < .12 + .40 * (1 - np.abs(2 * ((Uu * P) % 1.0) - 1)))
        return lines | wave | teeth
    col, met, rgh = painted(buff, cover(zigzag(U4, V4)))
    put(TILE['band_zigzag'], col, met, rgh)

    def steps(Uu, Vv, n_up=4):    # stepped pyramids (motif 1): rising dark, hanging red, half a period apart
        tri = 1 - np.abs(2 * ((Uu * P) % 1.0) - 1); tri2 = 1 - np.abs(2 * ((Uu * P + .5) % 1.0) - 1)
        st = np.floor(tri * n_up) / n_up; st2 = np.floor(tri2 * n_up) / n_up
        lines = ((Vv > .03) & (Vv < .10)) | ((Vv > .90) & (Vv < .97))
        up = (Vv >= .10) & (Vv < .10 + .42 * st); down = (Vv <= .90) & (Vv > .90 - .42 * st2)
        return lines | up, down & ~up
    d4, r4 = steps(U4, V4)
    col, met, rgh = painted(buff, cover(d4), cover(r4))
    put(TILE['band_steps'], col, met, rgh)
    # The same stepped band incised into polished stone (unguent jar): dark-filled cuts on alabaster.
    d4, r4 = steps(U4, V4, 3)
    col, met, rgh = painted([(0, (.50, .52, .45), 0, .45), (1, (.58, .59, .52), 0, .42)], cover(d4 | r4) * .9,
                            ink=np.array((.16, .17, .14)))
    put(TILE['band_stone'], col, met, rgh)

    # Fictional script (stele panel): 8 columns x 3 rows of carved glyphs, drawn in cell-normalised
    # coordinates so they stay square on a 24 x 9 cm panel.
    _, col, met, rgh = _gradient([(0, (.33, .315, .285), 0, .92), (1, (.42, .405, .365), 0, .9)])
    col = col * (1 + .15 * (_fbm(rng, 4, 4) - .5))[..., None]
    lx = (U * 8) % 1.0; ly = (V * 3) % 1.0; cid = (np.floor(U * 8) + 8 * np.floor(V * 3)).astype(int)
    rg = np.random.default_rng(77); mask = np.zeros((TS, TS), bool)
    strokes = [lambda: (np.abs(lx - .5) < .07) & (ly > .16) & (ly < .84),
               lambda: (np.abs(ly - .8) < .07) & (lx > .16) & (lx < .84),
               lambda: (np.abs(ly - .2) < .07) & (lx > .16) & (lx < .84),
               lambda: (np.abs(ly - .5) < .07) & (lx > .25) & (lx < .75),
               lambda: np.abs(np.hypot(lx - .5, ly - .5) - .24) < .065,
               lambda: np.hypot(lx - .5, ly - .5) < .09,
               lambda: (np.abs(ly - (.2 + .2 * np.floor((lx - .16) / .23))) < .065) & (lx > .16) & (lx < .84),
               lambda: (np.abs(ly - lx) < .08) & (lx > .16) & (lx < .84),
               lambda: (np.abs(ly - (.25 + .9 * np.abs(lx - .5))) < .07) & (lx > .16) & (lx < .84)]
    for c in range(24):
        for k in rg.choice(len(strokes), size=rg.integers(2, 4), replace=False):
            mask |= strokes[k]() & (cid == c)
    lip = np.roll(mask, -1, axis=0) & ~mask                                        # lit lower lip of each cut
    col[mask] = np.array((.12, .11, .10)); col[lip] *= 1.25
    put(TILE['glyphs'], col, met, rgh * np.ones((TS, TS)))

    TEX.mkdir(exist_ok=True)
    images = []
    for label, data in (('BaseColor', base), ('Metallic', metal), ('Roughness', rough)):
        rgba = np.ones((H, W, 4), np.float32)
        rgba[..., :3] = data if data.ndim == 3 else data[..., None]
        im = bpy.data.images.new('THTreasure_' + label, width=W, height=H, alpha=False)
        if label != 'BaseColor': im.colorspace_settings.name = 'Non-Color'
        im.pixels.foreach_set(rgba.ravel()); im.filepath_raw = str(TEX / ('Treasure_Atlas_' + label + '.png'))
        im.file_format = 'PNG'; im.save(); im.pack(); images.append(im)
    mat = bpy.data.materials.new('THTreasure_Atlas'); mat.use_nodes = True
    bs = next(x for x in mat.node_tree.nodes if x.type == 'BSDF_PRINCIPLED')
    for im, socket in zip(images, ('Base Color', 'Metallic', 'Roughness')):
        tx = mat.node_tree.nodes.new('ShaderNodeTexImage'); tx.image = im
        mat.node_tree.links.new(tx.outputs['Color'], bs.inputs[socket])
    return mat




def atlas_uv(tile, u, v):
    u = min(1.0, max(0.0, u)); v = min(1.0, max(0.0, v))
    col, row = tile % COLS, tile // COLS
    return ((col + MARGIN + (1 - 2 * MARGIN) * u) / COLS, (row + MARGIN + (1 - 2 * MARGIN) * v) / ROWS)


# ----------------------------------------------------------------------------------------------
# Modelling helpers. Inputs in centimetres; x across, y depth (front = -y), z up.
# ----------------------------------------------------------------------------------------------
def V3(x, y, z): return Vector((x * CM, y * CM, z * CM))


def sup2d(n, p=2.0, rot=None):
    """Unit superellipse, n points, counter-clockwise. p=2 circle/ellipse, larger p = squarer."""
    rot = math.pi / n if rot is None else rot
    out = []
    for k in range(n):
        a = rot + 2 * math.pi * k / n; c, s = math.cos(a), math.sin(a)
        out.append((math.copysign(abs(c) ** (2 / p), c), math.copysign(abs(s) ** (2 / p), s)))
    return out


def ring(cx, cy, z, rx, ry, n, p=2.0, rot=None, jitter=0.0, seed=0.0):
    pts = []
    for x, y in sup2d(n, p, rot):
        j = 1 + jitter * noise.noise(Vector((x * 1.3 + seed, y * 1.3 - seed, z * .2 + seed))) if jitter else 1
        pts.append(V3(cx + x * rx * j, cy + y * ry * j, z))
    return pts


def lathe_rings(profile, n, cx=0.0, cy=0.0, jitter=0.0, seed=0.0, wobble=0.0, rot=None):
    rings = []
    for r, z in profile:
        if r < 1e-6: rings.append([V3(cx, cy, z)])
        else:
            ox = wobble * math.sin(z * .45 + seed); oy = wobble * math.cos(z * .31 + seed)
            rings.append(ring(cx + ox, cy + oy, z, r, r, n, 2.0, rot, jitter, seed))
    return rings


def catmull(P, per_seg):
    out = []
    ext = [2 * P[0] - P[1]] + list(P) + [2 * P[-1] - P[-2]]
    for i in range(1, len(ext) - 2):
        p0, p1, p2, p3 = ext[i - 1], ext[i], ext[i + 1], ext[i + 2]
        for s in range(per_seg):
            t = s / per_seg; t2, t3 = t * t, t * t * t
            out.append(.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3))
    out.append(P[-1].copy())
    return out


def sstep(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a))); return t * t * (3 - 2 * t)


UDIR = Vector((.72, .48, .5)).normalized()   # u of gradient tiles: one continuous direction, so no seams


class Model:
    def __init__(self, name, scale=1.0, sharp_deg=40.0):
        self.name = name; self.scale = scale; self.sharp = math.radians(sharp_deg)
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.new('UVMap')
        self.lt = self.bm.faces.layers.int.new('tile'); self.lm = self.bm.faces.layers.int.new('mode')
        self.lb = self.bm.faces.layers.float.new('bias')
        self.rings = []; self.vbias = {}   # per-vertex shade offsets (chips): blend smoothly into the surface

    def face(self, verts, tile, bias=0.0, uvs=None):
        try: f = self.bm.faces.new(verts)
        except ValueError: return None
        f[self.lt] = TILE[tile]; f[self.lb] = bias
        if uvs is None: f[self.lm] = 0
        else:
            f[self.lm] = 1
            for loop, (u, v) in zip(f.loops, uvs): loop[self.uv].uv = atlas_uv(TILE[tile], u, v)
        return f

    def tag(self, faces, tile, bias=0.0):
        for f in faces: f[self.lt] = TILE[tile]; f[self.lm] = 0; f[self.lb] = bias

    def loft(self, rings, tile='clay', bias=0.0, bands=None, cap0=True, cap1=True, uturns=1, cap_bias=None, closed=False):
        """Skin consecutive rings (lists of Vectors, equal length or 1 = apex). closed=True joins the last ring
        back to the first (torus). bands[i] (strip ring i -> i+1): None (gradient tile), ('grad', tile, bias),
        ('cyl', tile, v0, v1) wrapping a painted band uturns times, ('planar', tile, fn(co) -> (u, v))."""
        vr = [[self.bm.verts.new(p) for p in r] for r in rings]
        faces = []
        seq = vr + [vr[0]] if closed else vr
        for i in range(len(seq) - 1):
            a, b = seq[i], seq[i + 1]
            spec = (bands[i] if bands and i < len(bands) else None) or ('grad', tile, bias)
            n = max(len(a), len(b))
            for k in range(n):
                k2 = (k + 1) % n
                if len(a) == 1: quad = [(a[0], k, 0), (b[k], k, 1), (b[k2], k + 1, 1)][::-1]
                elif len(b) == 1: quad = [(a[k], k, 0), (a[k2], k + 1, 0), (b[0], k, 1)]
                else: quad = [(a[k], k, 0), (a[k2], k + 1, 0), (b[k2], k + 1, 1), (b[k], k, 1)]
                verts = [q[0] for q in quad]
                if spec[0] == 'grad': f = self.face(verts, spec[1], spec[2])
                elif spec[0] == 'cyl':
                    _, t, v0, v1 = spec; du = uturns / n; u0 = (k * du) % 1.0
                    f = self.face(verts, t, 0, [(u0 + du * (q[1] - k), v0 + (v1 - v0) * q[2]) for q in quad])
                else: f = self.face(verts, spec[1], 0, [spec[2](q[0].co) for q in quad])
                if f: faces.append(f)
        cb = bias if cap_bias is None else cap_bias
        if not closed:
            if cap0 and len(vr[0]) > 2: faces.append(self.face(list(reversed(vr[0])), tile, cb))
            if cap1 and len(vr[-1]) > 2: faces.append(self.face(vr[-1], tile, cb))
        self.rings = vr
        return [v for r in vr for v in r], [f for f in faces if f]

    def slab(self, cx, cy, z0, z1, w, d, c=.3, tile='stone', bias=0.0, n=20, p=4.0, jitter=0.0, seed=0.0, taper=0.0):
        """Chamfered rounded-rectangle block (plinths, steps): rounded corners smooth, chamfers hard."""
        hw, hd = w / 2, d / 2
        rings = [ring(cx, cy, z0, hw - c, hd - c, n, p, None, jitter, seed), ring(cx, cy, z0 + c, hw, hd, n, p, None, jitter, seed),
                 ring(cx, cy, z1 - c, hw - taper, hd - taper, n, p, None, jitter, seed),
                 ring(cx, cy, z1, hw - c - taper, hd - c - taper, n, p, None, jitter, seed)]
        return self.loft(rings, tile, bias)

    def box(self, c, size, tile, bias=0.0, bevel=.25, taper=(1, 1), rot=(0, 0, 0), seg=1):
        before = set(self.bm.faces)
        vs = bmesh.ops.create_cube(self.bm, size=1.0)['verts']
        for v in vs:
            top = v.co.z > 0
            v.co.x *= size[0] * CM * (taper[0] if top else 1); v.co.y *= size[1] * CM * (taper[1] if top else 1); v.co.z *= size[2] * CM
        bmesh.ops.transform(self.bm, matrix=Matrix.Translation(Vector(c) * CM) @ Euler(rot, 'XYZ').to_matrix().to_4x4(), verts=vs)
        if bevel:
            bmesh.ops.bevel(self.bm, geom=list({e for v in vs for e in v.link_edges}), offset=bevel * CM, segments=seg, profile=.5, affect='EDGES')
        new = [f for f in self.bm.faces if f not in before]
        self.tag(new, tile, bias)
        return list({v for f in new for v in f.verts}), new

    def tube(self, ctrl, rfn, n=10, p=2.0, tile='bronze', bias=0.0, per_seg=3, up=(0, 1, 0), cap=True):
        """Sweep a superellipse along a Catmull-Rom path. rfn(t) -> (radius along 'up' side, radius across)."""
        pts = catmull([Vector(c) * CM for c in ctrl], per_seg); N = len(pts)
        rings, side = [], None
        for i, pt in enumerate(pts):
            t = (pts[min(i + 1, N - 1)] - pts[max(i - 1, 0)]).normalized()
            u = Vector(up) if side is None else side
            side = (u - t * u.dot(t)).normalized(); b = t.cross(side)
            ra, rb = rfn(i / (N - 1))
            rings.append([pt + side * (x * ra * CM) + b * (y * rb * CM) for x, y in sup2d(n, p)])
        return self.loft(rings, tile, bias, cap0=cap, cap1=cap)

    def torus(self, c, R, r, M=None, nseg=24, nside=10, tile='bronze', bias=0.0, rs=None):
        """Ring in the local x-z plane (axis = y). rs = cross-section radius along y (default r)."""
        T = Matrix.Translation(Vector(c) * CM) @ (M or Matrix.Identity(4)); rings = []
        for i in range(nseg):
            a = 2 * math.pi * i / nseg; d = Vector((math.cos(a), 0, math.sin(a))); cen = d * R * CM
            rings.append([T @ (cen + d * (x * r * CM) + Vector((0, y * (rs or r) * CM, 0))) for x, y in sup2d(nside)])
        return self.loft(rings, tile, bias, closed=True)

    def ydisc(self, c, profile, n, bands=None, tile='gold', bias=0.0, M=None):
        """Lathe around the Y axis (discs facing the front). profile: [(r, y)] in cm, centre c in cm."""
        rings = []
        for r, y in profile:
            if r < 1e-6: rings.append([Vector((0, y * CM, 0))])
            else: rings.append([Vector((x * r * CM, y * CM, s * r * CM)) for x, s in sup2d(n, 2.0, math.pi / 2)])
        T = Matrix.Translation(Vector(c) * CM) @ (M or Matrix.Identity(4))
        rings = [[T @ p for p in rg] for rg in rings]
        return self.loft(rings, tile, bias, bands=bands, cap0=False, cap1=False)

    def lens(self, c, rx, rz, depth, tile='turquoise', n=14, p=2.0, bias=0.0, M=None):
        """Domed cabochon / inlay facing -y, half sunk into the surface behind it."""
        sec = [(.0, 1.0, 1.0), (-.55, 1.0, 1.0), (-.85, .8, .78), (-1.0, .45, .42)]
        T = Matrix.Translation(Vector(c) * CM) @ (M or Matrix.Identity(4))
        rings = [[T @ V3(x * rx * sx, d * depth / 2, z * rz * sz) for x, z in sup2d(n, p, 0.0)] for d, sx, sz in sec]
        return self.loft(rings, tile, bias)

    def prism(self, outline, y0, y1, tile, bias=0.0, inset=0.0):
        """Extrude a polygon drawn in the x-z plane (cm) from y0 (back) to y1 (front); inset chamfers the front edge."""
        cx = sum(p[0] for p in outline) / len(outline); cz = sum(p[1] for p in outline) / len(outline)
        rings = [[V3(x, y0, z) for x, z in outline]]
        if inset:
            k = 1 - inset / max(1e-3, max(math.hypot(x - cx, z - cz) for x, z in outline))
            rings += [[V3(x, y1 + (y0 - y1) * .25, z) for x, z in outline],
                      [V3(cx + (x - cx) * k, y1, cz + (z - cz) * k) for x, z in outline]]
        else: rings.append([V3(x, y1, z) for x, z in outline])
        return self.loft(rings, tile, bias)

    def xform(self, verts, fn):
        for v in verts: v.co = fn(v.co)

    def chip(self, ring_moves, ang, width):
        """Knock a V-shaped chip into the last lathe. ring_moves: [(ring index, dz, dr)] in cm; ang/width in degrees."""
        moved = set()
        for ri, dz, dr in ring_moves:
            for v in self.rings[ri]:
                a = math.degrees(math.atan2(v.co.y, v.co.x)); da = abs((a - ang + 180) % 360 - 180)
                s = max(0.0, 1 - da / width)
                if s <= 0: continue
                d = Vector((v.co.x, v.co.y, 0)).normalized()
                v.co += Vector((0, 0, dz * s * CM)) + d * (dr * s * CM); moved.add(v)
                self.vbias[v] = max(self.vbias.get(v, 0.0), .32 * s ** .5)   # raw, lighter clay where it broke
        return moved

    # ------------------------------------------------------------------------------------------
    def finish(self, mat, ao_radius):
        bm = self.bm
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
        zs = min(v.co.z for v in bm.verts)
        base = [v for v in bm.verts if v.co.z < zs + .002]
        cx = (min(v.co.x for v in base) + max(v.co.x for v in base)) / 2; cy = (min(v.co.y for v in base) + max(v.co.y for v in base)) / 2
        for v in bm.verts: v.co = (v.co - Vector((cx, cy, zs))) * self.scale
        bmesh.ops.triangulate(bm, faces=list(bm.faces), quad_method='BEAUTY', ngon_method='BEAUTY')
        bm.normal_update()
        # Smooth shading everywhere; only intended corners (steps, rims, chamfers) become hard edges.
        for f in bm.faces: f.smooth = True
        for e in bm.edges:
            e.smooth = len(e.link_faces) == 2 and e.calc_face_angle(0.0) <= self.sharp
        self._shade(ao_radius * self.scale)
        vol = sum(f.calc_center_median().dot(f.normal) * f.calc_area() for f in bm.faces) / 3
        assert vol > 0, (self.name, 'inward normals', vol)
        mesh = bpy.data.meshes.new(self.name + '_Body'); bm.to_mesh(mesh); bm.free()
        for a in [a.name for a in mesh.attributes if a.name in ('tile', 'mode', 'bias')]: mesh.attributes.remove(mesh.attributes[a])
        mesh.materials.append(mat)
        obj = bpy.data.objects.new(self.name + '_Body', mesh)
        # Face-area weighted normals: large flat faces (plinth sides, stele faces) stay flat instead of picking up
        # gradients from their rounded corners; curved surfaces stay smooth and sharp edges stay sharp.
        sc = bpy.context.scene; sc.collection.objects.link(obj)
        mod = obj.modifiers.new('wn', 'WEIGHTED_NORMAL'); mod.mode = 'FACE_AREA'; mod.weight = 50; mod.keep_sharp = True
        dg = bpy.context.evaluated_depsgraph_get()
        baked = bpy.data.meshes.new_from_object(obj.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)
        obj.modifiers.clear(); old = obj.data; obj.data = baked; baked.name = self.name + '_Body'
        bpy.data.meshes.remove(old); sc.collection.objects.unlink(obj)
        if not baked.materials: baked.materials.append(mat)
        return obj

    def _shade(self, R):
        """Bake AO + convexity into the v coordinate of gradient tiles, one value per vertex (smooth, no seams)."""
        bm = self.bm
        tree = BVHTree.FromBMesh(bm)
        N = 32; dirs = []
        for i in range(N):   # cosine-weighted hemisphere (z up)
            u = (i + .5) / N; r = math.sqrt(u); ph = i * 2.39996
            dirs.append(Vector((r * math.cos(ph), r * math.sin(ph), math.sqrt(1 - u))))
        proj = [v.co.dot(UDIR) for v in bm.verts]; plo, pspan = min(proj), max(1e-4, max(proj) - min(proj))
        cache = {}

        def value(v):
            if v in cache: return cache[v]
            n_ = v.normal; q = Vector((0, 0, 1)).rotation_difference(n_); p = v.co + n_ * .0012
            occ = 0
            for d in dirs:
                dw = q @ d
                if tree.ray_cast(p, dw, R)[0] is not None: occ += 1
                elif dw.z < -1e-4 and p.z / -dw.z < R: occ += 1
            occ /= N
            s = c = 0
            for e in v.link_edges:
                d = e.other_vert(v).co - v.co
                if d.length > 1e-7: s += n_.dot(d.normalized()); c += 1
            edge = min(1.0, max(0.0, (-(s / c if c else 0) - .05) * 2.4))
            nz = .65 * noise.noise(v.co * 30.0) + .35 * noise.noise(v.co * 80.0 + Vector((5, 1, 3)))
            cache[v] = (.58 + .55 * edge - 1.0 * occ ** 1.3 + .1 * nz, (v.co.dot(UDIR) - plo) / pspan)
            return cache[v]
        for f in bm.faces:
            if f[self.lm] != 0: continue
            for loop in f.loops:
                val, u = value(loop.vert)
                vb = self.vbias.get(loop.vert, 0.0) if f[self.lt] != TILE['dark'] else 0.0
                loop[self.uv].uv = atlas_uv(f[self.lt], u, val + f[self.lb] + vb)


def tri_count(obj):
    obj.data.calc_loop_triangles(); return len(obj.data.loop_triangles)


G = lambda t, b=0.0: ('grad', t, b)
S_SCALE = 1.2   # the first five S pieces were drawn at 1/1.2 size (user decision: S is 1.2x)


# ----------------------------------------------------------------------------------------------
# Models (the first five S models are drawn small and scaled by S_SCALE; the new ones are drawn at size)
# ----------------------------------------------------------------------------------------------
def clay_pot():
    """土の小壺 — S, clay, 100-200G, ~18 cm. Hand-built squat jar: wave-and-teeth band on the belly,
    cream-slipped rim with a fresh chip, two stubby lugs on the shoulder."""
    m = Model('clay_pot', S_SCALE); n = 28
    prof = [(0, 0), (3.5, 0), (4.1, .35), (4.4, 1.1), (5.0, 1.8), (5.8, 2.9), (6.6, 4.4), (7.1, 5.4), (7.3, 6.5), (7.32, 7.5), (7.2, 8.4),
            (6.9, 9.3), (6.35, 10.2), (5.65, 10.95), (4.85, 11.6), (4.25, 12.15), (3.98, 12.7), (3.95, 13.2), (4.15, 13.7), (4.5, 14.15),
            (4.85, 14.55), (4.8, 15.0), (4.25, 15.05), (3.7, 14.4), (3.45, 13.0), (3.3, 11.8), (2.5, 11.4), (0, 11.2)]
    bands = [G('clay')] * 8 + [('cyl', 'band_zigzag', 0, .5), ('cyl', 'band_zigzag', .5, 1)] + \
            [G('clay')] * 10 + [G('slip')] * 3 + [G('dark', .1)] + [G('dark')] * 4
    m.loft(lathe_rings(prof, n, jitter=.01, seed=3.0, wobble=.05), 'clay', bands=bands, cap0=False, cap1=False, uturns=4)
    m.chip([(19, -.25, -.1), (20, -.75, -.25), (21, -.9, -.2), (22, -.85, 0), (23, -.35, .05)], -78, 20)
    m.chip([(2, .25, -.2), (3, 0, -.25)], 200, 18)
    for s in (-1, 1):   # lugs: short rounded knobs
        m.tube([(s * 6.2, 0, 9.55), (s * 6.9, 0, 9.6), (s * 7.5, 0, 9.65)], lambda t: (.95 - .2 * t, .8 - .15 * t), n=12, tile='clay', per_seg=2)
    return m, 'S', 'clay', (100, 200), .035


def clay_amphora():
    """大壺 — M, clay, 400-500G, ~38 cm. Egg-shaped storage jar; two strap handles sweep in one arc from the
    neck to the shoulder (the natural left / right beam targets). Stepped-pyramid band on the neck,
    wave-and-teeth band on the shoulder."""
    m = Model('clay_amphora'); n = 32
    prof = [(0, 0), (4.0, 0), (4.6, .6), (5.0, 1.7), (5.7, 2.8), (6.6, 4.0), (7.7, 5.6), (8.7, 7.4), (9.6, 9.4), (10.35, 11.5), (10.9, 13.6),
            (11.2, 15.3), (11.3, 17.0), (11.0, 20.2), (10.0, 23.4), (9.2, 24.9), (8.2, 26.2), (7.1, 27.5), (6.1, 28.6), (5.4, 29.5),
            (5.0, 30.4), (4.8, 31.4), (4.75, 32.4), (4.95, 34.4), (5.6, 35.6), (6.35, 36.3), (6.45, 37.3), (6.0, 38.0), (5.0, 38.0),
            (4.25, 37.0), (3.95, 35.0), (3.9, 33.0), (2.8, 32.6), (0, 32.4)]
    bands = [G('clay')] * 12 + [('cyl', 'band_zigzag', 0, .5), ('cyl', 'band_zigzag', .5, 1)] + \
            [G('clay')] * 7 + [('cyl', 'band_steps', 0, .5), ('cyl', 'band_steps', .5, 1)] + [G('slip')] * 5 + [G('dark', .15), G('dark', .05)] + [G('dark')] * 3
    m.loft(lathe_rings(prof, n, jitter=.008, seed=7.0, wobble=.08), 'clay', bands=bands, cap0=False, cap1=False, uturns=8)
    m.chip([(24, -.3, -.1), (25, -.8, -.3), (26, -1.0, -.25), (27, -1.0, 0), (28, -.8, .05), (29, -.3, .05)], 146, 26)
    ctrl = [(3.4, 32.6), (5.6, 33.4), (8.5, 33.3), (11.0, 31.7), (12.3, 28.9), (12.1, 25.9), (10.9, 23.6), (9.3, 22.2)]

    def rfn(t):
        flare = 1 + .32 * (sstep(.16, 0, t) + sstep(.84, 1, t))
        return 1.55 * flare, 1.0 * flare
    for s in (-1, 1):
        m.tube([(s * x, 0, z) for x, z in ctrl], rfn, n=14, p=2.6, tile='clay', per_seg=4, up=(0, 1, 0))
    return m, 'M', 'clay', (400, 500), .06


def bronze_figure():
    """青銅の立像 — S, bronze, 200-300G, ~30 cm. Votive figure in a bell robe, hands holding a concentric-eye
    disc with a turquoise boss, inlaid eyes, stepped crown with two stubby horns, on a stepped stone plinth."""
    m = Model('bronze_figure', S_SCALE)
    m.slab(0, 0, 0, 1.6, 9.0, 7.4, .35, 'stone', n=12, p=6, jitter=.02, seed=1)
    m.slab(0, 0, 1.6, 3.0, 7.2, 5.8, .3, 'stone', n=12, p=6, jitter=.015, seed=2)
    m.slab(0, 0, 2.9, 3.7, 5.4, 4.2, .25, 'bronze', -.05, n=12, p=6)
    P = 2.4; n = 16
    robe = [(3.6, 3.2, 2.5), (4.3, 3.32, 2.6), (4.9, 3.02, 2.36), (6.8, 2.72, 2.1), (8.8, 2.4, 1.84), (10.6, 2.12, 1.6), (11.5, 2.05, 1.52)]
    m.loft([ring(0, .05, z, rx, ry, n, P) for z, rx, ry in robe], 'bronze', -.08)
    m.loft([ring(0, .05, z, rx, ry, n, P) for z, rx, ry in [(10.9, 2.2, 1.66), (11.2, 2.34, 1.78), (12.1, 2.34, 1.78), (12.4, 2.12, 1.6)]], 'bronze')
    m.box((0, -1.98, 7.75), (1.9, .6, 6.5), 'bronze', -.3, bevel=.12, taper=(.72, 1), rot=(-.13, 0, 0))   # apron
    torso = [(11.6, 2.02, 1.48), (12.4, 2.05, 1.48), (13.2, 2.12, 1.5), (14.0, 2.25, 1.54), (14.8, 2.45, 1.58), (15.5, 2.7, 1.61),
             (16.2, 2.92, 1.62), (16.7, 2.94, 1.6), (17.1, 2.8, 1.53), (17.5, 2.4, 1.38), (17.8, 1.85, 1.2), (18.0, 1.3, 1.05)]
    m.loft([ring(0, 0, z, rx, ry, n, 2.4) for z, rx, ry in torso], 'bronze')
    m.loft([ring(0, -.05, z, r, r * .95, 12, 2.0) for z, r in [(17.7, 1.0), (18.9, .95)]], 'bronze')
    hc = -.15
    head = [(18.3, 1.0, 1.05), (18.65, 1.33, 1.37), (19.2, 1.57, 1.6), (19.8, 1.72, 1.72), (20.9, 1.78, 1.74),
            (21.4, 1.72, 1.68), (21.8, 1.62, 1.58), (22.15, 1.45, 1.4), (22.4, 1.25, 1.2)]
    hv, _ = m.loft([ring(0, hc, z, rx, ry, n, 2.2) for z, rx, ry in head], 'bronze', .04)
    fp = (hc - 1.3) * CM
    m.xform(hv, lambda c: Vector((c.x, fp + (c.y - fp) * .55 if c.y < fp else c.y, c.z)))
    bv, _ = m.box((0, -1.4, 20.85), (3.1, .7, .55), 'bronze', .1, bevel=.15)
    m.xform(bv, lambda c: c + Vector((0, .28 * (c.x / CM) ** 2 * CM, 0)))
    m.box((0, -1.48, 19.75), (.9, .95, 1.9), 'bronze', .12, bevel=.15, taper=(.5, .5))
    m.box((0, -1.0, 18.25), (1.75, 1.3, 1.5), 'bronze', bevel=.2, taper=(.62, .7), rot=(0, math.pi, 0))
    for s in (-1, 1):
        m.lens((s * .74, -1.3, 20.25), .62, .34, .5, n=12, p=1.6, M=Matrix.Rotation(s * -.25, 4, 'Z'))
    m.loft([ring(0, hc, z, rx, ry, n, 3.0) for z, rx, ry in [(21.3, 1.85, 1.82), (21.5, 1.97, 1.94), (22.3, 1.97, 1.94), (22.5, 1.6, 1.55)]], 'bronze', .05)
    m.loft([ring(0, hc, z, rx, ry, n, 3.0) for z, rx, ry in [(22.4, 1.45, 1.4), (23.15, 1.45, 1.4), (23.35, 1.0, .95)]], 'bronze', .05)
    for s in (-1, 1):
        m.tube([(s * x, hc + dy, z) for x, dy, z in [(1.0, 0, 22.5), (1.9, 0, 22.95), (2.55, -.1, 23.6), (2.7, -.25, 24.35), (2.4, -.35, 24.95)]],
               lambda t: (.78 - .25 * t, .78 - .25 * t), n=10, tile='bronze', bias=.05, per_seg=2, up=(0, 1, 0))
        m.tube([(s * 2.5, .05, 16.6), (s * 3.05, .05, 15.2), (s * 3.15, 0, 13.7), (s * 2.5, -1.1, 13.55), (s * 1.45, -1.95, 13.95)],
               lambda t: (.92 - .17 * t, .92 - .17 * t), n=10, tile='bronze', per_seg=2, up=(0, 1, 0))
        m.box((s * 1.2, -2.0, 14.05), (1.15, 1.3, 1.6), 'bronze', bevel=.4)
        m.box((s * .85, -2.35, 4.05), (1.15, 1.1, .85), 'bronze', bevel=.2)
    disc = [(0, .35), (1.3, .35), (1.6, .15), (1.6, -.22), (1.28, -.38), (.85, -.38), (.8, -.55), (.45, -.64), (0, -.68)]
    m.ydisc((0, -2.3, 14.3), disc, 14, bands=[None] * 5 + [G('turquoise'), G('turquoise', .05), G('turquoise', .05)], tile='bronze', bias=.08)
    return m, 'S', 'bronze', (200, 300), .03


def silver_mirror():
    """銀の手鏡 — S, silver, 300G, ~31 cm. Polished disc in a raised rim, held by two horned-bird wing
    brackets on a ringed handle that stands on a three-step foot; concentric rings on the back, stepped
    crest with a turquoise cabochon on top."""
    m = Model('silver_mirror', S_SCALE)
    m.slab(0, 0, 0, 1.4, 9.4, 7.0, .35, 'silver', -.05, n=16)
    m.slab(0, 0, 1.35, 2.6, 7.2, 5.4, .3, 'silver', n=16)
    m.slab(0, 0, 2.55, 3.6, 5.0, 3.8, .25, 'silver', n=16)
    handle = [(3.5, 1.5), (3.85, 1.42), (4.2, 1.35), (4.6, 1.5), (5.0, 1.62), (5.6, 1.62), (5.9, 1.4), (6.2, 1.2), (7.4, 1.15), (7.7, 1.35),
              (8.0, 1.55), (8.6, 1.55), (9.2, 1.3), (9.55, 1.55), (9.9, 1.8), (10.3, 1.8)]
    m.loft([ring(0, 0, z, r, r * .92, 16, 2.0) for z, r in handle], 'silver')
    m.slab(0, 0, 10.2, 11.4, 5.4, 2.7, .35, 'silver', n=16)
    cz = 17.8
    for s in (-1, 1):
        m.tube([(s * x, .1, z) for x, z in [(1.9, 10.9), (3.8, 11.15), (5.5, 12.2), (6.75, 13.9), (7.25, 15.9), (7.05, 17.7)]],
               lambda t: (1.0 - .25 * t, .85 - .2 * t), n=10, tile='silver', bias=.05, per_seg=2, up=(0, 1, 0))
    R = 5.4
    mirror = ('planar', 'mirror', lambda co: (.5 + co.x / (2 * R * CM), .5 + (co.z - cz * CM) / (2 * R * CM)))
    disc = [(0, -.95), (1.3, -.945), (2.5, -.93), (3.6, -.9), (4.6, -.86), (5.35, -.8), (5.45, -1.25), (5.95, -1.42), (6.4, -1.3), (6.6, -1.05),
            (6.65, .55), (6.3, .95), (5.0, .95), (4.7, 1.25), (4.1, 1.25), (3.8, .95), (2.3, .95), (1.9, 1.35), (.9, 1.55), (0, 1.6)]
    m.ydisc((0, 0, cz), disc, 32, bands=[mirror] * 5, tile='silver')
    m.slab(0, 0, 24.0, 24.95, 4.0, 2.4, .25, 'silver', .05, n=16)
    m.slab(0, 0, 24.85, 25.7, 2.4, 1.9, .25, 'silver', .05, n=16)
    m.lens((0, -1.1, 24.75), .62, .5, .55, n=14)
    return m, 'S', 'silver', (300, 300), .03


def gold_mask():
    """金の小仮面 — S, gold, 300G, ~24 cm. Leaning gold face on a stepped stone stand: brow ridge, inlaid
    almond eyes, straight nose, cheekbones, closed lips, concentric-eye diadem disc, ear spools, stepped crown."""
    m = Model('gold_mask', S_SCALE)
    m.slab(0, .2, 0, 1.6, 10.8, 8.2, .35, 'stone', n=12, p=6, jitter=.02, seed=4)
    m.slab(0, .2, 1.55, 3.0, 8.6, 6.6, .3, 'stone', n=12, p=6, jitter=.015, seed=5)
    lean_parts = []
    pv, _ = m.box((0, .9, 6.6), (3.0, 2.1, 9.2), 'stone', bevel=.3)
    lean_parts += pv
    Z0, H, W = 2.9, 13.6, 12.6
    hw_t = [(0, .36), (.08, .52), (.2, .72), (.35, .9), (.5, .99), (.62, 1.0), (.75, .98), (.88, .95), (1.0, .93)]
    hw = lambda t: float(np.interp(t, [a for a, _ in hw_t], [b for _, b in hw_t])) * W / 2

    def base_y(x, Zl):
        return -1.7 + .085 * x * x + .045 * max(0, Zl - 9.5) ** 2 + .06 * max(0, 2.6 - Zl) ** 2

    def relief(X, Z):
        ax = abs(X); g = lambda d: math.exp(-d * d); r = 0.0
        r += .42 * sstep(10.8, 11.1, Z) * (1 - sstep(12.6, 12.9, Z)) * (1 - sstep(5.3, 6.1, ax))
        r += .95 * sstep(8.5, 8.85, Z) * (1 - sstep(9.2, 10.5, Z)) * (1 - sstep(3.7, 5.3, ax))
        for sx in (-1, 1):
            d = ((X - sx * 2.5) / 1.7) ** 2 + ((Z - 7.85) / .9) ** 2
            r -= .85 * math.exp(-1.6 * d)
        tz = min(1.0, max(0.0, (9.0 - Z) / 3.4)); w = .62 + .85 * tz
        r += (.5 + 1.25 * tz) * max(0.0, 1 - (ax / w) ** 2) ** .8 * sstep(5.0, 5.65, Z) * (1 - sstep(9.0, 9.7, Z))
        r += .55 * g(math.hypot(ax - 1.15, (Z - 5.75) * 1.2) / .6)
        r += .75 * g(math.hypot(ax - 3.65, (Z - 6.3) * 1.25) / 1.6)
        r -= .3 * g(math.hypot(ax - 2.7, (Z - 4.4)) / 1.1)
        r += .7 * g(math.hypot(X / 1.6, (Z - 4.0) / .42)) + .62 * g(math.hypot(X / 1.4, (Z - 3.12) / .45))
        r -= .5 * g(math.hypot(X / 1.75, (Z - 3.55) / .2))
        r += .7 * g(math.hypot(X / 1.7, (Z - 1.35) / 1.15))
        return r

    NC, NR = 20, 28   # even: the back is a half-resolution grid
    cols = [math.copysign(abs(-1 + 2 * i / NC) ** 1.25, -1 + 2 * i / NC) for i in range(NC + 1)]
    F = []
    for j in range(NR + 1):
        t = j / NR; Zl = t * H; row = []
        for s in cols:
            x = s * hw(t); row.append(m.bm.verts.new(V3(x, base_y(x, Zl) - relief(x, Zl), Z0 + Zl)))
        F.append(row)
    for j in range(NR):
        for i in range(NC): m.face([F[j][i], F[j][i + 1], F[j + 1][i + 1], F[j + 1][i]], 'gold')
    B = []
    for j in range(0, NR + 1, 2):
        t = j / NR; Zl = t * H
        B.append([m.bm.verts.new(V3(cols[i] * hw(t) * .96, base_y(cols[i] * hw(t), Zl) + 1.6, Z0 + Zl)) for i in range(0, NC + 1, 2)])
    nb, nc = NR // 2, NC // 2
    for j in range(nb):
        for i in range(nc): m.face([B[j][i], B[j + 1][i], B[j + 1][i + 1], B[j][i + 1]], 'gold', -.15)
    ring_of = lambda Gd, nr, ncl: [Gd[0][i] for i in range(ncl + 1)] + [Gd[j][ncl] for j in range(1, nr + 1)] + [Gd[nr][i] for i in range(ncl - 1, -1, -1)] + [Gd[j][0] for j in range(nr - 1, 0, -1)]
    fl, bl = ring_of(F, NR, NC), ring_of(B, nb, nc)
    for k in range(len(bl)):
        f0, f1, f2 = fl[2 * k], fl[2 * k + 1], fl[(2 * k + 2) % len(fl)]; b0, b1 = bl[k], bl[(k + 1) % len(bl)]
        m.face([f1, f0, b0], 'gold', -.1); m.face([f1, b0, b1], 'gold', -.1); m.face([f2, f1, b1], 'gold', -.1)
    lean_parts += [v for r in F for v in r] + [v for r in B for v in r]
    yf = lambda x, Zl: base_y(x, Zl) - relief(x, Zl)
    for s in (-1, 1):
        vs, _ = m.lens((s * 2.5, yf(s * 2.5, 7.85) - .02, Z0 + 7.85), 1.3, .6, .5, n=14, p=1.55, M=Matrix.Rotation(s * -.25, 4, 'Z'))
        lean_parts += vs
    disc = [(0, .4), (1.35, .4), (1.48, .1), (1.48, -.15), (1.22, -.32), (.82, -.32), (.76, -.48), (.4, -.56), (0, -.6)]
    vs, _ = m.ydisc((0, yf(0, 11.85) + .05, Z0 + 11.85), disc, 16, bands=[None] * 5 + [G('turquoise'), G('turquoise', .05), G('turquoise', .05)], tile='gold', bias=.06)
    lean_parts += vs
    spool = [(0, .5), (1.5, .5), (1.65, .2), (1.65, -.22), (1.35, -.42), (.95, -.42), (.85, -.62), (.45, -.7), (0, -.72)]
    for s in (-1, 1):
        x = s * 6.15
        vs, _ = m.ydisc((x, base_y(x, 8.3) + .55, Z0 + 8.3), spool, 12, tile='gold', bias=.05, M=Matrix.Rotation(s * math.radians(58), 4, 'Z'))
        lean_parts += vs

    def tier(x0, x1, z0, z1, nseg, y0=-.45, y1=1.8, c=.22):
        sec = [(y0 + c, z0), (y1 - c, z0), (y1, z0 + c), (y1, z1 - c), (y1 - c, z1), (y0 + c, z1), (y0, z1 - c), (y0, z0 + c)]
        rings = []
        for k in range(nseg + 1):
            x = x0 + (x1 - x0) * k / nseg; yo = base_y(x, H)
            rings.append([V3(x, yo + y, z) for y, z in sec])
        return m.loft(rings, 'gold', .04)
    for x, z0, z1, ns in ((5.6, 16.25, 17.55, 8), (3.8, 17.45, 18.75, 6), (2.0, 18.65, 19.95, 4)):
        vs, _ = tier(-x, x, z0, z1, ns); lean_parts += vs
    pivot = Vector((0, -1.0 * CM, Z0 * CM)); Rm = Matrix.Rotation(math.radians(-9), 3, 'X')
    m.xform(set(lean_parts), lambda c: pivot + Rm @ (c - pivot))
    return m, 'S', 'gold', (300, 300), .03


# ---------------- new core models (drawn at final size) ----------------
def clay_offering_bowl():
    """供物の鉢 — S, clay, 100-200G, dia ~22 cm. Shallow bowl on a tall three-step foot; stepped-pyramid band
    outside, wave-and-teeth band inside, cream rim with a chip."""
    m = Model('clay_offering_bowl'); n = 32
    prof = [(0, 0), (5.2, 0), (5.5, .3), (5.5, 1.0), (5.3, 1.2), (4.3, 1.2), (4.3, 2.4), (4.1, 2.6), (3.2, 2.6), (3.05, 3.4), (3.15, 4.1),
            (3.6, 4.45), (4.6, 4.75), (5.8, 5.15), (7.0, 5.7), (8.2, 6.45), (9.2, 7.3), (10.0, 8.1), (10.6, 9.0), (10.95, 9.8), (11.0, 10.3),
            (10.85, 10.8), (10.3, 10.9), (9.85, 10.35), (9.4, 9.6), (8.6, 8.6), (7.6, 7.75), (6.4, 7.0), (4.8, 6.45), (2.5, 6.15), (0, 6.1)]
    bands = [G('clay')] * 14 + [('cyl', 'band_steps', 0, .5), ('cyl', 'band_steps', .5, 1)] + [G('clay')] * 2 + \
            [G('slip')] * 5 + [('cyl', 'band_zigzag', 1, .5), ('cyl', 'band_zigzag', .5, 0)] + [G('clay')] * 4
    m.loft(lathe_rings(prof, n, jitter=.008, seed=11.0, wobble=.04), 'clay', bands=bands, cap0=False, cap1=False, uturns=8)
    m.chip([(19, -.2, -.1), (20, -.6, -.2), (21, -.7, -.15), (22, -.65, 0), (23, -.3, .05)], -30, 16)
    m.chip([(3, .0, -.25), (4, -.15, -.2)], 120, 15)
    return m, 'S', 'clay', (100, 200), .035


def stone_beast():
    """石の小獣像 — S, stone, 100-200G, ~21 cm. A small standing idol of the horned bird, guardian of the kingdom:
    one columnar block from feet to head, wings closed tight. The wings are carved into the body surface itself
    (the ring outlines carry the relief): crisp vertical wing edges frame the chest, feather tiers step out toward
    the base, the two wings meet in a groove down the spine and swell into the folded tail behind. Small head
    bowed slightly, short heavy hooked beak, brow ridge over deep concentric-ring eyes, short thick horns of the
    same family as the bronze figure's. Concentric-eye boss on the chest, claws carved at the foot. Stepped plinth."""
    m = Model('stone_beast', 1.0, sharp_deg=36)
    sv, _ = m.slab(0, .2, 0, 1.6, 10.6, 10.2, .4, 'stone', -.05, n=12, p=6, jitter=.025, seed=21)
    m.slab(0, .2, 1.5, 2.7, 8.8, 8.4, .35, 'stone', -.02, n=12, p=6, jitter=.02, seed=24)
    for v in sv:   # a knocked-off plinth corner
        if v.co.x > 3.8 * CM and v.co.y < -3.9 * CM and v.co.z > .9 * CM:
            v.co.z -= .55 * CM; v.co.x -= .25 * CM; m.vbias[v] = .3

    PB = 2.8
    # (z, cy, rx, ry): a column that widens a little to the shoulders (a wedge), then narrows into the neck
    B = [(2.6, .25, 2.55, 2.6), (6.0, .1, 2.75, 2.8), (10.0, -.15, 3.0, 2.9), (13.0, -.1, 3.1, 2.85), (14.5, -.05, 2.6, 2.3),
         (15.2, 0, 2.0, 1.85), (15.8, 0, 1.7, 1.6)]
    bz = [b[0] for b in B]
    fz = lambda z, i: float(np.interp(z, bz, [b[i] for b in B]))
    WF = 45.0          # wing front edge: degrees in front of the side (frames the chest)
    STEPS = (7.8, 11.8, 13.6)

    def in_wing(t):    # t in degrees, 0 = +x, -90 = front, 90 = back
        t = (t + 180) % 360 - 180
        return (-WF <= t <= 90) or (t >= 90 or t <= -180 + WF)

    def relief(z, t):
        if z >= STEPS[2] or not in_wing(t): return 0.0
        tt = (t + 180) % 360 - 180
        r = .28 + (.28 if z < STEPS[1] else 0) + (.28 if z < STEPS[0] else 0)    # feather tiers step out toward the base
        if abs(abs(tt) - 90) < .1 and tt > 0: r -= .35                            # groove where the wings meet on the spine
        if min(abs(tt - 22), abs(tt - 158)) < .1: r -= .22                         # feather groove down each flank
        return r

    def surf(z, t, off=0.0):
        a = math.radians(t); c, s_ = math.cos(a), math.sin(a)
        cy, rx, ry = fz(z, 1), fz(z, 2), fz(z, 3)
        ry += .9 * max(0.0, s_) ** 3 * (1 - sstep(3.0, 7.5, z))                  # folded wing tips / tail swell behind
        x = math.copysign(abs(c) ** (2 / PB), c) * rx; y = math.copysign(abs(s_) ** (2 / PB), s_) * ry
        off += relief(z, t) + .04 * noise.noise(Vector((c * 2.5, s_ * 2.5, z * .5)))   # chisel unevenness
        d = Vector((x, y, 0)); d = d.normalized() if d.length > 1e-6 else Vector((1, 0, 0))
        return V3(x + d.x * off, cy + y + d.y * off, z)

    E = 1.5   # half-gap of the duplicated vertices that make a crisp vertical edge
    keys = [(-WF) % 360, (180 + WF) % 360]
    dist = lambda a_, k: abs((a_ - k + 180) % 360 - 180)
    uni = [i * 360 / 28 + 360 / 56 for i in range(28)]
    grooves = [22.0, 158.0]
    ang = [a_ for a_ in uni if all(dist(a_, k) > 4.0 for k in keys) and all(dist(a_, g) > 4.5 for g in grooves + [90.0])]
    ang += [(k + d) % 360 for k in keys for d in (-E, E)] + [g + d for g in grooves + [90.0] for d in (-3.0, 0.0, 3.0)]
    ang = sorted(ang)
    # paired rings straddle each tier step, so each step is a crisp horizontal edge
    zs_ = [2.6, 3.4, 4.3, 5.2, 6.1, 7.0, STEPS[0] - .06, STEPS[0] + .06, 8.7, 9.6, 10.5, 11.2, STEPS[1] - .06, STEPS[1] + .06,
           12.6, STEPS[2] - .06, STEPS[2] + .06, 14.2, 14.8, 15.3, 15.8]
    rings = [[surf(z, a_) for a_ in ang] for z in zs_]
    m.loft(rings, 'stone')

    # Chest boss (motif 2) and claws carved at the foot.
    boss = [(0, .5), (1.45, .5), (1.6, .2), (1.6, -.15), (1.3, -.32), (.9, -.32), (.82, -.5), (.4, -.6), (0, -.62)]
    m.ydisc((0, -2.95, 10.6), boss, 16, tile='stone', bias=.06)
    for sd in (-1, 1):
        for x0 in (.75, 1.55):
            m.tube([(sd * x0, -1.9, 3.35), (sd * x0 * 1.05, -2.75, 3.1), (sd * x0 * 1.08, -3.05, 2.8)], lambda t: (.45 - .15 * t, .4 - .12 * t),
                   n=6, tile='stone', bias=.04, per_seg=2)
    # Small head, bowed a little: brow ridge, deep concentric eyes, short heavy hooked beak, short thick horns.
    head = [(15.3, -.2, 1.75, 1.75), (16.0, -.35, 1.95, 2.05), (16.9, -.45, 1.95, 2.1), (17.7, -.4, 1.75, 1.85), (18.3, -.35, 1.3, 1.4), (18.7, -.3, .65, .7)]
    hv, _ = m.loft([ring(0, cy, z, rx, ry, 16, 2.3, jitter=.02, seed=25) for z, cy, rx, ry in head], 'stone', .03)
    m.xform(hv, lambda c: c - Vector((0, .45 * CM * math.exp(-((c.z / CM - 17.45) / .32) ** 2) * sstep(-1.6, -2.4, c.y / CM), 0)))
    eye = [(0, .3), (.55, .3), (.6, .05), (.48, -.1), (.3, -.1), (.24, -.22), (0, -.25)]
    for sd in (-1, 1):
        m.ydisc((sd * 1.05, -2.25, 16.95), eye, 10, tile='stone', bias=.02, M=Matrix.Rotation(sd * math.radians(38), 4, 'Z'))
        m.tube([(sd * 1.2, -.5, 18.2), (sd * 2.0, -.4, 18.6), (sd * 2.6, -.5, 19.5), (sd * 2.6, -.7, 20.4), (sd * 2.25, -.85, 21.0)],
               lambda t: (.72 - .3 * t,) * 2, n=8, tile='stone', bias=.05, per_seg=2)
    m.tube([(0, -2.4, 16.95), (0, -3.0, 16.7), (0, -3.28, 16.2), (0, -3.12, 15.72)], lambda t: (1.0 - .62 * t, .92 - .58 * t),
           n=10, p=2.2, tile='stone', bias=.05, per_seg=2, up=(1, 0, 0))
    return m, 'S', 'stone', (100, 200), .03


def stone_unguent_jar():
    """香油壺 — S, alabaster, 200G, ~25 cm. Slender polished stone jar: incised stepped band between carved grooves,
    stepped lid, concentric-eye knob."""
    m = Model('stone_unguent_jar'); n = 28
    prof = [(0, 0), (2.7, 0), (3.0, .3), (3.1, 1.0), (2.8, 1.4), (3.0, 1.8), (3.4, 2.4), (3.9, 3.4), (4.25, 4.35), (4.1, 4.5), (4.12, 4.8), (4.42, 4.95), (4.6, 6.0), (4.7, 7.5),
            (4.68, 9.0), (4.6, 10.5), (4.42, 11.9), (4.2, 13.2), (3.95, 14.1), (3.78, 14.25), (3.78, 14.55), (3.85, 14.65), (3.5, 15.3), (3.05, 16.0), (2.6, 16.6), (2.3, 17.0),
            (2.2, 17.4), (2.15, 18.6), (2.9, 18.9), (3.0, 19.5), (2.6, 19.7), (0, 19.7)]
    band = [None] * 15 + [('cyl', 'band_stone', 0, .5), ('cyl', 'band_stone', .5, 1)]
    m.loft(lathe_rings(prof, n, jitter=.006, seed=31.0), 'alabaster', bands=band, cap0=False, cap1=False, uturns=4)
    lid = [(0, 19.55), (3.25, 19.55), (3.4, 19.75), (3.4, 20.45), (3.2, 20.65), (2.65, 20.65), (2.65, 21.45), (2.45, 21.65), (1.95, 21.65),
           (1.95, 22.25), (1.75, 22.45), (0, 22.45)]
    m.loft(lathe_rings(lid, n), 'alabaster', .04, cap0=False, cap1=False)
    knob = [(0, .5), (1.3, .5), (1.42, .25), (1.42, -.25), (1.15, -.45), (.8, -.45), (.72, -.6), (.35, -.66), (0, -.68)]
    m.ydisc((0, 0, 23.55), knob, 18, tile='alabaster', bias=.05)
    return m, 'S', 'stone', (200, 200), .035


def bronze_amulet_disc():
    """護符盤 — S, bronze, 200G, dia ~17 cm. Thick (2.9 cm) concentric-eye disc with a turquoise pupil, standing
    in a stepped bronze stand, crowned by a pair of stubby horns."""
    m = Model('bronze_amulet_disc')
    m.slab(0, 0, 0, 1.4, 11.0, 6.6, .35, 'bronze', -.08)
    m.slab(0, 0, 1.35, 2.6, 8.6, 5.0, .3, 'bronze', -.04)
    m.slab(0, 0, 2.55, 4.8, 6.0, 4.2, .3, 'bronze')
    cz = 12.6
    prof = [(0, -1.75), (.6, -1.74), (1.1, -1.66), (1.6, -1.4), (1.75, -1.0), (2.3, -1.0), (2.6, -1.45), (3.6, -1.45), (3.9, -1.0),
            (5.0, -1.0), (5.3, -1.55), (7.6, -1.55), (8.2, -1.42), (8.5, -1.0), (8.5, 1.0), (8.2, 1.35), (5.0, 1.35), (4.7, 1.6),
            (4.0, 1.6), (3.7, 1.35), (1.5, 1.35), (0, 1.4)]
    m.ydisc((0, 0, cz), prof, 40, bands=[G('turquoise'), G('turquoise'), G('turquoise', .05), G('turquoise', -.1)], tile='bronze')
    m.box((0, 0, cz + 8.5 + .4), (2.8, 2.2, 1.6), 'bronze', .04, bevel=.35)
    for s in (-1, 1):
        m.tube([(s * .9, 0, cz + 8.8), (s * 2.0, 0, cz + 10.0), (s * 3.0, 0, cz + 10.5), (s * 3.7, 0, cz + 10.2)],
               lambda t: (.95 - .3 * t, .95 - .3 * t), n=10, tile='bronze', bias=.06, per_seg=3)
    return m, 'S', 'bronze', (200, 200), .03


def stone_stele():
    """石碑 — M, stone, 500-600G, ~46 cm. Stepped head, framed front with a concentric-eye disc, a horned-bird
    relief and a panel of the kingdom's (fictional) script; thick edge pilasters give left / right holds."""
    m = Model('stone_stele', 1.0, sharp_deg=35)
    m.slab(0, 0, 0, 4.0, 36.0, 15.0, .5, 'stone', -.05, n=12, p=6, jitter=.01, seed=41)
    m.slab(0, 0, 3.8, 42.0, 30.0, 9.5, .45, 'stone', n=12, p=7, jitter=.006, seed=42)
    m.slab(0, 0, 41.8, 43.6, 24.0, 9.0, .4, 'stone', .02, n=12, p=7)
    m.slab(0, 0, 43.4, 45.2, 16.0, 8.5, .4, 'stone', .04, n=12, p=7)
    m.slab(0, 0, 45.0, 46.4, 8.0, 8.0, .35, 'stone', .06, n=12, p=6)
    for s in (-1, 1):   # pilasters
        m.box((s * 13.6, -4.9, 22.9), (2.6, 1.2, 36.6), 'stone', .03, bevel=.35)
        m.box((s * 13.6, 4.9, 22.9), (2.6, 1.2, 36.6), 'stone', .0, bevel=.35)
    m.box((0, -4.9, 40.0), (24.6, 1.2, 2.2), 'stone', .03, bevel=.35)                 # lintel of the frame
    eye = [(0, .6), (4.4, .6), (4.6, .2), (4.6, -.3), (4.2, -.55), (3.2, -.55), (3.0, -.2), (2.4, -.2), (2.2, -.6), (1.4, -.6), (1.2, -.85),
           (.6, -.95), (0, -.98)]
    m.ydisc((0, -4.8, 34.0), eye, 32, tile='stone', bias=.05)
    half = [(0, 5.0), (.9, 5.0), (2.4, 7.6), (1.5, 4.6), (1.5, 3.6), (2.2, 3.2), (6.5, 3.2), (6.5, 2.0), (5.4, 2.0), (5.4, .8), (4.2, .8),
            (4.2, -.4), (2.0, -.4), (1.6, -2.4), (2.6, -4.6), (0, -3.6)]
    outline = [(x * 1.05, 23.3 + z * 1.05) for x, z in half] + [(-x * 1.05, 23.3 + z * 1.05) for x, z in reversed(half[1:-1])]
    m.prism(outline[::-1], -4.5, -5.35, 'stone', .06)
    x0, x1, z0, z1 = -11.6, 11.6, 6.6, 15.3
    vs, faces = m.box(((x0 + x1) / 2, -4.95, (z0 + z1) / 2), (x1 - x0, .9, z1 - z0), 'stone', .0, bevel=0)
    for f in faces:
        f.normal_update()
        if f.normal.y < -.9:
            f[m.lm] = 1; f[m.lt] = TILE['glyphs']
            for lp in f.loops: lp[m.uv].uv = atlas_uv(TILE['glyphs'], (lp.vert.co.x / CM - x0) / (x1 - x0), (lp.vert.co.z / CM - z0) / (z1 - z0))
    return m, 'M', 'stone', (500, 600), .05


def bronze_tripod_cauldron():
    """三脚の大釜 — M, bronze, 600G, ~39 cm. Round cauldron with cast ribs, rolled rim, three heavy legs and
    two thick ring handles standing on the rim (left / right beam targets); concentric-eye bosses."""
    m = Model('bronze_tripod_cauldron'); n = 28
    prof = [(0, 12.6), (4.0, 12.9), (8.0, 14.0), (10.0, 15.0), (11.5, 16.2), (12.8, 17.7), (13.8, 19.5),
            (14.35, 21.3), (14.6, 23.0), (14.9, 23.4), (14.9, 24.0), (14.6, 24.4), (14.5, 25.6), (14.8, 26.0), (14.8, 26.5), (14.4, 26.9),
            (13.9, 28.2), (13.4, 29.4), (13.3, 30.2), (14.0, 30.6), (14.6, 31.0), (14.85, 31.6), (14.8, 32.1), (14.2, 32.6), (13.4, 32.55),
            (12.9, 32.0), (12.7, 30.0), (11.6, 23.0), (0, 21.0)]
    bands = [G('bronze')] * 24 + [G('dark', .15), G('dark', .1)] + [G('dark')] * 3
    m.loft(lathe_rings(prof, n), 'bronze', bands=bands, cap0=False, cap1=False)
    for a in (-90, 30, 150):   # legs
        d = Vector((math.cos(math.radians(a)), math.sin(math.radians(a)), 0))
        P = lambda r, z: (d.x * r, d.y * r, z)
        m.tube([P(8.0, 16.0), P(10.6, 12.0), P(12.0, 7.0), P(12.4, 2.4), P(12.5, .9)],
               lambda t: (2.2 - .9 * sstep(0, .7, t) + .4 * sstep(.75, 1, t),) * 2, n=12, tile='bronze', per_seg=2, up=(0, 0, 1))
        cx, cy, _ = P(12.6, 0)
        m.loft([ring(cx, cy, z, r, r, 12) for z, r in ((0, 1.9), (.25, 2.25), (1.0, 2.25), (1.5, 1.7))], 'bronze', -.05)
    for s in (-1, 1):
        m.torus((s * 14.4, 0, 33.7), 4.0, 1.25, nseg=22, nside=10, tile='bronze', bias=.05, rs=1.45)
        m.box((s * 13.7, 0, 31.6), (3.2, 3.6, 2.6), 'bronze', bevel=.6)
    boss = [(0, .6), (2.1, .6), (2.3, .3), (2.3, -.2), (2.0, -.4), (1.4, -.4), (1.3, -.6), (.65, -.75), (0, -.8)]
    for a in (-35, -145):
        ar = math.radians(a)
        m.ydisc((14.4 * math.cos(ar), 14.4 * math.sin(ar), 21.0), boss, 12, tile='bronze', bias=.06, M=Matrix.Rotation(ar + math.pi / 2, 4, 'Z'))
    return m, 'M', 'bronze', (600, 600), .06


def gilded_sun_disc():
    """日輪盤 — M, gilded (gold on a dark ground), 700-800G, dia ~45 cm. Large concentric-eye disc with a
    turquoise centre and a crown of ray teeth, held by a gold cradle on a stepped pedestal."""
    m = Model('gilded_sun_disc', 1.0, sharp_deg=36)
    m.slab(0, 0, 0, 3.0, 34.0, 16.0, .45, 'stone', -.05, n=12, p=6, jitter=.01, seed=51)
    m.slab(0, 0, 2.9, 5.9, 26.0, 12.5, .4, 'stone', n=12, p=6)
    m.slab(0, 0, 5.8, 8.3, 18.0, 9.5, .35, 'gold', -.05, n=12, p=6)
    m.slab(0, 0, 8.2, 13.6, 13.0, 7.0, .4, 'gold', n=16, p=4, taper=1.0)
    cz = 8.3 + 3.0 + 17.3
    D = G('lacquer'); Au = G('gold'); Tq = G('turquoise')
    prof = [(0, -2.6), (.8, -2.58), (1.5, -2.46), (2.2, -2.1), (2.5, -1.6), (3.0, -1.6), (3.2, -2.05), (4.2, -2.05), (4.4, -1.5), (6.4, -1.5),
            (6.6, -2.05), (8.2, -2.05), (8.4, -1.5), (10.8, -1.5), (11.0, -2.1), (13.0, -2.1), (13.2, -1.6), (15.0, -1.6), (15.3, -2.2),
            (16.6, -2.2), (17.2, -1.8), (17.3, 1.6), (16.8, 2.0), (3.0, 2.0), (0, 2.1)]
    bands = [Tq, Tq, Tq, Au, Au, Au, Au, D, Au, Au, Au, D, Au, Au, Au, D, Au, Au, Au, Au, Au, G('gold', -.1), G('gold', -.12), G('gold', -.12)]
    m.ydisc((0, 0, cz), prof, 48, bands=bands, tile='gold')
    for k in range(16):   # ray teeth, alternating long / short, none behind the cradle
        a = math.radians(90 + k * 22.5)
        if abs(math.degrees(a) % 360 - 270) < 30: continue
        dd = Vector((math.cos(a), 0, math.sin(a))); tt = Vector((-math.sin(a), 0, math.cos(a)))
        r1 = 22.6 if k % 2 == 0 else 20.6; w0 = 4.4 if k % 2 == 0 else 3.6
        pts = [dd * 16.4 - tt * w0 / 2, dd * r1 - tt * .55, dd * r1 + tt * .55, dd * 16.4 + tt * w0 / 2]
        m.prism([(p.x, cz + p.z) for p in pts], 1.3, -1.25, 'gold', .04, inset=.3)
    return m, 'M', 'gold', (700, 800), .06


MODELS = [clay_pot, bronze_figure, silver_mirror, gold_mask, clay_offering_bowl, stone_beast, stone_unguent_jar, bronze_amulet_disc,
          clay_amphora, stone_stele, bronze_tripod_cauldron, gilded_sun_disc]
BUDGET = {'S': 3000, 'M': 4000}


# ----------------------------------------------------------------------------------------------
# Assemble, aim zones, export, stats
# ----------------------------------------------------------------------------------------------
def build(fn, mat):
    m, cls, tier, band, ao = fn()
    body = m.finish(mat, ao)
    root = bpy.data.objects.new(m.name + '_Root', None); root.empty_display_size = .05
    for key, val in (('treasure_class', cls), ('material_tier', tier), ('value_band_g', list(band)), ('front', '-Y'), ('origin', 'base centre, z=0')):
        root[key] = val
    body.parent = root
    pts = [Vector(c) for c in body.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts))); hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    zones = []
    if cls == 'S':
        zones.append(('Aim_Body', (lo + hi) / 2, hi - lo))
    else:   # M: left and right halves of the body (both must be hit for a two-hand grab)
        mid = (lo.x + hi.x) / 2
        zones.append(('Aim_Left', Vector(((lo.x + mid) / 2, (lo.y + hi.y) / 2, (lo.z + hi.z) / 2)), Vector((mid - lo.x, hi.y - lo.y, hi.z - lo.z))))
        zones.append(('Aim_Right', Vector(((mid + hi.x) / 2, (lo.y + hi.y) / 2, (lo.z + hi.z) / 2)), Vector((hi.x - mid, hi.y - lo.y, hi.z - lo.z))))
    aims = []
    for name, c, size in zones:
        e = bpy.data.objects.new(m.name + '_' + name, None); e.empty_display_type = 'CUBE'; e.empty_display_size = .5
        e.scale = size; e.location = c; e.parent = root
        e['aim_center_xyz'] = [round(x, 5) for x in c]; e['aim_size_xyz'] = [round(x, 5) for x in size]; aims.append(e)
    sc = bpy.context.scene
    for o in [root, body, *aims]: sc.collection.objects.link(o)
    for o in sc.view_layers[0].objects: o.select_set(False)
    return m.name, root, body, aims, sc, cls, tier, band


def run(only=()):
    mat = build_atlas(); stats_path = HERE / 'stats.json'
    stats = {s['model']: s for s in json.loads(stats_path.read_text(encoding='utf-8'))} if stats_path.exists() else {}
    for fn in MODELS:
        if only and fn.__name__ not in only: continue
        name, root, body, aims, sc, cls, tier, band = build(fn, mat)
        tris = tri_count(body)
        pts = [body.matrix_world @ Vector(c) for c in body.bound_box]
        dims = [round(max(p[i] for p in pts) - min(p[i] for p in pts), 5) for i in range(3)]
        if tris > BUDGET[cls]: print('WARNING over budget', name, tris)
        assert tris <= 5000, (name, tris)
        assert min(p.z for p in pts) > -1e-6, name
        for o in [root, body, *aims]: o.select_set(True)
        bpy.context.view_layer.objects.active = root
        # mesh_smooth_type='OFF' writes the split normals themselves (smooth surfaces + sharp edges); Unity imports them as-is.
        bpy.ops.export_scene.fbx(filepath=str(HERE / (name + '.fbx')), use_selection=True, object_types={'MESH', 'EMPTY'}, axis_forward='-Z', axis_up='Y',
                                 apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS', use_custom_props=True, path_mode='COPY', embed_textures=True,
                                 bake_anim=False, add_leaf_bones=False, mesh_smooth_type='OFF')
        bpy.data.libraries.write(str(HERE / (name + '.blend')), {root, body, *aims}, fake_user=True, compress=True)
        stats[name] = dict(model=name, triangles=tris, dimensions_m=dims, meshes=1, grips=0, treasure_class=cls, material_tier=tier, value_band_g=list(band),
                           aim_zones=len(aims))
        for o in [root, body, *aims]: sc.collection.objects.unlink(o)
        print('built', name, tris, dims)
    stats_path.write_text(json.dumps(list(stats.values()), indent=2, ensure_ascii=False), encoding='utf-8')


if __name__ == '__main__':
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    run(argv)
