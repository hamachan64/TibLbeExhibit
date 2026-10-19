"""Reload Revision 7 FBX/modules/blends and re-check layout. Execute through Blender MCP.

Reuses verify_stage.py (FBX + 33 module round trips, marker positions, packed
images, venue/height bounds). Layout checks use the measured search report
(check_search_visibility.py, run headless beforehand) and the path check.
"""
import json, runpy, importlib.util
from pathlib import Path
HERE = Path(__file__).resolve().parent
OUT = HERE / 'Revision7'
spec = importlib.util.spec_from_file_location('verify_stage_base_v7', HERE / 'verify_stage.py')
legacy = importlib.util.module_from_spec(spec); spec.loader.exec_module(legacy)
legacy.OUT = OUT


def layout_checks(data):
    import sys
    argv = sys.argv; sys.argv = ['check_context_paths.py', 'Revision7']
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
    search = json.loads((OUT / 'search_visibility.json').read_text(encoding='utf-8'))['summary']
    assert search['hidden_concealed_and_discoverable'] == search['hidden_intent'] == 18
    assert search['all_grabbable'] == search['discoverable_without_crouching'] == 23
    assert search['treasure_mesh_intersections'] == search['support_envelope_overlaps'] == search['floor_placed'] == 0
    return dict(paths_approaches=sum(r['reachable'] for r in paths['human_approaches']), cart_gap_to_rear_row_m=paths['cart_gap_to_rear_row_m'],
                search=search)


legacy.layout_checks = layout_checks


def run():
    legacy.run()
    p = OUT / 'verification.json'; data = json.loads(p.read_text(encoding='utf-8'))
    layout = json.loads((OUT / 'stage_layout.json').read_text(encoding='utf-8'))
    probe = data.pop('occlusion_probe', None)
    data['legacy_single_ray_probe'] = dict(probe or {}, total=23, note='Superseded by search_visibility.json (multi-point body/grip, walkable viewpoints).')
    data['combined_triangles_including_cart'] = data['combined_triangles'] + layout['preview_cart_stats']['triangles']
    data['unity_imported'] = False; data['unity_compile'] = 'not run; Blender-only revision'; data['unity_editmode'] = 'not run'; data['pico_fps'] = 'not measured'
    p.write_text(json.dumps(data, indent=2), encoding='utf-8')
    print('PASS Revision7:', json.dumps(dict(fbx=len(data['fbx']), blend=len(data['blend']), combined=data['combined_triangles_including_cart'], layout=data['layout']['search'])))


if __name__ == '__main__': run()
