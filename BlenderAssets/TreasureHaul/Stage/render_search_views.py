"""Render the review images of the stage headless (the blend is not saved).

  blender -b Output/Stage_Review.blend --python render_search_views.py -- [out_dir] [view ...]
out_dir defaults to Output (relative to this folder, or absolute). Views (default: the committed set):
  01_entrance_eye   practice row, eye 1.65 m          02_corridor_w / 03_corridor_e   walking the side corridors
  04_front_room_door / 05_front_room    06_back_room_door / 07_back_room    08_cross_corridor
  09_hall           10_hall_bays (east, from the opening)   11_top (orthographic)   12_overview (oblique)
  extra: hall_bay_w, sarcophagus, slots (one close-up per slot into out_dir/slots/, from search_visibility.json)
JPEG 1280 x 800 (q 82). The lens is wide (16 mm, ~97 deg horizontal) to approximate a headset's field of view.
"""
import bpy, json, math, sys
from pathlib import Path
from mathutils import Vector

HERE = Path(__file__).resolve().parent
ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = Path(ARGS[0]) if ARGS and (len(ARGS[0]) > 2 and (ARGS[0][1] == ':' or ARGS[0].startswith('/'))) else HERE / (ARGS[0] if ARGS else 'Output')
VIEWS = ARGS[1:] or ['01_entrance_eye', '02_corridor_w', '03_corridor_e', '04_front_room_door', '05_front_room', '06_back_room_door',
                     '07_back_room', '08_cross_corridor', '09_hall', '10_hall_bays', '11_top', '12_overview']
OUT.mkdir(parents=True, exist_ok=True)
scene = bpy.context.scene
cam = scene.camera
scene.render.image_settings.file_format = 'JPEG'; scene.render.image_settings.quality = 82
scene.render.resolution_percentage = 100
try: scene.eevee.taa_render_samples = 32
except AttributeError: pass


def L(x, d, h): return Vector((x, -d, h))


def aim(loc, target, lens=16, ortho=None):
    cam.location = L(*loc)
    cam.rotation_euler = (L(*target) - cam.location).to_track_quat('-Z', 'Y').to_euler()
    cam.data.type = 'ORTHO' if ortho else 'PERSP'
    if ortho: cam.data.ortho_scale = ortho
    else: cam.data.lens = lens
    cam.data.clip_start = .05


def shot(name, size=(1280, 800)):
    scene.render.resolution_x, scene.render.resolution_y = size
    scene.render.filepath = str(OUT / (name + '.jpg'))
    bpy.ops.render.render(write_still=True)


SHOTS = {
    '01_entrance_eye': ((0, 2.5, 1.65), (0, 8.0, .9)),
    '02_corridor_w': ((-1.62, 3.35, 1.65), (-1.75, 8.6, 1.0)),
    '03_corridor_e': ((1.62, 3.35, 1.65), (1.75, 8.6, 1.0)),
    '04_front_room_door': ((-1.72, 5.3, 1.65), (.45, 5.2, .7)),
    '05_front_room': ((-.62, 5.62, 1.65), (.35, 4.4, .7)),
    '06_back_room_door': ((1.72, 7.2, 1.65), (-.5, 6.8, .8)),
    '07_back_room': ((.55, 7.15, 1.65), (-.65, 6.6, .8)),
    '08_cross_corridor': ((-1.70, 8.05, 1.65), (1.9, 9.0, .8)),
    '09_hall': ((0, 8.55, 1.65), (0, 10.6, .9)),
    '10_hall_bays': ((.15, 9.95, 1.65), (2.0, 10.4, .6)),
    'hall_bay_w': ((-.15, 9.95, 1.65), (-2.0, 10.3, .6)),
    'sarcophagus': ((-.25, 5.45, 1.55), (.5, 5.45, .3)),
}

for view in VIEWS:
    if view in SHOTS:
        loc, tgt = SHOTS[view]; aim(loc, tgt); shot(view)
    elif view == '11_top':
        aim((0, 5.5, 20), (0, 5.5001, 0), ortho=11.4); shot(view, (760, 1600))
    elif view == '12_overview':
        aim((5.2, -2.2, 7.8), (0, 6.0, 0), lens=24); shot(view, (1280, 960))
    elif view == 'slots':
        data = json.loads((HERE / 'Output' / 'search_visibility.json').read_text(encoding='utf-8'))
        layout = json.loads((HERE / 'Output' / 'stage_layout.json').read_text(encoding='utf-8'))
        pos = {m['name']: m['position_unity_xyz'] for m in layout['markers']}
        (OUT / 'slots').mkdir(exist_ok=True)
        for r in data['slots']:
            if not r.get('best_near_view'): continue
            ex, eh, ed = r['best_near_view']['eye_unity_xyz']; px, ph, pd = pos[r['slot']]
            aim((ex, ed, eh), (px, pd, ph + .1), lens=24); shot('slots/' + r['slot'], (480, 360))
