"""Original geometry/textures for Treasure Haul S. Run in Blender's Python console.

Adds only a new named collection; never saves/replaces the user's working file.
Outputs collection libraries and selected-object FBXs beside this script.
"""
import bpy
import math
import json
from pathlib import Path
from mathutils import Vector
import numpy as np

OUT = Path(__file__).resolve().parent
PREFIX = 'TH_S_'
assert bpy.context.mode == 'OBJECT', 'Switch to Object mode before running.'
assert abs(bpy.context.scene.unit_settings.scale_length-1.0)<1e-9, 'Run in a metre-scale scene (scale_length=1).'
assert not bpy.data.collections.get(PREFIX + 'Treasures'), 'Already built: use another clean session.'
original_objects = set(bpy.data.objects)
original_selection = list(bpy.context.selected_objects)
original_active = bpy.context.view_layer.objects.active
OUT.mkdir(parents=True, exist_ok=True)
TEXTURES = OUT / 'textures'
TEXTURES.mkdir(exist_ok=True)
main = bpy.data.collections.new(PREFIX + 'Treasures')
bpy.context.scene.collection.children.link(main)

def texture(name, values, color=True):
    h, w = values.shape[:2]
    image = bpy.data.images.new(PREFIX + name, w, h, alpha=True)
    if not color:
        image.colorspace_settings.name = 'Non-Color'
    rgba = np.ones((h, w, 4), dtype=np.float32)
    rgba[:, :, :3] = values[:, :, None] if values.ndim == 2 else values
    image.pixels.foreach_set(rgba.ravel())
    image.filepath_raw = str(TEXTURES / (name + '.png'))
    image.file_format = 'PNG'
    image.save()
    image.pack()
    return image

# Deterministic, seamless original mottled corrosion; no downloaded source data.
n = 512
rng = np.random.default_rng(46)
noise = np.zeros((n, n))
fy,fx=np.meshgrid(np.fft.fftfreq(n),np.fft.fftfreq(n),indexing='ij')
for radius,strength in [(28,1),(12,.55),(4,.25),(1,.10)]:
    field=np.fft.ifft2(np.fft.fft2(rng.standard_normal((n,n)))*np.exp(-2*math.pi**2*radius**2*(fx*fx+fy*fy))).real
    noise+=field/(field.std()+1e-9)*strength
patina = np.clip((noise-.3)*.65, 0, .85)
grain = rng.random((n,n)) * .035
bronze = np.array([.22, .125, .055])
green = np.array([.055, .115, .085])
base = bronze[None,None,:]*(1-patina[:,:,None]) + green[None,None,:]*patina[:,:,None]
base *= (.75 + grain[:,:,None]*4)
images = {
    'BaseColor': texture('Bronze_BaseColor', base),
    'Roughness': texture('Bronze_Roughness', np.clip(.48+patina*.22+grain,0,1),False),
    'Metallic': texture('Bronze_Metallic', .88-patina*.55,False),
}

def material(name, color, metallic=0, roughness=.4):
    mat = bpy.data.materials.new(PREFIX+name)
    mat.diffuse_color = (*color,1)
    mat.use_nodes = True
    shader = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    shader.inputs['Base Color'].default_value = (*color,1)
    shader.inputs['Metallic'].default_value = metallic
    shader.inputs['Roughness'].default_value = roughness
    return mat, shader

aged, shader = material('AgedBronze', (.24,.13,.05),.8,.5)
for key, image in images.items():
    tex = aged.node_tree.nodes.new('ShaderNodeTexImage')
    tex.image = image
    aged.node_tree.links.new(tex.outputs['Color'], shader.inputs['Base Color' if key == 'BaseColor' else key])
gold, _ = material('WornGilt',(.44,.27,.09),.82,.43)
dark, _ = material('DarkRecess',(.028,.024,.017),.6,.5)
glow, shader = material('Grip_Emission',(.05,.72,.57),.25,.25)
shader.inputs['Emission Color'].default_value = (.04,1,.65,1)
shader.inputs['Emission Strength'].default_value = 2.5

def begin(name):
    global coll, root
    coll=bpy.data.collections.new(PREFIX+name)
    main.children.link(coll)
    root=bpy.data.objects.new(PREFIX+name+'_Root',None)
    coll.objects.link(root)
    root['asset_class']='Treasure S'
    return coll,root

