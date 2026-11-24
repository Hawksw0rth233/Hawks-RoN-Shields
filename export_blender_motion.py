"""Bake the approved Blender scene into the client assembly's embedded motion track."""
import bpy, struct
from pathlib import Path
from mathutils import Matrix
scene=bpy.context.scene
rig=next(o for o in scene.objects if o.type=='ARMATURE' and o.name.startswith('EFT'))
shield=next(o for o in scene.objects if o.name.startswith('Mount') and 'shield' in o.name)
gun=next(o for o in scene.objects if o.name.startswith('Mount') and 'EFT' in o.name)
swap=Matrix(((1,0,0),(0,0,1),(0,1,0)))
def v(p):return tuple(swap@p)
def orientation(matrix):
 q=(swap@matrix.to_3x3().normalized()@swap).to_quaternion()
 return (q.x,q.y,q.z,q.w)
def bone(name):return rig.matrix_world@rig.pose.bones['Base Human'+name].head
markers={m.name:m.frame for m in scene.timeline_markers}
assert [markers[k] for k in ['Draw','Shield low','Raise pistol','Raised','Retract pistol','Holster']]==[1,50,80,101,121,142]
out=Path(__file__).parent/'Client'/'ShieldMotion.bin'
with out.open('wb') as f:
 for frame in range(1,scene.frame_end+1):
  scene.frame_set(frame)
  center=(bone('LUpperarm')+bone('RUpperarm'))/2
  floats=(*v(shield.matrix_world.translation-center),*orientation(shield.matrix_world),*v(bone('LPalm')-center),*v(bone('RPalm')-center),*orientation(rig.matrix_world@rig.pose.bones["Base HumanRPalm"].matrix))
  fingers=[v(bone('LDigit%d%d'%(digit,seg))-center) for digit in range(1,6) for seg in range(1,4)]
  f.write(struct.pack('<68f',*floats,*v(bone('RUpperarm')-center),*v(bone('RForearm1')-center),*(i for p in fingers for i in p)))
print(out,out.stat().st_size)
