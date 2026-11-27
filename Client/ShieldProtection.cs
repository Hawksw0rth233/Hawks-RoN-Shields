using System;
using System.Collections.Generic;
using EFT;
using EFT.Ballistics;
using EFT.InventoryLogic;
using UnityEngine;

namespace Hawks.RonShields
{
    [Serializable]
    internal sealed class ShieldCondition
    {
        const string BodySlot = "mod_shield_body";
        const string WindowSlot = "mod_shield_window";
        const string CrackField = "HawksShieldCracks";
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
            Item,
            ShieldCondition
        > states = new System.Runtime.CompilerServices.ConditionalWeakTable<
            Item,
            ShieldCondition
        >();

        readonly Item shield;
        public List<Vector2> Cracks = new List<Vector2>();

        ShieldCondition(Item shield) => this.shield = shield;

        RepairableComponent Condition(string id)
        {
            if (shield is not CompoundItem compound)
                return null;
            foreach (var slot in compound.Slots)
                if (slot.ID == id)
                    return slot.ContainedItem?.GetItemComponent<RepairableComponent>();
            return null;
        }

        public float Body
        {
            get => Condition(BodySlot)?.Durability ?? 0;
            set
            {
                var part = Condition(BodySlot);
                if (part != null)
                    part.Durability = value;
            }
        }
        public float Window
        {
            get => Condition(WindowSlot)?.Durability ?? 0;
            set
            {
                var part = Condition(WindowSlot);
                if (part != null)
                    part.Durability = value;
            }
        }
        public float BodyMax => Condition(BodySlot)?.MaxDurability ?? 0;
        public float WindowMax => Condition(WindowSlot)?.MaxDurability ?? 0;

        internal static ShieldCondition Load(Item item) =>
            states.GetValue(item, x => new ShieldCondition(x));

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            harmony.Patch(
                HarmonyLib.AccessTools.Constructor(
                    typeof(Weapon),
                    new[] { typeof(string), typeof(WeaponTemplate) }
                ),
                postfix: new HarmonyLib.HarmonyMethod(
                    typeof(ShieldCondition),
                    nameof(RemoveWeaponCondition)
                )
            );
            harmony.Patch(
                HarmonyLib.AccessTools.Method(typeof(ItemDeserializer), "GetItemProperties"),
                postfix: new HarmonyLib.HarmonyMethod(typeof(ShieldCondition), nameof(WriteItem))
            );
            harmony.Patch(
                HarmonyLib.AccessTools.Method(typeof(ItemDeserializer), "CreateItem"),
                prefix: new HarmonyLib.HarmonyMethod(typeof(ShieldCondition), nameof(ReadItem)),
                postfix: new HarmonyLib.HarmonyMethod(typeof(ShieldCondition), nameof(ApplyItem))
            );
        }

        static void RemoveWeaponCondition(Weapon __instance)
        {
            if (!ShieldItemPreview.IsShield(__instance))
                return;
            __instance.Components.Remove(__instance.Repairable);
            __instance.Components.Add(
                new ArmorHolderComponent(__instance, ShieldArmorHolder.Instance)
            );
        }

        sealed class ShieldArmorHolder : IArmorComponentTemplate
        {
            internal static readonly ShieldArmorHolder Instance = new ShieldArmorHolder();
            public EBodyPartColliderType[] ArmorColliders => Array.Empty<EBodyPartColliderType>();
            public EArmorPlateCollider ArmorPlateColliders => default;
            public int ArmorClass => 0;
            public MaterialType Material => default;
            public EDeafStrength Deaf => default;
            public float BluntThroughput => 0;
            public Vector3 RicochetVals => Vector3.zero;
            public EArmorMaterial ArmorMaterial => default;
            public EArmorType ArmorType => default;
            public float SpeedPenaltyPercent => 0;
            public float MousePenalty => 0;
            public float WeaponErgonomicPenalty => 0;
        }

