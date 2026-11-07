"""Review images of the cargo board (JPEG, overwritten): review/*.jpg

  blender -b --factory-startup --python render_board.py [-- nostage]
Studio shots are rendered in this process; the stage shots re-launch Blender on ../Stage/Output/Stage_Review.blend
(read only, never saved) with this same script. Eevee. Nothing else is written.

Studio: front, three_quarter, back, close_digits (a provisional amber 7-segment "1,250G" on the screen),
        detail_chain (eye bolt, chain, outrigger), detail_foot (post foot, base plate, cable), bright_background.
Stage:  stage_practice_eye (practice line, Unity (0, 1.6, 2.5) = eye height 1.6 m, with the delivery bay machine),
        stage_practice_eye_digits (same, with the 7-segment "1,250G"), stage_practice_side (the right-most practice spot),
        stage_side_clearance (from the side: the posts stand behind the machine), stage_corridor_w_digits (coming back down the west corridor, about 5.5 m away).
Stage coordinates: Stage_Review.blend = (Unity x, -Unity z, Unity y) of VenueRoot local; the model root goes to
Unity (0, 0, 0.09) = Blender (0, -0.09, 0), not rotated (front -Y faces the practice line).
The 7-segment glyphs here are only a stand-in for the Unity mesh (layout inside stats.json digits_area).
"""
import bpy, math, sys, json, subprocess
from pathlib import Path
from mathutils import Vector

HERE = Path(__file__).resolve().parent
OUT = HERE / 'review'
STAGE = HERE.parent / 'Stage' / 'Output' / 'Stage_Review.blend'
ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
IN_STAGE = bpy.data.filepath.replace('\\', '/').endswith('Stage_Review.blend')
STATS = json.loads((HERE / 'stats.json').read_text(encoding='utf-8'))
ROOT_AT = Vector((0, -STATS['root_venue_unity'][2], 0))


def load():
    with bpy.data.libraries.load(str(HERE / 'CargoBoard.blend'), link=False) as (src, dst):
        dst.objects = list(src.objects)
    coll = bpy.data.collections.new('CargoBoard_review'); bpy.context.scene.collection.children.link(coll)
    for o in dst.objects: coll.objects.link(o)
    root = next(o for o in dst.objects if o.name.startswith('CargoBoard_Root'))
    return root


def cam_setup():
    sc = bpy.context.scene
    cam = sc.camera
    if cam is None:
        cam = bpy.data.objects.new('ReviewCam', bpy.data.cameras.new('ReviewCam')); sc.collection.objects.link(cam); sc.camera = cam
    return cam


def aim(cam, loc, target, lens):
    cam.location = Vector(loc); cam.rotation_euler = (Vector(target) - cam.location).to_track_quat('-Z', 'Y').to_euler()
    cam.data.type = 'PERSP'; cam.data.lens = lens; cam.data.clip_start = .03; cam.data.sensor_fit = 'AUTO'


