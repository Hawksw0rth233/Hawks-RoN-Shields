using System.Collections.Generic;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace Hawks.RonShields
{
    public sealed class ShieldOwnerContacts : MonoBehaviour
    {
        Player owner;
        Collider[] shield;
        readonly List<Collider> found=new List<Collider>();
        readonly HashSet<Collider> active=new HashSet<Collider>();
        readonly Dictionary<Collider,List<Collider>> ignored=new Dictionary<Collider,List<Collider>>();
        readonly List<Collider> expired=new List<Collider>();

        internal static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(SimpleCharacterController),"IsColliderIgnored"),
                postfix:new HarmonyMethod(typeof(ShieldOwnerContacts),nameof(Movement)));
            harmony.Patch(AccessTools.Method(typeof(Player.FirearmController),"IsOverlapHitIgnored"),
                postfix:new HarmonyMethod(typeof(ShieldOwnerContacts),nameof(Muzzle)));
        }
        static Player WearerOf(Collider col)
        {
            if(col && col.TryGetComponent<ShieldSurface>(out var surface) && surface.Protection)
                return surface.Protection.Wearer;
            return null;
        }
        static void Movement(SimpleCharacterController __instance,Collider __0,ref bool __result)
        {
            if(__result)return;
            var wearer=WearerOf(__0);
            if(wearer && __instance.transform.IsChildOf(wearer.gameObject.transform))__result=true;
        }
        static void Muzzle(Player ____player,RaycastHit __0,ref bool __result)
        {
            if(!__result && ____player && WearerOf(__0.collider)==____player)__result=true;
        }

        internal void Bind(Player wearer,Collider[] surfaces)
        {
            ResetPairs();owner=wearer;shield=surfaces;
            if(isActiveAndEnabled)Refresh();
        }
        void FixedUpdate()=>Refresh();
        void OnEnable()=>Refresh();
        void OnDisable()=>ResetPairs();
        void Refresh()
        {
            if(!owner || shield==null)return;
            owner.GetComponentsInChildren(false,found);
            active.Clear();
            foreach(var body in found){
                if(!body.enabled || body.isTrigger || body.transform.IsChildOf(transform))continue;
                active.Add(body);
                if(!ignored.TryGetValue(body,out var changed)){changed=new List<Collider>();ignored.Add(body,changed);}

                foreach(var part in shield){
                    if(!part.enabled || !part.gameObject.activeInHierarchy || Physics.GetIgnoreCollision(part,body))continue;
                    Physics.IgnoreCollision(part,body,true);
                    if(!changed.Contains(part))changed.Add(part);
                }

            }
            expired.Clear();
            foreach(var entry in ignored)if(!active.Contains(entry.Key))expired.Add(entry.Key);
            foreach(var body in expired){Restore(body,ignored[body]);ignored.Remove(body);}
        }
        internal void ResetPairs()
        {
            foreach(var entry in ignored)Restore(entry.Key,entry.Value);
            ignored.Clear();
        }
        static void Restore(Collider body,List<Collider> parts)
        {
            if(!body)return;
            foreach(var part in parts)if(part)Physics.IgnoreCollision(part,body,false);
        }
    }
}
