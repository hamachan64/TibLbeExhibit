"""Placeholder players for the action key visual (見つけて、運んで、壊すな).

- 壊すな: blue (back right) trips over a stone and the gilded sun disc flies out of
  their hands; green dives in from the right to catch it bare-handed (no beams).
- 運んで: red carries the bronze cauldron in the hall gate and looks round in alarm.
- 見つけて: yellow (left) crouches at a wall niche and reaches for the glowing gold mask.
Run inside Blender with KeyVisual_Scene.blend open:
    exec(open(r"<repo>/Design/KeyVisual/scene/kv_action_people.py", encoding="utf-8").read())
"""
import math
import os

import bpy
from mathutils import Euler, Vector

HERE = os.path.dirname(bpy.data.filepath)
exec(open(os.path.join(HERE, "kv_people.py"), encoding="utf-8").read())
O = bpy.data.objects

clear()


def show(root_name, location, rotation_deg):
    root = O[root_name]
    root.location = location
    root.rotation_euler = Euler([math.radians(a) for a in rotation_deg])
    for o in [root] + list(root.children_recursive):
        o.hide_render = False
        o.hide_set(False)


DISC_SCREEN = (0.5, 0.68)   # where the disc centre sits in the frame (0-1, from bottom left)
DISC_DEPTH = 1.9             # metres from the camera
GREEN_DIR_DEG = -80          # green's facing (yaw); they dive in from the right of the frame
DEBRIS_ORIGIN = Vector((-0.004, -7.688, 1.351))  # disc centre the debris burst was modelled around


def screen_point(u, v, depth):
    cam = bpy.context.scene.camera
    w = cam.data.sensor_width
    h = w * 9 / 16
    ray = Vector(((u - 0.5) * w, (v - 0.5) * h, -cam.data.lens))
    ray = cam.matrix_world.to_3x3() @ ray
    return cam.matrix_world.translation + ray * (depth / ray.length)


def ground_point(u, v):
    """Point on the floor seen at screen position (u, v)."""
    cam = bpy.context.scene.camera
    w = cam.data.sensor_width
    h = w * 9 / 16
    ray = cam.matrix_world.to_3x3() @ Vector(((u - 0.5) * w, (v - 0.5) * h, -cam.data.lens))
    origin = cam.matrix_world.translation
    return origin + ray * (-origin.z / ray.z)


def yaw_towards(src, dst):
    d = Vector(dst) - Vector(src)
    return math.degrees(math.atan2(-d.x, d.y))


# --- 壊すな: the disc in mid-air, green diving for it -------------------------------
GREEN = dict(
    pelvis=(0, -0.1, 0.74), chest=(0, 0.3, 0.98), neck=(0, 0.45, 1.06), head=(0, 0.56, 1.13),
    sl=(-0.18, 0.32, 1.02), sr=(0.18, 0.32, 1.02),
    el=(-0.17, 0.6, 1.1), wl=(-0.11, 0.83, 1.15), handl=(-0.1, 0.92, 1.17),
    er=(0.2, 0.6, 1.08), wr=(0.15, 0.83, 1.12), handr=(0.14, 0.92, 1.14),
    hl=(-0.1, -0.12, 0.7), kl=(-0.12, -0.5, 0.4), fl=(-0.12, -0.88, 0.1), fdl=(0, -0.06, -0.08),
    hr=(0.1, -0.08, 0.7), kr=(0.15, 0.36, 0.48), fr=(0.15, 0.38, 0.07), look=(0, 1, 0.45))
disc_center = screen_point(*DISC_SCREEN, DISC_DEPTH)
facing = Vector((-math.sin(math.radians(GREEN_DIR_DEG)), math.cos(math.radians(GREEN_DIR_DEG)), 0))
hands_local = (Vector(GREEN["handl"]) + Vector(GREEN["handr"])) / 2
green_root = disc_center - facing * (hands_local.y + 0.4)
green_root.z = 0
build("P4G", tuple(green_root), GREEN_DIR_DEG, GREEN, "G")
# The disc model's origin is at its base; its centre is about 0.25 m above it.
show("gilded_sun_disc_Root.001", disc_center - Vector((0, 0, 0.25)), (25, -30, 160))
for chip in bpy.data.collections["KV_Debris"].objects:
    if chip.name.startswith("KV_Chip"):
        if "kv_offset" not in chip:
            chip["kv_offset"] = list(chip.location - DEBRIS_ORIGIN)
        chip.location = disc_center + Vector(chip["kv_offset"])

