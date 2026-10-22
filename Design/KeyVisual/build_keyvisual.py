"""Treasure Haul key visual (16:9) generator.

Draws the key visual as SVG with a simple pinhole projection so the ruins,
the floor and the real exhibition hall above the 2.4 m walls share one
perspective. No external assets: every shape is generated here.

Usage: python3 build_keyvisual.py  -> keyvisual.svg (render with render.mjs)
"""
import math
import random
from pathlib import Path

W, H = 1920, 1080
CX, CY = 960, 610          # principal point (horizon slightly below middle)
F = 880                    # focal length in px
CAM_Y = 1.15               # camera height (m) - slightly low, heroic angle

rnd = random.Random(20261118)


def P(x, y, z):
    """World (m) -> screen (px). x right, y up, z forward."""
    return (CX + F * x / z, CY - F * (y - CAM_Y) / z)


def pts(seq):
    return " ".join(f"{x:.1f},{y:.1f}" for x, y in seq)


def poly(seq, fill, extra=""):
    return f'<polygon points="{pts(seq)}" fill="{fill}" {extra}/>'


out = []
add = out.append

# ---------------------------------------------------------------- defs
add(f'''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W} {H}" width="{W}" height="{H}">
<defs>
  <linearGradient id="hallSky" x1="0" y1="0" x2="0" y2="1">
    <stop offset="0" stop-color="#1a252c"/>
    <stop offset="0.3" stop-color="#2f3d45"/>
    <stop offset="0.5" stop-color="#55595a"/>
    <stop offset="0.6" stop-color="#7a6a55"/>
  </linearGradient>
  <radialGradient id="hallHaze" cx="0.5" cy="0.62" r="0.55">
    <stop offset="0" stop-color="#ffcf8a" stop-opacity="0.55"/>
    <stop offset="0.5" stop-color="#d48a4a" stop-opacity="0.18"/>
    <stop offset="1" stop-color="#d48a4a" stop-opacity="0"/>
  </radialGradient>
  <linearGradient id="floorG" x1="0" y1="0" x2="0" y2="1">
    <stop offset="0" stop-color="#b59a76"/>
    <stop offset="0.35" stop-color="#8a7458"/>
    <stop offset="1" stop-color="#2c2a28"/>
  </linearGradient>
  <radialGradient id="floorPool" cx="0.5" cy="0.12" r="0.6">
    <stop offset="0" stop-color="#ffd9a0" stop-opacity="0.75"/>
    <stop offset="0.4" stop-color="#f0a860" stop-opacity="0.25"/>
    <stop offset="1" stop-color="#f0a860" stop-opacity="0"/>
  </radialGradient>
  <radialGradient id="treasureGlow" cx="0.5" cy="0.5" r="0.5">
    <stop offset="0" stop-color="#fff4cf" stop-opacity="1"/>
    <stop offset="0.18" stop-color="#ffd27a" stop-opacity="0.85"/>
    <stop offset="0.5" stop-color="#e9963f" stop-opacity="0.28"/>
    <stop offset="1" stop-color="#e9963f" stop-opacity="0"/>
  </radialGradient>
  <linearGradient id="gold" x1="0" y1="0" x2="1" y2="1">
    <stop offset="0" stop-color="#fff0b8"/>
    <stop offset="0.35" stop-color="#f2c255"/>
    <stop offset="0.7" stop-color="#b9761f"/>
    <stop offset="1" stop-color="#6e3e10"/>
  </linearGradient>
  <linearGradient id="bronze" x1="0" y1="0" x2="1" y2="0">
    <stop offset="0" stop-color="#3b2a1c"/>
    <stop offset="0.45" stop-color="#c88a4a"/>
    <stop offset="0.6" stop-color="#f0c27d"/>
    <stop offset="1" stop-color="#4a3020"/>
  </linearGradient>
  <linearGradient id="beamA" x1="0" y1="0" x2="1" y2="0">
    <stop offset="0" stop-color="#7cf0b4" stop-opacity="0.95"/>
    <stop offset="1" stop-color="#7cf0b4" stop-opacity="0.15"/>
  </linearGradient>
  <linearGradient id="rayG" x1="0" y1="0" x2="0" y2="1">
    <stop offset="0" stop-color="#ffe3b0" stop-opacity="0.0"/>
    <stop offset="0.25" stop-color="#ffe3b0" stop-opacity="0.32"/>
    <stop offset="1" stop-color="#ffcf8a" stop-opacity="0.0"/>
  </linearGradient>
  <linearGradient id="vignetteTop" x1="0" y1="0" x2="0" y2="1">
    <stop offset="0" stop-color="#05090c" stop-opacity="0.85"/>
    <stop offset="1" stop-color="#05090c" stop-opacity="0"/>
  </linearGradient>
  <radialGradient id="vignette" cx="0.5" cy="0.55" r="0.75">
    <stop offset="0.55" stop-color="#000" stop-opacity="0"/>
    <stop offset="1" stop-color="#000" stop-opacity="0.62"/>
  </radialGradient>
  <filter id="blur2"><feGaussianBlur stdDeviation="2"/></filter>
  <filter id="blur6"><feGaussianBlur stdDeviation="6"/></filter>
  <filter id="blur14"><feGaussianBlur stdDeviation="14"/></filter>
  <filter id="blur30"><feGaussianBlur stdDeviation="30"/></filter>
  <filter id="dof"><feGaussianBlur stdDeviation="3.2"/></filter>
  <filter id="grain" x="0" y="0" width="100%" height="100%">
    <feTurbulence type="fractalNoise" baseFrequency="0.9" numOctaves="2" seed="7" result="n"/>
    <feColorMatrix type="saturate" values="0"/>
    <feComponentTransfer><feFuncA type="table" tableValues="0 0.11"/></feComponentTransfer>
  </filter>
  <filter id="stoneTex" x="0" y="0" width="100%" height="100%">
    <feTurbulence type="fractalNoise" baseFrequency="0.035 0.06" numOctaves="4" seed="3" result="t"/>
    <feColorMatrix in="t" type="matrix" values="0 0 0 0 0.05  0 0 0 0 0.04  0 0 0 0 0.03  0 0 0 0.9 -0.25"/>
    <feComposite in2="SourceGraphic" operator="in"/>
  </filter>
</defs>
''')

