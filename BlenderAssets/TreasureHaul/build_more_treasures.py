"""Build seven original Treasure Haul artifacts and export three pattern FBX variants.

Run with Blender 5.1.2 Python while the saved S showcase is open. This script is
local-only, creates no external downloads, and does not save over the open file.
"""
import bpy, math, json, shutil
from pathlib import Path
from mathutils import Vector
import numpy as np

OUT = Path(__file__).resolve().parent
PREFIX = 'TH10_'
assert bpy.context.mode == 'OBJECT'
scene = bpy.context.scene
old_main=bpy.data.collections.get(PREFIX+'Treasures')
if old_main:
    for obj in list(bpy.data.objects):
        if obj.name.startswith(PREFIX):bpy.data.objects.remove(obj,do_unlink=True)
    for child in list(old_main.children):bpy.data.collections.remove(child,do_unlink=True)
    bpy.data.collections.remove(old_main,do_unlink=True)
for datablocks in (bpy.data.materials,bpy.data.images):
    for datablock in list(datablocks):
        if datablock.name.startswith(PREFIX):datablocks.remove(datablock)
main = bpy.data.collections.new(PREFIX + 'Treasures')
scene.collection.children.link(main)

def make_texture(name, dark, light, accent, pattern):
    if name == 'Bronze_Patina':
        source = OUT/'S'/'textures'/'Bronze_BaseColor.png'
        target = OUT/'patterns'/(name+'.png')
        shutil.copyfile(source, target)
        image = bpy.data.images.load(str(target), check_existing=False)
        image.name = PREFIX+name
        image.pack()
        return image
    n=512; y,x=np.mgrid[0:n,0:n]/n*math.tau
    rng=np.random.default_rng(100+pattern)
    def value_noise(cells):
        grid=rng.random((cells,cells),dtype=np.float32)
        for _ in range(3):
            grid=(4*grid+np.roll(grid,1,0)+np.roll(grid,-1,0)+np.roll(grid,1,1)+np.roll(grid,-1,1)+np.roll(np.roll(grid,1,0),1,1)+np.roll(np.roll(grid,1,0),-1,1)+np.roll(np.roll(grid,-1,0),1,1)+np.roll(np.roll(grid,-1,0),-1,1))/12
        return np.repeat(np.repeat(grid,n//cells,axis=0),n//cells,axis=1)
    f=(.48*value_noise(4)+.27*value_noise(8)+.14*value_noise(16)+.08*value_noise(32)+.03*value_noise(64))
    f=(f-f.min())/max(1e-6,float(f.max()-f.min()))
    if pattern == 1:
        motif=np.cos(x*4+np.sin(y*2)*.4)*np.cos(y*4+np.sin(x*2)*.4)
        mask=np.clip((motif-.94)*4,0,.34)
        base=np.where((f>.58)[...,None],np.array(light),np.array(dark))
        base=base*(1-mask[...,None])+np.array(accent)*mask[...,None]
    elif pattern == 2:
        line=np.abs(np.sin((x*1.6+y*1.2)+f*.3))
        mask=np.clip((.035-line)*5,0,.32)
        base=np.where((f>.58)[...,None],np.array(light),np.array(dark))
        base=base*(1-mask[...,None])+np.array(accent)*mask[...,None]
    else:
        mask=np.clip((f-.5)*2.1,0,.58)
        base=np.array(dark)[None,None,:]*(1-mask[...,None])+np.array(light)[None,None,:]*mask[...,None]
        fleck=np.clip((value_noise(64)-.78)*1.8,0,.26)
        base=base*(1-fleck[...,None])+np.array(accent)[None,None,:]*fleck[...,None]
    base=np.clip(base,0,1).astype(np.float32)
    image=bpy.data.images.new(PREFIX+name, n,n,alpha=True)
    image.pixels.foreach_set(np.dstack((base,np.ones((n,n),dtype=np.float32))).ravel())
    image.filepath_raw=str(OUT/'patterns'/(name+'.png'))
    image.file_format='PNG'; image.save(); image.pack()
    return image

(OUT/'patterns').mkdir(exist_ok=True)
pattern_specs=[
 ('Bronze_Patina',(.17,.075,.025),(.28,.16,.07),(.035,.25,.20),0),
 ('Cinnabar_Gilt',(.10,.025,.018),(.37,.075,.035),(.68,.43,.13),1),
 ('Jade_Inlay',(.018,.09,.073),(.07,.26,.20),(.67,.44,.14),2),
]
images=[make_texture(*s) for s in pattern_specs]
skins=[]
for (spec,image) in zip(pattern_specs,images):
    name,dark,light,accent,kind=spec
    mat=bpy.data.materials.new(PREFIX+name);mat.use_nodes=True
    shader=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    shader.inputs['Metallic'].default_value=.72 if kind!=1 else .52
    shader.inputs['Roughness'].default_value=.5
    tex=mat.node_tree.nodes.new('ShaderNodeTexImage');tex.image=image
    mat.node_tree.links.new(tex.outputs['Color'],shader.inputs['Base Color'])
    mat['pattern_set']=name; mat.use_fake_user=True
    skins.append(mat)
gold=bpy.data.materials.get('TH_S_WornGilt') or bpy.data.materials.get('WornGilt')
glow=bpy.data.materials.get('TH_S_Grip_Emission') or bpy.data.materials.get('Grip_Emission')
darkmat=bpy.data.materials.get('TH_S_DarkRecess') or skins[0]
assert glow and gold, 'Base S model materials are missing.'

current=None; root=None
def begin(name):
    global current,root
    current=bpy.data.collections.new(PREFIX+name);main.children.link(current)
    root=bpy.data.objects.new(PREFIX+name+'_Root',None);current.objects.link(root)
    root['treasure_class']=name.split('_')[0]
    return current,root
def link(obj,name,mat=None):
    obj.name=PREFIX+name
    for c in list(obj.users_collection):c.objects.unlink(obj)
    current.objects.link(obj);obj.parent=root
    if mat:obj.data.materials.append(mat)
    return obj
def active(obj):
    bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
def apply(obj):
    active(obj)
    if obj.mode!='OBJECT':bpy.ops.object.mode_set(mode='OBJECT')
    bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
    for m in list(obj.modifiers):bpy.ops.object.modifier_apply(modifier=m.name)
    for p in obj.data.polygons:p.use_smooth=True
    return obj
def sphere(name,loc,scale,mat=None,seg=16,rings=10):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg,ring_count=rings,location=loc)
    o=link(bpy.context.object,name,mat);o.scale=scale;return apply(o)
def torus(name,loc,major,minor,mat=None,rot=(0,0,0),segments=24):
    bpy.ops.mesh.primitive_torus_add(major_segments=segments,minor_segments=8,major_radius=major,minor_radius=minor,location=loc,rotation=rot)
    return apply(link(bpy.context.object,name,mat))
def cylinder(name,loc,radius,depth,mat=None,verts=20,rot=(0,0,0),bevel=.001):
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts,radius=radius,depth=depth,location=loc,rotation=rot)
    o=link(bpy.context.object,name,mat)
    if bevel:
        m=o.modifiers.new('Soft cast edges','BEVEL');m.width=bevel;m.segments=2
        o.modifiers.new('Cast normals','WEIGHTED_NORMAL')
    return apply(o)
