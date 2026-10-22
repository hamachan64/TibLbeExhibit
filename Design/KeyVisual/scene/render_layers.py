"""Render the key visual layers from KeyVisual_Scene.blend (run inside Blender).

Usage (Blender's Python console or MCP):
    exec(open(r"<repo>/Design/KeyVisual/scene/render_layers.py").read())
Set SCALE = 50 for a quick preview.

Outputs (in Design/KeyVisual/render/):
  kv_3d_full.png   ruins + venue placeholder + placeholder people (with post: haze, bloom, vignette)
  kv_3d_plate.png  same, without people (plate for compositing photographed people)
  kv_3d_ruins.png  ruins and floor only, venue transparent (to drop in a real venue photo)
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
os.makedirs(OUT, exist_ok=True)

people = bpy.data.collections["KV_People"]
venue = bpy.data.collections["KV_Venue"]
# The bronze cauldron carried by the blue player belongs with the people.
carried = bpy.data.objects["bronze_tripod_cauldron_Root.001"]
carried_objs = [carried] + list(carried.children_recursive)


def set_hidden(objs, hidden):
    for o in objs:
        o.hide_render = hidden


def render(name, show_people, show_venue, transparent):
    people.hide_render = not show_people
    set_hidden(carried_objs, not show_people)
    venue.hide_render = not show_venue
    sc.render.film_transparent = transparent
    sc.render.image_settings.color_mode = "RGBA" if transparent else "RGB"
    # The post chain (mist, bloom, vignette) is only for the opaque frames.
    sc.render.use_compositing = not transparent
    sc.render.filepath = os.path.join(OUT, name)
    bpy.ops.render.render(write_still=True)


render("kv_3d_full.png", True, True, False)
render("kv_3d_plate.png", False, True, False)
render("kv_3d_ruins.png", False, False, True)
# Leave the file showing the full frame.
people.hide_render = False
set_hidden(carried_objs, False)
venue.hide_render = False
sc.render.film_transparent = False
sc.render.use_compositing = True
print("rendered to", os.path.abspath(OUT))
