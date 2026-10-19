"""Original small finds and weathered editions; preserves the user's scene."""
import bpy, math, json, importlib.util
from pathlib import Path
from mathutils import Vector
HERE=Path(__file__).resolve().parent
OUT=HERE/'ContextReviewed'
NAMES=['mask','cup','statuette','scarab_amulet','ceremonial_lamp','amphora','inscribed_stele','guardian_statue','relic_chest','sun_mask_secret']
NEW=['hand_mirror','votive_bell','perfume_jar','stamp_seal','coin_casket']

def active(objects):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objects:o.select_set(True)
    bpy.context.view_layer.objects.active=objects[0]

def run():
    OUT.mkdir(exist_ok=True)
    old=bpy.context.window.scene;selected=list(bpy.context.selected_objects);act=bpy.context.view_layer.objects.active
    originals=set(bpy.data.objects);scene=bpy.data.scenes.new('TH_Context_Treasure_Work');bpy.context.window.scene=scene
    results=[];collections=[];materials={}
    try:
        for name in NAMES:
            coll=bpy.data.collections.new('Context_'+name);scene.collection.children.link(coll)
            with bpy.data.libraries.load(str(HERE/'Reviewed'/(name+'.blend')),link=False) as (src,dst):dst.objects=src.objects
            objects=[o for o in dst.objects if o and o.type in ('MESH','EMPTY')]
            for o in objects:
                coll.objects.link(o)
                if o.type!='MESH':continue
                for i,m in enumerate(o.data.materials):
                    # Imported datablocks only. Shared source files remain intact.
                    key=m.name.split('.')[0]
                    if key not in materials:
                        mat=m.copy();mat.name='Context_'+key
                        bs=next((n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED'),None)
                        if bs and bs.inputs['Emission Strength'].default_value<.1:
                            bs.inputs['Roughness'].default_value=max(.68,bs.inputs['Roughness'].default_value)
                            if 'Gilt' in key or 'Gold' in key:
                                bs.inputs['Base Color'].default_value=(.38,.235,.085,1)
                                bs.inputs['Metallic'].default_value=.58
                        materials[key]=mat
                    o.data.materials[i]=materials[key]
            collections.append((name,coll,objects))
        bronze=next(m for k,m in materials.items() if 'Bronze' in k)
        gold=next(m for k,m in materials.items() if 'Gilt' in k)
        glow=next(m for k,m in materials.items() if 'Emission' in k)
        dark=next(m for k,m in materials.items() if 'Dark' in k)
        ceramic=gold.copy();ceramic.name='Context_UnglazedOchre';bs=next(n for n in ceramic.node_tree.nodes if n.type=='BSDF_PRINCIPLED');bs.inputs['Base Color'].default_value=(.21,.105,.052,1);bs.inputs['Metallic'].default_value=0;bs.inputs['Roughness'].default_value=.88
        mirror=gold.copy();mirror.name='Context_TarnishedMirror';bs=next(n for n in mirror.node_tree.nodes if n.type=='BSDF_PRINCIPLED');bs.inputs['Base Color'].default_value=(.25,.30,.26,1);bs.inputs['Metallic'].default_value=.75;bs.inputs['Roughness'].default_value=.44
        for name in NEW:
            coll=bpy.data.collections.new('Context_'+name);scene.collection.children.link(coll)
            root=bpy.data.objects.new(name+'_Root',None);coll.objects.link(root);root['treasure_class']='S';root['grip_count']=1
            parts=[]
            def own(o,mat,label):
                for c in list(o.users_collection):c.objects.unlink(o)
                coll.objects.link(o);o.name=name+'_'+label;o.parent=root;o.data.materials.append(mat);parts.append(o)
                return o
            def cylinder(label,loc,r,depth,mat=bronze,rot=(0,0,0),verts=24):
                bpy.ops.mesh.primitive_cylinder_add(vertices=verts,radius=r,depth=depth,location=loc,rotation=rot)
                return own(bpy.context.object,mat,label)
            def torus(label,loc,r,t,mat=gold,rot=(0,0,0)):
                bpy.ops.mesh.primitive_torus_add(major_segments=24,minor_segments=6,major_radius=r,minor_radius=t,location=loc,rotation=rot)
                return own(bpy.context.object,mat,label)
            def box(label,loc,size,mat=bronze):
                bpy.ops.mesh.primitive_cube_add(size=1,location=loc);o=bpy.context.object;o.scale=size
                active([o]);bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
                mod=o.modifiers.new('Worn corners','BEVEL');mod.width=min(.006,min(size)*.18);mod.segments=1;bpy.ops.object.modifier_apply(modifier=mod.name)
                return own(o,mat,label)
            def lathe(label,profile,mat=bronze):
                count=24;verts=[(r*math.cos(i*math.tau/count),r*math.sin(i*math.tau/count),z) for r,z in profile for i in range(count)]
                faces=[(j*count+i,j*count+(i+1)%count,(j+1)*count+(i+1)%count,(j+1)*count+i) for j in range(len(profile)-1) for i in range(count)]
                mesh=bpy.data.meshes.new(label);mesh.from_pydata(verts,[],faces);mesh.update();o=bpy.data.objects.new(label,mesh);coll.objects.link(o)
                return own(o,mat,label)
            if name=='hand_mirror':
                cylinder('Back',(0,0,.22),.105,.025,bronze,(math.pi/2,0,0))
                cylinder('Reflector',(0,-.014,.22),.086,.004,mirror,(math.pi/2,0,0))
                torus('Rim',(0,-.017,.22),.096,.009,gold,(math.pi/2,0,0))
                box('Handle',(0,0,.075),(.035,.028,.15))
                for z in (.03,.06,.09):torus('Binding',(0,0,z),.021,.004)
                gp=(0,0,.36)
            elif name=='votive_bell':
                lathe('Bell',[(.096,0),(.105,.015),(.09,.04),(.07,.13),(.035,.19),(.012,.20),(.012,.188),(.029,.178),(.058,.125),(.079,.04),(.087,.015),(.096,0)])
                cylinder('Clapper',(0,0,.065),.016,.11,gold)
                torus('LowerBand',(0,0,.027),.095,.006)
                for i in range(8):
                    a=i*math.tau/8;cylinder('Rivet',(.071*math.cos(a),.071*math.sin(a),.115),.007,.022,gold)
                gp=(0,0,.233)
            elif name=='perfume_jar':
                lathe('Vessel',[(.014,0),(.055,0),(.085,.035),(.09,.09),(.064,.16),(.028,.19),(.027,.23),(.017,.23),(.018,.19),(.052,.15),(.075,.09),(.067,.04),(.014,.015),(.014,0)],ceramic)
                cylinder('Stopper',(0,0,.244),.033,.028,gold)
                for z,r in [(.05,.085),(.15,.068),(.218,.03)]:torus('Inlay',(0,0,z),r,.004)
                gp=(0,0,.292)
            elif name=='stamp_seal':
                box('SealDie',(0,0,.024),(.135,.12,.048),gold)
                lathe('CarvedGrip',[(.015,.045),(.055,.045),(.06,.07),(.025,.11),(.025,.16),(.05,.185),(.045,.21),(.015,.21),(.015,.045)])
                for x in (-.04,0,.04):box('DieMark',(x,-.061,.026),(.012,.004,.022),dark)
                gp=(0,0,.245)
            else:
                box('Base',(0,0,.018),(.24,.16,.036))
                for x in (-.112,.112):box('Side',(x,0,.073),(.016,.16,.11))
                for y in (-.072,.072):box('Side',(0,y,.073),(.21,.016,.11))
                lid=box('OpenLid',(0,.094,.17),(.24,.018,.15));lid.rotation_euler.x=-.25
                for x in (-.065,.065):box('Band',(x,-.082,.075),(.018,.009,.115),gold)
                for i in range(9):
                    x=((i%3)-1)*.059;y=((i//3)-1)*.037
                    # Visible stacks supported from the box floor, not hidden
                    # single coins below its rim or unsupported floating coins.
                    for layer in range(8+i%3):
                        z=.040+layer*.010
                        cylinder('Coin',(x,y,z),.027,.010,gold,verts=12)
                    cylinder('CoinStamp',(x,y,z+.006),.009,.003,bronze,verts=6)
                gp=(0,-.082,.158)
            body=list(parts)
            active(body);bpy.ops.object.join();body=bpy.context.object;body.name=name+'_Body'
            active([body]);bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.uv.smart_project(island_margin=.02);bpy.ops.object.mode_set(mode='OBJECT')
            grip=torus('Grip',gp,.03,.006,glow,(math.pi/2,0,0));grip['is_grip']=True
            collections.append((name,coll,[root,body,grip]))
        for name,coll,objects in collections:
            active(objects);bpy.context.view_layer.update()
            meshes=[o for o in objects if o.type=='MESH']
            for o in meshes:o.data.calc_loop_triangles()
            triangles=sum(len(o.data.loop_triangles) for o in meshes);assert triangles<=5000,(name,triangles)
            points=[o.matrix_world@v.co for o in meshes for v in o.data.vertices]
            dims=[max(p[i] for p in points)-min(p[i] for p in points) for i in range(3)]
            bpy.data.libraries.write(str(OUT/(name+'.blend')),{coll},fake_user=True,compress=True)
            bpy.ops.export_scene.fbx(filepath=str(OUT/(name+'.fbx')),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',use_custom_props=True,path_mode='COPY',embed_textures=True,bake_anim=False,add_leaf_bones=False)
            results.append(dict(model=name,triangles=triangles,dimensions_m=dims,meshes=len(meshes)))
        (OUT/'stats.json').write_text(json.dumps(results,indent=2),encoding='utf-8')
        return results
    finally:
        bpy.context.window.scene=old;active(selected) if selected else bpy.ops.object.select_all(action='DESELECT');bpy.context.view_layer.objects.active=act
        assert originals.issubset(set(bpy.data.objects))

if __name__=='__main__':run()
