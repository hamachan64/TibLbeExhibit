"""Enemy models for Treasure Haul: the horned-crown king (guardian statue) and the grave keeper (ruin phantom).

Same fictional civilisation as the treasures and the stage ("the stepped kingdom"). Four motifs, repeated:
  1. steps   2. concentric "eye" discs   3. wave-and-teeth band   4. horned bird (horns)
The king built the ruin; the grave keeper served him and still walks the tomb corridors with a hanging lamp.

Headless, nothing but files is written (all outputs are overwritten):
  blender -b --factory-startup --python build_enemies.py [-- statue|phantom ...]
Writes
  Statue/KingStatue.blend / .fbx, Statue/textures/KingStatue_{BaseColor,Roughness,MetallicSmoothness}.png
  Phantom/GraveKeeper.blend / .fbx, Phantom/textures/GraveKeeper_Lantern_{BaseColor,MetallicSmoothness}.png
  stats.json (entries of the models that were built are replaced, the others kept)

Conventions (both models): metres, front = Blender -Y, up = +Z, origin = floor under the model.
The FBX is written Z-up (axis_up 'Z', forward 'Y', no bake_space_transform) with the model turned 180 degrees about Z first
(export() / turn_z180). In Unity turn on Bake Axis Conversion: every node is then rotation 0, Y-up, front +Z, and the mesh vertices are
Y-up too; local position = (x', z', y') of the turned Blender values (= (-x, z, -y) of the .blend = to_unity() = stats.json *_unity). Pivots are EMPTY nodes; meshes under a pivot
have their origin on it. The .blend itself is not turned (front = -Y).

Statue (15,000 tris max): Body (plinth, robe, arms, staff) + HeadPivot(EMPTY) > Head (head, crown, horns, braid)
  > Eye_L / Eye_R. Body and Head share one material (TH_Statue) with a 2048 atlas that is *baked*:
  smart-UV unwrap, Cycles bakes position / normal / ambient occlusion / pointiness / part id into float images,
  then numpy paints the stone (mottling, grain, rain streaks, soil from the ground, moss on the upward faces,
  cracks, chips, faded paint on the relief bands). Eyes: TH_StatueEye, dark stone (Unity makes them glow).
Phantom (4,000 tris max): Body (hood, robe, sleeves, pole: one translucent mesh with vertex colour
  RGB = soft fold shade, A = opacity multiplier), Void (dark face), Eye_L / Eye_R, LanternPivot(EMPTY)
  > LanternFrame (old bronze, gradient texture) / LanternGlow (same material as the eyes).
"""
import bpy, bmesh, json, math, sys, subprocess
import numpy as np
from pathlib import Path
from mathutils import Vector, Matrix, Euler, noise
from mathutils.bvhtree import BVHTree

HERE = Path(__file__).resolve().parent
CM = 0.01
ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []


# ----------------------------------------------------------------------------------------------
# Small maths
# ----------------------------------------------------------------------------------------------
def V3(x, y, z): return Vector((x * CM, y * CM, z * CM))


def sstep(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a))); return t * t * (3 - 2 * t)


def sup2d(n, p=2.0, rot=None):
    """Unit superellipse, n points, counter-clockwise (seen from +z). p=2 circle, larger p = squarer."""
    rot = math.pi / n if rot is None else rot
    out = []
    for k in range(n):
        a = rot + 2 * math.pi * k / n; c, s = math.cos(a), math.sin(a)
        out.append((math.copysign(abs(c) ** (2 / p), c), math.copysign(abs(s) ** (2 / p), s)))
    return out


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


def basis(axis, ref=(0, 0, 1)):
    a = Vector(axis).normalized(); r = Vector(ref)
    if abs(a.dot(r)) > .95: r = Vector((1, 0, 0)) if abs(a.x) < .9 else Vector((0, 1, 0))
    u = (r - a * r.dot(a)).normalized(); return a, u, a.cross(u)


# ----------------------------------------------------------------------------------------------
# Mesh builder (inputs in centimetres; x across, y depth (front = -y), z up)
# ----------------------------------------------------------------------------------------------
class Builder:
    def __init__(self, name, sharp_deg=40.0):
        self.name = name; self.sharp = math.radians(sharp_deg)
        self.bm = bmesh.new()
        self.lp = self.bm.faces.layers.int.new('part')
        self.lchip = self.bm.verts.layers.float.new('chip')
        self.lfade = self.bm.verts.layers.float.new('fade')    # phantom: 1 - opacity multiplier
        self.part = 0
        self._tree = None

    # -- primitives ------------------------------------------------------------------------------
    def face(self, verts):
        try: f = self.bm.faces.new(verts)
        except ValueError: return None
        f[self.lp] = self.part; return f

    def loft(self, rings, cap0=True, cap1=True, closed=False):
        vr = [[self.bm.verts.new(p) for p in r] for r in rings]
        faces = []
        seq = vr + [vr[0]] if closed else vr
        for i in range(len(seq) - 1):
            a, b = seq[i], seq[i + 1]; n = max(len(a), len(b))
            for k in range(n):
                k2 = (k + 1) % n
                if len(a) == 1: q = [a[0], b[k2], b[k]]
                elif len(b) == 1: q = [a[k], a[k2], b[0]]
                else: q = [a[k], a[k2], b[k2], b[k]]
                f = self.face(q)
                if f: faces.append(f)
        if not closed:
            if cap0 and len(vr[0]) > 2:
                f = self.face(list(reversed(vr[0])))
                if f: faces.append(f)
            if cap1 and len(vr[-1]) > 2:
                f = self.face(vr[-1])
                if f: faces.append(f)
        self._tree = None
        return vr, faces

    @staticmethod
    def ring(cx, cy, z, rx, ry, n, p=2.0, rot=None, fn=None, jitter=0.0, seed=0.0):
        pts = []
        for x, y in sup2d(n, p, rot):
            m = fn(math.atan2(y, x), z) if fn else 1.0
            if jitter: m *= 1 + jitter * noise.noise(Vector((x * 1.3 + seed, y * 1.3 - seed, z * .05 + seed)))
            pts.append(V3(cx + x * rx * m, cy + y * ry * m, z))
        return pts

    def slab(self, cx, cy, z0, z1, w, d, c=1.5, n=24, p=8.0, jitter=0.0, seed=0.0, taper=0.0):
        hw, hd = w / 2, d / 2
        R = lambda z, a, b: self.ring(cx, cy, z, a, b, n, p, None, None, jitter, seed)
        return self.loft([R(z0, hw - c, hd - c), R(z0 + c, hw, hd), R(z1 - c, hw - taper, hd - taper), R(z1, hw - c - taper, hd - c - taper)])

    def box(self, c, size, bevel=.5, taper=(1, 1), rot=(0, 0, 0), seg=1):
        before = set(self.bm.faces)
        vs = bmesh.ops.create_cube(self.bm, size=1.0)['verts']
        for v in vs:
            top = v.co.z > 0
            v.co.x *= size[0] * CM * (taper[0] if top else 1); v.co.y *= size[1] * CM * (taper[1] if top else 1); v.co.z *= size[2] * CM
        bmesh.ops.transform(self.bm, matrix=Matrix.Translation(Vector(c) * CM) @ Euler(rot, 'XYZ').to_matrix().to_4x4(), verts=vs)
        if bevel:
            bmesh.ops.bevel(self.bm, geom=list({e for v in vs for e in v.link_edges}), offset=bevel * CM, segments=seg, profile=.5, affect='EDGES')
        new = [f for f in self.bm.faces if f not in before]
        for f in new: f[self.lp] = self.part
        self._tree = None
        return list({v for f in new for v in f.verts}), new

    def tube(self, ctrl, rfn, n=10, p=2.0, per_seg=3, up=(0, 1, 0), cap=True, cap0=None, cap1=None):
        """Sweep a superellipse along a Catmull-Rom path (cm). rfn(t) -> (radius along 'side', radius across)."""
        pts = catmull([Vector(c) * CM for c in ctrl], per_seg); N = len(pts)
        rings, side = [], None
        for i, pt in enumerate(pts):
            t = (pts[min(i + 1, N - 1)] - pts[max(i - 1, 0)]).normalized()
            u = Vector(up) if side is None else side
            side = (u - t * u.dot(t)).normalized(); b = t.cross(side)
            ra, rb = rfn(i / (N - 1))
            rings.append([pt + side * (x * ra * CM) + b * (y * rb * CM) for x, y in sup2d(n, p)])
        return self.loft(rings, cap0=cap if cap0 is None else cap0, cap1=cap if cap1 is None else cap1)

    def lathe(self, c, axis, profile, n, p=2.0, ref=(0, 0, 1), sx=1.0, sy=1.0, rot=None, cap0=False, cap1=False):
        """Lathe around an arbitrary axis through c (cm). profile [(r, h)] along the axis."""
        a, u, w = basis(axis, ref); C = Vector(c) * CM
        rings = []
        for r, h in profile:
            cen = C + a * (h * CM)
            if r < 1e-6: rings.append([cen])
            else: rings.append([cen + u * (x * r * sx * CM) + w * (y * r * sy * CM) for x, y in sup2d(n, p, rot)])
        return self.loft(rings, cap0=cap0, cap1=cap1)

    def lens(self, c, rx, rz, depth, n=12, p=2.0, M=None, front=-1):
        """Domed cabochon facing -y (front=-1) or +y (front=1), half sunk into what is behind it."""
        sec = [(.0, 1.0), (-.5, 1.0), (-.85, .78), (-1.0, .42)]
        T = Matrix.Translation(Vector(c) * CM) @ (M or Matrix.Identity(4))
        rings = [[T @ V3(x * rx * s, -front * d * depth / 2, z * rz * s) for x, z in sup2d(n, p, 0.0)] for d, s in sec]
        return self.loft(rings)

    def ydisc(self, c, profile, n, M=None, sz=1.0):
        """Lathe around the Y axis (discs facing the front). profile [(r, y)] in cm, back centre first."""
        T = Matrix.Translation(Vector(c) * CM) @ (M or Matrix.Identity(4))
        rings = []
        for r, y in profile:
            if r < 1e-6: rings.append([T @ V3(0, y, 0)])
            else: rings.append([T @ V3(x * r, y, s * r * sz) for x, s in sup2d(n, 2.0, math.pi / 2)])
        return self.loft(rings, cap0=False, cap1=False)

    def prism(self, outline, yb, yf, M=None):
        """Extrude a polygon drawn in the x-z plane (cm). yb, yf: numbers or fn(x, z) -> y (cm)."""
        T = M or Matrix.Identity(4)
        f = lambda y, x, z: y(x, z) if callable(y) else y
        rings = [[T @ V3(x, f(yb, x, z), z) for x, z in outline], [T @ V3(x, f(yf, x, z), z) for x, z in outline]]
        return self.loft(rings)

    # -- surface queries ----------------------------------------------------------------------------
    def tree(self):
        if self._tree is None:
            self.bm.normal_update(); self._tree = BVHTree.FromBMesh(self.bm)
        return self._tree

    def surf(self, x, z, side=-1, default=0.0):
        """y (cm) of the first surface hit from the front (side=-1) or the back (side=1) at (x, z)."""
        hit = self.tree().ray_cast(Vector((x * CM, side * 3.0, z * CM)), Vector((0, -side, 0)), 6.0)[0]
        return hit.y / CM if hit is not None else default

    def chip(self, verts, centre, radius, disp):
        """Knock a dent: verts within radius (cm) of centre move by disp (cm) with a smooth falloff."""
        C = Vector(centre) * CM; D = Vector(disp) * CM
        for v in verts:
            d = (v.co - C).length / (radius * CM)
            if d >= 1: continue
            s = sstep(1, 0, d); v.co += D * s; v[self.lchip] = max(v[self.lchip], s)
        self._tree = None

    # -- finish -----------------------------------------------------------------------------------------
    def finish(self, mat, recalc=True, origin=Vector()):
        bm = self.bm
        if recalc: bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
        for v in bm.verts: v.co -= origin
        bmesh.ops.triangulate(bm, faces=list(bm.faces), quad_method='BEAUTY', ngon_method='BEAUTY')
        bm.normal_update()
        for f in bm.faces: f.smooth = True
        for e in bm.edges: e.smooth = len(e.link_faces) == 2 and e.calc_face_angle(0.0) <= self.sharp
        mesh = bpy.data.meshes.new(self.name); bm.to_mesh(mesh); bm.free()
        mesh.materials.append(mat)
        obj = bpy.data.objects.new(self.name, mesh)
        sc = bpy.context.scene; sc.collection.objects.link(obj)
        mod = obj.modifiers.new('wn', 'WEIGHTED_NORMAL'); mod.mode = 'FACE_AREA'; mod.weight = 50; mod.keep_sharp = True
        dg = bpy.context.evaluated_depsgraph_get()
        baked = bpy.data.meshes.new_from_object(obj.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)
        obj.modifiers.clear(); old = obj.data; obj.data = baked; baked.name = self.name
        bpy.data.meshes.remove(old)
        if not baked.materials: baked.materials.append(mat)
        return obj


