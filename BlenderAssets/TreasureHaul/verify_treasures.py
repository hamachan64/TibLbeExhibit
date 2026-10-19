"""Reload the treasure FBX/blend files in Treasures/ and check them against stats.json.
Execute through Blender MCP."""
import bpy,json
from pathlib import Path
from mathutils import Vector
HERE=Path(__file__).resolve().parent;OUT=HERE/'Treasures'
def inspect(objects,expected):
    bpy.context.view_layer.update();meshes=[o for o in objects if o.type=='MESH']
    for o in meshes:o.data.calc_loop_triangles()
    triangles=sum(len(o.data.loop_triangles) for o in meshes)
    assert triangles==expected['triangles'] and triangles<=5000
    assert all(o.data.uv_layers for o in meshes)
    assert all(t.area>1e-12 for o in meshes for t in o.data.loop_triangles),expected['model']
    points=[o.matrix_world@v.co for o in meshes for v in o.data.vertices]
    dims=[max(p[i] for p in points)-min(p[i] for p in points) for i in range(3)]
    assert all(abs(a-b)<.001 for a,b in zip(dims,expected['dimensions_m']))
    grips=[o for o in meshes if any(any(n.type=='BSDF_PRINCIPLED' and n.inputs['Emission Strength'].default_value>0 for n in m.node_tree.nodes) for m in o.data.materials if m and m.use_nodes)]
    expected_grips=2 if expected['model'] in ('amphora','inscribed_stele','guardian_statue','relic_chest') else 1
    assert len(grips)==expected_grips,(expected['model'],len(grips))
    return dict(model=expected['model'],triangles=triangles,grips=len(grips),uv=True,nondegenerate=True,dimension_roundtrip=True)
def run():
    old=bpy.context.window.scene;selected=list(bpy.context.selected_objects);active=bpy.context.view_layer.objects.active
    scene=bpy.data.scenes.new('TH_Context_Verification');bpy.context.window.scene=scene;report=[]
    try:
        for expected in json.loads((OUT/'stats.json').read_text(encoding='utf-8')):
            for ext in ('fbx','blend'):
                before=set(bpy.data.objects);path=OUT/(expected['model']+'.'+ext)
                if ext=='fbx':bpy.ops.import_scene.fbx(filepath=str(path),use_anim=False)
                else:
                    with bpy.data.libraries.load(str(path),link=False) as (src,dst):dst.objects=src.objects
                    for o in dst.objects:
                        if o:scene.collection.objects.link(o)
                added=set(bpy.data.objects)-before
                result=inspect(added,expected);result['format']=ext;report.append(result)
                if ext=='blend':
                    for o in added:
                        if o.type=='MESH':
                            for m in o.data.materials:
                                for n in m.node_tree.nodes:
                                    if n.type=='TEX_IMAGE':assert n.image and n.image.packed_file
                for o in added:bpy.data.objects.remove(o,do_unlink=True)
        (OUT/'verification.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    finally:
        bpy.context.window.scene=old;bpy.data.scenes.remove(scene)
        bpy.ops.object.select_all(action='DESELECT')
        for o in selected:o.select_set(True)
        bpy.context.view_layer.objects.active=active
    # The stage is verified separately (Stage/verify_stage.py).
    print('PASS: treasure FBX/blend round trips')
if __name__=='__main__':run()
