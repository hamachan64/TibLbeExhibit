"""Abandoned antiquities archive: original geometry, shared atlas, MR-height adaptation."""
import bpy, importlib.util, json, math
from pathlib import Path
HERE=Path(__file__).resolve().parent
spec=importlib.util.spec_from_file_location('stage_base',HERE/'build_stage.py')
base=importlib.util.module_from_spec(spec);spec.loader.exec_module(base)
base.OUT=HERE/'Revision2'

class ArchiveBuilder(base.Builder):
    def make_materials(self):
        super().make_materials()
        old=[(.16,.205,.20),(.34,.37,.32),(.10,.16,.15),(.055,.067,.07),(.28,.13,.067),(.47,.43,.30),(.54,.34,.09),(.09,.10,.085)]
        new=[(.34,.29,.22),(.24,.25,.23),(.10,.145,.135),(.10,.052,.032),(.22,.075,.045),(.37,.29,.13),(.49,.32,.08),(.13,.035,.04)]
        for node in self.surface.node_tree.nodes:
            if node.type!='TEX_IMAGE':continue
            im=node.image;data=base.np.array(im.pixels[:],dtype=base.np.float32).reshape(128,1024,4)
            if 'BaseColor' in im.name:
                for tile in range(8):data[:,tile*128:(tile+1)*128,:3]*=base.np.array(new[tile])/base.np.array(old[tile])
            if 'Metallic' in im.name:
                for tile in (3,7):data[:,tile*128:(tile+1)*128,:3]=0
            im.pixels.foreach_set(data.ravel());im.save();im.pack()

    def panel(self,x,d,h,w=.8,height=.3):
        self.box('PanelBacking',(x,d,h),(w,.055,height),3,.007)
        self.box('Inset',(x,d-.034,h),(w-.09,.012,height-.08),7,.003)
        for sx in (-1,1):self.box('Stile',(x+sx*(w/2-.018),d-.045,h),(.035,.025,height),3,.004)
        for sh in (-1,1):self.box('Rail',(x,d-.045,h+sh*(height/2-.018)),(w,.025,.035),3,.004)

    def lift(self):
        self.begin('THStage_ExtractionLift')
        self.box('MachineBase',(0,.8,.23),(3,.92,.42),2,.035)
        self.box('LoadingDeck',(0,.8,.47),(2.78,.84,.06),2,.012)
        for x in (-1.4,1.4):
            self.box('ControlHousing',(x,.8,.40),(.18,.94,.76),2,.025)
            self.box('Panel',(x,1.278,.52),(.13,.018,.29),3,.004,self.dark)
            for h in (.45,.51,.57):self.box('Indicator',(x,1.291,h),(.065,.008,.015),0,.001,self.amber)
        for i in range(17):self.box('DeckRib',(-1.25+i*.155,.8,.51),(.10,.76,.018),2,.003)
        for x in (-1.12,1.12):
            for i in range(3):self.box('HazardTab',(x+i*.05,1.272,.34),(.028,.012,.12),6,.002)
        self.box('MeterHousing',(0,1.27,.30),(1.0,.035,.27),2,.014)
        self.box('MeterGlass',(0,1.294,.32),(.82,.012,.13),3,.003,self.dark)
        for i in range(5):self.box('MeterTick',(-.30+i*.15,1.302,.32),(.07,.006,.012),0,0,self.teal)
        self.box('ReceiverSlot',(0,1.296,.20),(.55,.01,.025),3,.002,self.dark)
        self.finish()
        self.collider('ExtractionLift',(0,.815,.39),(3,.98,.78))
        self.marker('CollectionVolume',(0,.8,.67),'collection_volume',size=[2.7,.35,.74])
        self.marker('ScoreBoardAnchor',(0,.55,1.6),'nonphysical_ui_anchor')

    def bay(self,x,d,kind,index):
        self.begin('THStage_Bay_%02d_%s'%(index,kind))
        # All detailing stays inside the same 1.10 x .85 m validated footprint.
        if kind=='ArchiveDrawers':
            self.box('Cabinet',(x,d,.34),(1.02,.72,.62),3,.016)
            self.box('Top',(x,d,.67),(1.08,.78,.055),3,.012)
            for row in range(3):
                for side in (-1,1):
                    xx=x+side*.25;hh=.18+row*.18
                    self.box('Drawer',(xx,d-.371,hh),(.46,.025,.145),3,.006)
                    self.box('LabelFrame',(xx,d-.389,hh+.027),(.105,.012,.04),5,.002)
                    self.box('Label',(xx,d-.397,hh+.027),(.075,.005,.024),0,0)
                    self.box('Pull',(xx,d-.40,hh-.027),(.12,.025,.018),5,.003)
            self.box('MisfiledTray',(x+.16,d+.04,.715),(.48,.42,.035),2,.008,rotation=.08)
        elif kind=='EmptyVitrine':
            self.box('Base',(x,d,.20),(1.05,.76,.36),3,.015)
            self.box('Velvet',(x,d,.39),(.92,.62,.025),7,.004)
            for sx in (-1,1):
                for sd in (-1,1):self.box('Frame',(x+sx*.48,d+sd*.32,.55),(.035,.035,.33),5,.004)
            for sd in (-1,1):self.box('TopFrame',(x,d+sd*.32,.72),(1,.035,.035),5,.004)
            for sx in (-1,1):self.box('TopFrame',(x+sx*.48,d,.72),(.035,.67,.035),5,.004)
            self.box('EmptyMount',(x-.22,d,.43),(.23,.22,.055),1,.006)
            self.box('InventoryPlate',(x+.23,d-.35,.35),(.17,.015,.06),0,.002)
            # Missing glass is intentional: no transparent overdraw, no misleading barrier.
        elif kind=='BrokenDado':
            self.box('PlasterReturn',(x-.14,d+.11,.33),(.77,.32,.64),0,.013)
            self.panel(x-.14,d-.065,.25,.78,.40)
            self.box('BrokenTop',(x-.27,d+.11,.68),(.48,.34,.06),1,.008,rotation=.07)
            for i in range(3):self.box('ExposedBrick',(x+.28,d+.10,.14+i*.14),(.20,.31,.12),4,.013)
            self.box('FallenMoulding',(x,d-.29,.09),(.86,.11,.11),3,.006,rotation=.04)
            for i in range(3):self.rock('PlasterChip',(x-.32+i*.3,d-.24,.14),(.07,.08,.055),0)
        elif kind=='StorageBench':
            self.box('Bench',(x,d,.21),(1.02,.73,.38),3,.015)
            self.panel(x,d-.375,.22,.93,.26)
            self.box('PackingCase',(x-.19,d+.03,.54),(.57,.56,.28),2,.014)
            for sx in (-1,1):self.box('Strap',(x-.19+sx*.20,d+.03,.55),(.045,.58,.30),3,.006)
            self.box('ArchiveFolder',(x+.30,d-.04,.44),(.22,.42,.035),7,.003,rotation=-.06)
            self.box('ArchiveFolder',(x+.28,d-.04,.48),(.25,.40,.035),0,.003,rotation=.04)
        elif kind=='DisplayPlinth':
            self.box('PedestalFoot',(x-.14,d,.07),(.70,.69,.14),1,.02)
            self.box('Pedestal',(x-.14,d,.36),(.54,.53,.46),0,.015)
            for sd in (-1,1):self.box('Inset',(x-.14,d+sd*.272,.36),(.36,.018,.28),3,.005)
            self.box('Cornice',(x-.14,d,.62),(.71,.68,.07),1,.01)
            self.box('EmptyRedPad',(x-.14,d,.667),(.52,.47,.025),7,.003)
            self.box('FallenLabel',(x+.38,d-.12,.08),(.18,.24,.05),5,.006,rotation=.18)
        elif kind=='ServiceConsole':
            self.box('Equipment',(x,d,.34),(1.02,.72,.64),2,.025)
            self.box('ConsoleTop',(x,d,.69),(1.06,.76,.06),2,.015)
            self.box('Screen',(x-.19,d-.373,.48),(.43,.016,.23),3,.006,self.dark)
            for i in range(4):self.box('Readout',(x-.32+i*.09,d-.386,.48),(.045,.008,.013),0,.001,self.amber)
            for i in range(6):self.box('Vent',(x+.30,d-.373,.31+i*.045),(.23,.012,.018),3,.001,self.dark)
            self.box('LowerHatch',(x-.19,d-.373,.20),(.47,.018,.17),2,.006)
        self.finish()
        self.collider('Bay_%02d'%index,(x,d,.38),(1.10,.85,.76))

    def perimeter(self):
        self.begin('THStage_ArchiveWainscot')
        for side in (-1,1):
            for i in range(9):
                d=3.7+i*.79
                self.box('Skirting',(side*2.38,d,.10),(.20,.77,.20),3,.01)
                self.box('LowWall',(side*2.38,d,.35),(.17,.75,.45),0,.009)
                self.box('WoodPanel',(side*2.28,d,.29),(.025,.62,.30),3,.006)
                self.box('FadedInset',(side*2.262,d,.29),(.012,.51,.21),7,.002)
                self.box('DadoRail',(side*2.36,d,.59),(.22,.78,.045),3,.006)
            self.collider('SideSill_'+str(side),(side*2.375,6.86,.31),(.25,7.10,.62))
        self.finish()
        self.begin('THStage_BackArchiveCabinet')
        self.box('LongCabinet',(0,10.80,.33),(4.65,.32,.64),3,.012)
        self.box('StoneCounter',(0,10.79,.67),(4.72,.36,.055),1,.01)
        for i in range(8):
            x=-2.03+i*.58
            self.panel(x,10.62,.35,.53,.45)
            self.box('BrassPull',(x+.15,10.56,.36),(.025,.03,.08),5,.004)
        self.box('EmptyNiche',(0,10.78,.731),(.66,.30,.065),7,.004)
        self.collider('BackSill',(0,10.76,.385),(4.8,.44,.77));self.finish()
        self.begin('THStage_ExcavationFloorMarks')
        for side in (-1,1):
            for d in (3,5.4,7.65,10.2):self.box('RouteTab',(side*2.05,d,.004),(.15,.035,.008),6,0)
        self.finish()

def run():
    old=bpy.context.window.scene;selection=list(bpy.context.selected_objects);active=bpy.context.view_layer.objects.active;original=set(bpy.data.objects)
    try:
        b=ArchiveBuilder();b.lift()
        types=['ArchiveDrawers','EmptyVitrine','BrokenDado','StorageBench','DisplayPlinth','ServiceConsole']
        for i,kind in enumerate(types):b.bay(-1.6 if i%2==0 else 1.6,4.1+(i//2)*2.25,kind,i+1)
        b.perimeter();b.placement();b.preview_items();b.preview_setup();b.export()
        for view in ('overview','eye','top','lift'):b.render(view)
        return b
    finally:
        bpy.context.window.scene=old;bpy.ops.object.select_all(action='DESELECT')
        for obj in selection:obj.select_set(True)
        bpy.context.view_layer.objects.active=active
        assert original.issubset(set(bpy.data.objects))

if __name__=='__main__':stage_builder=run()
