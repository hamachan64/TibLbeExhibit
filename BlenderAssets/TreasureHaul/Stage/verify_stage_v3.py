"""Round trips plus maze navigation, cart turning envelope, roll-in dock checks."""
import bpy, importlib.util, json, math
from pathlib import Path
from collections import deque
HERE=Path(__file__).resolve().parent
spec=importlib.util.spec_from_file_location('stage_verify',HERE/'verify_stage.py')
legacy=importlib.util.module_from_spec(spec);spec.loader.exec_module(legacy)
legacy.OUT=HERE/'Revision3'

def navigation(data):
    obstacles=[c for c in data['colliders'] if c['name']!='VenueFloor']
    candidates=[m for m in data['markers'] if m['role']=='treasure_candidate']
    assert len(candidates)==28
    minimum=11
    for m in candidates:
        x,_,d=m['position_unity_xyz'];r=m['clearance_radius_m']
        edge=min(2.5-abs(x),d,11-d)-r;minimum=min(minimum,edge)
        assert edge>=.7-1e-6 and d-r>2.8,m['name']
        assert all(legacy.disk_box_clear(x,d,r,c) for c in obstacles),m['name']
    for i,a in enumerate(candidates):
        for b in candidates[i+1:]:
            x,_,d=a['position_unity_xyz'];xx,_,dd=b['position_unity_xyz']
            assert math.hypot(x-xx,d-dd)>=.54
    # 5cm occupancy grid, four-connected edges (no diagonal corner cutting).
    step=.05
    def point(n):return round(n[0]*step-2.5,5),round(n[1]*step,5)
    def node(x,d):return round((x+2.5)/step),round(d/step)
    def grid(radius):
        return {n for i in range(101) for j in range(221) for n in [(i,j)]
                for x,d in [point(n)] if abs(x)+radius<=2.5+1e-6 and radius<=d<=11-radius
                and all(legacy.disk_box_clear(x,d,radius,c) for c in obstacles)}
    def flood(valid,start):
        assert start in valid,('invalid start',point(start))
        prev={start:None};q=deque([start])
        while q:
            a=q.popleft()
            for b in ((a[0]-1,a[1]),(a[0]+1,a[1]),(a[0],a[1]-1),(a[0],a[1]+1)):
                if b in valid and b not in prev:prev[b]=a;q.append(b)
        return prev
    def path(prev,end):
        assert end in prev,('unreachable',point(end))
        values=[]
        while end is not None:values.append(point(end));end=prev[end]
        return values[::-1]
    start=node(0,2.5);human=flood(grid(.30),start)
    human_paths={m['name']:path(human,node(m['position_unity_xyz'][0],m['position_unity_xyz'][2])) for m in candidates}
    for m in data['markers']:
        if m['role']=='tutorial':assert node(m['position_unity_xyz'][0],m['position_unity_xyz'][2]) in human
    cart_grid=grid(.80);cart=flood(cart_grid,start)
    checkpoints={'door_a':(1.45,3.8),'loop_left':(-1.45,5.85),'loop_right':(1.45,5.85),
                 'door_b':(-1.45,8.1),'rear_left':(-1.15,9.85),'rear_right':(1.15,9.85)}
    cart_paths={name:path(cart,node(*p)) for name,p in checkpoints.items()}
    # Both sides of the L-wall connect above AND below it: genuine loop.
    loop={}
    for name,condition in [('front',lambda d:d<=5.85),('back',lambda d:d>=5.85)]:
        prev=flood({n for n in cart_grid if condition(point(n)[1])},node(-1.45,5.85))
        loop[name]=path(prev,node(1.45,5.85))
    # Exact axis-aligned cart rectangle swept straight into the receiver.
    # Half length includes the handle, plus 3cm geometric clearance.
    dock_samples=[]
    for i in range(61):
        d=2.5+(1.20-2.5)*i/60
        for c in obstacles:
            x,h,z=c['center_unity_xyz'];w,hh,l=c['size_unity_xyz']
            assert abs(x)>=w/2+.408+.03 or abs(d-z)>=l/2+.620+.03,('dock collision',c['name'],d)
        dock_samples.append([0,round(d,4)])
    sample=data['sample_placements'];assert len(sample)==23
    assert sum(p['class_name']=='S' for p in sample)==14
    report=dict(candidate_count=28,min_treasure_edge_margin_m=round(minimum,3),approaches_passed=28,
                human_proxy_radius_m=.30,cart_radius_m=.80,cart_checkpoints_passed=list(checkpoints),
                loop_front_and_back_passed=True,cart_roll_in_passed=True,grid_step_m=step,
                movable_treasures_in_path_check=False,
                limitation='Static architecture only; treasure clearance, multiple people and physical cart handling require gameplay/device tests.')
    (legacy.OUT/'navigation_paths.json').write_text(json.dumps(dict(human=human_paths,cart=cart_paths,loop=loop,dock=dock_samples),indent=2),encoding='utf-8')
    return report

def run(output=None):
    if output is not None:legacy.OUT=output
    legacy.layout_checks=navigation
    legacy.run()
    p=legacy.OUT/'verification.json';data=json.loads(p.read_text(encoding='utf-8'))
    layout=json.loads((legacy.OUT/'stage_layout.json').read_text(encoding='utf-8'))
    data['combined_triangles_including_cart']=data['combined_triangles']+layout['preview_cart_stats']['triangles']
    data['unity_compile']='not run; model-only revision'
    data['unity_editmode']='not run; model-only revision'
    p.write_text(json.dumps(data,indent=2),encoding='utf-8')
    print('Maze verification:',data['layout'])

if __name__=='__main__':run()
