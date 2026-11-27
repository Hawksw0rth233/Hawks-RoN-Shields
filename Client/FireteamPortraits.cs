using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;

namespace Hawks.RonShields
{
    internal static class FireteamPortraits
    {
        static readonly EquipmentSlot[] WeaponSlots =
        {
            EquipmentSlot.FirstPrimaryWeapon,
            EquipmentSlot.SecondPrimaryWeapon,
            EquipmentSlot.Holster,
            EquipmentSlot.Scabbard,
        };

        internal static void Install(Harmony harmony, ManualLogSource logger)
        {
            var menu = AccessTools.TypeByName("pitTeam.Components.SquadControlMenuUi");
            if (menu == null)
                return;

            var coroutine = AccessTools.Method(menu, "LoadTeammatePortraitCoroutine");
            var moveNext = coroutine == null ? null : AccessTools.EnumeratorMoveNext(coroutine);
            if (moveNext == null)
            {
                logger.LogWarning(
                    "PiT Fireteam portrait compatibility could not find the roster renderer."
                );
                return;
            }

            try
            {
                harmony.Patch(
                    moveNext,
                    transpiler: new HarmonyMethod(typeof(FireteamPortraits), nameof(ReplaceRequest))
                );
            }
            catch (Exception error)
            {
                logger.LogWarning(
                    $"PiT Fireteam portrait compatibility could not be applied: {error.Message}"
                );
            }
        }

        static IEnumerable<CodeInstruction> ReplaceRequest(
            IEnumerable<CodeInstruction> instructions
        )
        {
            var constructor = AccessTools.Constructor(
                typeof(PlayerIconRequest),
                new[] { typeof(InventoryEquipment), typeof(BodyCustomization) }
            );
            var code = instructions.ToList();
            var request = code.Single(instruction =>
                instruction.opcode == OpCodes.Newobj && Equals(instruction.operand, constructor)
            );
            request.opcode = OpCodes.Call;
            request.operand = AccessTools.Method(
                typeof(FireteamPortraits),
                nameof(CreatePortraitRequest)
            );
            return code;
        }

        static PlayerIconRequest CreatePortraitRequest(
            InventoryEquipment equipment,
            BodyCustomization customization
        )
        {
            if (!WeaponSlots.Any(slot => Shields.Model(equipment.GetSlot(slot)?.ContainedItem)))
                return new PlayerIconRequest(equipment, customization);

            var factory = Singleton<ItemFactory>.Instance;
            var tree = factory.FlatItemsToTree(factory.TreeToFlatItems(equipment));
            if (
                !tree.Items.TryGetValue(equipment.Id, out var root)
                || !(root is InventoryEquipment copy)
            )
                return new PlayerIconRequest(equipment, customization);
            foreach (var slot in WeaponSlots)
            {
                var copySlot = copy.GetSlot(slot);
                if (Shields.Model(copySlot?.ContainedItem))
                    copySlot.RemoveItemWithoutRestrictions();
            }
            return new PlayerIconRequest(copy, customization);
        }
    }
}
