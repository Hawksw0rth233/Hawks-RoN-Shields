using EFT;
using EFT.Ballistics;
using HarmonyLib;
using Systems.Effects;
using UnityEngine;

namespace Hawks.RonShields
{
    internal static class ShieldImpactAudio
    {
        static Effects effects;

        internal static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(Effects.Effect), nameof(Effects.Effect.Emit)),
                prefix: new HarmonyMethod(typeof(ShieldImpactAudio), nameof(FilterDuplicate)));
        }

        internal static bool Play(MaterialType material, Vector3 point)
        {
            if (!effects) effects = Object.FindObjectOfType<Effects>();
            if (!effects || !BetterAudio.Instance) return false;
            var effect = effects.Get(material);
            var bank = effect?.Sound;
            if (!bank && material == MaterialType.GlassVisor) bank = effects.Get(MaterialType.Glass)?.Sound;
            if (!bank || bank.Environments == null || bank.Environments.Length == 0) return false;
            var variants = bank.Environments[0]?.Clips;
            if (variants == null || variants.Length == 0 || variants[0]?.Clips == null ||
                variants[0].Clips.Length == 0) return false;
            var clip = bank.PickSingleClip(0);
            return clip && BetterAudio.Instance.TryPlayAtPoint(out _, point, clip,
                BetterAudio.AudioSourceGroupType.Impacts, Mathf.CeilToInt(bank.Rolloff),
                bank.RandomVolume, EOcclusionTest.None, null, true, true, true, true);
        }

        // EFT may queue a stock visual effect for the very same contact.
        static void FilterDuplicate(BallisticCollider hitCollider, Vector3 position, ref bool withDecal, ref float volume, bool isKnife, bool isGrenade)
        {
            if (!(hitCollider is ShieldSurface surface)) return;
            if (surface.ConsumePlayedContact(position)) volume=0f;
            if (!withDecal || isKnife || isGrenade) return;
            withDecal=false;
        }
    }
}