def link(obj,name,mat):
    obj.name=PREFIX+name
    for c in list(obj.users_collection): c.objects.unlink(obj)
    coll.objects.link(obj)
    obj.parent=root
    if mat: obj.data.materials.append(mat)
    return obj

def active(obj):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active=obj

def apply(obj):
    active(obj)
    bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
    for mod in list(obj.modifiers): bpy.ops.object.modifier_apply(modifier=mod.name)
    for p in obj.data.polygons: p.use_smooth=True
    return obj

def mesh(name,verts,faces,mat=aged):
    data=bpy.data.meshes.new(PREFIX+name)
    data.from_pydata(verts,[],faces)
    data.update()
    obj=bpy.data.objects.new(PREFIX+name,data)
    coll.objects.link(obj)
    obj.parent=root
    data.materials.append(mat)
    for p in data.polygons: p.use_smooth=True
    return obj

def sphere(name,loc,scale,mat=aged,segments=20,rings=12):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments,ring_count=rings,location=loc)
    obj=link(bpy.context.object,name,mat)
    obj.scale=scale
    return apply(obj)

def torus(name,loc,major,minor,mat=gold,rotation=(0,0,0),segments=40):
    bpy.ops.mesh.primitive_torus_add(major_segments=segments,minor_segments=8,location=loc,major_radius=major,minor_radius=minor,rotation=rotation)
    return apply(link(bpy.context.object,name,mat))

def lathe(name,profile,mat=aged,segments=48):
    vs=[(r*math.cos(i*math.tau/segments),r*math.sin(i*math.tau/segments),z) for r,z in profile for i in range(segments)]
    fs=[]
    for j in range(len(profile)-1):
        for i in range(segments):
            a=j*segments+i;b=j*segments+(i+1)%segments
            fs.append((a,b,b+segments,a+segments))
    return mesh(name,vs,fs,mat)

assets=[]
coll,root=begin('Mask')
rows,cols=24,24
verts=[]
for j in range(rows+1):
    phi=.06+(math.pi-.12)*j/rows
    z=.145*math.cos(phi)+.15
    for i in range(cols+1):
        theta=-math.pi/2+math.pi*i/cols
        xx=.095*math.sin(phi)*math.sin(theta)
        yy=-.025*math.sin(phi)*math.cos(theta)
        nose=.038*math.exp(-(xx/.016)**2-((z-.155)/.045)**2)
        brow=.009*math.exp(-((z-.205)/.012)**2)*math.exp(-(xx/.08)**4)
        lip=.008*math.exp(-(xx/.032)**4-((z-.09)/.009)**2)
        yy-=nose+brow+lip
        verts.append((xx,yy,z))
faces=[]
for j in range(rows):
    for i in range(cols):
        a=j*(cols+1)+i
        faces.append((a,a+1,a+cols+2,a+cols+1))
face=mesh('Mask_Face',verts,faces)
mod=face.modifiers.new('Cast wall','SOLIDIFY');mod.thickness=.0035
apply(face)
for side in [-1,1]:
    bpy.ops.mesh.primitive_cylinder_add(vertices=32,radius=1,depth=.16,location=(side*.041,-.025,.191),rotation=(math.pi/2,0,0))
    cutter=bpy.context.object;cutter.scale=(.023,.012,1)
    apply(cutter)
    mod=face.modifiers.new('Eye aperture','BOOLEAN');mod.operation='DIFFERENCE';mod.object=cutter
    active(face);bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.data.objects.remove(cutter,do_unlink=True)
for side in [-1,1]:
    for z in [.115,.15,.23]:
        xx=side*.078*math.sqrt(max(.1,1-((z-.15)/.145)**2))
        sphere('Mask_Rivet',(xx,-.02,z),(.003,.002,.003),gold,12,6)
sphere('Mask_BrowSeal',(0,-.024,.259),(.012,.004,.014),gold,16,8)
torus('Mask_Handle',(0,.025,.306),.035,.006,glow,(math.pi/2,0,0),40)
for side in [-1,1]: sphere('Mask_HandleMount',(side*.018,.016,.279),(.009,.01,.009),gold,12,6)
assets.append((coll,root))

