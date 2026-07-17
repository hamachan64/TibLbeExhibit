# CardItem_KingWhiteStagBeetle_build.py
# キングホワイトオオクワガタ（GIカード No.53 / Cランク）のパラメトリックビルドスクリプト
# 案A: 可動パーツ分割構成 — ルートEmpty配下に Body / Mandible L,R / Antenna L,R / Leg x6 を
#      個別メッシュとして生成し、各関節位置（大顎基部・触角基部・脚の付け根）に原点を置く。
#      Unity側では各パーツの localRotation を DOTween で回すだけで動かせる。
# Blender MCP から exec(open(path).read()) で実行する。ヘルパーは bpy.app.driver_namespace['kw'] を使用。
import bpy, bmesh, math
from mathutils import Vector, Matrix
from mathutils.geometry import tessellate_polygon
from collections import defaultdict

kw = bpy.app.driver_namespace['kw']
catmull, se_ring, loft, tube, ellipsoid = kw['catmull'], kw['se_ring'], kw['loft'], kw['tube'], kw['ellipsoid']

ROOT_NAME = 'KingWhiteStagBeetle'
mat = bpy.data.materials.get('KingWhiteStagBeetle_Mat')

# ---- 旧オブジェクト（単一メッシュ版・旧分割版）を削除 ----
for obj in list(bpy.data.objects):
    if obj.name == ROOT_NAME or obj.name.startswith('KWSB_'):
        me_old = obj.data if obj.type == 'MESH' else None
        bpy.data.objects.remove(obj, do_unlink=True)
        if me_old:
            bpy.data.meshes.remove(me_old)

# ============ グループ管理 ============
class Group:
    def __init__(self, name, pivot):
        self.name = name
        self.pivot = Vector(pivot)
        self.bm = bmesh.new()
        self.pl = self.bm.faces.layers.int.new('part')

groups = []
def new_group(name, pivot):
    g = Group(name, pivot)
    groups.append(g)
    return g.bm, g.pl

# ---- polygon inward offset (miter, clamped) ----
def poly_offset(pts2d, d):
    n = len(pts2d)
    area = sum(pts2d[i][0]*pts2d[(i+1) % n][1] - pts2d[(i+1) % n][0]*pts2d[i][1] for i in range(n))
    sign = 1.0 if area > 0 else -1.0
    out = []
    for i in range(n):
        p_prev = Vector((pts2d[(i-1) % n][0], pts2d[(i-1) % n][1], 0))
        p_cur  = Vector((pts2d[i][0], pts2d[i][1], 0))
        p_next = Vector((pts2d[(i+1) % n][0], pts2d[(i+1) % n][1], 0))
        e1 = (p_cur - p_prev).normalized()
        e2 = (p_next - p_cur).normalized()
        n1 = Vector((-e1.y, e1.x, 0)) * sign
        n2 = Vector((-e2.y, e2.x, 0)) * sign
        nm = (n1 + n2)
        if nm.length < 1e-6:
            nm = n1
        nm.normalize()
        cosh = max(0.35, (1.0 + n1.dot(n2)) * 0.5) ** 0.5
        q = p_cur + nm * (d / cosh)
        out.append((q.x, q.y))
    return out

# ---- volumetric blade (domed top, thick base -> thinner tip) ----
def blade(bm, PL, pts2d, z_bot, t_base, t_tip, y_base, y_tip, lift_fn, pid):
    def t_of(y):
        u = min(1.0, max(0.0, (y - y_base) / (y_tip - y_base)))
        return t_base * (1-u) + t_tip * u
    profile = [(0.0020, 0.00), (0.0, 0.18), (-0.0012, 0.50), (0.0, 0.82), (0.0020, 1.00), (0.0050, 1.22)]
    vrings = []
    for off, zf in profile:
        opts = poly_offset(pts2d, off) if abs(off) > 1e-9 else pts2d
        ring = []
        for (ox, oy), (x0, y0) in zip(opts, pts2d):
            z = z_bot + t_of(y0) * zf + lift_fn(y0)
            ring.append(bm.verts.new((ox, oy, z)))
        vrings.append(ring)
    n = len(pts2d)
    faces = []
    for ra, rb in zip(vrings[:-1], vrings[1:]):
        for i in range(n):
            faces.append(bm.faces.new((ra[i], ra[(i+1) % n], rb[(i+1) % n], rb[i])))
    cap_pts = poly_offset(pts2d, profile[-1][0])
    bot_pts = poly_offset(pts2d, profile[0][0])
    tris = tessellate_polygon([[Vector((x, y, 0)) for (x, y) in cap_pts]])
    for (a, b, c) in tris:
        faces.append(bm.faces.new((vrings[-1][a], vrings[-1][b], vrings[-1][c])))
    tris2 = tessellate_polygon([[Vector((x, y, 0)) for (x, y) in bot_pts]])
    for (a, b, c) in tris2:
        faces.append(bm.faces.new((vrings[0][c], vrings[0][b], vrings[0][a])))
    for f in faces:
        f[PL] = pid

