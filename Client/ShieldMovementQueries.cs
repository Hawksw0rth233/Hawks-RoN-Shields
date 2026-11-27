using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace Hawks.RonShields
{
    // Substitute only the immediate movement queries. Projectile casts and other players
    // continue to see the actual shield surfaces, even during the same physics tick.
    internal static class ShieldMovementQueries
    {
        [ThreadStatic]
        static Collider[] overlaps;

        [ThreadStatic]
        static RaycastHit[] hits;

        internal static void Install(Harmony harmony)
        {
            var patch = new HarmonyMethod(typeof(ShieldMovementQueries), nameof(ReplaceQueries));
            foreach (
                var name in new[]
                {
                    "CanStandAt",
                    "OverlapOrHasNoGround",
                    "HeadBump",
                    "GetSpherePenetrationDepth",
                    "get_CanProne",
                }
            )
                harmony.Patch(
                    AccessTools.DeclaredMethod(typeof(MovementContext), name),
                    transpiler: patch
                );
            harmony.Patch(
                AccessTools.DeclaredMethod(
                    typeof(MovementContext),
                    "CanRoll",
                    new[] { typeof(int), typeof(Vector3), typeof(Vector3) }
                ),
                transpiler: patch
            );
            harmony.Patch(
                AccessTools.DeclaredMethod(
                    typeof(SimpleCharacterController),
                    "FindSomethingToStepUp"
                ),
                transpiler: patch
            );
        }

        static IEnumerable<CodeInstruction> ReplaceQueries(
            IEnumerable<CodeInstruction> instructions,
            MethodBase __originalMethod
        )
        {
            foreach (var instruction in instructions)
            {
                if (
                    instruction.operand is MethodInfo method
                    && method.DeclaringType == typeof(Physics)
                )
                {
                    var parameters = method
                        .GetParameters()
                        .Select(p => p.ParameterType)
                        .Concat(new[] { typeof(object) })
                        .ToArray();
                    var replacement = AccessTools.DeclaredMethod(
                        typeof(ShieldMovementQueries),
                        method.Name,
                        parameters
                    );
                    if (replacement != null)
                    {
                        var load = new CodeInstruction(OpCodes.Ldarg_0);
                        load.labels.AddRange(instruction.labels);
                        load.blocks.AddRange(instruction.blocks);
                        yield return load;
                        if (__originalMethod.DeclaringType == typeof(MovementContext))
                            yield return new CodeInstruction(
                                OpCodes.Ldfld,
                                AccessTools.Field(typeof(MovementContext), "_player")
                            );
                        yield return new CodeInstruction(OpCodes.Call, replacement);
                        continue;
                    }
                }
                yield return instruction;
            }
        }

        static bool Own(Collider collider, object mover)
        {
            if (mover is Player player)
                return ShieldOwnerContacts.WearerOf(collider) == player
                    || (
                        collider
                        && collider.TryGetComponent<ShieldSurface>(out var surface)
                        && surface.Protection
                        && surface.Protection.IsCarriedBy(player)
                    );
            var wearer = ShieldOwnerContacts.WearerOf(collider);
            if (!wearer)
                return false;
            if (mover is SimpleCharacterController controller)
                return ShieldOwnerContacts.MatchesController(wearer, controller);
            return false;
        }

        static int OverlapBoxNonAlloc(
            Vector3 center,
            Vector3 extents,
            Collider[] result,
            Quaternion rotation,
            int mask,
            QueryTriggerInteraction triggers,
            object mover
        )
        {
            var source = result;
            while (true)
            {
                int count = Physics.OverlapBoxNonAlloc(
                    center,
                    extents,
                    source,
                    rotation,
                    mask,
                    triggers
                );
                int kept = 0;
                for (int i = 0; i < count; i++)
                    if (!Own(source[i], mover) && kept < result.Length)
                        result[kept++] = source[i];
                if (count < source.Length || kept == result.Length)
                    return kept;
                // The shield may have filled the original buffer and hidden a wall behind it.
                source = new Collider[checked(source.Length * 2)];
            }
        }

        static bool CheckBox(
            Vector3 center,
            Vector3 extents,
            Quaternion rotation,
            int mask,
            QueryTriggerInteraction triggers,
            object mover
        )
        {
            if (!Physics.CheckBox(center, extents, rotation, mask, triggers))
                return false;
            if (overlaps == null)
                overlaps = new Collider[16];
            return OverlapBoxNonAlloc(center, extents, overlaps, rotation, mask, triggers, mover)
                > 0;
        }

        static bool CheckSphere(
            Vector3 center,
            float radius,
            int mask,
            QueryTriggerInteraction triggers,
            object mover
        )
        {
            if (!Physics.CheckSphere(center, radius, mask, triggers))
                return false;
            if (overlaps == null)
                overlaps = new Collider[16];
            while (true)
            {
                int count = Physics.OverlapSphereNonAlloc(center, radius, overlaps, mask, triggers);
                for (int i = 0; i < count; i++)
                    if (!Own(overlaps[i], mover))
                        return true;
                if (count < overlaps.Length)
                    return false;
                Array.Resize(ref overlaps, checked(overlaps.Length * 2));
            }
        }

        enum Shape
        {
            Ray,
            Sphere,
            Capsule,
        }

        static bool NextHit(
            Shape shape,
            Vector3 origin,
            Vector3 end,
            float radius,
            Vector3 direction,
            float distance,
            int mask,
            QueryTriggerInteraction triggers,
            object mover,
            out RaycastHit hit
        )
        {
            if (hits == null)
                hits = new RaycastHit[16];
            while (true)
            {
                int count;
                if (shape == Shape.Ray)
                    count = Physics.RaycastNonAlloc(
                        origin,
                        direction,
                        hits,
                        distance,
                        mask,
                        triggers
                    );
                else if (shape == Shape.Sphere)
                    count = Physics.SphereCastNonAlloc(
                        origin,
                        radius,
                        direction,
                        hits,
                        distance,
                        mask,
                        triggers
                    );
                else
                    count = Physics.CapsuleCastNonAlloc(
                        origin,
                        end,
                        radius,
                        direction,
                        hits,
                        distance,
                        mask,
                        triggers
                    );
                if (count == hits.Length)
                {
                    Array.Resize(ref hits, checked(hits.Length * 2));
                    continue;
                }
                hit = default;
                bool found = false;
                for (int i = 0; i < count; i++)
                    if (
                        !Own(hits[i].collider, mover) && (!found || hits[i].distance < hit.distance)
                    )
                    {
                        found = true;
                        hit = hits[i];
                    }
                return found;
            }
        }

        static bool Raycast(
            Ray ray,
            float distance,
            int mask,
            QueryTriggerInteraction triggers,
            object mover
        )
        {
            if (!Physics.Raycast(ray, out var hit, distance, mask, triggers))
                return false;
            return !Own(hit.collider, mover)
                || NextHit(
                    Shape.Ray,
                    ray.origin,
                    default,
                    0,
                    ray.direction,
                    distance,
                    mask,
                    triggers,
                    mover,
                    out hit
                );
        }

        static bool SphereCast(
            Ray ray,
            float radius,
            out RaycastHit hit,
            float distance,
            int mask,
            QueryTriggerInteraction triggers,
            object mover
        )
        {
            if (!Physics.SphereCast(ray, radius, out hit, distance, mask, triggers))
                return false;
            return !Own(hit.collider, mover)
                || NextHit(
                    Shape.Sphere,
                    ray.origin,
                    default,
                    radius,
                    ray.direction,
                    distance,
                    mask,
                    triggers,
                    mover,
                    out hit
                );
        }

        static bool SphereCast(
            Vector3 origin,
            float radius,
            Vector3 direction,
            out RaycastHit hit,
            float distance,
            int mask,
            object mover
        ) =>
            SphereCast(
                new Ray(origin, direction),
                radius,
                out hit,
                distance,
                mask,
                QueryTriggerInteraction.UseGlobal,
                mover
            );

        static bool CapsuleCast(
            Vector3 start,
            Vector3 end,
            float radius,
            Vector3 direction,
            out RaycastHit hit,
            float distance,
            int mask,
            QueryTriggerInteraction triggers,
            object mover
        )
        {
            if (
                !Physics.CapsuleCast(
                    start,
                    end,
                    radius,
                    direction,
                    out hit,
                    distance,
                    mask,
                    triggers
                )
            )
                return false;
            return !Own(hit.collider, mover)
                || NextHit(
                    Shape.Capsule,
                    start,
                    end,
                    radius,
                    direction,
                    distance,
                    mask,
                    triggers,
                    mover,
                    out hit
                );
        }
    }
}
