using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Utils;

namespace Hawks.RonShields;
public record ShieldRepairRequest : SPTarkov.Server.Core.Models.Utils.IRequestData { public MongoId Id {get;init;} public double Body {get;init;} public double Window {get;init;} public System.Text.Json.JsonElement? Condition {get;init;} }

[Injectable(TypePriority=OnLoadOrder.Routers+1)]
public sealed class ShieldRepair(JsonUtil json,ProfileHelper profiles):StaticRouter(json,[
    new RouteAction<ShieldRepairRequest>("/hawks/ron-shields/repair-condition",
        (url,request,session,output,token)=>ValueTask.FromResult(
            Prepare(profiles.GetPmcProfile(session),request)?"true":"false"))
])
{
    internal static bool Prepare(PmcData? profile,ShieldRepairRequest request)
    {
        var item=profile?.Inventory?.Items?.FirstOrDefault(i=>i.Id==request.Id);
        if(item==null || item.Template.ToString() is not ("692340510000000000000001" or "692340510000000000000002"))return false;
        if(!double.IsFinite(request.Body) || !double.IsFinite(request.Window) ||
           request.Body is <0 or >500 || request.Window is <0 or >100)return false;
        item.Upd??=new();
        if(request.Condition is { ValueKind: System.Text.Json.JsonValueKind.Object } condition){
            item.Upd.ExtensionData??=new();
            item.Upd.ExtensionData["HawksShieldCondition"]=condition.Clone();
        }
        item.Upd.Repairable??=new();
        item.Upd.Repairable.MaxDurability=600;
        item.Upd.Repairable.Durability=request.Body+request.Window;
        return true;
    }
}
