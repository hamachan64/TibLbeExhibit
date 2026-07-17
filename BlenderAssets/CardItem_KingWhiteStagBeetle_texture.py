# CardItem_KingWhiteStagBeetle_texture.py
# キングホワイトオオクワガタの BaseMap (1024x1024) をプロシージャル生成する。
# 基調: アニメ準拠のシルバー（銀白）。白すぎない金属感 + 暗色の輪郭線・境界線。
# UVリージョン（build.py と対応）:
#   A: 体・上面投影 [0.02,0.48]x[0.02,0.48] / B: 体・底面 [0.02,0.48]x[0.52,0.98]
#   C: 大顎・上面投影 [0.52,0.98]x[0.02,0.48] / D: 脚・触角 [0.52,0.74]x[0.52,0.74]
#   E: 眼 [0.76,0.88]x[0.52,0.64] / F: 牙・距・爪 [0.90,0.99]x[0.52,0.64]
import bpy, os
import numpy as np

SIZE = 1024
TEX_DIR = r"c:\Users\hama8\TibLbeExhibit\Assets\_Content\Models\Items\KingWhiteStagBeetle"
TEX_PATH = os.path.join(TEX_DIR, "KingWhiteStagBeetle_BaseMap.png")

img = np.zeros((SIZE, SIZE, 4), dtype=np.float32)
img[..., 3] = 1.0
img[..., 0:3] = (0.4, 0.4, 0.45)  # 未使用領域

rng = np.random.default_rng(53)  # カードNo.53

def region_grid(u0, u1, v0, v1, wx0, wx1, wy0, wy1):
    c0, c1 = int(u0*SIZE), int(u1*SIZE)
    r0, r1 = int(v0*SIZE), int(v1*SIZE)
    us = (np.arange(c0, c1) + 0.5) / SIZE
    vs = (np.arange(r0, r1) + 0.5) / SIZE
    X = wx0 + (us - u0) / (u1 - u0) * (wx1 - wx0)
    Y = wy0 + (vs - v0) / (v1 - v0) * (wy1 - wy0)
    XX, YY = np.meshgrid(X, Y)
    return (r0, r1, c0, c1), XX, YY

def seg_mask(X, Y, a, b, w):
    ax, ay = a; bx, by = b
    dx, dy = bx-ax, by-ay
    L2 = dx*dx + dy*dy
    if L2 < 1e-12:
        d = np.sqrt((X-ax)**2 + (Y-ay)**2)
    else:
        t = np.clip(((X-ax)*dx + (Y-ay)*dy) / L2, 0, 1)
        d = np.sqrt((X-(ax+t*dx))**2 + (Y-(ay+t*dy))**2)
    return np.clip(1.0 - d/w, 0, 1)

def disk_mask(X, Y, c, r):
    d = np.sqrt((X-c[0])**2 + (Y-c[1])**2)
    return np.clip(1.0 - d/r, 0, 1)

def blend(buf, mask, color, strength):
    m = (mask * strength)[..., None]
    buf[..., 0:3] = buf[..., 0:3]*(1-m) + np.asarray(color)[None, None, :]*m

# ============ Region A: 体・上面（シルバー） ============
bounds, XA, YA = region_grid(0.02, 0.48, 0.02, 0.48, -0.08, 0.08, -0.125, 0.175)
r0, r1, c0, c1 = bounds
A = np.zeros((r1-r0, c1-c0, 4), dtype=np.float32); A[..., 3] = 1.0
A[..., 0:3] = (0.775, 0.800, 0.835)  # シルバーグレー基調

st_y  = np.array([-0.113, -0.100, -0.085, -0.068, -0.035, -0.012, 0.015, 0.045, 0.075, 0.100, 0.120, 0.138, 0.150, 0.155])
st_hw = np.array([ 0.044,  0.062,  0.056,  0.046,  0.060,  0.052, 0.070, 0.075, 0.074, 0.070, 0.063, 0.050, 0.033, 0.012])
hwA = np.interp(YA, st_y, st_hw)
tEdge = np.clip((np.abs(XA)/np.maximum(hwA, 1e-4) - 0.50) / 0.50, 0, 1)
blend(A, tEdge**1.4, (0.565, 0.615, 0.685), 0.95)
# 各上翅・前胸のドーム状スペキュラハイライト（アニメ調）
for s in (1, -1):
    domeE = np.exp(-(((XA - s*0.036)/0.030)**2 + ((YA - 0.060)/0.062)**2))
    blend(A, domeE, (0.955, 0.965, 0.985), 0.55)
    domeP = np.exp(-(((XA - s*0.028)/0.022)**2 + ((YA + 0.040)/0.024)**2))
    blend(A, domeP, (0.930, 0.940, 0.965), 0.45)
