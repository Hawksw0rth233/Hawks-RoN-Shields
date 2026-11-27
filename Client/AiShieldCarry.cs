using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace Hawks.RonShields
{
    internal sealed class AiShieldCarry : MonoBehaviour
    {
        Player owner;
        Item shield;
        GameObject visual;
        float nextCheck;
        ShieldPreview rendererPose;
        bool lowering;

        internal void RestoreVisual()
        {
            if (rendererPose)
                rendererPose.RestoreGun();
        }

        internal void RenderRaisedShield(RootMotion.FinalIK.LimbIK[] limbs)
        {
            if (!rendererPose || !visual)
                return;
            var held = owner.HandsController?.Item;
            var sidearm = InSlot(owner, EquipmentSlot.Holster);
            bool ready =
                owner.HandsController is Player.EmptyHandsController
                || (sidearm != null && held == sidearm);
            if (!ready)
            {
                visual.SetActive(false);
                return;
            }
            if (rendererPose.RenderAi(owner, limbs, lowering))
                visual.SetActive(true);
        }

        internal bool Switching { get; private set; }

        internal static AiShieldCarry For(Player player)
        {
            var carry = player.GetComponent<AiShieldCarry>()
                ?? player.gameObject.AddComponent<AiShieldCarry>();
            carry.owner = player;
            return carry;
        }

        static Item InSlot(Player player, EquipmentSlot slot) =>
            player.Inventory?.Equipment?.GetSlot(slot)?.ContainedItem;

        internal static Item FindShield(Player player)
        {
            var first = InSlot(player, EquipmentSlot.FirstPrimaryWeapon);
            if (Shields.Model(first))
                return first;
            var second = InSlot(player, EquipmentSlot.SecondPrimaryWeapon);
            return Shields.Model(second) ? second : null;
        }

        static Weapon Usable(Item item) =>
            item is Weapon gun
            && !Shields.Model(gun)
            && (gun.ChamberAmmoCount > 0 || gun.GetCurrentMagazineCount() > 0)
                ? gun
                : null;

        internal static Weapon PreferredWeapon(Player player) =>
            Usable(InSlot(player, EquipmentSlot.FirstPrimaryWeapon))
            ?? Usable(InSlot(player, EquipmentSlot.SecondPrimaryWeapon))
            ?? Usable(InSlot(player, EquipmentSlot.Holster));

        internal bool IsRaised(Item item) => visual && visual.activeSelf && shield == item;

        internal void UseEmptyHands(Callback<IHandsController> callback = null)
        {
            if (owner.HandsController is Player.EmptyHandsController empty)
            {
                callback?.Invoke(new Result<IHandsController>(empty));
                return;
            }
            Switching = true;
            owner.SetEmptyHands(result =>
            {
                Switching = false;
                Player.Call(result, callback);
            });
        }

        void LateUpdate()
        {
            if (!owner || owner.HealthController?.IsAlive != true)
            {
                Lower();
                return;
            }
            var selected = FindShield(owner);
            if (selected == null)
            {
                Lower();
                return;
            }
            var preferred = PreferredWeapon(owner);
            var held = owner.HandsController?.Item;
            if (!Switching && Time.time >= nextCheck)
            {
                nextCheck = Time.time + .35f;
                if (preferred == null && !(owner.HandsController is Player.EmptyHandsController))
                    UseEmptyHands();
                else if (preferred != null && held != preferred)
                {
                    Switching = true;
                    owner.TryProceed(preferred, _ => Switching = false, false);
                }
            }
            bool usingPrimary =
                preferred != null && preferred != InSlot(owner, EquipmentSlot.Holster);
            bool ready =
                preferred == null
                    ? owner.HandsController is Player.EmptyHandsController
                    : held == preferred;
            if (usingPrimary || !ready || owner.IsInPronePose)
            {
                if (rendererPose && visual && Shields.IsVant(shield) && !rendererPose.Holstered)
                {
                    lowering = true;
                    if (owner.HandsController?.Item != InSlot(owner, EquipmentSlot.Holster))
                        rendererPose.DetachSidearm();
                    rendererPose.TickLowering();
                }
                else
                    Lower();
                return;
            }
            lowering = false;
            if (!visual || shield != selected)
            {
                Lower();
                shield = selected;
                visual = Instantiate(Shields.Model(shield), transform, false);
                visual.SetActive(false);
                ShieldProtection.Attach(visual, shield, owner);
            }
            if (!rendererPose)
                rendererPose = gameObject.AddComponent<ShieldPreview>();
            var fc = owner.HandsController as Player.FirearmController;
            var weaponModel = fc?.ControllerGameObject;
            rendererPose.BindAi(shield, visual, weaponModel ? weaponModel.transform : null);
        }

        void OnDisable() => Lower();

        void Lower()
        {
            if (rendererPose)
                rendererPose.Release();
            lowering = false;
            if (!visual)
                return;
            visual.SetActive(false);
            Destroy(visual);
            visual = null;
        }
    }
}
