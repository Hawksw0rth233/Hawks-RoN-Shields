using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Modding.Custom;

namespace Hawks.RonShields;

[Injectable(TypePriority = OnLoadOrder.Preload + 1)]
public sealed class Shields(
    CustomItemService items,
    TemplateTable templates,
    GlobalTable globals,
    TraderConfig traderConfig
) : IOnLoad
{
    const string BodyPart = "692340510000000000000011";
    internal const string HrtShield = "692340510000000000000002";
    internal const string HrtBodyPart = "692340510000000000000013";
    internal const int HrtBodyDurability = 400;
    internal const string LmBodyPart = "692340510000000000000014";
    internal const string VmWindowPart = "692340510000000000000015";
    const string WindowPart = "692340510000000000000012";
    const string PlateDonor = "64afc71497cf3a403c01ff38";
    const string Donor = "61f7c9e189e6fb1a5e3ea78d";

    internal static (string Template, int Max) BodyFor(string shield) =>
        shield switch
        {
            HrtShield or "692340510000000000000005" => (HrtBodyPart, HrtBodyDurability),
            "692340510000000000000003" or "692340510000000000000004" => (LmBodyPart, 350),
            _ => (BodyPart, 500),
        };

    internal static (string Template, int Max) WindowFor(string shield) =>
        shield == "692340510000000000000005" ? (VmWindowPart, 200) : (WindowPart, 100);

    public Task OnLoadAsync(CancellationToken token)
    {
        RegisterArmorPart(BodyPart, "Shield body", "盾身", "盾本体", 5, 500);
        RegisterArmorPart(HrtBodyPart, "Shield body", "盾身", "盾本体", 5, HrtBodyDurability);
        RegisterArmorPart(WindowPart, "Shield viewport", "观察窗", "防弾窓", 4, 100);
        RegisterArmorPart(LmBodyPart, "Shield body", "盾身", "盾本体", 5, 350);
        RegisterArmorPart(VmWindowPart, "Shield viewport", "观察窗", "防弾窓", 5, 200);
        ShieldFleaCondition.Install();
        Register(
            "692340510000000000000001",
            "Ballistic shield",
            "警用防弹盾",
            "バリスティックシールド",
            65000
        );
        Register(
            "692340510000000000000002",
            "HRT ballistic shield",
            "HRT 防弹盾",
            "HRT バリスティックシールド",
            65000
        );
        Register(
            "692340510000000000000003",
            "Vant LM C ballistic shield",
            "Vant LM C 防弹盾",
            "Vant LM C 防弾盾",
            65000,
            false
        );
        Register(
            "692340510000000000000004",
            "Vant LM O ballistic shield",
            "Vant LM O 防弹盾",
            "Vant LM O 防弾盾",
            65000,
            false
        );
        Register(
            "692340510000000000000005",
            "Vant VM ballistic shield",
            "Vant VM 防弹盾",
            "Vant VM 防弾盾",
            65000
        );
        return Task.CompletedTask;
    }

    void Register(string id, string en, string ch, string jp, int price, bool viewport = true)
    {
        var locales = new Dictionary<string, LocaleDetails>();
        foreach (
            var (language, name, description) in new[]
            {
                (
                    "en",
                    en,
                    "When equipped, the NIJ Level IV-rated shield will block or deflect most projectiles that encounter it. The shield is therefore best used by the pointman of a team, who can form a mobile wall of cover."
                ),
                (
                    "ch",
                    ch,
                    "装备NIJ IV级盾牌后能够将阻挡或偏转大多数遭遇的子弹。因此，该盾牌最适合队伍中的尖兵使用，使用者可以化身为移动的掩护墙。"
                ),
                (
                    "jp",
                    jp,
                    "NIJレベルIV規格のシールドを装備すれば、遭遇するほとんどの弾丸を遮断または逸らすことが可能。そのため、シールドはチームのポイントマンが使用するのが最適であり、移動式の壁として味方の援護ができる。"
                ),
            }
        )
            locales[language] = new()
            {
                Name = name,
                ShortName = name,
                Description = id[^1] switch
                {
                    '3' => language == "en" ? "A compact Vant LM shield with the front flap closed."
                    : language == "ch" ? "一面紧凑的 Vant LM 防弹盾，正面罩片闭合。"
                    : "前面のカバーを閉じた小型の Vant LM 防弾盾。",
                    '4' => language == "en"
                        ? "A compact Vant LM shield with the front flap raised, exposing a red disc."
                    : language == "ch" ? "一面紧凑的 Vant LM 防弹盾，罩片翻起，露出红色圆面。"
                    : "前面のカバーを上げ、赤い円盤が見えている小型の Vant LM 防弾盾。",
                    '5' => language == "en"
                        ? "A tall Vant VM shield with a rectangular viewport and an extended lower panel."
                    : language == "ch" ? "一面带矩形观察窗和下部延伸护板的 Vant VM 防弹盾。"
                    : "長方形の防弾窓と下部パネルを備えた縦長の Vant VM 防弾盾。",
                    _ => description,
                },
            };
        var result = items.CreateItemFromClone(
            new()
            {
                ItemTplToClone = Donor,
                NewId = id,
                NewItemName = "ron_shield_" + id[^1],
                ParentId = templates.Items[Donor].Parent,
                HandbookParentId = "5b5f79eb86f77447ed5636b7",
                HandbookPriceRoubles = price,
                FleaPriceRoubles = price,
                Locales = locales,
            }
        );
        if (!result.Success)
            throw new InvalidOperationException(string.Join(", ", result.Errors));
        var props = templates.Items[id].Properties!;
        string bodyPart = BodyFor(id).Template;
        props.Slots = !viewport
            ? [ArmorSlot(id, "mod_shield_body", bodyPart, "1")]
            :
            [
                ArmorSlot(id, "mod_shield_body", bodyPart, "1"),
                ArmorSlot(id, "mod_shield_window", WindowFor(id).Template, "2"),
            ];
        props.Chambers = [];
        props.Cartridges = [];
        props.Width = 3;
        props.Height = 5;
        props.SizeReduceRight = 0;
        props.Durability = props.MaxDurability = null;
        RegisterPreset(id, en, viewport);
        traderConfig.Fence.Blacklist.Add(id);
        props.MinRepairDegradation =
            props.MaxRepairDegradation =
            props.MinRepairKitDegradation =
            props.MaxRepairKitDegradation =
                0;
        props.Weight = 8;
        props.WeapClass = "specialWeapon";
        props.CanSellOnRagfair = true;
        props.IsMountable = false;
        var inventory = templates.Items["55d7217a4bdc2d86028b456d"].Properties!;
        foreach (
            var slot in inventory.Slots!.Where(s =>
                s.Name == "FirstPrimaryWeapon" || s.Name == "SecondPrimaryWeapon"
            )
        )
        foreach (var filter in slot.Properties!.Filters!)
            filter.Filter!.Add(id);
    }

    void RegisterArmorPart(
        string id,
        string en,
        string ch,
        string jp,
        int armorClass,
        int durability
    )
    {
        var result = items.CreateItemFromClone(
            new()
            {
                ItemTplToClone = PlateDonor,
                NewId = id,
                NewItemName = en,
                ParentId = templates.Items[PlateDonor].Parent,
                HandbookParentId = "5b5f704686f77447ec5d76d7",
                HandbookPriceRoubles = 1,
                FleaPriceRoubles = 1,
                Locales = new()
                {
                    ["en"] = new()
                    {
                        Name = en,
                        ShortName = en,
                        Description = en,
                    },
                    ["ch"] = new()
                    {
                        Name = ch,
                        ShortName = ch,
                        Description = ch,
                    },
                    ["jp"] = new()
                    {
                        Name = jp,
                        ShortName = jp,
                        Description = jp,
                    },
                },
            }
        );
        if (!result.Success)
            throw new InvalidOperationException(string.Join(", ", result.Errors));
        var props = templates.Items[id].Properties!;
        props.Durability = props.MaxDurability = durability;
        props.ArmorClass = armorClass;
        // Physical hit testing belongs to the placed shield surfaces.
        props.ArmorColliders = [];
        props.ArmorPlateColliders = [];
        props.SpeedPenaltyPercent = props.MousePenalty = props.WeaponErgonomicPenalty = 0;
        props.Weight = 0;
        props.CanSellOnRagfair = false;
        props.RaidModdable = false;
    }

    static Slot ArmorSlot(string shield, string name, string part, string ordinal) =>
        new()
        {
            Id = shield[..21] + "a" + shield[^1] + ordinal,
            Parent = shield,
            Name = name,
            Required = true,
            MergeSlotWithChildren = true,
            Prototype = "55d30c394bdc2dae468b4577",
            Properties = new()
            {
                Filters =
                [
                    new()
                    {
                        Filter = [part],
                        Plate = part,
                        Locked = true,
                    },
                ],
            },
        };

    void RegisterPreset(string shield, string name, bool viewport)
    {
        string key = shield[..21] + "b" + shield[^1] + "0";
        string root = key[..23] + "1";
        var parts = new List<Item>
        {
            new Item { Id = root, Template = shield },
            PresetPart(
                key[..23] + "2",
                root,
                "mod_shield_body",
                BodyFor(shield).Template,
                BodyFor(shield).Max
            ),
        };
        if (viewport)
            parts.Add(
                PresetPart(
                    key[..23] + "3",
                    root,
                    "mod_shield_window",
                    WindowFor(shield).Template,
                    WindowFor(shield).Max
                )
            );
        globals.ItemPresets[key] = new Preset
        {
            Id = key,
            Encyclopedia = shield,
            Parent = root,
            Type = "Preset",
            Name = name,
            ChangeWeaponName = false,
            Items = parts,
        };
    }

    static Item PresetPart(
        string id,
        string parent,
        string slot,
        string template,
        int durability
    ) =>
        new()
        {
            Id = id,
            ParentId = parent,
            SlotId = slot,
            Template = template,
            Upd = new()
            {
                Repairable = new() { Durability = durability, MaxDurability = durability },
            },
        };
}