# 頭部前縁の暗め陰影
headShade = np.clip((-(YA + 0.090)) / 0.030, 0, 1)
blend(A, headShade, (0.70, 0.72, 0.76), 0.40)
sheen = np.exp(-(((XA)/0.045)**2 + ((YA-0.055)/0.060)**2))
blend(A, sheen, (0.940, 0.945, 0.940), 0.35)
# 上翅の合わせ目（中央縦線）＋溝の影
blend(A, seg_mask(XA, YA, (0, -0.006), (0, 0.150), 0.0050), (0.60, 0.625, 0.665), 0.60)
blend(A, seg_mask(XA, YA, (0, -0.006), (0, 0.150), 0.0015), (0.30, 0.32, 0.36), 0.95)
# 条溝（ストライエ・控えめ）
for xs in (0.019, 0.038, 0.056):
    for s in (1, -1):
        blend(A, seg_mask(XA, YA, (s*xs, 0.004), (s*xs*0.82, 0.136), 0.0010), (0.585, 0.61, 0.655), 0.55)
# 前胸/上翅の境界・頭/前胸の境界（暗色の輪郭線）
blend(A, seg_mask(XA, YA, (-0.058, -0.0105), (0.058, -0.0105), 0.0030), (0.585, 0.61, 0.655), 0.6)
blend(A, seg_mask(XA, YA, (-0.058, -0.0105), (0.058, -0.0105), 0.0014), (0.33, 0.35, 0.40), 0.9)
blend(A, seg_mask(XA, YA, (-0.045, -0.0665), (0.045, -0.0665), 0.0030), (0.585, 0.61, 0.655), 0.6)
blend(A, seg_mask(XA, YA, (-0.045, -0.0665), (0.045, -0.0665), 0.0014), (0.33, 0.35, 0.40), 0.9)
# 前胸の中央縦溝
blend(A, seg_mask(XA, YA, (0, -0.062), (0, -0.015), 0.0011), (0.56, 0.58, 0.62), 0.5)
# 大顎付け根のAO
for s in (1, -1):
    blend(A, disk_mask(XA, YA, (s*0.036, -0.108), 0.007), (0.58, 0.60, 0.64), 0.4)
# 小楯板
blend(A, disk_mask(XA, YA, (0, -0.004), 0.009), (0.68, 0.70, 0.735), 0.5)
# 前胸側方の点刻
for s in (1, -1):
    blend(A, disk_mask(XA, YA, (s*0.050, -0.035), 0.0035), (0.40, 0.42, 0.46), 0.65)
# 頭盾の切れ込み
blend(A, disk_mask(XA, YA, (0, -0.1115), 0.005), (0.45, 0.47, 0.51), 0.45)
A[..., 0:3] += rng.normal(0, 0.006, A[..., 0:3].shape).astype(np.float32)
img[r0:r1, c0:c1] = A

# ============ Region B: 体・底面（暗めシルバー） ============
bounds, XB, YB = region_grid(0.02, 0.48, 0.52, 0.98, -0.08, 0.08, -0.125, 0.175)
r0, r1, c0, c1 = bounds
B = np.zeros((r1-r0, c1-c0, 4), dtype=np.float32); B[..., 3] = 1.0
B[..., 0:3] = (0.685, 0.700, 0.725)
hwB = np.interp(YB, st_y, st_hw)
tEdgeB = np.clip((np.abs(XB)/np.maximum(hwB, 1e-4) - 0.55) / 0.45, 0, 1)
blend(B, tEdgeB, (0.55, 0.57, 0.61), 0.6)
for k in range(5):
    y0 = 0.015 + 0.030*k
    for s in (1, -1):
        blend(B, seg_mask(XB, YB, (0, y0), (s*0.062, y0+0.006), 0.0014), (0.42, 0.44, 0.47), 0.6)
for (hx, hy) in ((0.048, -0.075), (0.060, 0.005), (0.058, 0.085)):
    for s in (1, -1):
        blend(B, disk_mask(XB, YB, (s*hx, hy), 0.014), (0.47, 0.49, 0.52), 0.55)
blend(B, seg_mask(XB, YB, (0, -0.110), (0, 0.010), 0.0012), (0.50, 0.52, 0.55), 0.4)
B[..., 0:3] += rng.normal(0, 0.005, B[..., 0:3].shape).astype(np.float32)
img[r0:r1, c0:c1] = B

# ============ Region C: 大顎（シルバー） ============
bounds, XC, YC = region_grid(0.52, 0.98, 0.02, 0.48, -0.09, 0.09, -0.26, -0.08)
r0, r1, c0, c1 = bounds
C = np.zeros((r1-r0, c1-c0, 4), dtype=np.float32); C[..., 3] = 1.0
C[..., 0:3] = (0.785, 0.805, 0.835)
tipCool = np.clip((-(YC + 0.20)) / 0.05, 0, 1)
blend(C, tipCool, (0.66, 0.69, 0.74), 0.35)
baseSh = np.clip(((YC + 0.12)) / 0.03, 0, 1)
blend(C, baseSh, (0.65, 0.67, 0.71), 0.35)
# ビーム上のスペキュラハイライト
for s in (1, -1):
    hlM = np.exp(-(((XC - s*0.052)/0.020)**2 + ((YC + 0.19)/0.045)**2))
    blend(C, hlM, (0.95, 0.96, 0.98), 0.5)
