"""Round-trip validation for all 10 Treasure Haul treasure types and FBX patterns."""
import bpy,json
from pathlib import Path
from mathutils import Vector

OUT=Path(__file__).resolve().parent
s=json.loads((OUT/'S'/'stats.json').read_text(encoding='utf-8'))
t=json.loads((OUT/'ten_type_stats.json').read_text(encoding='utf-8'))
records={a['name']:a for a in s['assets']+t['assets']}
for a in s['assets']:
    if 'dimensions_m' not in records[a['name']]:records[a['name']].update(a)
paths={}
for a in s['assets']:
    name=a['name'];paths[name]=[OUT/'S'/(name+'.fbx'),OUT/(name+'_pattern_cinnabar.fbx'),OUT/(name+'_pattern_jade.fbx')]
for a in t['assets']:
    if a.get('fbx_variants'):
        paths[a['name']]=[OUT/n for n in a['fbx_variants']]
report=[]
for name,fbxs in paths.items():
    expected=records[name]
    for path in fbxs:
        assert path.is_file(),str(path)
        before=set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=str(path),use_anim=False)
        imported=set(bpy.data.objects)-before
        meshes=[o for o in imported if o.type=='MESH']
        for o in meshes:o.data.calc_loop_triangles()
        triangles=sum(len(o.data.loop_triangles) for o in meshes)
        assert triangles==expected['triangles'],(path.name,triangles,expected['triangles'])
        corners=[o.matrix_world@Vector(c) for o in meshes for c in o.bound_box]
        dimensions=[max(v[i] for v in corners)-min(v[i] for v in corners) for i in range(3)]
        assert all(abs(a-b)<.001 for a,b in zip(dimensions,expected['dimensions_m'])),(path.name,dimensions,expected['dimensions_m'])
        handle_materials=[]
        for o in meshes:
            for mat in o.data.materials:
                if mat and 'Grip_Emission' in mat.name:handle_materials.append(mat)
        grips=1 if expected['class']=='S' or expected['class']=='V' else 2
        assert len(handle_materials)==grips,(path.name,len(handle_materials),grips)
        for mat in handle_materials:
            shader=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
            assert shader.inputs['Emission Strength'].default_value>0,(path.name,'zero emission')
        report.append({'model':name,'variant':path.name,'triangles':triangles,'dimensions_m':[round(v,4) for v in dimensions],'emissive_materials':len(handle_materials),'expected_grip_count':grips})
        for o in imported:bpy.data.objects.remove(o,do_unlink=True)

# Verify every new .blend opens with its own scene and packed texture data.
for a in t['assets']:
    if not a.get('blend'):continue
    path=OUT/a['blend'];before=set(bpy.data.scenes)
    with bpy.data.libraries.load(str(path),link=False) as (src,dst):dst.scenes=src.scenes
    loaded=[sc for sc in dst.scenes if sc]
    assert len(loaded)==1,(path.name,len(loaded))
    objects=list(loaded[0].objects);meshes=[o for o in objects if o.type=='MESH']
    for o in meshes:o.data.calc_loop_triangles()
    triangles=sum(len(o.data.loop_triangles) for o in meshes)
    assert triangles==a['triangles'],(path.name,triangles)
    images=[im for o in meshes for mat in o.data.materials if mat and mat.use_nodes for node in mat.node_tree.nodes if node.type=='TEX_IMAGE' for im in [node.image] if im]
    # The secret sun mask is intentionally solid gold in its standalone .blend;
    # it has no image texture until a patterned FBX variant is exported.
    assert all(im.packed_file for im in images),(path.name,'unpacked image')
    bpy.data.scenes.remove(loaded[0])

result={'model_count':len(records),'fbx_variant_count':sum(map(len,paths.values())),'all_under_5000_tri':all(a['triangles']<=5000 for a in records.values()),'blend_scene_and_packed_texture_checks':len(t['assets']),'fbx_roundtrip_checks':report,'unity_imported':False}
(OUT/'ten_type_verification.json').write_text(json.dumps(result,indent=2,ensure_ascii=False),encoding='utf-8')
print(json.dumps({'model_count':result['model_count'],'fbx_variant_count':result['fbx_variant_count'],'all_under_5000_tri':result['all_under_5000_tri'],'blend_scene_and_packed_texture_checks':result['blend_scene_and_packed_texture_checks'],'unity_imported':False},indent=2))
