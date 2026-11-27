using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using EFT.InventoryLogic;
using HarmonyLib;

namespace Hawks.RonShields
{
    internal static class AssCompatibility
    {
        static readonly EquipmentSlot[] PrimarySlots =
        {
            EquipmentSlot.FirstPrimaryWeapon,
            EquipmentSlot.SecondPrimaryWeapon,
        };

        static ConfigEntry<bool> repairShields;
        static AccessTools.FieldRef<InventoryController> inventory;

        internal static void Install(Harmony harmony, ConfigFile config, ManualLogSource logger)
        {
            var plugin = Chainloader.PluginInfos.Values.FirstOrDefault(info =>
                string.Equals(
                    info.Metadata.GUID,
                    "com.dvize.ass",
                    StringComparison.OrdinalIgnoreCase
                )
            );
            if (plugin?.Instance == null)
                return;

            var component = plugin.Instance.GetType().Assembly.GetType("armorMod.AssComponent");
            var field =
                component == null ? null : AccessTools.Field(component, "inventoryController");
            var repair =
                component == null
                    ? null
                    : AccessTools.Method(
                        component,
                        "RepairItems",
                        new[] { typeof(List<EquipmentSlot>), typeof(bool) }
                    );
            if (
                field == null
                || field.FieldType != typeof(InventoryController)
                || !field.IsStatic
                || repair == null
                || !repair.IsStatic
            )
            {
                logger.LogWarning(
                    "This A.S.S. build does not expose the expected equipment repair routine."
                );
                return;
            }

            inventory = AccessTools.StaticFieldRefAccess<InventoryController>(field);
            repairShields = config.Bind(
                "Compatibility",
                "A.S.S. shield repair",
                false,
                "Allow Arse Scratching Service's armor repair to restore the shield body and viewport."
            );
            harmony.Patch(
                repair,
                prefix: new HarmonyMethod(typeof(AssCompatibility), nameof(FilterRepairSlots))
            );
        }

        static void FilterRepairSlots(ref List<EquipmentSlot> __0, bool __1)
        {
            var equipment = inventory()?.Inventory?.Equipment;
            if (equipment == null)
                return;

            if (__1)
            {
                if (!repairShields.Value)
                    return;
                foreach (var slot in PrimarySlots)
                {
                    if (
                        __0.Contains(slot)
                        || !ShieldItemPreview.IsShield(equipment.GetSlot(slot)?.ContainedItem)
                    )
                        continue;
                    __0 = new List<EquipmentSlot>(__0) { slot };
                }
            }
            else if (
                __0.Any(slot => ShieldItemPreview.IsShield(equipment.GetSlot(slot)?.ContainedItem))
            )
            {
                __0 = __0.FindAll(slot =>
                    !ShieldItemPreview.IsShield(equipment.GetSlot(slot)?.ContainedItem)
                );
            }
        }
    }
}
