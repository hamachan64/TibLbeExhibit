"""Placeholder players for the key visual (run inside Blender with KeyVisual_Scene.blend open).

Simple mannequins (skin-modifier limbs, ellipsoid torso) wearing a headset and a
player-colour armband. They only stand in until photographed people are composited.
Poses are joint positions in a local frame (x right, y forward, z up), placed with
a root position and a yaw in degrees.

Usage: exec(open(r"<repo>/Design/KeyVisual/scene/kv_people.py").read())
"""
import math

import bmesh
import bpy
from mathutils import Matrix, Quaternion, Vector

M = bpy.data.materials
PC = bpy.data.collections["KV_People"]

COLOR = {"Y": (0.95, 0.72, 0.08), "R": (0.9, 0.12, 0.1), "B": (0.1, 0.4, 0.95), "G": (0.1, 0.85, 0.4)}
SHIRT = {"Y": (0.25, 0.22, 0.18), "R": (0.12, 0.14, 0.19), "B": (0.3, 0.21, 0.14), "G": (0.13, 0.2, 0.16)}


def mat(name, base, rough=0.7, emit=None, estr=0.0):
    m = M.get(name) or M.new(name)
    m.use_nodes = True
    b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    b.inputs["Base Color"].default_value = (*base, 1)
    b.inputs["Roughness"].default_value = rough
    if emit:
        b.inputs["Emission Color"].default_value = (*emit, 1)
        b.inputs["Emission Strength"].default_value = estr
    return m


def _link(o):
    for c in o.users_collection:
        c.objects.unlink(o)
    PC.objects.link(o)


def _chain(name, pts, radii, material):
    me = bpy.data.meshes.new(name)
    me.from_pydata(pts, [(i, i + 1) for i in range(len(pts) - 1)], [])
    me.update()
    o = bpy.data.objects.new(name, me)
    PC.objects.link(o)
    o.modifiers.new("Skin", "SKIN").use_smooth_shade = True
    sk = me.skin_vertices[0].data
    for i, r in enumerate(radii):
        sk[i].radius = r if isinstance(r, tuple) else (r, r)
    sk[0].use_root = True
    s = o.modifiers.new("Sub", "SUBSURF")
    s.levels = s.render_levels = 2
    me.materials.append(material)
    return o


def _ellipsoid(name, center, up, side, scale, material):
    bpy.ops.mesh.primitive_uv_sphere_add(radius=1, location=center, segments=32, ring_count=16)
    o = bpy.context.active_object
    o.name = name
    o.data.materials.append(material)
    _link(o)
    z = up.normalized()
    x = (side - z * side.dot(z)).normalized()
    o.rotation_euler = Matrix((x, z.cross(x), z)).transposed().to_euler()
    o.scale = scale
    for p in o.data.polygons:
        p.use_smooth = True
    return o