# ---------------------------------------------------------------- real hall (above the walls)
# This is the real exhibition hall seen in passthrough: roof trusses, hanging
# lights, neighbouring booth towers and banners. Kept low-contrast so the top
# band works as copy space.
add(f'<rect width="{W}" height="{H}" fill="url(#hallSky)"/>')

# neighbouring booth towers / signage peeking over the ruin walls (blurred: out of focus)
far_z = 50.0
add('<g filter="url(#blur2)">')
for i in range(-14, 15):
    xc = i * 3.3 + rnd.uniform(-0.8, 0.8)
    w = rnd.uniform(1.6, 3.0)
    h = rnd.uniform(9.5, 14.5)
    x0, y0 = P(xc - w / 2, h, far_z)
    x1, y1 = P(xc + w / 2, 0, far_z)
    col = rnd.choice(["#34424a", "#3c4a52", "#463f3a", "#2e3b44", "#4a4640"])
    add(f'<rect x="{x0:.0f}" y="{y0:.0f}" width="{x1 - x0:.0f}" height="{y1 - y0:.0f}" fill="{col}"/>')
    if rnd.random() < 0.7:
        # lit sign panel (abstract, no text/logos)
        sy = P(0, h - 0.6, far_z)[1]
        pc = rnd.choice(["#e9d9b8", "#9fc3d0", "#d9b07a", "#c9d6d8"])
        add(f'<rect x="{x0 + 2:.0f}" y="{y0 + 3:.0f}" width="{x1 - x0 - 4:.0f}" height="{(sy - y0) * 0.9:.0f}" fill="{pc}" opacity="0.35"/>')
add('</g>')

# hanging banners from the roof (real venue)
for (x, z, wb, hb, c) in [(-14, 30, 3.0, 2.2, "#3d4d55"), (9, 26, 2.6, 2.0, "#4a463f"), (21, 38, 3.2, 2.4, "#3a4850"), (-26, 40, 3.0, 2.2, "#46433d")]:
    a = P(x - wb / 2, 9.0, z)
    b = P(x + wb / 2, 9.0 - hb, z)
    t = P(x, 11.0, z)
    add(f'<line x1="{t[0]:.0f}" y1="{t[1]:.0f}" x2="{t[0]:.0f}" y2="{a[1]:.0f}" stroke="#1b252b" stroke-width="1"/>')
    add(f'<rect x="{a[0]:.0f}" y="{a[1]:.0f}" width="{b[0] - a[0]:.0f}" height="{b[1] - a[1]:.0f}" fill="{c}" opacity="0.8"/>')

# roof trusses: dark steel silhouettes against the lit hall
roof_y = 11.0
truss = []
for z in [13, 18, 25, 34, 46]:
    sw = 34 / z
    top = [P(x, roof_y, z) for x in (-60, 60)]
    bot = [P(x, roof_y - 1.2, z) for x in (-60, 60)]
    truss.append(f'<line x1="{top[0][0]:.0f}" y1="{top[0][1]:.0f}" x2="{top[1][0]:.0f}" y2="{top[1][1]:.0f}" stroke="#0f171c" stroke-width="{sw * 1.4:.1f}"/>')
    truss.append(f'<line x1="{bot[0][0]:.0f}" y1="{bot[0][1]:.0f}" x2="{bot[1][0]:.0f}" y2="{bot[1][1]:.0f}" stroke="#0f171c" stroke-width="{sw:.1f}"/>')
    zz = [P(k * 1.2, roof_y - (1.2 if k % 2 else 0), z) for k in range(-50, 51)]
    truss.append(f'<polyline points="{pts(zz)}" fill="none" stroke="#131c22" stroke-width="{sw * 0.6:.1f}"/>')
    # hanging high-bay lights under each truss
    for x in range(-40, 41, 8):
        lx, ly = P(x + (4 if z in (18, 34) else 0), roof_y - 1.6, z)
        r = 80 / z
        truss.append(f'<circle cx="{lx:.0f}" cy="{ly + r:.0f}" r="{r * 3:.1f}" fill="#fff0d0" opacity="0.07" filter="url(#blur6)"/>')
        truss.append(f'<rect x="{lx - r:.1f}" y="{ly - r * 0.5:.1f}" width="{r * 2:.1f}" height="{r * 0.7:.1f}" fill="#fff6e4" opacity="0.8"/>')
for x in (-30, -15, 0, 15, 30):
    a, b = P(x, roof_y, 13), P(x, roof_y, 46)
    truss.append(f'<line x1="{a[0]:.0f}" y1="{a[1]:.0f}" x2="{b[0]:.0f}" y2="{b[1]:.0f}" stroke="#0f171c" stroke-width="2.2"/>')
add('<g opacity="0.75">' + "".join(truss) + '</g>')
# warm haze rising from the ruins
add(f'<rect width="{W}" height="{H}" fill="url(#hallHaze)"/>')


