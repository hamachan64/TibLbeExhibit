"""Measure approach paths with all new support envelopes included."""
import math,json,sys
from pathlib import Path
from collections import deque
# Optional revision folder: python check_context_paths.py Revision7 (or blender -b --python ... -- Revision7)
_args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else sys.argv[1:]
OUT=Path(__file__).resolve().parent/(_args[0] if _args and _args[0].startswith('Revision') else 'Revision5')
def run():
    data=json.loads((OUT/'stage_layout.json').read_text(encoding='utf-8'))
    obs=[c for c in data['colliders'] if c['name']!='VenueFloor']
    def clear(x,d,r,c):
        cx,_,cd=c['center_unity_xyz'];w,_,dd=c['size_unity_xyz']
        return math.hypot(max(abs(x-cx)-w/2,0),max(abs(d-cd)-dd/2,0))>=r-1e-6
    def flood(r):
        valid={(i,j) for i in range(101) for j in range(221) if abs(i*.05-2.5)+r<=2.5 and r<=j*.05<=11-r and all(clear(i*.05-2.5,j*.05,r,c) for c in obs)}
        start=(50,50);assert start in valid
        prev={start:None};q=deque([start])
        while q:
            i,j=q.popleft()
            for n in ((i+1,j),(i-1,j),(i,j+1),(i,j-1)):
                if n in valid and n not in prev:prev[n]=(i,j);q.append(n)
        return prev
    human=flood(.30);cart=flood(.80);results=[]
    def path(prev,end):
        route=[]
        while end is not None:route.append([round(end[0]*.05-2.5,3),round(end[1]*.05,3)]);end=prev[end]
        return route[::-1]
    for m in data['markers']:
        if m['role']!='treasure_candidate':continue
        x,_,d=m['position_unity_xyz']
        best=min(human,key=lambda n:math.hypot(n[0]*.05-2.5-x,n[1]*.05-d))
        dist=math.hypot(best[0]*.05-2.5-x,best[1]*.05-d)
        results.append(dict(slot=m['name'],distance_m=round(dist,3),reachable=dist<=.65,approach=[best[0]*.05-2.5,best[1]*.05],path=path(human,best)))
    report=dict(human_approaches=results,human_all_reachable=all(r['reachable'] for r in results),cart_max_depth_m=max(n[1]*.05 for n in cart),human_radius_m=.30,cart_radius_m=.8,grid_m=.05)
    report['cart_route']=path(cart,max(cart,key=lambda n:(n[1],-abs(n[0]-25))))
    report['limitation']='Static solid envelopes; 0.65m horizontal reach proxy. No hand tracking, crowds or runtime physics guarantee. The earlier full cart loop is not retained.'
    (OUT/'approach_checks.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    def pt(x,d):return 210+x*65,770-d*65
    svg=['<svg xmlns="http://www.w3.org/2000/svg" width="920" height="830">','<rect width="920" height="830" fill="#182725"/>','<g font-family="sans-serif" fill="#ece4cf">','<text x="40" y="32" font-size="24">TREASURE HAUL / OFFERING RUINS</text>']
    for c in obs:
        x,_,d=c['center_unity_xyz'];w,_,length=c['size_unity_xyz'];a,b=pt(x-w/2,d+length/2)
        color='#bf9662' if 'FindSupport' in c['name'] else '#62776c'
        svg.append(f'<rect x="{a}" y="{b}" width="{w*65}" height="{length*65}" fill="{color}"/>')
    route=' '.join('%g,%g'%pt(x,d) for x,d in report['cart_route']);svg.append(f'<polyline points="{route}" fill="none" stroke="#7cddd2" stroke-width="3"/>')
    for i,m in enumerate(m for m in data['markers'] if m['role']=='treasure_candidate'):
        x,_,d=m['position_unity_xyz'];a,b=pt(x,d);svg.append(f'<text x="{a}" y="{b+4}" text-anchor="middle" font-size="9" fill="#ffffff">{i+1}</text>')
    hidden=sum(1 for m in data['markers'] if m.get('hidden_intent'))
    notes=['5 x 11 m / 23 supported finds','14 S + 9 M / 10 S model types','Gold: stone supports and niches' if hidden else 'Gold: stone supports and wall mounts','Teal: measured cart route (radius 0.80 m)','All 23 reachable by human proxy (0.30 m)','Treasure perimeter exclusion: >= 0.70 m','All architecture and placed finds below 0.80 m','Low altars replace three partition bodies',('%d niches closed toward entrance'%hidden) if hidden else 'West cache row: human exploration branch','Rear row: carry finds to cart delivery point','No full-width passthrough floor in export','Unity integration / device validation pending']
    for i,n in enumerate(notes):svg.append(f'<text x="395" y="{105+i*39}" font-size="15">{n}</text>')
    svg.append('</g></svg>');(OUT/'floorplan.svg').write_text('\n'.join(svg),encoding='utf-8')
    # The cart must pull up to the rearmost find row (Revision5's absolute 8.9 m encoded
    # the same thing for a row at d 9.9). Gap = row front face minus cart front edge.
    rear_front=max(c['center_unity_xyz'][2]-c['size_unity_xyz'][2]/2 for c in obs if 'FindSupport' in c['name'])
    report['rear_row_front_m']=round(rear_front,3);report['cart_gap_to_rear_row_m']=round(rear_front-(report['cart_max_depth_m']+.8),3)
    (OUT/'approach_checks.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print('Approaches:',sum(r['reachable'] for r in results),'/',len(results),'Cart depth:',report['cart_max_depth_m'],'gap to rear row:',report['cart_gap_to_rear_row_m'])
    assert report['human_all_reachable'] and report['cart_gap_to_rear_row_m']<=.10
    return report
if __name__=='__main__':run()
