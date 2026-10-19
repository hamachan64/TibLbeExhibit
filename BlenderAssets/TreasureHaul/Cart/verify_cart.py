"""Blender geometry / FBX round-trip and articulated-pivot checks for Cart/Output. Not Unity tests."""
import bpy, json, math
from pathlib import Path
OUT=Path(__file__).resolve().parent/'Output'

def inspect(objects,expected):
    meshes=[o for o in objects if o.type=='MESH']
    for o in meshes:
        o.data.calc_loop_triangles()
        assert o.data.uv_layers, o.name
        assert all(t.area>1e-12 for t in o.data.loop_triangles),o.name
    points=[o.matrix_world@v.co for o in meshes for v in o.data.vertices]
    bounds=[[min(p[i] for p in points),max(p[i] for p in points)] for i in range(3)]
    triangles=sum(len(o.data.loop_triangles) for o in meshes)
    assert triangles==expected['triangles']<=5000
    assert len(meshes)==expected['meshes']
    assert max(abs(bounds[i][j]-expected['bounds_blender'][i][j]) for i in range(3) for j in range(2))<.001
    assert bounds[2][0]>=-.0001 and bounds[2][1]<.8
    parts={o.get('part_id'):o for o in objects if o.get('part_id')}
    wheels=[o for key,o in parts.items() if key.startswith('Wheel_')]
    casters=[o for key,o in parts.items() if key.startswith('Caster_')]
    assert len(wheels)==4 and len(casters)==2
    for wheel in wheels:
        assert abs(wheel.matrix_world.translation.z-.1)<.001
        child=next(o for o in wheel.children if o.type=='MESH')
        local=[child.matrix_local@v.co for v in child.data.vertices]
        assert abs(max(math.hypot(p.y,p.z) for p in local)-.1)<.001
        before=wheel.matrix_world.translation.copy();rotation=wheel.rotation_euler.copy()
        wheel.rotation_euler.x+=math.pi/2;bpy.context.view_layer.update()
        assert (wheel.matrix_world.translation-before).length<1e-6
        wheel.rotation_euler=rotation;bpy.context.view_layer.update()
    for caster in casters:
        wheel=next(o for o in caster.children if o.get('part_id','').startswith('Wheel_'))
        offset=wheel.matrix_world.translation-caster.matrix_world.translation
        rotation=caster.rotation_euler.copy();caster.rotation_euler.z+=math.pi/2;bpy.context.view_layer.update()
        after=wheel.matrix_world.translation-caster.matrix_world.translation
        assert abs(after.length-offset.length)<1e-5
        assert abs(after.z-offset.z)<1e-5
        assert (after-offset).length>.05
        caster.rotation_euler=rotation;bpy.context.view_layer.update()
    handle=parts['PushHandlePivot']
    assert all(parts['PushGrip_'+side].parent==handle for side in ('L','R'))
    assert not any(o.type in ('LIGHT','CAMERA') for o in objects)
    return dict(triangles=triangles,meshes=len(meshes),bounds_blender=bounds,wheel_pivots=4,swivel_pivots=2,grip_anchors=2)

def run():
    expected=json.loads((OUT/'cart_stats.json').read_text(encoding='utf-8'))
    original=bpy.context.window.scene;selection=list(bpy.context.selected_objects);active=bpy.context.view_layer.objects.active
    scene=bpy.data.scenes.new('THCart_VerifyOnly');bpy.context.window.scene=scene
    created=set();loaded=None
    try:
        before=set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=str(OUT/'ExcavationCart.fbx'),use_anim=False)
        created=set(bpy.data.objects)-before;bpy.context.view_layer.update()
        result={'fbx':inspect(created,expected)}
        with bpy.data.libraries.load(str(OUT/'ExcavationCart.blend'),link=False) as (src,dst):dst.scenes=src.scenes
        assert len(dst.scenes)==1
        loaded=dst.scenes[0];bpy.context.window.scene=loaded;bpy.context.view_layer.update()
        result['blend']=inspect(list(loaded.objects),expected)
        for o in loaded.objects:
            if o.type!='MESH':continue
            for mat in o.data.materials:
                for node in mat.node_tree.nodes:
                    if node.type=='TEX_IMAGE':assert node.image and node.image.packed_file
        result.update(unity_imported=False,unity_compile='not run',unity_editmode='not run',pico_fps='not measured',physical_cart_fit='not measured')
        (OUT/'verification.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
        print(json.dumps(result,indent=2))
    finally:
        bpy.context.window.scene=original
        if loaded:
            loaded_objects=list(loaded.objects);bpy.data.scenes.remove(loaded)
            for obj in loaded_objects:bpy.data.objects.remove(obj,do_unlink=True)
        for obj in created:bpy.data.objects.remove(obj,do_unlink=True)
        bpy.data.scenes.remove(scene)
        bpy.ops.object.select_all(action='DESELECT')
        for obj in selection:obj.select_set(True)
        bpy.context.view_layer.objects.active=active

if __name__=='__main__':run()