# ============ BODY + EYES（非可動）: pivot=接地面中央 ============
bm, PL = new_group('KWSB_Body', (0, 0, 0))
stations = [
    (-0.113, 0.044, 0.010, 0.012, 0.042, 3.8),
    (-0.108, 0.058, 0.014, 0.016, 0.043, 3.8),
    (-0.100, 0.062, 0.016, 0.018, 0.044, 3.8),
    (-0.085, 0.056, 0.016, 0.018, 0.044, 3.6),
    (-0.072, 0.050, 0.014, 0.017, 0.044, 3.4),
    (-0.068, 0.046, 0.012, 0.016, 0.044, 3.2),
    (-0.065, 0.054, 0.020, 0.019, 0.045, 2.9),
    (-0.050, 0.059, 0.024, 0.021, 0.046, 2.9),
    (-0.035, 0.060, 0.025, 0.021, 0.046, 2.9),
    (-0.015, 0.057, 0.023, 0.021, 0.046, 2.9),
    (-0.012, 0.052, 0.020, 0.020, 0.046, 2.5),
    (-0.008, 0.062, 0.026, 0.021, 0.047, 2.3),
    ( 0.015, 0.070, 0.032, 0.023, 0.048, 2.2),
    ( 0.045, 0.075, 0.036, 0.024, 0.049, 2.2),
    ( 0.075, 0.074, 0.036, 0.024, 0.049, 2.2),
    ( 0.100, 0.070, 0.033, 0.022, 0.048, 2.2),
    ( 0.120, 0.063, 0.029, 0.020, 0.047, 2.2),
    ( 0.138, 0.050, 0.023, 0.017, 0.046, 2.2),
    ( 0.150, 0.033, 0.015, 0.011, 0.044, 2.2),
    ( 0.155, 0.012, 0.006, 0.005, 0.042, 2.2),
]
rings = [se_ring(y, hw, ta, ba, mz, 24, e_top=et, e_bot=2.0) for (y, hw, ta, ba, mz, et) in stations]
loft(bm, rings, PL, 1, cap_start=True, cap_end=True)
for sx in (1, -1):
    a = Vector((0.050*sx, -0.104, 0.049))
    b = Vector((0.062*sx, -0.095, 0.049))
    ellipsoid(bm, a, b, 0.0080, 0.0080, PL, 5, nseg=12, nring=6)

# ============ MANDIBLES（可動）: pivot=基部（Z軸回転で開閉） ============
mand_outline = [
    (0.012, -0.098),
    (0.024, -0.112),
    (0.006, -0.134),
    (0.026, -0.152),
    (0.033, -0.168), (0.035, -0.182), (0.029, -0.197),
    (0.018, -0.206),
    (0.028, -0.218), (0.030, -0.226),
    (0.008, -0.242),
    (0.026, -0.245),
    (0.044, -0.236), (0.058, -0.224), (0.068, -0.207),
    (0.074, -0.188), (0.077, -0.168),
    (0.078, -0.154),
    (0.067, -0.145),
    (0.062, -0.124), (0.058, -0.104),
]
def mand_lift(y):
    return max(0.0, (-y - 0.105)) * 0.055
for sx, suffix in ((1, 'R'), (-1, 'L')):
    bm, PL = new_group('KWSB_Mandible_' + suffix, (0.035*sx, -0.102, 0.048))
    pts = [(sx*x, y) for (x, y) in mand_outline]
    if sx < 0:
        pts = list(reversed(pts))  # ミラー時も反時計回りを維持
    blade(bm, PL, pts, 0.038, 0.020, 0.011, -0.098, -0.243, mand_lift, 2)

# ============ ANTENNAE（可動）: pivot=基部 ============
for sx, suffix in ((1, 'R'), (-1, 'L')):
    bm, PL = new_group('KWSB_Antenna_' + suffix, (0.050*sx, -0.108, 0.037))
    ctrl = [(0.050*sx, -0.108, 0.037), (0.068*sx, -0.128, 0.039), (0.080*sx, -0.150, 0.041), (0.086*sx, -0.170, 0.042)]
    path = catmull(ctrl, 9)
    tube(bm, path, lambda t: 0.0024, 8, PL, 4, cap_start=True, cap_end=True, round_tip=True)
    d = (path[-1] - path[-2]).normalized()
    side = Vector((-d.y, d.x, 0)).normalized()
    for ss in (1, -1):
        prong_tip = path[-1] + d*0.013 + side*(0.0050*ss)
        ellipsoid(bm, path[-1], prong_tip, 0.0022, 0.0007, PL, 4, nseg=6, nring=3)

