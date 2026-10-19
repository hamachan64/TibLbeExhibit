"""Original excavation handcart, using this project's authored stage atlas.

Run in Blender via MCP. Keeps the user's active scene and objects intact.
Logical X=width, D=forward, H=up; Blender=(X,-D,H). No external assets.
"""
import bpy, json, math, importlib.util
from pathlib import Path
from mathutils import Vector, Matrix

OUT=Path(__file__).resolve().parent
STAGE=OUT.parent/'Stage'
spec=importlib.util.spec_from_file_location('th_local_stage_tools',STAGE/'build_stage.py')
stage=importlib.util.module_from_spec(spec);spec.loader.exec_module(stage)

class CartBuilder(stage.Builder):
    def __init__(self):
        OUT.mkdir(exist_ok=True)
        self.scene=bpy.data.scenes.new('TH_Cart_Review');bpy.context.window.scene=self.scene
        self.scene.unit_settings.system='METRIC';self.scene.unit_settings.scale_length=1
        self.runtime=bpy.data.collections.new('TH_Cart_Runtime');self.scene.collection.children.link(self.runtime)
        self.preview=bpy.data.collections.new('TH_Cart_PreviewOnly');self.scene.collection.children.link(self.preview)
        self.root=bpy.data.objects.new('TH_Cart_Root',None);self.runtime.objects.link(self.root)
        self.root['asset_id']='TH_ExcavationCart';self.root['origin']='ground beneath deck center'
        self.modules=[];self.parts=[];self.markers=[];self.colliders=[];self.preview_treasures=[]
        self.pivots=[]
        with bpy.data.libraries.load(str(STAGE/'ExcavationDepot_5x11.blend'),link=False) as (src,dst):
            dst.materials=[n for n in src.materials if n.startswith(('THStage_SharedSurface','THStage_ExtractionSignal'))]
        self.surface=next(m for m in dst.materials if m and m.name.startswith('THStage_SharedSurface'))
        self.teal=next(m for m in dst.materials if m and m.name.startswith('THStage_ExtractionSignal'))
        self.floor=self.solid('THCart_PreviewFloor',(.19,.22,.22,1))

    def pivot(self,name,pos,parent=None,axis=None):
        obj=bpy.data.objects.new('THCart_'+name,None);self.runtime.objects.link(obj)
        obj.parent=parent or self.root;obj.location=(pos[0],-pos[1],pos[2])
        obj['part_id']=name
        if axis:obj['rotation_axis_blender']=axis
        obj.empty_display_size=.06;self.pivots.append(obj)
        return obj

    def attach(self,obj,pivot):
        # finish() leaves world-space mesh vertices. Move them to the pivot frame.
        bpy.context.view_layer.update()
        obj.data.transform(pivot.matrix_world.inverted())
        obj.parent=pivot;obj.matrix_parent_inverse=Matrix.Identity(4);obj.matrix_basis=Matrix.Identity(4)

    def rod(self,name,a,b,radius=.022,tile=2):
        va=Vector((a[0],-a[1],a[2]));vb=Vector((b[0],-b[1],b[2]));delta=vb-va
        mid=(va+vb)/2
        bpy.ops.mesh.primitive_cylinder_add(vertices=12,radius=radius,depth=delta.length,location=mid)
        obj=bpy.context.object;obj.rotation_euler=delta.to_track_quat('Z','Y').to_euler()
        return self.own(obj,name,tile)

    def body(self):
        self.begin('THCart_Chassis')
        self.box('Frame',(0,0,.25),(.72,1,.08),2,.018)
        self.box('BedSupport',(0,0,.283),(.67,.94,.026),3,.006)
        for i in range(9):self.box('BedSlat',(0,-.408+i*.102,.303),(.66,.098,.034),2,.005)
        for side in (-1,1):
            self.box('LowerLip',(side*.347,0,.34),(.036,1.0,.06),2,.008)
            self.box('EdgeWear',(side*.367,0,.272),(.016,.94,.03),4,.004)
            for d in (-.4,.4):
                self.rod('RailPost',(side*.35,d,.29),(side*.35,d,.48),.025,2)
                self.cylinder('PostFoot',(side*.35,d,.305),.033,.025,5,12)
            self.rod('SideRail',(side*.35,-.43,.48),(side*.35,.45,.48),.025,2)
            for d in (-.33,.33):
                self.box('AxleSupport',(side*.29,d,.22),(.15,.16,.045),3,.008)
        self.box('FrontLip',(0,.486,.35),(.72,.04,.06),2,.008)
        self.box('RearLip',(0,-.486,.35),(.72,.04,.06),2,.008)
        self.rod('FrontRail',(-.35,.45,.48),(.35,.45,.48),.025,2)
        for x in (-.26,.26):self.box('FrontWear',(x,.51,.265),(.08,.018,.065),6,.005)
        self.box('IdentityPlate',(0,.511,.263),(.22,.015,.065),3,.006)
        # A short marker, not the glowing rings used to identify treasure grips.
        self.box('Locator',(0,.521,.263),(.11,.006,.014),2,.001,self.teal)
        for x in (-.29,.29):
            for d in (-.40,.40):self.cylinder('DeckBolt',(x,d,.324),.013,.008,5,8)
        self.finish()
        self.collider('Deck',(0,0,.265),(.72,1,.11))
        for side in (-1,1):
            self.collider('SideLip_'+str(side),(side*.347,0,.34),(.036,1,.06))
            self.collider('SideRail_'+str(side),(side*.35,.01,.48),(.05,.88,.05))
        self.collider('FrontRail',(0,.45,.48),(.70,.05,.05))
        self.collider('FrontLip',(0,.486,.35),(.72,.04,.06))
        self.collider('RearLip',(0,-.486,.35),(.72,.04,.06))

    def handle(self):
        pivot=self.pivot('PushHandlePivot',(0,-.44,.28),axis='X')
        self.begin('THCart_PushHandle')
        for side in (-1,1):
            self.rod('Upright',(side*.30,-.44,.28),(side*.30,-.57,.70),.023,2)
            self.rod('Bend',(side*.30,-.57,.70),(side*.27,-.60,.75),.023,2)
            self.cylinder('Hinge',(side*.30,-.44,.28),.034,.055,4,12,(0,math.pi/2,0))
            self.rod('Grip',(side*.09,-.60,.75),(side*.25,-.60,.75),.029,7)
        self.rod('Crossbar',(-.27,-.60,.75),(.27,-.60,.75),.023,2)
        self.attach(self.finish(),pivot)
        for side in (-1,1):
            m=self.pivot('PushGrip_'+('L' if side<0 else 'R'),(side*.17,-.60,.75))
            bpy.context.view_layer.update()
            world=m.matrix_world.copy();m.parent=pivot;m.matrix_world=world
            m['purpose']='future interaction anchor; no gameplay implemented'

    def tire(self,label,x,d):
        profile=[(-.032,.055),(-.037,.075),(-.028,.097),(-.018,.10),(.018,.10),(.028,.097),(.037,.075),(.032,.055)]
        n=20
        vs=[(x+ax,-d+r*math.sin(i*math.tau/n),.10+r*math.cos(i*math.tau/n)) for ax,r in profile for i in range(n)]
        faces=[]
        for j in range(len(profile)):
            for i in range(n):faces.append((j*n+i,j*n+(i+1)%n,((j+1)%len(profile))*n+(i+1)%n,((j+1)%len(profile))*n+i))
        mesh=bpy.data.meshes.new(label);mesh.from_pydata(vs,[],faces);mesh.update()
        obj=bpy.data.objects.new(label,mesh);self.runtime.objects.link(obj)
        self.own(obj,'Tire',7)

    def wheels(self):
        for front,d in ((True,.33),(False,-.37)):
            for side in (-1,1):
                x=side*.29;tag=('Front' if front else 'Rear')+('L' if side<0 else 'R')
                caster=None
                if not front:
                    caster=self.pivot('Caster_'+tag,(x,d+.04,.23),axis='Z')
                self.begin('THCart_Fork_'+tag)
                for sx in (-1,1):self.box('Fork',(x+sx*.05,d,.155),(.018,.13,.15),3,.008)
                self.box('Bridge',(x,d+.015,.225),(.13,.14,.025),3,.006)
                if caster:self.cylinder('SwivelBearing',(x,d+.04,.235),.057,.022,5,16)
                fork=self.finish()
                if caster:self.attach(fork,caster)
                wheel=self.pivot('Wheel_'+tag,(x,d,.10),axis='X')
                if caster:
                    bpy.context.view_layer.update()
                    matrix=wheel.matrix_world.copy();wheel.parent=caster;wheel.matrix_world=matrix
                self.begin('THCart_Wheel_'+tag)
                self.tire(tag,x,d)
                self.cylinder('Rim',(x,d,.10),.058,.078,2,20,(0,math.pi/2,0))
                self.cylinder('Axle',(x,d,.10),.022,.095,5,12,(0,math.pi/2,0))
                self.attach(self.finish(),wheel)
                wheel['radius_m']=.1
        # Fixed front forks are structural: merge into the body to reduce renderers.
        body=next(o for o in self.modules if o.get('module_id')=='THCart_Chassis')
        fixed=[o for o in self.modules if o.get('module_id','').startswith('THCart_Fork_Front')]
        self.modules=[o for o in self.modules if o not in fixed]
        stage.activate([body,*fixed]);bpy.ops.object.join()
        self.compact(body)

    def compact(self,obj):
        old=list(obj.data.materials);unique=list(dict.fromkeys(old));ids=[unique.index(old[p.material_index]) for p in obj.data.polygons]
        obj.data.materials.clear()
        for mat in unique:obj.data.materials.append(mat)
        for p,i in zip(obj.data.polygons,ids):p.material_index=i

    def cargo(self):
        for name,x,d in [('amphora',0,.19),('cup',-.17,-.20),('statuette',.16,-.20)]:
            with bpy.data.libraries.load(str(OUT.parent/'Reviewed'/(name+'.blend')),link=False) as (src,dst):dst.objects=src.objects
            objects=[o for o in dst.objects if o and o.type in ('EMPTY','MESH')]
            for o in objects:self.preview.objects.link(o)
            root=next(o for o in objects if o.type=='EMPTY' and o.parent is None)
            root.location=(x,-d,0);root.rotation_euler.z=math.pi
            bpy.context.view_layer.update()
            low=min((o.matrix_world@v.co).z for o in objects if o.type=='MESH' for v in o.data.vertices)
            root.location.z=.32-low
            self.preview_treasures.extend(objects)

    def setup_preview(self):
        self.begin('THCart_Preview')
        floor=self.box('Floor',(0,0,-.025),(3,3,.05),0,.003,self.floor)
        self.runtime.objects.unlink(floor);self.preview.objects.link(floor);floor.parent=None
        self.scene.world=bpy.data.worlds.new('THCart_World');self.scene.world.use_nodes=True
        bg=next(n for n in self.scene.world.node_tree.nodes if n.type=='BACKGROUND');bg.inputs['Color'].default_value=(.12,.15,.17,1);bg.inputs['Strength'].default_value=.45
        try:self.scene.render.engine='CYCLES';self.scene.cycles.samples=32
        except TypeError:pass
        for label,loc,power,size,color in [('Key',(2,-3,4),500,3,(1,.83,.63)),('Fill',(-3,-1,2),350,3,(.6,.8,1)),('Rim',(1,3,3),450,2,(.7,1,.9))]:
            data=bpy.data.lights.new('THCart_'+label,'AREA');data.energy=power;data.shape='DISK';data.size=size;data.color=color
            ob=bpy.data.objects.new(data.name,data);self.preview.objects.link(ob);ob.location=loc
            ob.rotation_euler=(Vector((0,0,.3))-ob.location).to_track_quat('-Z','Y').to_euler()
        data=bpy.data.cameras.new('THCart_Camera');ob=bpy.data.objects.new(data.name,data);self.preview.objects.link(ob);self.scene.camera=ob
        ob.location=(1.8,-2.2,1.6);ob.rotation_euler=(Vector((0,0,.36))-ob.location).to_track_quat('-Z','Y').to_euler()
        data.type='ORTHO';data.ortho_scale=1.7
        self.scene.render.resolution_x=1350;self.scene.render.resolution_y=1150;self.scene.render.resolution_percentage=100
        self.scene.render.image_settings.file_format='PNG'

    def export(self):
        bpy.context.view_layer.update();objects=list(self.runtime.objects)
        stats=stage.stats(objects);bb=stage.bounds(objects)
        assert stats['triangles']<=5000,stats
        assert bb[2][0]>-.0001 and bb[2][1]<.8,bb
        stage.activate(objects)
        bpy.ops.export_scene.fbx(filepath=str(OUT/'ExcavationCart.fbx'),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',use_custom_props=True,path_mode='COPY',embed_textures=True,bake_anim=False,add_leaf_bones=False)
        scene=bpy.data.scenes.new('TH_Cart_Asset');scene.collection.children.link(self.runtime);scene.unit_settings.system='METRIC'
        bpy.data.libraries.write(str(OUT/'ExcavationCart.blend'),{scene},fake_user=True,compress=True);bpy.data.scenes.remove(scene)
        bpy.data.libraries.write(str(OUT/'Cart_Review.blend'),{self.scene},fake_user=True,compress=True)
        report=dict(**stats,dimensions_m=[round(b-a,5) for a,b in bb],bounds_blender=bb,
                    load_floor_height_m=.32,load_clear_size_m=[.64,.88],wheel_radius_m=.1,
                    turn_envelope_diameter_m=round(2*max(math.hypot(x,y) for x in bb[0] for y in bb[1]),3),
                    stage_min_lane_m=1.26,straight_side_margin_m=round((1.26-(bb[0][1]-bb[0][0]))/2,3),
                    colliders=self.colliders,parts=[dict(id=o.get('module_id'),**stage.stats([o])) for o in self.modules],
                    pivots=[dict(id=o['part_id'],parent=o.parent.get('part_id',o.parent.name),location_blender=list(o.location),axis=o.get('rotation_axis_blender')) for o in self.pivots],
                    unity_imported=False,gameplay='model only; movable cart is future option U-4')
        (OUT/'cart_stats.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps(report,indent=2))

    def render(self,name):
        for obj in self.preview_treasures:obj.hide_render=name!='loaded'
        self.scene.render.filepath=str(OUT/(name+'.png'))
        bpy.ops.render.render(write_still=True,scene=self.scene.name)
        for obj in self.preview_treasures:obj.hide_render=False

def run():
    previous=bpy.context.window.scene;selection=list(bpy.context.selected_objects);active=bpy.context.view_layer.objects.active;original=set(bpy.data.objects)
    try:
        b=CartBuilder();b.body();b.handle();b.wheels();b.cargo();b.setup_preview();b.export();return b
    finally:
        bpy.context.window.scene=previous;bpy.ops.object.select_all(action='DESELECT')
        for obj in selection:obj.select_set(True)
        bpy.context.view_layer.objects.active=active
        assert original.issubset(set(bpy.data.objects))

if __name__=='__main__':cart_builder=run()