# ---------------------------------------------------------------- stone helpers
def stone_wall_x(x, z0, z1, h, face_dir, base_col, seed, ragged=True, z_step=None):
    """Wall plane at constant x, spanning z0..z1, blocks with mortar lines.
    face_dir: +1 if the face looks toward +x (left wall), -1 for right wall."""
    r = random.Random(seed)
    g = []
    # base face
    top = []
    zs = []
    z = z0
    while z < z1:
        zs.append(z)
        z += 0.55 + r.random() * 0.25
    zs.append(z1)
    # ragged top
    for i in range(len(zs) - 1):
        hh = h - (r.random() * 0.25 if ragged else 0)
        top.append((zs[i], hh))
        top.append((zs[i + 1], hh))
    face = [P(x, 0, z0)] + [P(x, hh, zz) for zz, hh in top] + [P(x, 0, z1)]
    g.append(poly(face, base_col))
    g.append(f'<polygon points="{pts(face)}" fill="#fff" filter="url(#stoneTex)" opacity="0.9"/>')
    # courses
    rows = [0, 0.38, 0.74, 1.12, 1.48, 1.86, 2.2]
    for ri, y in enumerate(rows[1:], 1):
        a, b = P(x, y, z0), P(x, y, z1)
        g.append(f'<line x1="{a[0]:.1f}" y1="{a[1]:.1f}" x2="{b[0]:.1f}" y2="{b[1]:.1f}" stroke="#14100c" stroke-opacity="0.55" stroke-width="{max(0.8, 6 / ((z0 + z1) / 2)):.1f}"/>')
    for ri in range(len(rows) - 1):
        y0, y1 = rows[ri], rows[ri + 1]
        z = z0 + (0.3 if ri % 2 else 0)
        while z < z1:
            a, b = P(x, y0, z), P(x, y1, z)
            g.append(f'<line x1="{a[0]:.1f}" y1="{a[1]:.1f}" x2="{b[0]:.1f}" y2="{b[1]:.1f}" stroke="#14100c" stroke-opacity="0.5" stroke-width="{max(0.8, 5 / z):.1f}"/>')
            # per-block shade variation
            zn = z + 0.6 + r.random() * 0.25
            if r.random() < 0.55:
                blk = [P(x, y0, z), P(x, y1, z), P(x, y1, min(zn, z1)), P(x, y0, min(zn, z1))]
                shade = r.choice(["#000", "#000", "#fff"])
                g.append(poly(blk, shade, f'opacity="{0.04 + r.random() * 0.1:.2f}"'))
            z = zn
    # moss at the foot
    moss = [P(x, 0, z0), P(x, 0.22, z0)]
    z = z0
    while z < z1:
        z += 0.4
        moss.append(P(x, 0.12 + r.random() * 0.2, min(z, z1)))
    moss.append(P(x, 0, z1))
    g.append(poly(moss, "#3f4a24", 'opacity="0.55"'))
    # top cap (wall thickness 0.2)
    t = 0.2 * -face_dir
    for zz, hh in top[::2]:
        pass
    edge = [P(x, hh, zz) for zz, hh in top]
    g.append(f'<polyline points="{pts(edge)}" fill="none" stroke="#e8b878" stroke-opacity="0.45" stroke-width="2"/>')
    return "".join(g)


def stone_wall_z(z, x0, x1, h, base_col, seed):
    """Back wall (constant z) with blocks."""
    r = random.Random(seed)
    g = []
    face = [P(x0, 0, z), P(x0, h, z), P(x1, h, z), P(x1, 0, z)]
    g.append(poly(face, base_col))
    g.append(f'<polygon points="{pts(face)}" fill="#fff" filter="url(#stoneTex)" opacity="0.8"/>')
    rows = [0, 0.4, 0.8, 1.2, 1.6, 2.0, h]
    for ri in range(len(rows) - 1):
        y0, y1 = rows[ri], rows[ri + 1]
        a, b = P(x0, y1, z), P(x1, y1, z)
        g.append(f'<line x1="{a[0]:.1f}" y1="{a[1]:.1f}" x2="{b[0]:.1f}" y2="{b[1]:.1f}" stroke="#120e0a" stroke-opacity="0.6" stroke-width="1.2"/>')
        x = x0 + (0.35 if ri % 2 else 0)
        while x < x1:
            a, b = P(x, y0, z), P(x, y1, z)
            g.append(f'<line x1="{a[0]:.1f}" y1="{a[1]:.1f}" x2="{b[0]:.1f}" y2="{b[1]:.1f}" stroke="#120e0a" stroke-opacity="0.55" stroke-width="1"/>')
            xn = x + 0.7 + r.random() * 0.3
            if r.random() < 0.6:
                blk = [P(x, y0, z), P(x, y1, z), P(min(xn, x1), y1, z), P(min(xn, x1), y0, z)]
                g.append(poly(blk, r.choice(["#000", "#fff"]), f'opacity="{0.04 + r.random() * 0.09:.2f}"'))
            x = xn
    return "".join(g)


# ---------------------------------------------------------------- the great hall (far)
HALL_Z = 11.0
HALL_X = 4.6
WALL_H = 2.4
add(stone_wall_z(HALL_Z, -HALL_X, HALL_X, WALL_H, "#5a4a3a", 11))
# frieze band on the back wall (orange/teal step pattern, like the stage)
fr0, fr1 = 1.95, 2.18
band = [P(-HALL_X, fr0, HALL_Z), P(-HALL_X, fr1, HALL_Z), P(HALL_X, fr1, HALL_Z), P(HALL_X, fr0, HALL_Z)]
add(poly(band, "#4b3b2e"))
steps = []
x = -HALL_X
while x < HALL_X:
    a = P(x, fr0 + 0.02, HALL_Z)
    b = P(x + 0.18, fr1 - 0.03, HALL_Z)
    c = P(x + 0.36, fr0 + 0.02, HALL_Z)
    steps.append(f'<polyline points="{pts([a, b, c])}" fill="none" stroke="#c0643a" stroke-width="2" opacity="0.75"/>')
    d = P(x + 0.18, fr0 + 0.03, HALL_Z)
    steps.append(f'<circle cx="{d[0]:.1f}" cy="{d[1] - 3:.1f}" r="1.6" fill="#4fa49a" opacity="0.8"/>')
    x += 0.36
