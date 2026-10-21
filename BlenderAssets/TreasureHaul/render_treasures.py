"""Lineup render of every treasure in Treasures/ at true relative size.

Headless, in a throwaway session (nothing is saved except the PNG):
  blender -b --factory-startup --python render_treasures.py
Writes Treasures/lineup.jpg (S on the lower row, M on the upper row). Stage previews: Stage/render_search_views.py.
"""
import bpy, json, math
from pathlib import Path
from mathutils import Vector
HERE = Path(__file__).resolve().parent
SRC = HERE / 'Treasures'
_stats = json.loads((SRC / 'stats.json').read_text(encoding='utf-8'))
_new = [m for m in _stats if 'treasure_class' in m]          # handle-free models only (the old set has no class field)
_names = [m['model'] for m in (_new or _stats)]
ROWS = [[m['model'] for m in (_new or _stats) if m.get('treasure_class', 'S') == 'S'], [m['model'] for m in (_new or _stats) if m.get('treasure_class') == 'M']]
ROWS = [r for r in ROWS if r]
scene = bpy.context.scene
for o in list(scene.objects): bpy.data.objects.remove(o, do_unlink=True)
scene.world = bpy.data.worlds.new('TH_Lineup_World'); scene.world.use_nodes = True
bg = next(n for n in scene.world.node_tree.nodes if n.type == 'BACKGROUND')
bg.inputs['Color'].default_value = (.12, .15, .17, 1); bg.inputs['Strength'].default_value = .45
scene.view_settings.view_transform = 'Standard'   # closest to Unity URP without tonemapping
try: scene.render.engine = 'CYCLES'; scene.cycles.samples = 32
except TypeError: pass

width = 0; tallest = 0; z_row = 0.0
for names in ROWS:                       # S row at the bottom, M row stacked above it
    x = 0; row_h = 0
    for name in names:
        with bpy.data.libraries.load(str(SRC / (name + '.blend')), link=False) as (src, dst): dst.objects = src.objects
        objects = [o for o in dst.objects if o]
        for o in objects: scene.collection.objects.link(o)
        root = next(o for o in objects if o.type == 'EMPTY' and o.parent is None)
        root.location = (0, 0, 0); bpy.context.view_layer.update()
        pts = [o.matrix_world @ Vector(c) for o in objects if o.type == 'MESH' for c in o.bound_box]
        lo, hi = min(p.x for p in pts), max(p.x for p in pts); row_h = max(row_h, max(p.z for p in pts))
        root.location = (x - lo, 0, z_row); x += hi - lo + .06
    width = max(width, x); z_row += row_h + .08
tallest = z_row - .08
for name, pos, power, size in [('Key', (width * .5 + 1, -3, 3), 500, 4), ('Rim', (width * .5 + 1, 3, 2), 200, 3)]:
    data = bpy.data.lights.new(name, 'AREA'); data.energy = power; data.size = size
    o = bpy.data.objects.new(name, data); scene.collection.objects.link(o); o.location = pos
    o.rotation_euler = (Vector((width / 2, 0, tallest / 2)) - o.location).to_track_quat('-Z', 'Y').to_euler()
data = bpy.data.cameras.new('LineupCamera'); cam = bpy.data.objects.new('LineupCamera', data); scene.collection.objects.link(cam)
cam.location = (width / 2, -4, tallest / 2); cam.rotation_euler = (math.pi / 2, 0, 0)
data.type = 'ORTHO'; data.ortho_scale = width + .1; scene.camera = cam
scene.render.resolution_x = 1800; scene.render.resolution_y = int(1800 * (tallest + .06) / (width + .1)); scene.render.resolution_percentage = 100
data.shift_y = 0.0
scene.render.image_settings.file_format = 'JPEG'; scene.render.image_settings.quality = 88; scene.render.filepath = str(SRC / 'lineup.jpg')
bpy.ops.render.render(write_still=True)
print('lineup', len(sum(ROWS, [])), 'treasures,', json.dumps(round(width, 2)), 'm wide')
