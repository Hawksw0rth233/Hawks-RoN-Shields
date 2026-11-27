using System.Collections.Generic;
using System.Reflection.Emit;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;

namespace Hawks.RonShields
{
    internal sealed class ShieldStats
    {
        static readonly ShieldStats Patrol = new ShieldStats(3, 3, -10, -15, -5);
        static readonly ShieldStats Hrt = new ShieldStats(4.5f, 3, -7, -15, -3);
        static readonly ShieldStats VantLm = new ShieldStats(5, 0, -5, 0, 0);
        static readonly ShieldStats VantVm = new ShieldStats(5, 5, -12, -10, -5);
        internal readonly float Body,
            Window,
            Ergonomics,
            Movement,
            Turning;

        ShieldStats(float body, float window, float ergonomics, float movement, float turning)
        {
            Body = body;
            Window = window;
            Ergonomics = ergonomics;
            Movement = movement;
            Turning = turning;
        }

        internal static ShieldStats For(Item item)
        {
            if (item?.TemplateId == "692340510000000000000001")
                return Patrol;
            if (item?.TemplateId == "692340510000000000000002")
                return Hrt;
            return item?.TemplateId == "692340510000000000000005" ? VantVm : VantLm;
        }

        internal bool HasPenalties => Movement != 0 || Ergonomics != 0 || Turning != 0;

        static ShieldStats Held(Player player)
        {
            var carry = ShieldCarry.Current;
            return carry && carry.HeldBy(player) ? For(carry.Selected) : null;
        }

        internal static void Install(Harmony harmony)
        {
            ShieldSettings.Ergonomics.SettingChanged += RefreshHeld;
            ShieldSettings.Movement.SettingChanged += RefreshHeld;
            harmony.Patch(
                AccessTools.Method(typeof(Player), nameof(Player.RecalculateEquipmentParams)),
                transpiler: new HarmonyMethod(typeof(ShieldStats), nameof(EquipmentPenalties))
            );
            harmony.Patch(
                AccessTools.Method(typeof(Player), nameof(Player.GetRotationMultiplier)),
                postfix: new HarmonyMethod(typeof(ShieldStats), nameof(Turn))
            );
        }

        static void RefreshHeld(object sender, System.EventArgs args)
        {
            var carry = ShieldCarry.Current;
            if (carry && carry.Owner)
                carry.Owner.RecalculateEquipmentParams();
        }

        static void AddPenalties(Player player, ref float movement, ref float ergonomics)
        {
            var stats = Held(player);
            if (stats == null)
                return;
            if (ShieldSettings.Movement.Value)
                movement += stats.Movement;
            if (ShieldSettings.Ergonomics.Value)
                ergonomics += stats.Ergonomics;
        }

        static void Turn(Player __instance, ref float __result)
        {
            var stats = Held(__instance);
            if (stats != null)
                __result *= (1f + stats.Turning * .01f) / .95f;
        }

        static IEnumerable<CodeInstruction> EquipmentPenalties(
            IEnumerable<CodeInstruction> instructions
        )
        {
            var code = new List<CodeInstruction>(instructions);
            var field = AccessTools.Field(typeof(Player), "_ergonomicsPenalty");
            for (int i = 2; i < code.Count - 2; i++)
            {
                if (
                    !code[i].LoadsField(field)
                    || code[i + 1].opcode != OpCodes.Ldc_R4
                    || !Equals(code[i + 1].operand, 100f)
                    || code[i + 2].opcode != OpCodes.Div
                    || code[i - 2].opcode != OpCodes.Ldarg_0
                    || code[i - 1].opcode != OpCodes.Ldarg_0
                )
                    continue;
                var entry = new CodeInstruction(OpCodes.Ldarg_0);
                entry.labels.AddRange(code[i - 2].labels);
                code[i - 2].labels.Clear();
                code.InsertRange(
                    i - 2,
                    new[]
                    {
                        entry,
                        new CodeInstruction(OpCodes.Ldloca_S, (byte)0),
                        new CodeInstruction(OpCodes.Ldarg_0),
                        new CodeInstruction(OpCodes.Ldflda, field),
                        new CodeInstruction(
                            OpCodes.Call,
                            AccessTools.Method(typeof(ShieldStats), nameof(AddPenalties))
                        ),
                    }
                );
                return code;
            }
            throw new System.InvalidOperationException(
                "Native armor penalty normalization not found."
            );
        }
    }
}
