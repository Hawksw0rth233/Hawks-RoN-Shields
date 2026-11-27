using System.Collections.Generic;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace Hawks.RonShields
{
    public sealed class ShieldOwnerContacts : MonoBehaviour
    {
        Player owner;
        Collider[] shield;
        readonly List<Collider> shieldParts = new List<Collider>();
        readonly HashSet<Collider> registered = new HashSet<Collider>();
        static readonly Dictionary<Collider, ShieldOwnerContacts> hostParts =
            new Dictionary<Collider, ShieldOwnerContacts>();

        struct Prior
        {
            internal bool unity,
                eft;
        }

        string backpack;
        bool sampled;
        float traceUntil;
        float nextTrace;
        readonly List<Collider> found = new List<Collider>();
        readonly HashSet<Collider> active = new HashSet<Collider>();
        readonly Dictionary<Collider, Dictionary<Collider, Prior>> ignored =
            new Dictionary<Collider, Dictionary<Collider, Prior>>();
        readonly List<Collider> expired = new List<Collider>();

        internal static void Install(Harmony harmony)
        {
            ShieldMovementQueries.Install(harmony);
            harmony.Patch(
                AccessTools.Method(typeof(SimpleCharacterController), "IsColliderIgnored"),
                postfix: new HarmonyMethod(typeof(ShieldOwnerContacts), nameof(Movement))
            );
            harmony.Patch(
                AccessTools.Method(typeof(Player.FirearmController), "IsOverlapHitIgnored"),
                postfix: new HarmonyMethod(typeof(ShieldOwnerContacts), nameof(Muzzle))
            );
            harmony.Patch(
                AccessTools.DeclaredMethod(
                    typeof(PlayerBody.SlotView),
                    "CreateAndParent",
                    new[] { typeof(PlayerBody), typeof(GameObject) }
                ),
                postfix: new HarmonyMethod(typeof(ShieldOwnerContacts), nameof(AfterSlotParented))
            );
            harmony.Patch(
                AccessTools.DeclaredMethod(
                    typeof(PlayerBody.SlotView),
                    "ChangePositionAfterBackpackChanged"
                ),
                postfix: new HarmonyMethod(typeof(ShieldOwnerContacts), nameof(AfterBackpackChange))
            );
            Debug.Log("[RoN Shields] slot reparent contact patch v3 ready");
        }

        static void AfterBackpackChange(PlayerBody.SlotView __instance)
        {
            var item = __instance.ContainedItem?.Value;
            if (item == null || !ShieldItemPreview.IsShield(item))
                return;
            var model = __instance.Model;
            var display = model ? model.GetComponentInChildren<ShieldDisplay>(true) : null;
            Debug.Log(
                $"[RoN Shields] backpack slot callback shield={item.Id} model={model?.name} display={!!display}"
            );
        }

        static void AfterSlotParented(PlayerBody __0, GameObject __1)
        {
            if (!__1)
                return;
            var display = __1.GetComponentInChildren<ShieldDisplay>(true);
            if (!display || !display.Visual)
                return;
            display.AfterSlotParented(__0);
        }

        internal void RefreshAfterReparent()
        {
            if (isActiveAndEnabled)
                Refresh();
            if (!owner || !owner.IsYourPlayer)
                return;
            nextTrace = Time.unscaledTime;
            traceUntil = nextTrace + 12f;
        }

        internal static Player WearerOf(Collider col)
        {
            if (col && col.TryGetComponent<ShieldSurface>(out var surface) && surface.Protection)
                return surface.Protection.Wearer;
            return col && hostParts.TryGetValue(col, out var contacts) && contacts
                ? contacts.owner
                : null;
        }

        static void Movement(SimpleCharacterController __instance, Collider __0, ref bool __result)
        {
            if (__result)
                return;
            var wearer = WearerOf(__0);
            if (wearer && MatchesController(wearer, __instance))
                __result = true;
        }

        internal static bool MatchesController(
            Player wearer,
            SimpleCharacterController controller
        ) =>
            ReferenceEquals(wearer.CharacterController, controller)
            || controller.transform.IsChildOf(wearer.gameObject.transform);

        static void Muzzle(Player ____player, RaycastHit __0, ref bool __result)
        {
            if (!__result && ____player && WearerOf(__0.collider) == ____player)
                __result = true;
        }

        internal void Bind(Player wearer, Collider[] surfaces)
        {
            ResetPairs();
            owner = wearer;
            shield = surfaces;
            if (isActiveAndEnabled)
                Refresh();
        }

        void FixedUpdate() => Refresh();

        void OnEnable() => Refresh();

        void OnDisable()
        {
            ResetPairs();
            ClearRegistration();
        }

        void ClearRegistration()
        {
            foreach (var c in registered)
                if (hostParts.TryGetValue(c, out var previous) && previous == this)
                    hostParts.Remove(c);
            registered.Clear();
            shieldParts.Clear();
        }

        void CollectParts()
        {
            var display = GetComponentInParent<ShieldDisplay>();
            if (display && display.Visual == gameObject)
                display.GetComponentsInChildren(true, shieldParts);
            else
            {
                shieldParts.Clear();
                shieldParts.AddRange(shield);
            }
            foreach (var part in shieldParts)
                if (part && registered.Add(part))
                    hostParts[part] = this;
            expired.Clear();
            foreach (var part in registered)
                if (!part || !shieldParts.Contains(part))
                    expired.Add(part);
            foreach (var part in expired)
            {
                if (hostParts.TryGetValue(part, out var previous) && previous == this)
                    hostParts.Remove(part);
                registered.Remove(part);
            }
        }

        void Refresh()
        {
            if (!owner || shield == null)
                return;
            CollectParts();
            owner.GetComponentsInChildren(false, found);
            var capsule = owner.CharacterController?.GetCollider();
            if (capsule && !found.Contains(capsule))
                found.Add(capsule);
            active.Clear();
            foreach (var body in found)
            {
                if (!body.enabled || body.isTrigger || registered.Contains(body))
                    continue;
                active.Add(body);
                if (!ignored.TryGetValue(body, out var changed))
                {
                    changed = new Dictionary<Collider, Prior>();
                    ignored.Add(body, changed);
                }

                expired.Clear();
                foreach (var part in changed.Keys)
                    if (!registered.Contains(part))
                        expired.Add(part);
                foreach (var part in expired)
                {
                    RestorePair(body, part, changed[part]);
                    changed.Remove(part);
                }
                foreach (var part in shieldParts)
                {
                    if (
                        !part
                        || part == body
                        || !part.enabled
                        || !part.gameObject.activeInHierarchy
                    )
                        continue;
                    bool unity = Physics.GetIgnoreCollision(part, body),
                        eft = PhysicsExtensions.GetIgnoreCollision(part, body);
                    if (unity && eft)
                        continue;
                    if (!changed.ContainsKey(part))
                        changed.Add(part, new Prior { unity = unity, eft = eft });
                    PhysicsExtensions.IgnoreCollision(part, body, true);
                }
            }
            expired.Clear();
            foreach (var entry in ignored)
                if (!active.Contains(entry.Key))
                    expired.Add(entry.Key);
            foreach (var body in expired)
            {
                Restore(body, ignored[body]);
                ignored.Remove(body);
            }
            if (owner.IsYourPlayer)
            {
                var equipped = owner
                    .Inventory?.Equipment?.GetSlot(EFT.InventoryLogic.EquipmentSlot.Backpack)
                    ?.ContainedItem?.Id.ToString();
                if (sampled && backpack != null && equipped == null)
                {
                    nextTrace = Time.unscaledTime;
                    traceUntil = nextTrace + 12f;
                    Debug.Log(
                        "[RoN Shields] backpack dropped; tracking wearer capsule contacts for 12s"
                    );
                }
                sampled = true;
                backpack = equipped;
                if (capsule && Time.unscaledTime < traceUntil && Time.unscaledTime >= nextTrace)
                {
                    nextTrace = Time.unscaledTime + 1.5f;
                    var b = capsule.bounds;
                    foreach (
                        var c in Physics.OverlapBox(
                            b.center,
                            b.extents,
                            Quaternion.identity,
                            ~0,
                            QueryTriggerInteraction.Ignore
                        )
                    )
                    {
                        if (
                            !c
                            || c == capsule
                            || !Physics.ComputePenetration(
                                capsule,
                                capsule.transform.position,
                                capsule.transform.rotation,
                                c,
                                c.transform.position,
                                c.transform.rotation,
                                out var dir,
                                out var depth
                            )
                        )
                            continue;
                        Debug.Log(
                            $"[RoN Shields] capsule overlap {c.name} root={c.transform.root.name} layer={c.gameObject.layer} shield={registered.Contains(c)} unityIgnore={Physics.GetIgnoreCollision(c, capsule)} eftIgnore={PhysicsExtensions.GetIgnoreCollision(c, capsule)} depth={depth:F3} direction={dir}"
                        );
                    }
                }
            }
        }

        internal void ResetPairs()
        {
            foreach (var entry in ignored)
                Restore(entry.Key, entry.Value);
            ignored.Clear();
        }

        static void Restore(Collider body, Dictionary<Collider, Prior> parts)
        {
            foreach (var entry in parts)
                RestorePair(body, entry.Key, entry.Value);
        }

        static void RestorePair(Collider body, Collider part, Prior prior)
        {
            if (!body || !part)
                return;
            PhysicsExtensions.IgnoreCollision(part, body, prior.eft);
            if (prior.unity != prior.eft)
                Physics.IgnoreCollision(part, body, prior.unity);
        }
    }
}
