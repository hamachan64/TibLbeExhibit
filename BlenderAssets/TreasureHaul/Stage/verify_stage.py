"""Measured geometry, FBX round trips, candidate clearance and approach checks."""
import bpy,json,math,itertools
from pathlib import Path
from mathutils import Vector
OUT=Path(__file__).resolve().parent

def inspect(objects):
    meshes=[o for o in objects if o.type=='MESH']
    for o in meshes:o.data.calc_loop_triangles()
    assert all(o.data.uv_layers for o in meshes),'Missing UVs'
    assert all(t.area>1e-12 for o in meshes for t in o.data.loop_triangles),'Degenerate face'
    pts=[o.matrix_world@v.co for o in meshes for v in o.data.vertices]
    assert all(-2.5001<=p.x<=2.5001 and -11.0001<=p.y<=.0001 and -.0001<=p.z<.8 for p in pts),'Venue/height bounds'
    return dict(triangles=sum(len(o.data.loop_triangles) for o in meshes),meshes=len(meshes),
                max_height_m=max(p.z for p in pts),material_slots=sum(len(o.data.materials) for o in meshes))

def disk_box_clear(x,d,r,collider):
    cx,h,cd=collider['center_unity_xyz'];w,hh,dd=collider['size_unity_xyz']
    return math.hypot(max(abs(x-cx)-w/2,0),max(abs(d-cd)-dd/2,0))>=r-1e-6

def layout_checks(data):
    candidates=[m for m in data['markers'] if m['role']=='treasure_candidate']
    assert len(candidates)>=23
    obstacles=[c for c in data['colliders'] if c['name']!='VenueFloor']
    min_edge=11
    for m in candidates:
        x,h,d=m['position_unity_xyz'];r=m['clearance_radius_m']
        edge=min(2.5-abs(x),d,11-d)-r;min_edge=min(min_edge,edge)
        assert edge>=.7-1e-6,(m['name'],'edge margin')
        assert d-r>2.8,(m['name'],'tutorial area')
        assert abs(x)-r>=.6-1e-6,(m['name'],'central aisle')
        assert all(disk_box_clear(x,d,r,c) for c in obstacles),(m['name'],'collider overlap')
        # Reach the depth along the center aisle, then approach laterally. Human proxy
        # radius .30m is only an authoring check, not device or crowd certification.
        route=[(0,1.65+(d-1.65)*i/100) for i in range(101)]+[(x*i/100,d) for i in range(101)]
        assert all(disk_box_clear(px,pd,.30,c) for px,pd in route for c in obstacles),(m['name'],'approach blocked')
    for a,b in itertools.combinations(candidates,2):
        ax,_,ad=a['position_unity_xyz'];bx,_,bd=b['position_unity_xyz']
        assert math.hypot(ax-bx,ad-bd)>=a['clearance_radius_m']+b['clearance_radius_m']-1e-6
    training=[m for m in data['markers'] if m['role']=='tutorial'];assert len(training)==4
    for m in training:
        x,_,d=m['position_unity_xyz'];assert all(disk_box_clear(x,d,.30,c) for c in obstacles)
    sample=data['sample_placements'];assert len(sample)==23
    assert sum(m['class_name']=='S' for m in sample)==14 and sum(m['class_name']=='M' for m in sample)==9
    return dict(candidate_count=len(candidates),min_treasure_edge_margin_m=round(min_edge,3),approaches_passed=len(candidates),
                tutorial_points=4,sample_population='14 S + 9 M',human_proxy_radius_m=.30)

def run():
    data=json.loads((OUT/'stage_layout.json').read_text(encoding='utf-8'))
    result={'layout':layout_checks(data),'fbx':[],'blend':[]}
    original=bpy.context.window.scene;selected=list(bpy.context.selected_objects);active=bpy.context.view_layer.objects.active
    scene=bpy.data.scenes.new('THStage_VerifyOnly');bpy.context.window.scene=scene
    try:
        paths=[('ExcavationDepot_5x11.fbx',data['runtime_stats'])]+[('modules/'+m['name']+'.fbx',m) for m in data['module_stats']]
        for path,expected in paths:
            previous=set(bpy.data.objects)
            bpy.ops.import_scene.fbx(filepath=str(OUT/path),use_anim=False)
            added=set(bpy.data.objects)-previous;bpy.context.view_layer.update()
            actual=inspect(added)
            assert actual['triangles']==expected['triangles'] and actual['meshes']==expected['meshes'],path
            if path=='ExcavationDepot_5x11.fbx':
                markers={o.get('anchor_id'):o for o in added if o.get('anchor_id')}
                assert len(markers)==len(data['markers'])
                for m in data['markers']:
                    assert (markers[m['name']].matrix_world.translation-Vector(m['position_blender_xyz'])).length<.001
                dg=bpy.context.evaluated_depsgraph_get();eye=Vector((0,-2.4,1.65));hidden=0
                for m in data['markers']:
                    if m['role']!='treasure_candidate':continue
                    target=Vector(m['position_blender_xyz'])+Vector((0,0,.15));direction=target-eye
                    hit=scene.ray_cast(dg,eye,direction.normalized(),distance=direction.length-.02)[0]
                    hidden+=bool(hit)
                result['occlusion_probe']={'eye_height_m':1.65,'target_height_m':.15,'occluded_candidate_targets':hidden,'total':28}
            result['fbx'].append(dict(path=path,**actual))
            for obj in added:bpy.data.objects.remove(obj,do_unlink=True)
        for file in ('ExcavationDepot_5x11.blend','Stage_Review.blend'):
            with bpy.data.libraries.load(str(OUT/file),link=False) as (src,dst):dst.scenes=src.scenes
            assert len(dst.scenes)==1
            loaded=dst.scenes[0];bpy.context.window.scene=loaded;bpy.context.view_layer.update()
            runtime=[o for o in loaded.objects if o.type=='MESH' and o.get('module_id')]
            actual=inspect(runtime);assert actual['triangles']==data['runtime_stats']['triangles']
            for o in runtime:
                for m in o.data.materials:
                    for n in m.node_tree.nodes:
                        if n.type=='TEX_IMAGE':assert n.image and n.image.packed_file
            if file=='Stage_Review.blend':assert loaded.camera and loaded.camera.location.length>1
            result['blend'].append(dict(path=file,**actual))
            bpy.context.window.scene=scene;bpy.data.scenes.remove(loaded)
        result['combined_triangles']=data['runtime_stats']['triangles']+data['preview_treasure_stats']['triangles']
        assert result['combined_triangles']<300000
        result.update(unity_imported=False,unity_compile='not run; Unity not running',unity_editmode='not run',pico_fps='not measured')
        (OUT/'verification.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
        print(json.dumps(result,indent=2))
    finally:
        bpy.context.window.scene=original;bpy.data.scenes.remove(scene)
        bpy.ops.object.select_all(action='DESELECT')
        for o in selected:o.select_set(True)
        bpy.context.view_layer.objects.active=active

if __name__=='__main__':run()
