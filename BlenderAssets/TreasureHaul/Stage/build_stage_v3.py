"""Original low-walled maze and roll-in extraction bay. No external art assets."""
import bpy, importlib.util, json, math
from pathlib import Path
HERE=Path(__file__).resolve().parent
spec=importlib.util.spec_from_file_location('archive_v2',HERE/'build_stage_v2.py')
v2=importlib.util.module_from_spec(spec);spec.loader.exec_module(v2)
base=v2.base
base.OUT=HERE/'Revision3'

class MazeBuilder(v2.ArchiveBuilder):
    def wall(self,name,x,d,w,length,style=0):
        self.begin('THStage_'+name)
        horizontal=w>length
        self.box('Plaster',(x,d,.40),(w-.025,length-.025,.59),0,.008)
        self.box('Skirting',(x,d,.10),(w,length,.20),3,.008)
        self.box('Cap',(x,d,.715),(w,length,.07),3,.01)
        count=max(1,int(max(w,length)/.64))
        span=max(w,length)/count
        for i in range(count):
            offset=(i-(count-1)/2)*span
            for sign in (-1,1):
                if horizontal:
                    p=(x+offset,d+sign*(length/2-.006),.33);size=(span-.055,.012,.32)
                else:
                    p=(x+sign*(w/2-.006),d+offset,.33);size=(.012,span-.055,.32)
                self.box('Inset',p,size,7 if (i+style)%4 else 3,.002)
                # Vertical pilasters break up long walls without increasing footprint.
                if horizontal:
                    self.box('Stile',(x+offset-span/2+.022,d+sign*(length/2-.008),.36),(.032,.016,.56),3,.003)
                else:
                    self.box('Stile',(x+sign*(w/2-.008),d+offset-span/2+.022,.36),(.016,.032,.56),3,.003)
        self.finish();self.collider(name,(x,d,.375),(w,length,.75))

    def maze(self):
        self.wall('WestBoundary',-2.41,6.9,.18,8.2)
        self.wall('EastBoundary',2.41,6.9,.18,8.2,1)
        self.wall('BackBoundary',0,10.91,4.64,.18,2)
        # Opposite openings: clear width 1.70m each. No straight center corridor.
        self.wall('EntryPartition',-.85,3.8,2.94,.26,1)
        self.wall('RearPartition',.85,8.1,2.94,.26,2)
        self.wall('RearRoomDivider',0,10.36,.26,.92,1)
        # L-shaped partition: distinct approaches, a recessed pocket, and two routes.
        # Move the rear partition 20cm; retain the receiver's maneuvering area.
        self.wall('CentralCrosswall',0,5.71,1.20,.26,2)
        self.wall('CentralReturn',.47,6.06,.26,.44,1)
        # Labels and inset drawers are built into partitions, not loose obstacles.
        self.begin('THStage_ArchiveDetails')
        for x,d in [(-1.2,3.8),(.2,8.1),(1.4,8.1)]:
            for row in range(3):
                h=.23+row*.13
                self.box('DrawerFace',(x,d-.139,h),(.50,.018,.105),3,.003)
                self.box('Label',(x-.12,d-.152,h),(.09,.006,.035),0,.001)
                self.box('BrassPull',(x+.09,d-.16,h),(.10,.018,.018),5,.003)
            # Include all projections in the clearance envelope.
            self.collider('DrawerFront_'+str(x),(x,d-.155,.37),(.52,.04,.43))
        for x,d in [(.51,3.8),(-.51,8.1)]:
            self.box('DoorJambCap',(x,d,.765),(.18,.26,.025),1,.004)
            self.box('RoomPlaque',(x,d-.138,.56),(.11,.012,.085),5,.002)
        self.finish()

    def lift(self):
        self.begin('THStage_ExtractionBay')
        # Local receiver floor only. Front threshold 6mm; no raised loading bench.
        self.box('ReceiverFloor',(0,1.20,.012),(2.30,1.56,.024),2,.004)
        for x in (-1.12,1.12):
            self.box('FloorGuide',(x,1.20,.027),(.025,1.50,.006),0,0,self.teal)
        self.box('EntryThreshold',(0,2.03,.003),(2.30,.10,.006),2,0)
        self.box('BackMachine',(0,.30,.36),(3,.24,.72),2,.025)
        for sign in (-1,1):
            self.box('SideReceiver',(sign*1.34,1.20,.20),(.30,1.52,.40),2,.02)
            self.box('TopRail',(sign*1.34,1.20,.42),(.30,1.52,.04),2,.008)
            for i in range(8):
                self.box('WarningMark',(sign*1.34,.55+i*.17,.446),(.22,.07,.008),6,0,rotation=.18)
            self.collider('ReceiverSide_'+str(sign),(sign*1.34,1.20,.225),(.30,1.52,.45))
        self.box('MeterHousing',(0,.448,.56),(1.18,.08,.31),2,.015)
        self.box('MeterWindow',(0,.494,.57),(1.02,.014,.19),3,.004,self.dark)
        for i in range(5):self.box('MeterPlaceholder',(-.36+i*.18,.505,.57),(.085,.008,.025),0,.001,self.teal)
        self.box('Actuator',(0,.46,.24),(.54,.08,.18),2,.01)
        self.box('IntakeSlot',(0,.507,.24),(.40,.012,.055),3,.001,self.dark)
        for x in (-1.1,1.1):
            for i in range(5):self.box('Vent',(x,.428,.27+i*.06),(.38,.014,.022),3,.002,self.dark)
        self.box('ControlPod',(-1.34,1.82,.50),(.30,.28,.52),2,.018)
        self.box('ControlFace',(-1.34,1.97,.59),(.24,.02,.25),3,.003,self.dark)
        self.cylinder('Button',(-1.34,1.989,.60),.065,.018,vertices=12,rotation=(math.pi/2,0,0),mat=self.amber)
        self.finish()
        self.collider('ReceiverBack',(0,.35,.36),(3,.34,.72))
        self.collider('ControlPod',(-1.34,1.85,.39),(.32,.32,.78))
        self.marker('CollectionVolume',(0,1.20,.49),'collection_volume',size=[2.30,.92,1.56])
        self.marker('ScoreBoardAnchor',(0,.32,1.5),'nonphysical_ui_anchor')
        self.marker('CartDock',(0,1.20,.024),'cart_dock',preview_only=True)

    def placement(self):
        for i,x in enumerate((-1.2,-.4,.4,1.2)):
            self.marker('Tutorial_%02d'%(i+1),(x,2.5,0),'tutorial')
        # Room pockets, not a straight row down the old central aisle.
        coords=[(x,d) for d in (4.4,5.0,6.65,7.25) for x in (-1.5,-.85,-.2)]
        coords += [(x,d) for d in (4.4,5.0,6.60,7.25) for x in (.75,1.40)]
        coords += [(x,d) for d in (8.55,9.20,9.85) for x in (-1.5,-.85,.75,1.40)]
        # Cull against every conservative fixed envelope; retain 28 spaced candidates.
        kept=[]
        for x,d in coords:
            if not all(clear(x,d,.27,c) for c in self.colliders):continue
            if any(math.hypot(x-a,d-b)<.55 for a,b in kept):continue
            kept.append((x,d))
        assert len(kept)>=28,len(kept)
        # Distribute near/middle/rear rather than discarding the back of the list.
        kept=kept[:20]+kept[-8:]
        assert len(set(kept))==28
        for i,(x,d) in enumerate(kept):
            self.marker('TreasureSlot_%02d'%(i+1),(x,d,0),'treasure_candidate',clearance_radius_m=.27,
                        allowed_classes=['S','M'],distance_band='near' if d<5.5 else 'mid' if d<8 else 'far')

    def cart_preview(self):
        path=HERE.parent/'Cart/Revision2/ExcavationCart.blend'
        with bpy.data.libraries.load(str(path),link=False) as (src,dst):dst.objects=src.objects
        objects=[o for o in dst.objects if o and o.type in ('MESH','EMPTY')]
        for o in objects:
            self.preview.objects.link(o);o['preview_only']=True
            if 'module_id' in o:del o['module_id']
        roots=[o for o in objects if o.parent is None]
        assert len(roots)==1
        roots[0].location=(0,-1.20,.024);roots[0].rotation_euler.z=math.pi
        self.preview_cart=objects

    def export(self):
        super().export()
        p=base.OUT/'stage_layout.json';data=json.loads(p.read_text(encoding='utf-8'))
        data.pop('central_route')
        data['revision']=3
        data['navigation']={'type':'offset doors, central L-shaped partition and loop, two rear room pockets',
            'door_clear_width_m':1.70,'cart_dimensions_m':[.816,1.204],
            'cart_clearance_proxy_radius_m':.80,'maze_is_visual_and_cargo_geometry_only':True,
            'human_collision_or_forced_locomotion_implemented':False}
        data['preview_cart_stats']=base.stats(self.preview_cart)
        data['preview_only']='floor, 23 treasures, existing cart, camera, lights; not in stage FBX'
        data['collection_change']='0.024m receiver floor, frontal roll-in; no scoring or interaction implementation'
        p.write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8')
        write_plan(data)

