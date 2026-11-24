using System.Collections.Generic;
using System.Threading.Tasks;
using Comfort.Common;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace Hawks.RonShields
{
    internal static class ShieldRepair
    {
        static readonly HashSet<string> pending=new HashSet<string>();
        internal static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(EFT.UI.ItemUiContext),"GetItemContextInteractions"),
                prefix:new HarmonyMethod(typeof(ShieldRepair),nameof(BeforeMenu)));
            harmony.Patch(AccessTools.Constructor(typeof(DefaultRepairStrategy),new[]{typeof(Item),typeof(EFT.Repairing.RepairController)}),
                prefix:new HarmonyMethod(typeof(ShieldRepair),nameof(Prepare)));
            harmony.Patch(AccessTools.Method(typeof(DefaultRepairStrategy),"RepairItem"),
                prefix:new HarmonyMethod(typeof(ShieldRepair),nameof(Begin)),
                postfix:new HarmonyMethod(typeof(ShieldRepair),nameof(Complete)));
        }

        internal static void Sync(Item item,ShieldCondition hp)
        {
            if(item==null || pending.Contains(item.Id.ToString()))return;
            var native=item.GetItemComponent<RepairableComponent>();
            if(native==null)return;
            native.MaxDurability=600;
            native.Durability=hp.Body+hp.Window;
        }
        static void BeforeMenu(ItemContext __0)
        {
            if(__0!=null) Prepare(__0.Item);
        }

        static void Prepare(Item item)
        {
            if(!ShieldItemPreview.IsShield(item))return;
            var hp=ShieldCondition.Load(item);
            Sync(item,hp);
        }
        static bool Begin(Item ____item,out bool __state,ref Task<IResult> __result)
        {
            __state=false;
            if(!ShieldItemPreview.IsShield(____item))return true;
            Prepare(____item);
            var hp=ShieldCondition.Load(____item);
            string error="Shield repair is already pending";
            if(!pending.Contains(____item.Id.ToString())){
                try {
                    string payload=Newtonsoft.Json.JsonConvert.SerializeObject(new { Id=____item.Id.ToString(),hp.Body,hp.Window, Condition=hp.ToToken() });
                    string accepted=SPT.Common.Http.RequestHandler.PostJson("/hawks/ron-shields/repair-condition",payload);
                    if(accepted?.Trim()=="true"){
                        __state=pending.Add(____item.Id.ToString());
                        return true;
                    }
                    error="Shield repair condition sync failed";
                } catch(System.Exception ex){error=ex.Message;}
            }
            __result=Task.FromResult<IResult>(new FailedResult(error,0));
            return false;
        }
        static void Complete(Item ____item,bool __state,ref Task<IResult> __result)
        {
            if(__state)__result=Finish(__result,____item);
        }

        internal static void Restore(ShieldCondition hp,float repairedTotal)
        {
            float missing=600-hp.Body-hp.Window;
            if(missing<=0)return;
            float ratio=Mathf.Clamp01((repairedTotal-hp.Body-hp.Window)/missing);
            if(ratio<=0)return;
            hp.Body=Mathf.Lerp(hp.Body,500f,ratio);
            hp.Window=Mathf.Lerp(hp.Window,100f,ratio);
            if(hp.Window>=100f)hp.Cracks.Clear();
        }

        static async Task<IResult> Finish(Task<IResult> task,Item item)
        {
            try {
                var result=await task;
                if(result.Succeed){
                    var hp=ShieldCondition.Load(item);
                    Restore(hp,item.GetItemComponent<RepairableComponent>().Durability);
                }
                return result;
            } finally {
                pending.Remove(item.Id.ToString());
                Sync(item,ShieldCondition.Load(item));
                item.UpdateAttributes();
                item.RaiseRefreshEvent(true,true);
            }
        }
    }
}
