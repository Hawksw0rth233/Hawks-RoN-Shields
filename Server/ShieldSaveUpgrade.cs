using System.Text.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Profile;

namespace Hawks.RonShields;

[Injectable(InjectionType.Transient, int.MaxValue, TypePriority = 400000)]
public sealed class ShieldSaveUpgrade : SaveLoadRouter
{
    private const string LegacyField = "HawksShieldCondition";
    private const string CracksField = "HawksShieldCracks";

    protected override List<HandledRoute> GetHandledRoutes() =>
        [new("hawks-ron-shield-upgrade", false)];

    protected override SptProfile HandleLoadInternal(SptProfile profile)
    {
        Upgrade(profile.CharacterData?.PmcData?.Inventory?.Items);
        Upgrade(profile.CharacterData?.ScavData?.Inventory?.Items);
        return profile;
    }

    private static void Upgrade(List<Item>? inventory)
    {
        if (inventory == null)
            return;
        foreach (
            var shield in inventory.Where(item =>
                item.Template.ToString()
                    is "692340510000000000000003"
                        or "692340510000000000000004"
                        or "692340510000000000000005"
            )
        )
        {
            string id = shield.Template.ToString();
            UpdateVantPart(
                inventory,
                shield,
                "mod_shield_body",
                "692340510000000000000011",
                500,
                Shields.BodyFor(id)
            );
            if (id == "692340510000000000000005")
                UpdateVantPart(
                    inventory,
                    shield,
                    "mod_shield_window",
                    "692340510000000000000012",
                    100,
                    Shields.WindowFor(id)
                );
        }
        foreach (
            var shield in inventory
                .Where(item =>
                    item.Template.ToString()
                        is "692340510000000000000001"
                            or "692340510000000000000002"
                )
                .ToArray()
        )
        {
            var upd = shield.Upd;
            JsonElement legacy = default;
            if (
                upd?.ExtensionData != null
                && upd.ExtensionData.TryGetValue(LegacyField, out var raw)
                && raw is JsonElement element
            )
                legacy = element;

            // Only the old 600-point aggregate belongs to the shield's armor.
            // A 100-point value came from its donor weapon template.
            var total = upd?.Repairable;
            double fraction =
                total?.MaxDurability == 600
                && total.Durability is double value
                && double.IsFinite(value)
                    ? Math.Clamp(value / 600, 0, 1)
                    : 1;
            bool hrt = shield.Template.ToString() == Shields.HrtShield;
            int bodyCap = hrt ? Shields.HrtBodyDurability : 500;
            EnsurePart(
                inventory,
                shield,
                "mod_shield_body",
                hrt ? Shields.HrtBodyPart : "692340510000000000000011",
                bodyCap,
                Math.Min(bodyCap, ReadDurability(legacy, "Body", 500 * fraction, 500))
            );
            if (hrt)
            {
                var body = inventory.Find(item =>
                    item.ParentId == shield.Id.ToString() && item.SlotId == "mod_shield_body"
                );
                if (body != null && body.Template.ToString() == "692340510000000000000011")
                {
                    body.Template = Shields.HrtBodyPart;
                    if (body.Upd?.Repairable is { } condition)
                    {
                        double cap = Math.Min(bodyCap, condition.MaxDurability ?? bodyCap);
                        condition.MaxDurability = cap;
                        condition.Durability = Math.Min(cap, condition.Durability ?? cap);
                    }
                }
            }
            EnsurePart(
                inventory,
                shield,
                "mod_shield_window",
                "692340510000000000000012",
                100,
                ReadDurability(legacy, "Window", 100 * fraction, 100)
            );

            if (upd == null)
                continue;
            upd.Repairable = null;
            if (upd.ExtensionData == null || !upd.ExtensionData.Remove(LegacyField))
                continue;
            if (
                !upd.ExtensionData.ContainsKey(CracksField)
                && legacy.ValueKind == JsonValueKind.Object
                && legacy.TryGetProperty("Cracks", out var cracks)
                && cracks.ValueKind == JsonValueKind.Array
            )
            {
                var points = new List<double[]>();
                foreach (var point in cracks.EnumerateArray())
                {
                    if (
                        point.ValueKind == JsonValueKind.Object
                        && TryNumber(point, "x", out double x)
                        && TryNumber(point, "y", out double y)
                    )
                        points.Add([x, y]);
                }
                if (points.Count > 0)
                    upd.ExtensionData[CracksField] = JsonSerializer.SerializeToElement(points);
            }
        }
    }

    private static void UpdateVantPart(
        List<Item> inventory,
        Item shield,
        string slot,
        string oldTemplate,
        int oldCap,
        (string Template, int Max) target
    )
    {
        var part = inventory.Find(item =>
            item.ParentId == shield.Id.ToString() && item.SlotId == slot
        );
        if (part == null || part.Template.ToString() != oldTemplate)
            return;

        part.Template = target.Template;
        if (part.Upd?.Repairable is not { } condition)
            return;
        double lostCapacity = Math.Max(0, oldCap - (condition.MaxDurability ?? oldCap));
        double cap = Math.Max(0, target.Max - lostCapacity);
        condition.MaxDurability = cap;
        condition.Durability = Math.Min(cap, condition.Durability ?? oldCap);
    }

    private static void EnsurePart(
        List<Item> inventory,
        Item parent,
        string slot,
        string template,
        int max,
        double current
    )
    {
        if (inventory.Any(item => item.ParentId == parent.Id.ToString() && item.SlotId == slot))
            return;
        inventory.Add(
            new Item
            {
                Id = new MongoId(),
                Template = template,
                ParentId = parent.Id.ToString(),
                SlotId = slot,
                Upd = new()
                {
                    Repairable = new() { Durability = current, MaxDurability = max },
                },
            }
        );
    }

    private static double ReadDurability(JsonElement state, string key, double fallback, int max) =>
        state.ValueKind == JsonValueKind.Object && TryNumber(state, key, out double value)
            ? Math.Clamp(value, 0, max)
            : fallback;

    private static bool TryNumber(JsonElement element, string key, out double value)
    {
        value = 0;
        return element.TryGetProperty(key, out var number)
            && number.ValueKind == JsonValueKind.Number
            && number.TryGetDouble(out value)
            && double.IsFinite(value);
    }
}
