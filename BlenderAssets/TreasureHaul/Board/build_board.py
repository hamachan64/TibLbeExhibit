"""Cargo board for Treasure Haul: the excavation crew's heavy electronic score board over the delivery bay.

A dull blue-green painted steel box (1.93 x 0.655 x 0.135 m outside, chamfered) hangs on two chains from a small gantry
(two square-tube posts behind the delivery bay machine, an H-beam cross bar, two outriggers that carry the chains, knee
braces, side braces, base plates). Front: a black screen recess (the 7-segment digits are a separate Unity mesh), a
hazard-striped top rail, a blank metal label plate (the label text is a Unity TMP), a small power lamp (own object /
material, Unity lights it), four corner bolts. Back: junction box with a cable gland, the power cable running over the
cross bar and down the right post to the floor, brass name plate, stiffener ribs, a vent.

Headless, nothing but files is written (all outputs are overwritten):
  blender -b --factory-startup --python build_board.py
Writes CargoBoard.blend / CargoBoard.fbx, textures/CargoBoard_{BaseColor,MetallicSmoothness}.png, stats.json

Conventions: metres, front = Blender -Y, up = +Z, origin = centre between the two post feet on the floor.
FBX axes as the enemies (#67, BlenderAssets/TreasureHaul/Enemies/build_enemies.py export() / turn_z180): written Z-up
(axis_up 'Z', forward 'Y', no bake_space_transform) with the model turned 180 deg about Z first; in Unity turn on
Bake Axis Conversion -> every node rotation 0, Y up, front +Z, local position = (-x, z, -y) of the .blend (to_unity()).
Placement: the model root goes to VenueRoot local ROOT_VENUE_UNITY; the screen centre then lands on the
ScoreBoardAnchor (0, 1.5, 0.32) of stage_layout.json.

Materials (3): TH_CargoBoard (one 1024 atlas: box, rail, plate, bolts, gantry, chains, cable), TH_CargoBoardScreen
(plain black, the recess floor), TH_CargoBoardLamp (the lamp lens, Unity drives the colour / emission).
Texture: smart-UV unwrap (box 1.5x the texel density of the gantry) -> Cycles bakes position / normal / ambient
occlusion / edge (bevel-node) / part id into float images -> numpy paints the steel (paint mottling, grime in the
hollows, rain streaks, chipped paint on the edges with bare steel and rust, rust drips under the bolts, dust on the
upward faces, worn hazard stripes, soil splashed on the gantry feet).
"""
import bpy, bmesh, json, math, sys
import numpy as np
from pathlib import Path
from mathutils import Vector, Matrix

HERE = Path(__file__).resolve().parent
TEX_N = 1024
TEX_DIR = HERE / 'textures'
ROOT_VENUE_UNITY = (0.0, 0.0, 0.09)     # VenueRoot local position of the model root (Unity)
SCREEN_VENUE_UNITY = (0.0, 1.5, 0.32)   # ScoreBoardAnchor (stage_layout.json)

# ---------------------------------------------------------------------------------------------- dimensions (Blender)
BOX_W, BOX_D = 1.90, 0.12
BOX_Z0, BOX_Z1 = 1.24, 1.83
BOX_YC = -0.19                     # box centre (front face y = -0.25, back face y = -0.13)
BOX_YF, BOX_YB = BOX_YC - BOX_D / 2, BOX_YC + BOX_D / 2
CAP_W, CAP_D, CAP_Z0, CAP_Z1 = 1.93, 0.135, 1.82, 1.895
OPEN_X, OPEN_Z0, OPEN_Z1 = 0.875, 1.295, 1.705     # screen opening in the front face (before the bezel inset)
BEZEL, RECESS = 0.012, 0.020
PLATE_C, PLATE_S = Vector((-0.705, BOX_YF - 0.003, 1.757)), (0.30, 0.006, 0.088)
LAMP_C = Vector((0.80, BOX_YF, 1.762))
BOLTS = [(sx * 0.902, z) for sx in (-1, 1) for z in (1.292, 1.785)]
HANG_X = 0.70
POST_X, POST_W = 1.25, 0.08
BEAM_Z0, BEAM_H, BEAM_W = 2.32, 0.12, 0.09
BEAM_X = 1.36
OUT_Z0, OUT_Z1 = 2.265, 2.32
EYE_R, EYE_r = 0.020, 0.0055
LINK_IN_L, LINK_IN_W, LINK_r = 0.046, 0.020, 0.005
N_LINKS = 6
EYE_NUT_H = 0.012

(P_PAINT, P_HAZARD, P_PLATE, P_GALV, P_FRAME, P_CHAIN, P_CABLE, P_DARK, P_BRASS, P_GREY, P_VENT) = range(11)

ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []


# ---------------------------------------------------------------------------------------------- small helpers
def frame(xaxis, up=(0, 0, 1)):
    x = Vector(xaxis).normalized(); u = Vector(up)
    if abs(x.dot(u)) > .95: u = Vector((1, 0, 0)) if abs(x.x) < .9 else Vector((0, 1, 0))
    y = u.cross(x).normalized(); z = x.cross(y)
    return Matrix((x, y, z)).transposed()


R_FRONT = Matrix(((1, 0, 0), (0, 0, -1), (0, 1, 0)))     # local z -> -Y (out of the front face), local y -> +Z
R_BACK = Matrix(((-1, 0, 0), (0, 0, 1), (0, 1, 0)))     # local z -> +Y (out of the back face)
R_UP = Matrix.Identity(3)


def to_unity(v): return [round(-v[0], 4), round(v[2], 4), round(-v[1], 4)]


def size_unity(s): return [round(abs(s[0]), 4), round(abs(s[2]), 4), round(abs(s[1]), 4)]


def venue(v):
    u = to_unity(v); return [round(u[i] + ROOT_VENUE_UNITY[i], 4) for i in range(3)]


