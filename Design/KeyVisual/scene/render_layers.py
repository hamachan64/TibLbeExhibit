"""Render the key visual layers from KeyVisual_Scene.blend (run inside Blender).

Usage (Blender's Python console or MCP):
    exec(open(r"<repo>/Design/KeyVisual/scene/render_layers.py").read())
or  blender -b scene/KeyVisual_Scene.blend --python scene/render_layers.py
Set SCALE = 50 for a quick preview.

Outputs (in Design/KeyVisual/render/):
  kv_3d_full.png   cavern ruins + placeholder people (with post: haze, bloom, vignette)
  kv_3d_plate.png  same, without the people and the cauldron red carries (plate for compositing
                   photographed people; the flying disc, its debris and the gold mask stay)
"""
import os

import bpy

SCALE = 100
SAMPLES = 256
OUT = os.path.join(os.path.dirname(bpy.data.filepath), "..", "render")

sc = bpy.context.scene
# Use the GPU when one is available (also when run with `blender -b`).
cprefs = bpy.context.preferences.addons["cycles"].preferences
for device_type in ("OPTIX", "CUDA", "HIP", "ONEAPI", "METAL"):
    try:
        cprefs.compute_device_type = device_type
    except TypeError:
        continue
    cprefs.get_devices()
    gpus = [d for d in cprefs.devices if d.type == device_type]
    if gpus:
        for d in cprefs.devices:
            d.use = d.type == device_type
        sc.cycles.device = "GPU"
        break
sc.render.resolution_x, sc.render.resolution_y = 3840, 2160
sc.render.resolution_percentage = SCALE
sc.cycles.samples = SAMPLES
sc.render.image_settings.file_format = "PNG"
sc.render.image_settings.color_mode = "RGB"
sc.render.film_transparent = False
sc.render.use_compositing = True
os.makedirs(OUT, exist_ok=True)

people = bpy.data.collections["KV_People"]
carried = bpy.data.objects["bronze_tripod_cauldron_Root.001"]
carried_objs = [carried] + list(carried.children_recursive)


def render(name, show_people):
    people.hide_render = not show_people
    for o in carried_objs:
        o.hide_render = not show_people
    sc.render.filepath = os.path.join(OUT, name)
    bpy.ops.render.render(write_still=True)


render("kv_3d_full.png", True)
render("kv_3d_plate.png", False)
# Leave the file showing the full frame.
people.hide_render = False
for o in carried_objs:
    o.hide_render = False
print("rendered to", os.path.abspath(OUT))