add("".join(steps))
# concentric carved panels behind the altar
for px in (-0.75, 0.75):
    c = P(px, 1.35, HALL_Z)
    for k, rr in enumerate([0.42, 0.32, 0.22, 0.12]):
        add(f'<ellipse cx="{c[0]:.1f}" cy="{c[1]:.1f}" rx="{F * rr * 0.55 / HALL_Z:.1f}" ry="{F * rr / HALL_Z:.1f}" fill="none" stroke="#2a1f17" stroke-width="2" opacity="0.6"/>')

# hall side walls (short visible part)
add(stone_wall_x(-HALL_X, 9.4, HALL_Z, WALL_H, +1, "#3e342b", 21))
add(stone_wall_x(HALL_X, 9.4, HALL_Z, WALL_H, -1, "#3e342b", 22))

# work lamps clamped on the back wall top — the scene's key light
lamp_l = P(-1.6, 2.5, HALL_Z - 0.1)
lamp_r = P(1.7, 2.5, HALL_Z - 0.1)
# floor of the far area
far_floor = [P(-HALL_X, 0, HALL_Z), P(HALL_X, 0, HALL_Z), P(HALL_X, 0, 9.4), P(-HALL_X, 0, 9.4)]
add(poly(far_floor, "#c4a57c"))

# altar (stepped)
def box(x0, x1, y0, y1, z0, z1, top_col, front_col, side_col):
    g = []
    g.append(poly([P(x0, y1, z0), P(x1, y1, z0), P(x1, y1, z1), P(x0, y1, z1)], top_col))
    g.append(poly([P(x0, y0, z0), P(x1, y0, z0), P(x1, y1, z0), P(x0, y1, z0)], front_col))
    # visible side faces (camera is at x=0)
    if x0 > 0:
        g.append(poly([P(x0, y0, z0), P(x0, y1, z0), P(x0, y1, z1), P(x0, y0, z1)], side_col))
    if x1 < 0:
        g.append(poly([P(x1, y0, z0), P(x1, y1, z0), P(x1, y1, z1), P(x1, y0, z1)], side_col))
    return "".join(g)


AZ = 10.2
add(box(-0.85, 0.85, 0, 0.45, AZ - 0.35, AZ + 0.4, "#8a7660", "#4a3c30", "#3a2f26"))
add(box(-0.55, 0.55, 0.45, 1.0, AZ - 0.15, AZ + 0.35, "#a08a70", "#55463a", "#3a2f26"))
# carved circles on the altar front
for px in (-0.38, 0.38):
    c = P(px, 0.23, AZ - 0.35)
    add(f'<ellipse cx="{c[0]:.1f}" cy="{c[1]:.1f}" rx="{F * 0.16 / AZ:.1f}" ry="{F * 0.15 / AZ:.1f}" fill="none" stroke="#2a2018" stroke-width="1.6" opacity="0.7"/>')

# light cones from the work lamps hitting the altar
tgt = P(0, 1.2, AZ)
for lp, spread in ((lamp_l, 120), (lamp_r, 110)):
    cone = [lp, (tgt[0] - spread, tgt[1] + 150), (tgt[0] + spread, tgt[1] + 150)]
    add(f'<polygon points="{pts(cone)}" fill="#ffd9a0" opacity="0.10" filter="url(#blur14)"/>')
for lp in (lamp_l, lamp_r):
    add(f'<rect x="{lp[0] - 9:.0f}" y="{lp[1] - 6:.0f}" width="18" height="12" rx="2" fill="#2b2b28"/>')
    add(f'<circle cx="{lp[0]:.0f}" cy="{lp[1] + 2:.0f}" r="22" fill="#ffe6b0" opacity="0.35" filter="url(#blur6)"/>')
    add(f'<circle cx="{lp[0]:.0f}" cy="{lp[1] + 2:.0f}" r="5" fill="#fffbe8"/>')

# ---------------------------------------------------------------- the hero treasure: golden sun disk
tc = P(0, 1.55, AZ)
tr = F * 0.46 / AZ        # disk radius in px
add(f'<circle cx="{tc[0]:.1f}" cy="{tc[1]:.1f}" r="{tr * 7:.1f}" fill="url(#treasureGlow)" opacity="1"/>')
# god rays from above onto the treasure (through the open, roofless ruins)
for i, (dx, w, op) in enumerate([(-260, 90, 0.5), (-120, 60, 0.6), (40, 110, 0.55), (210, 70, 0.4)]):
    top_y = -40
    ray = [(tc[0] + dx - w, top_y), (tc[0] + dx + w, top_y), (tc[0] + w * 0.35, tc[1] + 120), (tc[0] - w * 0.35, tc[1] + 120)]
    add(f'<polygon points="{pts(ray)}" fill="url(#rayG)" opacity="{op}" filter="url(#blur14)"/>')