class Builder:
    def __init__(self, name, sharp_deg=35.0):
        self.name = name; self.sharp = math.radians(sharp_deg)
        self.bm = bmesh.new()
        self.lp = self.bm.faces.layers.int.new('part')
        self.ls = self.bm.faces.layers.int.new('soft')

    def _tag(self, before, part, soft=False):
        new = [f for f in self.bm.faces if f not in before]
        for f in new: f[self.lp] = part; f[self.ls] = int(soft)
        bmesh.ops.recalc_face_normals(self.bm, faces=new)
        self.bm.normal_update()
        return new

    def box(self, c, size, part, bevel=0.0, R=None):
        bm = self.bm; before = set(bm.faces)
        vs = bmesh.ops.create_cube(bm, size=1.0)['verts']
        for v in vs: v.co = Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2]))
        if bevel > 0:
            es = list({e for v in vs for e in v.link_edges})
            bmesh.ops.bevel(bm, geom=vs + es, offset=bevel, segments=1, affect='EDGES', profile=.5, clamp_overlap=True)
        new = [f for f in bm.faces if f not in before]
        R = R or Matrix.Identity(3)
        for v in {v for f in new for v in f.verts}: v.co = Vector(c) + R @ v.co
        return self._tag(before, part)

    def beam(self, p0, p1, w, h, part, bevel=0.0, up=(0, 0, 1)):
        p0, p1 = Vector(p0), Vector(p1)
        return self.box((p0 + p1) / 2, ((p1 - p0).length, w, h), part, bevel, frame(p1 - p0, up))

    def prism(self, outline, x0, x1, part, c=(0, 0, 0), R=None):
        """outline: (y, z) points (counter-clockwise), extruded along local x from x0 to x1."""
        bm = self.bm; before = set(bm.faces); R = R or Matrix.Identity(3); c = Vector(c)
        a = [bm.verts.new(c + R @ Vector((x0, y, z))) for y, z in outline]
        b = [bm.verts.new(c + R @ Vector((x1, y, z))) for y, z in outline]
        n = len(outline)
        bm.faces.new(list(reversed(a))); bm.faces.new(b)
        for k in range(n):
            k2 = (k + 1) % n; bm.faces.new([a[k], a[k2], b[k2], b[k]])
        return self._tag(before, part)

    def lathe(self, profile, n, c, R, part, cap0=False, cap1=False, soft=False, rot=0.0):
        """profile: (r, h) from bottom to top, revolved about local z. r == 0 -> a single apex vertex."""
        bm = self.bm; before = set(bm.faces); c = Vector(c)
        rings = []
        for r, h in profile:
            if r <= 0: rings.append([bm.verts.new(c + R @ Vector((0, 0, h)))]); continue
            rings.append([bm.verts.new(c + R @ Vector((r * math.cos(rot + 2 * math.pi * k / n), r * math.sin(rot + 2 * math.pi * k / n), h)))
                          for k in range(n)])
        for i in range(len(rings) - 1):
            A, B = rings[i], rings[i + 1]
            for k in range(n):
                k2 = (k + 1) % n
                if len(A) == 1: bm.faces.new([A[0], B[k], B[k2]])
                elif len(B) == 1: bm.faces.new([A[k], A[k2], B[0]])
                else: bm.faces.new([A[k], A[k2], B[k2], B[k]])
        if cap0 and len(rings[0]) > 2: bm.faces.new(list(reversed(rings[0])))
        if cap1 and len(rings[-1]) > 2: bm.faces.new(rings[-1])
        return self._tag(before, part, soft)

    def sweep(self, centre, normals, binormals, r, nmin, part, closed=True, rot=0.0, caps=False):
        """Tube of radius r along the centre line (cross sections in the normal / binormal plane)."""
        bm = self.bm; before = set(bm.faces)
        rings = []
        for p, nn, bb in zip(centre, normals, binormals):
            rings.append([bm.verts.new(p + r * (math.cos(rot + 2 * math.pi * k / nmin) * nn + math.sin(rot + 2 * math.pi * k / nmin) * bb))
                          for k in range(nmin)])
        m = len(rings)
        for i in range(m if closed else m - 1):
            A, B = rings[i], rings[(i + 1) % m]
            for k in range(nmin):
                k2 = (k + 1) % nmin; bm.faces.new([A[k], A[k2], B[k2], B[k]])
        if caps and not closed:
            bm.faces.new(list(reversed(rings[0]))); bm.faces.new(rings[-1])
        return self._tag(before, part, soft=True)

    def link(self, c, inner_len, inner_w, r, axis_n, nmaj_half=4, nmin=3, part=P_CHAIN, rot=0.0):
        """Chain link / eye ring: a stadium centre line in the plane with normal axis_n, long axis = +Z."""
        c = Vector(c); n = Vector(axis_n).normalized(); zaxis = Vector((0, 0, 1)); u = zaxis.cross(n).normalized()
        rc = inner_w / 2 + r; ls = max(0.0, inner_len - inner_w) / 2
        pts, outs = [], []
        for half, s in ((0, 1), (1, -1)):
            for k in range(nmaj_half):
                a = math.pi * half + math.pi * k / (nmaj_half - 1)
                d = math.cos(a) * u + math.sin(a) * zaxis
                pts.append(c + s * ls * zaxis + rc * d); outs.append(d)
        # drop duplicated points where the straight part has zero length
        if ls < 1e-6:
            pts2, outs2 = [], []
            for p, o in zip(pts, outs):
                if not pts2 or (p - pts2[-1]).length > 1e-6: pts2.append(p); outs2.append(o)
            if (pts2[0] - pts2[-1]).length < 1e-6: pts2.pop(); outs2.pop()
            pts, outs = pts2, outs2
        return self.sweep(pts, outs, [n] * len(pts), r, nmin, part, closed=True, rot=rot)

    def finish(self, mat):
        bm = self.bm
        bmesh.ops.triangulate(bm, faces=list(bm.faces), quad_method='BEAUTY', ngon_method='BEAUTY')
        bm.normal_update()
        cnt = {}
        for f in bm.faces: cnt[f[self.lp]] = cnt.get(f[self.lp], 0) + 1
        print('TRIS', self.name, len(bm.faces), sorted(cnt.items()))
        for f in bm.faces: f.smooth = True
        for e in bm.edges:
            lf = e.link_faces
            if len(lf) != 2: e.smooth = False; continue
            soft = lf[0][self.ls] and lf[1][self.ls]
            e.smooth = bool(soft) or e.calc_face_angle(0.0) <= self.sharp
        mesh = bpy.data.meshes.new(self.name); bm.to_mesh(mesh); bm.free()
        mesh.materials.append(mat)
        obj = bpy.data.objects.new(self.name, mesh); bpy.context.scene.collection.objects.link(obj)
        mod = obj.modifiers.new('wn', 'WEIGHTED_NORMAL'); mod.mode = 'FACE_AREA'; mod.weight = 50; mod.keep_sharp = True
        dg = bpy.context.evaluated_depsgraph_get()
        baked = bpy.data.meshes.new_from_object(obj.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)
        obj.modifiers.clear(); old = obj.data; obj.data = baked; baked.name = self.name
        bpy.data.meshes.remove(old)
        if not baked.materials: baked.materials.append(mat)
        return obj


def tri_count(obj):
    obj.data.calc_loop_triangles(); return len(obj.data.loop_triangles)


# ---------------------------------------------------------------------------------------------- geometry
def hex_bolt(b, c, R, r=.016, h=.008, part=P_GALV, washer=0.0):
    if washer > 0:
        b.lathe([(washer, 0), (washer, .0025)], 6, c, R, part, cap1=True)
        c = Vector(c) + R @ Vector((0, 0, .0025))
    b.lathe([(r, 0), (r, h)], 6, c, R, part, cap1=True, rot=math.pi / 6)


def rivet(b, c, R, r=.0055, part=P_GALV):
    b.lathe([(r, 0), (0, .003)], 6, c, R, part)


