using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Modding.Custom;

namespace Hawks.RonShields;
[Injectable(TypePriority = OnLoadOrder.Preload + 1)]
public sealed class Shields(CustomItemService items, TemplateTable templates) : IOnLoad
{
    const string Donor = "61f7c9e189e6fb1a5e3ea78d";
    public Task OnLoadAsync(CancellationToken token)
    {

        Register("692340510000000000000001","Ballistic shield","警用防弹盾","バリスティックシールド",65000);
        Register("692340510000000000000002","HRT ballistic shield","HRT 防弹盾","HRT バリスティックシールド",65000);
        return Task.CompletedTask;
    }
    void Register(string id,string en,string ch,string jp,int price)
    {
        var locales = new Dictionary<string,LocaleDetails>();
        foreach(var (language,name,description) in new[]{
            ("en",en,"When equipped, the NIJ Level IV-rated shield will block or deflect most projectiles that encounter it. The shield is therefore best used by the pointman of a team, who can form a mobile wall of cover."),
            ("ch",ch,"装备NIJ IV级盾牌后能够将阻挡或偏转大多数遭遇的子弹。因此，该盾牌最适合队伍中的尖兵使用，使用者可以化身为移动的掩护墙。"),
            ("jp",jp,"NIJレベルIV規格のシールドを装備すれば、遭遇するほとんどの弾丸を遮断または逸らすことが可能。そのため、シールドはチームのポイントマンが使用するのが最適であり、移動式の壁として味方の援護ができる。")
        })
            locales[language]=new(){Name=name,ShortName=name,Description=description};
        var result=items.CreateItemFromClone(new(){
            ItemTplToClone=Donor,NewId=id,NewItemName="ron_shield_"+id[^1], ParentId=templates.Items[Donor].Parent,
            HandbookParentId="5b5f79eb86f77447ed5636b7",HandbookPriceRoubles=price,FleaPriceRoubles=price,Locales=locales});
        if (!result.Success) throw new InvalidOperationException(string.Join(", ",result.Errors));
        var props=templates.Items[id].Properties!;
        props.Slots=[];props.Chambers=[];props.Cartridges=[];props.Width=3;props.Height=5;props.SizeReduceRight=0;
        props.Durability=props.MaxDurability=600;
        props.MinRepairDegradation=props.MaxRepairDegradation=props.MinRepairKitDegradation=props.MaxRepairKitDegradation=0;
        props.Weight=8;props.WeapClass="specialWeapon";props.CanSellOnRagfair=true;props.IsMountable=false;
        var inventory=templates.Items["55d7217a4bdc2d86028b456d"].Properties!;
        foreach(var slot in inventory.Slots!.Where(s=>s.Name=="FirstPrimaryWeapon"||s.Name=="SecondPrimaryWeapon"))
            foreach(var filter in slot.Properties!.Filters!) filter.Filter!.Add(id);
    }
}