rays = []
for k in range(16):
    a = k * math.pi * 2 / 16
    a0, a1 = a - 0.11, a + 0.11
    p0 = (tc[0] + tr * 0.98 * math.cos(a0), tc[1] + tr * 0.98 * math.sin(a0))
    p1 = (tc[0] + tr * 1.45 * math.cos(a), tc[1] + tr * 1.45 * math.sin(a))
    p2 = (tc[0] + tr * 0.98 * math.cos(a1), tc[1] + tr * 0.98 * math.sin(a1))
    rays.append(poly([p0, p1, p2], "url(#gold)"))
add("".join(rays))
add(f'<circle cx="{tc[0]:.1f}" cy="{tc[1]:.1f}" r="{tr:.1f}" fill="url(#gold)" stroke="#5a320c" stroke-width="1.5"/>')
for k, rr in enumerate([0.8, 0.62, 0.44]):
    add(f'<circle cx="{tc[0]:.1f}" cy="{tc[1]:.1f}" r="{tr * rr:.1f}" fill="none" stroke="#7a4a14" stroke-width="2.2" opacity="0.8"/>')
add(f'<circle cx="{tc[0]:.1f}" cy="{tc[1]:.1f}" r="{tr * 0.22:.1f}" fill="#1f8c84" stroke="#0b3f3a" stroke-width="1.5"/>')
add(f'<circle cx="{tc[0] - tr * 0.07:.1f}" cy="{tc[1] - tr * 0.08:.1f}" r="{tr * 0.07:.1f}" fill="#bff5ea"/>')
# stand under the disk
sb = P(0, 1.0, AZ)
add(f'<polygon points="{pts([(tc[0] - 10, tc[1] + tr * 0.9), (tc[0] + 10, tc[1] + tr * 0.9), (sb[0] + 26, sb[1]), (sb[0] - 26, sb[1])])}" fill="#6b4317"/>')
# specular flare (restrained)
add(f'<circle cx="{tc[0] - tr * 0.45:.1f}" cy="{tc[1] - tr * 0.5:.1f}" r="{tr * 0.5:.1f}" fill="#fff8de" opacity="0.55" filter="url(#blur6)"/>')
add(f'<path d="M{tc[0] - tr * 0.45:.1f},{tc[1] - tr * 0.5 - 22:.1f} v44 M{tc[0] - tr * 0.45 - 22:.1f},{tc[1] - tr * 0.5:.1f} h44" stroke="#fffbe8" stroke-width="1.4" opacity="0.8"/>')

# side treasures on the altar step (bronze tripod pot right, small mask left)
pot = P(0.42, 0.62, AZ - 0.25)
pr = F * 0.12 / (AZ - 0.25)
add(f'<ellipse cx="{pot[0]:.1f}" cy="{pot[1]:.1f}" rx="{pr:.1f}" ry="{pr * 0.8:.1f}" fill="url(#bronze)"/>')
add(f'<ellipse cx="{pot[0]:.1f}" cy="{pot[1] - pr * 0.7:.1f}" rx="{pr * 0.9:.1f}" ry="{pr * 0.2:.1f}" fill="#2a1c12"/>')

# ---------------------------------------------------------------- floor (ruin floor)
floor = [P(-HALL_X, 0, 9.4), P(HALL_X, 0, 9.4), (W + 400, H + 10), (-400, H + 10)]
add(poly(floor, "url(#floorG)"))
# flagstone joints
fj = []
for zz in [1.6, 2.2, 2.9, 3.7, 4.6, 5.6, 6.8, 8.1, 9.4]:
    a, b = P(-4.6, 0, zz), P(4.6, 0, zz)
    fj.append(f'<line x1="{a[0]:.0f}" y1="{a[1]:.0f}" x2="{b[0]:.0f}" y2="{b[1]:.0f}" stroke="#2a2018" stroke-opacity="0.35" stroke-width="{max(1, 4 / zz):.1f}"/>')
for xx in [-3.9, -3.0, -2.1, -1.2, -0.4, 0.5, 1.4, 2.3, 3.2, 4.1]:
    a, b = P(xx, 0, 1.0), P(xx, 0, 9.4)
    fj.append(f'<line x1="{a[0]:.0f}" y1="{a[1]:.0f}" x2="{b[0]:.0f}" y2="{b[1]:.0f}" stroke="#2a2018" stroke-opacity="0.28" stroke-width="1.5"/>')
add("".join(fj))
# warm light pool spilling from the hall toward the camera
pool_c = P(0, 0, 8.5)
add(f'<ellipse cx="{pool_c[0]:.0f}" cy="{pool_c[1] + 90:.0f}" rx="700" ry="260" fill="url(#floorPool)"/>')
# dust / scattered rubble on the floor
for i in range(60):
    x = rnd.uniform(-2.0, 2.0)
    z = rnd.uniform(1.4, 9.0)
    sx, sy = P(x, 0, z)
    s = rnd.uniform(0.015, 0.05) * F / z
    add(f'<ellipse cx="{sx:.0f}" cy="{sy:.0f}" rx="{s:.1f}" ry="{s * 0.45:.1f}" fill="#3a2f25" opacity="{rnd.uniform(0.25, 0.6):.2f}"/>')

# ---------------------------------------------------------------- corridor walls (near)
# right side: run of wall with a niche and a lamp; left side: partition with opening
add(stone_wall_x(-2.3, 1.05, 9.4, WALL_H, +1, "#4b3f33", 31))
add(stone_wall_x(2.3, 1.05, 9.4, WALL_H, -1, "#4f4335", 32))

