"""Walkability, clear widths and approach to every treasure slot, from the colliders in stage_layout.json.

  blender -b --factory-startup --python check_context_paths.py [-- Output]
(plain Python also works). Writes Output/approach_checks.json and Output/floorplan.svg.

Human proxy: a 0.30 m radius disc, flood-filled (8-connected) on a 0.05 m grid from the practice row, against every collider
that stands in the body's height range (0.05-1.80 m; boxes with yaw).
Clear width: the same flood (0.025 m grid) with a 0.575 m disc (1.15 m clear) must connect the main loop
(entrance - both corridors - cross corridor - hall) and reach both room doorways.
Approach: for each slot, a reachable stand point at least 0.70 m from the venue edges, within 1.20 m
horizontally, whose palm (1.1 m) has a clear line to the slot (above the surface) through the beam-blocking
boxes, within the 1.5 m beam.
"""
import json, math, sys
from collections import deque
from pathlib import Path

ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = Path(__file__).resolve().parent / (ARGS[0] if ARGS else 'Output')
GRID, HUMAN_R, WIDE_R = .05, .30, .575
EDGE_MIN, STAND_MAX, BEAM, PALM_H = .70, 1.20, 1.50, 1.10
W2, DEPTH = 2.5, 11.0


def boxes(data, body=True):
    out = []
    for c in data['colliders']:
        f = c['footprint_logical']
        if body and (f['h1'] < .05 or f['h0'] > 1.80): continue
        out.append((f['center_xd'][0], f['center_xd'][1], f['size_xd'][0] / 2, f['size_xd'][1] / 2, math.cos(f['angle_rad']), math.sin(f['angle_rad']), f['h0'], f['h1'], c))
    return out


def dist_box(x, d, b):
    cx, cd, hx, hd, ca, sa = b[:6]
    dx, dd = x - cx, d - cd
    lx, ld = dx * ca + dd * sa, -dx * sa + dd * ca
    return math.hypot(max(abs(lx) - hx, 0), max(abs(ld) - hd, 0))