mand_outline = [
    (0.012, -0.098), (0.024, -0.112), (0.006, -0.134), (0.026, -0.152),
    (0.033, -0.168), (0.035, -0.182), (0.029, -0.197), (0.018, -0.206),
    (0.028, -0.218), (0.030, -0.226), (0.008, -0.242), (0.026, -0.245),
    (0.044, -0.236), (0.058, -0.224), (0.068, -0.207), (0.074, -0.188),
    (0.077, -0.168), (0.078, -0.154), (0.067, -0.145), (0.062, -0.124), (0.058, -0.104),
]
for s in (1, -1):
    pts = [(s*x, y) for (x, y) in mand_outline]
    for a, b in zip(pts, pts[1:] + pts[:1]):
        blend(C, seg_mask(XC, YC, a, b, 0.0050), (0.60, 0.625, 0.665), 0.45)
        blend(C, seg_mask(XC, YC, a, b, 0.0016), (0.32, 0.34, 0.385), 0.75)
    blend(C, disk_mask(XC, YC, (s*0.031, -0.180), 0.013), (0.63, 0.65, 0.685), 0.35)
    blend(C, disk_mask(XC, YC, (s*0.027, -0.222), 0.009), (0.61, 0.63, 0.66), 0.40)
    blend(C, disk_mask(XC, YC, (s*0.020, -0.120), 0.009), (0.65, 0.67, 0.70), 0.30)
C[..., 0:3] += rng.normal(0, 0.006, C[..., 0:3].shape).astype(np.float32)
img[r0:r1, c0:c1] = C

# ============ Region D: 脚・触角（シルバー、接地側ほど濃） ============
bounds, XD, YD = region_grid(0.52, 0.74, 0.52, 0.74, 0.0, 1.0, 0.0, 1.0)
r0, r1, c0, c1 = bounds
D = np.zeros((r1-r0, c1-c0, 4), dtype=np.float32); D[..., 3] = 1.0
vv = np.clip((YD - 0.05) / 0.90, 0, 1)[..., None]
dark = np.array([0.46, 0.48, 0.53]); light = np.array([0.78, 0.80, 0.84])
D[..., 0:3] = dark[None, None, :]*(1-vv) + light[None, None, :]*vv
D[..., 0:3] += rng.normal(0, 0.008, D[..., 0:3].shape).astype(np.float32)
img[r0:r1, c0:c1] = D

# ============ Region E: 複眼（ダークスチールグレー） ============
bounds, XE, YE = region_grid(0.76, 0.88, 0.52, 0.64, -1.0, 1.0, -1.0, 1.0)
r0, r1, c0, c1 = bounds
E = np.zeros((r1-r0, c1-c0, 4), dtype=np.float32); E[..., 3] = 1.0
E[..., 0:3] = (0.16, 0.17, 0.20)
hl = np.exp(-((XE-0.3)**2 + (YE-0.3)**2) / 0.5)
E[..., 0:3] += (hl*0.10)[..., None]
img[r0:r1, c0:c1] = E

# ============ Region F: 牙・距・爪 ============
bounds, XF, YF = region_grid(0.90, 0.99, 0.52, 0.64, 0.0, 1.0, 0.0, 1.0)
r0, r1, c0, c1 = bounds
F = np.zeros((r1-r0, c1-c0, 4), dtype=np.float32); F[..., 3] = 1.0
vvf = np.clip(YF, 0, 1)[..., None]
darkF = np.array([0.28, 0.28, 0.31]); lightF = np.array([0.70, 0.70, 0.72])
F[..., 0:3] = darkF[None, None, :]*(1-vvf) + lightF[None, None, :]*vvf
img[r0:r1, c0:c1] = F

# ============ 保存 & マテリアル接続 ============
np.clip(img, 0, 1, out=img)
os.makedirs(TEX_DIR, exist_ok=True)
name = 'KingWhiteStagBeetle_BaseMap'
if name in bpy.data.images:
    bpy.data.images.remove(bpy.data.images[name])
image = bpy.data.images.new(name, SIZE, SIZE, alpha=False)
image.pixels.foreach_set(img.ravel())
image.filepath_raw = TEX_PATH
image.file_format = 'PNG'
image.save()

mat = bpy.data.materials.get('KingWhiteStagBeetle_Mat')
nt = mat.node_tree
bsdf = None
for n in nt.nodes:
    if n.type == 'BSDF_PRINCIPLED':
        bsdf = n
tex = nt.nodes.get('KWSB_BaseTex')
if tex is None:
    tex = nt.nodes.new('ShaderNodeTexImage')
    tex.name = 'KWSB_BaseTex'
    tex.location = (-400, 200)
tex.image = image
for l in list(bsdf.inputs['Base Color'].links):
    nt.links.remove(l)
nt.links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])
bsdf.inputs['Metallic'].default_value = 0.55
bsdf.inputs['Roughness'].default_value = 0.20
print("TEXTURE OK ->", TEX_PATH)