def tri_count(obj):
    obj.data.calc_loop_triangles(); return len(obj.data.loop_triangles)


def to_unity(v): return [round(-v[0], 4), round(v[2], 4), round(-v[1], 4)]


def size_unity(s): return [round(abs(s[0]), 4), round(abs(s[2]), 4), round(abs(s[1]), 4)]


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def shift_origin(obj, world_pivot, parent_world):
    """Move a mesh object's origin to world_pivot (its mesh stays put); location relative to the parent."""
    obj.data.transform(Matrix.Translation(-world_pivot))
    obj.location = world_pivot - parent_world


# ----------------------------------------------------------------------------------------------
# Numpy noise (3-D value noise on a hashed lattice)
# ----------------------------------------------------------------------------------------------
_TAB = np.random.default_rng(4242).random(1 << 16).astype(np.float32)


def vnoise3(p):
    i = np.floor(p).astype(np.int64); f = (p - i).astype(np.float32); f = f * f * (3 - 2 * f)

    def h(dx, dy, dz):
        n = (i[:, 0] + dx) * 73856093 ^ (i[:, 1] + dy) * 19349663 ^ (i[:, 2] + dz) * 83492791
        n = (n ^ (n >> 13)) * 1274126177
        return _TAB[(n ^ (n >> 16)) & 0xFFFF]
    x, y, z = f[:, 0], f[:, 1], f[:, 2]
    c00 = h(0, 0, 0) * (1 - x) + h(1, 0, 0) * x; c10 = h(0, 1, 0) * (1 - x) + h(1, 1, 0) * x
    c01 = h(0, 0, 1) * (1 - x) + h(1, 0, 1) * x; c11 = h(0, 1, 1) * (1 - x) + h(1, 1, 1) * x
    return (c00 * (1 - y) + c10 * y) * (1 - z) + (c01 * (1 - y) + c11 * y) * z


def fbm3(p, octaves=4, seed=0.0):
    out = np.zeros(len(p), np.float32); amp = 1.0; tot = 0.0; q = p + seed
    for o in range(octaves):
        out += amp * vnoise3(q * (2 ** o) + o * 17.31); tot += amp; amp *= .5
    return out / tot


def np_sstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t)


def save_png(path, rgba, name, non_color=False):
    H, W = rgba.shape[:2]
    im = bpy.data.images.new(name, width=W, height=H, alpha=True)
    if non_color: im.colorspace_settings.name = 'Non-Color'
    im.pixels.foreach_set(np.ascontiguousarray(rgba, np.float32).ravel())
    im.filepath_raw = str(path); im.file_format = 'PNG'
    path.parent.mkdir(parents=True, exist_ok=True); im.save()
    return im


def setup_cycles(samples):
    sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.samples = samples
    sc.cycles.device = 'CPU'
    try:
        prefs = bpy.context.preferences.addons['cycles'].preferences
        for kind in ('OPTIX', 'CUDA'):
            try:
                prefs.compute_device_type = kind; prefs.get_devices()
                devs = [d for d in prefs.devices if d.type == kind]
                if devs:
                    for d in prefs.devices: d.use = d.type == kind
                    sc.cycles.device = 'GPU'; break
            except TypeError: continue
    except Exception as e: print('cycles GPU unavailable:', e)


# ==============================================================================================
# 1. The horned-crown king (guardian statue)
# ==============================================================================================
P_STONE, P_PED, P_SKIN, P_BAND, P_CROWN, P_STAFF, P_HAIR, P_PLATE = range(8)
NECK = Vector((0, -1.0, 259.0))      # head pivot (cm): centre of the neck where it leaves the collar
STATUE_DIR = HERE / 'Statue'

ROBE = [  # z, rx, ry, cy
    (58.5, 46, 38, 1.5), (60.5, 48.5, 40, 1.5), (64, 48.5, 40, 1.5), (66, 47.2, 38.8, 1.5), (82, 45.5, 37, 1.2), (102, 43, 34.5, 1),
    (122, 40.5, 31.8, .8), (142, 38, 29, .6), (160, 35.5, 26.5, .4), (172, 34, 25, .2), (186, 33.5, 24.5, 0), (200, 35, 25.5, -.5),
    (214, 37, 26.5, -.8), (228, 39, 26.5, -.5), (238, 40, 25, 0), (246, 37, 22, .5), (252, 28, 18, .5), (256, 18, 15, .5), (258, 12, 11, .5)]


def robe_fold(a, z):
    amp = .05 * sstep(168, 70, z) + .012 * sstep(100, 62, z)
    c, s = math.cos(a), math.sin(a)
    w = sstep(.28, .5, abs(c)) if s < 0 else 1.0        # flat under the apron
    f = .62 * math.sin(10 * a + .4 + z * .004) + .38 * math.sin(17 * a + 1.9 + z * .011)
    f = math.copysign(abs(f) ** .7, f)                   # crisper pleat ridges
    return 1 + amp * w * f


def step_outline(w, h, n=3, down=False):
    """Stepped pyramid (motif 1) in x-z, base centred on x=0 from z=0 to h, n steps; down=True hangs from z=h."""
    xs = [w / 2 * (1 - .8 * k / n) for k in range(n)]; zs = [h * k / n for k in range(n + 1)]
    pts = [(-xs[0], 0.0), (xs[0], 0.0)]
    for k in range(n):
        pts.append((xs[k], zs[k + 1]))
        if k < n - 1: pts.append((xs[k + 1], zs[k + 1]))
    pts += [(-x, z) for x, z in reversed(pts[2:])]
    if down: pts = [(x, h - z) for x, z in pts][::-1]
    return pts