def box(name,loc,scale,mat=None,bevel=.008):
    bpy.ops.mesh.primitive_cube_add(size=1,location=loc)
    o=link(bpy.context.object,name,mat);o.scale=scale
    if bevel:
        m=o.modifiers.new('Worn edges','BEVEL');m.width=bevel;m.segments=2
        o.modifiers.new('Cast normals','WEIGHTED_NORMAL')
    return apply(o)
def mesh(name,verts,faces,mat=None):
    d=bpy.data.meshes.new(PREFIX+name);d.from_pydata(verts,[],faces);d.update()
    o=bpy.data.objects.new(PREFIX+name,d);current.objects.link(o);o.parent=root
    if mat:d.materials.append(mat)
    for p in d.polygons:p.use_smooth=True
    return o
def lathe(name,profile,mat=None,segments=36):
    vs=[(r*math.cos(i*math.tau/segments),r*math.sin(i*math.tau/segments),z) for r,z in profile for i in range(segments)]
    fs=[]
    for j in range(len(profile)-1):
        for i in range(segments):
            a=j*segments+i;b=j*segments+(i+1)%segments
            fs.append((a,b,b+segments,a+segments))
    return mesh(name,vs,fs,mat)
def grip(name,loc,major=.035,axis='Y'):
    rot=(math.pi/2,0,0) if axis=='Y' else (0,math.pi/2,0)
    return torus(name,loc,major,.006,glow,rot,32)
def finish_uv():
    for o in current.objects:
        if o.type!='MESH':continue
        if len(o.data.materials)==0:o.data.materials.append(skins[0])
        active(o);bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.uv.smart_project(angle_limit=1.15,island_margin=.02);bpy.ops.object.mode_set(mode='OBJECT')
        o.data.calc_loop_triangles()