# niche in right wall with a clay jar (clue being discovered by player C)
nz0, nz1, ny0, ny1 = 3.2, 3.85, 0.85, 1.45
niche = [P(2.3, ny0, nz0), P(2.3, ny1, nz0), P(2.3, ny1, nz1), P(2.3, ny0, nz1)]
add(poly(niche, "#1b1511"))
side = [P(2.3, ny0, nz0), P(2.3, ny1, nz0), P(2.42, ny1, nz0), P(2.42, ny0, nz0)]
add(poly(side, "#3a2e24"))
sill = [P(2.3, ny0, nz0), P(2.42, ny0, nz0), P(2.42, ny0, nz1), P(2.3, ny0, nz1)]
add(poly(sill, "#6a5846"))
jar = P(2.36, 1.0, 3.55)
jr = F * 0.1 / 3.5
JAR_C = jar
add(f'<g><ellipse cx="{jar[0]:.0f}" cy="{jar[1]:.0f}" rx="{jr * 0.8:.1f}" ry="{jr:.1f}" fill="#b88a5a"/>'
    f'<rect x="{jar[0] - jr * 0.3:.0f}" y="{jar[1] - jr * 1.5:.0f}" width="{jr * 0.6:.0f}" height="{jr * 0.6:.0f}" fill="#9b6f45"/>'
    f'<path d="M{jar[0] - jr * 0.75:.0f},{jar[1] - jr * 0.1:.0f} h{jr * 1.5:.0f}" stroke="#4a2e1a" stroke-width="2.5"/></g>')

# wall lamps on the right wall top (like the stage's work lights)
for lz in (5.2, 7.8):
    lp = P(2.25, 2.3, lz)
    s = F * 0.06 / lz
    add(f'<line x1="{lp[0]:.0f}" y1="{lp[1]:.0f}" x2="{lp[0]:.0f}" y2="{lp[1] + s * 1.5:.0f}" stroke="#1a1a18" stroke-width="{max(1, s * 0.3):.1f}"/>')
    add(f'<circle cx="{lp[0]:.0f}" cy="{lp[1] + s * 2:.0f}" r="{s * 2.2:.1f}" fill="#ffcf88" opacity="0.35" filter="url(#blur6)"/>')
    add(f'<rect x="{lp[0] - s * 0.6:.0f}" y="{lp[1] + s * 1.5:.0f}" width="{s * 1.2:.1f}" height="{s:.1f}" fill="#ffe2a8"/>')

# excavation crate + tray at left mid (expedition equipment)
add(box(-2.1, -1.45, 0, 0.5, 5.6, 6.2, "#a07448", "#7a5434", "#5a3d26"))
for yy in (0.17, 0.34):
    a, b = P(-2.1, yy, 5.6), P(-1.45, yy, 5.6)
    add(f'<line x1="{a[0]:.0f}" y1="{a[1]:.0f}" x2="{b[0]:.0f}" y2="{b[1]:.0f}" stroke="#4a3020" stroke-width="2"/>')
tag = P(-1.78, 0.25, 5.6)
add(f'<rect x="{tag[0] - 12:.0f}" y="{tag[1] - 6:.0f}" width="24" height="12" fill="#e8e0cc" opacity="0.9"/>')

# rubble near camera
for (x, z, s) in [(-1.95, 1.9, 0.28), (-1.7, 2.3, 0.18), (2.05, 1.7, 0.32), (1.85, 2.15, 0.2)]:
    c = P(x, 0.08, z)
    rr = F * s / z
    add(f'<polygon points="{pts([(c[0] - rr, c[1] + rr * 0.35), (c[0] - rr * 0.6, c[1] - rr * 0.55), (c[0] + rr * 0.4, c[1] - rr * 0.7), (c[0] + rr, c[1] + rr * 0.3)])}" fill="#3c3229"/>')
    add(f'<polygon points="{pts([(c[0] - rr * 0.6, c[1] - rr * 0.55), (c[0] + rr * 0.4, c[1] - rr * 0.7), (c[0] + rr * 0.1, c[1] - rr * 0.1)])}" fill="#7a6a58"/>')


# ---------------------------------------------------------------- people
def person(cx, cy, s, parts, rim_dir=(-1, -1), rim_col="#ffc98a", body_col="#1a1d22"):
    """Draw a figure from stroked limbs. Coordinates in local units (cm-ish),
    origin at the feet centre, y down. s = px per unit."""
    def tr(p):
        return (cx + p[0] * s, cy + p[1] * s)

    def shapes(col, dx=0, dy=0, accent=True):
        g = []
        for kind, data in parts:
            if kind == "limb":
                ptsl, w, c = data
                c = col if (c is None or not accent) else c
                d = "M" + " L".join(f"{tr(p)[0] + dx:.1f},{tr(p)[1] + dy:.1f}" for p in ptsl)
                g.append(f'<path d="{d}" fill="none" stroke="{c}" stroke-width="{w * s:.1f}" stroke-linecap="round" stroke-linejoin="round"/>')
            elif kind == "poly":
                ptsl, c = data
                c = col if (c is None or not accent) else c
                g.append(f'<polygon points="{pts([(tr(p)[0] + dx, tr(p)[1] + dy) for p in ptsl])}" fill="{c}" stroke="{c}" stroke-width="{2 * s:.1f}" stroke-linejoin="round"/>')
            elif kind == "ell":
                (x, y, rx, ry, rot), c = data
                c = col if (c is None or not accent) else c
                p = tr((x, y))
                g.append(f'<ellipse cx="{p[0] + dx:.1f}" cy="{p[1] + dy:.1f}" rx="{rx * s:.1f}" ry="{ry * s:.1f}" transform="rotate({rot} {p[0] + dx:.1f} {p[1] + dy:.1f})" fill="{c}"/>')
        return "".join(g)

    rim = shapes(rim_col, accent=False)
    off = max(1.4, s * 0.9)
    body = shapes(body_col, -rim_dir[0] * off, -rim_dir[1] * off)
    shadow = f'<ellipse cx="{cx:.0f}" cy="{cy + 2:.0f}" rx="{40 * s:.0f}" ry="{7 * s:.0f}" fill="#0b0805" opacity="0.55" filter="url(#blur6)"/>'
    return shadow + rim + body