# --- 壊すな: blue tripped over a stone; arms flung after the disc ------------------
BLUE = dict(
    pelvis=(0, 0, 0.68), chest=(0, 0.42, 0.88), neck=(0, 0.6, 0.95), head=(0, 0.72, 1.0),
    sl=(-0.18, 0.44, 0.93), sr=(0.18, 0.44, 0.93),
    el=(-0.2, 0.72, 1.0), wl=(-0.18, 0.95, 1.1), handl=(-0.17, 1.03, 1.14),
    er=(0.22, 0.7, 0.95), wr=(0.24, 0.92, 1.05), handr=(0.24, 1.0, 1.08),
    hl=(-0.09, 0, 0.64), kl=(-0.12, -0.3, 0.62), fl=(-0.12, -0.62, 0.95), fdl=(0, -0.1, 0.08),
    hr=(0.1, 0, 0.64), kr=(0.12, 0.34, 0.38), fr=(0.13, 0.4, 0.07), look=(0, 1, 0.5))
blue_root = screen_point(0.37, 0.3, 3.0)
blue_root.z = 0
blue_root = tuple(blue_root)
blue_yaw = yaw_towards(blue_root, disc_center)
w_blue = build("P3B", blue_root, blue_yaw, BLUE, "B")
O["KV_TripStone"].location = w_blue((-0.12, -0.5, 0.06))

# --- 運んで: red carries the cauldron, looking round at the disc --------------------
RED = dict(
    pelvis=(0, 0, 0.98), chest=(0, -0.03, 1.3), neck=(0, -0.02, 1.5), head=(0, 0.0, 1.63),
    sl=(-0.19, -0.02, 1.43), sr=(0.19, -0.02, 1.43),
    el=(-0.24, 0.12, 1.1), wl=(-0.2, 0.32, 1.04), handl=(-0.18, 0.36, 1.02),
    er=(0.24, 0.12, 1.1), wr=(0.2, 0.32, 1.04), handr=(0.18, 0.36, 1.02),
    hl=(-0.1, 0, 0.93), kl=(-0.1, 0.14, 0.5), fl=(-0.11, 0.18, 0.07),
    hr=(0.1, 0, 0.93), kr=(0.1, -0.08, 0.48), fr=(0.11, -0.2, 0.1), look=(-0.75, 0.6, 0.1))
w_red = build("P2R", (-0.45, -10.1, 0), 15, RED, "R")
show("bronze_tripod_cauldron_Root.001", w_red((0, 0.38, 0.69)), (0, 0, 25))

# --- 見つけて: yellow crouches at the niche and reaches for the glowing gold mask -----
NICHE = Vector((1.45, -9.48, 1.0))   # TreasureSlot_24
show("gold_mask_Root.001", NICHE, (0, 0, 0))
O["KV_MaskFind"].location = NICHE + Vector((0, 0.2, 0.12))
YELLOW = dict(
    pelvis=(0, -0.08, 0.6), chest=(0, 0.1, 0.9), neck=(0, 0.2, 1.05), head=(0, 0.28, 1.15),
    sl=(-0.18, 0.12, 1.0), sr=(0.18, 0.12, 1.0),
    el=(-0.2, 0.34, 1.02), wl=(-0.12, 0.5, 1.06), handl=(-0.09, 0.57, 1.07),
    er=(0.26, 0.2, 0.85), wr=(0.32, 0.36, 0.9), handr=(0.33, 0.43, 0.92),
    hl=(-0.1, -0.08, 0.56), kl=(-0.15, 0.3, 0.48), fl=(-0.15, 0.22, 0.07),
    hr=(0.1, -0.08, 0.56), kr=(0.15, 0.2, 0.4), fr=(0.15, -0.12, 0.07),
    look=(-0.05, 1, -0.15))
yellow_root = (2.05, -9.0, 0)
build("P1Y", yellow_root, yaw_towards(yellow_root, NICHE), YELLOW, "Y")
