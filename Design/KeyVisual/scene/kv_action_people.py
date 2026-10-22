"""Placeholder players for the action key visual ("壊すな" moment).

Blue tripped and the gilded sun disc flew out of their hands; green dives in and
catches it with both palm beams; red shouts and points from the altar gate;
yellow holds their head. Run inside Blender with KeyVisual_Scene.blend open:
    exec(open(r"<repo>/Design/KeyVisual/scene/kv_action_people.py", encoding="utf-8").read())
"""
import os

import bpy
from mathutils import Vector

HERE = os.path.dirname(bpy.data.filepath)
exec(open(os.path.join(HERE, "kv_people.py"), encoding="utf-8").read())

clear()
body = bpy.data.objects["gilded_sun_disc_Body.001"]
disc_center = sum((body.matrix_world @ Vector(c) for c in body.bound_box), Vector()) / 8

GREEN = dict(
    pelvis=(0, -0.1, 0.74), chest=(0, 0.3, 0.98), neck=(0, 0.45, 1.06), head=(0, 0.56, 1.12),
    sl=(-0.18, 0.32, 1.02), sr=(0.18, 0.32, 1.02),
    el=(-0.16, 0.6, 1.08), wl=(-0.09, 0.82, 1.1), handl=(-0.07, 0.9, 1.1),
    er=(0.2, 0.6, 1.05), wr=(0.13, 0.82, 1.06), handr=(0.11, 0.9, 1.06),
    hl=(-0.1, -0.12, 0.7), kl=(-0.12, -0.5, 0.4), fl=(-0.12, -0.88, 0.1), fdl=(0, -0.06, -0.08),
    hr=(0.1, -0.08, 0.7), kr=(0.15, 0.36, 0.48), fr=(0.15, 0.38, 0.07), look=(0, 1, 0.35))
BLUE = dict(
    pelvis=(0, 0, 0.82), chest=(0, 0.32, 1.12), neck=(0, 0.46, 1.25), head=(0, 0.55, 1.36),
    sl=(-0.18, 0.33, 1.17), sr=(0.18, 0.33, 1.17),
    el=(-0.24, 0.58, 1.3), wl=(-0.2, 0.78, 1.42), handl=(-0.19, 0.85, 1.46),
    er=(0.25, 0.58, 1.28), wr=(0.24, 0.79, 1.38), handr=(0.24, 0.86, 1.41),
    hl=(-0.09, 0, 0.78), kl=(-0.12, -0.32, 0.7), fl=(-0.12, -0.66, 0.88), fdl=(0, -0.08, 0.12),
    hr=(0.1, 0, 0.78), kr=(0.12, 0.3, 0.45), fr=(0.13, 0.32, 0.07), look=(-0.2, 1, 0.45))
_s = Vector((0.19, 0, 1.43))
_d = Vector((0.3, 1, -0.1)).normalized()
RED = dict(
    pelvis=(0, 0, 0.98), chest=(0, 0.03, 1.3), neck=(0, 0.04, 1.5), head=(0, 0.06, 1.63),
    sl=(-0.19, 0, 1.43), sr=tuple(_s),
    er=tuple(_s + _d * 0.29), wr=tuple(_s + _d * 0.55), handr=tuple(_s + _d * 0.66),
    el=(-0.3, 0.05, 1.25), wl=(-0.38, 0.15, 1.12), handl=(-0.4, 0.2, 1.08),
    hl=(-0.1, 0, 0.93), kl=(-0.16, 0.06, 0.5), fl=(-0.2, 0.05, 0.07),
    hr=(0.1, 0, 0.93), kr=(0.16, 0.12, 0.5), fr=(0.2, 0.2, 0.07), look=(0.3, 1, 0.05))
YELLOW = dict(
    pelvis=(0, 0, 0.97), chest=(0, -0.04, 1.3), neck=(0, -0.04, 1.5), head=(0, -0.03, 1.63),
    sl=(-0.19, -0.03, 1.43), sr=(0.19, -0.03, 1.43),
    el=(-0.33, 0.02, 1.66), wl=(-0.17, 0.05, 1.75), handl=(-0.1, 0.04, 1.74),
    er=(0.33, 0.02, 1.66), wr=(0.17, 0.05, 1.75), handr=(0.1, 0.04, 1.74),
    hl=(-0.1, 0, 0.92), kl=(-0.13, 0.05, 0.5), fl=(-0.15, 0.0, 0.07),
    hr=(0.1, 0, 0.92), kr=(0.14, -0.05, 0.5), fr=(0.15, -0.12, 0.07), look=(-0.3, 1, 0.15))

w_green = build("P4G", (-1.87, -8.45, 0), -62, GREEN, "G")
build("P3B", (0.95, -9.0, 0), -25, BLUE, "B")
build("P2R", (-0.55, -10.15, 0), -25, RED, "R")
build("P1Y", (0.55, -10.3, 0), 15, YELLOW, "Y")
# Beams stop just short of the disc (the beam "holds" it).
for side in "lr":
    start = w_green(GREEN["hand" + side])
    beam(f"P4G_Beam_{side}", start, disc_center + (start - disc_center).normalized() * 0.22, "G")
