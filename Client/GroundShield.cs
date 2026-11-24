using System.Collections.Generic;
using EFT.AssetsManager;
using EFT.Interactive;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace Hawks.RonShields
{
    public sealed class GroundShield : MonoBehaviour
    {
        readonly List<Collider> hulls=new List<Collider>();
        readonly List<Collider> disabled=new List<Collider>();
        readonly List<Renderer> hidden=new List<Renderer>();
        bool bound;

        internal static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(LootItem),"Init"),postfix:Hook(nameof(WorldItem)));
            harmony.Patch(AccessTools.Method(typeof(AssetPoolObject),"GetColliders"),prefix:Hook(nameof(WorldColliders)));
            harmony.Patch(AccessTools.Method(typeof(LootItem),"OnReturnToPool"),prefix:Hook(nameof(Release)));
        }
        static HarmonyMethod Hook(string name)=>new HarmonyMethod(typeof(GroundShield),name);

        static void WorldItem(LootItem __instance,Item item,BoxCollider ____boundCollider)
        {
            var root=__instance.gameObject;
            var existing=root.GetComponent<GroundShield>();
            var model=Shields.Model(item);
            if(!model){if(existing)existing.Clear();return;}
            var display=root.GetComponent<ShieldDisplay>() ?? root.AddComponent<ShieldDisplay>();
            display.Bind(model,item);
            int lootLayer=LayerMask.NameToLayer("Loot");
            if(lootLayer>=0)
                foreach(var part in display.Visual.GetComponentsInChildren<Transform>(true))
                    part.gameObject.layer=lootLayer;
            var shield=existing ? existing : root.AddComponent<GroundShield>();
            shield.Bind(display.Visual,____boundCollider);
        }

        internal void Bind(GameObject visual,BoxCollider pickup)
        {
            Clear();
            visual.transform.localPosition=Vector3.zero;
            visual.transform.localRotation=Quaternion.identity;
            visual.SetActive(true);
            foreach(var r in GetComponentsInChildren<Renderer>(true)){
                if(r.transform.IsChildOf(visual.transform) || r.forceRenderingOff)continue;
                // EFT can re-enable a pooled receiver when its ground LOD becomes visible.
                r.forceRenderingOff=true;
                hidden.Add(r);
            }
            foreach(var col in GetComponentsInChildren<Collider>(true)){
                if(col==pickup || !col.enabled)continue;
                col.enabled=false;
                disabled.Add(col);
            }
            foreach(var part in visual.GetComponentsInChildren<MeshFilter>()){
                if(!part.sharedMesh)continue;
                var hull=part.gameObject.AddComponent<MeshCollider>();
                hull.convex=true;
                hull.sharedMesh=part.sharedMesh;
                hulls.Add(hull);
            }
            bound=hulls.Count>0;
        }

        static bool WorldColliders(AssetPoolObject __instance,ref List<Collider> __result)
        {
            if(!__instance.TryGetComponent<GroundShield>(out var shield) || !shield.bound)return true;
            __result=new List<Collider>(shield.hulls);
            return false;
        }
        static void Release(LootItem __instance)
        {
            if(__instance.TryGetComponent<GroundShield>(out var shield))shield.Clear();
        }
        internal void Clear()
        {
            bound=false;
            foreach(var hull in hulls)if(hull){hull.enabled=false;Destroy(hull);}
            foreach(var col in disabled)if(col)col.enabled=true;
            foreach(var r in hidden)if(r)r.forceRenderingOff=false;
            hulls.Clear();disabled.Clear();hidden.Clear();
        }
    }
}
