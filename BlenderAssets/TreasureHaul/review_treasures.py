"""Non-destructive review pass. Run via Blender MCP; originals remain in the open scene.

Loads standalone sources, writes reviewed assets to Reviewed/, and restores the user's
scene. Re-runnable from the committed originals; no external assets or network calls.
"""
import bpy, bmesh, json, math
from pathlib import Path
from mathutils import Vector

BASE = Path(__file__).resolve().parent
OUT = BASE / 'Reviewed'
OUT.mkdir(exist_ok=True)
SPECS = [('mask','S'), ('cup','S'), ('statuette','S'),
         ('scarab_amulet','S'), ('ceremonial_lamp','S'), ('amphora','M'),
         ('inscribed_stele','M'), ('guardian_statue','L'),
         ('relic_chest','L'), ('sun_mask_secret','V')]

def active(objects):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects: obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]

def grip(obj):
    return 'Handle' in obj.name and not any(s in obj.name for s in ('Socket','Mount'))

def measure(objects):
    meshes = [o for o in objects if o.type == 'MESH']
    for o in meshes: o.data.calc_loop_triangles()
    corners = [o.matrix_world @ Vector(c) for o in meshes for c in o.bound_box]
    return dict(triangles=sum(len(o.data.loop_triangles) for o in meshes),
                vertices=sum(len(o.data.vertices) for o in meshes),
                meshes=len(meshes), material_slots=sum(len(o.data.materials) for o in meshes),
                dimensions_m=[round(max(v[i] for v in corners)-min(v[i] for v in corners),5) for i in range(3)])

def clean(obj):
    bm = bmesh.new(); bm.from_mesh(obj.data)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=0.000001)
    bmesh.ops.dissolve_degenerate(bm, edges=list(bm.edges), dist=0.0000001)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(obj.data); bm.free(); obj.data.update()

def replace_mesh(obj, verts, faces):
    materials = list(obj.data.materials)
    data = bpy.data.meshes.new(obj.name + '_Reviewed')
    data.from_pydata(verts, [], faces); data.update()
    obj.data = data
    for mat in materials: data.materials.append(mat)
    obj.location = (0,0,0); obj.rotation_euler = (0,0,0); obj.scale=(1,1,1)