def statue_body():
    b = Builder('KingStatue_Body', sharp_deg=38)
    # --- stepped plinth -----------------------------------------------------------------------------------
    b.part = P_PED
    s1, _ = b.slab(0, 0, 0, 20, 168, 150, c=2.2, n=20, p=9, jitter=.010, seed=1.3)
    s2, _ = b.slab(0, 0, 20, 40, 140, 124, c=2.0, n=20, p=9, jitter=.008, seed=2.1)
    s3, _ = b.slab(0, 0, 40, 58, 112, 100, c=1.8, n=20, p=9, jitter=.006, seed=3.7)
    flat = lambda rs: [v for r in rs for v in r]
    s1, s2, s3 = flat(s1), flat(s2), flat(s3)
    b.chip(s1, (80, -72, 20), 14, (-3.5, 3, -7))       # broken corners
    b.chip(s1, (-82, 70, 18), 11, (3, -3, -6))
    b.chip(s2, (-68, -60, 40), 10, (2.5, 2.5, -6))
    b.chip(s3, (54, 47, 58), 9, (-2, -2, -5))
    b.chip(s1, (12, -75, 20), 8, (0, 2.5, -3))
    b.chip(s2, (60, 60, 40), 9, (-2, -2, -4))
    # relief bands on the front faces: stepped pyramids (rising and hanging) on the middle step,
    # wave-and-teeth on the top step
    yf2 = b.surf(0, 30); yf3 = b.surf(0, 49)
    b.part = P_BAND
    for k in range(7):
        x = -51 + k * 17
        poly = [(px + x, pz + 23) for px, pz in step_outline(14.5, 13.5, 3, down=bool(k % 2))]
        b.prism(poly, yf2 + 1.0, yf2 - 1.5)
    for k in range(11):   # teeth
        x = -45 + k * 9
        b.prism([(x - 4.0, 43), (x + 4.0, 43), (x, 52.5)], yf3 + 1.0, yf3 - 1.3)
    b.prism([(-50, 53.5), (50, 53.5), (50, 55.3), (-50, 55.3)], yf3 + 1.0, yf3 - 1.0)
    # --- feet --------------------------------------------------------------------------------------------
    b.part = P_SKIN
    for s in (-1, 1):
        b.box((s * 13.5, -41, 61.8), (13.5, 17, 7.6), bevel=2.2, taper=(.92, .9))
        for t in range(3):   # toes
            b.box((s * 13.5 + (t - 1) * 4.0 * s, -49.0, 60.0), (3.7, 3.8, 3.8), bevel=1.1)
    # --- robe and torso (one lathe, pleated below the waist) -------------------------------------------
    b.part = P_STONE
    rings = [b.ring(0, cy, z, rx, ry, 40, 2.3, None, robe_fold) for z, rx, ry, cy in ROBE]
    b.loft(rings, cap0=False, cap1=True)
    # back mantle in two stepped layers (motif 1), each with a raised border, ending above the belt
    zr = [r[0] for r in ROBE]; rxr = [r[1] for r in ROBE]; ryr = [r[2] for r in ROBE]

    def mantle(spec, a0, a1, K, edge_drop):
        secs = []
        for z, extra, fold in spec:
            rx = np.interp(z, zr, rxr); ry = np.interp(z, zr, ryr)
            outer, inner = [], []
            for k in range(K):
                t = k / (K - 1); a = math.radians(a0 + (a1 - a0) * t)
                bord = .9 if (k in (0, 1, K - 2, K - 1)) else 0.0          # raised border along the side edges
                f = 1 + .085 * fold * math.sin(7 * a + .6) * (0 if bord else 1)
                dz = edge_drop * math.sin(a) ** 2 * fold                    # the hem is rounded, lowest at the middle of the back
                outer.append(V3(math.cos(a) * (rx + extra + bord) * f, math.sin(a) * (ry + extra + bord) * f, z - dz))
                inner.append(V3(math.cos(a) * ((rx + extra) * f - 1.6), math.sin(a) * ((ry + extra) * f - 1.6), z - dz))
            secs.append(outer + inner[::-1])
        return b.loft(secs[::-1])
    b.part = P_STONE
    mantle([(245, 2.6, 0), (238, 3.6, .3), (222, 5.0, .7), (206, 6.2, 1), (198, 7.0, 1), (196.5, 7.6, 1), (195.5, 6.0, 1)], 26, 154, 15, 6)
    mantle([(246.5, 4.6, 0), (241, 6.6, .1), (230, 9.0, .25), (224, 10.0, .3), (223, 8.6, .3)], 34, 146, 11, 3)   # short upper layer
    # belt with a concentric buckle in front and a stepped clasp at the back
    b.part = P_PLATE
    b.loft([b.ring(0, 0, z, rx, ry, 36, 2.3) for z, rx, ry in ((171, 35.0, 26.0), (172.5, 36.4, 27.4), (186, 36.2, 27.2), (187.5, 34.8, 25.8))])
    b.part = P_BAND
    b.ydisc((0, -27.4, 179.2), [(0, 1.5), (8.2, 1.5), (8.6, -.6), (8.0, -1.0), (6.2, -1.0), (5.9, -.4), (4.4, -.4), (4.1, -1.1), (1.9, -1.1), (0, -1.6)], 12)
    yb = b.surf(0, 179, side=1, default=27.0)
    b.prism([(px, pz + 173.0) for px, pz in step_outline(16, 12, 3)], yb - 1.0, yb + 1.6)
    b.ydisc((0, yb + 1.4, 177.6), [(0, -1.0), (3.8, -1.0), (4.0, .2), (3.4, .8), (2.0, .8), (1.7, .3), (0, 1.0)], 10)
    # front and back aprons: boards from the belt to the hem, with wave-and-teeth and stepped bands in relief
    zs = [66, 76, 90, 108, 128, 150, 171]
    for side in (-1, 1):
        b.part = P_STONE
        yfront = [b.surf(0, z, side=side) + side * 2.4 for z in zs]
        apron = []
        for z, yf in zip(zs, yfront):
            w = (12.5 if side < 0 else 14.5) + 5.5 * (171 - z) / 105
            sec = [V3(-w, yf - side * 7, z), V3(-w, yf - side * .7, z), V3(-w + .7, yf, z), V3(w - .7, yf, z), V3(w, yf - side * .7, z), V3(w, yf - side * 7, z)]
            apron.append(sec[::-1] if side < 0 else sec)
        b.loft(apron)
        ya = (lambda yf_: (lambda x, z: float(np.interp(z, zs, yf_))))(yfront)
        In = lambda x, z, ya=ya, s=side: ya(x, z) - s * .6
        Out = lambda d: (lambda x, z, ya=ya, s=side: ya(x, z) + s * d)
        b.part = P_BAND
        for k in range(7):
            x = -12.9 + k * 4.3
            b.prism([(x - 2.0, 71), (x + 2.0, 71), (x, 79)], In, Out(1.1))
        b.prism([(-15.5, 80.5), (15.5, 80.5), (15.2, 82.2), (-15.2, 82.2)], In, Out(1.0))
        for k in range(3):
            x = -9.6 + k * 9.6
            b.prism([(px + x, pz + 86) for px, pz in step_outline(9.0, 10.5, 3, down=bool(k % 2))], In, Out(1.2))
        b.prism([(-14.5, 98.5), (14.5, 98.5), (14.3, 100.2), (-14.3, 100.2)], In, Out(1.0))
        if side > 0:   # the back board also carries a concentric eye and a stepped column under the belt
            b.ydisc((0, 0, 0), [(0, -1.2), (6.0, -1.2), (6.4, .2), (5.6, 1.0), (4.0, 1.0), (3.7, .4), (2.2, .4), (1.9, 1.3), (0, 1.5)], 12,
                    M=Matrix.Translation(V3(0, ya(0, 122) + .2, 122)))
            for k, (w, z0) in enumerate(((8.0, 140), (6.0, 147), (4.0, 154))):
                b.prism([(-w / 2, z0), (w / 2, z0), (w / 2, z0 + 5.5), (-w / 2, z0 + 5.5)], In, Out(1.0 + .5 * k))
    # --- broad collar: three stepped tiers that dip over the chest ---------------------------------------
    b.part = P_CROWN

    def collar(spec, drop):
        out = []
        for z, rx, ry, cy, dk in spec:
            r = []
            for x, y in sup2d(32, 2.3):
                r.append(V3(x * rx, cy + y * ry, z - drop * dk * max(0.0, -y) ** 2))
            out.append(r)
        return out
    b.loft(collar([(263.6, 14.6, 13.6, -.8, 0), (264.3, 17.6, 16.2, -.8, 0), (261.6, 22.4, 19.4, -.5, .25), (260.3, 23.6, 20.4, -.4, .3),
                   (259.6, 23.3, 20.2, -.4, .3)], 3.0), cap0=False, cap1=False)
    b.loft(collar([(261.2, 22.0, 19.0, -.3, .2), (260.2, 25.6, 21.4, -.2, .35), (255.6, 32.0, 24.4, 0, .75), (254.1, 33.2, 25.2, 0, .8),
                   (253.5, 32.8, 25.0, 0, .8)], 6.0), cap0=False, cap1=False)
    b.loft(collar([(255.4, 31.6, 24.0, 0, .5), (254.4, 35.0, 26.0, 0, .62), (249.0, 41.0, 28.3, 0, .95), (246.8, 41.6, 28.8, 0, 1),
                   (245.8, 40.6, 28.0, 0, 1)], 10.0), cap0=False, cap1=False)
    # --- chest plate: two stepped boards on the breast, the concentric disc at the centre, abdomen steps ------
    def board(z0, z1, w0, w1, lift, n_z=5, bevel=.8):
        zz = [z0 + (z1 - z0) * i / (n_z - 1) for i in range(n_z)]
        secs = []
        for z in zz:
            w = w0 + (w1 - w0) * (z - z0) / (z1 - z0)
            xs = [-w, -w * .5, 0, w * .5, w]
            fy = [b.surf(x, z, default=-26.0) - lift for x in xs]
            front = [V3(x, y, z) for x, y in zip(xs, fy)]
            back = [V3(x, y + lift + 4.0, z) for x, y in zip(xs, fy)]
            secs.append(back[:1] + front + back[-1:][::-1] + back[1:-1][::-1])
        return b.loft(secs)
    b.part = P_PLATE
    board(192.0, 234.0, 15.0, 19.0, 1.6)
    board(196.0, 230.0, 11.0, 14.5, 1.4)
    for k, (z0, w) in enumerate(((188.6, 26.0), (186.6, 0))):   # abdomen step under the plate (above the belt)
        if w: board(z0, z0 + 3.0, w / 2, w / 2 + 1.0, 1.2, n_z=2)
    b.part = P_BAND
    yp = b.surf(0, 220, default=-28.0)
    b.ydisc((0, yp - .8, 220.0), [(0, 1.8), (8.4, 1.8), (9.0, .5), (9.0, -.5), (8.2, -1.1), (6.5, -1.1), (6.2, -.5), (4.8, -.5), (4.4, -1.3),
                                  (2.3, -1.3), (2.0, -.7), (0, -1.9)], 14)
    for s in (-1, 1):   # stepped corner blocks of the plate
        b.prism([(px + s * 13.0, pz + 232.0) for px, pz in step_outline(6.0, 4.0, 2, down=True)], b.surf(s * 13, 233, default=-26) + 1.0,
                b.surf(s * 13, 233, default=-26) - 2.4)
    # --- arms: sleeves (robe), stepped shoulder caps in two layers, bare forearms, bracelets, hands ----------------
    b.part = P_STONE
    for s in (-1, 1):
        elbow = (s * 52, 5, 201)
        b.tube([(s * 29, 0, 244), (s * 40, 2, 238), (s * 48, 4, 219), elbow], lambda t: (11.5 + 1.5 * sstep(.6, 1, t), 11.0 + 1.5 * sstep(.6, 1, t)),
               n=12, per_seg=2, up=(0, 0, 1))
        b.part = P_BAND
        b.tube([(s * 51.2, 4.8, 203.5), (s * 52.6, 5.2, 198.6)], lambda t: (13.6, 13.2), n=12, per_seg=1, up=(0, 0, 1))   # sleeve hem
        b.part = P_CROWN   # stepped shoulder caps: a wide lower plate and the stepped upper cap
        b.lathe((s * 43.5, 1, 235.0), (s * .62, 0, 1), [(19.5, -3.0), (20.5, -1.4), (20.5, .4), (17.0, 1.2), (0, 2.0)], 12, 2.6, ref=(0, 1, 0), sy=.88, cap0=True)
        b.lathe((s * 39.5, 1, 240.5), (s * .5, 0, 1), [(17.5, -4.5), (18.5, -2.5), (18.5, -.2), (14.6, .6), (14.6, 2.8), (10.2, 3.5), (10.2, 5.4), (0, 6.0)],
                12, 2.4, ref=(0, 1, 0), sy=.9, cap0=True)
        b.part = P_STONE
    b.part = P_SKIN
    # right forearm (the statue's right = -X) goes out to the staff; muscle bulge below the elbow
    fr = lambda t: (7.6 + 1.2 * math.sin(min(1, t * 2.2) * math.pi) * (1 - t) - 1.6 * t, 7.2 + .9 * math.sin(min(1, t * 2.2) * math.pi) * (1 - t) - 1.5 * t)
    b.tube([(-51.5, 4.5, 203.5), (-60, -.5, 190), (-69.6, -5.5, 177.5)], fr, n=10, per_seg=3, up=(0, 0, 1))
    b.part = P_BAND
    b.tube([(-67.2, -4.3, 180.7), (-70.1, -5.8, 176.6)], lambda t: (6.9, 6.6), n=10, per_seg=1, up=(0, 0, 1))      # bracelet
    b.part = P_SKIN
    # fist around the staff (axis x=-76, y=-7): the back of the hand on the outside, four fingers curling round the
    # front of the shaft with knuckles, the thumb across them
    SX, SY = -76.0, -7.0
    b.box((SX + 5.6, SY + .5, 169.6), (5.4, 10.2, 15.0), bevel=1.8, taper=(1, .92))
    for k, z in enumerate((162.8, 166.6, 170.4, 174.2)):
        r = 5.5 + (.25 if k in (1, 2) else 0)
        arc = [(SX + r * math.cos(math.radians(a)), SY + r * math.sin(math.radians(a)), z + .25 * (a + 100) / 100) for a in (12, -30, -75, -120, -160, -195)]
        b.tube(arc, lambda t: (2.0 + .25 * math.sin(t * math.pi), 1.85), n=6, per_seg=1, up=(0, 0, 1))
        b.box((SX + 1.2, SY - 6.3, z), (3.4, 1.6, 3.2), bevel=.6, rot=(0, 0, math.radians(-12)))   # knuckle
    b.tube([(SX + 6.0, SY - 2.5, 176.6), (SX + 3.5, SY - 5.9, 177.6), (SX - .5, SY - 6.9, 175.6), (SX - 3.2, SY - 6.0, 173.8)],
           lambda t: (2.3 - .5 * t, 2.1 - .4 * t), n=6, per_seg=1, up=(0, 0, 1))
    # left forearm comes across; the hand rests on the chest plate, fingers together and slightly curled
    b.tube([(51.5, 4.5, 203.5), (42, -10, 200), (24.5, -24.0, 200.5)], fr, n=10, per_seg=3, up=(0, 0, 1))
    b.part = P_BAND
    b.tube([(27.8, -22.5, 200.3), (23.2, -25.0, 200.6)], lambda t: (6.6, 6.3), n=10, per_seg=1, up=(0, 0, 1))      # bracelet
    b.part = P_SKIN
    yc = lambda x, z: min(b.surf(x, z, default=-27.0), b.surf(x + 2, z, default=-27.0))
    hy = yc(13, 202) - 2.4
    b.box((14.0, hy, 202.2), (11.5, 10.4, 5.0), bevel=1.9, taper=(.95, .9), rot=(math.radians(90), 0, math.radians(-8)))   # back of the hand
    for k, z in enumerate((198.6, 201.2, 203.8, 206.3)):
        L = (10.2, 12.0, 11.6, 9.8)[k]
        x0 = 8.6
        pts = [(x0, yc(x0, z) - 2.0, z), (x0 - L * .45, yc(x0 - L * .45, z) - 2.3, z + .2), (x0 - L * .85, yc(x0 - L * .85, z) - 1.4, z + .3),
               (x0 - L, yc(x0 - L, z) - .6, z + .3)]
        b.tube(pts, lambda t: (1.35 - .25 * t, 1.3 - .25 * t), n=6, per_seg=1, up=(0, 0, 1))
    b.tube([(17.0, hy - .2, 207.4), (13.6, yc(13.6, 209.2) - 1.7, 209.4), (10.6, yc(10.6, 209.6) - 1.2, 209.3)], lambda t: (1.7 - .3 * t, 1.6 - .3 * t),
           n=6, per_seg=1, up=(0, 0, 1))
    # --- staff: shaft, bands, ferrule, stepped finial with a concentric eye and a pair of small horns ----
    b.part = P_STAFF
    b.lathe((SX, SY, 20), (0, 0, 1), [(0, 0), (4.6, 0), (4.6, 4.0), (3.8, 5.6), (3.3, 7), (3.2, 100), (3.9, 101), (3.9, 104), (3.2, 105),
                                      (3.15, 258), (3.9, 259), (3.9, 262), (3.3, 263), (3.3, 276), (0, 276)], 8, cap0=False, cap1=False)
    b.slab(SX, SY, 296, 300.5, 12, 8, c=.8, n=12, p=6)
    b.slab(SX, SY, 300.4, 304, 9, 6.4, c=.7, n=12, p=6)
    b.slab(SX, SY, 290, 296.2, 6.5, 6.5, c=.6, n=8, p=4)
    b.ydisc((SX, SY, 311.5), [(0, 2.4), (7.4, 2.4), (7.8, 1.0), (7.8, -1.0), (7.0, -1.8), (5.0, -1.8), (4.8, -1.2), (3.2, -1.2), (2.9, -2.2), (0, -2.4)], 12)
    for s in (-1, 1):
        b.tube([(SX + s * 5.5, SY, 314), (SX + s * 9.6, SY - .5, 317.5), (SX + s * 10.8, SY - .8, 323), (SX + s * 9.0, SY - .6, 327.5)],
               lambda t: (1.6 * (1 - t) + .35, 1.6 * (1 - t) + .35), n=6, per_seg=2, up=(0, 1, 0))
    return b


HEAD_SCALE = 1.12     # the head is drawn at 1/1.12 and scaled about the neck pivot (a larger head reads better from below)
HAIR_AXIS = (0.0, -1.0)  # (x, y) cm of the head's vertical axis (strand lines in the texture run around it)