def build_box_body(b):
    """The steel box: chamfered, a recessed screen opening (the recess floor is the separate screen object)."""
    bm = b.bm; before = set(bm.faces)
    vs = bmesh.ops.create_cube(bm, size=1.0)['verts']
    for v in vs: v.co = Vector((v.co.x * BOX_W, v.co.y * BOX_D, v.co.z * (BOX_Z1 - BOX_Z0))) + Vector((0, BOX_YC, (BOX_Z0 + BOX_Z1) / 2))
    es = list({e for v in vs for e in v.link_edges})
    bmesh.ops.bevel(bm, geom=vs + es, offset=.012, segments=1, affect='EDGES', profile=.5, clamp_overlap=True)
    bm.normal_update()
    body = [f for f in bm.faces if f not in before]

    def front_faces():
        return [f for f in bm.faces if f.normal.y < -.99 and abs(f.calc_center_median().y - BOX_YF) < 1e-4]
    for co, no in (((OPEN_X, 0, 0), (1, 0, 0)), ((-OPEN_X, 0, 0), (1, 0, 0)), ((0, 0, OPEN_Z0), (0, 0, 1)), ((0, 0, OPEN_Z1), (0, 0, 1))):
        ff = front_faces()
        geom = ff + list({e for f in ff for e in f.edges}) + list({v for f in ff for v in f.verts})
        bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-5, plane_co=co, plane_no=no)
    bm.normal_update()
    sf = next(f for f in front_faces() if abs(f.calc_center_median().x) < OPEN_X and OPEN_Z0 < f.calc_center_median().z < OPEN_Z1)
    ring = bmesh.ops.inset_region(bm, faces=[sf], thickness=BEZEL, depth=0.0, use_even_offset=True)['faces']
    ex = bmesh.ops.extrude_face_region(bm, geom=[sf])
    nv = [e for e in ex['geom'] if isinstance(e, bmesh.types.BMVert)]
    nf = [e for e in ex['geom'] if isinstance(e, bmesh.types.BMFace) and all(v in nv for v in e.verts)]
    if sf.is_valid and sf not in nf: bmesh.ops.delete(bm, geom=[sf], context='FACES_ONLY')    # the original face stays behind
    for v in nv: v.co.y += RECESS
    new = [f for f in bm.faces if f not in before]
    for f in new: f[b.lp] = P_PAINT; f[b.ls] = 0
    bmesh.ops.recalc_face_normals(bm, faces=new); bm.normal_update()
    floor = nf[0]
    scr = [Vector(v.co) for v in floor.verts]
    # bezel ring and the recess walls: dark
    walls = [f for f in bm.faces if f not in before and f is not floor and f.calc_center_median().y > BOX_YF - 1e-4 and
             f.calc_center_median().y < BOX_YF + RECESS + 1e-4 and abs(f.calc_center_median().x) < OPEN_X + 1e-3 and
             OPEN_Z0 - 1e-3 < f.calc_center_median().z < OPEN_Z1 + 1e-3]
    for f in walls + ring: f[b.lp] = P_DARK
    bmesh.ops.delete(bm, geom=[floor], context='FACES_ONLY')
    bm.normal_update()
    return scr


def build_board(b):
    scr = build_box_body(b)
    # hazard-striped top rail (a cap channel over the top edge); front-facing faces carry the stripes
    for f in b.box((0, BOX_YC, (CAP_Z0 + CAP_Z1) / 2), (CAP_W, CAP_D, CAP_Z1 - CAP_Z0), P_PAINT, bevel=.006):
        if f.normal.y < -.5: f[b.lp] = P_HAZARD
    # bottom rail and the two side channels: with the top rail they frame the front like a heavy equipment case
    b.box((0, BOX_YC, BOX_Z0 + .016), (CAP_W, CAP_D, .032), P_PAINT, bevel=.005)
    for sx in (-1, 1):
        b.box((sx * (BOX_W / 2 - .002), BOX_YC, (BOX_Z0 + .03 + CAP_Z0) / 2), (.034, CAP_D - .004, CAP_Z0 - BOX_Z0 - .03), P_PAINT, bevel=.005)
    # label plate (blank) with four rivets
    b.box(PLATE_C, PLATE_S, P_PLATE, bevel=.0022)
    pf = PLATE_C.y - PLATE_S[1] / 2
    for sx in (-1, 1):
        for sz in (-1, 1):
            rivet(b, (PLATE_C.x + sx * (PLATE_S[0] / 2 - .014), pf, PLATE_C.z + sz * (PLATE_S[2] / 2 - .013)), R_FRONT, r=.0048)
    # lamp bezel (the lens is the separate lamp object)
    b.lathe([(.027, 0), (.027, .005), (.0215, .0075), (.0185, .0055)], 10, LAMP_C, R_FRONT, P_DARK, rot=math.pi / 10)
    # corner bolts with washers
    for x, z in BOLTS: hex_bolt(b, (x, BOX_YF, z), R_FRONT, r=.0135, h=.008, washer=.019)
    # eye bolts on top: nut + ring (ring plane faces the viewer)
    eyes = []
    for sx in (-1, 1):
        c = Vector((sx * HANG_X, BOX_YC, CAP_Z1))
        b.lathe([(.024, 0), (.024, .006)], 6, c, R_UP, P_GALV, cap1=True)                         # weld pad
        hex_bolt(b, c + Vector((0, 0, .006)), R_UP, r=.0135, h=EYE_NUT_H - .006)
        ec = c + Vector((0, 0, EYE_NUT_H + EYE_r + EYE_R - .002))
        b.link(ec, 2 * (EYE_R - EYE_r), 2 * (EYE_R - EYE_r), EYE_r, (0, 1, 0), nmaj_half=5, nmin=3, part=P_GALV)
        eyes.append(ec)
    # back: stiffener ribs, junction box + gland, brass name plate, vent
    for z in (1.315, 1.765):
        b.box((0, BOX_YB + .011, z), (1.70, .022, .030), P_PAINT, bevel=.003)
    jb = Vector((0.45, BOX_YB + .028, 1.555))
    b.box(jb, (.24, .056, .17), P_GREY, bevel=.005)
    b.box(jb + Vector((0, .029, 0)), (.20, .004, .13), P_GREY, bevel=.0015)                       # lid
    for sx in (-1, 1):
        for sz in (-1, 1):
            hex_bolt(b, jb + Vector((sx * .088, .031, sz * .053)), R_BACK, r=.0065, h=.004)
    gland = jb + Vector((0, 0, .085))
    b.lathe([(.016, 0), (.016, .012), (.011, .014), (.011, .028)], 6, gland, R_UP, P_GALV, cap1=True, rot=math.pi / 6)
    npc = Vector((-0.45, BOX_YB + .0015, 1.585))
    b.box(npc, (.17, .003, .075), P_BRASS, bevel=.001)
    for sx in (-1, 1):
        for sz in (-1, 1):
            rivet(b, npc + Vector((sx * .072, .0015, sz * .026)), R_BACK, r=.004, part=P_BRASS)
    b.box((0, BOX_YB + .006, 1.555), (.30, .012, .11), P_VENT, bevel=.003)
    return scr, eyes, jb, gland


