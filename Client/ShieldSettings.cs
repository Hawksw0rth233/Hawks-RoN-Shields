using BepInEx.Configuration;
using EFT.InventoryLogic;
using UnityEngine;

namespace Hawks.RonShields
{
    internal static class ShieldSettings
    {
        internal static ConfigEntry<bool> Ergonomics,
            Movement,
            Backpack;
        static readonly string[] Names =
        {
            "Ballistic Shield",
            "HRT Ballistic Shield",
            "Vant LM C",
            "Vant LM O",
            "Vant VM",
        };
        static readonly ConfigEntry<float>[] Body = new ConfigEntry<float>[5],
            Window = new ConfigEntry<float>[5],
            Durability = new ConfigEntry<float>[5];

        internal static void Bind(ConfigFile config)
        {
            const string general = "00 General";
            Ergonomics = config.Bind(
                general,
                "Apply Ergonomics Penalties",
                true,
                "Apply the shield's ergonomics penalty while holding it."
            );
            Movement = config.Bind(
                general,
                "Apply Movement Speed Penalties",
                true,
                "Apply the shield's movement speed penalty while holding it."
            );
            Backpack = config.Bind(
                general,
                "Restrict Backpack and Shield Loadouts",
                false,
                "Prevent equipping a shield in either primary weapon slot while wearing a backpack, or equipping a backpack while a shield occupies either slot. Existing loadouts are preserved."
            );
            for (int i = 0; i < Names.Length; i++)
            {
                string section = $"0{i + 1} {Names[i]}";
                Durability[i] = config.Bind(
                    section,
                    "Durability Multiplier",
                    100f,
                    new ConfigDescription(
                        "Effective durability percentage for every armor part. Applies to existing shields without restoring their condition.",
                        new AcceptableValueRange<float>(0, 500)
                    )
                );
                Body[i] = config.Bind(
                    section,
                    "Shield Body Armor Class",
                    i == 0 ? 3f
                        : i == 1 ? 4.5f
                        : 5f,
                    ArmorDescription("shield body")
                );
                if (i != 2 && i != 3)
                    Window[i] = config.Bind(
                        section,
                        "Viewport Armor Class",
                        i < 2 ? 3f : 5f,
                        ArmorDescription("viewport")
                    );
            }
        }

        static ConfigDescription ArmorDescription(string part) =>
            new ConfigDescription(
                "Projectile protection level of the " + part + ".",
                new AcceptableValueRange<float>(0, 6.9f)
            );

        static int Index(Item item)
        {
            switch (item.TemplateId.ToString())
            {
                case "692340510000000000000001":
                    return 0;
                case "692340510000000000000002":
                    return 1;
                case "692340510000000000000003":
                    return 2;
                case "692340510000000000000004":
                    return 3;
                case "692340510000000000000005":
                    return 4;
                default:
                    return -1;
            }
        }

        internal static float Scale(Item item)
        {
            int i = Index(item);
            return i < 0 ? 1f : Durability[i].Value * .01f;
        }

        internal static float Armor(Item item, bool window)
        {
            int i = Index(item);
            if (i < 0)
                return window ? 4f : 5.5f;
            return Mathf.Round((window ? Window[i]?.Value ?? 0 : Body[i].Value) * 10f) * .1f;
        }
    }
}
