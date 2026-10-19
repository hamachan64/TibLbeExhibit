"""Render review images of a stage revision headless (the blend is not saved).

  blender -b RevisionN/Stage_Review.blend --python render_search_views.py -- RevisionN [views...]
Views: eye (entrance sightline), eye_west, top, overview, peek (one tile per hidden
find, from the measured best close viewpoint in search_visibility.json).
"""
import bpy, json, math, sys
from pathlib import Path
from mathutils import Vector

HERE = Path(__file__).resolve().parent
ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else ['Revision6']
OUT = HERE / ARGS[0]
VIEWS = ARGS[1:] or ['eye', 'eye_west', 'top', 'overview', 'peek']
scene = bpy.context.scene
cam = scene.camera
for engine in ('BLENDER_EEVEE_NEXT', 'BLENDER_EEVEE'):
    try: scene.render.engine = engine; break
    except TypeError: pass
try: scene.eevee.taa_render_samples = 32
except AttributeError: pass
scene.render.image_settings.file_format = 'PNG'
scene.render.resolution_percentage = 100


def aim(loc, target, persp=True, lens=22, ortho=12.3):
    cam.location = loc
    cam.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
    cam.data.type = 'PERSP' if persp else 'ORTHO'
    if persp: cam.data.lens = lens
    else: cam.data.ortho_scale = ortho


def shot(name, size):
    scene.render.resolution_x, scene.render.resolution_y = size
    scene.render.filepath = str(OUT / (name + '.png'))
    bpy.ops.render.render(write_still=True)


for view in VIEWS:
    if view == 'eye':  # adult standing on the practice row, looking into the ruins
        aim((0.0, -2.6, 1.65), (0, -8, .3)); shot('eye', (1500, 950))
    elif view == 'eye_west':
        aim((-1.9, -2.7, 1.65), (-.6, -7.5, .3)); shot('eye_west', (1500, 950))
    elif view == 'top':
        aim((0, -5.5, 16), (0, -5.5, 0), persp=False, ortho=12.3); shot('top', (800, 1500))
    elif view == 'overview':
        aim((9, 6, 10), (0, -5.1, 0), persp=False, ortho=13.9); shot('overview', (1550, 1250))
    elif view == 'peek':
        data = json.loads((OUT / 'search_visibility.json').read_text(encoding='utf-8'))
        layout = json.loads((OUT / 'stage_layout.json').read_text(encoding='utf-8'))
        pos = {m['name']: m['position_blender_xyz'] for m in layout['markers']}
        (OUT / 'peek').mkdir(exist_ok=True)
        for r in data['slots']:
            if not r['hidden_intent'] or not r['best_near_view']: continue
            ex, eh, ed = r['best_near_view']['eye_unity_xyz']
            aim((ex, -ed, eh), pos[r['slot']], lens=30); shot('peek/' + r['slot'], (480, 360))