def build_rig(b, eyes, gland):
    # posts, base plates, anchors, gussets, side braces with feet
    for sx in (-1, 1):
        x = sx * POST_X
        b.box((x, 0, (0.012 + BEAM_Z0 - .012) / 2), (POST_W, POST_W, BEAM_Z0 - .012 - .012), P_FRAME, bevel=.006)
        b.box((x, 0, .006), (.34, .16, .012), P_FRAME, bevel=.004)
        for ax in (-.12, .12):
            for ay in (-.05, .05):
                hex_bolt(b, (x + ax, ay, .012), R_UP, r=.0115, h=.010)
        for gx in (-1, 1):      # triangular gussets on both x faces of the post foot
            b.prism([(0, .012), (.10, .012), (0, .13)], -.004, .004, P_FRAME, c=(x + gx * POST_W / 2, 0, 0),
                    R=Matrix(((0, gx, 0), (1, 0, 0), (0, 0, 1))))
        b.box((x, 0, BEAM_Z0 - .006), (.14, .11, .012), P_FRAME)                                   # head plate
        b.beam((x - sx * POST_W / 2, 0, 1.97), (sx * (POST_X - .33), 0, BEAM_Z0), .034, .034, P_FRAME, up=(0, 1, 0))
        foot = Vector((sx * 1.575, 0, .012))
        b.box((foot.x, 0, .006), (.10, .09, .012), P_FRAME)
        b.beam((x + sx * POST_W / 2, 0, .62), foot + Vector((0, 0, .01)), .034, .034, P_FRAME, up=(0, 1, 0))
    # H-beam cross bar
    tf, tw, hw = .011, .008, BEAM_W / 2
    H = [(-hw, 0), (hw, 0), (hw, tf), (tw / 2, tf), (tw / 2, BEAM_H - tf), (hw, BEAM_H - tf), (hw, BEAM_H), (-hw, BEAM_H),
         (-hw, BEAM_H - tf), (-tw / 2, BEAM_H - tf), (-tw / 2, tf), (-hw, tf)]
    b.prism(H, -BEAM_X, BEAM_X, P_FRAME, c=(0, 0, BEAM_Z0))
    for sx in (-1, 1):
        b.box((sx * (BEAM_X + .005), 0, BEAM_Z0 + BEAM_H / 2), (.01, BEAM_W + .01, BEAM_H + .01), P_FRAME)
    # outriggers under the bottom flange, a tab and a pad eye at the hanging point
    hangs = []
    for sx in (-1, 1):
        x = sx * HANG_X
        b.box((x, (-.245 + .05) / 2, (OUT_Z0 + OUT_Z1) / 2), (.05, .295, OUT_Z1 - OUT_Z0), P_FRAME, bevel=.004)
        b.box((x, .0, OUT_Z1 - .003), (.09, BEAM_W + .02, .006), P_FRAME)                          # clamp plate
        for by in (-1, 1):
            hex_bolt(b, (x + by * .036, by * .052, OUT_Z1), R_UP, r=.008, h=.006)
        hangs.append(Vector((x, BOX_YC, OUT_Z0)))
    # chains: eye on the box -> N links -> pad eye under the outrigger
    chain_info = []
    for ec, top in zip(eyes, hangs):
        c0 = ec.z + EYE_R + EYE_r + LINK_IN_L / 2
        centres = []
        for k in range(N_LINKS):
            cz = c0 + k * LINK_IN_L
            axis = (1, 0, 0) if k % 2 == 0 else (0, 1, 0)
            twist = .06 * math.sin(k * 1.7 + ec.x * 3)
            b.link((ec.x + .0015 * math.sin(k * 2.1), ec.y, cz), LINK_IN_L, LINK_IN_W, LINK_r, axis, nmaj_half=4, nmin=3, rot=twist)
            centres.append(cz)
        pad_c = centres[-1] + LINK_IN_L / 2 + EYE_R - EYE_r
        last_axis = (1, 0, 0) if (N_LINKS - 1) % 2 == 0 else (0, 1, 0)
        pad_axis = (0, 1, 0) if last_axis == (1, 0, 0) else (1, 0, 0)
        b.link((ec.x, ec.y, pad_c), 2 * (EYE_R - EYE_r), 2 * (EYE_R - EYE_r), EYE_r, pad_axis, nmaj_half=5, nmin=3, part=P_GALV)
        tab_z0 = pad_c + EYE_R * .5
        tab_size = (.044, .010, OUT_Z0 - tab_z0) if pad_axis == (0, 1, 0) else (.010, .044, OUT_Z0 - tab_z0)
        b.box((ec.x, ec.y, (tab_z0 + OUT_Z0) / 2), tab_size, P_FRAME)
        chain_info.append(dict(bottom_eye=ec, top_eye_z=pad_c, link_centres=centres))
    # power cable: junction box gland -> up behind the box -> under the cross bar -> down the right post -> floor
    ctrl = [gland + Vector((0, 0, .028)), gland + Vector((0, 0, .09)), Vector((gland.x, BOX_YB + .052, CAP_Z1 + .06)),
            Vector((gland.x + .02, BOX_YB + .10, 2.18)), Vector((gland.x + .12, -.02, BEAM_Z0 - .016)),
            Vector((.80, .0, BEAM_Z0 - .045)), Vector((1.03, .0, BEAM_Z0 - .014)), Vector((1.17, .046, 2.20)),
            Vector((POST_X, .054, 2.02)), Vector((POST_X, .054, 1.40)), Vector((POST_X, .054, .70)), Vector((POST_X, .056, .16)),
            Vector((POST_X + .02, .068, .045)), Vector((POST_X + .12, .072, .021)), Vector((POST_X + .25, .072, .0205)),
            Vector((POST_X + .36, .066, .0086)), Vector((POST_X + .65, .058, .0086)), Vector((2.35, .066, .0086))]
    pts = catmull(ctrl, 4)
    cable_r = .0085
    pts = [p if p.z > .5 else Vector((p.x, p.y, max(p.z, cable_r))) for p in pts]
    pts = simplify(pts, .006)
    T = [(pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized() for i in range(len(pts))]
    Nn, Bn = transport(T)
    b.sweep(pts, Nn, Bn, cable_r, 5, P_CABLE, closed=False, caps=True)
    for z in (1.62, .95):
        b.box((POST_X, .047, z), (.092, .018, .022), P_GALV)                                      # cable clamps
    return chain_info, hangs, pts


def catmull(P, per):
    out = []
    P = [P[0]] + P + [P[-1]]
    for i in range(1, len(P) - 2):
        p0, p1, p2, p3 = P[i - 1], P[i], P[i + 1], P[i + 2]
        for k in range(per):
            t = k / per; t2 = t * t; t3 = t2 * t
            out.append(.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3))
    out.append(P[-2])
    return out


def simplify(pts, tol):
    """Drop points that are nearly on the straight line of their neighbours (keeps straight runs cheap)."""
    out = [pts[0]]
    for i in range(1, len(pts) - 1):
        a, p, c = out[-1], pts[i], pts[i + 1]
        d = (c - a); L = d.length
        if L < 1e-6: continue
        dist = (p - a).cross(d).length / L
        if dist > tol or (p - a).length > .45: out.append(p)
    out.append(pts[-1])
    return out


def transport(T):
    n = T[0].cross(Vector((0, 0, 1)))
    if n.length < 1e-3: n = T[0].cross(Vector((1, 0, 0)))
    n.normalize(); N, B = [], []
    for i, t in enumerate(T):
        if i > 0:
            ax = T[i - 1].cross(t)
            if ax.length > 1e-6:
                ang = math.asin(min(1.0, ax.length)); n = Matrix.Rotation(ang, 3, ax.normalized()) @ n
        n = (n - t * n.dot(t)).normalized(); N.append(n.copy()); B.append(t.cross(n))
    return N, B


def build_screen(scr, mat):
    xs = [v.x for v in scr]; zs = [v.z for v in scr]; y = scr[0].y
    x0, x1, z0, z1 = min(xs), max(xs), min(zs), max(zs)
    c = Vector(((x0 + x1) / 2, y, (z0 + z1) / 2))
    me = bpy.data.meshes.new('CargoBoard_Screen')
    vv = [(x0 - c.x, 0, z0 - c.z), (x1 - c.x, 0, z0 - c.z), (x1 - c.x, 0, z1 - c.z), (x0 - c.x, 0, z1 - c.z)]
    me.from_pydata(vv, [], [(0, 1, 2, 3)])
    me.update()
    if me.polygons[0].normal.y > 0: me.polygons[0].flip(); me.update()
    uv = me.uv_layers.new(name='UVMap')
    for li, (u, v) in zip(range(4), ((0, 0), (1, 0), (1, 1), (0, 1))): uv.data[li].uv = (u, v)
    me.materials.append(mat)
    o = bpy.data.objects.new('CargoBoard_Screen', me); bpy.context.scene.collection.objects.link(o)
    o.location = c
    return o, (x1 - x0, z1 - z0)


