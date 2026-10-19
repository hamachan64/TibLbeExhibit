"""Searchable ruins: actual roofed cavities and reverse-facing openings."""
import bpy,runpy,math,json
from pathlib import Path
HERE=Path(__file__).resolve().parent
v5=runpy.run_path(str(HERE/'build_stage_v5.py'),run_name='context5')
base=v5['base'];base.OUT=HERE/'Revision6'
HIDDEN={0,1,4,5,6,7,8,10,11,12,14,15,16,17,19,20,21,22}
TYPES=['amphora','mask','inscribed_stele','cup','amphora','inscribed_stele','hand_mirror','amphora','statuette','inscribed_stele','amphora','stamp_seal','coin_casket','inscribed_stele','scarab_amulet','ceremonial_lamp','amphora','cup','statuette','votive_bell','perfume_jar','scarab_amulet','ceremonial_lamp']

class SearchBuilder(v5['ContextBuilder']):
    def placement(self):
        super().placement()
        # More turning clearance before the rear opening.
        m=next(m for m in self.markers if m['name']=='TreasureSlot_23')
        m['position_unity_xyz'][2]=6.02;m['position_blender_xyz'][1]=-6.02
        next(o for o in self.runtime.objects if o.get('anchor_id')==m['name']).location.y=-6.02

    def preview_items(self):
        self.support_checks=[];self.find_records=[]
        candidates=[m for m in self.markers if m['role']=='treasure_candidate']
        for i,(name,slot) in enumerate(zip(TYPES,candidates)):
            x,_,d=slot['position_unity_xyz'];hidden=i in HIDDEN
            with bpy.data.libraries.load(str(base.TREASURES/(name+'.blend')),link=False) as (src,dst):dst.objects=src.objects
            objects=[o for o in dst.objects if o and o.type in ('MESH','EMPTY')]
            for o in objects:self.preview.objects.link(o);o['preview_only']=True
            root=next(o for o in objects if o.type=='EMPTY' and o.parent is None)
            # Local positive D is away from the entrance. Shelters open on their
            # reverse or east face, never straight toward the arrival sightline.
            opening='east' if i>=19 or i==17 else 'rear'
            root.rotation_euler.z=math.pi/2 if opening=='east' and hidden else 0 if hidden else math.pi
            root.location=(0,0,0);bpy.context.view_layer.update();bb=base.bounds(objects)
            width=bb[0][1]-bb[0][0];depth=bb[1][1]-bb[1][0];height=bb[2][1]-bb[2][0]
            h=.18 if d in (3.8,8.1,5.71) else .10
            suspended=.035 if name in ('mask','hand_mirror') and hidden else 0
            root.location=(x,-d,h+suspended-bb[2][0]);bpy.context.view_layer.update()
            item_bb=base.bounds(objects)
            w=max(.33,width+.11);length=max(.33,depth+.11)
            self.begin('THStage_FindSupport_%02d'%(i+1))
            self.box('BrokenStoneBase',(x,d,h/2),(w,length,h),1,.012)
            if hidden:
                top=min(.79,max(.57,item_bb[2][1]+.075));roof=.04;inner_top=top-roof
                assert inner_top-item_bb[2][1]>.025,(name,item_bb,top)
                # Closed front + roof genuinely occlude the full artifact and
                # glowing grip. Opening provides the hand and removal route.
                self.box('FrontAshlar',(x,d-length/2+.025,(h+top)/2),(w,.05,top-h),1,.009)
                if opening=='rear':
                    for side in (-1,1):self.box('Jamb',(x+side*(w/2-.025),d,(h+top)/2),(.05,length,top-h),4 if i%3==0 else 1,.009)
                else:
                    self.box('BackAshlar',(x,d+length/2-.025,(h+top)/2),(w,.05,top-h),1,.009)
                    self.box('WestJamb',(x-w/2+.025,d,(h+top)/2),(.05,length,top-h),1,.009)
                # Two adjoining stones form the overhead lintel; the seam does
                # not admit a top-down view. Front/back positions vary by site.
                self.box('OverhangingLintel',(x,d,top-roof/2),(w+.014,length+.014,roof),1,.006)
                self.box('CapFragment',(x-w*.18,d-length*.08,top+.008),(w*.52,length*.66,.016),4,.003)
                if name in ('mask','hand_mirror'):
                    self.box('WallPeg',(x,d-length/2+.05,h+suspended+height*.56),(.04,.09,.025),5,.003)
                kind='side_rubble_cavity' if opening=='east' else 'reverse_open_stone_locker'
                # Vary outside silhouette without blocking the open extraction
                # face or increasing the verified footprint.
                if i%3==0:self.box('ErodedCourse',(x,d-length/2-.008,h+.12),(w*.85,.024,.07),4,.009)
                discovery=[x+.68,d,1.0] if opening=='east' else [x,d+.68,1.0]
            else:
                kind='exposed_stone_find';top=h
                self.box('CrackedCap',(x,d,h+.012),(w,length,.024),4,.004)
                root.location.z+=.024;h+=.024
                discovery=[x,d-.65,1.65]
            support=self.finish();module=support['module_id']
            sb=base.bounds([support]);self.collider(module,((sb[0][0]+sb[0][1])/2,-(sb[1][0]+sb[1][1])/2,(sb[2][0]+sb[2][1])/2),(sb[0][1]-sb[0][0],sb[1][1]-sb[1][0],sb[2][1]-sb[2][0]))
            slot.update(position_unity_xyz=[x,root.location.z,d],position_blender_xyz=list(root.location),model=name,support_module=module,placement_kind=kind,
                        support_surface_height_m=h,rotation_blender_xyz=list(root.rotation_euler),hidden_intent=hidden,opening=opening if hidden else 'top',
                        discovery_eye_unity_xyz=[discovery[0],discovery[2],discovery[1]],allowed_classes=['M' if name in ('amphora','inscribed_stele') else 'S'])
            marker=next(o for o in self.runtime.objects if o.get('anchor_id')==slot['name']);marker.location=root.location
            marker['hidden_intent']=hidden;marker['placement_kind']=kind;marker['support_module']=module
            self.sample_placements.append(dict(model=name,slot=slot['name'],class_name=slot['allowed_classes'][0],root_name=root.name))
            self.preview_treasures.extend(objects);bpy.context.view_layer.update();actual=base.bounds(objects)
            self.support_checks.append(dict(slot=slot['name'],model=name,kind=kind,base_height_m=actual[2][0],top_height_m=actual[2][1],support_height_m=h,suspended_offset_m=suspended))
            self.find_records.append(dict(slot=slot['name'],objects=objects,root=root,hidden=hidden,discovery=discovery))

    def export(self):
        super().export()
        p=base.OUT/'stage_layout.json';data=json.loads(p.read_text(encoding='utf-8'))
        data['revision']=6;data['search_design']={'hidden_target_count':18,'exposed_cues':5,'mechanism':'Opaque lintels and closed approach faces; enter the side/reverse sightline to discover contents.'}
        p.write_text(json.dumps(data,indent=2,ensure_ascii=False),encoding='utf-8')

def run():
    old=bpy.context.window.scene;selected=list(bpy.context.selected_objects);active=bpy.context.view_layer.objects.active;original=set(bpy.data.objects)
    try:
        b=SearchBuilder();b.lift();b.maze();b.placement();b.preview_items();b.cart_preview();b.preview_setup();b.export();bpy.app.driver_namespace['TH_Search_Stage']=b
        return b
    finally:
        bpy.context.window.scene=old;bpy.ops.object.select_all(action='DESELECT')
        for o in selected:o.select_set(True)
        bpy.context.view_layer.objects.active=active
        assert original.issubset(set(bpy.data.objects))
if __name__=='__main__':run()
