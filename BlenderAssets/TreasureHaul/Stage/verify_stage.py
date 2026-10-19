"""Reload Stage/Output FBX, modules and blends, and re-check the layout. Execute through Blender MCP.

Run check_search_visibility.py (headless) first; this reads its search_visibility.json
and runs check_context_paths.py. Writes Output/verification.json.
"""
import bpy, json, runpy, sys
from pathlib import Path
from mathutils import Vector
HERE = Path(__file__).resolve().parent
OUT = HERE / 'Output'


def inspect(objects):
    meshes = [o for o in objects if o.type == 'MESH']
    for o in meshes: o.data.calc_loop_triangles()
    assert all(o.data.uv_layers for o in meshes), 'Missing UVs'
    assert all(t.area > 1e-12 for o in meshes for t in o.data.loop_triangles), 'Degenerate face'
    pts = [o.matrix_world @ v.co for o in meshes for v in o.data.vertices]
    assert all(-2.5001 <= p.x <= 2.5001 and -11.0001 <= p.y <= .0001 and -.0001 <= p.z < .8 for p in pts), 'Venue/height bounds'
    return dict(triangles=sum(len(o.data.loop_triangles) for o in meshes), meshes=len(meshes),
                max_height_m=max(p.z for p in pts), material_slots=sum(len(o.data.materials) for o in meshes))


def layout_checks(data):
    argv = sys.argv; sys.argv = ['check_context_paths.py', '--', OUT.name]
    try: paths = runpy.run_path(str(HERE / 'check_context_paths.py'), run_name='verify')['run']()
    finally: sys.argv = argv
    assert paths['human_all_reachable'] and paths['cart_gap_to_rear_row_m'] <= .10
    assert len(data['sample_placements']) == 23
    assert sum(x['class_name'] == 'S' for x in data['sample_placements']) == 14
    for p in data['supported_placements']:
        assert p['base_height_m'] >= .1 - 1e-4 and p['top_height_m'] < .8, p  # raised on stone, never on the floor
        assert abs(p['base_height_m'] - p['support_height_m'] - p['suspended_offset_m']) < .001, p
    for m in data['markers']:
        if m['role'] == 'treasure_candidate':
            x, h, d = m['position_unity_xyz']; assert min(2.5 - abs(x), d, 11 - d) - m['clearance_radius_m'] >= .7, m['name']
    training = [m for m in data['markers'] if m['role'] == 'tutorial']; assert len(training) == 4
    search = json.loads((OUT / 'search_visibility.json').read_text(encoding='utf-8'))['summary']
    assert search['hidden_concealed_and_discoverable'] == search['hidden_intent'] == 18
    assert search['all_grabbable'] == search['discoverable_without_crouching'] == 23
    assert search['treasure_mesh_intersections'] == search['support_envelope_overlaps'] == search['floor_placed'] == 0
    return dict(paths_approaches=sum(r['reachable'] for r in paths['human_approaches']), cart_gap_to_rear_row_m=paths['cart_gap_to_rear_row_m'],
                search=search)


def run():
    data = json.loads((OUT / 'stage_layout.json').read_text(encoding='utf-8'))
    result = {'layout': layout_checks(data), 'fbx': [], 'blend': []}
    original = bpy.context.window.scene; selected = list(bpy.context.selected_objects); active = bpy.context.view_layer.objects.active
    scene = bpy.data.scenes.new('THStage_VerifyOnly'); bpy.context.window.scene = scene
    try:
        paths = [('ExcavationDepot_5x11.fbx', data['runtime_stats'])] + [('modules/' + m['name'] + '.fbx', m) for m in data['module_stats']]
        for path, expected in paths:
            previous = set(bpy.data.objects)
            bpy.ops.import_scene.fbx(filepath=str(OUT / path), use_anim=False)
            added = set(bpy.data.objects) - previous; bpy.context.view_layer.update()
            actual = inspect(added)
            assert actual['triangles'] == expected['triangles'] and actual['meshes'] == expected['meshes'], path
            if path == 'ExcavationDepot_5x11.fbx':
                markers = {o.get('anchor_id'): o for o in added if o.get('anchor_id')}
                assert len(markers) == len(data['markers'])
                for m in data['markers']:
                    assert (markers[m['name']].matrix_world.translation - Vector(m['position_blender_xyz'])).length < .001
            result['fbx'].append(dict(path=path, **actual))
            for obj in added: bpy.data.objects.remove(obj, do_unlink=True)
        for file in ('ExcavationDepot_5x11.blend', 'Stage_Review.blend'):
            with bpy.data.libraries.load(str(OUT / file), link=False) as (src, dst): dst.scenes = src.scenes
            assert len(dst.scenes) == 1
            loaded = dst.scenes[0]; bpy.context.window.scene = loaded; bpy.context.view_layer.update()
            runtime = [o for o in loaded.objects if o.type == 'MESH' and o.get('module_id')]
            actual = inspect(runtime); assert actual['triangles'] == data['runtime_stats']['triangles']
            for o in runtime:
                for m in o.data.materials:
                    for n in m.node_tree.nodes:
                        if n.type == 'TEX_IMAGE': assert n.image and n.image.packed_file
            if file == 'Stage_Review.blend': assert loaded.camera and loaded.camera.location.length > 1
            result['blend'].append(dict(path=file, **actual))
            bpy.context.window.scene = scene; bpy.data.scenes.remove(loaded)
        result['combined_triangles'] = data['runtime_stats']['triangles'] + data['preview_treasure_stats']['triangles']
        result['combined_triangles_including_cart'] = result['combined_triangles'] + data['preview_cart_stats']['triangles']
        assert result['combined_triangles'] < 300000
        result.update(unity_imported=False, unity_compile='not run; Blender-only output', unity_editmode='not run', pico_fps='not measured')
        (OUT / 'verification.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
        print('PASS:', json.dumps(dict(fbx=len(result['fbx']), blend=len(result['blend']), combined=result['combined_triangles_including_cart'], layout=result['layout']['search'])))
    finally:
        bpy.context.window.scene = original; bpy.data.scenes.remove(scene)
        bpy.ops.object.select_all(action='DESELECT')
        for o in selected: o.select_set(True)
        bpy.context.view_layer.objects.active = active


if __name__ == '__main__': run()