def build_lamp(mat):
    b = Builder('CargoBoard_Lamp', sharp_deg=80)
    b.lathe([(.019, 0), (.0185, .006), (.015, .0115), (.008, .0148), (0, .0158)], 10, Vector(), R_FRONT, 0, cap0=True, soft=True,
            rot=math.pi / 10)
    o = b.finish(mat); o.location = LAMP_C
    o.data.uv_layers.new(name='UVMap')
    return o


# ---------------------------------------------------------------------------------------------- UV, bake, paint
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


def unwrap(objs, scales):
    sc = bpy.context.scene; vl = bpy.context.view_layer
    sc.tool_settings.use_uv_select_sync = True
    for o, s in zip(objs, scales):
        for x in vl.objects: x.select_set(False)
        o.data.uv_layers.new(name='UVMap')
        o.select_set(True); vl.objects.active = o
        bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.uv.smart_project(angle_limit=math.radians(50), island_margin=.003, area_weight=0.0, correct_aspect=True, scale_to_bounds=False)
        bpy.ops.object.mode_set(mode='OBJECT')
        uv = np.empty(len(o.data.loops) * 2, np.float32); o.data.uv_layers[0].data.foreach_get('uv', uv); o.data.uv_layers[0].data.foreach_set('uv', uv * s)
    for x in vl.objects: x.select_set(False)
    for o in objs: o.select_set(True)
    vl.objects.active = objs[0]
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.pack_islands(rotate=True, margin=.004)
    bpy.ops.object.mode_set(mode='OBJECT')


def bake_passes(objs, mat):
    setup_cycles(1)
    sc = bpy.context.scene; vl = bpy.context.view_layer
    floor = bpy.data.objects.new('bake_floor', bpy.data.meshes.new('bake_floor'))
    fb = bmesh.new(); bmesh.ops.create_grid(fb, x_segments=1, y_segments=1, size=5.0); fb.to_mesh(floor.data); fb.free()
    sc.collection.objects.link(floor)
    nt = mat.node_tree; nodes = nt.nodes; links = nt.links
    keep = set(n.name for n in nodes)
    out = next(n for n in nodes if n.type == 'OUTPUT_MATERIAL')
    em = nodes.new('ShaderNodeEmission'); img_node = nodes.new('ShaderNodeTexImage')
    geo = nodes.new('ShaderNodeNewGeometry')
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
        bpy.ops.object.bake(type='EMIT', margin=6, margin_type='EXTEND', use_clear=True)
        buf = np.empty(TEX_N * TEX_N * 4, np.float32); im.pixels.foreach_get(buf)
        res[name] = buf.reshape(TEX_N, TEX_N, 4); bpy.data.images.remove(im)
    mad = nodes.new('ShaderNodeVectorMath'); mad.operation = 'MULTIPLY_ADD'
    mad.inputs[1].default_value = (.2, .2, .2); mad.inputs[2].default_value = (.5, .5, .2)
    links.new(geo.outputs['Position'], mad.inputs[0]); bake('pos', mad.outputs[0], 1)
    mad2 = nodes.new('ShaderNodeVectorMath'); mad2.operation = 'MULTIPLY_ADD'
    mad2.inputs[1].default_value = (.5, .5, .5); mad2.inputs[2].default_value = (.5, .5, .5)
    links.new(geo.outputs['Normal'], mad2.inputs[0]); bake('nrm', mad2.outputs[0], 1)
    ao1 = nodes.new('ShaderNodeAmbientOcclusion'); ao1.samples = 16; ao1.inputs['Distance'].default_value = .35
    ao2 = nodes.new('ShaderNodeAmbientOcclusion'); ao2.samples = 16; ao2.inputs['Distance'].default_value = .045
    bev = nodes.new('ShaderNodeBevel'); bev.samples = 8; bev.inputs['Radius'].default_value = .006
    dot = nodes.new('ShaderNodeVectorMath'); dot.operation = 'DOT_PRODUCT'
    links.new(bev.outputs['Normal'], dot.inputs[0]); links.new(geo.outputs['Normal'], dot.inputs[1])
    sub = nodes.new('ShaderNodeMath'); sub.operation = 'SUBTRACT'; sub.inputs[0].default_value = 1.0
    links.new(dot.outputs['Value'], sub.inputs[1])
    comb = nodes.new('ShaderNodeCombineXYZ')
    links.new(ao1.outputs['AO'], comb.inputs[0]); links.new(ao2.outputs['AO'], comb.inputs[1]); links.new(sub.outputs[0], comb.inputs[2])
    bake('occ', comb.outputs[0], 64)
    at = nodes.new('ShaderNodeAttribute'); at.attribute_name = 'part'; at.attribute_type = 'GEOMETRY'
    dv = nodes.new('ShaderNodeMath'); dv.operation = 'MULTIPLY'; dv.inputs[1].default_value = 1 / 16
    links.new(at.outputs['Fac'], dv.inputs[0])
    comb2 = nodes.new('ShaderNodeCombineXYZ'); links.new(dv.outputs[0], comb2.inputs[0])
    bake('part', comb2.outputs[0], 1)
    for n in list(nodes):
        if n.name not in keep: nodes.remove(n)
    if old_link: links.new(old_link, out.inputs['Surface'])
    bpy.data.objects.remove(floor, do_unlink=True)
    return res


_TAB = np.random.default_rng(1250).random(1 << 16).astype(np.float32)


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


def sst(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t)


def mix(col, c, w):
    w = np.clip(w, 0, 1)[:, None]; return col * (1 - w) + np.asarray(c, np.float32) * w


