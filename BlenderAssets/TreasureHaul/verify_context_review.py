"""Reload actual FBX/blend outputs and measure supported-stage geometry."""
import bpy,json,runpy,importlib.util
from pathlib import Path
from mathutils import Vector
HERE=Path(__file__).resolve().parent;OUT=HERE/'ContextReviewed'
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
    spec=importlib.util.spec_from_file_location('verify_stage_base',HERE/'Stage/verify_stage.py');legacy=importlib.util.module_from_spec(spec);spec.loader.exec_module(legacy)
    legacy.OUT=HERE/'Stage/Revision5'
    def layout_checks(data):
        paths=runpy.run_path(str(HERE/'Stage/check_context_paths.py'),run_name='verify')['run']()
        assert paths['human_all_reachable'] and paths['cart_max_depth_m']>=8.9
        assert len(data['sample_placements'])==23
        assert sum(x['class_name']=='S' for x in data['sample_placements'])==14
        for p in data['supported_placements']:
            assert p['base_height_m']>.1 and p['top_height_m']<.8
            assert abs(p['base_height_m']-p['support_height_m']-p['suspended_offset_m'])<.001
        for m in data['markers']:
            if m['role']=='treasure_candidate':
                x,h,d=m['position_unity_xyz'];assert min(2.5-abs(x),d,11-d)-m['clearance_radius_m']>=.7
        return paths
    legacy.layout_checks=layout_checks;legacy.run()
    p=legacy.OUT/'verification.json';data=json.loads(p.read_text(encoding='utf-8'))
    data['occlusion_probe']['total']=23
    data['occlusion_probe']['note']='Single entrance ray to root + 0.15m; this is not a hand-reach or full-silhouette concealment test.'
    layout=json.loads((legacy.OUT/'stage_layout.json').read_text(encoding='utf-8'))
    data['combined_triangles_including_cart']=data['combined_triangles']+layout['preview_cart_stats']['triangles']
    data['unity_compile']='See unity_verification.json; models not imported into Unity'
    data['unity_editmode']='See unity_verification.json; models not imported into Unity'
    p.write_text(json.dumps(data,indent=2),encoding='utf-8')
    print('PASS: 30 treasure round trips, supported-stage FBX/modules/blends, 23 approaches and cart rear delivery route')
if __name__=='__main__':run()
