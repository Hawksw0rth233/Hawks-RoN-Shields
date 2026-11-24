import os
import numpy as np,struct,pathlib, json
p=pathlib.Path(__file__).resolve().parent;r=pathlib.Path(os.environ['RON_EXPORT'])
out={}
clips={'Draw':'low_draw','Holster':'low_holster','Raise':'low_to_up','Lower':'up_to_low'}
for key,suffix in clips.items():
 clip='fp_fiveseven_add_shield_'+suffix
 src=next(p for p in r.rglob(clip+'.psa') if '/FP/Shield/' in p.as_posix());d=src.read_bytes();chunks={};i=0
 while i<len(d):
  s,_,n,c=struct.unpack_from('<20siii',d,i);i+=32;chunks[s.split(b'\0')[0]]=(i,n,c);i+=n*c
 off,bs,nb=chunks[b'BONENAMES'];names=[d[off+i*bs:off+i*bs+64].split(b'\0')[0].decode() for i in range(nb)];parents=[struct.unpack_from('<i',d,off+i*bs+72)[0] for i in range(nb)];base,ks,nk=chunks[b'ANIMKEYS'];data=[]
 for frame in range(nk//nb):
  mats=[]
  for k in range(nb):
   px,py,pz,x,y,z,w,_=struct.unpack_from('<8f',d,base+(frame*nb+k)*ks); mat=np.eye(4);mat[:3,3]=[px,py,pz]
   mat[:3,:3]=[[1-2*(y*y+z*z),2*(x*y-z*w),2*(x*z+y*w)],[2*(x*y+z*w),1-2*(x*x+z*z),2*(y*z-x*w)],[2*(x*z-y*w),2*(y*z+x*w),1-2*(x*x+y*y)]]
   mats.append(mat if k==0 else mats[parents[k]]@mat)
  def pt(n):return mats[names.index(n)][:3,3]
  def unit(v):return v/max(np.linalg.norm(v),1e-7)
  side=unit(pt('upperarm_RI')-pt('upperarm_LE'));front=unit(np.cross(pt('head')-pt('Pelvis'),side));up=unit(np.cross(side,front))
  row=[]
  for hand in ('LE','RI'):
   v=pt('hand_'+hand)-pt('upperarm_'+hand)
   row.append([np.dot(v,u)*.01 for u in (side,up,-front)])
  data.append(row)
 fps=struct.unpack_from('<f',d,chunks[b'ANIMINFO'][0]+152)[0]
 out[key]=(data,len(data)/fps)
lines=['using UnityEngine;', 'namespace Hawks.RonShields { internal static class PoseTracks {']
for key,(data,duration) in out.items():
 print(key,len(data),duration)
 lines.append(f'internal const float {key}Duration={duration:.7f}f;')
 for hand,i in [('Left',0),('Right',1)]:
  lines.append(f'internal static readonly Vector3[] {key}{hand} = {{')
  for row in data:lines.append(' new Vector3('+','.join(f'{x:.6f}f' for x in row[i])+'),')
  lines.append('};')
lines.append('}}')
(p/'Client/PoseTracks.cs').write_text(chr(10).join(lines))
