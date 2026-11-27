using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using EFT;
using EFT.Ballistics;
using HarmonyLib;
using UnityEngine;

namespace Hawks.RonShields
{
    internal static class ShieldMelee
    {
        private static readonly ConditionalWeakTable<
            Player.BaseKnifeController,
            HashSet<ShieldProtection>
        > swings =
            new ConditionalWeakTable<Player.BaseKnifeController, HashSet<ShieldProtection>>();

        static readonly ConditionalWeakTable<
            Player.BaseKnifeController,
            HashSet<ShieldProtection>
        > soundSwings =
            new ConditionalWeakTable<Player.BaseKnifeController, HashSet<ShieldProtection>>();

        internal static void Install(Harmony harmony)
        {
            harmony.Patch(
                AccessTools.Method(typeof(KnifeCollider), "GetMaxAvailableDistance"),
                transpiler: Hook(nameof(ObserveRay))
            );
            harmony.Patch(
                AccessTools.Method(typeof(Player.BaseKnifeController), "ProcessHit"),
                postfix: Hook(nameof(OnContact))
            );
            harmony.Patch(
                AccessTools.Method(typeof(KnifeCollider), "OnFireEnd"),
                postfix: Hook(nameof(EndSwing))
            );
        }

        static HarmonyMethod Hook(string name) => new HarmonyMethod(typeof(ShieldMelee), name);

        static IEnumerable<CodeInstruction> ObserveRay(IEnumerable<CodeInstruction> instructions)
        {
            var original = AccessTools.Method(
                typeof(Physics),
                "Raycast",
                new[]
                {
                    typeof(Ray),
                    typeof(RaycastHit).MakeByRefType(),
                    typeof(float),
                    typeof(int),
                    typeof(QueryTriggerInteraction),
                }
            );
            int replaced = 0;
            foreach (var op in instructions)
            {
                if (!op.Calls(original))
                {
                    yield return op;
                    continue;
                }
                yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(op);
                yield return new CodeInstruction(
                    OpCodes.Call,
                    AccessTools.Method(typeof(ShieldMelee), nameof(Raycast))
                );
                replaced++;
            }
            if (replaced != 1)
                throw new InvalidOperationException(
                    "Unexpected native melee raycast count: " + replaced
                );
        }

        static readonly AccessTools.FieldRef<KnifeCollider, Player.BaseKnifeController> Controller =
            AccessTools.FieldRefAccess<KnifeCollider, Player.BaseKnifeController>("Knife");

        static bool Raycast(
            Ray ray,
            out RaycastHit hit,
            float range,
            int mask,
            QueryTriggerInteraction triggers,
            KnifeCollider knife
        )
        {
            bool found = Physics.Raycast(ray, out hit, range, mask, triggers);
            if (found && hit.collider.TryGetComponent<ShieldSurface>(out var surface))
                Contact(Controller(knife), surface, hit.point);
            return found;
        }

        static void OnContact(
            Player.BaseKnifeController __instance,
            BallisticCollider ballisticCollider,
            Player.KnifeRaycastHit hit
        )
        {
            if (ballisticCollider is ShieldSurface surface)
                Contact(__instance, surface, hit.point);
        }

        static void Contact(
            Player.BaseKnifeController attacker,
            ShieldSurface surface,
            Vector3 point
        )
        {
            var protection = surface.Protection;
            if (
                attacker?.Item == null
                || !protection
                || ReferenceEquals(protection.Wearer?.HandsController, attacker)
            )
                return;
            if (soundSwings.GetOrCreateValue(attacker).Add(protection))
                surface.Sound(point);
            if (!protection.CanReceiveMelee(attacker))
                return;
            float fraction = SwingFraction(attacker);
            if (fraction <= 0 || !swings.GetOrCreateValue(attacker).Add(protection))
                return;
            protection.DrainArms(fraction);
        }

        const float DeadBlowSeconds = 1.30f;

        internal static float Fraction(float length, float speed)
        {
            if (!(length > 0) || !(Mathf.Abs(speed) > .0001f))
                return 0;
            return Mathf.Clamp01(length / (Mathf.Abs(speed) * DeadBlowSeconds));
        }

        static float SwingFraction(Player.BaseKnifeController attacker)
        {
            if (attacker.Item.TemplateId == "6087e570b998180e9f76dc24")
                return 1f;
            var anim = attacker.FirearmsAnimator?.Animator;
            return AnimationFraction(anim);
        }

        internal static float AnimationFraction(IAnimator anim)
        {
            if (anim == null)
                return 0;
            int hands = -1;
            for (int i = 0; i < anim.layerCount; i++)
                if (anim.GetLayerName(i) == "Hands")
                {
                    hands = i;
                    break;
                }
            for (int pass = -1; pass < anim.layerCount; pass++)
            {
                int layer = pass < 0 ? hands : pass;
                if (layer < 0 || (pass >= 0 && layer == hands))
                    continue;
                var state = anim.GetCurrentAnimatorStateInfo(layer);
                if ((state.loop || state.length <= 0) && anim.IsInTransition(layer))
                    state = anim.GetNextAnimatorStateInfo(layer);
                // Both EFT animator implementations return state length with its playback multiplier applied.
                if (state.length <= 0 || (state.loop && layer != hands))
                    continue;
                return Fraction(
                    state.length,
                    anim is AnimationSystem.UnityAnimatorWrapper ? 1f : anim.speed
                );
            }
            return 0;
        }

        static void EndSwing(KnifeCollider __instance)
        {
            var attacker = Controller(__instance);
            if (attacker == null)
                return;
            swings.Remove(attacker);
            soundSwings.Remove(attacker);
        }
    }
}