assets=[]
# 4: S scarab amulet, cast wing cases and a single top grip.
begin('S_Scarab_Amulet')
sphere('Scarab_Core',(0,0,.075),(.067,.038,.081),None,20,12)
sphere('Scarab_Head',(0,-.006,.143),(.034,.032,.026),gold,16,10)
for side in (-1,1):
    sphere('Scarab_Elytron',(side*.029,-.002,.09),(.038,.039,.065),None,18,10)
    for i in range(3):
        cylinder('Scarab_Leg',(side*(.061+i*.003),0,.045+i*.012),.004,.045,None,8,(0,math.pi/2,0),0)
    mesh('Scarab_Antenna',[(side*.014,-.012,.158),(side*.032,-.012,.184),(side*.038,-.012,.18)],[(0,1,2)],gold)
torus('Scarab_Scar',(0,-.04,.084),.014,.002,gold,(math.pi/2,0,0),24)
grip('Scarab_Handle',(0,.035,.17),.026)
assets.append((current,root,'scarab_amulet','S',1))

# 5: S ceremonial oil lamp, hollow reservoir, raised spout and handle.
begin('S_Ceremonial_Lamp')
lathe('Lamp_Reservoir',[(0,0),(.058,0),(.064,.009),(.058,.022),(.045,.04),(.024,.048),(.016,.053),(0,.053)],None,40)
torus('Lamp_Foot',(0,0,.008),.056,.003,gold,segments=36)
lathe('Lamp_Spout',[(.012,.038),(.022,.044),(.052,.055),(.082,.062),(.09,.069),(.088,.074)],None,20)
torus('Lamp_SpoutRim',(.088,0,.072),.007,.002,gold,segments=20)
torus('Lamp_Handle',(-.047,0,.066),.034,.006,glow,(math.pi/2,0,0),32)
for x in (-.03,.025):sphere('Lamp_HandleMount',(x,0,.047),(.009,.011,.008),gold,12,8)
for i in range(8):
    a=math.tau*i/8
    sphere('Lamp_Rivet',(.054*math.cos(a),.054*math.sin(a),.018),(.003,.003,.003),gold,8,6)
assets.append((current,root,'ceremonial_lamp','S',1))

# 6: M storage amphora, paired handles on left and right.
begin('M_Amphora')
lathe('Amphora_Body',[(0,0),(.07,0),(.085,.016),(.095,.07),(.083,.15),(.06,.23),(.036,.29),(.03,.34),(.046,.36),(.05,.372),(.044,.38),(.03,.37),(0,.365)],None,32)
torus('Amphora_Foot',(0,0,.012),.073,.005,gold,segments=24)
torus('Amphora_Neck',(0,0,.352),.041,.004,gold,segments=24)
for side in (-1,1):
    torus('Amphora_Handle',(side*.09,0,.25),.047,.008,glow,(math.pi/2,0,0),24)
    sphere('Amphora_HandleMount',(side*.069,0,.292),(.012,.014,.016),gold,12,8)
    sphere('Amphora_HandleMount',(side*.069,0,.208),(.012,.014,.016),gold,12,8)
for z,r in ((.09,.09),(.13,.085),(.17,.073)):
    torus('Amphora_Band',(0,0,z),r,.0025,gold,segments=24)
assets.append((current,root,'amphora','M',2))

# 7: M inscribed stele tablet with paired side grips.
begin('M_Inscribed_Stele')
box('Stele_Body',(0,0,.22),(.34,.075,.43),None,.018)
mesh('Stele_Crown',[(-.17,-.02,.42),(-.12,-.02,.49),(0,-.02,.53),(.12,-.02,.49),(.17,-.02,.42)],[(0,1,2),(2,3,4)],gold)
for z in (.1,.16,.22,.28,.34):
    # shallow incised glyph bars, varied to avoid readable writing or copied scripts.
    cylinder('Stele_Glyph',(0,-.039,z),.004,.20+(z%0.03),gold,6,(math.pi/2,0,0),0)
for side in (-1,1):
    grip('Stele_Handle',(side*.21,0,.22),.028,'Y')
    cylinder('Stele_HandleSocket',(side*.18,0,.22),.018,.025,gold,16,(0,math.pi/2,0),.002)
assets.append((current,root,'inscribed_stele','M',2))