def headset(x, y, facing, col="#2a2d33", strap="#111"):
    """VR/MR headset seen roughly from behind/side. facing: +1 looks right, -1 left."""
    return [
        ("limb", ([(x - 9 * facing, y - 3), (x + 2 * facing, y - 6)], 3.2, strap)),
        ("ell", ((x + 9 * facing, y + 1, 7.5, 5.2, 0), col)),
    ]


# Player A — centre-left, turning back, pointing at the sun disk (explorer who found it)
ax, ay = P(-0.95, 0, 4.1)
sA = F / 4.1 / 100 * 1.0
A = [
    ("limb", ([(-16, 0), (-10, -44), (-4, -86)], 12, "#1f2328")),     # back leg (mid-stride)
    ("limb", ([(14, -3), (11, -46), (4, -86)], 12, "#1f2328")),      # front leg
    ("poly", ([(-15, -86), (16, -86), (18, -122), (14, -146), (-14, -146), (-19, -122)], "#2b3a4a")),  # jacket
    ("limb", ([(-14, -140), (-24, -114), (-30, -92)], 8.5, "#2b3a4a")),  # left arm down (relaxed)
    ("limb", ([(13, -141), (40, -152), (70, -160)], 8.5, "#2b3a4a")),   # right arm pointing
    ("ell", ((74, -161, 5, 4, -10), "#d9a77a")),                     # pointing hand
    ("limb", ([(74, -161), (84, -163)], 2.4, "#d9a77a")),            # index finger
    ("limb", ([(0, -146), (2, -152)], 9, "#c89068")),                # neck
    ("ell", ((2, -163, 10, 11.5, 0), "#2a1f1a")),                    # head (hair)
] + headset(2, -164, +1) + [
    ("limb", ([(-16, -138), (-15, -100)], 2.2, "#e04848")),          # player colour stripe (red)
]
add(person(ax, ay, sA, A, rim_dir=(1, -0.6)))

# Player B — centre-right, carrying a bronze jar with both hands, looking back at A
bx, by = P(1.05, 0, 4.7)
sB = F / 4.7 / 100
B = [
    ("limb", ([(-13, 0), (-8, -42), (-4, -84)], 12.5, "#3a3a40")),
    ("limb", ([(12, -1), (9, -42), (4, -84)], 12.5, "#3a3a40")),
    ("poly", ([(-16, -84), (15, -84), (18, -120), (13, -145), (-14, -145), (-18, -120)], "#6b4a2e")),  # canvas jacket
    ("limb", ([(-14, -138), (-22, -116), (-12, -100)], 8.5, "#6b4a2e")),
    ("limb", ([(13, -138), (22, -116), (14, -100)], 8.5, "#6b4a2e")),
    ("limb", ([(0, -145), (-1, -151)], 9, "#b98560")),
    ("ell", ((-2, -162, 10, 11.5, 0), "#3b2a20")),
] + headset(-2, -163, -1) + [
    ("limb", ([(16, -136), (15, -100)], 2.2, "#4a86e0")),            # player colour stripe (blue)
]
add(person(bx, by, sB, B, rim_dir=(-1, -0.6)))
# the jar B carries (drawn over the hands)
jx, jy = bx + 1 * sB, by - 104 * sB
add(f'<ellipse cx="{jx:.0f}" cy="{jy:.0f}" rx="{17 * sB:.1f}" ry="{15 * sB:.1f}" fill="url(#bronze)"/>')
add(f'<rect x="{jx - 8 * sB:.0f}" y="{jy - 22 * sB:.0f}" width="{16 * sB:.0f}" height="{8 * sB:.0f}" fill="#8a5a30"/>')
add(f'<ellipse cx="{jx:.0f}" cy="{jy - 22 * sB:.0f}" rx="{10 * sB:.1f}" ry="{2.5 * sB:.1f}" fill="#2a1c12"/>')
add(f'<ellipse cx="{jx - 15 * sB:.0f}" cy="{jy - 2 * sB:.0f}" rx="{4 * sB:.1f}" ry="{3.5 * sB:.1f}" fill="#c89068"/>')
add(f'<ellipse cx="{jx + 15 * sB:.0f}" cy="{jy - 2 * sB:.0f}" rx="{4 * sB:.1f}" ry="{3.5 * sB:.1f}" fill="#c89068"/>')
# a teal-green verdigris band
add(f'<path d="M{jx - 16 * sB:.0f},{jy + 3 * sB:.0f} q{16 * sB:.0f},{6 * sB:.0f} {32 * sB:.0f},0" stroke="#4f9a86" stroke-width="{2.2 * sB:.1f}" fill="none" opacity="0.8"/>')