coll,root=begin('Cup')
# Continuous cross-section: underside -> outer bowl -> rolled lip -> inner bowl floor.
profile=[(0,0),(.046,0),(.05,.006),(.05,.012),(.042,.017),(.019,.025),(.013,.041),(.013,.07),(.022,.082),(.032,.088),(.047,.109),(.06,.142),(.068,.18),(.069,.203),(.066,.209),(.063,.203),(.062,.18),(.055,.145),(.042,.113),(.027,.095),(0,.095)]
lathe('Cup_HollowBody',profile,segments=48)
torus('Cup_Rim',(0,0,.204),.066,.003,gold,segments=48)
torus('Cup_FootBand',(0,0,.011),.048,.0025,gold,segments=40)
torus('Cup_StemBand',(0,0,.06),.014,.003,gold,segments=32)
torus('Cup_Handle',(.082,0,.155),.033,.006,glow,(math.pi/2,0,0),40)
for z in [.126,.184]: sphere('Cup_HandleMount',(.058,0,z),(.012,.012,.009),gold,12,6)
# Chased vertical ribs on the lower bowl, built as low-poly shallow strips.
for i in range(12):
    a=math.tau*i/12
    vs=[]
    for r,z in [(.033,.09),(.048,.111),(.061,.145),(.0685,.18)]:
        for da in [-.025,.025]: vs.append((r*math.cos(a+da),r*math.sin(a+da),z))
    mesh('Cup_ChasedFlute',vs,[(0,1,3,2),(2,3,5,4),(4,5,7,6)],gold)
assets.append((coll,root))

coll,root=begin('Statuette')
lathe('Statuette_Plinth',[(0,0),(.055,0),(.06,.006),(.06,.018),(.052,.026),(0,.026)],gold,32)
sphere('Statuette_OwlBody',(0,0,.107),(.047,.033,.077),aged,24,16)
sphere('Statuette_OwlHead',(0,-.002,.191),(.052,.035,.044),aged,24,12)
for side in [-1,1]:
    sphere('Statuette_Wing',(side*.037,.002,.11),(.018,.031,.064),aged,16,10)
    sphere('Statuette_FacialDisk',(side*.024,-.031,.195),(.023,.006,.025),gold,16,10)
    sphere('Statuette_Eye',(side*.024,-.037,.197),(.010,.004,.010),dark,16,8)
    sphere('Statuette_EyeGlint',(side*.022,-.040,.2),(.002,.0015,.002),gold,8,4)
    # Pointed cast ear tufts and talons.
    mesh('Statuette_Ear',[(side*.023,0,.217),(side*.047,-.01,.218),(side*.045,.008,.249),(side*.035,.019,.218)],[(0,1,2),(1,3,2),(3,0,2),(0,3,1)],aged)
    for dx in [-.007,0,.007]: sphere('Statuette_Talon',(side*.025+dx,-.015,.03),(.004,.014,.005),gold,10,6)
    # Individually raised feather ridges in rows on visible wings.
    for k in range(6):
        z=.066+k*.014
        xx=side*(.043+.008*math.sin(k/5*math.pi))
        mesh('Statuette_Feather',[(xx-side*.004,-.022,z+.018),(xx+side*.004,-.022,z+.018),(xx,-.029,z),(xx,-.030,z+.015)],[(0,1,3),(1,2,3),(2,0,3)],gold)
mesh('Statuette_Beak',[(-.008,-.037,.185),(.008,-.037,.185),(0,-.054,.171),(0,-.033,.165)],[(0,1,2),(1,3,2),(3,0,2),(0,3,1)],gold)
torus('Statuette_Handle',(0,.025,.258),.029,.0055,glow,(math.pi/2,0,0),32)
for side in [-1,1]: sphere('Statuette_HandleMount',(side*.014,.019,.234),(.008,.009,.007),gold,12,6)
assets.append((coll,root))

