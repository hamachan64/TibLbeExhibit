"""Reuse measured maze navigation and exported geometry checks for ruin revision."""
import importlib.util,json
from pathlib import Path
HERE=Path(__file__).resolve().parent
spec=importlib.util.spec_from_file_location('maze_verify',HERE/'verify_stage_v3.py')
module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
original_inspect=module.legacy.inspect
ground_results=[]
def inspect(objects):
    result=original_inspect(objects)
    for obj in objects:
        if obj.type!='MESH' or not obj.get('ground_decoration_only'):continue
        points=[obj.matrix_world@v.co for v in obj.data.vertices]
        assert max(p.z for p in points)<.0041
        assert obj.get('do_not_add_collider') and obj.get('disable_shadow_casting_in_unity')
        obj.data.calc_loop_triangles()
        ground_results.append({'max_height_m':max(p.z for p in points),
            'summed_surface_area_with_overlaps_m2':sum(t.area for t in obj.data.loop_triangles)})
    return result
module.legacy.inspect=inspect
module.run(HERE/'Revision4')
assert len(ground_results)>=4
p=HERE/'Revision4/verification.json';data=json.loads(p.read_text(encoding='utf-8'))
data['ground_decoration_checks']=ground_results
p.write_text(json.dumps(data,indent=2),encoding='utf-8')
