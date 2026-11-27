from pathlib import Path
import struct,numpy as np
assets=Path(__file__).parent/'Unity/Assets/Vant'
for name,suffix in [('VantLMC','LM_C'),('VantLMO','LM_O'),('VantVM','VM')]:
    v=[]; n=[];uv=[];faces=[[],[]];k=suffix.split('_')[0]
    for line in (Path('D:/Downloads')/('Vant '+k)/('Vant_'+suffix+'.obj')).read_text().splitlines():
        a=line.split()
        if not a:continue
        if a[0]=='v':v.append(list(map(float,a[1:4])))
        elif a[0]=='vt':uv.append(list(map(float,a[1:3])))
        elif a[0]=='vn':n.append(list(map(float,a[1:4])))
        elif a[0]=='f':
            poly=[tuple(int(s)-1 for s in vertex.split('/')) for vertex in a[1:]]
            pts=np.array([v[c[0]] for c in poly]);center=pts.mean(0)
            glass=k=='VM' and 107.4<=pts[:,1].min() and pts[:,1].max()<114.5 and np.ptp(pts,axis=0)[0]>20 and np.ptp(pts,axis=0)[1]>6 and np.ptp(pts,axis=0)[2]<.1
            for j in range(1,len(poly)-1):faces[int(glass)].extend([poly[0],poly[j+1],poly[j]])
    v=np.array(v);mid=(v.max(0)+v.min(0))/2
    if k=='VM':mid[1]=110.95-25.5
    v=(v-mid)*np.array([-.01,.01,.01]);n=np.array(n)*[-1,1,1]
    if k=='VM':assert len(faces[1])==12,len(faces[1])
    with (assets/(name+'.bytes')).open('wb') as f:
        f.write(struct.pack('<i',sum(bool(x) for x in faces)))
        for i,refs in enumerate(faces):
            if not refs:continue
            idx={r:j for j,r in enumerate(dict.fromkeys(refs))}
            f.write(struct.pack('<iii',i,len(idx),len(refs)))
            for vi,ti,ni in idx:f.write(struct.pack('<8f',*v[vi],*n[ni],*uv[ti]))
            f.write(np.array([idx[r] for r in refs],dtype='<i4').tobytes())
    print(name,[len(a)//3 for a in faces])
