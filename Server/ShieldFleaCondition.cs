using System.Reflection.Emit;
using HarmonyLib;
using SPTarkov.Server.Core.Generators.Ragfair;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;

namespace Hawks.RonShields;

internal static class ShieldFleaCondition
{
    internal static void Install()
    {
        var harmony = new Harmony("com.hawks.ronshields.native-flea-condition");
        var generator = typeof(RagfairOfferGenerator);
        var betterFlea = AccessTools.TypeByName("BetterFlea.Offers.PresetOfferGate");
        if (betterFlea != null)
        {
            var rebuild = AccessTools.Method(betterFlea, "MaybeRandomBuild", [typeof(List<Item>)]);
            if (rebuild != null)
                harmony.Patch(rebuild, prefix: Hook(nameof(KeepFactoryShield)));
        }
        harmony.Patch(
            AccessTools.Method(generator, "GetDynamicConditionIdForTpl"),
            postfix: Hook(nameof(UseArmorCondition))
        );
        harmony.Patch(
            AccessTools.Method(generator, "RandomiseItemCondition"),
            transpiler: Hook(nameof(RouteArmorCondition))
        );
    }

    private static HarmonyMethod Hook(string name) =>
        new(AccessTools.Method(typeof(ShieldFleaCondition), name));

    internal static bool IsShield(MongoId id) =>
        id.ToString()
            is "692340510000000000000001"
                or "692340510000000000000002"
                or "692340510000000000000003"
                or "692340510000000000000004"
                or "692340510000000000000005";

    // BetterFlea's random weapon builds can replace the fixed factory preset.
    private static bool KeepFactoryShield(List<Item> __0) =>
        __0.Count == 0 || !IsShield(__0[0].Template);

    private static void UseArmorCondition(MongoId tpl, ref MongoId? __result)
    {
        if (IsShield(tpl))
            __result = BaseClasses.ARMOR;
    }

    private static IEnumerable<CodeInstruction> RouteArmorCondition(
        IEnumerable<CodeInstruction> code
    )
    {
        var original = AccessTools.Method(
            typeof(ItemHelper),
            nameof(ItemHelper.ArmorItemCanHoldMods)
        );
        var replacement = AccessTools.Method(
            typeof(ShieldFleaCondition),
            nameof(HasArmorConditionParts)
        );
        int matches = 0;
        foreach (var instruction in code)
        {
            if (instruction.Calls(original))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = replacement;
                matches++;
            }
            yield return instruction;
        }
        if (matches != 1)
            throw new InvalidOperationException("Unexpected flea armor-condition branch.");
    }

    // Only the native flea condition calculation sees the shield as an armor carrier.
    private static bool HasArmorConditionParts(ItemHelper helper, MongoId tpl) =>
        IsShield(tpl) || helper.ArmorItemCanHoldMods(tpl);
}
