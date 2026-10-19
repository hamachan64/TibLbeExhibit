"""Contextual treasure supports and original finds, without floor spawns."""
import bpy, importlib.util, math, json
from pathlib import Path
from mathutils import Vector
HERE=Path(__file__).resolve().parent
spec=importlib.util.spec_from_file_location('ruins4',HERE/'build_stage_v4.py');v4=importlib.util.module_from_spec(spec);spec.loader.exec_module(v4)
base=v4.base;base.OUT=HERE/'Revision5';base.TREASURES=HERE.parent/'ContextReviewed'

class ContextBuilder(v4.RuinBuilder):
    def wall(self,name,x,d,w,length,style=0):
        if name not in ('EntryPartition','RearPartition','CentralCrosswall'):
            return super().wall(name,x,d,w,length,style)
        self.wall_specs.append((name,x,d,w,length));self.begin('THStage_'+name)
        count=max(1,round(w/.42));span=w/count
        for i in range(count):self.box('AltarFoundation',(x+(i-(count-1)/2)*span,d,.08),(span-.009,length,.16),1,.014)
        self.finish();self.collider(name,(x,d,.08),(w,length,.16))

    def placement(self):
        for i,x in enumerate((-1.2,-.4,.4,1.2)):self.marker('Tutorial_%02d'%(i+1),(x,2.5,0),'tutorial')
        coords=[(x,3.8) for x in (-1.5,-1.05,-.6,-.15,.3)]
        coords += [(x,8.1) for x in (-.3,.15,.6,1.05,1.5)]
        coords += [(x,9.9) for x in (-1.5,-1,-.5,0,.5,1,1.5)]
        coords += [(-.36,5.71),(.18,5.71)]
        coords += [(-1.5,d) for d in (4.8,5.25,5.7,6.15)]
        for i,(x,d) in enumerate(coords):self.marker('TreasureSlot_%02d'%(i+1),(x,d,0),'treasure_candidate',clearance_radius_m=.25,allowed_classes=['S','M'])

    def preview_items(self):
        candidates=[m for m in self.markers if m['role']=='treasure_candidate']
        types=['amphora','mask','inscribed_stele','cup','amphora',
               'inscribed_stele','hand_mirror','amphora','statuette','inscribed_stele',
               'amphora','stamp_seal','coin_casket','inscribed_stele','scarab_amulet','ceremonial_lamp','amphora',
               'cup','statuette','votive_bell','perfume_jar','scarab_amulet','ceremonial_lamp']
        ids=range(23)
        self.support_checks=[]
        for index,(name,slotid) in enumerate(zip(types,ids)):
            slot=candidates[slotid];x,_,d=slot['position_unity_xyz']
            with bpy.data.libraries.load(str(base.TREASURES/(name+'.blend')),link=False) as (src,dst):dst.objects=src.objects
            objects=[o for o in dst.objects if o and o.type in ('MESH','EMPTY')]
            for o in objects:self.preview.objects.link(o);o['preview_only']=True
            root=next(o for o in objects if o.parent is None and o.type=='EMPTY')
            # All front faces look toward entrance. No random facing away from user.
            root.rotation_euler.z=math.pi;root.location=(0,0,0);bpy.context.view_layer.update()
            bounds=base.bounds(objects);w=bounds[0][1]-bounds[0][0];depth=bounds[1][1]-bounds[1][0];height=bounds[2][1]-bounds[2][0]
            support_h=min(.22+(index%3)*.025,max(.08,.76-height));support_w=max(.25,w+.045);support_d=max(.24,depth+.045)
            kind='stone_plinth' if name in ('amphora','inscribed_stele','statuette','cup') else 'broken_altar_cache'
            if name in ('mask','hand_mirror'):kind='wall_mount'
            self.begin('THStage_FindSupport_%02d'%(index+1))
            self.box('Foundation',(x,d,(support_h-.028)/2),(support_w,support_d,support_h-.028),1,.018)
            self.box('WornCap',(x,d,support_h-.014),(support_w+.015,support_d+.015,.028),4,.007)
            # Offset chips connect supports to the existing ruined-stone vocabulary.
            for side in (-1,1):self.rock('ErodedFoot',(x+side*support_w*.37,d+support_d*.22,.041),(.045,.045,.035),1)
            roof_h=None
            if kind=='broken_altar_cache':
                roof_h=min(.74,support_h+height+.065)
                back=d+support_d/2-.018
                self.box('BackFragment',(x,back,(support_h+roof_h)/2),(support_w,.035,roof_h-support_h),1,.009)
                # One side fragment hides the lower silhouette; open front and top
                # leave the single grip reachable and visible at close range.
                self.box('SideFragment',(x-support_w/2+.018,d+.025,support_h+.055),(.035,support_d-.05,.11),4,.008)
                self.box('BrokenFrontLip',(x-support_w*.12,d-support_d/2+.012,support_h+.045),(support_w*.70,.024,.09),1,.006)
            elif kind=='wall_mount':
                back=d+support_d/2+.035
                self.box('StandingWallFragment',(x,back,.34),(support_w+.04,.075,.68),1,.013)
                # The bracket touches the artifact back; it does not clamp the grip.
                bracket_front=d-bounds[1][0]-.004
                bracket_back=back+.015
                self.box('BronzeBracket',(x,(bracket_front+bracket_back)/2,support_h+.05+height*.60),(.045,bracket_back-bracket_front,.026),5,.004)
            support=self.finish()
            # Exact grounded pose: base bounds (rather than arbitrary root offset).
            suspended=.05 if kind=='wall_mount' else 0
            root.location=(x,-d,support_h+suspended-bounds[2][0]);bpy.context.view_layer.update()
            actual=base.bounds(objects)
            assert abs(actual[2][0]-support_h-suspended)<1e-5
            slot['position_unity_xyz']=[x,root.location.z,d];slot['position_blender_xyz']=list(root.location)
            slot.update(support_module=support.name,placement_kind=kind,model=name,rotation_blender_xyz=list(root.rotation_euler),support_surface_height_m=support_h)
            slot['allowed_classes']=['M' if name in ('amphora','inscribed_stele') else 'S']
            marker=next(o for o in self.runtime.objects if o.get('anchor_id')==slot['name']);marker.location=root.location
            marker['placement_kind']=kind;marker['support_module']=support.name
            bb=base.bounds([support]);self.collider(support.name,((bb[0][0]+bb[0][1])/2,-(bb[1][0]+bb[1][1])/2,(bb[2][0]+bb[2][1])/2),(bb[0][1]-bb[0][0],bb[1][1]-bb[1][0],bb[2][1]-bb[2][0]))
            self.support_checks.append(dict(slot=slot['name'],model=name,kind=kind,base_height_m=actual[2][0],top_height_m=actual[2][1],support_height_m=support_h,suspended_offset_m=suspended))
            self.sample_placements.append(dict(model=name,slot=slot['name'],class_name='M' if name in ('amphora','inscribed_stele') else 'S',root_name=root.name))
            self.preview_treasures.extend(objects)
        # Unbuilt candidates must never be used as unsupported fallback spawns.
        used={p['slot'] for p in self.sample_placements}
        for m in candidates:
            if m['name'] not in used:
                m['role']='reserved_disabled';m['enabled']=False
                next(o for o in self.runtime.objects if o.get('anchor_id')==m['name'])['role']='reserved_disabled'

    def export(self):
        super().export()
        p=base.OUT/'stage_layout.json';data=json.loads(p.read_text(encoding='utf-8'))
        data.update(revision=5,supported_placements=self.support_checks,
                    spawn_rule='23 authored model-specific supported anchors. Do not randomly swap incompatible shapes.',
                    navigation_status='Recheck with added support solids; previous revision cart paths are not a guarantee.')
        data['navigation']['type']='Low offering altars, central ruin, west caches and rear offerings; cart delivery route via offset openings'
        p.write_text(json.dumps(data,indent=2,ensure_ascii=False),encoding='utf-8')

def run():
    old=bpy.context.window.scene;selected=list(bpy.context.selected_objects);active=bpy.context.view_layer.objects.active;original=set(bpy.data.objects)
    try:
        b=ContextBuilder();b.lift();b.maze();b.placement();b.preview_items();b.cart_preview();b.preview_setup();b.export()
        return b
    finally:
        bpy.context.window.scene=old;bpy.ops.object.select_all(action='DESELECT')
        for o in selected:o.select_set(True)
        bpy.context.view_layer.objects.active=active
        assert original.issubset(set(bpy.data.objects))

if __name__=='__main__':stage_builder=run()