def shot(name, size):
    sc = bpy.context.scene
    sc.render.resolution_x, sc.render.resolution_y = size; sc.render.resolution_percentage = 100
    sc.render.image_settings.file_format = 'JPEG'; sc.render.image_settings.quality = 88
    path = OUT / name; sc.render.filepath = str(path); OUT.mkdir(parents=True, exist_ok=True)
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
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene; eevee()
    w = bpy.data.worlds.new('W'); sc.world = w; w.use_nodes = True
    bg = next(n for n in w.node_tree.nodes if n.type == 'BACKGROUND')
    bg.inputs['Color'].default_value = (.62, .62, .63, 1) if bright else (.030, .036, .045, 1)
    fm = bpy.data.materials.new('Floor'); fm.use_nodes = True
    fb = next(n for n in fm.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    fb.inputs['Base Color'].default_value = (.50, .49, .47, 1) if bright else (.06, .06, .065, 1); fb.inputs['Roughness'].default_value = .85
    bpy.ops.mesh.primitive_plane_add(size=40); bpy.context.object.data.materials.append(fm)
    for name, loc, energy, size, col in (('Key', (-3.5, -5.0, 4.5), 1500, 3.0, (1, .95, .88)), ('Fill', (5.0, -3.0, 2.0), 380, 5.0, (.85, .9, 1)),
                                         ('Rim', (1.5, 5.0, 4.0), 900, 3.0, (.9, .95, 1))):
        ld = bpy.data.lights.new(name, 'AREA'); ld.energy = energy * (.5 if bright else 1); ld.size = size; ld.color = col
        lo = bpy.data.objects.new(name, ld); sc.collection.objects.link(lo); lo.location = loc
        lo.rotation_euler = (Vector((0, 0, 1.3)) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
    sc.view_settings.view_transform = 'AgX'
    try: sc.view_settings.look = 'AgX - Medium High Contrast'
    except TypeError: pass
    return cam_setup()


# ---------------------------------------------------------------------------------------------- provisional 7-segment
SEG = {'0': 'abcdef', '1': 'bc', '2': 'abdeg', '3': 'abcdg', '4': 'bcfg', '5': 'acdfg', '6': 'acdefg', '7': 'abc',
       '8': 'abcdefg', '9': 'abcdfg', 'G': 'acdef', ' ': ''}


def seven_seg(root_loc, text='1,250G', strength=3.2):
    """Amber digits (lit) + faint unlit segments, laid out right aligned in the digits area (5 digits, comma, G)."""
    da = STATS['digits_area']; cu = da['centre_unity']
    c = Vector((-cu[0], -cu[2], cu[1])) + root_loc          # Unity -> Blender (model) -> world
    W, H = da['size_unity_wh']
    gh, gw, t, gap, cw = .27, .155, .031, .05, .045
    slant = math.tan(math.radians(7))
    total = 6 * gw + 5 * gap + cw
    x0 = c.x - total / 2 + (H / 2) * slant * .0
    lit = bpy.data.materials.new('SegLit'); lit.use_nodes = True
    b = next(n for n in lit.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    b.inputs['Base Color'].default_value = (.03, .012, .002, 1); b.inputs['Emission Color'].default_value = (1, .33, .025, 1)
    b.inputs['Emission Strength'].default_value = strength
    dim = bpy.data.materials.new('SegDim'); dim.use_nodes = True
    b = next(n for n in dim.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    b.inputs['Base Color'].default_value = (.010, .0055, .0025, 1); b.inputs['Emission Color'].default_value = (1, .33, .025, 1)
    b.inputs['Emission Strength'].default_value = .012; b.inputs['Roughness'].default_value = .6
    digits = text.replace(',', '').rstrip('G').rjust(5, ' ')
    glyphs = list(digits) + ['G']
    verts = {True: [], False: []}; faces = {True: [], False: []}

    def bar(p0, p1, lit_on):
        p0, p1 = Vector(p0), Vector(p1); d = (p1 - p0).normalized(); n = Vector((-d.y, d.x))
        pts = [p0, p0 + d * t / 2 + n * t / 2, p1 - d * t / 2 + n * t / 2, p1, p1 - d * t / 2 - n * t / 2, p0 + d * t / 2 - n * t / 2]
        base = len(verts[lit_on])
        for q in pts:
            verts[lit_on].append((x0 + q.x + (q.y - gh / 2) * slant, c.y, c.z - gh / 2 + q.y))
        faces[lit_on].append(tuple(range(base, base + 6)))

    xg = 0.0
    for gi, ch in enumerate(glyphs):
        on = SEG.get(ch, '')
        if ch == ' ': on = ''
        e = .006; h2 = gh / 2
        segs = {'a': ((t / 2 + e, gh - t / 2), (gw - t / 2 - e, gh - t / 2)), 'g': ((t / 2 + e, h2), (gw - t / 2 - e, h2)),
                'd': ((t / 2 + e, t / 2), (gw - t / 2 - e, t / 2)), 'f': ((t / 2, h2 + e), (t / 2, gh - t / 2 - e)),
                'b': ((gw - t / 2, h2 + e), (gw - t / 2, gh - t / 2 - e)), 'e': ((t / 2, t / 2 + e), (t / 2, h2 - e)),
                'c': ((gw - t / 2, t / 2 + e), (gw - t / 2, h2 - e))}
        for k, (p0, p1) in segs.items():
            bar((xg + p0[0], p0[1]), (xg + p1[0], p1[1]), k in on)
        xg += gw + gap
        if gi == 1:       # thousands comma after the 2nd digit
            lit_c = digits[1] != ' '
            base = len(verts[lit_c]); cx = xg - gap / 2 + cw / 2
            for q in ((cx - .012, -.03), (cx + .006, -.03), (cx + .016, .03), (cx - .002, .03)):
                verts[lit_c].append((x0 + q[0] + (q[1] - gh / 2) * slant, c.y, c.z - gh / 2 + q[1]))
            faces[lit_c].append(tuple(range(base, base + 4)))
            xg += cw
    for on, m in ((True, lit), (False, dim)):
        me = bpy.data.meshes.new('Seg_%s' % on); me.from_pydata(verts[on], [], faces[on]); me.update()
        for p in me.polygons:
            if p.normal.y > 0: p.flip()
        me.update(); me.materials.append(m)
        o = bpy.data.objects.new('Seg_%s' % on, me); bpy.context.scene.collection.objects.link(o)
    # a faint warm glow spilling onto the bezel and the rail (what the bloom of the real display would give)
    ld = bpy.data.lights.new('SegGlow', 'AREA'); ld.energy = 6; ld.size = 1.3; ld.size_y = .25; ld.shape = 'RECTANGLE'; ld.color = (1, .55, .15)
    lo = bpy.data.objects.new('SegGlow', ld); bpy.context.scene.collection.objects.link(lo)
    lo.location = c + Vector((0, -.05, 0)); lo.rotation_euler = (math.radians(-90), 0, 0)


# ---------------------------------------------------------------------------------------------- shots
def studio_shots():
    cam = studio(); root = load()
    aim(cam, (0, -5.6, 1.35), (0, -.1, 1.25), 50); shot('front.jpg', (1600, 1200))
    aim(cam, (-3.1, -4.3, 2.0), (0, -.1, 1.3), 42); shot('three_quarter.jpg', (1600, 1200))
    aim(cam, (.9, 5.2, 1.7), (0, 0, 1.3), 46); shot('back.jpg', (1600, 1200))
    aim(cam, (1.4, -.85, 2.25), (.72, -.19, 2.08), 45); shot('detail_chain.jpg', (1200, 1000))
    aim(cam, (1.85, -1.05, .55), (1.32, .02, .22), 40); shot('detail_foot.jpg', (1200, 1000))
    aim(cam, (-.9, 1.3, 1.75), (.2, -.12, 1.58), 32); shot('detail_back.jpg', (1200, 1000))
    seven_seg(Vector(), strength=1.6)
    aim(cam, (.25, -2.3, 1.62), (0, -.2, 1.53), 38); shot('close_digits.jpg', (1600, 1000))
    cam = studio(bright=True); load(); seven_seg(Vector(), strength=2.2)
    aim(cam, (-1.6, -4.4, 1.6), (0, -.1, 1.35), 45); shot('bright_background.jpg', (1600, 1200))


def stage_shots():
    eevee(32)
    cam = cam_setup()
    root = load(); root.location = ROOT_AT
    # Stage_Review.blend lights only the ruin; add a soft, cool overhead light over the entrance zone (the hall lights of the
    # venue / the passthrough brightness that Unity's environment light stands in for)
    ld = bpy.data.lights.new('VenueFill', 'AREA'); ld.energy = 260; ld.size = 3.0; ld.color = (.92, .95, 1)
    lo = bpy.data.objects.new('VenueFill', ld); bpy.context.scene.collection.objects.link(lo)
    lo.location = (0, -2.2, 3.4); lo.rotation_euler = (Vector((0, -.6, 1.2)) - lo.location).to_track_quat('-Z', 'Y').to_euler()
    aim(cam, (0, -2.5, 1.6), (0, -.55, 1.18), 17); shot('stage_practice_eye.jpg', (1600, 1000))
    aim(cam, (3.3, -1.25, 1.35), (0, -.25, 1.15), 22); shot('stage_side_clearance.jpg', (1600, 1000))
    aim(cam, (0, -.05, 3.6), (0, -.6, 0.0), 14); shot('stage_top_clearance.jpg', (1600, 1000))
    seven_seg(ROOT_AT)
    aim(cam, (0, -2.5, 1.6), (0, -.55, 1.18), 17); shot('stage_practice_eye_digits.jpg', (1600, 1000))
    aim(cam, (1.2, -2.5, 1.6), (.1, -.5, 1.2), 17); shot('stage_practice_side_digits.jpg', (1600, 1000))
    aim(cam, (-1.75, -5.6, 1.6), (-.45, -.3, 1.45), 30); shot('stage_corridor_w_digits.jpg', (1600, 1000))


if IN_STAGE:
    stage_shots()
else:
    studio_shots()
    if 'nostage' not in ARGS:
        subprocess.run([bpy.app.binary_path, '-b', str(STAGE), '--python', str(Path(__file__).resolve()), '--'], check=True)
