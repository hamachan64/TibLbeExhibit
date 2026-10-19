"""Original salvage bin: researched revision, no external art assets."""
import bpy, importlib.util, math, json
from pathlib import Path
from mathutils import Vector
HERE=Path(__file__).resolve().parent
spec=importlib.util.spec_from_file_location('cart_base',HERE/'build_cart.py')
base=importlib.util.module_from_spec(spec);spec.loader.exec_module(base)
base.OUT=HERE/'Revision2';base.OUT.mkdir(exist_ok=True)

class SalvageCart(base.CartBuilder):
    def __init__(self):
        super().__init__()
        self.root['revision']=2
        self.paint=self.solid('THCart_WornEnamel',(.16,.20,.18,1))
        # Original coarse paint chips; eight repeated swatches match existing UV helpers.
        np=base.stage.np;rng=np.random.default_rng(19026)
        coarse=np.repeat(np.repeat(rng.random((64,64)),2,0),2,1)
        chip=coarse<.025;pixels=np.ones((128,128,4),dtype=np.float32)
        for i,c in enumerate((.20,.25,.22)):pixels[:,:,i]=c*(.90+.10*coarse)
        pixels[chip,:3]=(.10,.115,.09)
        pixels=np.tile(pixels,(1,8,1))
        img=bpy.data.images.new('THCart_OriginalWornPaint',width=1024,height=128)
        img.pixels.foreach_set(pixels.ravel());img.pack()
        img.filepath_raw=str(base.OUT/'Cart_WornPaint.png');img.file_format='PNG';img.save()
        tex=self.paint.node_tree.nodes.new('ShaderNodeTexImage');tex.image=img
        bs=next(n for n in self.paint.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
        self.paint.node_tree.links.new(tex.outputs['Color'],bs.inputs['Base Color'])
        bs.inputs['Metallic'].default_value=.35;bs.inputs['Roughness'].default_value=.82
        self.dark=self.solid('THCart_Charcoal',(.025,.033,.029,1))
        self.screen=self.solid('THCart_Screen',(.006,.009,.024,1))
        self.digits=self.solid('THCart_Phosphor',(.9,.75,.12,1))
        bs=next(n for n in self.digits.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
        bs.inputs['Emission Color'].default_value=(.9,.75,.12,1);bs.inputs['Emission Strength'].default_value=1.2

    def body(self):
        self.begin('THCart_Chassis')
        self.box('Underpan',(0,0,.25),(.72,1,.10),3,.025,self.dark)
        for side in (-1,1):self.box('RecessedDriveHousing',(side*.29,0,.19),(.19,.94,.22),3,.02,self.dark)
        self.box('LoadFloor',(0,0,.303),(.66,.94,.034),3,.006,self.dark)
        # Broad panels and heavy bumpers replace the narrow rails of the first draft.
        for side in (-1,1):
            self.box('BinWall',(side*.35,0,.475),(.06,1,.31),0,.012,self.paint)
            self.box('Rim',(side*.35,0,.64),(.075,1.025,.045),3,.012,self.dark)
            self.box('RecessPanel',(side*.382,0,.46),(.008,.69,.19),3,.002,self.dark)
            self.box('InsetPanel',(side*.389,0,.47),(.008,.61,.13),0,.002,self.paint)
            for d in (-.43,.43):
                self.box('CornerShoe',(side*.35,d,.35),(.10,.15,.20),3,.016,self.dark)
                self.box('WearTab',(side*.404,d,.39),(.008,.08,.08),4,.001)
            for d in (-.33,.33):self.box('WheelMount',(side*.29,d,.23),(.15,.16,.06),3,.008,self.dark)
        for d in (-.48,.48):
            self.box('EndWall',(0,d,.475),(.66,.05,.31),0,.01,self.paint)
            self.box('EndRim',(0,d,.64),(.70,.07,.045),3,.01,self.dark)
        for x in (-.27,.27):
            self.box('FrontBuffer',(x,.525,.35),(.14,.10,.14),3,.02,self.dark)
            self.box('WarningPatch',(x,.58,.37),(.085,.008,.045),6,.001)
        # Deliberately asymmetric machine details, no copied logo or game typography.
        for i in range(5):self.box('Vent',(-.15+i*.045,.51,.47),(.023,.008,.065),3,.001,self.dark)
        self.box('ServiceHatch',(.21,.515,.50),(.14,.02,.14),3,.008,self.dark)
        self.finish()
        self.collider('Deck',(0,0,.28),(.66,.94,.08))
        for s in (-1,1):self.collider('Wall'+str(s),(s*.35,0,.475),(.06,1,.31))
        for d in (-.48,.48):self.collider('End'+str(d),(0,d,.475),(.66,.05,.31))

    def handle(self):
        pivot=self.pivot('PushHandlePivot',(0,-.50,.64),axis='X')
        self.begin('THCart_PushHandle')
        for side in (-1,1):
            self.rod('ShortStanchion',(side*.30,-.47,.59),(side*.30,-.59,.75),.026,3)
            self.rod('Grip',(side*.18,-.59,.75),(side*.32,-.59,.75),.03,7)
        self.rod('PushBar',(-.30,-.59,.75),(.30,-.59,.75),.023,3)
        self.attach(self.finish(),pivot)
        for side in (-1,1):
            p=self.pivot('PushGrip_'+('L' if side<0 else 'R'),(side*.25,-.59,.75))
            bpy.context.view_layer.update();world=p.matrix_world.copy();p.parent=pivot;p.matrix_world=world
        self.begin('THCart_ValueConsole')
        self.box('Console',(0,-.535,.64),(.49,.12,.23),3,.022,self.dark)
        self.box('ScreenGlass',(0,-.601,.65),(.405,.008,.132),3,.002,self.screen)
        self.box('StatusLamp',(.22,-.603,.65),(.015,.008,.035),0,.001,self.digits)
        self.finish()
        anchor=self.pivot('ValueDisplay',(0,-.610,.65));anchor['purpose']='current cargo sum in G; runtime text, not baked price'
        anchor['text_width_m']=.38;anchor['text_height_m']=.11
        volume=self.pivot('CargoVolume',(0,0,.49));volume['size_unity_xyz']=[.64,.34,.88]

    def cargo(self):
        # Library locations are relative to original Cart folder, not revision output.
        saved=base.OUT;base.OUT=HERE
        try:super().cargo()
        finally:base.OUT=saved

    def setup_preview(self):
        super().setup_preview()
        self.scene.camera.location=(1.6,2.3,1.7)
        self.scene.camera.rotation_euler=(Vector((0,0,.4))-self.scene.camera.location).to_track_quat('-Z','Y').to_euler()
        self.scene.camera.data.ortho_scale=1.65
        self.preview_digits=[]

    def render(self,name):
        # Seven-segment preview drawn from primitives, no font/image downloads.
        for o in self.preview_digits:bpy.data.objects.remove(o,do_unlink=True)
        self.preview_digits=[];self.begin('PreviewDigits')
        value='  3600' if name=='loaded' else '     0'
        codes={'0':'abcdef','1':'bc','2':'abged','3':'abgcd','4':'fgbc','5':'afgcd','6':'afgecd','7':'abc','8':'abcdefg','9':'abfgcd'}
        segs={'a':(0,.040,.030,.006),'g':(0,0,.030,.006),'d':(0,-.040,.030,.006),'f':(-.018,.021,.006,.032),'b':(.018,.021,.006,.032),'e':(-.018,-.021,.006,.032),'c':(.018,-.021,.006,.032)}
        previous=bpy.context.window.scene;bpy.context.window.scene=self.scene
        try:
            for i,ch in enumerate(value):
                if ch==' ':continue
                for seg in codes[ch]:
                    x,z,w,h=segs[seg]
                    o=self.box('PreviewDigit',(-(-.18+i*.052+x),-.612,.65+z),(w,.002,h),0,0,self.digits)
                    self.runtime.objects.unlink(o);self.preview.objects.link(o);self.preview_digits.append(o)
            curve=bpy.data.curves.new('PreviewUnitG','FONT');curve.body='G';curve.size=.095;curve.align_x='CENTER'
            o=bpy.data.objects.new('PreviewUnitG',curve);self.preview.objects.link(o)
            o.location=(-.15,.613,.614);o.rotation_euler=(math.pi/2,0,math.pi);curve.materials.append(self.digits)
            self.preview_digits.append(o)
            super().render(name)
        finally:bpy.context.window.scene=previous

def run():
    old=bpy.context.window.scene;selection=list(bpy.context.selected_objects);active=bpy.context.view_layer.objects.active
    try:
        b=SalvageCart();b.body();b.handle();b.wheels();b.cargo();b.setup_preview();b.export()
        b.render('empty');b.render('loaded')
        bpy.data.libraries.write(str(base.OUT/'Cart_Review.blend'),{b.scene},fake_user=True,compress=True)
        return b
    finally:
        bpy.context.window.scene=old;bpy.ops.object.select_all(action='DESELECT')
        for o in selection:o.select_set(True)
        bpy.context.view_layer.objects.active=active

if __name__=='__main__':cart_builder=run()