def statue_head():
    h = Builder('KingStatue_Head', sharp_deg=38)
    h.part = P_SKIN
    N = 28

    def head_fn(a, z):
        c, s = math.cos(a), math.sin(a)
        m = 1.0
        if s < -.15 and 272 < z < 286:      # cheekbones
            m += .035 * math.exp(-((abs(c) - .62) / .2) ** 2) * sstep(272, 277, z) * sstep(286, 281, z)
        return m
    prof = [(259.5, 11.4, 11.0, -1.0, 2.0), (264, 11.8, 11.3, -1.0, 2.0), (267, 13.2, 12.6, -2.6, 2.3), (269.2, 14.4, 14.0, -3.0, 2.5),
            (273, 14.9, 15.5, -2.6, 2.3), (279, 16.1, 16.6, -2.0, 2.25), (286, 16.6, 17.2, -1.5, 2.2), (293, 16.6, 17.4, -1.0, 2.15),
            (299, 15.6, 16.8, -.6, 2.1), (304, 12.6, 14.4, 0, 2.0), (306.5, 8.0, 9.5, 0, 2.0)]
    vr, _ = h.loft([h.ring(0, cy, z, rx, ry, N, p, None, head_fn) for z, rx, ry, cy, p in prof], cap0=True, cap1=True)
    fp = -12.8
    for v in [v for r in vr for v in r]:          # flatten the face plane a little
        if v.co.z / CM > 266 and v.co.y < fp * CM:
            v.co.y = fp * CM + (v.co.y - fp * CM) * .62
    h._tree = None
    S = lambda x, z, d=-15.0: h.surf(x, z, default=d)
    # brow ridge, nose, lips, chin
    brow = [(x, S(x, 290.2 - .006 * x * x) - .6, 290.2 - .006 * x * x + .9) for x in (-14.5, -9, -3, 3, 9, 14.5)]
    h.tube(brow, lambda t: (1.7 - .5 * abs(2 * t - 1), 1.9 - .5 * abs(2 * t - 1)), n=6, p=2.6, per_seg=2, up=(0, 0, 1))
    yn = S(0, 281)
    h.box((0, yn - 2.3, 281.2), (4.0, 4.6, 11.0), bevel=.9, taper=(.6, .55), rot=(math.radians(-6), 0, 0))
    h.box((0, yn - 2.4, 277.0), (4.8, 3.0, 1.8), bevel=.8)                    # nostril wings
    for s_ in (-1, 1):   # upper lip: two halves meeting in a small bow under the philtrum
        h.lens((s_ * 2.55, S(s_ * 2.55, 273.9) + .25, 273.9 - .12), 2.9, .95, 1.25, n=8, p=1.7, M=Matrix.Rotation(s_ * math.radians(-7), 4, 'Y'))
    h.lens((0, S(0, 271.25) + .2, 271.25), 4.3, 1.35, 2.2, n=10, p=1.8)         # lower lip (fuller)
    h.box((0, S(0, 276.2) - .45, 275.8), (1.6, 1.0, 2.0), bevel=.4)             # philtrum ridge
    h.box((0, S(0, 268.0) - 1.0, 267.8), (10.4, 4.6, 4.8), bevel=1.5)          # chin, broad and squared

    for s in (-1, 1):
        # eyelids around the eye socket (the eye itself is a separate mesh)
        ex = s * 6.4; ez = 285.4
        up_ = [(ex + s * dx * 4.4, S(ex + s * dx * 4.4, ez) - .5 - .5 * (1 - dx * dx), ez + 2.0 * (1 - dx * dx) + .35) for dx in (-1, -.5, 0, .5, 1)]
        lo_ = [(ex + s * dx * 4.0, S(ex + s * dx * 4.0, ez) - .3 - .3 * (1 - dx * dx), ez - 1.75 * (1 - dx * dx) - .1) for dx in (-1, -.5, 0, .5, 1)]
        h.tube(up_, lambda t: (1.35 - .45 * abs(2 * t - 1), 1.5 - .45 * abs(2 * t - 1)), n=5, per_seg=1, up=(0, 0, 1))
        h.tube(lo_, lambda t: (1.0 - .35 * abs(2 * t - 1), 1.1 - .35 * abs(2 * t - 1)), n=5, per_seg=1, up=(0, 0, 1))
        # ears with concentric ear plugs
        h.lens((s * 16.2, .6, 280.5), 3.3, 6.4, 3.0, n=8, M=Matrix.Rotation(s * math.radians(90), 4, 'Z'), front=-1)
        h.part = P_BAND
        h.ydisc((s * 17.8, .6, 279.8), [(0, 1.0), (2.7, 1.0), (2.9, .1), (2.3, -.5), (1.4, -.5), (0, -.3)], 8,
                M=Matrix.Rotation(s * math.radians(90), 4, 'Z'))
        h.part = P_SKIN
    # --- hair: a combed mass over the back and the temples, cut in two steps (motif 1), and a heavy braid ---
    h.part = P_HAIR
    pz = [r[0] for r in prof]; prx = [r[1] for r in prof]; pry = [r[2] for r in prof]; pcy = [r[3] for r in prof]

    def shell(zsecs, a0, a1, n, ph=0.0):
        secs = []
        for z, off, inset in zsecs:
            rx = np.interp(z, pz, prx); ry = np.interp(z, pz, pry); cy = np.interp(z, pz, pcy)
            sec = []
            for i in range(n):
                a = math.radians(a0 + (a1 - a0) * i / (n - 1))
                g = off - inset - 1.25 * (1 - abs(math.cos(6 * a + ph))) ** 1.5 * (1 - inset / 2.5)
                sec.append(V3(math.cos(a) * (rx + g), cy + math.sin(a) * (ry + g), z))
            secs.append(sec)
        vr_, fs = h.loft(secs[::-1], cap0=False, cap1=False)
        return fs
    # upper: from under the crown band to just above the ears, wrapping the temples
    shell([(297.0, .4, 0), (292.0, 1.5, 0), (287.0, 1.8, 0), (283.0, 1.9, 0), (282.2, 1.9, 1.6), (282.0, .2, 2.4)], -10, 190, 30)
    # lower step: the back of the head down to the nape, stepping out further
    shell([(284.0, 1.6, 0), (281.0, 3.6, 0), (277.0, 3.8, 0), (276.4, 3.8, 2.0), (276.2, 1.6, 3.2)], 26, 154, 26, ph=.26)
    shell([(277.5, 3.4, 0), (274.0, 5.2, 0), (271.0, 5.3, 0), (270.2, 5.3, 2.4), (270.0, 2.0, 4.0)], 34, 146, 22)
    for k in range(4):
        z = 280.5 - k * 3.6
        yb = h.surf(0, z, side=1, default=19.0)
        h.lens((0, yb - .2, z), 3.9 - .2 * k, 2.8, 3.8, n=8, front=1, M=Matrix.Rotation(math.radians(20 if k % 2 else -20), 4, 'Y'))
    zc = 280.5 - 4 * 3.6 + 1.4
    h.slab(0, h.surf(0, zc, side=1, default=17.0) + .6, zc - 2.6, zc, 5.4, 3.2, c=.5, n=8, p=5)
    # --- crown: band + 2 stepped tiers + cap, front medallion, back crest ---------------------------------------
    h.part = P_CROWN
    R = lambda z, rx, ry, cy=-.5: h.ring(0, cy, z, rx, ry, 24, 3.2)
    h.loft([R(295.3, 17.9, 18.4), R(295.9, 18.7, 19.2), R(302.4, 18.7, 19.2), R(303.0, 17.7, 18.2)])
    h.loft([R(302.8, 16.6, 17.1), R(303.3, 17.0, 17.5), R(308.4, 16.7, 17.2), R(308.9, 15.8, 16.3)])
    h.loft([R(308.7, 13.9, 14.4), R(309.2, 14.2, 14.7), R(313.6, 13.9, 14.4), R(314.1, 13.0, 13.5)])
    h.loft([R(313.9, 10.4, 10.9), R(314.3, 10.7, 11.2), R(317.6, 10.4, 10.9), R(318.1, 8.8, 9.3)])
    for k in range(10):                              # studs on the band (between the medallion and the horns)
        a = math.radians(-90 + 36 * k)
        if abs(math.degrees(a) + 90) < 20: continue
        x, y = math.cos(a) * 18.9, -.5 + math.sin(a) * 19.4
        h.box((x, y, 299.2), (2.4, 2.4, 3.2), bevel=0, rot=(0, 0, a + math.pi / 2))
    h.part = P_BAND
    h.ydisc((0, -19.6, 299.2), [(0, 1.2), (4.6, 1.2), (4.9, .3), (4.9, -.5), (4.3, -.9), (3.1, -.9), (2.9, -.4), (1.7, -.4), (1.5, -1.1), (0, -1.25)], 12, sz=.85)
    h.part = P_CROWN
    # back crest: a thin plate leaning back, widening in steps like a fan (motif 1), with a concentric eye
    crest = [(-8.5, 296), (8.5, 296), (8.5, 300), (11.5, 300), (11.5, 304.5), (14.5, 304.5), (14.5, 313.5), (11.0, 313.5), (11.0, 318),
             (6.0, 318), (6.0, 321.5), (-6.0, 321.5), (-6.0, 318), (-11.0, 318), (-11.0, 313.5), (-14.5, 313.5), (-14.5, 304.5),
             (-11.5, 304.5), (-11.5, 300), (-8.5, 300)]
    lean = Matrix.Translation(V3(0, 18.4, 296)) @ Matrix.Rotation(math.radians(-14), 4, 'X') @ Matrix.Translation(V3(0, -18.4, -296))
    h.prism(crest, 17.4, 19.6, M=lean)
    h.part = P_BAND
    h.ydisc((0, 0, 0), [(0, -1.0), (4.4, -1.0), (4.7, -.2), (4.7, .5), (4.0, .9), (2.7, .9), (2.5, .4), (1.4, .4), (0, 1.2)], 10,
            M=lean @ Matrix.Translation(V3(0, 19.8, 309.0)))
    # --- horns (motif 4): ribbed, sweeping out and up; the tips set the 3.6 m height -----------------------
    h.part = P_CROWN
    for s in (-1, 1):
        ctrl = [(s * 12, -1, 306), (s * 20, -1.5, 309), (s * 29, -3, 313.5), (s * 37, -4.3, 320.5), (s * 41.8, -4.0, 329.5),
                (s * 42.2, -2.6, 338.5), (s * 38.6, -1, 345), (s * 34.4, 0, 347.6)]
        h.tube(ctrl, lambda t: ((4.4 * (1 - t) ** .85 + .45) * (1 + .07 * math.sin(t * math.pi * 12) * (1 - t)),
                                (4.0 * (1 - t) ** .85 + .45) * (1 + .07 * math.sin(t * math.pi * 12) * (1 - t))), n=8, per_seg=3, up=(0, 1, 0))
        h.tube([(s * 16.2, -1.2, 307.4), (s * 20.6, -1.5, 309.4)], lambda t: (5.4, 5.1), n=8, per_seg=1, up=(0, 1, 0))   # horn socket
    # draw at 1/HEAD_SCALE, scale about the neck pivot
    for v in h.bm.verts: v.co = NECK * CM + (v.co - NECK * CM) * HEAD_SCALE
    h._tree = None
    return h


def statue_eye(s):
    e = Builder('KingStatue_Eye_' + ('L' if s > 0 else 'R'), sharp_deg=50)
    k = HEAD_SCALE
    e.ydisc((0, 0, 0), [(0, 1.0 * k), (3.6 * k, 1.0 * k), (3.75 * k, .2 * k), (3.45 * k, -.45 * k), (2.4 * k, -.6 * k), (1.9 * k, -.95 * k),
                        (1.3 * k, -.6 * k), (.8 * k, -1.1 * k), (0, -1.2 * k)], 12, sz=.62)
    return e


# ----------------------------------------------------------------------------------------------
# Statue texture: unwrap, bake geometry passes, paint with numpy
# ----------------------------------------------------------------------------------------------
TEX_N = 2048