def fixes(objects):
    for obj in objects:
        if obj.type != 'MESH': continue
        name = obj.name
        if 'Stele_Glyph' in name:
            # Original abstract relief, not readable text or copied historical script.
            z=float(obj.location.z); row=round((z-.1)/.06); vs=[]; fs=[]
            for column,x in enumerate((-.085,0,.085)):
                flip=1 if (row+column)%2 else -1
                points=[(x-.022,z+.014*flip),(x,z-.014*flip),(x+.022,z+.014*flip)]
                segments=[(points[0],points[1]),(points[1],points[2]),((x,z-.014),(x,z+.022))]
                for (ax,az),(bx,bz) in segments:
                    dx,dz=bx-ax,bz-az; length=math.hypot(dx,dz); nx,nz=-dz/length*.002,dx/length*.002
                    start=len(vs)
                    vs.extend([(ax+nx,y,az+nz) for y in (-.043,-.037)])
                    vs.extend([(bx+nx,y,bz+nz) for y in (-.043,-.037)])
                    vs.extend([(bx-nx,y,bz-nz) for y in (-.043,-.037)])
                    vs.extend([(ax-nx,y,az-nz) for y in (-.043,-.037)])
                    fs.extend([tuple(start+i for i in f) for f in ((0,2,4,6),(1,7,5,3),(0,1,3,2),(2,3,5,4),(4,5,7,6),(6,7,1,0))])
            replace_mesh(obj,vs,fs)
        if 'Stele_Crown' in name:
            replace_mesh(obj, [(-.17,-.035,.42),(-.12,-.035,.49),(0,-.035,.53),(.12,-.035,.49),(.17,-.035,.42)], [(0,1,2,3,4)])
        if 'Lamp_Spout' in name and 'Rim' not in name:
            # Closed, tapered directional spout with a recessed opening; not a lathe.
            rings=[(.035,.025,.016),(.064,.045,.014),(.088,.068,.010),(.088,.068,.006),(.061,.047,.006)]
            vs=[(x,y+r*math.cos(i*math.tau/16),z+r*math.sin(i*math.tau/16)) for x,z,r in rings for i in range(16) for y in [0]]
            fs=[(j*16+i,j*16+(i+1)%16,(j+1)*16+(i+1)%16,(j+1)*16+i) for j in range(4) for i in range(16)]
            fs += [tuple(reversed(range(16))),tuple(range(64,80))]
            replace_mesh(obj,vs,fs)
            for poly in obj.data.polygons:poly.use_smooth=poly.index<64
        if 'Lamp_SpoutRim' in name:
            obj.rotation_euler.y=math.pi/2; obj.location=(.089,0,.068)
        if 'Guardian_Cheek' in name:
            obj.scale=(.8,.8,.65)  # Cast muzzle, less spherical/toy-like.
        if 'Guardian_Mane' in name:
            obj.location.x *= .76  # Attach previously floating beads to torso.
            obj.scale=(.8,.8,1.5)
        if 'Guardian_HandleSocket' in name:
            obj.location.x = math.copysign(.20,obj.location.x)
            obj.location.z = .32 if obj.location.z < .34 else .36
            obj.scale=(3.4,1,1)
        if 'Chest_HandleSocket' in name:
            obj.scale.x=1.4
        if 'Chest_Band' in name:
            obj.location.y=math.copysign(.176,obj.location.y)
        if 'SunMask_Ray' in name:
            # Broad eight-point crown: legible treasure silhouette at a distance.
            center=sum((v.co for v in obj.data.vertices),Vector())/len(obj.data.vertices)
            side=1 if center.x>0 else -1
            rank=min(range(4),key=lambda i:abs(abs(center.x)-(.065+i*.008)))
            angle=.32+rank*.36
            x=side*.084*math.sin(angle); z=.16+.124*math.cos(angle)
            tip=(side*.142*math.sin(angle),-.006,.16+.192*math.cos(angle))
            replace_mesh(obj,[(x-side*.012*math.cos(angle),-.006,z+.012*math.sin(angle)),
                              (x+side*.012*math.cos(angle),-.006,z-.012*math.sin(angle)),tip],[(0,1,2)])
        if any(s in name for s in ('Scarab_Antenna','Guardian_Ear','SunMask_Ray','Stele_Crown')):
            active([obj]); m=obj.modifiers.new('Cast thickness','SOLIDIFY'); m.thickness=.004
            bpy.ops.object.modifier_apply(modifier=m.name)
        clean(obj)
        if not obj.data.uv_layers:
            active([obj]); bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
            bpy.ops.uv.smart_project(island_margin=.02); bpy.ops.object.mode_set(mode='OBJECT')

def add_treasure_symbols(objects,coll,root,name,gem):
    gold=next(m for o in objects if o.type=='MESH' for m in o.data.materials if m and 'WornGilt' in m.name)
    def ornament(label,loc,scale,mat):
        # Faceted, closed jewel or gold setting; 20 triangles, readable broad planes.
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1,radius=1)
        obj=bpy.context.object;obj.name='Reviewed_'+name+'_'+label
        for c in list(obj.users_collection):c.objects.unlink(obj)
        coll.objects.link(obj);obj.parent=root;obj.location=loc;obj.scale=scale
        obj.data.materials.append(mat);objects.append(obj)
        active([obj]);bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
        bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.uv.smart_project(island_margin=.02);bpy.ops.object.mode_set(mode='OBJECT')
    if name=='relic_chest':
        ornament('GoldLockSetting',(0,-.21,.25),(.043,.013,.048),gold)
        ornament('JewelLock',(0,-.224,.25),(.027,.014,.032),gem)
    if name=='inscribed_stele':
        ornament('GoldSeal',(0,-.044,.45),(.039,.012,.042),gold)
        ornament('JewelSeal',(0,-.057,.45),(.024,.009,.026),gem)
    if name=='sun_mask_secret':
        ornament('CrownJewel',(0,-.038,.262),(.027,.014,.031),gem)
    if name=='guardian_statue':
        ornament('GoldPectoral',(0,-.116,.43),(.066,.019,.052),gold)
        ornament('PectoralJewel',(0,-.138,.43),(.037,.015,.033),gem)