# Player C — right, crouching at the niche, palm beam (green) touching the jar: the "clue" moment
cx_, cy_ = P(1.55, 0, 3.05)
sC = F / 3.05 / 100
C = [
    ("limb", ([(-10, 0), (-22, -26), (-6, -48)], 12.5, "#2e3b33")),  # bent leg
    ("limb", ([(14, -1), (20, -30), (2, -52)], 12.5, "#2e3b33")),
    ("poly", ([(-14, -50), (14, -52), (18, -80), (10, -100), (-12, -98), (-18, -76)], "#3f5a4a")),
    ("limb", ([(-10, -94), (-22, -72), (-16, -58)], 8.2, "#3f5a4a")),   # resting arm on knee
    ("limb", ([(10, -95), (28, -96), (42, -104)], 8.2, "#3f5a4a")),     # reaching arm
    ("ell", ((46, -105, 4.5, 4, -10), "#d9a77a")),
    ("limb", ([(0, -100), (2, -106)], 8.5, "#c89068")),
    ("ell", ((3, -116, 10, 11.5, 0), "#1c1612")),
] + headset(3, -117, +1) + [
    ("limb", ([(-15, -92), (-16, -60)], 2.2, "#52c98a")),
]
add(person(cx_, cy_, sC, C, rim_dir=(-1, -0.4)))
hand = (cx_ + 46 * sC, cy_ - 105 * sC)
# short palm beam to the jar + soft outline glow on the jar (in-world feedback, not HUD)
add(f'<line x1="{hand[0]:.0f}" y1="{hand[1]:.0f}" x2="{JAR_C[0] - 4:.0f}" y2="{JAR_C[1]:.0f}" stroke="url(#beamA)" stroke-width="3" stroke-linecap="round"/>')
add(f'<line x1="{hand[0]:.0f}" y1="{hand[1]:.0f}" x2="{JAR_C[0] - 4:.0f}" y2="{JAR_C[1]:.0f}" stroke="#7cf0b4" stroke-width="9" opacity="0.18" filter="url(#blur2)"/>')
add(f'<ellipse cx="{JAR_C[0]:.0f}" cy="{JAR_C[1]:.0f}" rx="{jr * 0.95:.1f}" ry="{jr * 1.15:.1f}" fill="none" stroke="#9cf5c8" stroke-width="2" opacity="0.8"/>')

# Player D — foreground left, back view, arm raised: "over here!" signalling to the team
dx_, dy_ = P(-1.35, 0, 1.75)
sD = F / 1.75 / 100
D = [
    ("limb", ([(-8, 0), (-7, -44), (-4, -86)], 13, "#22252a")),
    ("limb", ([(9, 0), (8, -44), (4, -86)], 13, "#22252a")),
    ("poly", ([(-17, -86), (17, -86), (20, -122), (16, -147), (-16, -147), (-20, -122)], "#3a2f3d")),
    ("limb", ([(15, -142), (30, -166), (36, -196)], 9, "#3a2f3d")),      # raised arm
    ("ell", ((37, -200, 5.5, 6.5, 15), "#d0996c")),                     # open palm
    ("limb", ([(33, -205), (29, -215)], 2.2, "#d0996c")),               # fingers spread
    ("limb", ([(36, -206), (35, -218)], 2.2, "#d0996c")),
    ("limb", ([(40, -206), (42, -217)], 2.2, "#d0996c")),
    ("limb", ([(43, -203), (48, -211)], 2.1, "#d0996c")),
    ("limb", ([(32, -198), (25, -201)], 2.3, "#d0996c")),
    ("limb", ([(-15, -140), (-24, -114), (-26, -92)], 9, "#3a2f3d")),
    ("limb", ([(0, -147), (0, -153)], 9.5, "#b07a55")),
    ("ell", ((0, -165, 10.5, 12, 0), "#17110d")),
] + [
    ("limb", ([(-10, -168), (10, -168)], 3.4, "#0d0d0d")),              # strap across back of head
    ("ell", ((0, -168, 5, 4, 0), "#2a2d33")),                           # rear battery pod
    ("limb", ([(-17, -140), (-16, -100)], 2.4, "#e8c440")),             # player colour (yellow)
]
add(f'<g filter="url(#dof)">{person(dx_, dy_, sD, D, rim_dir=(1, -0.8))}</g>')

# ---------------------------------------------------------------- the real floor coming through (MR)
# At the very bottom the ruin floor gives way to the booth carpet and boundary tape.
add('<g>')
carpet = [(-50, H - 70), (W + 50, H - 110), (W + 50, H + 10), (-50, H + 10)]
add(poly(carpet, "#2a2d31", 'opacity="0.92"'))
add(f'<polygon points="{pts(carpet)}" fill="#fff" filter="url(#grain)" opacity="0.8"/>')
# soft blend edge so the virtual floor dissolves into the real one
add(f'<polyline points="{pts([(-50, H - 70), (W + 50, H - 110)])}" stroke="#2a2d31" stroke-width="40" filter="url(#blur14)" opacity="0.9"/>')
# yellow/black boundary tape (real booth)
tape0, tape1 = (-50, H - 40), (W + 50, H - 78)
add(f'<line x1="{tape0[0]}" y1="{tape0[1]}" x2="{tape1[0]}" y2="{tape1[1]}" stroke="#e6c23a" stroke-width="16"/>')
add(f'<line x1="{tape0[0]}" y1="{tape0[1]}" x2="{tape1[0]}" y2="{tape1[1]}" stroke="#151515" stroke-width="16" stroke-dasharray="18 18"/>')
add('</g>')

# ---------------------------------------------------------------- grading
add(f'<rect width="{W}" height="300" fill="url(#vignetteTop)" opacity="0.55"/>')
add(f'<rect width="{W}" height="{H}" fill="url(#vignette)"/>')
add(f'<rect width="{W}" height="{H}" fill="#ff9a40" opacity="0.04" style="mix-blend-mode:screen"/>')
add(f'<rect width="{W}" height="{H}" filter="url(#grain)"/>')
add('</svg>')

Path(__file__).with_name("keyvisual.svg").write_text("\n".join(out), encoding="utf-8")
print("wrote keyvisual.svg")