def unwrap(objs, scales):
    sc = bpy.context.scene; vl = bpy.context.view_layer
    sc.tool_settings.use_uv_select_sync = True
    for o, s in zip(objs, scales):
        for x in vl.objects: x.select_set(False)
        o.data.uv_layers.new(name='UVMap')
        o.select_set(True); vl.objects.active = o
        bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.uv.smart_project(angle_limit=math.radians(52), island_margin=.004, area_weight=0.0, correct_aspect=True, scale_to_bounds=False)
        bpy.ops.object.mode_set(mode='OBJECT')
        uv = np.empty(len(o.data.loops) * 2, np.float32); o.data.uv_layers[0].data.foreach_get('uv', uv); o.data.uv_layers[0].data.foreach_set('uv', uv * s)
    for x in vl.objects: x.select_set(False)
    for o in objs: o.select_set(True)
    vl.objects.active = objs[0]
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.pack_islands(rotate=True, margin=.0025)
    bpy.ops.object.mode_set(mode='OBJECT')


def bake_passes(objs, mat):
    """Bake float passes into images: pos (xyz), nrm (xyz), occ (AO wide, AO tight, pointiness), part (part/16, chip, -)."""
    setup_cycles(1)
    sc = bpy.context.scene; vl = bpy.context.view_layer
    floor = bpy.data.objects.new('bake_floor', bpy.data.meshes.new('bake_floor'))
    fb = bmesh.new(); bmesh.ops.create_grid(fb, x_segments=1, y_segments=1, size=4.0); fb.to_mesh(floor.data); fb.free()
    sc.collection.objects.link(floor)
    nt = mat.node_tree; nodes = nt.nodes; links = nt.links
    keep = set(n.name for n in nodes)
    out = next(n for n in nodes if n.type == 'OUTPUT_MATERIAL')
    em = nodes.new('ShaderNodeEmission'); img_node = nodes.new('ShaderNodeTexImage')
    geo = nodes.new('ShaderNodeNewGeometry'); comb = nodes.new('ShaderNodeCombineXYZ')
    mad = nodes.new('ShaderNodeVectorMath'); mad.operation = 'MULTIPLY_ADD'
    old_link = out.inputs['Surface'].links[0].from_socket if out.inputs['Surface'].links else None
    links.new(em.outputs[0], out.inputs['Surface'])
    for x in vl.objects: x.select_set(False)
    for o in objs: o.select_set(True)
    vl.objects.active = objs[0]
    res = {}

    def bake(name, socket, samples):
        sc.cycles.samples = samples
        im = bpy.data.images.new('bake_' + name, TEX_N, TEX_N, alpha=True, float_buffer=True)
        im.generated_color = (0, 0, 0, 0)
        img_node.image = im; nodes.active = img_node
        for l in list(em.inputs['Color'].links): links.remove(l)
        links.new(socket, em.inputs['Color'])
        bpy.ops.object.bake(type='EMIT', margin=12, margin_type='EXTEND', use_clear=True)
        buf = np.empty(TEX_N * TEX_N * 4, np.float32); im.pixels.foreach_get(buf)
        res[name] = buf.reshape(TEX_N, TEX_N, 4); bpy.data.images.remove(im)
    mad.inputs[1].default_value = (.25, .25, .25); mad.inputs[2].default_value = (.5, .5, .5)
    links.new(geo.outputs['Position'], mad.inputs[0]); bake('pos', mad.outputs[0], 1)
    mad2 = nodes.new('ShaderNodeVectorMath'); mad2.operation = 'MULTIPLY_ADD'
    mad2.inputs[1].default_value = (.5, .5, .5); mad2.inputs[2].default_value = (.5, .5, .5)
    links.new(geo.outputs['Normal'], mad2.inputs[0]); bake('nrm', mad2.outputs[0], 1)
    ao1 = nodes.new('ShaderNodeAmbientOcclusion'); ao1.samples = 16; ao1.inputs['Distance'].default_value = .45
    ao2 = nodes.new('ShaderNodeAmbientOcclusion'); ao2.samples = 16; ao2.inputs['Distance'].default_value = .06
    links.new(ao1.outputs['AO'], comb.inputs[0]); links.new(ao2.outputs['AO'], comb.inputs[1]); links.new(geo.outputs['Pointiness'], comb.inputs[2])
    bake('occ', comb.outputs[0], 96)
    at = nodes.new('ShaderNodeAttribute'); at.attribute_name = 'part'; at.attribute_type = 'GEOMETRY'
    ac = nodes.new('ShaderNodeAttribute'); ac.attribute_name = 'chip'; ac.attribute_type = 'GEOMETRY'
    dv = nodes.new('ShaderNodeMath'); dv.operation = 'MULTIPLY'; dv.inputs[1].default_value = 1 / 16
    links.new(at.outputs['Fac'], dv.inputs[0])
    comb2 = nodes.new('ShaderNodeCombineXYZ'); links.new(dv.outputs[0], comb2.inputs[0]); links.new(ac.outputs['Fac'], comb2.inputs[1])
    bake('part', comb2.outputs[0], 1)
    for n in list(nodes):
        if n.name not in keep: nodes.remove(n)
    if old_link: links.new(old_link, out.inputs['Surface'])
    bpy.data.objects.remove(floor, do_unlink=True)
    return res


def paint_statue(res):
    pos, nrm, occ, part = res['pos'], res['nrm'], res['occ'], res['part']
    valid = pos[..., 3] > .5
    idx = np.nonzero(valid.ravel())[0]
    P = ((pos[..., :3].reshape(-1, 3)[idx] - .5) * 4).astype(np.float64)           # metres
    Nn = nrm[..., :3].reshape(-1, 3)[idx] * 2 - 1
    Nn /= np.maximum(np.linalg.norm(Nn, axis=1, keepdims=True), 1e-6)
    aoL = occ[..., 0].ravel()[idx]; aoS = occ[..., 1].ravel()[idx]; pt = occ[..., 2].ravel()[idx]
    pid = np.rint(part[..., 0].ravel()[idx] * 16).astype(int); chip = np.clip(part[..., 1].ravel()[idx], 0, 1)
    z = P[:, 2]; nz = Nn[:, 2]
    # pointiness -> convex edges / concave creases (normalised by its spread)
    med = np.median(pt); spread = max(1e-4, np.percentile(np.abs(pt - med), 90))
    edge = np.clip((pt - med) / spread - .35, 0, 1.6) / 1.6; crease = np.clip((med - pt) / spread - .35, 0, 1.6) / 1.6

    tone = np.empty((len(idx), 3), np.float32)
    tones = {P_STONE: (.37, .35, .31), P_PED: (.34, .32, .285), P_SKIN: (.40, .375, .33), P_BAND: (.37, .35, .31),
             P_CROWN: (.35, .33, .29), P_STAFF: (.31, .295, .265), P_HAIR: (.30, .285, .255), P_PLATE: (.36, .34, .30)}
    for k, c in tones.items(): tone[pid == k] = c
    mott = fbm3(P * 1.7, 4, 3.1); mid = fbm3(P * 7.5, 3, 11.7); grain = vnoise3(P * 160.0); grain2 = vnoise3(P * 55.0 + 9.1)
    col = tone * ((.76 + .46 * mott) * (.92 + .16 * mid) * (.95 + .10 * grain) * (.97 + .06 * grain2))[:, None]
    # warm / cool shifts of the limestone, and a few rusty iron stains
    warm = fbm3(P * .9, 2, 40.0)
    col *= np.stack([1 + .07 * (warm - .5), np.ones_like(warm), 1 - .08 * (warm - .5)], 1)
    rust = np.clip((fbm3(P * 2.3, 3, 77.0) - .62) * 5, 0, 1)
    col *= (1 + rust[:, None] * np.array((.10, .0, -.12)))
    # combed strands on the hair: fine grooves running down, around the head axis
    hair = (pid == P_HAIR)
    ang = np.arctan2(P[:, 1] - HAIR_AXIS[1] * CM, P[:, 0] - HAIR_AXIS[0] * CM)
    strand = np.abs(np.sin(ang * 13 + .8 * np.sin(P[:, 2] * 6.0)))
    col *= (1 - .22 * hair * np.clip((.22 - strand) / .22, 0, 1))[:, None]
    # rain streaks running down the vertical faces under every ledge
    st = fbm3(P * np.array([5.0, 5.0, .55]), 3, 21.0); st2 = np.clip((st - .48) * 4.5, 0, 1)
    vert = np.clip(1 - np.abs(nz) * 1.6, 0, 1)
    col *= (1 - .32 * st2 * vert)[:, None]
    # dark drips under the ledges (where the wide occlusion falls off on a vertical face)
    drip = np.clip((fbm3(P * np.array([14.0, 14.0, 1.2]), 2, 9.0) - .45) * 3, 0, 1) * vert * np.clip((.85 - aoL) * 3, 0, 1)
    col *= (1 - .25 * drip)[:, None]
    # ambient occlusion (wide and tight) and crease / edge shading
    col *= ((.22 + .78 * aoL ** 1.3) * (.42 + .58 * aoS ** 1.3) * (1 - .35 * crease))[:, None]
    col += (.075 * edge * (.6 + .4 * grain))[:, None]
    # weathering pits in the limestone
    pits = np.clip((vnoise3(P * 85.0 + 2.2) - .84) * 9, 0, 1) * np.clip((fbm3(P * 2.0, 2, 90.0) - .35) * 3, 0, 1)
    col *= (1 - .35 * pits)[:, None]
    # faded paint on the relief bands (red ochre), traces of gilding on the crown band
    flake = fbm3(P * 22.0, 3, 5.0)
    band = (pid == P_BAND).astype(np.float32) * np.clip((flake - .38) * 3.5, 0, 1) * (.35 + .65 * aoS)
    col = col * (1 - .55 * band[:, None]) + np.array((.46, .19, .12)) * (.55 * band * (.7 + .3 * mott))[:, None]
    gild = (pid == P_CROWN).astype(np.float32) * np.clip((fbm3(P * 14.0, 3, 8.0) - .55) * 4, 0, 1) * np.clip((z - 2.95) * 6, 0, 1) * aoS
    col = col * (1 - .35 * gild[:, None]) + np.array((.56, .43, .20)) * (.35 * gild)[:, None]
    # old pigment on the crown, collar, chest plate, belt and back mantle: red earth and a dull blue-green, faded and
    # flaked, kept in the hollows (where rain did not wash it off)
    rr = np.hypot(P[:, 0] - HAIR_AXIS[0] * CM, P[:, 1] - HAIR_AXIS[1] * CM)
    painted = ((pid == P_PLATE) | ((pid == P_CROWN) & ((z < 2.7) | (rr < .23)))).astype(np.float32)   # not on the horns
    hue = fbm3(P * 2.2, 2, 123.0)
    keep = np.clip((fbm3(P * 16.0, 3, 41.0) - .47) * 3.0, 0, 1) * (.25 + .75 * np.clip((1 - aoS) * 2.5 + .3, 0, 1))
    pig = painted * keep * .48
    red = np.array((.44, .20, .13)); teal = np.array((.20, .33, .30))
    pc = np.where((hue > .5)[:, None], red, teal) * (.75 + .35 * mott)[:, None]
    col = col * (1 - pig[:, None]) + pc * pig[:, None]
    # chips: lighter raw stone where it broke
    col = col * (1 - .6 * chip[:, None]) + np.array((.56, .53, .47)) * (.6 * chip * (.9 + .2 * grain))[:, None]
    # cracks: thin dark lines where a noise sheet crosses the surface, only in some regions
    region = np.clip((fbm3(P * 1.1, 2, 70.0) - .66) * 8, 0, 1) * (pid != P_HAIR)
    c1 = np.abs(fbm3(P * 2.6, 4, 33.0) - .5)                          # few, long, jagged cracks
    wid = .0065
    crack = np.clip(1 - c1 / wid, 0, 1) * region
    lip = np.clip(1 - np.abs(c1 - wid * 1.6) / (wid * .8), 0, 1) * region * .5
    col = col * (1 - .7 * crack[:, None]) * (1 + .12 * lip[:, None])
    # soil and damp rising from the ground, strongest in the cavities of the plinth
    soil = np_sstep(1.15, .0, z) ** 1.6 * (.5 + .5 * fbm3(P * 4.0, 3, 15.0)) * (.65 + .35 * (1 - aoS))
    col = col * (1 - .58 * soil[:, None]) + np.array((.17, .13, .095)) * (.58 * soil)[:, None]
    # grime settles on the upward faces (darker), moss on some of them and in low crevices; pale lichen spots
    up = np.clip((nz - .30) / .45, 0, 1)
    col *= (1 - .18 * up * (.5 + .5 * mid))[:, None]
    mn = fbm3(P * 9.0, 3, 61.0)
    moss = np.clip((mn - .40 + .26 * np_sstep(1.8, 0, z) - .06 * np_sstep(2.0, 3.4, z)) * 4, 0, 1) * up
    moss = np.maximum(moss, np.clip((mn - .48) * 4, 0, 1) * np_sstep(1.0, .0, z) * np.clip((1 - aoS) * 1.8 + (1 - aoL) * .6, 0, 1))
    moss = np.clip(moss, 0, 1) * (.7 + .3 * grain)
    mcol = np.array((.15, .17, .085)) * (.65 + .6 * fbm3(P * 30.0, 2, 3.3))[:, None]
    col = col * (1 - .8 * moss[:, None]) + mcol * (.8 * moss)[:, None]
    lich = np.clip((vnoise3(P * 26.0 + 4.4) - .80) * 9, 0, 1) * np.clip(nz * 1.5, 0, 1) * (1 - moss)
    col = col * (1 - .35 * lich[:, None]) + np.array((.62, .60, .48)) * (.35 * lich)[:, None]
    col = np.clip(col, 0, 1)
    rough = .90 - .10 * edge - .06 * (pid == P_SKIN) + .07 * moss - .05 * chip + .03 * (grain - .5)
    rough = np.clip(rough, .55, 1)
    base = np.zeros((TEX_N * TEX_N, 4), np.float32); base[:, 3] = 1; base[:, :3] = (.3, .29, .26)
    base[idx, :3] = col
    rgh = np.ones((TEX_N * TEX_N, 4), np.float32); rgh[:, :3] = .9; rgh[idx, :3] = rough[:, None]
    ms = np.zeros((TEX_N * TEX_N, 4), np.float32); ms[:, 3] = .1; ms[idx, 3] = 1 - rough
    shp = (TEX_N, TEX_N, 4)
    return base.reshape(shp), rgh.reshape(shp), ms.reshape(shp)


