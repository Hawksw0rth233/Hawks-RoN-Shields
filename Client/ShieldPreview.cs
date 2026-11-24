using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using RootMotion.FinalIK;
using UnityEngine;

namespace Hawks.RonShields
{
    public sealed class ShieldPreview : MonoBehaviour
    {
        PlayerBody body;
        Item held;
        GameObject visual, created;
        Transform gun, fingerRoot;
        readonly Transform[] fingers=new Transform[15];
        Vector3 gunPosition;
        Quaternion gunRotation;
        bool moved;
        readonly ShieldPose animated=new ShieldPose();
        Vector3 anchor;Quaternion anchorRotation;
        bool anchored,lookNext=true,lookApplied;
        float started=-100;
        Transform neck; Quaternion beforeLook,afterLook;
        ShieldCarry.ArmTranslation rightShoulder,leftShoulder;
        bool gripBound;
        Quaternion gripRotation, gunFromGrip;
        Vector3 gunOffset;
        internal static bool AllowRandomIdle(MenuPlayerPoser __instance)
        {
            var v=__instance.GetComponent<ShieldPreview>();
            if(!v || !v.visual)return true;
            __instance.PlayerAnimatorController.SetInteger(PlayerAnimator.UI_RANDOM_TRANSITION,0);
            if(Time.time-v.started>3f){v.started=Time.time;v.lookNext=!v.lookNext;}
            return false;
        }
        internal static void ResolveHands(PlayerVisualRepresentation __0,InventoryController __1,ref Item __result)
        {
            var equipment=__0.Equipment;
            if(equipment==null)return;
            var first=equipment.GetSlot(EquipmentSlot.FirstPrimaryWeapon)?.ContainedItem;
            var second=equipment.GetSlot(EquipmentSlot.SecondPrimaryWeapon)?.ContainedItem;
            var carry=ShieldCarry.Current;
            if(carry && carry.HasShield){
                if(first?.Id==carry.Selected.Id){__result=first;return;}
                if(second?.Id==carry.Selected.Id){__result=second;return;}
            }
            // Without a live controller EFT chooses the first occupied weapon slot for the portrait.
            // A pair of slung shields should leave the arms in the normal idle pose.
            if(__1==null && Shields.Model(first) && Shields.Model(second) && Shields.Model(__result))
                __result=null;
        }
        public static void Prepare(PlayerModelLoader __instance,ref Item __2,out Item __state)
        {
            __state=Shields.Model(__2)?__2:null;
            if(__state!=null && __instance.PlayerBody?.Equipment?.GetSlot(EquipmentSlot.Holster)?.ContainedItem is Weapon sidearm)
                __2=sidearm;
        }
        public static void Bind(PlayerModelLoader __instance,Item __state,WeaponPrefab ____weaponPrefab)
        {
            var poser=__instance.ModelPlayerPoser;
            if(!poser)return;
            var view=poser.GetComponent<ShieldPreview>() ?? poser.gameObject.AddComponent<ShieldPreview>();
            view.Release();
            view.body=__instance.PlayerBody;view.held=__state;
            if(__state==null || !____weaponPrefab)return;
            var display=____weaponPrefab.GetComponent<ShieldDisplay>();
            if(display && display.Visual)view.visual=display.Visual;
            else{
                view.gun=____weaponPrefab.transform;

                view.created=Instantiate(Shields.Model(__state),poser.transform,false);
                view.visual=view.created;
                foreach(var node in view.visual.GetComponentsInChildren<Transform>(true))node.gameObject.layer=poser.gameObject.layer;
            }
        }
        internal static bool BodyFrame(PlayerBones b,out Vector3 center,out Quaternion facing,out float scale)
        {
            center=Vector3.zero;facing=Quaternion.identity;scale=1;
            if(!b || !b.Neck)return false;
            var lower=b.Spine1;
            if(!lower){
                for(var bone=b.Neck.parent;bone;bone=bone.parent){
                    if(bone.name=="Base HumanSpine1"){lower=bone;break;}
                }
            }
            if(!lower)return false;
            var left=b.Upperarms?.Length>=2?b.Upperarms[0]:null;
            var right=b.Upperarms?.Length>=2?b.Upperarms[1]:null;
            if(!left || !right){
                var menu=b.GetComponent<MenuPlayerPoser>();
                if(menu && menu.Limbs?.Length>=2){
                    left=menu.Limbs[0]?.solver.bone1.transform;
                    right=menu.Limbs[1]?.solver.bone1.transform;
                }
                if(!left)left=b.LeftShoulder?.Original;
                if(!right)right=b.RightShoulder?.Original;
            }
            if(!left || !right)return false;
            var up=(b.Neck.position-lower.position).normalized;
            var forward=Vector3.Cross(right.position-left.position,up).normalized;
            if(forward.sqrMagnitude<.5f)return false;
            center=(left.position+right.position)*.5f;
            facing=Quaternion.LookRotation(forward,up);scale=Mathf.Abs(b.transform.lossyScale.y);
            return true;
        }
        public static void Before(MenuPlayerPoser __instance)
        {
            var v=__instance.GetComponent<ShieldPreview>();if(v)v.RestoreGun();
        }
        public static void After(MenuPlayerPoser __instance)
        {
            var v=__instance.GetComponent<ShieldPreview>();if(!v || !v.body)return;
            v.Slot(EquipmentSlot.FirstPrimaryWeapon,__instance.PBones);
            v.Slot(EquipmentSlot.SecondPrimaryWeapon,__instance.PBones);
            if(!v.visual || !BodyFrame(__instance.PBones,out var center,out var facing,out var scale))return;
            var rig=__instance.transform;
            if(!v.anchored){
                v.anchor=rig.InverseTransformPoint(center);v.anchorRotation=Quaternion.Inverse(rig.rotation)*facing;v.anchored=true;
            }
            center=rig.TransformPoint(v.anchor);facing=rig.rotation*v.anchorRotation;
            float age=Time.time-v.started;
            float aim=0;
            if(age<3f){
                if(v.lookNext || !v.gun){
                    v.neck=__instance.PBones.Neck;v.beforeLook=v.neck.localRotation;
                    float yaw=18f*Mathf.Sin(age*Mathf.PI*2f/3f)*Mathf.Sin(age*Mathf.PI/3f);
                    v.neck.rotation=Quaternion.AngleAxis(yaw,facing*Vector3.up)*v.neck.rotation;
                    v.afterLook=v.neck.localRotation;v.lookApplied=true;
                }else aim=Mathf.SmoothStep(0,1,Mathf.Min(age,3f-age)/.8f);
            }
            var p=v.animated;
            ShieldMotion.Sample(age<1.5f?80:121,age<1.5f?100:141,age<1.5f?aim:1-aim,p);
            var offset=facing*(Shields.ArmsAndShieldCm.Value*.01f*scale);
            var frame=new ShieldPoseFrame(p,center+offset,facing,Quaternion.identity,scale);
            v.visual.transform.SetPositionAndRotation(frame.Center+facing*(Shields.ShieldOnlyCm.Value*.01f*scale),frame.Rotation);
            var limbs=__instance.Limbs;
            if(limbs==null || limbs.Length<2 || !limbs[0] || !limbs[1])return;
            var l=limbs[0].solver;var r=limbs[1].solver;
            if(!l.initiated || !r.initiated)return;
            var palm=r.bone3.transform;
            if(!v.gripBound){
                var reference=facing;
                if(v.gun){
                    Transform port=null,weapon=null;
                    foreach(var node in v.gun.GetComponentsInChildren<Transform>(true)){
                        if(node.name=="fireport" && !port)port=node;
                        if(node.name=="weapon" && !weapon)weapon=node;
                    }
                    if(port && weapon){
                        var direction=-port.up;
                        var up=Vector3.ProjectOnPlane(weapon.forward,direction);
                        if(up.sqrMagnitude>.001f)reference=Quaternion.LookRotation(direction,up);
                    }
                    v.gunFromGrip=Quaternion.Inverse(palm.rotation)*v.gun.rotation;
                    v.gunOffset=Quaternion.Inverse(palm.rotation)*(v.gun.position-palm.position)/scale;
                }
                v.gripRotation=Quaternion.Inverse(reference)*palm.rotation;
                v.gripBound=true;
            }
            var wristRot=facing*ShieldMotion.WristDelta(p)*v.gripRotation;
            v.leftShoulder.Apply(l.bone1.transform,offset);
            v.rightShoulder.Apply(r.bone1.transform,frame.Upper-r.bone1.transform.position);
            SolveArm(limbs[0],frame.Left,null,null);
            ShieldCarry.PoseFingers(limbs[0],p,frame.LeftBasis,ref v.fingerRoot,v.fingers);
            SolveArm(limbs[1],frame.Right,v.gun ? wristRot : (Quaternion?)null,Vector3.Cross(frame.Elbow-frame.Upper,frame.Right-frame.Upper));
            if(v.gun){
                v.gunPosition=v.gun.localPosition;v.gunRotation=v.gun.localRotation;v.moved=true;
                v.gun.SetPositionAndRotation(palm.position+wristRot*(v.gunOffset*scale),wristRot*v.gunFromGrip);
            }
        }
        static void SolveArm(LimbIK arm,Vector3 position,Quaternion? rotation,Vector3? normal)
        {
            var s=arm.solver;
            var target=s.target;var p=s.IKPosition;var q=s.IKRotation;float w=s.IKPositionWeight,r=s.IKRotationWeight;
            var mode=s.bendModifier;var weight=s.bendModifierWeight;var bend=s.bendNormal;
            try{
                s.target=null;s.IKPosition=position;s.IKPositionWeight=1;s.IKRotationWeight=rotation.HasValue?1:0;
                if(rotation.HasValue)s.IKRotation=rotation.Value;
                if(normal.HasValue && normal.Value.sqrMagnitude>1e-10f){
                    s.bendModifier=IKSolverLimb.BendModifier.Custom;s.bendModifierWeight=0;s.bendNormal=normal.Value.normalized;
                }
                s.Update();
            }finally{
                s.target=target;s.IKPosition=p;s.IKPositionWeight=w;s.IKRotation=q;s.IKRotationWeight=r;
                s.bendModifier=mode;s.bendModifierWeight=weight;s.bendNormal=bend;
            }
        }
        void Slot(EquipmentSlot slot,PlayerBones bones)
        {
            var item=body.Equipment?.GetSlot(slot)?.ContainedItem;
            if(!Shields.Model(item))return;
            var model=body.GetSlotViewByItem(item)?.Model;
            var display=model?model.GetComponentInChildren<ShieldDisplay>(true):null;
            if(!display || !display.Visual || display.Visual==visual)return;
            display.Visual.SetActive(held?.Id!=item.Id);
            if(display.Visual.activeSelf)display.PlaceOnBack(bones,body.Equipment,held);
        }
        void RestoreGun(){rightShoulder.Restore();leftShoulder.Restore();if(lookApplied && neck && Quaternion.Angle(neck.localRotation,afterLook)<.01f)neck.localRotation=beforeLook;lookApplied=false;if(moved && gun){gun.localPosition=gunPosition;gun.localRotation=gunRotation;}moved=false;}
        void Release(){RestoreGun();anchored=gripBound=false;started=-100;lookNext=true;if(created){created.SetActive(false);Destroy(created);}created=visual=null;gun=null;held=null;}
        void OnDestroy()=>Release();
    }
}

