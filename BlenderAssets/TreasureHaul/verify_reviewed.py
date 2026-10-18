"""Round-trip assertions against the actual reviewed exports, in an isolated scene."""
import bpy, bmesh, json
from pathlib import Path
from mathutils import Vector
OUT=Path(__file__).resolve().parent/'Reviewed'

def inspect(objects,expected):
    meshes=[o for o in objects if o.type=='MESH']
    for o in meshes:o.data.calc_loop_triangles()
    tri=sum(len(o.data.loop_triangles) for o in meshes)
    assert tri==expected['after']['triangles'],(expected['name'],tri)
    assert len(meshes)==expected['after']['meshes']
    assert all(o.data.uv_layers for o in meshes)
    assert all(t.area>1e-12 for o in meshes for t in o.data.loop_triangles),'degenerate triangles'
    corners=[o.matrix_world@Vector(c) for o in meshes for c in o.bound_box]
    dims=[max(v[i] for v in corners)-min(v[i] for v in corners) for i in range(3)]
    assert all(abs(a-b)<.001 for a,b in zip(dims,expected['after']['dimensions_m'])),dims
    mats=[m for o in meshes for m in o.data.materials if m]
    emission=[m for m in mats if 'Grip_Emission' in m.name]
    assert len(emission)==expected['grips']
    for m in emission:
        bs=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
        assert bs.inputs['Emission Strength'].default_value>0
    assert any('WornGilt' in m.name for m in mats),'gold trim lost'
    boundary=0
    for o in meshes:
        bm=bmesh.new();bm.from_mesh(o.data)
        boundary+=sum(e.is_boundary for e in bm.edges);bm.free()
    return dict(triangles=tri,meshes=len(meshes),material_slots=len(mats),boundary_edges=boundary,dimensions_m=dims)

def run():
    stats=json.loads((OUT/'review_stats.json').read_text(encoding='utf-8'))
    original=bpy.context.window.scene
    scene=bpy.data.scenes.new('TH_Review_Verification');bpy.context.window.scene=scene
    results=[]
    try:
        for item in stats:
            for file in item['variants']:
                before=set(bpy.data.objects)
                bpy.ops.import_scene.fbx(filepath=str(OUT/file),use_anim=False)
                added=set(bpy.data.objects)-before
                measured=inspect(added,item)
                mats=[m for o in added if o.type=='MESH' for m in o.data.materials if m]
                if 'pattern_jade' in file:
                    jade=[m for m in mats if 'Jade_Inlay' in m.name]; assert jade
                    for m in jade:
                        bs=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
                        assert bs.inputs['Metallic'].default_value<.01
                results.append(dict(file=file,**measured))
                for obj in added:bpy.data.objects.remove(obj,do_unlink=True)
            with bpy.data.libraries.load(str(OUT/(item['name']+'.blend')),link=False) as (src,dst):dst.scenes=src.scenes
            assert len(dst.scenes)==1
            loaded=dst.scenes[0]
            bpy.context.window.scene=loaded
            bpy.context.view_layer.update()
            inspect(list(loaded.objects),item)
            for obj in loaded.objects:
                if obj.type!='MESH':continue
                for mat in obj.data.materials:
                    for node in mat.node_tree.nodes:
                        if node.type=='TEX_IMAGE' and node.image:assert node.image.packed_file
            bpy.context.window.scene=scene
            bpy.data.scenes.remove(loaded)
        result=dict(fbx_passed=len(results),blend_passed=len(stats),checks=results,
                    unity_compile='not run',unity_editmode='not run',pico_fps='not measured')
        (OUT/'verification.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
        print(json.dumps(dict(fbx_passed=len(results),blend_passed=len(stats))))
    finally:
        bpy.context.window.scene=original
        bpy.data.scenes.remove(scene)

if __name__=='__main__':run()