def build_statue():
    reset_scene()
    sc = bpy.context.scene
    mat = bpy.data.materials.new('TH_Statue'); mat.use_nodes = True
    eye_mat = bpy.data.materials.new('TH_StatueEye'); eye_mat.use_nodes = True
    bs = next(n for n in eye_mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    bs.inputs['Base Color'].default_value = (.045, .042, .038, 1); bs.inputs['Roughness'].default_value = .55
    bs.inputs['Emission Color'].default_value = (0, 0, 0, 1); bs.inputs['Emission Strength'].default_value = 1.0
    eye_mat['unity_note'] = '_BaseColor dark stone by default; #68 raises _BaseColor and _EmissionColor to make the eyes glow'

    bb = statue_body(); hb = statue_head()
    body = bb.finish(mat)
    head = hb.finish(mat)
    eyes = []
    eye_pos = {}
    for s in (1, -1):
        eb = statue_eye(s)
        ex = s * 6.4 * HEAD_SCALE; ez = NECK.z + (285.4 - NECK.z) * HEAD_SCALE
        tr = BVHTree.FromObject(head, bpy.context.evaluated_depsgraph_get())
        hit = tr.ray_cast(Vector((ex * CM, -1.0, ez * CM)), Vector((0, 1, 0)), 2.0)[0]
        ey = hit.y / CM + .6 if hit is not None else -15.0
        eb.bm.transform(Matrix.Rotation(s * math.radians(14), 4, 'Z'))
        o = eb.finish(eye_mat)
        o.location = V3(ex, ey, ez); eyes.append(o); eye_pos['L' if s > 0 else 'R'] = Vector(o.location)
    # UVs and the baked atlas (body + head share it; the head gets 1.6x the texel density)
    unwrap([body, head], [1.0, 1.6])
    res = bake_passes([body, head], mat)
    base, rgh, ms = paint_statue(res)
    tex = STATUE_DIR / 'textures'
    im_b = save_png(tex / 'KingStatue_BaseColor.png', base, 'KingStatue_BaseColor')
    im_r = save_png(tex / 'KingStatue_Roughness.png', rgh, 'KingStatue_Roughness', non_color=True)
    save_png(tex / 'KingStatue_MetallicSmoothness.png', ms, 'KingStatue_MetallicSmoothness', non_color=True)
    nt = mat.node_tree; bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    t1 = nt.nodes.new('ShaderNodeTexImage'); t1.image = im_b; nt.links.new(t1.outputs['Color'], bsdf.inputs['Base Color'])
    t2 = nt.nodes.new('ShaderNodeTexImage'); t2.image = im_r; nt.links.new(t2.outputs['Color'], bsdf.inputs['Roughness'])
    for o in (body, head):
        for a in [a.name for a in o.data.attributes if a.name in ('part', 'chip', 'fade')]: o.data.attributes.remove(o.data.attributes[a])
    # hierarchy: Root > Body, HeadPivot > Head > Eye_L / Eye_R
    pivot = NECK * CM
    root = bpy.data.objects.new('KingStatue_Root', None); root.empty_display_size = .3
    hp = bpy.data.objects.new('KingStatue_HeadPivot', None); hp.empty_display_type = 'ARROWS'; hp.empty_display_size = .25
    for o in (root, hp): sc.collection.objects.link(o)
    hp.parent = root; hp.location = pivot
    body.parent = root
    shift_origin(head, pivot, Vector()); head.parent = hp; head.location = Vector()
    for o in eyes:
        o.location = o.location - pivot; o.parent = head
    root['front'] = '-Y (Blender) / +Z (Unity)'; root['origin'] = 'centre of the plinth bottom, z=0'
    hp['note'] = 'turn about the up axis to turn the head; origin = centre of the neck'
    objs = [root, body, hp, head, *eyes]
    bpy.context.view_layer.update()
    stats = statue_stats(root, body, head, eyes, hp)
    export(objs, STATUE_DIR / 'KingStatue.fbx', STATUE_DIR / 'KingStatue.blend')
    return stats


def statue_stats(root, body, head, eyes, hp):
    allm = [body, head, *eyes]
    pts = [o.matrix_world @ v.co for o in allm for v in o.data.vertices]
    lo = Vector([min(p[i] for p in pts) for i in range(3)]); hi = Vector([max(p[i] for p in pts) for i in range(3)])
    tris = {o.name: tri_count(o) for o in allm}
    # colliders (Unity BoxCollider): plinth steps + robe (two) + staff & fist, from the floor to ~2.1 m
    wv = [body.matrix_world @ v.co for v in body.data.vertices]

    def bbox(sel):
        q = [p for p in wv if sel(p)]
        a = Vector([min(p[i] for p in q) for i in range(3)]); b = Vector([max(p[i] for p in q) for i in range(3)])
        return a, b
    boxes = []
    # z ranges are fixed (plinth steps 0-0.2-0.4-0.58, robe to 1.3, everything up to 2.1 m); x-y from the vertices in that range
    SA = Vector((-.76, -.07))
    on_staff = lambda p: (Vector((p.x, p.y)) - SA).length < .12
    for name, z0, z1, sel in (('Plinth1', 0.0, .20, lambda p: p.z < .195), ('Plinth2', .20, .40, lambda p: .205 < p.z < .395 and not on_staff(p)),
                              ('Plinth3', .40, .58, lambda p: .405 < p.z < .575 and not on_staff(p)),
                              ('RobeLower', .58, 1.30, lambda p: .585 < p.z < 1.30 and not on_staff(p)),
                              ('RobeUpper', 1.30, 2.10, lambda p: 1.20 <= p.z < 2.10 and not on_staff(p) and p.x > -.62),
                              ('Staff', .20, 2.10, lambda p: .2 < p.z < 2.10 and on_staff(p))):
        a, b = bbox(sel); a.z, b.z = z0, z1
        boxes.append(dict(name='Collider_' + name, center_unity=to_unity((a + b) / 2), size_unity=size_unity(b - a)))
    eye_w = {k: eyes[i].matrix_world.translation for i, k in enumerate(('L', 'R'))}
    return dict(model='KingStatue', height_m=round(hi.z - lo.z, 4), dimensions_m=[round(hi[i] - lo[i], 4) for i in range(3)],
                bounds_unity=dict(min=to_unity(Vector((hi.x, hi.y, lo.z))), max=to_unity(Vector((lo.x, lo.y, hi.z)))),
                triangles=tris, triangles_total=sum(tris.values()),
                eye_center_unity={k: to_unity(v) for k, v in eye_w.items()}, eye_height_m=round(eye_w['L'].z, 4),
                head_pivot_unity=to_unity(hp.matrix_world.translation), head_pivot_note='turn KingStatue_HeadPivot about the world up axis (local Y with Bake Axis Conversion on; local Z without it)',
                colliders=boxes, materials={'TH_Statue': ['KingStatue_Body', 'KingStatue_Head'], 'TH_StatueEye': ['KingStatue_Eye_L', 'KingStatue_Eye_R']},
                textures=['textures/KingStatue_BaseColor.png', 'textures/KingStatue_Roughness.png', 'textures/KingStatue_MetallicSmoothness.png'],
                texture_size=TEX_N)


# ==============================================================================================
# 2. The grave keeper (ruin phantom)
# ==============================================================================================
PHANTOM_DIR = HERE / 'Phantom'
EYE_Z = 150.0
PIVOT = Vector((-24.0, -80.0, 196.0))      # lantern hang point (cm)
FADE_LO, FADE_HI = .15, .70


def phantom_body():
    b = Builder('GraveKeeper_Body', sharp_deg=55)
    N = 32
    # robe: ragged hem with tongues, pleats that drift, a gentle backward trail (gliding)
    def tongue(a):
        t = (a / (2 * math.pi) * 9 + .3) % 1.0
        return abs(2 * t - 1) ** 1.6 + .25 * noise.noise(Vector((math.cos(a) * 2, math.sin(a) * 2, 1.7)))

    def pleat(a, z):
        amp = .15 * sstep(118, 16, z) + .025
        f = .6 * math.sin(7 * a + .02 * z + .5) + .4 * math.sin(12 * a - .015 * z + 2.0); f = math.copysign(abs(f) ** .6, f)
        return 1 + amp * f
    robe = [(141, 20.5, 15.5, 2.0), (126, 22.0, 16.5, 2.0), (112, 23.2, 17.4, 2.0), (98, 24.6, 18.4, 2.5), (84, 26.2, 19.4, 3.0),
            (70, 28.0, 20.6, 3.6), (56, 30.0, 22.0, 4.2), (42, 31.6, 23.4, 4.8), (31, 32.8, 24.4, 5.4), (21, 33.4, 25.0, 6.0)]
    rings = [b.ring(0, cy, z, rx, ry, N, 2.1, None, pleat) for z, rx, ry, cy in robe]
    hem = []
    for k, (x, y) in enumerate(sup2d(N, 2.1)):
        a = math.atan2(y, x); m = pleat(a, 6)
        zz = 4 + 11 * tongue(a)
        hem.append(V3(x * 33.6 * m, 6.4 + y * 25.0 * m, zz))
    rings.append(hem)
    vr, robe_faces = b.loft(rings[::-1], cap0=False, cap1=False)
    for v in vr[0]: v[b.lfade] = .55       # ragged edge a little thinner (the shader also fades by height)
    keep = set(robe_faces)                 # built outward; excluded from the normal recalculation
    # chest band of steps (motif 1): a raised band with stepped blocks in front
    b.part = 1
    band = [b.ring(0, 2.0, z, rx, ry, N, 2.1) for z, rx, ry in ((98.2, 24.7, 18.5), (99.0, 25.8, 19.6), (108.0, 25.1, 19.0), (108.8, 24.0, 17.9))]
    b.loft(band, cap0=False, cap1=False)
    yb = lambda x, z: b.surf(x, z, default=-16.0)
    for k in range(5):
        x = -12 + k * 6
        poly = [(px + x, pz + 100.0) for px, pz in step_outline(5.2, 7.2, 3, down=bool(k % 2))]
        b.prism(poly, lambda x_, z: yb(x_, z) + .8, lambda x_, z: yb(x_, z) - 1.3)
    b.part = 0
    # hood: dome with a pointed tip pulled back, short ragged cape over the shoulders
    def hood_fn(a, z):
        if z > 132: return 1.0
        return 1 + .09 * math.sin(6 * a + 1.0)
    hood = [(117, 29.0, 22.0, 2.0), (124, 28.0, 21.2, 2.0), (130, 26.0, 20.0, 2.0), (136, 23.2, 18.6, 2.0), (141, 20.6, 17.4, 2.0), (145, 17.4, 16.4, 1.5),
            (149, 16.4, 16.4, 1.2), (153, 16.2, 16.6, 1.4), (157, 15.9, 16.6, 1.8), (161, 15.2, 16.2, 2.4), (165, 14.0, 15.4, 3.2),
            (169, 12.0, 13.8, 4.2), (173, 9.0, 11.2, 5.6), (176.5, 5.2, 7.4, 7.4), (179, 1.8, 3.0, 9.4)]
    hr = [b.ring(0, cy, z, rx, ry, N, 2.15, None, hood_fn) for z, rx, ry, cy in hood]
    # ragged lower edge of the cape
    for v in hr[0]:
        a = math.atan2(v.y, v.x); t = (a / (2 * math.pi) * 7 + .15) % 1.0
        v.z = v.z - (5.5 * abs(2 * t - 1) ** 1.5) * CM
    hr.append([V3(0, 10.4, 180.0)])
    hv, hf = b.loft(hr, cap0=False, cap1=False)
    for v in hv[0]: v[b.lfade] = 1.0
    for v in hv[1]: v[b.lfade] = .35
    # face opening: remove front faces inside an ellipse, snap the rim to it, turn the rim inwards (lip)
    OX, OZ, OC = 10.2, 12.4, EYE_Z + .8
    hole = [f for f in hf if f.is_valid and f.calc_center_median().y < 0
            and (f.calc_center_median().x / (OX * CM)) ** 2 + ((f.calc_center_median().z - OC * CM) / (OZ * CM)) ** 2 < 1]
    hole_v = {v for f in hole for v in f.verts}
    bmesh.ops.delete(b.bm, geom=hole, context='FACES_ONLY')
    loose = [v for v in hole_v if v.is_valid and not v.link_faces]
    bmesh.ops.delete(b.bm, geom=loose, context='VERTS')
    rim = [e for e in b.bm.edges if e.is_boundary and all(v in hole_v for v in e.verts)]
    rim_v = list({v for e in rim for v in e.verts})
    for v in rim_v:
        d = Vector((v.co.x / (OX * CM), 0, (v.co.z - OC * CM) / (OZ * CM)))
        k = 1 / max(1e-6, math.hypot(d.x, d.z))
        v.co.x *= k; v.co.z = OC * CM + (v.co.z - OC * CM) * k
    ext = bmesh.ops.extrude_edge_only(b.bm, edges=rim)
    lip = [g for g in ext['geom'] if isinstance(g, bmesh.types.BMVert)]
    for v in lip:
        v.co.x *= .86; v.co.z = OC * CM + (v.co.z - OC * CM) * .88; v.co.y += 4.5 * CM
    target = Vector((0, -.4, OC * CM))
    for f in [g for g in ext['geom'] if isinstance(g, bmesh.types.BMFace)]:
        f[b.lp] = 0; f.normal_update()
        if f.normal.dot(target - f.calc_center_median()) < 0: f.normal_flip()
        keep.add(f)
    keep |= {f for f in hf if f.is_valid}
    # horns on the hood (the king's horns, small)
    for s in (-1, 1):
        b.tube([(s * 11.5, 4.5, 166.5), (s * 16.5, 4.0, 169.5), (s * 20.0, 2.8, 174.5), (s * 20.4, 1.5, 180.5), (s * 18.2, .6, 185.5)],
               lambda t: ((2.9 * (1 - t) ** .8 + .35) * (1 + .08 * math.sin(t * math.pi * 9) * (1 - t)),) * 2, n=7, per_seg=2, up=(0, 1, 0))
    # sleeves: right one reaches forward with the pole, left hangs; the ends fade out (vertex alpha)
    def sleeve(ctrl, r0, r1):
        vr, _ = b.tube(ctrl, lambda t: (r0 + (r1 - r0) * t ** 1.6, (r0 + (r1 - r0) * t ** 1.6) * .92), n=10, per_seg=2, up=(0, 0, 1), cap0=True, cap1=False)
        n = len(vr)
        for i, r in enumerate(vr):
            f = sstep(.55, 1.0, i / (n - 1))
            for v in r: v[b.lfade] = f
        # ragged sleeve end
        for k, v in enumerate(vr[-1]):
            v.co += (Vector(ctrl[-1]) - Vector(ctrl[-2])).normalized() * (2.4 * math.sin(k * 2.1) * CM)
    sleeve([(-15, 1, 137), (-23, 1, 128), (-27.5, -6, 117), (-27, -22, 116.5), (-25.5, -36.5, 119.0)], 6.4, 10.5)
    sleeve([(15, 1, 137), (22, 2, 126), (25, 2, 108), (24, -1, 90), (22.5, -3, 80)], 6.4, 10.0)
    # pole: from behind the hand up and forward to a hook; its tail end fades
    H = Vector((-25.0, -40.0, 120.5)); T = Vector((-24.0, -78.5, 199.0)); d = T - H
    B = H - d * .42
    pv, _ = b.tube([tuple(B), tuple(H - d * .1), tuple(H + d * .4), tuple(T - d * .05), (-24.0, -80.4, 201.8), (-24.0, -82.0, 200.6),
                    (-24.0, -81.4, 198.6), (-24.0, -80.2, 197.9)],
                   lambda t: (1.55 - .4 * t, 1.55 - .4 * t), n=6, per_seg=2, up=(1, 0, 0))
    for i, r in enumerate(pv):
        for v in r: v[b.lfade] = sstep(.18, 0, i / (len(pv) - 1))
    return b, keep


def phantom_void():
    v = Builder('GraveKeeper_Void', sharp_deg=80)
    # concave dark dish behind the face opening (faces the front)
    v.ydisc((0, -9.0, EYE_Z + .8), [(0, 6.2), (5.6, 5.0), (8.6, 2.4), (10.2, -.2), (10.4, -1.2)][::-1], 18, sz=1.2)
    return v


def phantom_eye(s):
    e = Builder('GraveKeeper_Eye_' + ('L' if s > 0 else 'R'), sharp_deg=60)
    e.ydisc((0, 0, 0), [(0, .5), (1.6, .5), (1.72, 0), (1.5, -.45), (.85, -.7), (0, -.75)], 10, sz=.55)
    return e


def lantern_frame():
    f = Builder('GraveKeeper_LanternFrame', sharp_deg=40)
    sq = lambda w: w / 1.4142          # slab(n=4, p=2) radius for a square of side w
    # relative to the hang point (cm); everything hangs below it
    f.lathe((0, 0, 0), (0, 0, 1), [(0, -1.4), (1.3, -.9), (1.3, .5), (0, 1.0)], 6)                          # hang knob
    f.lathe((0, 0, 0), (0, 0, 1), [(.8, -6.4), (.8, -1.2)], 6)                                               # hanger rod
    for z0, w in ((-5.8, 5.0), (-7.8, 8.8), (-9.8, 12.6)):                                                   # stepped roof
        f.slab(0, 0, z0 - 2.2, z0, 2 * sq(w), 2 * sq(w), c=.35, n=4, p=2)
    for sx in (-1, 1):
        for sy in (-1, 1):
            f.box((sx * 5.2, sy * 5.2, -19.0), (1.5, 1.5, 15.0), bevel=.3)                                   # corner posts
    for z in (-12.7, -25.3):                                                                                 # frame rings
        f.slab(0, 0, z - .6, z + .6, 2 * sq(12.2), 2 * sq(12.2), c=.25, n=4, p=2)
    for z0, w in ((-26.0, 12.4), (-28.0, 8.6), (-30.0, 4.8)):                                                # stepped base
        f.slab(0, 0, z0 - 2.0, z0, 2 * sq(w), 2 * sq(w), c=.3, n=4, p=2)
    f.lathe((0, 0, -32.0), (0, 0, 1), [(0, -4.4), (1.3, -3.2), (1.9, -1.5), (1.7, 0)], 8)                  # drop finial
    f.ydisc((0, -6.25, -27.0), [(0, .5), (1.6, .5), (1.7, 0), (1.2, -.25), (.8, -.1), (0, -.3)], 10)          # concentric eye on the base
    return f


def lantern_glow():
    g = Builder('GraveKeeper_LanternGlow', sharp_deg=60)
    g.lathe((0, 0, 0), (0, 0, 1), [(0, -24.0), (2.2, -22.0), (3.0, -19.4), (2.5, -16.6), (0, -14.4)], 6, cap0=False, cap1=False)
    return g


def vertex_shade(obj, R=.12, N=24):
    """Per-vertex ambient occlusion (0 open .. 1 occluded) by casting rays against the mesh itself."""
    me = obj.data; tree = BVHTree.FromObject(obj, bpy.context.evaluated_depsgraph_get())
    dirs = []
    for i in range(N):
        u = (i + .5) / N; r = math.sqrt(u); ph = i * 2.39996
        dirs.append(Vector((r * math.cos(ph), r * math.sin(ph), math.sqrt(1 - u))))
    out = []
    for v in me.vertices:
        n = v.normal; q = Vector((0, 0, 1)).rotation_difference(n); p = v.co + n * .002
        occ = sum(1 for d in dirs if tree.ray_cast(p, q @ d, R)[0] is not None) / N
        out.append(occ)
    return out


def gradient_strip(stops, W=256, H=8):
    v = np.linspace(0, 1, W)
    col = np.stack([np.interp(v, [s[0] for s in stops], [s[1][i] for s in stops]) for i in range(3)], -1)
    met = np.interp(v, [s[0] for s in stops], [s[2] for s in stops]); rgh = np.interp(v, [s[0] for s in stops], [s[3] for s in stops])
    rng = np.random.default_rng(5); mott = 1 + .08 * (rng.random(W) - .5)
    base = np.ones((H, W, 4), np.float32); base[..., :3] = (col * mott[:, None])[None]
    ms = np.zeros((H, W, 4), np.float32); ms[..., 0] = met[None]; ms[..., 3] = (1 - rgh)[None]
    return base, ms


# old bronze: verdigris in the cavities, dark brown body, worn warm edges
BRONZE_OLD = [(0, (.035, .07, .06), 0, .95), (.22, (.11, .23, .19), .05, .9), (.42, (.15, .22, .17), .25, .82), (.58, (.17, .16, .11), .55, .7),
              (.78, (.27, .19, .11), .8, .55), (1, (.52, .39, .23), .95, .4)]


def build_phantom():
    reset_scene()
    sc = bpy.context.scene
    m_body = bpy.data.materials.new('TH_PhantomBody'); m_void = bpy.data.materials.new('TH_PhantomVoid')
    m_eye = bpy.data.materials.new('TH_PhantomEye'); m_lan = bpy.data.materials.new('TH_PhantomLantern')
    for m in (m_body, m_void, m_eye, m_lan): m.use_nodes = True
    ghost_preview_nodes(m_body)
    bsdf = lambda m: next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    bsdf(m_void).inputs['Base Color'].default_value = (.006, .009, .012, 1); bsdf(m_void).inputs['Roughness'].default_value = 1.0
    bsdf(m_void).inputs['Specular IOR Level'].default_value = 0.0
    glow_nodes(m_eye, (0.2, 0.7, 0.9), 2.2)
    base, ms = gradient_strip(BRONZE_OLD)
    tex = PHANTOM_DIR / 'textures'
    im_b = save_png(tex / 'GraveKeeper_Lantern_BaseColor.png', base, 'GraveKeeper_Lantern_BaseColor')
    save_png(tex / 'GraveKeeper_Lantern_MetallicSmoothness.png', ms, 'GraveKeeper_Lantern_MetallicSmoothness', non_color=True)
    nt = m_lan.node_tree; t = nt.nodes.new('ShaderNodeTexImage'); t.image = im_b; t.interpolation = 'Linear'
    nt.links.new(t.outputs['Color'], bsdf(m_lan).inputs['Base Color']); bsdf(m_lan).inputs['Metallic'].default_value = .6
    bsdf(m_lan).inputs['Roughness'].default_value = .55

    bb, keep = phantom_body()
    bmesh.ops.recalc_face_normals(bb.bm, faces=[f for f in bb.bm.faces if f not in keep])
    body = bb.finish(m_body, recalc=False)
    # vertex colour: RGB = soft fold shade, A = opacity multiplier (1 - fade)
    occ = vertex_shade(body, R=.10)
    fade = body.data.attributes['fade'].data
    me = body.data
    ca = me.color_attributes.new('Col', 'BYTE_COLOR', 'CORNER')
    vcol = []
    for i, v in enumerate(me.vertices):
        sh = 1 - .55 * occ[i] ** 1.2
        vcol.append((sh, sh, sh, max(0.0, 1 - fade[i].value)))
    band = {p.index for p in me.polygons if me.attributes['part'].data[p.index].value == 1}
    for p in me.polygons:
        for li in p.loop_indices:
            r, g, bl, a = vcol[me.loops[li].vertex_index]
            if p.index in band: r = g = bl = min(1.0, r * 1.25)
            ca.data[li].color = (r, g, bl, a)
    me.color_attributes.active_color = ca; me.color_attributes.render_color_index = 0
    # cylindrical UV (u = angle, v = height / 2 m) for shader noise if needed
    uvl = me.uv_layers.new(name='UVMap')
    for p in me.polygons:
        us = [math.atan2(me.vertices[me.loops[li].vertex_index].co.y, me.vertices[me.loops[li].vertex_index].co.x) / (2 * math.pi) % 1 for li in p.loop_indices]
        if max(us) - min(us) > .5: us = [u + 1 if u < .5 else u for u in us]
        for li, u in zip(p.loop_indices, us): uvl.data[li].uv = (u, me.vertices[me.loops[li].vertex_index].co.z / 2)
    for a in [a.name for a in me.attributes if a.name in ('part', 'chip', 'fade')]: me.attributes.remove(me.attributes[a])

    void = phantom_void().finish(m_void)
    if sum((p.normal.y for p in void.data.polygons), 0.0) > 0: void.data.flip_normals()
    eyes = []
    for s in (1, -1):
        o = phantom_eye(s).finish(m_eye)
        o.data.transform(Matrix.Rotation(s * math.radians(12), 4, 'Z'))
        o.location = V3(s * 4.3, -11.0, EYE_Z); eyes.append(o)
    fb = lantern_frame(); frame = fb.finish(m_lan)
    occ = vertex_shade(frame, R=.035)
    uvl = frame.data.uv_layers.new(name='UVMap')
    edge = []
    for v in frame.data.vertices: edge.append(0.0)
    for p in frame.data.polygons:
        for li in p.loop_indices:
            vi = frame.data.loops[li].vertex_index
            nz = noise.noise(frame.data.vertices[vi].co * 60.0)
            uvl.data[li].uv = (min(.98, max(.02, .62 - .75 * occ[vi] ** 1.1 + .12 * nz)), .5)
    for a in [a.name for a in frame.data.attributes if a.name in ('part', 'chip', 'fade')]: frame.data.attributes.remove(frame.data.attributes[a])
    glow = lantern_glow().finish(m_eye)
    for o in (void, *eyes, glow):
        for a in [a.name for a in o.data.attributes if a.name in ('part', 'chip', 'fade')]: o.data.attributes.remove(o.data.attributes[a])
    root = bpy.data.objects.new('GraveKeeper_Root', None); root.empty_display_size = .3
    lp = bpy.data.objects.new('GraveKeeper_LanternPivot', None); lp.empty_display_type = 'ARROWS'; lp.empty_display_size = .1
    for o in (root, lp): sc.collection.objects.link(o)
    for o in (body, void, *eyes): o.parent = root
    lp.parent = root; lp.location = PIVOT * CM
    for o in (frame, glow): o.parent = lp; o.location = Vector()
    root['front'] = '-Y (Blender) / +Z (Unity)'; root['origin'] = 'floor under the hem'
    root['fade_start_m'] = FADE_LO; root['fade_full_m'] = FADE_HI
    lp['note'] = 'hang point of the lantern; swing it about the left-right axis (local X)'
    objs = [root, body, void, *eyes, lp, frame, glow]
    bpy.context.view_layer.update()
    allm = [body, void, *eyes, frame, glow]
    pts = [o.matrix_world @ v.co for o in allm for v in o.data.vertices]
    lo = Vector([min(p[i] for p in pts) for i in range(3)]); hi = Vector([max(p[i] for p in pts) for i in range(3)])
    tris = {o.name: tri_count(o) for o in allm}
    gw = [glow.matrix_world @ v.co for v in glow.data.vertices]
    gc = sum(gw, Vector()) / len(gw)
    stats = dict(model='GraveKeeper', height_m=round(hi.z - lo.z, 4), dimensions_m=[round(hi[i] - lo[i], 4) for i in range(3)],
                 lowest_z_m=round(lo.z, 4), triangles=tris, triangles_total=sum(tris.values()),
                 eye_center_unity={k: to_unity(eyes[i].matrix_world.translation) for i, k in enumerate(('L', 'R'))},
                 eye_height_m=round(eyes[0].matrix_world.translation.z, 4),
                 lantern_pivot_unity=to_unity(lp.matrix_world.translation), lantern_glow_center_unity=to_unity(gc),
                 lantern_forward_m=round(-gc.y, 4), lantern_height_m=round(gc.z, 4),
                 hem_fade_start_m=FADE_LO, hem_fade_full_m=FADE_HI,
                 vertex_color='Col (corner, byte): RGB = fold shade multiplier, A = opacity multiplier (sleeve ends, cape edge, pole tail -> 0)',
                 materials={'TH_PhantomBody': ['GraveKeeper_Body'], 'TH_PhantomVoid': ['GraveKeeper_Void'],
                            'TH_PhantomEye': ['GraveKeeper_Eye_L', 'GraveKeeper_Eye_R', 'GraveKeeper_LanternGlow'], 'TH_PhantomLantern': ['GraveKeeper_LanternFrame']},
                 textures=['textures/GraveKeeper_Lantern_BaseColor.png', 'textures/GraveKeeper_Lantern_MetallicSmoothness.png'])
    export(objs, PHANTOM_DIR / 'GraveKeeper.fbx', PHANTOM_DIR / 'GraveKeeper.blend')
    return stats


def glow_nodes(m, rgb, strength=4.0):
    nt = m.node_tree; nt.nodes.clear()
    out = nt.nodes.new('ShaderNodeOutputMaterial'); em = nt.nodes.new('ShaderNodeEmission')
    em.inputs['Color'].default_value = (*rgb, 1); em.inputs['Strength'].default_value = strength
    nt.links.new(em.outputs[0], out.inputs['Surface'])


def ghost_preview_nodes(m, rgb=(.34, .62, .70)):
    """Approximation of TH_PhantomGhost for Blender previews: unlit, rim (fresnel) brighter and more opaque,
    fades out below FADE_HI down to FADE_LO (object-space height), times the vertex alpha, one layer."""
    nt = m.node_tree; nt.nodes.clear(); N = nt.nodes; Lk = nt.links.new
    out = N.new('ShaderNodeOutputMaterial'); mix = N.new('ShaderNodeMixShader')
    tr = N.new('ShaderNodeBsdfTransparent'); em = N.new('ShaderNodeEmission')
    lw = N.new('ShaderNodeLayerWeight'); lw.inputs['Blend'].default_value = .45
    vc = N.new('ShaderNodeVertexColor'); vc.layer_name = 'Col'
    tc = N.new('ShaderNodeTexCoord'); sep = N.new('ShaderNodeSeparateXYZ')
    mr = N.new('ShaderNodeMapRange'); mr.inputs['From Min'].default_value = FADE_LO; mr.inputs['From Max'].default_value = FADE_HI
    mr.interpolation_type = 'SMOOTHSTEP'
    rim = N.new('ShaderNodeMath'); rim.operation = 'MULTIPLY_ADD'; rim.inputs[1].default_value = .6; rim.inputs[2].default_value = .22
    a1 = N.new('ShaderNodeMath'); a1.operation = 'MULTIPLY'; a2 = N.new('ShaderNodeMath'); a2.operation = 'MULTIPLY'
    a2.use_clamp = True
    cm = N.new('ShaderNodeMath'); cm.operation = 'MULTIPLY_ADD'; cm.inputs[1].default_value = 1.6; cm.inputs[2].default_value = .55
    col = N.new('ShaderNodeVectorMath'); col.operation = 'MULTIPLY'; col.inputs[1].default_value = rgb
    em_s = N.new('ShaderNodeMath'); em_s.operation = 'MULTIPLY'
    Lk(lw.outputs['Facing'], rim.inputs[0]); Lk(tc.outputs['Object'], sep.inputs[0]); Lk(sep.outputs['Z'], mr.inputs['Value'])
    Lk(rim.outputs[0], a1.inputs[0]); Lk(mr.outputs['Result'], a1.inputs[1]); Lk(a1.outputs[0], a2.inputs[0]); Lk(vc.outputs['Alpha'], a2.inputs[1])
    Lk(vc.outputs['Color'], col.inputs[0]); Lk(lw.outputs['Facing'], cm.inputs[0]); Lk(cm.outputs[0], em_s.inputs[0])
    em_s.inputs[1].default_value = 1.0
    Lk(col.outputs[0], em.inputs['Color']); Lk(em_s.outputs[0], em.inputs['Strength'])
    Lk(a2.outputs[0], mix.inputs['Fac']); Lk(tr.outputs[0], mix.inputs[1]); Lk(em.outputs[0], mix.inputs[2]); Lk(mix.outputs[0], out.inputs['Surface'])
    for attr, val in (('surface_render_method', 'BLENDED'), ('use_transparency_overlap', False), ('use_backface_culling', True)):
        try: setattr(m, attr, val)
        except (AttributeError, TypeError): pass
    m['unity_note'] = 'TIB/TreasureHaul/PhantomGhost: unlit, fresnel rim, height fade, vertex A, depth prepass (one layer), queue 2900'


# ----------------------------------------------------------------------------------------------
# Export
# ----------------------------------------------------------------------------------------------
def turn_z180(objs):
    """Turn the whole model 180 degrees about the world Z axis (it is its own inverse). Every node has identity rotation and
    scale (pivots are plain EMPTYs with a location), so this is: negate x and y of every location, and rotate every mesh
    (vertices and custom normals) about its own origin. Needed only for the FBX, see export()."""
    seen = set()
    for o in objs:
        if o.rotation_euler.to_quaternion().angle > 1e-6 or any(abs(v - 1.0) > 1e-6 for v in o.scale):
            raise RuntimeError('%s has a rotation or scale; the FBX axis trick needs identity nodes' % o.name)
        o.location = Vector((-o.location.x, -o.location.y, o.location.z))
        if o.type == 'MESH' and o.data.name not in seen:
            seen.add(o.data.name)
            o.data.transform(Matrix.Rotation(math.pi, 4, 'Z'))
            o.data.update()
    bpy.context.view_layer.update()


def export(objs, fbx_path, blend_path):
    """FBX axes (decided 2026-11-07 by importing every variant into Unity 6.3 and reading the nodes):
    write the file in Blender's own Z-up (axis_up='Z', axis_forward='Y', no bake_space_transform) and turn the model 180 degrees
    about Z first. With Bake Axis Conversion ON, Unity then reads every node with rotation 0, Y up, front +Z, local
    positions = (x', z', y') of the turned Blender values (= (-x, z, -y) of the .blend = to_unity() = stats.json *_unity), and the mesh vertices are Y-up too.
    (-Z / Y with bake_space_transform breaks the meshes under nested EMPTYs; -Z / Y without it leaves a 270 degrees X on the root and
    Z-up meshes, and Bake Axis Conversion does nothing then.) The .blend is written after the model is turned back."""
    fbx_path.parent.mkdir(parents=True, exist_ok=True)
    vl = bpy.context.view_layer
    for o in vl.objects: o.select_set(False)
    for o in objs: o.select_set(True)
    vl.objects.active = objs[0]
    turn_z180(objs)
    try:
        bpy.ops.export_scene.fbx(filepath=str(fbx_path), use_selection=True, object_types={'MESH', 'EMPTY'}, axis_forward='Y', axis_up='Z',
                                 apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS', global_scale=1.0, bake_space_transform=False,
                                 use_custom_props=True, path_mode='STRIP', embed_textures=False, bake_anim=False, add_leaf_bones=False,
                                 mesh_smooth_type='OFF', colors_type='SRGB', use_mesh_modifiers=True)
    finally:
        turn_z180(objs)
    bpy.data.libraries.write(str(blend_path), set(objs), fake_user=True, path_remap='RELATIVE_ALL', compress=True)


def run(only):
    stats_path = HERE / 'stats.json'
    stats = {s['model']: s for s in json.loads(stats_path.read_text(encoding='utf-8'))} if stats_path.exists() else {}
    if not only or 'statue' in only:
        s = build_statue(); stats[s['model']] = s; print('built KingStatue', s['triangles_total'], s['height_m'])
    if not only or 'phantom' in only:
        s = build_phantom(); stats[s['model']] = s; print('built GraveKeeper', s['triangles_total'], s['height_m'])
    stats_path.write_text(json.dumps(list(stats.values()), indent=2, ensure_ascii=False), encoding='utf-8')


if __name__ == '__main__':
    run(ARGS)
