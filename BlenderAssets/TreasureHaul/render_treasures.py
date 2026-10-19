"""Lineup render of every treasure in Treasures/ at true relative size.

Headless, in a throwaway session (nothing is saved except the PNG):
  blender -b --factory-startup --python render_treasures.py
Writes Treasures/lineup.png. Stage previews: Stage/render_search_views.py.
"""
import bpy, json
from pathlib import Path
from mathutils import Vector
HERE = Path(__file__).resolve().parent
SRC = HERE / 'Treasures'
ROWS = [['mask', 'cup', 'statuette', 'scarab_amulet', 'ceremonial_lamp', 'hand_mirror', 'votive_bell', 'perfume_jar', 'stamp_seal', 'coin_casket'],
        ['amphora', 'inscribed_stele', 'guardian_statue', 'relic_chest', 'sun_mask_secret']]

scene = bpy.context.scene
for o in list(scene.objects): bpy.data.objects.remove(o, do_unlink=True)
scene.world = bpy.data.worlds.new('TH_Lineup_World'); scene.world.use_nodes = True
bg = next(n for n in scene.world.node_tree.nodes if n.type == 'BACKGROUND')
bg.inputs['Color'].default_value = (.12, .15, .17, 1); bg.inputs['Strength'].default_value = .45
try: scene.render.engine = 'CYCLES'; scene.cycles.samples = 32
except TypeError: pass

width = 0
for r, names in enumerate(ROWS):
    x = 0
    for name in names:
        with bpy.data.libraries.load(str(SRC / (name + '.blend')), link=False) as (src, dst): dst.objects = src.objects
        objects = [o for o in dst.objects if o]
        for o in objects: scene.collection.objects.link(o)
        root = next(o for o in objects if o.type == 'EMPTY' and o.parent is None)
        root.location = (0, 0, 0); bpy.context.view_layer.update()
        pts = [o.matrix_world @ Vector(c) for o in objects if o.type == 'MESH' for c in o.bound_box]
        lo, hi = min(p.x for p in pts), max(p.x for p in pts)
        root.location = (x - lo, r * .9, 0); x += hi - lo + .08
    width = max(width, x)
for name, pos, power, size in [('Key', (1.5, -3, 3), 400, 4), ('Rim', (2.5, 3, 2), 200, 3)]:
    data = bpy.data.lights.new(name, 'AREA'); data.energy = power; data.size = size
    o = bpy.data.objects.new(name, data); scene.collection.objects.link(o); o.location = pos
    o.rotation_euler = (Vector((width / 2, .45, .2)) - o.location).to_track_quat('-Z', 'Y').to_euler()
data = bpy.data.cameras.new('LineupCamera'); cam = bpy.data.objects.new('LineupCamera', data); scene.collection.objects.link(cam)
cam.location = (width / 2, -4, 1.4); cam.rotation_euler = (Vector((width / 2, .45, .25)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
data.type = 'ORTHO'; data.ortho_scale = width + .3; scene.camera = cam
scene.render.resolution_x = 1800; scene.render.resolution_y = 760; scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = 'PNG'; scene.render.filepath = str(SRC / 'lineup.png')
bpy.ops.render.render(write_still=True)
print('lineup', len(sum(ROWS, [])), 'treasures,', json.dumps(round(width, 2)), 'm wide')