def clear(x,d,r,c):
    cx,h,cd=c['center_unity_xyz'];w,hh,dd=c['size_unity_xyz']
    return math.hypot(max(abs(x-cx)-w/2,0),max(abs(d-cd)-dd/2,0))>=r-1e-7

def write_plan(data):
    scale=66;ox=220;oy=820
    def pt(x,d):return ox+x*scale,oy-d*scale
    parts=['<svg xmlns="http://www.w3.org/2000/svg" width="980" height="900" viewBox="0 0 980 900">',
           '<rect width="980" height="900" fill="#19282b"/>','<g font-family="sans-serif" fill="#e9e9df">',
           '<text x="50" y="40" font-size="25">TREASURE HAUL / MAZE + EXTRACTION BAY</text>']
    def rect(x,d,w,l,color):
        a,b=pt(x-w/2,d+l/2);parts.append(f'<rect x="{a}" y="{b}" width="{w*scale}" height="{l*scale}" fill="{color}"/>')
    rect(0,5.5,5,11,'#314246');rect(0,1.20,2.3,1.56,'#347d77')
    for c in data['colliders']:
        if c['name']=='VenueFloor':continue
        x,h,d=c['center_unity_xyz'];w,hh,l=c['size_unity_xyz'];rect(x,d,w,l,'#9e8b6c')
    for m in data['markers']:
        if m['role'] not in ('tutorial','treasure_candidate'):continue
        x,h,d=m['position_unity_xyz'];a,b=pt(x,d)
        parts.append(f'<circle cx="{a}" cy="{b}" r="5" fill="'+('#e9c16e' if m['role']=='treasure_candidate' else '#83b8dd')+'"/>')
    routes=[[(0,2.5),(1.47,2.9),(1.47,4.75),(1.45,7.1),(-1.47,7.1),(-1.47,9.05),(-1.15,9.85)],
            [(1.47,4.75),(-1.45,4.75),(-1.45,7.1)], [(-1.47,9.05),(1.15,9.05),(1.15,9.85)]]
    for route in routes:
        points=' '.join('%g,%g'%pt(x,d) for x,d in route)
        parts.append(f'<polyline points="{points}" fill="none" stroke="#76c7bb" stroke-width="3" stroke-dasharray="7 5"/>')
    notes=['5 m x 11 m / fixed original layout','Low partitions: max 0.78 m','A: right opening, 1.70 m clear','B: left opening, 1.70 m clear',
           'Central L-wall: two paths + recessed pocket','Rear rooms: short exploration branches','Receiver: floor + side machines + console','Existing cart shown in review only','Gold: 28 treasure candidate positions',
           'Blue: 4 tutorial positions','Dashed routes: schematic, not runtime AI','See verification.json for measured paths',
           'No walls above waist / no ceiling','MR venue floor remains visible','Unity integration and device testing pending']
    for i,note in enumerate(notes):parts.append(f'<text x="425" y="{115+i*39}" font-size="17">{note}</text>')
    parts.extend(['<text x="220" y="855" text-anchor="middle" font-size="18">START / EXTRACTION</text>','</g></svg>'])
    (base.OUT/'floorplan.svg').write_text('\n'.join(parts),encoding='utf-8')

def run():
    old=bpy.context.window.scene;selected=list(bpy.context.selected_objects);active=bpy.context.view_layer.objects.active;original=set(bpy.data.objects)
    try:
        b=MazeBuilder();b.lift();b.maze();b.placement();b.preview_items();b.cart_preview();b.preview_setup();b.export()
        for view in ('overview','top','eye','lift'):b.render(view)
        return b
    finally:
        bpy.context.window.scene=old;bpy.ops.object.select_all(action='DESELECT')
        for obj in selected:obj.select_set(True)
        bpy.context.view_layer.objects.active=active
        assert original.issubset(set(bpy.data.objects))

if __name__=='__main__':stage_builder=run()