        static void WriteItem(Item __0, Dictionary<string, object> __result)
        {
            if (!ShieldItemPreview.IsShield(__0))
                return;
            var hp = Load(__0);
            if (hp.Cracks.Count > 0)
                __result[CrackField] = new Newtonsoft.Json.Linq.JArray(
                    hp.Cracks.ConvertAll(v => new Newtonsoft.Json.Linq.JArray(v.x, v.y))
                );
        }

        static void ReadItem(Item __0, ref UnparsedData __1, out List<Vector2> __state)
        {
            __state = null;
            if (
                !ShieldItemPreview.IsShield(__0)
                || !(__1?.JToken is Newtonsoft.Json.Linq.JObject upd)
                || upd[CrackField] == null
            )
                return;
            __state = new List<Vector2>();
            foreach (var point in upd[CrackField])
                __state.Add(new Vector2((float)point[0], (float)point[1]));
            var native = (Newtonsoft.Json.Linq.JObject)upd.DeepClone();
            native.Remove(CrackField);
            __1 = new UnparsedData { JToken = native };
        }

        static void ApplyItem(Item __result, List<Vector2> __state)
        {
            if (__state != null)
                Load(__result).Cracks = __state;
        }
    }

    public sealed class ShieldSurface : BallisticCollider
    {
        internal ShieldProtection Protection;
        internal bool Window;

        struct ContactSound
        {
            public int Frame;
            public Vector3 Point;
        }

        readonly List<ContactSound> played = new List<ContactSound>();

        internal void Sound(Vector3 point)
        {
            if (!ShieldImpactAudio.Play(TypeOfMaterial, point))
                return;
            TrimSounds();
            played.Add(new ContactSound { Frame = Time.frameCount, Point = point });
        }

        void TrimSounds()
        {
            int oldest = Time.frameCount - 2;
            for (int i = played.Count - 1; i >= 0; i--)
                if (played[i].Frame < oldest)
                    played.RemoveAt(i);
        }

        internal bool ConsumePlayedContact(Vector3 point)
        {
            TrimSounds();
            for (int i = 0; i < played.Count; i++)
            {
                if ((played[i].Point - point).sqrMagnitude > .0001f)
                    continue;
                played.RemoveAt(i);
                return true;
            }
            return false;
        }

        public override PlayerHitInfo ApplyHit(DamageInfo info, ShotId id)
        {
            if (Protection && info.DamageType == EDamageType.Bullet)
                Protection.Hit(this, info, id);
            return base.ApplyHit(info, id);
        }
    }

    public sealed class ShieldProtection : MonoBehaviour
    {
        ShieldCondition state;
        Player holder;
        internal Player Wearer { get; private set; }

        internal bool IsCarriedBy(Player player) =>
            player
            && worn
            && (
                Wearer == player
                || (shieldItem != null && Shields.IsInPrimarySlot(player, shieldItem))
            );

        ShieldOwnerContacts ownerContacts;

        internal void SetWearer(Player player)
        {
            if (Wearer == player)
                return;
            Wearer = player;
            if (!ownerContacts)
                ownerContacts = gameObject.AddComponent<ShieldOwnerContacts>();
            ownerContacts.Bind(player, colliders);
        }

        Item shieldItem;
        MeshCollider[] colliders;
        bool[] windowParts;
        Bounds windowBounds;
        WindowCracks cracks;
        readonly HashSet<ShotId> bodyShots = new HashSet<ShotId>(),
            windowShots = new HashSet<ShotId>();
        readonly HashSet<ShotId> staminaShots = new HashSet<ShotId>();
        bool worn = true;
        float lastScale = -1,
            lastBodyClass = -1,
            lastWindowClass = -1;
        bool lastBodyIntact,
            lastWindowIntact;

        internal static bool IsViewport(MeshFilter part)
        {
            var bounds = part.sharedMesh.bounds;
            return part.name == "VantViewport"
                || part.name == "Window"
                    && bounds.size.x > .25f
                    && bounds.center.y > .15f
                    && bounds.center.y < .35f;
        }

        internal static ShieldProtection Attach(GameObject visual, Item item, Player player)
        {
            var instance = visual.AddComponent<ShieldProtection>();
            instance.shieldItem = item;
            instance.holder = player;
            instance.state = ShieldCondition.Load(item);
            int layer = LayerMask.NameToLayer("TransparentCollider");
            if (layer < 0)
                throw new InvalidOperationException("Missing SPT projectile collider layer");
            var ancestorBody = visual.GetComponentInParent<Rigidbody>();
            if (ancestorBody && !ancestorBody.isKinematic)
            {
                var body = visual.GetComponent<Rigidbody>() ?? visual.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
            }
            var pieces = System.Array.FindAll(
                visual.GetComponentsInChildren<MeshFilter>(true),
                part => part.sharedMesh && !ShieldImpactMarks.IsImpactMesh(part.sharedMesh)
            );
            instance.colliders = new MeshCollider[pieces.Length];
            instance.windowParts = new bool[pieces.Length];
            for (int i = 0; i < pieces.Length; i++)
            {
                var part = pieces[i];
                var mesh = part.sharedMesh;
                // The other transparent meshes are lamp lenses. Only the viewing port has this extent.
                bool window = IsViewport(part);
                var node = new GameObject(
                    window ? "Window projectile surface" : "Shell projectile surface"
                );
                node.transform.SetParent(part.transform, false);
                node.layer = layer;
                var col = node.AddComponent<MeshCollider>();
                col.sharedMesh = mesh;
                var surface = node.AddComponent<ShieldSurface>();
                surface.Protection = instance;
                surface.Window = window;
                surface.TypeOfMaterial = window ? MaterialType.GlassVisor : MaterialType.MetalThick;
                surface.Associate(surface.TypeOfMaterial);
                surface.PenetrationLevel = ShieldSettings.Armor(item, window) * 10f;
                surface.PenetrationChance = 1;
                instance.colliders[i] = col;
                instance.windowParts[i] = window;
                if (window)
                {
                    instance.windowBounds = mesh.bounds;
                    instance.cracks = WindowCracks.Create(part);
                }
            }
            ShieldImpactMarks.Bind(visual, item);
            instance.SetWearer(player);
            instance.SetWorn(true);
            return instance;
        }

        internal int RestoreProjectileLayers()
        {
            int layer = LayerMask.NameToLayer("TransparentCollider"),
                repaired = 0;
            if (layer < 0 || colliders == null)
                return 0;
            foreach (var col in colliders)
            {
                if (!col || col.gameObject.layer == layer)
                    continue;
                col.gameObject.layer = layer;
                repaired++;
            }
            return repaired;
        }

        internal void SetWorn(bool value)
        {
            bool changed =
                worn != value
                || lastBodyIntact != (state.Body > 0)
                || lastWindowIntact != (state.Window > 0);
            if (changed && ownerContacts)
                ownerContacts.ResetPairs();
            worn = value;
            lastBodyIntact = state.Body > 0;
            lastWindowIntact = state.Window > 0;
            for (int i = 0; i < colliders.Length; i++)
                colliders[i].enabled =
                    value
                    && ShieldSettings.Scale(shieldItem) > 0
                    && (windowParts[i] ? state.Window : state.Body) > 0;
            if (ownerContacts)
                ownerContacts.enabled = worn;
        }

        internal bool CanReceiveMelee(Player.BaseKnifeController attacker)
        {
            return worn
                && isActiveAndEnabled
                && holder
                && ShieldCarry.Current
                && ShieldCarry.Current.HeldBy(holder)
                && !ReferenceEquals(holder.HandsController, attacker);
        }

        internal void DrainArms(float fraction)
        {
            var stamina = holder.Physical.HandsStamina;
            stamina.Current = Mathf.Max(
                0,
                stamina.Current - (float)stamina.TotalCapacity * fraction
            );
            if (stamina.Current <= .001f)
                ShieldCarry.Current.NoteDepletion();
        }

        internal static float ImpactWear(
            float penetration,
            float armorDamagePortion,
            float resistance,
            float destructibility
        )
        {
            float factor =
                resistance <= 0 ? 1.1f : Mathf.Clamp(penetration / resistance, .6f, 1.1f);
            return Mathf.Max(1f, penetration * armorDamagePortion * factor * destructibility);
        }

        internal void Hit(ShieldSurface surface, DamageInfo info, ShotId shot)
        {
            if (!worn || !gameObject.activeInHierarchy)
                return;
            bool window = surface.Window;
            bool known = !shot.Equals(ShotId.EMPTY_SHOT_ID);
            if (known && !(window ? windowShots : bodyShots).Add(shot))
                return;
            surface.Sound(info.HitPoint);
            var material = window ? EArmorMaterial.Glass : EArmorMaterial.ArmoredSteel;
            float destructibility = Comfort
                .Common
                .Singleton<GlobalConfiguration>
                .Instance
                .ArmorMaterials[material]
                .Destructibility;
            float wear =
                ImpactWear(
                    info.PenetrationPower,
                    info.ArmorDamage,
                    surface.PenetrationLevel,
                    destructibility
                ) / Mathf.Max(.0001f, ShieldSettings.Scale(shieldItem));
            if (window)
            {
                bool intact = state.Window > 0;
                state.Window = Mathf.Max(0, state.Window - wear);
                if (intact && state.Window == 0)
                    ShieldImpactAudio.Play(MaterialType.GlassShattered, info.HitPoint);
                var p = surface.transform.InverseTransformPoint(info.HitPoint);
                state.Cracks.Add(
                    new Vector2(
                        Mathf.InverseLerp(windowBounds.min.x, windowBounds.max.x, p.x),
                        Mathf.InverseLerp(windowBounds.min.y, windowBounds.max.y, p.y)
                    )
                );
            }
            else
            {
                state.Body = Mathf.Max(0, state.Body - wear);
                MarkBodyImpact(info.HitPoint, info.HitNormal);
            }
            if (
                holder
                && ShieldCarry.Current
                && ShieldCarry.Current.HeldBy(holder)
                && (!known || staminaShots.Add(shot))
            )
            {
                var stamina = holder.Physical.HandsStamina;
                stamina.Current = Mathf.Max(
                    0,
                    stamina.Current - (float)stamina.TotalCapacity * .05f
                );
                if (stamina.Current <= .001f)
                    ShieldCarry.Current.NoteDepletion();
            }

            SetWorn(worn);
        }

        internal void MarkBodyImpact(Vector3 point, Vector3 normal)
        {
            if (state == null || !worn || !isActiveAndEnabled || normal.sqrMagnitude < .0001f)
                return;
            var marks = ShieldImpactMarks.For(shieldItem);
            if (marks == null)
                return;
            var local = transform.InverseTransformPoint(point);
            if (marks.Impacts.Exists(mark => (mark.Point - local).sqrMagnitude < .000004f))
                return;
            if (marks.Impacts.Count >= 48)
                marks.Impacts.RemoveAt(0);
            marks.Impacts.Add(
                new ShieldImpactMark(
                    ++marks.Revision,
                    local,
                    transform.InverseTransformDirection(normal).normalized
                )
            );
        }

        void LateUpdate()
        {
            if (state == null)
                return;
            float scale = ShieldSettings.Scale(shieldItem);
            float body = ShieldSettings.Armor(shieldItem, false);
            float window = ShieldSettings.Armor(shieldItem, true);
            if (lastBodyClass != body || lastWindowClass != window)
            {
                foreach (var collider in colliders)
                {
                    var surface = collider.GetComponent<ShieldSurface>();
                    surface.PenetrationLevel = (surface.Window ? window : body) * 10f;
                }
                lastBodyClass = body;
                lastWindowClass = window;
            }
            if (lastScale != scale)
            {
                lastScale = scale;
                SetWorn(worn);
            }
            if (lastBodyIntact != (state.Body > 0) || lastWindowIntact != (state.Window > 0))
                SetWorn(worn);
            if (cracks)
                cracks.Refresh(state.Cracks);
        }
    }
}