def build(name, origin, yaw_deg, J, colour):
    """Build one player. Returns a function mapping local joint coords to world."""
    mat("KV_Skin", (0.55, 0.36, 0.27), 0.5)
    mat("KV_Pants", (0.04, 0.045, 0.055), 0.8)
    mat("KV_Hair", (0.02, 0.015, 0.012), 0.6)
    mat("KV_Headset", (0.85, 0.86, 0.88), 0.35)
    mat("KV_Visor", (0.01, 0.01, 0.012), 0.15)
    shirt = mat(f"KV_Shirt_{name}", SHIRT[colour], 0.85)
    skin, pants = M["KV_Skin"], M["KV_Pants"]
    R = Matrix.Rotation(math.radians(yaw_deg), 3, "Z")
    T = Vector(origin)

    def W(p):
        return R @ Vector(p) + T

    def g(k):
        return W(J[k])

    side, up = g("sr") - g("sl"), g("chest") - g("pelvis")
    _ellipsoid(f"{name}_Torso", g("pelvis").lerp(g("chest"), 0.62), up, side, (0.145, 0.095, 0.22), shirt)
    _ellipsoid(f"{name}_Chest", g("chest") + up.normalized() * 0.04, up, side, (0.18, 0.1, 0.1), shirt)
    _ellipsoid(f"{name}_Hips", g("pelvis"), up, side, (0.15, 0.1, 0.11), pants)
    for s in "lr":
        _chain(f"{name}_Arm_{s}", [g("s" + s), g("e" + s), g("w" + s)], [0.058, 0.048, 0.04], shirt)
        w, h = g("w" + s), g("hand" + s)
        dv = h - w
        _chain(f"{name}_Hand_{s}", [w, w + dv * 0.5, h, h + dv.normalized() * 0.04],
               [0.034, (0.048, 0.022), (0.046, 0.02), (0.028, 0.014)], skin)
        f = g("f" + s)
        fd = R @ Vector(J.get("fd" + s, (0, 0.15, -0.01)))
        _chain(f"{name}_Leg_{s}", [g("h" + s), g("k" + s), f, f + fd], [0.085, 0.06, 0.048, (0.045, 0.032)], pants)
    hp, nk = g("head"), g("neck")
    _chain(f"{name}_Neck", [nk - up.normalized() * 0.08, nk, hp - Vector((0, 0, 0.04))], [0.05, 0.048, 0.05], skin)
    hf = (R @ Vector(J.get("look", (0, 1, 0)))).normalized()
    hside = Vector((hf.y, -hf.x, 0))
    _ellipsoid(f"{name}_HeadS", hp, Vector((0, 0, 1)), hside, (0.082, 0.095, 0.11), skin)
    _ellipsoid(f"{name}_Hair", hp + Vector((0, 0, 0.025)) - hf * 0.015, Vector((0, 0, 1)), hside,
               (0.088, 0.1, 0.1), M["KV_Hair"])
    # Headset: rounded box over the eyes, dark visor at the front, strap around the head.
    me = bpy.data.meshes.new(f"{name}_HMD")
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1)
    for v in bm.verts:
        v.co = Vector((v.co.x * 0.19, v.co.y * 0.095, v.co.z * 0.105))
    bmesh.ops.bevel(bm, geom=bm.edges[:], offset=0.03, segments=4, affect="EDGES")
    bm.to_mesh(me)
    bm.free()
    me.materials.append(M["KV_Headset"])
    me.materials.append(M["KV_Visor"])
    for p in me.polygons:
        p.use_smooth = True
        if p.center.y > 0.04:
            p.material_index = 1
    o = bpy.data.objects.new(f"{name}_HMD", me)
    PC.objects.link(o)
    q = hf.to_track_quat("Y", "Z")
    o.location = hp + hf * 0.1 + Vector((0, 0, 0.015))
    o.rotation_euler = q.to_euler()
    bpy.ops.mesh.primitive_torus_add(major_radius=0.1, minor_radius=0.016, location=hp + Vector((0, 0, 0.03)))
    t = bpy.context.active_object
    t.name = f"{name}_Strap"
    t.scale = (0.95, 1.12, 1.0)
    t.rotation_euler = (q @ Quaternion((1, 0, 0), math.radians(-12))).to_euler()
    t.data.materials.append(M["KV_Headset"])
    _link(t)
    band = mat(f"KV_Band_{name}", COLOR[colour], 0.4, emit=COLOR[colour], estr=0.8)
    for s in "lr":
        a, b = g("s" + s), g("e" + s)
        bpy.ops.mesh.primitive_cylinder_add(radius=0.062, depth=0.05, location=a.lerp(b, 0.5), vertices=24)
        c = bpy.context.active_object
        c.name = f"{name}_Band_{s}"
        c.rotation_euler = (b - a).to_track_quat("Z", "Y").to_euler()
        c.data.materials.append(band)
        _link(c)
    return W


def beam(name, start, end, colour, radius=0.007, strength=8.0):
    """Short palm beam (thin emissive cylinder) in the player's colour."""
    m = M.get(f"KV_Beam_{colour}") or M.new(f"KV_Beam_{colour}")
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    em = nt.nodes.new("ShaderNodeEmission")
    em.inputs[0].default_value = (*COLOR[colour], 1)
    em.inputs[1].default_value = strength
    nt.links.new(em.outputs[0], out.inputs[0])
    a, b = Vector(start), Vector(end)
    bpy.ops.mesh.primitive_cylinder_add(radius=radius, depth=(b - a).length, location=(a + b) / 2, vertices=12)
    c = bpy.context.active_object
    c.name = name
    c.rotation_euler = (b - a).to_track_quat("Z", "Y").to_euler()
    c.data.materials.append(m)
    c.visible_shadow = False
    _link(c)
    return c


def clear():
    for o in list(PC.objects):
        bpy.data.objects.remove(o)