def paint(res):
    pos, nrm, occ, part = res['pos'], res['nrm'], res['occ'], res['part']
    valid = pos[..., 3] > .5
    idx = np.nonzero(valid.ravel())[0]
    P = ((pos[..., :3].reshape(-1, 3)[idx] - np.array((.5, .5, .2))) / .2).astype(np.float64)
    Nn = nrm[..., :3].reshape(-1, 3)[idx] * 2 - 1
    Nn /= np.maximum(np.linalg.norm(Nn, axis=1, keepdims=True), 1e-6)
    aoW = np.clip(occ[..., 0].ravel()[idx], 0, 1); aoT = np.clip(occ[..., 1].ravel()[idx], 0, 1)
    edge = np.clip(occ[..., 2].ravel()[idx] * 9.0, 0, 1)
    pid = np.rint(part[..., 0].ravel()[idx] * 16).astype(int)
    x, y, z = P[:, 0], P[:, 1], P[:, 2]; nz = Nn[:, 2]; ny = Nn[:, 1]
    vert = np.clip(1 - np.abs(nz) * 1.4, 0, 1); up = np.clip((nz - .45) / .4, 0, 1)
    n = len(idx)
    on = lambda *ks: np.isin(pid, ks).astype(np.float32)

    base = {P_PAINT: (.150, .200, .190), P_HAZARD: (.150, .200, .190), P_PLATE: (.40, .395, .36), P_GALV: (.27, .27, .26),
            P_FRAME: (.072, .080, .080), P_CHAIN: (.17, .165, .155), P_CABLE: (.022, .022, .022), P_DARK: (.028, .030, .031),
            P_BRASS: (.30, .215, .095), P_GREY: (.165, .175, .170), P_VENT: (.165, .175, .170)}
    col = np.zeros((n, 3), np.float32)
    for k, c in base.items(): col[pid == k] = c
    metal = np.zeros(n, np.float32); smooth = np.full(n, .30, np.float32)
    for k, (m, s) in {P_PLATE: (.85, .50), P_GALV: (.85, .42), P_CHAIN: (.80, .36), P_CABLE: (0, .38), P_BRASS: (.9, .45),
                      P_DARK: (0, .40)}.items():
        metal[pid == k] = m; smooth[pid == k] = s

    paintable = on(P_PAINT, P_HAZARD, P_FRAME, P_GREY, P_VENT, P_DARK)
    wearable = on(P_PAINT, P_HAZARD, P_FRAME, P_GREY, P_VENT)
    mott = fbm3(P * 2.6, 4, 3.1); mid = fbm3(P * 9.0, 3, 11.7); fine = vnoise3(P * 140.0); fine2 = vnoise3(P * 45.0 + 5.5)
    col *= ((.84 + .32 * mott) * (.94 + .12 * mid) * (.96 + .08 * fine))[:, None]
    # hazard stripes (45 deg, on the front of the top rail): yellow / black, the yellow worn and dirty
    s = np.mod((x - z) / .11, 1.0)
    yel = sst(-.035, .035, .25 - np.abs(s - .5))          # anti-aliased (about one texel)
    aa = np.clip(np.minimum(np.abs(s - .25), np.abs(s - .75)) / .02, 0, 1)
    haz = on(P_HAZARD)
    hcol = (np.array((.60, .385, .045)) * yel[:, None] + np.array((.028, .028, .027)) * (1 - yel[:, None])) * (.85 + .3 * mott)[:, None]
    hcol = hcol * (.75 + .25 * aa)[:, None] + (1 - aa)[:, None] * np.array((.20, .14, .03)) * .25
    col = mix(col, hcol, haz)
    # vent slots (dark horizontal lines on the vent panel)
    slot = (np.abs(np.mod((z - 1.555) / .018 + .5, 1.0) - .5) < .22).astype(np.float32) * (np.abs(x) < .135) * (np.abs(z - 1.555) < .045)
    col = mix(col, (.012, .012, .012), on(P_VENT) * slot * (ny > .5))
    # ambient occlusion and grime in the hollows
    col *= ((.30 + .70 * aoW ** 1.25) * (.50 + .50 * aoT ** 1.4))[:, None]
    grime = np.clip((1 - aoT) * 1.6, 0, 1) * (.6 + .4 * mid)
    col = mix(col, (.045, .040, .032), .55 * grime * paintable)
    # rain streaks running down the vertical faces, darker under the top rail and the outriggers
    st = fbm3(P * np.array([30.0, 30.0, 1.6]), 3, 21.0); st2 = np.clip((st - .46) * 4.0, 0, 1)
    streak_region = .45 + .55 * np.clip((fbm3(P * np.array([3.0, 3.0, .8]), 2, 31.0) - .35) * 2.5, 0, 1)
    col *= (1 - .24 * st2 * vert * paintable * streak_region)[:, None]
    under_rail = sst(.16, .0, CAP_Z0 - z) * (z < CAP_Z0) * (z > BOX_Z0) * (ny < -.5)
    col *= (1 - .25 * under_rail * (.4 + .6 * st2))[:, None]
    # rust drips under the front bolts and the plate rivets
    drip = np.zeros(n, np.float32)
    for bx, bz in BOLTS + [(PLATE_C.x + sx * .136, PLATE_C.z - .031) for sx in (-1, 1)]:
        dx = np.abs(x - bx - .004 * np.sin(z * 90)); dzv = bz - z
        w = np.clip(1 - dx / (.006 + .010 * np.clip(dzv / .12, 0, 1)), 0, 1) * sst(-.005, .01, dzv) * sst(.16 + .08 * fine2, .0, dzv)
        drip = np.maximum(drip, w * (ny < -.5))
    col = mix(col, (.20, .075, .025), .55 * drip * (.6 + .4 * st))
    # chipped paint: edges and a few knocks on the faces -> bare steel, rust inside the bigger chips
    chipn = fbm3(P * 22.0, 3, 5.0)
    knock = np.clip((vnoise3(P * 11.0 + 3.3) - .86) * 7, 0, 1) * np.clip((fbm3(P * 40, 2, 9.0) - .45) * 4, 0, 1)
    low = sst(.6, .0, z)
    wear = np.clip(edge * (1.0 + .8 * low) * (.35 + 1.1 * chipn) - .42, 0, 1) * 2.4 + knock * .9
    wear = np.clip(wear, 0, 1) * wearable
    # the stripes on the rail: scraped flecks (fine), more along the top edge and towards the ends, and a few scratches
    fleck = np.clip((fbm3(P * 48.0, 3, 77.0) - .62) * 5.0, 0, 1) * (.35 + .9 * sst(CAP_Z1 - .03, CAP_Z1, z) + .6 * sst(.75, .97, np.abs(x)))
    scratch = np.clip((np.abs(np.sin((x * 1.7 - z * .35) * 260 + 4 * fine2)) - .985) * 60, 0, 1) * np.clip((fbm3(P * 6, 2, 3.0) - .45) * 4, 0, 1)
    wear = np.maximum(wear, haz * np.clip(fleck + .8 * scratch, 0, 1))
    # hazard marking on the post feet (as on the bay machine): stripes from 8 cm to 40 cm, worn
    post = on(P_FRAME) * (np.abs(np.abs(x) - POST_X) < POST_W / 2 + .002) * (np.abs(y) < POST_W / 2 + .002) * (z > .08) * (z < .40) * vert
    sp = np.mod((z + (x + y) * .9) / .09, 1.0)
    py = sst(-.07, .07, .25 - np.abs(sp - .5))[:, None]
    pc = (np.array((.56, .36, .045)) * py + np.array((.03, .03, .028)) * (1 - py)) * (.8 + .35 * mott)[:, None]
    col = mix(col, pc * ((.30 + .70 * aoW ** 1.25) * (.5 + .5 * aoT ** 1.4))[:, None], post * .95)
    haz = np.maximum(haz, post)
    rustn = fbm3(P * 14.0, 3, 41.0)
    bare = np.array((.30, .29, .27)) * (.8 + .4 * fine2)[:, None]
    rustc = np.where((rustn > .5)[:, None], np.array((.21, .085, .03)), np.array((.13, .055, .025))) * (.8 + .4 * fine)[:, None]
    rusty = np.clip((rustn - .38) * 3.0 + low * .6, 0, 1)
    chipcol = bare * (1 - rusty[:, None]) + rustc * rusty[:, None]
    col = mix(col, chipcol, wear)
    # a thin dark rim around each chip (the paint edge)
    rim = np.clip(wear * (1 - wear) * 4, 0, 1) * .35
    col *= (1 - rim)[:, None]
    metal = metal * (1 - wear) + wear * (.75 * (1 - rusty))
    smooth = smooth * (1 - wear) + wear * (.48 * (1 - rusty) + .12 * rusty)
    # rust bloom on the bare metal parts (chain, eyes, bolts) and on the low gantry
    rb = np.clip((fbm3(P * 18.0, 3, 13.0) - .5) * 3, 0, 1) * on(P_CHAIN, P_GALV) * (.5 + .5 * (1 - aoT))
    col = mix(col, (.19, .075, .028), .75 * rb)
    metal *= (1 - .8 * rb); smooth = smooth * (1 - rb) + .15 * rb
    soil = sst(.45, .0, z) * (.4 + .6 * fbm3(P * 6.0, 3, 15.0)) * on(P_FRAME, P_GALV, P_CABLE)
    col = mix(col, (.13, .105, .08), .6 * soil)
    smooth *= (1 - .6 * soil)
    # dust on the upward faces, a little everywhere
    dust = up * (.35 + .65 * fbm3(P * 7.0, 3, 61.0)) * (.75 + .25 * (1 - aoT)) + .10 * (1 - aoW)
    col = mix(col, (.30, .28, .245), .45 * np.clip(dust, 0, 1) * (1 - on(P_DARK) * .5))
    smooth *= (1 - .55 * np.clip(dust, 0, 1))
    # scuffs and fine scratches on the plate (but keep it light and plain for the TMP label)
    scr = np.clip((np.abs(np.sin((x * 1.3 + z * .4) * 900 + fine2 * 6)) - .97) * 30, 0, 1) * on(P_PLATE)
    col = mix(col, (.55, .54, .50), .25 * scr)
    # tarnish on the brass plate and the cable is a little glossy where it bends
    col = mix(col, (.12, .10, .05), on(P_BRASS) * .45 * np.clip((1 - aoT) * 2 + (mott - .5), 0, 1))
    # bright edge scuff (worn but painted)
    col += (.04 * edge * paintable * (1 - wear) * (.5 + .5 * fine))[:, None]
    col = np.clip(col, 0, 1)
    smooth = np.clip(smooth + .04 * (fine - .5), .05, .85); metal = np.clip(metal, 0, 1)

    B = np.zeros((TEX_N * TEX_N, 4), np.float32); B[:, 3] = 1; B[:, :3] = (.12, .15, .145)
    B[idx, :3] = col
    M = np.zeros((TEX_N * TEX_N, 4), np.float32); M[:, 3] = .3
    M[idx, 0] = metal; M[idx, 1] = metal; M[idx, 2] = metal; M[idx, 3] = smooth
    shp = (TEX_N, TEX_N, 4)
    return B.reshape(shp), M.reshape(shp)