# ============ LEGS（可動）: pivot=付け根（股関節） ============
legs = [
    ('Front', (0.048, -0.075, 0.026), (0.115, -0.115, 0.034), (0.155, -0.150, 0.006), (0.175, -0.168, 0.001)),
    ('Mid',   (0.060,  0.005, 0.026), (0.125, -0.010, 0.034), (0.170,  0.020, 0.006), (0.190,  0.035, 0.001)),
    ('Rear',  (0.058,  0.085, 0.026), (0.120,  0.108, 0.034), (0.162,  0.150, 0.006), (0.182,  0.170, 0.001)),
]
for sx, sfx in ((1, 'R'), (-1, 'L')):
    for lname, hip0, knee0, foot0_, toe0 in legs:
        hip   = Vector((hip0[0]*sx, hip0[1], hip0[2]))
        knee  = Vector((knee0[0]*sx, knee0[1], knee0[2]))
        foot0 = Vector((foot0_[0]*sx, foot0_[1], foot0_[2]))
        toe   = Vector((toe0[0]*sx, toe0[1], toe0[2]))
        bm, PL = new_group('KWSB_Leg_%s_%s' % (lname, sfx), hip)
        ellipsoid(bm, hip - (knee-hip)*0.10, knee + (knee-hip)*0.02, 0.010, 0.008, PL, 6, nseg=10, nring=7, pexp=0.5)
        ellipsoid(bm, knee - (foot0-knee)*0.04, foot0, 0.0065, 0.0035, PL, 7, nseg=10, nring=7, pexp=0.55)
        spur_tip = foot0 + Vector((0.003*sx, 0.002, -0.003))
        ellipsoid(bm, foot0 + Vector((0, 0, 0.002)), spur_tip, 0.0028, 0.0009, PL, 3, nseg=6, nring=3)
        seg1_end = foot0 + (toe-foot0)*0.55
        ellipsoid(bm, foot0, seg1_end + (toe-foot0)*0.05, 0.0040, 0.0034, PL, 8, nseg=8, nring=4, pexp=0.7)
        ellipsoid(bm, seg1_end, toe, 0.0034, 0.0028, PL, 8, nseg=8, nring=4, pexp=0.7)
        fwd = (toe - foot0).normalized()
        side = Vector((-fwd.y*sx, fwd.x*sx, 0)).normalized()
        for ss in (1, -1):
            claw_tip = toe + fwd*0.008 + side*(0.0035*ss) + Vector((0, 0, -0.0012))
            ellipsoid(bm, toe, claw_tip, 0.0020, 0.0006, PL, 9, nseg=6, nring=3)

# ============ 各グループ: 法線検証 → UV → 原点移動 → オブジェクト化 ============
root = bpy.data.objects.new(ROOT_NAME, None)
root.empty_display_size = 0.05
bpy.context.collection.objects.link(root)

def assign_uv(bm, PL):
    uv_layer = bm.loops.layers.uv.new('UVMap')
    def _c(v):
        return min(1.0, max(0.0, v))
    for f in bm.faces:
        pid = f[PL]
        for loop in f.loops:
            x, y, z = loop.vert.co
            if pid == 1:
                u = 0.02 + _c((x + 0.08) / 0.16) * 0.46
                v = 0.02 + _c((y + 0.125) / 0.30) * 0.46
                if f.normal.z < -0.2:
                    v += 0.5
            elif pid == 2:
                u = 0.52 + _c((x + 0.09) / 0.18) * 0.46
                v = 0.02 + _c((y + 0.26) / 0.18) * 0.46
            elif pid == 5:
                u = 0.82 + f.normal.x * 0.04
                v = 0.58 + f.normal.y * 0.04
            elif pid in (3, 9):
                u = 0.945 + f.normal.x * 0.035
                v = 0.53 + _c(z / 0.07) * 0.10
            else:
                u = 0.53 + _c((x + 0.20) / 0.40) * 0.20
                v = 0.53 + _c(z / 0.05) * 0.20
            loop[uv_layer].uv = (u, v)

total_tris = 0
total_flipped = 0
for g in groups:
    bm = g.bm
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    # 連結成分ごとの符号付き体積チェック
    bm.verts.ensure_lookup_table()
    parent = list(range(len(bm.verts)))
    def _find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]; a = parent[a]
        return a
    for e in bm.edges:
        ra, rb = _find(e.verts[0].index), _find(e.verts[1].index)
        if ra != rb:
            parent[ra] = rb
    comp_faces = defaultdict(list)
    for f in bm.faces:
        comp_faces[_find(f.verts[0].index)].append(f)
    for _, faces in comp_faces.items():
        vol = 0.0
        for f in faces:
            vs = f.verts
            for i in range(1, len(vs)-1):
                vol += vs[0].co.dot(vs[i].co.cross(vs[i+1].co))
        if vol < 0:
            for f in faces:
                f.normal_flip()
            total_flipped += 1
    # UV はワールド座標のまま割り当ててから原点へ平行移動
    assign_uv(bm, g.pl)
    bmesh.ops.translate(bm, verts=bm.verts, vec=-g.pivot)
    me = bpy.data.meshes.new(g.name + '_Mesh')
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(g.name, me)
    obj.location = g.pivot
    bpy.context.collection.objects.link(obj)
    obj.parent = root
    if mat:
        me.materials.append(mat)
    bpy.context.view_layer.objects.active = obj
    for o in bpy.context.selected_objects:
        o.select_set(False)
    obj.select_set(True)
    bpy.ops.object.shade_auto_smooth(angle=math.radians(50))
    total_tris += sum(len(p.vertices) - 2 for p in me.polygons)

print("BUILD OK (split) objects=", len(groups), "tris=", total_tris, "flipped=", total_flipped)