# 8: L guardian statue, original seated lion-like idol, two handles at its sides.
begin('L_Guardian_Statue')
box('Guardian_Plinth',(0,0,.035),(.46,.34,.07),gold,.012)
sphere('Guardian_Torso',(0,0,.34),(.16,.12,.25),None,20,14)
sphere('Guardian_Head',(0,-.005,.62),(.15,.125,.13),None,16,10)
for side in (-1,1):
    sphere('Guardian_Cheek',(side*.09,-.094,.59),(.062,.048,.062),gold,12,8)
    sphere('Guardian_Eye',(side*.054,-.122,.64),(.018,.012,.018),darkmat,12,8)
    mesh('Guardian_Ear',[(side*.075,0,.70),(side*.14,0,.72),(side*.13,.015,.62)],[(0,1,2)],None)
    sphere('Guardian_Haunch',(side*.14,.012,.18),(.08,.11,.13),None,12,8)
    sphere('Guardian_Foreleg',(side*.115,-.09,.12),(.055,.09,.1),None,12,8)
    torus('Guardian_Handle',(side*.28,0,.34),.04,.007,glow,(math.pi/2,0,0),24)
    for z in (.22,.44):sphere('Guardian_HandleSocket',(side*.22,0,z),(.018,.02,.02),gold,12,8)
    for i in range(3):
        sphere('Guardian_Mane',(side*(.12+i*.018),-.075,.48-i*.045),(.025,.03,.025),gold,10,6)
# curled tail
torus('Guardian_Tail',(.12,.09,.40),.075,.012,None,(math.pi/2,0,0),24)
assets.append((current,root,'guardian_statue','L',2))

# 9: L sealed relic chest, dark bronze body, cast bands and paired end grips.
begin('L_Relic_Chest')
box('Chest_Body',(0,0,.19),(.58,.34,.32),None,.035)
box('Chest_Lid',(0,0,.38),(.59,.35,.12),None,.05)
for side in (-1,1):
    for y in (-.13,.13):
        box('Chest_Band',(side*.18,y,.22),(.035,.018,.35),gold,.008)
    torus('Chest_Handle',(side*.35,0,.23),.045,.008,glow,(math.pi/2,0,0),24)
    cylinder('Chest_HandleSocket',(side*.29,0,.23),.018,.035,gold,16,(0,math.pi/2,0),.002)
box('Chest_LockPlate',(0,-.182,.24),(.09,.018,.12),gold,.008)
sphere('Chest_LockBoss',(0,-.197,.24),(.018,.012,.018),gold,12,8)
for x in (-.23,-.12,.12,.23):
    sphere('Chest_Rivet',(x,-.18,.21),(.008,.006,.008),gold,10,6)
assets.append((current,root,'relic_chest','L',2))

# 10: distinct high-value golden sun mask, single glowing ring handle.
begin('V_Sun_Mask')
rows,cols=20,28;verts=[];faces=[]
for j in range(rows+1):
    phi=.08+(math.pi-.16)*j/rows
    z=.16+.13*math.cos(phi)
    for i in range(cols+1):
        theta=-math.pi/2+math.pi*i/cols
        x=.086*math.sin(phi)*math.sin(theta)
        y=-.028*math.sin(phi)*math.cos(theta)
        y-=.03*math.exp(-(x/.014)**2-((z-.16)/.043)**2)
        verts.append((x,y,z))
for j in range(rows):
    for i in range(cols):
        a=j*(cols+1)+i;faces.append((a,a+1,a+cols+2,a+cols+1))
sun=mesh('SunMask_Face',verts,faces,gold)
m=sun.modifiers.new('Cast wall','SOLIDIFY');m.thickness=.004;apply(sun)
for side in (-1,1):
    sphere('SunMask_Eye',(side*.036,-.027,.177),(.019,.008,.011),darkmat,12,8)
    torus('SunMask_EyeRim',(side*.036,-.031,.177),.022,.003,gold,(math.pi/2,0,0),24)
    for i in range(4):
        a=(i/3-.5)*1.25
        # small raised rays around upper cheek, a distinct sun-crown silhouette
        start=(side*(.055+i*.007),-.01,.21+i*.012)
        end=(side*(.075+i*.009),-.01,.225+i*.02)
        mesh('SunMask_Ray',[start,(end[0]-.006,end[1],end[2]),end],[(0,1,2)],gold)
torus('SunMask_Brow',(0,-.031,.27),.05,.003,gold,(math.pi/2,0,0),32)
grip('SunMask_Handle',(0,.025,.32),.03)
assets.append((current,root,'sun_mask_secret','V',1))

def assign_skin(collection, skin):
    old={o.name:[m for m in o.data.materials] for o in collection.objects if o.type=='MESH'}
    for o in collection.objects:
        if o.type!='MESH':continue
        o.data.materials.clear()
        is_handle=('Handle' in o.name and 'Mount' not in o.name and 'Socket' not in o.name)
        o.data.materials.append(glow if is_handle else skin)
    return old
