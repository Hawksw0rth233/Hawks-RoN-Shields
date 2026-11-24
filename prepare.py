import os
import struct,json,shutil
from pathlib import Path
from PIL import Image,ImageOps
r=Path(os.environ['RON_EXPORT'])
d=(Path(__file__).resolve().parent / 'Unity/Assets/Shields')
for name,stem,albedo,normal,orm in [('Patrol','SK_Device_Ballistic_Shield','Ballistic_Shield_B','Ballistic_Shield_N','Ballistic_Shield_ARM'),('HRT','SK_Device_Ballistic_Shield_HRT','HRT_Shield_Shield_BaseColor','HRT_Shield_Shield_Normal','HRT_Shield_Shield_OcclusionRoughnessMetallic')]:
 raw=next(r.rglob(stem+'.glb')).read_bytes();n=struct.unpack_from('<I',raw,12)[0];j=json.loads(raw[20:20+n]);buf=raw[28+n:]
 def acc(i):
  a=j['accessors'][i];v=j['bufferViews'][a['bufferView']];fmt='<'+{'SCALAR':'','VEC2':'2','VEC3':'3'}[a['type']]+{5121:'B',5123:'H',5125:'I',5126:'f'}[a['componentType']];ofs=a.get('byteOffset',0)+v.get('byteOffset',0);return [struct.unpack_from(fmt,buf,ofs+k*v.get('byteStride',struct.calcsize(fmt))) for k in range(a['count'])]
 with (d/(name+'.bytes')).open('wb') as f:
  def pack(fmt,*args):f.write(struct.pack('<'+fmt,*args))
  parts=j['meshes'][0]['primitives'];pack('i',len(parts))
  for prim in parts:
   a=prim['attributes'];pos=acc(a['POSITION']);inds=acc(prim['indices']); glass='glass' in j['materials'][prim['material']]['name'].lower()
   pack('iii',int(glass),len(pos),len(inds))
   for (x,y,z),(nx,ny,nz),(u,v) in zip(pos,acc(a['NORMAL']),acc(a['TEXCOORD_0'])):pack('8f',-x,y,z,-nx,ny,nz,u,1-v)
   for k in range(0,len(inds),3):pack('3i',inds[k][0],inds[k+2][0],inds[k+1][0])
 for src,suffix in [(albedo,'Color'),(normal,'Normal')]:shutil.copyfile(next(r.rglob(src+'.png')),d/(name+suffix+'.png'))
 oc,rough,metal=Image.open(next(r.rglob(orm+'.png'))).convert('RGB').split();Image.merge('RGBA',(metal,oc,metal,ImageOps.invert(rough))).save(d/(name+'Mask.png'))