stats={'blender_version':bpy.app.version_string,'units':'metres','source':'original procedural geometry and texture pixels; no external assets','assets':[]}
for index,(collection,root) in enumerate(assets):
    objects=[o for o in collection.objects if o.type=='MESH']
    # UVs for every exported mesh; image textures are standard FBX-recognizable inputs.
    for obj in objects:
        for slot_index in reversed(range(len(obj.data.materials))):
            if obj.data.materials[slot_index] is None:
                obj.data.materials.pop(index=slot_index)
        active(obj)
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.uv.smart_project(angle_limit=1.15192,island_margin=.025)
        bpy.ops.object.mode_set(mode='OBJECT')
        if obj.name.startswith(PREFIX+'Mask_Face'):
            for loop in obj.data.loops:
                co=obj.data.vertices[loop.vertex_index].co
                obj.data.uv_layers.active.data[loop.index].uv=((co.x+.10)/.20,co.z/.30)
        elif any(word in obj.name for word in ['HollowBody','OwlBody','OwlHead','Wing']):
            z_values=[v.co.z for v in obj.data.vertices]
            z_min,z_max=min(z_values),max(z_values)
            for polygon in obj.data.polygons:
                coords=[]
                for loop_index in polygon.loop_indices:
                    co=obj.data.vertices[obj.data.loops[loop_index].vertex_index].co
                    coords.append((loop_index,math.atan2(co.y,co.x)/math.tau+.5,(co.z-z_min)/(z_max-z_min)))
                seam=max(c[1] for c in coords)-min(c[1] for c in coords)>.5
                for loop_index,u,v in coords:
                    obj.data.uv_layers.active.data[loop_index].uv=(u+1 if seam and u<.5 else u,v)
        obj.data.calc_loop_triangles()
    count=sum(len(o.data.loop_triangles) for o in objects)
    # A deterministic safety decimation only where needed; leaves the grip intact.
    if count>4900:
        biggest=sorted(objects,key=lambda o:len(o.data.loop_triangles),reverse=True)
        for obj in biggest:
            if 'Handle' in obj.name: continue
            old=len(obj.data.loop_triangles)
            ratio=max(.55,1-(count-4800)/old)
            mod=obj.modifiers.new('Budget reduction','DECIMATE');mod.ratio=ratio
            apply(obj);obj.data.calc_loop_triangles()
            count+=len(obj.data.loop_triangles)-old
            if count<=4900: break
    assert count<=5000,(collection.name,count)
    corners=[o.matrix_world@Vector(c) for o in objects for c in o.bound_box]
    lo=[min(v[i] for v in corners) for i in range(3)]
    hi=[max(v[i] for v in corners) for i in range(3)]
    name=collection.name.replace(PREFIX,'').lower()
    root['triangle_count']=count
    root['grip_object']=next(o.name for o in objects if o.data.materials and o.data.materials[0]==glow)
    saved_scene=bpy.data.scenes.new(PREFIX+name+'_AssetScene')
    saved_scene.collection.children.link(collection)
    saved_scene.unit_settings.system='METRIC'
    saved_scene.unit_settings.scale_length=1.0
    bpy.data.libraries.write(str(OUT/(name+'.blend')),{saved_scene},fake_user=True,compress=True)
    # The saved scene retains the origin placement; remove only our temporary datablock.
    bpy.data.scenes.remove(saved_scene)
    bpy.ops.object.select_all(action='DESELECT')
    for o in collection.objects:o.select_set(True)
    bpy.context.view_layer.objects.active=root
    bpy.ops.export_scene.fbx(filepath=str(OUT/(name+'.fbx')),use_selection=True,object_types={'EMPTY','MESH'},apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',axis_forward='-Z',axis_up='Y',use_mesh_modifiers=True,mesh_smooth_type='FACE',use_custom_props=True,path_mode='COPY',embed_textures=True,add_leaf_bones=False,bake_anim=False)
    stats['assets'].append({'name':name,'triangles':count,'mesh_objects':len(objects),'dimensions_m':[round(hi[i]-lo[i],4) for i in range(3)],'grip':root['grip_object'],'materials':sorted({m.name for o in objects for m in o.data.materials}),'uv_layers':all(len(o.data.uv_layers)>0 for o in objects)})
    root.location=(3+index*.36,0,0)

with (OUT/'stats.json').open('w',encoding='utf-8') as f:json.dump(stats,f,indent=2,ensure_ascii=False)
bpy.ops.object.select_all(action='DESELECT')
for o in original_selection:o.select_set(True)
bpy.context.view_layer.objects.active=original_active
assert original_objects.issubset(set(bpy.data.objects))
print(json.dumps(stats,indent=2))