def run():
    original_scene=bpy.context.window.scene
    original_selection=list(bpy.context.selected_objects)
    original_active=bpy.context.view_layer.objects.active
    original_objects=set(bpy.data.objects)
    report=[]; preview_objects=[]
    review=bpy.data.scenes.new('TH_Reviewed_Showcase')
    review.unit_settings.system='METRIC'; review.unit_settings.scale_length=1
    bpy.context.window.scene=review
    # Use copies of the existing, original local materials, including packed images.
    skins=[]
    for name in ('Bronze_Patina','Cinnabar_Gilt','Jade_Inlay'):
        mat=bpy.data.materials['TH10_'+name].copy(); mat.name='Reviewed_'+name
        shader=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
        shader.inputs['Metallic'].default_value=0.0 if name=='Jade_Inlay' else (.25 if name=='Cinnabar_Gilt' else .72)
        shader.inputs['Roughness'].default_value=.42 if name=='Jade_Inlay' else .52
        skins.append(mat)
    gem=bpy.data.materials.new('Reviewed_TurquoiseJewel');gem.use_nodes=True
    shader=next(n for n in gem.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    shader.inputs['Base Color'].default_value=(.008,.23,.18,1)
    shader.inputs['Metallic'].default_value=0
    shader.inputs['Roughness'].default_value=.22
    try:
        for index,(name,kind) in enumerate(SPECS):
            source=BASE/('S/'+name+'.blend' if index<3 else name+'.blend')
            with bpy.data.libraries.load(str(source),link=False) as (src,dst): dst.scenes=src.scenes
            loaded=dst.scenes[0]
            objects=list(loaded.objects)
            roots=[o for o in objects if o.type=='EMPTY' and o.parent is None]
            assert len(roots)==1,(name,len(roots))
            root=roots[0]; root.location=(0,0,0)
            coll=bpy.data.collections.new('Reviewed_'+name); review.collection.children.link(coll)
            for obj in objects: coll.objects.link(obj)
            bpy.data.scenes.remove(loaded)
            bpy.context.view_layer.update()
            before=measure(objects)
            fixes(objects)
            add_treasure_symbols(objects,coll,root,name,gem)
            body=[o for o in objects if o.type=='MESH' and not grip(o)]
            handles=[o for o in objects if o.type=='MESH' and grip(o)]
            # Preserve gold and recesses; replace only bronze surface slots.
            for obj in body:
                if 'SunMask_Face' in obj.name:
                    face_gold=obj.data.materials[0].copy(); face_gold.name='Reviewed_SecretFaceGold'
                    obj.data.materials[0]=face_gold
                for i,mat in enumerate(obj.data.materials):
                    if mat and any(s in mat.name for s in ('Bronze','Patina')): obj.data.materials[i]=skins[0]
            active(body); bpy.ops.object.join()
            body=bpy.context.object; body.name='Reviewed_'+name+'_Body'
            # Join keeps repeated slots; collapse identical materials to a single submesh.
            old=list(body.data.materials); unique=list(dict.fromkeys(old))
            ids=[unique.index(old[p.material_index]) for p in body.data.polygons]
            body.data.materials.clear()
            for mat in unique: body.data.materials.append(mat)
            for poly,mi in zip(body.data.polygons,ids): poly.material_index=mi
            objects=[root,body,*handles]
            bpy.context.view_layer.update()
            after=measure(objects)
            assert after['triangles']<=5000,(name,after)
            assert len(handles)==(1 if kind in ('S','V') else 2)
            root['triangle_count']=after['triangles']; root['handle_count']=len(handles)
            root['reviewed']=True
            variants=[]
            base_mats=list(body.data.materials)
            for vi,skin in enumerate(skins):
                for i,mat in enumerate(base_mats):
                    body.data.materials[i]=skin if mat==skins[0] or (vi>0 and 'SecretFaceGold' in mat.name) else mat
                suffix=('', '_pattern_cinnabar','_pattern_jade')[vi]
                path=OUT/(name+suffix+'.fbx')
                active(objects)
                bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,object_types={'EMPTY','MESH'},apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',axis_forward='-Z',axis_up='Y',use_mesh_modifiers=True,mesh_smooth_type='FACE',use_custom_props=True,path_mode='COPY',embed_textures=True,add_leaf_bones=False,bake_anim=False)
                variants.append(path.name)
            for i,mat in enumerate(base_mats):body.data.materials[i]=mat
            asset_scene=bpy.data.scenes.new('Reviewed_'+name)
            asset_scene.collection.children.link(coll); asset_scene.unit_settings.system='METRIC'
            bpy.data.libraries.write(str(OUT/(name+'.blend')),{asset_scene},fake_user=True,compress=True)
            bpy.data.scenes.remove(asset_scene)
            report.append(dict(name=name,kind=kind,before=before,after=after,grips=len(handles),variants=variants))
            root.location=((index-4.5)*.85,0,0)
            preview_objects.extend(objects)
        # Reuse authored lighting/camera, copied independently from the user's scene.
        for obj in original_scene.objects:
            if obj.type in ('LIGHT','CAMERA'):
                dup=obj.copy();dup.data=obj.data.copy();review.collection.objects.link(dup)
                if obj==original_scene.camera:review.camera=dup
        review.world=original_scene.world.copy() if original_scene.world else None
        review.render.engine=original_scene.render.engine
        review.render.resolution_x=2200;review.render.resolution_y=700;review.render.resolution_percentage=100
        camera=review.camera; camera.data.type='ORTHO';camera.data.ortho_scale=8.8
        camera.location=(0,-10,3.5);camera.rotation_euler=(Vector((0,0,.3))-camera.location).to_track_quat('-Z','Y').to_euler()
        review.render.filepath=str(OUT/'showcase.png')
        bpy.data.libraries.write(str(OUT/'showcase.blend'),{review},fake_user=True,compress=True)
        (OUT/'review_stats.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
        print(json.dumps(report,indent=2))
    finally:
        bpy.context.window.scene=original_scene
        active(original_selection) if original_selection else bpy.ops.object.select_all(action='DESELECT')
        bpy.context.view_layer.objects.active=original_active
        assert original_objects.issubset(set(bpy.data.objects))

def render_previews(scene):
    """Render saved lineup, then close-ups; always restore authored transforms."""
    bpy.ops.render.render(write_still=True,scene=scene.name)
    roots=[o for o in scene.objects if o.type=='EMPTY']
    transforms={o:o.matrix_world.copy() for o in roots}
    visibility={o:o.hide_render for o in scene.objects}
    camera=scene.camera; camera_matrix=camera.matrix_world.copy(); ortho=camera.data.ortho_scale
    resolution=(scene.render.resolution_x,scene.render.resolution_y)
    path=scene.render.filepath
    try:
        selected=['ceremonial_lamp','inscribed_stele','guardian_statue','relic_chest','sun_mask_secret','cup']
        for o in scene.objects:
            if o.type=='MESH':o.hide_render=True
        for index,name in enumerate(selected):
            coll=next(c for c in scene.collection.children if c.name.startswith('Reviewed_'+name))
            root=next(o for o in coll.objects if o.type=='EMPTY')
            dims=next(r['after']['dimensions_m'] for r in json.loads((OUT/'review_stats.json').read_text()) if r['name']==name)
            scale=.8/max(dims)
            root.scale=(scale,)*3
            root.location=((index%3-1)*1.15,0,(.65 if index<3 else -.55))
            for obj in coll.objects:obj.hide_render=False
        camera.location=(0,-8,2.2);camera.rotation_euler=(Vector((0,0,.45))-camera.location).to_track_quat('-Z','Y').to_euler()
        camera.data.ortho_scale=3.8
        scene.render.resolution_x=1600;scene.render.resolution_y=1100
        scene.render.filepath=str(OUT/'details.png')
        bpy.ops.render.render(write_still=True,scene=scene.name)
    finally:
        for o,mat in transforms.items():o.matrix_world=mat
        for o,hidden in visibility.items():o.hide_render=hidden
        camera.matrix_world=camera_matrix;camera.data.ortho_scale=ortho
        scene.render.resolution_x,scene.render.resolution_y=resolution
        scene.render.filepath=path

if __name__=='__main__':run()