def save_png(path, rgba, name, non_color=False):
    H, W = rgba.shape[:2]
    im = bpy.data.images.new(name, width=W, height=H, alpha=True)
    if non_color: im.colorspace_settings.name = 'Non-Color'
    im.pixels.foreach_set(np.ascontiguousarray(rgba, np.float32).ravel())
    im.filepath_raw = str(path); im.file_format = 'PNG'
    path.parent.mkdir(parents=True, exist_ok=True); im.save()
    return im


# ---------------------------------------------------------------------------------------------- export (same as #67)
def turn_z180(objs):
    seen = set()
    for o in objs:
        if o.rotation_euler.to_quaternion().angle > 1e-6 or any(abs(v - 1.0) > 1e-6 for v in o.scale):
            raise RuntimeError('%s has a rotation or scale; the FBX axis trick needs identity nodes' % o.name)
        o.location = Vector((-o.location.x, -o.location.y, o.location.z))
        if o.type == 'MESH' and o.data.name not in seen:
            seen.add(o.data.name); o.data.transform(Matrix.Rotation(math.pi, 4, 'Z')); o.data.update()
    bpy.context.view_layer.update()


def export(objs, fbx_path, blend_path):
    """Same FBX axes as the enemies (#67): Z-up file, model turned 180 deg about Z while writing; Unity: Bake Axis Conversion ON."""
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


