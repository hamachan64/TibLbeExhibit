"""Review images of the enemy models (JPEG, overwritten): Statue/review/*.jpg and Phantom/review/*.jpg.

  blender -b --factory-startup --python render_review.py [-- statue|phantom ...]
Studio shots are rendered in this process; the stage shots re-launch Blender on ../Stage/Output/Stage_Review.blend
(read only, never saved) with this same script. Eevee. Nothing else is written.

Statue: front, three-quarter, back, head close-up, head turned 0 / 90 / 180 deg, scale (2.4 m wall, 1.7 m person),
        eyes lit (colour change only, as #68 will do), stage: hall looking up, west corridor over the walls, entrance.
Phantom: front, three-quarter, side, back, bright grey background (passthrough venue), alert (red eyes and lamp),
        stage: west corridor at eye height 1.65 m (far and close).
Stage coordinates: L(x, d, h) = Blender (x, -d, h) (as in Stage/render_search_views.py). The statue stands outside
the far wall (outer face at d=11.0) facing the entrance; the phantom walks the west corridor towards the camera.
"""
import bpy, math, sys, subprocess
from pathlib import Path
from mathutils import Vector

HERE = Path(__file__).resolve().parent
STAGE = HERE.parent / 'Stage' / 'Output' / 'Stage_Review.blend'
ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
MODELS = [a for a in ARGS if a in ('statue', 'phantom')] or ['statue', 'phantom']
IN_STAGE = bpy.data.filepath.replace('\\', '/').endswith('Stage_Review.blend')
STATUE_AT = (0.0, 11.85)     # (x, d) in stage coordinates
PHANTOM_AT = (-1.72, 6.1)


def L(x, d, h): return Vector((x, -d, h))


def load(blend, root_name):
    with bpy.data.libraries.load(str(blend), link=False) as (src, dst):
        dst.objects = list(src.objects)
    coll = bpy.data.collections.new(root_name + '_review'); bpy.context.scene.collection.children.link(coll)
    for o in dst.objects: coll.objects.link(o)
    return next(o for o in dst.objects if o.name.startswith(root_name)), coll


def cam_setup():
    sc = bpy.context.scene
    cam = sc.camera
    if cam is None:
        cam = bpy.data.objects.new('ReviewCam', bpy.data.cameras.new('ReviewCam')); sc.collection.objects.link(cam); sc.camera = cam
    return cam


def aim(cam, loc, target, lens):
    cam.location = Vector(loc); cam.rotation_euler = (Vector(target) - cam.location).to_track_quat('-Z', 'Y').to_euler()
    cam.data.type = 'PERSP'; cam.data.lens = lens; cam.data.clip_start = .05; cam.data.sensor_fit = 'AUTO'


def shot(path, size):
    sc = bpy.context.scene
    sc.render.resolution_x, sc.render.resolution_y = size; sc.render.resolution_percentage = 100
    sc.render.image_settings.file_format = 'JPEG'; sc.render.image_settings.quality = 85
    sc.render.filepath = str(path); path.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.render.render(write_still=True); print('wrote', path)


def eevee(samples=48):
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_EEVEE'
    try: sc.eevee.taa_render_samples = samples
    except AttributeError: pass
    for attr, val in (('use_raytracing', True), ('use_shadows', True), ('use_gtao', True)):
        try: setattr(sc.eevee, attr, val)
        except AttributeError: pass


