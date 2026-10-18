"""Validate exported files and render an isolated additive preview in Blender.

Run after build_treasures.py. Removes only its own imported audit copies/preview scene.
"""
import bpy
import json
from pathlib import Path
from mathutils import Vector

OUT=Path(__file__).resolve().parent
selection=list(bpy.context.selected_objects)
active=bpy.context.view_layer.objects.active
originals={o.name:tuple(v for row in o.matrix_world for v in row) for o in bpy.data.objects if not o.name.startswith('TH_S_')}
stats=json.loads((OUT/'stats.json').read_text(encoding='utf-8'))
report=[]
for asset in stats['assets']:
    name=asset['name']
    before=set(bpy.data.objects)
    with bpy.data.libraries.load(str(OUT/(name+'.blend')),link=False) as (src,dst):
        dst.scenes=src.scenes
    loaded=dst.scenes[0]
    objects=[o for o in loaded.objects if o.type=='MESH']
    for o in objects:o.data.calc_loop_triangles()
    triangles=sum(len(o.data.loop_triangles) for o in objects)
    assert triangles==asset['triangles']
    roots=[o for o in loaded.objects if o.type=='EMPTY']
    assert len(roots)==1 and roots[0].location.length<1e-6
    packed=[]
    for o in objects:
        for m in o.data.materials:
            if m and m.use_nodes:
                for n in m.node_tree.nodes:
                    if n.type=='TEX_IMAGE' and n.image:
                        assert n.image.packed_file
                        packed.append(n.image.name)
    assert packed
    bpy.data.scenes.remove(loaded)
    for o in set(bpy.data.objects)-before:bpy.data.objects.remove(o,do_unlink=True)
    # Import into live scene transiently; only newly imported object IDs are removed.
    before=set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(OUT/(name+'.fbx')),use_anim=False)
    imported=set(bpy.data.objects)-before
    objects=[o for o in imported if o.type=='MESH']
    for o in objects:o.data.calc_loop_triangles()
    tri=sum(len(o.data.loop_triangles) for o in objects)
    assert tri==asset['triangles'],(name,tri)
    assert all(o.data.uv_layers for o in objects)
    corners=[o.matrix_world@Vector(c) for o in objects for c in o.bound_box]
    size=[max(v[i] for v in corners)-min(v[i] for v in corners) for i in range(3)]
    assert all(abs(a-b)<.0002 for a,b in zip(size,asset['dimensions_m'])),(name,size)
    grips=[o for o in objects if any(m and 'Grip_Emission' in m.name for m in o.data.materials)]
    assert len(grips)==1
    grip_material=next(m for m in grips[0].data.materials if m and 'Grip_Emission' in m.name)
    bsdf=next(n for n in grip_material.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    emission_color=list(bsdf.inputs['Emission Color'].default_value)
    emission_strength=bsdf.inputs['Emission Strength'].default_value
    assert emission_strength>0 and max(emission_color[:3])>0
    report.append({'asset':name,'blend_scene_at_origin':True,'blend_packed_textures':True,'fbx_triangles':tri,'fbx_dimensions_m':[round(v,4) for v in size],'fbx_uvs':True,'fbx_grip_count':len(grips),'fbx_emission_color_linear':emission_color,'fbx_emission_strength':emission_strength})
    for o in imported:bpy.data.objects.remove(o,do_unlink=True)

# Render with independent scene/world/camera/lights; never changes the active scene.
preview=bpy.data.scenes.new('TH_S_PreviewTemporary')
preview.collection.children.link(bpy.data.collections['TH_S_Treasures'])
world=bpy.data.worlds.new('TH_S_PreviewWorld')
world.use_nodes=True
background=next(n for n in world.node_tree.nodes if n.type=='BACKGROUND')
background.inputs['Color'].default_value=(.035,.043,.055,1)
background.inputs['Strength'].default_value=.45
preview.world=world
preview.render.engine='CYCLES'
preview.cycles.samples=32
preview.render.resolution_x=1500
preview.render.resolution_y=760
preview.render.resolution_percentage=100
preview.render.image_settings.file_format='PNG'
preview.render.filepath=str(OUT/'preview.png')
camera_data=bpy.data.cameras.new('TH_S_PreviewCamera')
camera=bpy.data.objects.new('TH_S_PreviewCamera',camera_data)
preview.collection.objects.link(camera)
camera.location=(3.34,-1.45,.68)
target=Vector((3.34,0,.17))
camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler()
camera_data.type='ORTHO';camera_data.ortho_scale=1.04
preview.camera=camera
for name,loc,power,size,color in [('Key',(2.9,-.5,1),75,.7,(1,.86,.65)),('Fill',(4,-.25,.6),45,.6,(.65,.85,1)),('Rim',(3.4,.45,.8),90,.5,(.6,1,.9))]:
    data=bpy.data.lights.new('TH_S_Preview'+name,'AREA');data.energy=power;data.shape='DISK';data.size=size;data.color=color
    obj=bpy.data.objects.new(data.name,data);preview.collection.objects.link(obj);obj.location=loc
    obj.rotation_euler=(target-obj.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.render.render(write_still=True,scene=preview.name)
preview_only=list(preview.collection.objects)
bpy.data.scenes.remove(preview)
for o in preview_only:bpy.data.objects.remove(o,do_unlink=True)
bpy.ops.object.select_all(action='DESELECT')
for o in selection:o.select_set(True)
bpy.context.view_layer.objects.active=active
assert all(tuple(v for row in bpy.data.objects[name].matrix_world for v in row)==matrix for name,matrix in originals.items())
result={'original_objects_preserved':True,'unity_imported':False,'unity_compile_and_editmode':'not run: no Unity import requested','checks':report}
(OUT/'verification.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
print(json.dumps(result,indent=2))