def flood(obs, r, start, grid=GRID):
    ni, nj = int(round(2 * W2 / grid)), int(round(DEPTH / grid))
    buckets = {}
    for b in obs:   # 0.5 m buckets, each box registered in every bucket its disc-inflated bounds touch
        rad = math.hypot(b[2], b[3]) + r
        for bi in range(int((b[0] - rad + W2) // .5), int((b[0] + rad + W2) // .5) + 1):
            for bj in range(int((b[1] - rad) // .5), int((b[1] + rad) // .5) + 1): buckets.setdefault((bi, bj), []).append(b)
    def ok(i, j):
        x, d = i * grid - W2, j * grid
        near = buckets.get((int((x + W2) // .5), int(d // .5)), [])
        return W2 - abs(x) >= r - 1e-9 and r - 1e-9 <= d <= DEPTH - r + 1e-9 and all(dist_box(x, d, b) >= r - 1e-6 for b in near)
    valid = {(i, j) for i in range(ni + 1) for j in range(nj + 1) if ok(i, j)}
    s = min(valid, key=lambda n: math.hypot(n[0] * grid - W2 - start[0], n[1] * grid - start[1]))
    prev = {s: None}; q = deque([s])
    while q:
        i, j = q.popleft()
        for n in ((i + 1, j), (i - 1, j), (i, j + 1), (i, j - 1), (i + 1, j + 1), (i - 1, j - 1), (i + 1, j - 1), (i - 1, j + 1)):
            if n in valid and n not in prev: prev[n] = (i, j); q.append(n)
    return {(round(i * grid / GRID, 6), round(j * grid / GRID, 6)) for i, j in prev} if grid != GRID else prev


def seg_hits_box(p, q, b):
    """3D segment p->q (x, d, h) against a yawed box (slab test in the box frame)."""
    cx, cd, hx, hd, ca, sa, h0, h1 = b[:8]
    def loc(v):
        dx, dd = v[0] - cx, v[1] - cd
        return (dx * ca + dd * sa, -dx * sa + dd * ca, v[2] - (h0 + h1) / 2)
    a, e = loc(p), loc(q); half = (hx, hd, (h1 - h0) / 2)
    t0, t1 = 0.0, 1.0
    for k in range(3):
        dk = e[k] - a[k]
        if abs(dk) < 1e-12:
            if abs(a[k]) > half[k]: return False
            continue
        ta, tb = (-half[k] - a[k]) / dk, (half[k] - a[k]) / dk
        if ta > tb: ta, tb = tb, ta
        t0, t1 = max(t0, ta), min(t1, tb)
        if t0 > t1: return False
    return t1 > 1e-4 and t0 < 1 - 1e-4


def run():
    data = json.loads((OUT / 'stage_layout.json').read_text(encoding='utf-8'))
    obs = boxes(data); beam = [b for b in boxes(data, body=False) if b[8]['beam_blocking']]
    human = flood(obs, HUMAN_R, (0, 2.6)); wide = flood(obs, WIDE_R, (0, 2.6), grid=.025)
    pt = lambda n: (round(n[0] * GRID - W2, 3), round(n[1] * GRID, 3))
    lay = data['layout']
    loop = {'entrance': (0, 2.6), 'corridor_w': (-1.70, 6.0), 'corridor_e': (1.70, 6.0), 'cross': (0, 8.8), 'hall': (0, 9.5),
            'front_room_doorway': (-lay['block']['outer_x'] + .1, sum(lay['block']['front_room_door']['depth']) / 2),
            'back_room_doorway': (lay['block']['outer_x'] - .1, sum(lay['block']['back_room_door']['depth']) / 2)}
    near = lambda prev, p, tol=.04: any(math.hypot(pt(n)[0] - p[0], pt(n)[1] - p[1]) <= tol for n in prev)
    wide_ok = {k: near(wide, p) for k, p in loop.items()}
    human_ok = {k: near(human, p) for k, p in loop.items()}
    results = []
    for m in data['markers']:
        if m['role'] != 'treasure_slot': continue
        sx, sh, sd = m['position_unity_xyz']; tgt = (sx, sd, sh + .10)
        best = None
        for n in human:
            x, d = pt(n)
            if min(W2 - abs(x), d, DEPTH - d) < EDGE_MIN: continue
            hd = math.hypot(x - sx, d - sd)
            if hd > STAND_MAX: continue
            palm = (x + (sx - x) * min(1, .35 / max(hd, 1e-6)), d + (sd - d) * min(1, .35 / max(hd, 1e-6)), PALM_H)   # arm reaching toward the slot
            dist = math.dist(palm, tgt)
            if dist > BEAM: continue
            if any(seg_hits_box(palm, tgt, b) for b in beam): continue
            if best is None or hd < best['stand_distance_m']:
                best = dict(stand_xd=[x, d], stand_distance_m=round(hd, 3), palm_to_slot_m=round(dist, 3))
        results.append(dict(slot=m['name'], zone=m['zone'], host=m['host_type'], approach=best, reachable=best is not None))
    edge_slots = [m['name'] for m in data['markers'] if m['role'] == 'treasure_slot' and
                  (abs(m['position_unity_xyz'][0]) > 2.5 or not 0 <= m['position_unity_xyz'][2] <= 11)]
    report = dict(human_radius_m=HUMAN_R, wide_radius_m=WIDE_R, grid_m=GRID, walkable_cells=len(human),
                  main_loop_clear_1_15m=wide_ok, main_loop_walkable=human_ok, slots_reachable=sum(r['reachable'] for r in results),
                  slots_total=len(results), slots_outside_venue=edge_slots, approaches=results,
                  rules=dict(stand_min_from_edge_m=EDGE_MIN, stand_max_horizontal_m=STAND_MAX, beam_m=BEAM, palm_height_m=PALM_H),
                  limitation='Static boxes. Stone relief and decor are not in the colliders; verify_stage.py measures the real clear width with rays.')
    (OUT / 'approach_checks.json').write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding='utf-8')
    svg_plan(data, report, human)
    print('LOOP clear 1.15:', wide_ok)
    print('Slots reachable: %d / %d' % (report['slots_reachable'], report['slots_total']),
          [(r['slot'], r['zone'], r['host']) for r in results if not r['reachable']])
    return report


def svg_plan(data, report, human):
    S = 62; X0, Y0 = 30, 60
    P = lambda x, d: (X0 + (x + W2) * S, Y0 + (DEPTH - d) * S)   # entrance at the bottom, +x to the right (Unity top view)
    out = ['<svg xmlns="http://www.w3.org/2000/svg" width="%d" height="%d">' % (X0 * 2 + 5 * S + 300, Y0 + 11 * S + 40),
           '<rect width="100%" height="100%" fill="#1d2422"/>', '<g font-family="sans-serif">',
           '<text x="%d" y="34" font-size="20" fill="#ece4cf">TREASURE HAUL STAGE (top view, Unity X right, entrance bottom)</text>' % X0]
    a, b = P(-W2, DEPTH); out.append('<rect x="%g" y="%g" width="%g" height="%g" fill="#2b3331" stroke="#6b7a74"/>' % (a, b, 5 * S, 11 * S))
    for n in human:
        x, d = n[0] * GRID - W2, n[1] * GRID
        if n[0] % 2 == 0 and n[1] % 2 == 0:
            a, b = P(x, d); out.append('<circle cx="%g" cy="%g" r="1.2" fill="#46605a"/>' % (a, b))
    for c in data['colliders']:
        f = c['footprint_logical']; x, d = f['center_xd']; w, l = f['size_xd']; ang = f['angle_rad']
        ca, sa = math.cos(ang), math.sin(ang)
        pts = [P(x + lx * ca - ld * sa, d + lx * sa + ld * ca) for lx, ld in ((-w / 2, -l / 2), (w / 2, -l / 2), (w / 2, l / 2), (-w / 2, l / 2))]
        col = '#8f8a7c' if c['beam_blocking'] else ('#6d5a3b' if c['kind'] != 'niche_lip' else '#b9ad8f')
        out.append('<polygon points="%s" fill="%s"/>' % (' '.join('%g,%g' % p for p in pts), col))
    reach = {r['slot']: r for r in report['approaches']}
    for m in data['markers']:
        x, h, d = m['position_unity_xyz']; a, b = P(x, d)
        if m['role'] == 'treasure_slot':
            col = '#e8b04a' if 'M' in m['allowed_classes'] else '#7fd4c4'
            if 'S' in m['allowed_classes'] and 'M' in m['allowed_classes']: col = '#f4e27a'
            r = reach[m['name']]
            if r['approach']:
                sx, sd = r['approach']['stand_xd']; c2 = P(sx, sd)
                out.append('<line x1="%g" y1="%g" x2="%g" y2="%g" stroke="#ffffff" stroke-opacity=".35"/>' % (a, b, c2[0], c2[1]))
            out.append('<circle cx="%g" cy="%g" r="%g" fill="%s" stroke="%s" stroke-width="2"/>' % (a, b, 7 if 'M' in m['allowed_classes'] else 5, col, '#ff4040' if m.get('hidden_intent') else '#222'))
            out.append('<text x="%g" y="%g" font-size="9" fill="#ffffff">%s</text>' % (a + 7, b - 5, m['name'][-2:]))
        elif m['role'] == 'tutorial':
            out.append('<rect x="%g" y="%g" width="8" height="8" fill="#5fa8ff"/>' % (a - 4, b - 4))
        elif m['role'] == 'light_anchor':
            out.append('<circle cx="%g" cy="%g" r="3" fill="#ffd27a"/>' % (a, b))
    sl = data['slots']
    notes = ['5 x 11 m, walls 2.4 m, no ceiling', '%d slots: %s' % (sl['count'], ', '.join('%s %d' % kv for kv in sl['by_class'].items())),
             'hidden intent (red ring): %d' % sl['hidden_intent'], 'teal = S, yellow = S/M, orange = M', 'grey = beam-blocking walls',
             'brown = furniture (beam passes)', 'dots = walkable (r 0.30 m)', 'white line = stand point for the slot',
             'reachable: %d / %d' % (report['slots_reachable'], report['slots_total']),
             '1.15 m loop: %s' % ('OK' if all(report['main_loop_clear_1_15m'].values()) else 'CHECK'), 'blue squares = practice row', 'yellow dots = lamps']
    for i, n in enumerate(notes):
        out.append('<text x="%d" y="%d" font-size="13" fill="#ece4cf">%s</text>' % (X0 + 5 * S + 20, Y0 + 20 + i * 24, n))
    out.append('</g></svg>')
    (OUT / 'floorplan.svg').write_text('\n'.join(out), encoding='utf-8')


if __name__ == '__main__':
    run()