def studio(bright=False):
    """Dark grey studio like the treasure line-up (or a bright passthrough-like grey)."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene; eevee()
    w = bpy.data.worlds.new('W'); sc.world = w; w.use_nodes = True
    bg = next(n for n in w.node_tree.nodes if n.type == 'BACKGROUND')
    bg.inputs['Color'].default_value = (.62, .62, .63, 1) if bright else (.030, .036, .045, 1)
    bg.inputs['Strength'].default_value = 1.0
    fm = bpy.data.materials.new('Floor'); fm.use_nodes = True
    fb = next(n for n in fm.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    fb.inputs['Base Color'].default_value = (.55, .55, .55, 1) if bright else (.05, .055, .065, 1); fb.inputs['Roughness'].default_value = .85
    bpy.ops.mesh.primitive_plane_add(size=60); bpy.context.object.data.materials.append(fm)
    for name, loc, energy, size, col in (('Key', (-4.5, -6.0, 7.0), 2600, 4.0, (1, .96, .9)), ('Fill', (6.0, -4.0, 3.0), 700, 6.0, (.85, .9, 1)),
                                         ('Rim', (2.0, 7.0, 6.0), 1800, 3.0, (.9, .95, 1))):
        ld = bpy.data.lights.new(name, 'AREA'); ld.energy = energy * (.45 if bright else 1); ld.size = size; ld.color = col
        lo = bpy.data.objects.new(name, ld); sc.collection.objects.link(lo); lo.location = loc
        lo.rotation_euler = (Vector((0, 0, 1.6)) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
    sc.view_settings.view_transform = 'AgX'
    try: sc.view_settings.look = 'AgX - Medium High Contrast'
    except TypeError: pass
    return cam_setup()


def set_eye_color(mat_name, rgb, strength):
    m = bpy.data.materials.get(mat_name)
    if m is None: return
    for n in m.node_tree.nodes:
        if n.type == 'EMISSION':
            n.inputs['Color'].default_value = (*rgb, 1); n.inputs['Strength'].default_value = strength
        if n.type == 'BSDF_PRINCIPLED':
            n.inputs['Emission Color'].default_value = (*rgb, 1); n.inputs['Emission Strength'].default_value = strength
            n.inputs['Base Color'].default_value = (*rgb, 1)


def mannequin(x, y):
    """1.7 m neutral figure for scale."""
    m = bpy.data.materials.new('Person'); m.use_nodes = True
    b = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'); b.inputs['Base Color'].default_value = (.75, .75, .76, 1)
    parts = [((x, y, .45), (.13, .13, .45), 'CYL'), ((x, y, 1.18), (.19, .12, .32), 'CYL'), ((x, y, 1.58), (.105, .105, .12), 'SPH')]
    for loc, s, kind in parts:
        if kind == 'CYL': bpy.ops.mesh.primitive_cylinder_add(vertices=20, radius=1, depth=2, location=loc)
        else: bpy.ops.mesh.primitive_uv_sphere_add(radius=1, location=loc)
        o = bpy.context.object; o.scale = s; o.data.materials.append(m)
        bpy.ops.object.shade_smooth()


def wall(x, y, w=3.0, h=2.4, t=.3):
    m = bpy.data.materials.new('Wall'); m.use_nodes = True
    b = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'); b.inputs['Base Color'].default_value = (.29, .275, .245, 1)
    bpy.ops.mesh.primitive_cube_add(location=(x, y, h / 2)); o = bpy.context.object; o.scale = (w / 2, t / 2, h / 2); o.data.materials.append(m)


# ----------------------------------------------------------------------------------------------
def statue_studio():
    out = HERE / 'Statue' / 'review'
    cam = studio()
    root, _ = load(HERE / 'Statue' / 'KingStatue.blend', 'KingStatue_Root')
    hp = bpy.data.objects['KingStatue_HeadPivot']
    P = (1200, 1500)
    aim(cam, (0, -10.5, 2.0), (0, 0, 1.82), 70); shot(out / 'front.jpg', P)
    aim(cam, (-6.6, -7.8, 2.6), (0, 0, 1.8), 70); shot(out / 'three_quarter.jpg', P)
    aim(cam, (0, 10.5, 2.2), (0, 0, 1.82), 70); shot(out / 'back.jpg', P)
    aim(cam, (1.6, -2.3, 2.75), (0, 0, 3.0), 55); shot(out / 'head_close.jpg', (1200, 1200))
    aim(cam, (-1.7, 1.9, 3.5), (0, 0, 3.0), 55); shot(out / 'head_back.jpg', (1200, 1200))
    for deg in (0, 90, 180):
        hp.rotation_euler = (0, 0, math.radians(deg))
        aim(cam, (2.5, -3.4, 3.7), (0, 0, 2.8), 40); shot(out / ('head_turn_%03d.jpg' % deg), (1200, 1200))
    hp.rotation_euler = (0, 0, 0)
    set_eye_color('TH_StatueEye', (1.0, .55, .18), 18.0)
    aim(cam, (.9, -3.4, 2.7), (0, 0, 2.95), 60); shot(out / 'eyes_lit.jpg', (1200, 1000))
    set_eye_color('TH_StatueEye', (.045, .042, .038), 0.0)
    wall(-2.3, .6); mannequin(1.35, -.9)
    aim(cam, (.5, -12.5, 1.9), (-.4, 0, 1.75), 50); shot(out / 'scale_wall_person.jpg', (1600, 1100))


def phantom_studio():
    out = HERE / 'Phantom' / 'review'
    cam = studio()
    load(HERE / 'Phantom' / 'GraveKeeper.blend', 'GraveKeeper_Root')
    P = (1100, 1300)
    aim(cam, (0, -5.6, 1.25), (0, 0, 1.05), 50); shot(out / 'front.jpg', P)
    aim(cam, (-3.6, -4.2, 1.6), (0, -.25, 1.05), 50); shot(out / 'three_quarter.jpg', P)
    aim(cam, (5.6, -.4, 1.2), (0, -.3, 1.05), 50); shot(out / 'side.jpg', P)
    aim(cam, (1.0, 5.4, 1.5), (0, 0, 1.05), 50); shot(out / 'back.jpg', P)
    aim(cam, (-.55, -1.9, 1.6), (0, -.2, 1.5), 50); shot(out / 'face_close.jpg', (1200, 1000))
    set_eye_color('TH_PhantomEye', (1.0, .15, .10), 4.0)
    aim(cam, (-2.6, -4.2, 1.55), (0, -.3, 1.1), 50); shot(out / 'alert_red.jpg', P)
    cam = studio(bright=True)
    load(HERE / 'Phantom' / 'GraveKeeper.blend', 'GraveKeeper_Root')
    aim(cam, (-2.2, -4.6, 1.6), (0, -.3, 1.05), 50); shot(out / 'bright_background.jpg', P)


def statue_stage():
    out = HERE / 'Statue' / 'review'
    eevee(32)
    cam = cam_setup()
    root, _ = load(HERE / 'Statue' / 'KingStatue.blend', 'KingStatue_Root')
    root.location = L(STATUE_AT[0], STATUE_AT[1], 0); root.rotation_euler = (0, 0, math.pi)
    # the stage review file has no light outside the venue: a soft cool light over the statue (as the venue lights would)
    ld = bpy.data.lights.new('StatueLight', 'AREA'); ld.energy = 900; ld.size = 3.0; ld.color = (.9, .93, 1)
    lo = bpy.data.objects.new('StatueLight', ld); bpy.context.scene.collection.objects.link(lo)
    lo.location = L(1.5, 9.8, 6.0); lo.rotation_euler = (L(0, 11.85, 2.0) - lo.location).to_track_quat('-Z', 'Y').to_euler()
    aim(cam, L(.2, 9.75, 1.65), L(0, 11.85, 2.75), 16); shot(out / 'stage_hall_lookup.jpg', (1280, 800))
    aim(cam, L(-1.62, 4.6, 1.65), L(-.3, 11.85, 2.7), 22); shot(out / 'stage_corridor_over_wall.jpg', (1280, 800))
    aim(cam, L(0, 2.4, 1.65), L(0, 11.85, 2.5), 24); shot(out / 'stage_entrance.jpg', (1280, 800))


def phantom_stage():
    out = HERE / 'Phantom' / 'review'
    eevee(32)
    cam = cam_setup()
    root, _ = load(HERE / 'Phantom' / 'GraveKeeper.blend', 'GraveKeeper_Root')
    root.location = L(PHANTOM_AT[0], PHANTOM_AT[1], 0); root.rotation_euler = (0, 0, math.radians(184))
    aim(cam, L(-1.62, 3.35, 1.65), L(-1.72, 6.1, 1.15), 20); shot(out / 'stage_corridor_w.jpg', (1280, 800))
    aim(cam, L(-1.6, 4.3, 1.65), L(-1.72, 6.1, 1.3), 26); shot(out / 'stage_corridor_w_close.jpg', (1280, 800))


if IN_STAGE:
    if 'statue' in MODELS: statue_stage()
    if 'phantom' in MODELS: phantom_stage()
else:
    if 'statue' in MODELS: statue_studio()
    if 'phantom' in MODELS: phantom_studio()
    if "nostage" not in ARGS: subprocess.run([bpy.app.binary_path, '-b', str(STAGE), '--python', str(Path(__file__).resolve()), '--', *MODELS], check=True)
