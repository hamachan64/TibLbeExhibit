"""Original weathered ruins; same maze clearance, local ground fragments only."""
import bpy, importlib.util, math, random, json
from pathlib import Path
HERE=Path(__file__).resolve().parent
spec=importlib.util.spec_from_file_location('maze_v3',HERE/'build_stage_v3.py')
v3=importlib.util.module_from_spec(spec);spec.loader.exec_module(v3)
base=v3.base;base.OUT=HERE/'Revision4'

class RuinBuilder(v3.MazeBuilder):
    def make_materials(self):
        super().make_materials()
        colors=[(.42,.38,.30),(.28,.265,.22),(.10,.145,.135),(.10,.052,.032),(.29,.25,.19),(.37,.29,.13),(.49,.32,.08),(.16,.125,.085)]
        rng=base.np.random.default_rng(4019);yy,xx=base.np.mgrid[0:128,0:128]
        for node in self.surface.node_tree.nodes:
            if node.type!='TEX_IMAGE':continue
            im=node.image;data=base.np.array(im.pixels[:],dtype=base.np.float32).reshape(128,1024,4)
            for tile in (0,1,4,7):
                region=data[:,tile*128:(tile+1)*128,:3]
                if 'BaseColor' in im.name:
                    coarse=base.np.repeat(base.np.repeat(rng.random((32,32)),4,0),4,1)
                    coarse=(coarse+base.np.roll(coarse,2,0)+base.np.roll(coarse,2,1))/3
                    value=.76+.19*coarse+.09*rng.random((128,128))
                    crack=abs(xx-(55+11*base.np.sin(yy*.09)+yy*.2))<.6
                    value[crack]*=.65
                    value[yy<18]*=.72
                    region[:]=base.np.array(colors[tile])*value[...,None]
                elif 'Metallic' in im.name:region[:]=0
                else:region[:]=.94
            im.pixels.foreach_set(data.ravel());im.save();im.pack()
        self.ruin_rng=random.Random(4019);self.wall_specs=[]

    def patch(self,label,vertices,tile):
        # Convex chipped patch, normal points out of the visible surface.
        mesh=bpy.data.meshes.new(label)
        mesh.from_pydata(vertices,[],[tuple(range(len(vertices)))]);mesh.update()
        obj=bpy.data.objects.new(label,mesh);self.runtime.objects.link(obj)
        return self.own(obj,label,tile)

    def wall(self,name,x,d,w,length,style=0):
        self.wall_specs.append((name,x,d,w,length))
        self.begin('THStage_'+name)
        rng=self.ruin_rng;horizontal=w>length;long=max(w,length);thin=min(w,length)
        # Dark recessed mortar; staggered individual blocks remain inside old bounds.
        self.box('Mortar',(x,d,.215),(w-.028,length-.028,.41),1,.006)
        for row in range(3):
            count=max(1,round(long/.46)+(row%2));span=long/count
            for i in range(count):
                a=(i-(count-1)/2)*span
                height=rng.uniform(.07,.23) if row==2 else rng.uniform(.19,.22)
                if row==2 and i%5==2:continue
                h=.02+row*.23+height/2
                size=(span-.012,thin-.006,height) if horizontal else (thin-.006,span-.012,height)
                pos=(x+a,d,h) if horizontal else (x,d+a,h)
                obj=self.box('ChippedStone',pos,size,1 if rng.random()<.65 else 4,.012)
                # Asymmetric erosion of the top corners; no point expands the bounds.
                for vertex in obj.data.vertices:
                    if vertex.co.z>0:
                        vertex.co.z-=rng.uniform(0,.025)
                        vertex.co.x*=rng.uniform(.97,1);vertex.co.y*=rng.uniform(.97,1)
        # Broken plaster islands, irregular outlines, not a continuous neat panel.
        for i in range(max(1,int(long/.75))):
            a=(i+.5)*long/max(1,int(long/.75))-long/2
            width=min(.48,long*.65)*rng.uniform(.65,1);height=rng.uniform(.15,.31)
            center=.34+rng.uniform(-.06,.05)
            outline=[(-.5,-.3),(-.30,-.5),(.23,-.43),(.5,-.18),(.39,.37),(.04,.5),(-.46,.22)]
            for side in (-1,1):
                if horizontal:verts=[(x+a+u*width,-(d+side*(thin/2-.001)),center+v*height) for u,v in outline]
                else:verts=[(x+side*(thin/2-.001),-(d+a+u*width),center+v*height) for u,v in outline]
                if (horizontal and side<0) or (not horizontal and side>0):verts.reverse()
                self.patch('PeelingPlaster',verts,0)
        self.finish();self.collider(name,(x,d,.375),(w,length,.75))

    def maze(self):
        for args in [('WestBoundary',-2.41,6.9,.18,8.2),('EastBoundary',2.41,6.9,.18,8.2),
                     ('BackBoundary',0,10.91,4.64,.18),('EntryPartition',-.85,3.8,2.94,.26),
                     ('RearPartition',.85,8.1,2.94,.26),('RearRoomDivider',0,10.36,.26,.92),
                     ('CentralCrosswall',0,5.71,1.20,.26),('CentralReturn',.47,6.06,.26,.44)]:self.wall(*args)
        self.ground()

    def ground(self):
        self.begin('THStage_RuinGroundFragments');rng=self.ruin_rng;patch_index=0
        # Flat fractured ground patches along wall feet. No raised rubble in routes.
        # All patches are opaque small polygons, not a full floor or alpha layers.
        for name,x,d,w,length in self.wall_specs:
            horizontal=w>length;long=max(w,length);thin=min(w,length)
            count=max(1,round(long/.48));span=long/count
            for i in range(count):
                a=(i+.5)*span-long/2
                for side in (-1,1):
                    if rng.random()<.18:continue
                    cx=x+a if horizontal else x+side*(thin/2+.07)
                    cd=d+side*(thin/2+.07) if horizontal else d+a
                    if not (-2.46<cx<2.46 and .1<cd<10.96):continue
                    patch_index+=1
                    sx=span*rng.uniform(.6,1.1) if horizontal else rng.uniform(.17,.40)
                    sd=rng.uniform(.17,.40) if horizontal else span*rng.uniform(.6,1.1)
                    verts=[]
                    for k in range(8):
                        ang=2*math.pi*k/8;rr=rng.uniform(.78,1)
                        px=max(-2.495,min(2.495,cx+math.cos(ang)*sx*rr))
                        pd=max(.005,min(10.995,cd+math.sin(ang)*sd*rr))
                        verts.append((px,-pd,.001+patch_index*.000002))
                    self.patch('SoilApron',list(reversed(verts)),7)
                    if rng.random()<.8:
                        # A separate small shard at a slightly higher planar level.
                        stone=[(cx-sx*.45,-(cd-sd*.30),.003),(cx+sx*.32,-(cd-sd*.35),.003),
                               (cx+sx*.43,-(cd+sd*.13),.003),(cx+sx*.12,-(cd+sd*.34),.003),
                               (cx-sx*.42,-(cd+sd*.20),.003)]
                        stone=[(max(-2.495,min(2.495,px)),max(-10.995,min(-.005,py)),z+patch_index*.000002) for px,py,z in stone]
                        self.patch('BrokenPaving',list(reversed(stone)),1 if rng.random()<.7 else 4)
        obj=self.finish();obj['ground_decoration_only']=True;obj['do_not_add_collider']=True
        obj.visible_shadow=False;obj['disable_shadow_casting_in_unity']=True

    def export(self):
        super().export()
        p=base.OUT/'stage_layout.json';data=json.loads(p.read_text(encoding='utf-8'))
        data['revision']=4
        data['ground_decoration']={'max_height_m':.004,'collider':False,'cast_shadows':False,'full_floor':False,
            'description':'Local opaque soil and paving fragments at wall feet; keep passthrough in open paths.'}
        p.write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8')
        # Parent's drawing helper writes through the same base output directory.

def run():
    old=bpy.context.window.scene;selection=list(bpy.context.selected_objects);active=bpy.context.view_layer.objects.active;original=set(bpy.data.objects)
    try:
        b=RuinBuilder();b.lift();b.maze();b.placement();b.preview_items();b.cart_preview();b.preview_setup();b.export()
        for view in ('overview','eye','top','lift'):b.render(view)
        return b
    finally:
        bpy.context.window.scene=old;bpy.ops.object.select_all(action='DESELECT')
        for o in selection:o.select_set(True)
        bpy.context.view_layer.objects.active=active
        assert original.issubset(set(bpy.data.objects))

if __name__=='__main__':stage_builder=run()
