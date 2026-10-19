"""Showcase render of the added S finds, preserving active scene and selection.
Stage previews are rendered by Stage/render_search_views.py."""
import bpy,math
from pathlib import Path
from mathutils import Vector
HERE=Path(__file__).resolve().parent
def run():
    old=bpy.context.window.scene;selected=list(bpy.context.selected_objects);active=bpy.context.view_layer.objects.active
    try:
        # Detail view of the original small additions, at their true relative sizes.
        scene=bpy.data.scenes.new('TH_New_S_Find_Showcase');bpy.context.window.scene=scene
        scene.world=bpy.data.worlds.new('TH_New_S_World');scene.world.use_nodes=True
        bg=next(n for n in scene.world.node_tree.nodes if n.type=='BACKGROUND')
        bg.inputs['Color'].default_value=(.12,.15,.17,1);bg.inputs['Strength'].default_value=.45
        scene.render.engine='CYCLES';scene.cycles.samples=32
        for index,name in enumerate(('hand_mirror','votive_bell','perfume_jar','stamp_seal','coin_casket')):
            with bpy.data.libraries.load(str(HERE/'ContextReviewed'/(name+'.blend')),link=False) as (src,dst):dst.objects=src.objects
            objects=[o for o in dst.objects if o]
            for o in objects:scene.collection.objects.link(o)
            root=next(o for o in objects if o.type=='EMPTY' and o.parent is None);root.location=((index-2)*.37,0,0)
        for name,pos,power,size in [('Key',(0,-2,3),180,3),('Rim',(1,1,2),100,2)]:
            data=bpy.data.lights.new(name,'AREA');data.energy=power;data.size=size;o=bpy.data.objects.new(name,data);scene.collection.objects.link(o);o.location=pos;o.rotation_euler=(Vector((0,0,.15))-o.location).to_track_quat('-Z','Y').to_euler()
        data=bpy.data.cameras.new('FindCamera');o=bpy.data.objects.new('FindCamera',data);scene.collection.objects.link(o);o.location=(.75,-3,1.6);o.rotation_euler=(Vector((0,0,.17))-o.location).to_track_quat('-Z','Y').to_euler();data.type='ORTHO';data.ortho_scale=2.05;scene.camera=o
        scene.render.resolution_x=1700;scene.render.resolution_y=750;scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG';scene.render.filepath=str(HERE/'ContextReviewed/new_s.png')
        bpy.ops.render.render(write_still=True,scene=scene.name)
        bpy.data.libraries.write(str(HERE/'ContextReviewed/New_S_Showcase.blend'),{scene},fake_user=True,compress=True)
    finally:
        bpy.context.window.scene=old;bpy.ops.object.select_all(action='DESELECT')
        for o in selected:o.select_set(True)
        bpy.context.view_layer.objects.active=active
if __name__=='__main__':run()