# ---------------------------------------------------------------------------------------------- main
def make_materials():
    mat = bpy.data.materials.new('TH_CargoBoard'); mat.use_nodes = True
    scr = bpy.data.materials.new('TH_CargoBoardScreen'); scr.use_nodes = True
    bs = next(n for n in scr.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    bs.inputs['Base Color'].default_value = (.004, .0045, .0045, 1); bs.inputs['Roughness'].default_value = .62
    bs.inputs['Specular IOR Level'].default_value = .25
    lamp = bpy.data.materials.new('TH_CargoBoardLamp'); lamp.use_nodes = True
    bl = next(n for n in lamp.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    bl.inputs['Base Color'].default_value = (.06, .30, .10, 1); bl.inputs['Roughness'].default_value = .2
    bl.inputs['Emission Color'].default_value = (.12, 1.0, .25, 1); bl.inputs['Emission Strength'].default_value = 1.6
    lamp['unity_note'] = 'CargoBoardView drives _BaseColor / _EmissionColor (green when powered, flashes on a delivery)'
    scr['unity_note'] = 'plain black; the 7-segment mesh sits a few mm in front of it'
    return mat, scr, lamp


def run():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    mat, scr_mat, lamp_mat = make_materials()
    bb = Builder('CargoBoard_Box'); scr, eyes, jb, gland = build_board(bb)
    rb = Builder('CargoBoard_Rig'); chains, hangs, cable = build_rig(rb, eyes, gland)
    box = bb.finish(mat); rig = rb.finish(mat)
    screen, scr_size = build_screen(scr, scr_mat)
    lamp = build_lamp(lamp_mat)
    unwrap([box, rig], [1.5, 1.0])
    res = bake_passes([box, rig], mat)
    base, ms = paint(res)
    im_b = save_png(TEX_DIR / 'CargoBoard_BaseColor.png', base, 'CargoBoard_BaseColor')
    im_m = save_png(TEX_DIR / 'CargoBoard_MetallicSmoothness.png', ms, 'CargoBoard_MetallicSmoothness', non_color=True)
    im_m.alpha_mode = 'CHANNEL_PACKED'
    nt = mat.node_tree; bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    t1 = nt.nodes.new('ShaderNodeTexImage'); t1.image = im_b; nt.links.new(t1.outputs['Color'], bsdf.inputs['Base Color'])
    t2 = nt.nodes.new('ShaderNodeTexImage'); t2.image = im_m
    sep = nt.nodes.new('ShaderNodeSeparateColor'); nt.links.new(t2.outputs['Color'], sep.inputs[0])
    nt.links.new(sep.outputs[0], bsdf.inputs['Metallic'])
    inv = nt.nodes.new('ShaderNodeMath'); inv.operation = 'SUBTRACT'; inv.inputs[0].default_value = 1.0
    nt.links.new(t2.outputs['Alpha'], inv.inputs[1]); nt.links.new(inv.outputs[0], bsdf.inputs['Roughness'])
    # join box + rig into one mesh (one draw call in Unity)
    tris_parts = {'box': tri_count(box), 'gantry_chains_cable': tri_count(rig)}
    vl = bpy.context.view_layer
    for o in vl.objects: o.select_set(False)
    box.select_set(True); rig.select_set(True); vl.objects.active = box
    bpy.ops.object.join()
    body = box; body.name = 'CargoBoard_Body'; body.data.name = 'CargoBoard_Body'
    for a in [a.name for a in body.data.attributes if a.name in ('part', 'soft')]: body.data.attributes.remove(body.data.attributes[a])
    # hierarchy and markers
    root = bpy.data.objects.new('CargoBoard_Root', None); root.empty_display_size = .3; sc.collection.objects.link(root)
    scr_c = Vector(screen.location)
    digits = bpy.data.objects.new('CargoBoard_DigitsAnchor', None); digits.empty_display_type = 'PLAIN_AXES'; digits.empty_display_size = .1
    label = bpy.data.objects.new('CargoBoard_LabelAnchor', None); label.empty_display_type = 'PLAIN_AXES'; label.empty_display_size = .05
    for o in (digits, label): sc.collection.objects.link(o)
    DIGIT_GAP = .004
    digits.location = scr_c + Vector((0, -DIGIT_GAP, 0))
    label_face = PLATE_C.y - PLATE_S[1] / 2
    label.location = Vector((PLATE_C.x, label_face - .001, PLATE_C.z))
    for o in (body, screen, lamp, digits, label): o.parent = root
    root['front'] = '-Y (Blender) / +Z (Unity)'; root['origin'] = 'floor, centre between the two post feet'
    root['venue_position_unity'] = str(ROOT_VENUE_UNITY)
    objs = [root, body, screen, lamp, digits, label]
    vl.update()
    stats = make_stats(root, body, screen, lamp, scr_size, scr_c, digits, label, tris_parts, chains, hangs)
    export(objs, HERE / 'CargoBoard.fbx', HERE / 'CargoBoard.blend')
    (HERE / 'stats.json').write_text(json.dumps(stats, indent=2, ensure_ascii=False), encoding='utf-8')
    print('built CargoBoard', stats['triangles_total'])


def make_stats(root, body, screen, lamp, scr_size, scr_c, digits, label, tris_parts, chains, hangs):
    def bounds(objs):
        pts = [o.matrix_world @ v.co for o in objs for v in o.data.vertices]
        return Vector([min(p[i] for p in pts) for i in range(3)]), Vector([max(p[i] for p in pts) for i in range(3)])
    lo, hi = bounds([body, screen, lamp])
    tris = {o.name: tri_count(o) for o in (body, screen, lamp)}
    box_lo = Vector((-CAP_W / 2, BOX_YC - CAP_D / 2, BOX_Z0)); box_hi = Vector((CAP_W / 2, BOX_YC + CAP_D / 2, CAP_Z1))
    digits_w, digits_h = 1.60, 0.30
    off = to_unity(scr_c)
    tgt_ok = [round(off[i] + ROOT_VENUE_UNITY[i], 4) for i in range(3)] == [round(v, 4) for v in SCREEN_VENUE_UNITY]
    s = {
        'model': 'CargoBoard',
        'units': 'm; *_unity = model local (FBX root, Bake Axis Conversion ON, front +Z); *_venue = VenueRoot local',
        'root_venue_unity': list(ROOT_VENUE_UNITY),
        'root_origin': 'floor (y=0), centre between the two post feet',
        'overall_size_unity': size_unity(hi - lo),
        'overall_bounds_unity': {'min': [round(-hi.x, 4), round(lo.z, 4), round(-hi.y, 4)], 'max': [round(-lo.x, 4), round(hi.z, 4), round(-lo.y, 4)]},
        'box': {
            'outer_size_unity': size_unity(box_hi - box_lo),
            'body_size_unity': [BOX_W, round(BOX_Z1 - BOX_Z0, 4), BOX_D],
            'top_rail_size_unity': [CAP_W, round(CAP_Z1 - CAP_Z0, 4), CAP_D],
            'centre_unity': to_unity((box_lo + box_hi) / 2), 'bottom_y': BOX_Z0, 'top_y': CAP_Z1,
            'front_face_z_unity': round(-BOX_YF, 4), 'back_face_z_unity': round(-BOX_YB, 4),
            'chamfer_m': .012,
        },
        'screen': {
            'object': 'CargoBoard_Screen', 'material': 'TH_CargoBoardScreen',
            'centre_unity': off, 'centre_venue': venue(scr_c), 'matches_ScoreBoardAnchor': tgt_ok,
            'size_unity_wh': [round(scr_size[0], 4), round(scr_size[1], 4)], 'recess_depth_m': RECESS, 'bezel_m': BEZEL,
            'offset_root_to_screen_centre_unity': off,
        },
        'digits_area': {
            'anchor': 'CargoBoard_DigitsAnchor', 'centre_unity': to_unity(digits.location), 'centre_venue': venue(digits.location),
            'size_unity_wh': [digits_w, digits_h], 'facing': '+Z', 'gap_in_front_of_screen_m': round(scr_c.y - digits.location.y, 4),
            'note': '5 digits + thousands comma + G, right aligned; keep the glyphs inside this rectangle',
        },
        'label_plate': {
            'anchor': 'CargoBoard_LabelAnchor (1 mm in front of the plate face)', 'centre_unity': to_unity(label.location),
            'centre_venue': venue(label.location), 'plate_size_unity_wh': [PLATE_S[0], PLATE_S[2]],
            'text_area_unity_wh': [round(PLATE_S[0] - .04, 4), round(PLATE_S[2] - .03, 4)], 'facing': '+Z',
            'note': 'blank brushed-metal plate (light); put dark TMP text here ("納品額")',
        },
        'lamp': {'object': 'CargoBoard_Lamp', 'material': 'TH_CargoBoardLamp', 'centre_unity': to_unity(lamp.location),
                 'centre_venue': venue(lamp.location), 'lens_radius_m': .019, 'lens_height_m': .0158},
        'bolts_front_unity': [to_unity((x, BOX_YF, z)) for x, z in BOLTS],
        'chains': [{'bottom_eye_centre_unity': to_unity(c['bottom_eye']), 'top_pad_eye_centre_unity': to_unity((c['bottom_eye'].x, c['bottom_eye'].y, c['top_eye_z'])),
                    'links': N_LINKS, 'link_inner_len_m': LINK_IN_L} for c in chains],
        'gantry': {
            'posts_centre_xz_unity': [[-sx * POST_X, 0.0] for sx in (-1, 1)], 'posts_centre_xz_venue': [[-sx * POST_X, ROOT_VENUE_UNITY[2]] for sx in (-1, 1)],
            'post_section_m': POST_W, 'base_plate_size_xz_m': [.34, .16], 'side_brace_feet_x_unity': [-1.575, 1.575],
            'cross_bar_bottom_y': BEAM_Z0, 'cross_bar_top_y': BEAM_Z0 + BEAM_H, 'cross_bar_length_m': round(2 * BEAM_X + .02, 4),
            'outrigger_hang_points_unity': [to_unity(h) for h in hangs],
        },
        'triangles': tris, 'triangles_body_parts': tris_parts, 'triangles_total': sum(tris.values()),
        'materials': {'TH_CargoBoard': ['CargoBoard_Body'], 'TH_CargoBoardScreen': ['CargoBoard_Screen'], 'TH_CargoBoardLamp': ['CargoBoard_Lamp']},
        'textures': {'textures/CargoBoard_BaseColor.png': 'sRGB, %d' % TEX_N,
                     'textures/CargoBoard_MetallicSmoothness.png': 'linear (sRGB off), %d, R = metallic, A = smoothness' % TEX_N},
        'colliders': 'none (does not block beams, treasures or the phantom sight)',
    }
    return s


if __name__ == '__main__':
    run()