def restore(collection,old):
    for o in collection.objects:
        if o.name in old:
            o.data.materials.clear()
            for m in old[o.name]:o.data.materials.append(m)

stats={'source':'original Blender geometry and procedural texture patterns; no external assets','assets':[]}
for collection,asset_root,name,kind,grips in assets:
    current=collection;root=asset_root;finish_uv()
    for o in collection.objects:
        if o.type=='MESH':o.data.calc_loop_triangles()
    tri=sum(len(o.data.loop_triangles) for o in collection.objects if o.type=='MESH')
    assert tri<=5000,(name,tri)
    mesh_objects=[o for o in collection.objects if o.type=='MESH']
    corners=[o.matrix_world@Vector(c) for o in mesh_objects for c in o.bound_box]
    dims=[max(v[i] for v in corners)-min(v[i] for v in corners) for i in range(3)]
    actual_grips=sum(1 for o in mesh_objects if 'Handle' in o.name and 'Mount' not in o.name and 'Socket' not in o.name)
    assert actual_grips==grips,(name,actual_grips,grips)
    asset_root['triangle_count']=tri;asset_root['handle_count']=grips
    for skin in skins:
        skin.use_fake_user=True
    preview_scene=bpy.data.scenes.new(PREFIX+name+'_Scene')
    preview_scene.collection.children.link(collection);preview_scene.unit_settings.system='METRIC';preview_scene.unit_settings.scale_length=1
    blend=OUT/(name+'.blend')
    bpy.data.libraries.write(str(blend),{preview_scene,collection,*skins,*images},fake_user=True,compress=True)
    bpy.data.scenes.remove(preview_scene)
    variants=[]
    for si,skin in enumerate(skins):
        old=assign_skin(collection,skin)
        bpy.ops.object.select_all(action='DESELECT')
        for o in collection.objects:o.select_set(True)
        bpy.context.view_layer.objects.active=asset_root
        suffix='' if si==0 else ('_pattern_cinnabar' if si==1 else '_pattern_jade')
        path=OUT/(name+suffix+'.fbx')
        bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,object_types={'EMPTY','MESH'},apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',axis_forward='-Z',axis_up='Y',use_mesh_modifiers=True,mesh_smooth_type='FACE',use_custom_props=True,path_mode='COPY',embed_textures=True,add_leaf_bones=False,bake_anim=False)
        restore(collection,old);variants.append(path.name)
    stats['assets'].append({'name':name,'class':kind,'triangles':tri,'dimensions_m':[round(v,4) for v in dims],'handles':grips,'blend':blend.name,'fbx_variants':variants,'pattern_sets':[m['pattern_set'] for m in skins]})

# Add color/pattern FBX variants for the original three S artifacts already in the live scene.
for old_name,kind,grips in [('Mask','S',1),('Cup','S',1),('Statuette','S',1)]:
    asset_root=bpy.data.objects.get('TH_S_'+old_name+'_Root')
    assert asset_root
    collection=bpy.data.collections.get('TH_S_'+old_name)
    assert collection
    tri=sum(len(o.data.loop_triangles) for o in collection.objects if o.type=='MESH')
    variants=[]
    old_location=asset_root.location.copy();asset_root.location=(0,0,0)
    for si,skin in enumerate(skins[1:],start=1):
        old=assign_skin(collection,skin)
        bpy.ops.object.select_all(action='DESELECT')
        for o in collection.objects:o.select_set(True)
        bpy.context.view_layer.objects.active=asset_root
        path=OUT/(old_name.lower()+('_pattern_cinnabar' if si==1 else '_pattern_jade')+'.fbx')
        bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,object_types={'EMPTY','MESH'},apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',axis_forward='-Z',axis_up='Y',use_mesh_modifiers=True,mesh_smooth_type='FACE',use_custom_props=True,path_mode='COPY',embed_textures=True,add_leaf_bones=False,bake_anim=False)
        restore(collection,old);variants.append(path.name)
    asset_root.location=old_location
    stats['assets'].append({'name':old_name.lower(),'class':kind,'triangles':tri,'handles':grips,'existing_blend':'S/'+old_name.lower()+'.blend','existing_fbx_variants':variants,'pattern_sets':[m['pattern_set'] for m in skins]})

with (OUT/'ten_type_stats.json').open('w',encoding='utf-8') as f:json.dump(stats,f,indent=2,ensure_ascii=False)
# Restore prior selection and leave the user's current scene active without saving over it.
bpy.ops.object.select_all(action='DESELECT')
print(json.dumps(stats,indent=2,ensure_ascii=False))
