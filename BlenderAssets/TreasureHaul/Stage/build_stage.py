"""Original 5 x 11 m MR excavation depot. Execute through Blender MCP.

No downloads, generators or edits to the user's scene. Unity assets are not touched.
Logical coordinates are (width X, depth D, height H), Blender (X, -D, H).
"""
import bpy, bmesh, json, math, random
import numpy as np
from pathlib import Path
from mathutils import Vector

OUT=Path(__file__).resolve().parent
TREASURES=OUT.parent/'Reviewed'
RNG=random.Random(4618)

def activate(objects):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects:obj.select_set(True)
    bpy.context.view_layer.objects.active=objects[0]

def bounds(objects):
    points=[o.matrix_world@Vector(c) for o in objects if o.type=='MESH' for c in o.bound_box]
    return [[min(p[i] for p in points),max(p[i] for p in points)] for i in range(3)]

def stats(objects):
    meshes=[o for o in objects if o.type=='MESH']
    for o in meshes:o.data.calc_loop_triangles()
    return dict(meshes=len(meshes),triangles=sum(len(o.data.loop_triangles) for o in meshes),
                vertices=sum(len(o.data.vertices) for o in meshes),
                material_slots=sum(len(o.data.materials) for o in meshes))

class Builder:
    def __init__(self):
        OUT.mkdir(exist_ok=True);(OUT/'textures').mkdir(exist_ok=True)
        self.scene=bpy.data.scenes.new('TH_Stage_ExcavationDepot_5x11')
        bpy.context.window.scene=self.scene
        self.scene.unit_settings.system='METRIC';self.scene.unit_settings.scale_length=1
        self.runtime=bpy.data.collections.new('TH_STAGE_Runtime');self.scene.collection.children.link(self.runtime)
        self.preview=bpy.data.collections.new('TH_STAGE_PreviewOnly');self.scene.collection.children.link(self.preview)
        self.root=bpy.data.objects.new('VenueRoot',None);self.runtime.objects.link(self.root)
        self.root['venue_width_m']=5.;self.root['venue_length_m']=11.
        self.root['logical_axes']='X across, D away from lift, H up; Blender=(X,-D,H)'
        self.modules=[];self.colliders=[];self.markers=[];self.parts=[];self.preview_treasures=[];self.sample_placements=[]
        self.make_materials()

    def make_materials(self):
        # Eight original 128 px material swatches in a shared 1024 x 128 atlas.
        colors=[(.16,.205,.20),(.34,.37,.32),(.10,.16,.15),(.055,.067,.07),
                (.28,.13,.067),(.47,.43,.30),(.54,.34,.09),(.09,.10,.085)]
        metallic=[0,0,.65,.75,.28,.35,.2,0]
        roughness=[.9,.85,.65,.55,.88,.66,.72,.96]
        n=128; rng=np.random.default_rng(4618)
        base=np.zeros((n,n*8,4),dtype=np.float32);metal=base.copy();rough=base.copy()
        y,x=np.mgrid[0:n,0:n]
        for tile,color in enumerate(colors):
            coarse=rng.random((16,16));noise=np.repeat(np.repeat(coarse,8,0),8,1)
            noise=.65*noise+.35*rng.random((n,n))
            border=np.minimum.reduce([x,y,n-1-x,n-1-y])/n
            grime=np.clip((.2-border)*2.5,0,.3)
            value=.75+.35*noise-grime
            rgb=np.array(color)[None,None,:]*value[...,None]
            # Small mineral inclusions and scratched paint, without copied textures.
            scratch=(rng.random((n,n))>.992)
            rgb[scratch]*=1.3
            base[:,tile*n:(tile+1)*n,:3]=np.clip(rgb,0,1)
            metal[:,tile*n:(tile+1)*n,:3]=metallic[tile]
            rough[:,tile*n:(tile+1)*n,:3]=np.clip(roughness[tile]+(noise-.5)*.12,0,1)[...,None]
        images=[]
        for label,data in [('BaseColor',base),('Metallic',metal),('Roughness',rough)]:
            data[:,:,3]=1
            im=bpy.data.images.new('THStage_'+label,width=n*8,height=n,alpha=False)
            if label!='BaseColor':im.colorspace_settings.name='Non-Color'
            im.pixels.foreach_set(data.ravel());im.filepath_raw=str(OUT/'textures'/('Stage_'+label+'.png'))
            im.file_format='PNG';im.save();im.pack();images.append(im)
        self.surface=bpy.data.materials.new('THStage_SharedSurface');self.surface.use_nodes=True
        bs=next(n for n in self.surface.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
        for im,socket in zip(images,('Base Color','Metallic','Roughness')):
            tex=self.surface.node_tree.nodes.new('ShaderNodeTexImage');tex.image=im
            self.surface.node_tree.links.new(tex.outputs['Color'],bs.inputs[socket])
        self.amber=self.solid('THStage_AmberLocator',(.8,.32,.055,1),1.8)
        self.teal=self.solid('THStage_ExtractionSignal',(.08,.6,.45,1),1.8)
        self.dark=self.solid('THStage_DarkRecess',(.018,.023,.022,1))
        self.floor=self.solid('THStage_PREVIEW_RealFloor',(.24,.26,.26,1))

    def solid(self,name,color,emission=0):
        m=bpy.data.materials.new(name);m.use_nodes=True
        bs=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
        bs.inputs['Base Color'].default_value=color;bs.inputs['Roughness'].default_value=.8
        if emission:
            bs.inputs['Emission Color'].default_value=color;bs.inputs['Emission Strength'].default_value=emission
        return m

    def begin(self,name):
        self.parts=[];self.name=name

    def own(self,obj,label,tile,material=None):
        obj.name=self.name+'_'+label
        for c in list(obj.users_collection):c.objects.unlink(obj)
        self.runtime.objects.link(obj);obj.parent=self.root
        obj.data.materials.append(material or self.surface)
        # Planar per-face UVs into original atlas; a single common surface material.
        uv=obj.data.uv_layers.new(name='UVMap') if not obj.data.uv_layers else obj.data.uv_layers.active
        for poly in obj.data.polygons:
            axis=max(range(3),key=lambda i:abs(poly.normal[i]));axes=[i for i in range(3) if i!=axis]
            coords=[obj.data.vertices[obj.data.loops[li].vertex_index].co for li in poly.loop_indices]
            low=[min(v[a] for v in coords) for a in axes];high=[max(v[a] for v in coords) for a in axes]
            for li,v in zip(poly.loop_indices,coords):
                u=(v[axes[0]]-low[0])/max(1e-6,high[0]-low[0]);w=(v[axes[1]]-low[1])/max(1e-6,high[1]-low[1])
                uv.data[li].uv=((tile+.07+.86*u)/8,.07+.86*w)
        self.parts.append(obj)
        return obj

    def box(self,label,pos,size,tile=0,bevel=.015,mat=None,rotation=0):
        x,d,h=pos
        bpy.ops.mesh.primitive_cube_add(size=1,location=(x,-d,h))
        obj=bpy.context.object;obj.scale=size
        activate([obj]);bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
        if bevel:
            mod=obj.modifiers.new('Worn bevel','BEVEL');mod.width=min(bevel,min(size)*.2);mod.segments=1
            bpy.ops.object.modifier_apply(modifier=mod.name)
        obj.rotation_euler.z=rotation
        return self.own(obj,label,tile,mat)

    def cylinder(self,label,pos,radius,depth,tile=0,vertices=12,rotation=(0,0,0),mat=None):
        x,d,h=pos
        bpy.ops.mesh.primitive_cylinder_add(vertices=vertices,radius=radius,depth=depth,location=(x,-d,h),rotation=rotation)
        return self.own(bpy.context.object,label,tile,mat)

    def rock(self,label,pos,size,tile=0):
        x,d,h=pos
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1,radius=1,location=(x,-d,h))
        obj=bpy.context.object
        for v in obj.data.vertices:v.co*=RNG.uniform(.85,1.15)
        obj.scale=size
        return self.own(obj,label,tile)

    def collider(self,name,pos,size):
        self.colliders.append(dict(name=name,center_unity_xyz=[pos[0],pos[2],pos[1]],size_unity_xyz=[size[0],size[2],size[1]],rotation_unity_xyz=[0,0,0]))

    def finish(self):
        activate(self.parts);bpy.ops.object.join();obj=bpy.context.object;obj.name=self.name
        obj['module_id']=self.name
        old=list(obj.data.materials);unique=list(dict.fromkeys(old))
        ids=[unique.index(old[p.material_index]) for p in obj.data.polygons]
        obj.data.materials.clear()
        for mat in unique:obj.data.materials.append(mat)
        for poly,mi in zip(obj.data.polygons,ids):poly.material_index=mi
        activate([obj]);bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
        self.modules.append(obj)
        return obj

    def lamp(self,x,d,h):
        self.box('LampFoot',(x,d,h+.025),(.18,.16,.05),3,.01)
        self.box('LampGlass',(x,d,h+.08),(.10,.09,.065),6,.008,self.amber)
        self.box('LampHood',(x,d,h+.12),(.19,.17,.025),3,.008)
        for sx in (-1,1):
            self.box('LampGuard',(x+sx*.065,d-.05,h+.075),(.016,.025,.09),3,.003)

    def lift(self):
        self.begin('THStage_ExtractionLift')
        self.box('Chassis',(0,.8,.23),(3,.9,.40),2,.045)
        self.box('TopDeck',(0,.8,.465),(2.96,.88,.07),3,.018)
        for i in range(13):self.box('DeckSlat',(-1.35+i*.225,.8,.51),(.205,.80,.025),2,.005)
        for x in (-1.32,1.32):
            for d in (.49,1.11):self.cylinder('Bolt',(x,d,.535),.027,.018,5,8)
        for d in (.34,1.26):self.box('EdgeSignal',(0,d,.43),(2.82,.028,.045),2,.004,self.teal)
        for x in (-1.4,1.4):
            self.box('CornerGuard',(x,.8,.37),(.13,.94,.52),6,.02)
            for d in (.52,.98):self.box('CornerWear',(x,d,.64),(.125,.16,.018),3,.003)
        for x in (-.92,.92):
            self.box('Piston',(x,.8,.12),(.28,.64,.18),3,.025)
            self.cylinder('Axle',(x,.8,.18),.1,.52,4,12,(math.pi/2,0,0))
        # Riveted front plate and bold downward arrow identify the destination.
        self.box('CollectorPlate',(0,1.27,.28),(.62,.035,.23),3,.015)
        self.box('ArrowStem',(0,1.294,.30),(.055,.008,.15),0,0,self.teal)
        for side in (-1,1):
            ob=self.box('ArrowWing',(side*.048,1.295,.26),(.12,.009,.035),0,0,self.teal)
            ob.rotation_euler.y=-side*.62
        self.collider('ExtractionLift',(0,.8,.27),(3,.96,.54))
        for x in (-1.4,1.4):self.collider('LiftCorner_'+str(x),(x,.8,.33),(.14,.96,.66))
        self.finish()
        self.marker('CollectionVolume',(0,.8,.67),'collection_volume',size=[2.7,.35,.74])
        self.marker('ScoreBoardAnchor',(0,.55,1.6),'nonphysical_ui_anchor')

    def bay(self,x,d,kind,index):
        self.begin('THStage_Bay_%02d_%s'%(index,kind))
        self.box('Foundation',(x,d,.07),(1.10,.85,.14),0,.035)
        if kind=='BrokenWall':
            for row in range(3):
                for col in range(3 if row<2 else 2):
                    self.box('Ashlar',(x-.35+col*.35+(row%2)*.03,d+.11,.22+row*.16),(.335,.40,.15),0 if (row+col)%2 else 1,.025)
            self.box('BrokenCap',(x-.22,d+.11,.66),(.49,.43,.09),1,.02,rotation=.07)
            self.rock('BrokenCapShard',(x+.16,d+.11,.60),(.15,.17,.08),1)
            for i in range(4):self.rock('Rubble',(x-.36+i*.22,d-.25,.20),(.12,.11,.10),0)
            self.lamp(x+.38,d-.17,.14)
        elif kind=='ColumnStump':
            self.box('Plinth',(x-.12,d,.19),(.65,.65,.16),1,.03)
            self.cylinder('Column',(x-.12,d,.43),.265,.34,0,12)
            self.cylinder('Collar',(x-.12,d,.30),.287,.06,1,12)
            self.cylinder('BrokenTop',(x-.12,d,.62),.275,.065,1,12)
            for i in range(6):
                a=i*math.tau/6
                self.box('Flute',(x-.12+.255*math.cos(a),d+.255*math.sin(a),.44),(.025,.025,.23),3,.005)
            self.rock('Fragment',(x+.37,d+.14,.24),(.13,.17,.13),1)
            self.lamp(x+.34,d-.20,.14)
        else:
            self.box('ReliquaryBody',(x,d,.33),(.96,.69,.38),0,.04)
            self.box('ReliquaryLid',(x,d,.555),(1.02,.75,.085),1,.025)
            self.box('Recess',(x,d-.351,.33),(.73,.013,.20),0,.005,self.dark)
            for sx in (-.36,.36):
                self.box('Band',(x+sx,d,.36),(.065,.72,.46),4,.01)
                for dz in (-.2,.2):self.cylinder('Rivet',(x+sx,d+dz,.60),.022,.02,5,8)
            self.lamp(x+.33,d+.16,.605)
        self.collider('Bay_%02d'%index,(x,d,.37),(1.10,.85,.74))
        self.finish()

    def perimeter(self):
        self.begin('THStage_Perimeter_Remnants')
        for side in (-1,1):
            for i in range(9):
                d=3.7+i*.79
                self.box('Sill',(side*2.38,d,.12),(.20,.73,.24),0,.025)
                if i%3==0:
                    self.box('Anchor',(side*2.36,d,.30),(.24,.30,.16),2,.018)
                    self.box('RustPlate',(side*2.225,d,.28),(.018,.21,.14),4,.005)
            self.collider('SideSill_'+str(side),(side*2.38,6.86,.20),(.24,7.10,.40))
        for i in range(8):
            self.box('BackRubble',(-2.1+i*.6,10.81,.18),(.56,.32,.36),0 if i%2 else 1,.03)
        self.collider('BackSill',(0,10.81,.18),(4.8,.34,.36))
        self.finish()
        self.begin('THStage_SealedArchiveRelief')
        self.box('OldLintel',(0,10.78,.35),(1.58,.34,.60),0,.035)
        self.box('Inset',(0,10.62,.40),(1.30,.018,.35),3,.02,self.dark)
        for i in range(9):
            x=-.56+i*.14
            self.box('ReliefTooth',(x,10.60,.4),(.055,.024,.21 if i%2 else .29),1,.005)
        for side in (-1,1):
            self.box('EndPier',(side*.76,10.78,.41),(.16,.37,.72),1,.02)
            self.box('OldCap',(side*.76,10.78,.76),(.20,.39,.035),0,.008)
        self.collider('SealedArchiveRelief',(0,10.78,.39),(1.75,.42,.78))
        self.finish()
        # Flat floor marks only, no cable or step crossing the walking route.
        self.begin('THStage_ExcavationFloorMarks')
        for side in (-1,1):
            for d in (3,5.4,7.65,10.2):
                self.box('SurveyMark',(side*2.05,d,.004),(.15,.035,.008),6,0)
                self.box('SurveyMark',(side*2.11,d+.06,.004),(.035,.15,.008),6,0)
        self.finish()

    def marker(self,name,pos,role,**extra):
        obj=bpy.data.objects.new(name,None);self.runtime.objects.link(obj);obj.parent=self.root
        obj.location=(pos[0],-pos[1],pos[2]);obj.empty_display_size=.08
        obj['role']=role
        obj['anchor_id']=name
        record=dict(name=name,role=role,position_unity_xyz=[pos[0],pos[2],pos[1]],position_blender_xyz=list(obj.location),**extra)
        self.markers.append(record)
        return obj

    def placement(self):
        for i,x in enumerate((-1.2,-.4,.4,1.2)):
            self.marker('Tutorial_%02d'%(i+1),(x,2.4,0),'tutorial',symbol=('circle','triangle','square','star')[i],color=('red','blue','green','yellow')[i])
        for row,d in enumerate((3.2,4.95,5.55,7.2,7.8,9.45,10.0)):
            for col,x in enumerate((-1.5,-.9,.9,1.5)):
                self.marker('TreasureSlot_%02d'%(row*4+col+1),(x,d,0),'treasure_candidate',
                            clearance_radius_m=.27,allowed_classes=['S','M'],distance_band=('near' if d<5.6 else 'mid' if d<8 else 'far'))

    def preview_items(self):
        candidates=[m for m in self.markers if m['role']=='treasure_candidate']
        # Exactly the 4-player core population: 14 S + 9 M. L/secret excluded.
        types=(['mask','cup','statuette','scarab_amulet','ceremonial_lamp']*3)[:14]+(['amphora','inscribed_stele']*5)[:9]
        selection=[candidates[i] for i in (0,2,3,4,5,7,8,10,11,12,13,15,16,18,19,20,21,22,23,24,25,26,27)]
        rng=random.Random(23);rng.shuffle(types)
        for name,slot in zip(types,selection):
            with bpy.data.libraries.load(str(TREASURES/(name+'.blend')),link=False) as (src,dst):dst.objects=src.objects
            added=[o for o in dst.objects if o and o.type in ('MESH','EMPTY')]
            for obj in added:self.preview.objects.link(obj)
            root=next(o for o in added if o.type=='EMPTY' and o.parent is None)
            root.location=slot['position_blender_xyz'];root['preview_only']=True
            root.rotation_euler.z=math.pi+rng.uniform(-.25,.25)
            self.sample_placements.append(dict(model=name,slot=slot['name'],class_name='M' if name in ('amphora','inscribed_stele') else 'S',root_name=root.name))
            self.preview_treasures.extend(added)
        self.scene.view_layers[0].update()

    def preview_setup(self):
        self.begin('THStage_PreviewFloor')
        obj=self.box('Floor',(0,5.5,-.04),(5,11,.08),0,.015,self.floor)
        self.runtime.objects.unlink(obj);self.preview.objects.link(obj);obj.parent=None
        self.scene.world=bpy.data.worlds.new('THStage_PreviewWorld');self.scene.world.use_nodes=True
        bg=next(n for n in self.scene.world.node_tree.nodes if n.type=='BACKGROUND')
        bg.inputs['Color'].default_value=(.12,.15,.17,1);bg.inputs['Strength'].default_value=.45
        try:self.scene.render.engine='CYCLES';self.scene.cycles.samples=24
        except TypeError:pass
        for name,pos,power,size,color in [('SoftSky',(0,-4,7),1700,8,(.66,.8,1)),('LiftWarm',(-3,0,4),800,4,(1,.65,.35)),('RearRim',(2,-11,4),1100,5,(.5,.8,.78))]:
            data=bpy.data.lights.new('THStage_'+name,'AREA');data.energy=power;data.shape='DISK';data.size=size;data.color=color
            obj=bpy.data.objects.new(data.name,data);self.preview.objects.link(obj);obj.location=pos
            obj.rotation_euler=(Vector((0,-5,.2))-obj.location).to_track_quat('-Z','Y').to_euler()
        camera=bpy.data.cameras.new('THStage_PreviewCamera');obj=bpy.data.objects.new(camera.name,camera);self.preview.objects.link(obj);self.scene.camera=obj
        obj.location=(9,6,10);obj.rotation_euler=(Vector((0,-5.1,0))-obj.location).to_track_quat('-Z','Y').to_euler()
        camera.type='ORTHO';camera.ortho_scale=13.9
        self.scene.render.resolution_x=1550;self.scene.render.resolution_y=1250
        self.scene.render.image_settings.file_format='PNG';self.scene.render.resolution_percentage=100

    def export(self):
        bpy.context.view_layer.update()
        for obj in self.modules:
            box=bounds([obj]);assert box[0][0]>=-2.5-1e-5 and box[0][1]<=2.5+1e-5,(obj.name,box)
            assert box[1][0]>=-11-1e-5 and box[1][1]<=1e-5,(obj.name,box)
            assert box[2][1]<.8,(obj.name,box)
        runtime=list(self.runtime.objects)
        activate(runtime)
        bpy.ops.export_scene.fbx(filepath=str(OUT/'ExcavationDepot_5x11.fbx'),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',use_custom_props=True,path_mode='COPY',embed_textures=True,bake_anim=False,add_leaf_bones=False)
        # Each module is separately replaceable; preserve assembled local coordinates.
        (OUT/'modules').mkdir(exist_ok=True)
        for obj in self.modules:
            activate([obj])
            bpy.ops.export_scene.fbx(filepath=str(OUT/'modules'/(obj['module_id']+'.fbx')),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',use_custom_props=True,path_mode='COPY',embed_textures=True,bake_anim=False,add_leaf_bones=False)
        self.colliders.append(dict(name='VenueFloor',center_unity_xyz=[0,-.025,5.5],size_unity_xyz=[5,.05,11],rotation_unity_xyz=[0,0,0]))
        metadata=dict(venue=dict(width_m=5,length_m=11,origin='center of entrance edge',blender_from_unity='(X, -Z, Y)',
                                  warning_distance_authoring_assumption_m=.4,spawn_edge_extra_margin_m=.3),
                      central_route=dict(fixed_clear_width_m=2.1,with_treasure_envelopes_clear_width_m=1.26),
                      runtime_stats=stats(runtime),preview_treasure_stats=stats(self.preview_treasures),
                      module_stats=[dict(name=o['module_id'],**stats([o])) for o in self.modules],
                      colliders=self.colliders,markers=self.markers,
                      collider_status='Conservative placement envelopes only; split to fit visible solids with Unity Editor API before gameplay use.',
                      sample_placements=self.sample_placements,
                      preview_only='floor, 23 sample treasures, camera and lights; not in stage FBX',
                      unity_imported=False,pico_fps_measured=False)
        (OUT/'stage_layout.json').write_text(json.dumps(metadata,indent=2,ensure_ascii=False),encoding='utf-8')
        # A clean standalone runtime scene and a separately labelled review scene.
        runtime_scene=bpy.data.scenes.new('TH_Stage_Runtime_5x11');runtime_scene.collection.children.link(self.runtime)
        runtime_scene.unit_settings.system='METRIC';runtime_scene.unit_settings.scale_length=1
        bpy.data.libraries.write(str(OUT/'ExcavationDepot_5x11.blend'),{runtime_scene},fake_user=True,compress=True)
        bpy.data.scenes.remove(runtime_scene)
        bpy.data.libraries.write(str(OUT/'Stage_Review.blend'),{self.scene},fake_user=True,compress=True)
        write_plan(metadata)
        print(json.dumps(dict(stage=metadata['runtime_stats'],treasures=metadata['preview_treasure_stats'],modules=len(self.modules),candidate_points=28),indent=2))

    def render(self,view):
        camera=self.scene.camera
        if view=='overview':
            camera.location=(9,6,10);target=(0,-5.1,0);camera.data.type='ORTHO';camera.data.ortho_scale=13.9
            size=(1550,1250)
        elif view=='top':
            camera.location=(0,-5.5,16);target=(0,-5.5,0);camera.data.type='ORTHO';camera.data.ortho_scale=12.3
            size=(800,1500)
        elif view=='eye':
            camera.location=(.1,-2.6,1.65);target=(0,-8,.3);camera.data.type='PERSP';camera.data.lens=22
            size=(1500,950)
        else:
            camera.location=(3.4,-4.1,2.6);target=(0,-.8,.35);camera.data.type='PERSP';camera.data.lens=37
            size=(1400,950)
        camera.rotation_euler=(Vector(target)-camera.location).to_track_quat('-Z','Y').to_euler()
        self.scene.render.resolution_x,self.scene.render.resolution_y=size
        self.scene.render.filepath=str(OUT/(view+'.png'))
        bpy.ops.render.render(write_still=True,scene=self.scene.name)

def write_plan(layout):
    """Editable dimensioned plan, authored SVG; no font/asset downloads."""
    scale=64; ox=245; oy=850
    def pt(x,d):return ox+x*scale,oy-d*scale
    parts=['<svg xmlns="http://www.w3.org/2000/svg" width="900" height="950" viewBox="0 0 900 950">',
           '<rect width="900" height="950" fill="#152222"/>',
           '<g font-family="sans-serif" fill="#e2e9e3">',
           '<text x="65" y="45" font-size="26">TREASURE HAUL / 発掘収蔵区画</text>',
           '<text x="65" y="77" font-size="15">5m × 11m ・ 最大4人 ・ MR床面はパススルー</text>']
    def rect(x,d,w,l,color,stroke='none'):
        px,py=pt(x-w/2,d+l/2)
        parts.append(f'<rect x="{px}" y="{py}" width="{w*scale}" height="{l*scale}" fill="{color}" stroke="{stroke}"/>')
    rect(0,5.5,5,11,'#263736','#829e97')
    rect(0,5.5,3.6,9.6,'none','#bf9452')
    rect(0,6.05,1.2,8.7,'#284c42')
    rect(0,2.2,3.6,1.2,'#32484e')
    for c in layout['colliders']:
        if c['name']=='VenueFloor':continue
        x,h,d=c['center_unity_xyz'];w,height,length=c['size_unity_xyz']
        rect(x,d,w,length,'#7a8072' if 'Lift' not in c['name'] else '#5caca0','#b4b6a5')
    for m in layout['markers']:
        if m['role'] not in ('treasure_candidate','tutorial'):continue
        x,h,d=m['position_unity_xyz'];px,py=pt(x,d)
        if m['role']=='tutorial':
            parts.append(f'<rect x="{px-9}" y="{py-9}" width="18" height="18" fill="#7398b0"/>')
            parts.append(f'<text x="{px}" y="{py+4}" text-anchor="middle" font-size="11">{m["name"][-1]}</text>')
        else:
            parts.append(f'<circle cx="{px}" cy="{py}" r="{.27*scale}" fill="none" stroke="#997d42" stroke-dasharray="2 3"/>')
            parts.append(f'<circle cx="{px}" cy="{py}" r="4" fill="#efc567"/>')
    parts.extend(['<text x="245" y="888" text-anchor="middle" font-size="17">入口側 / 原点 (0,0,0)</text>',
                  '<text x="245" y="130" text-anchor="middle" font-size="18">← 5.0 m →</text>',
                  '<text x="52" y="500" font-size="18" transform="rotate(-90 52 500)">← 11.0 m →</text>'])
    notes=[('配置と動線',26),('水色：搬出リフト（幅3m）',17),('青い四角：練習用の固定4点',17),('黄色の点：お宝候補28点',17),('点線円：半径0.27mの配置余裕',17),('中央の緑：幅1.2mの通路',17),('灰色：高さ0.8m未満の障害物',17),('',17),('境界から0.7mは配置除外',17),('警告距離0.4m＋余白0.3mを仮定',15),('警告距離はUnity導入時に再確認',15),('',17),('4人用の例：S 14個＋M 9個',17),('候補は毎回ランダム選択する想定',15),('L・秘宝はコア配置に含めない',15),('',17),('原点：幅中央・入口端・床面',17),('Unity：X=幅、Y=高さ、Z=奥行き',15),('Blender：(X, -奥行き, 高さ)',15)]
    y=180
    for label,size in notes:
        parts.append(f'<text x="470" y="{y}" font-size="{size}">{label}</text>');y+=34
    parts.extend(['</g></svg>'])
    (OUT/'floorplan.svg').write_text('\n'.join(parts),encoding='utf-8')

def run():
    previous=bpy.context.window.scene;selected=list(bpy.context.selected_objects);active_obj=bpy.context.view_layer.objects.active
    original=set(bpy.data.objects)
    try:
        b=Builder();b.lift()
        for i,(x,d,kind) in enumerate([(-1.6,4.1,'BrokenWall'),(1.6,4.1,'Reliquary'),(-1.6,6.35,'ColumnStump'),(1.6,6.35,'BrokenWall'),(-1.6,8.6,'Reliquary'),(1.6,8.6,'ColumnStump')],1):b.bay(x,d,kind,i)
        b.perimeter();b.placement();b.preview_items();b.preview_setup();b.export()
        return b
    finally:
        bpy.context.window.scene=previous
        if selected:activate(selected)
        else:bpy.ops.object.select_all(action='DESELECT')
        bpy.context.view_layer.objects.active=active_obj
        assert original.issubset(set(bpy.data.objects))

if __name__=='__main__':stage_builder=run()
