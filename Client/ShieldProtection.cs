using System;
using System.Collections.Generic;
using EFT;
using EFT.InventoryLogic;
using EFT.Ballistics;
using UnityEngine;

namespace Hawks.RonShields
{
    [Serializable]
    internal sealed class ShieldCondition
    {
        public float Body=500, Window=100;
        public List<Vector2> Cracks=new List<Vector2>();
        internal const string Field = "HawksShieldCondition";
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Item,ShieldCondition> states =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Item,ShieldCondition>();

        internal static ShieldCondition Load(Item item) => states.GetValue(item, _ => new ShieldCondition());

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            harmony.Patch(HarmonyLib.AccessTools.Method(typeof(ItemDeserializer),"GetItemProperties"),
                postfix:new HarmonyLib.HarmonyMethod(typeof(ShieldCondition),nameof(WriteItem)));
            harmony.Patch(HarmonyLib.AccessTools.Method(typeof(ItemDeserializer),"CreateItem"),
                prefix:new HarmonyLib.HarmonyMethod(typeof(ShieldCondition),nameof(ReadItem)),
                postfix:new HarmonyLib.HarmonyMethod(typeof(ShieldCondition),nameof(ApplyItem)));
        }

        static void WriteItem(Item __0,Dictionary<string,object> __result)
        {
            if(!ShieldItemPreview.IsShield(__0))return;
            var hp=Load(__0);
            __result[Field]=hp.ToToken();
        }

        internal Newtonsoft.Json.Linq.JObject ToToken() =>
            Newtonsoft.Json.Linq.JObject.Parse(JsonUtility.ToJson(this));

        static void ReadItem(Item __0,ref UnparsedData __1,out ShieldCondition __state)
        {
            __state=null;
            if(!ShieldItemPreview.IsShield(__0) || !(__1?.JToken is Newtonsoft.Json.Linq.JObject upd) || upd[Field]==null)return;
            __state=JsonUtility.FromJson<ShieldCondition>(upd[Field].ToString());
            var native=(Newtonsoft.Json.Linq.JObject)upd.DeepClone();
            native.Remove(Field);
            __1=new UnparsedData{JToken=native};
        }

        static void ApplyItem(Item __result,ShieldCondition __state)
        {
            if(__state==null)return;
            states.Remove(__result);
            states.Add(__result,__state);
            // A server-side repair may have been committed before the client closed.
            var total=__result.GetItemComponent<RepairableComponent>();
            if(total!=null && total.Durability>__state.Body+__state.Window)
                ShieldRepair.Restore(__state,total.Durability);
        }

    }

    public sealed class ShieldSurface : BallisticCollider
    {
        internal ShieldProtection Protection;
        internal bool Window;
        struct ContactSound { public int Frame; public Vector3 Point; }
        readonly List<ContactSound> played = new List<ContactSound>();
        internal void Sound(Vector3 point)
        {
            if (!ShieldImpactAudio.Play(TypeOfMaterial, point)) return;
            TrimSounds();
            played.Add(new ContactSound { Frame = Time.frameCount, Point = point });
        }
        void TrimSounds()
        {
            int oldest = Time.frameCount - 2;
            for (int i = played.Count - 1; i >= 0; i--)
                if (played[i].Frame < oldest) played.RemoveAt(i);
        }
        internal bool ConsumePlayedContact(Vector3 point)
        {
            TrimSounds();
            for (int i = 0; i < played.Count; i++)
            {
                if ((played[i].Point - point).sqrMagnitude > .0001f) continue;
                played.RemoveAt(i);
                return true;
            }
            return false;
        }
        public override PlayerHitInfo ApplyHit(DamageInfo info,ShotId id)
        {
            if(Protection && info.DamageType==EDamageType.Bullet)Protection.Hit(this,info,id);
            return base.ApplyHit(info,id);
        }
    }

    public sealed class ShieldProtection : MonoBehaviour
    {
        ShieldCondition state;
        Player holder;
        internal Player Wearer {get;private set;}
        ShieldOwnerContacts ownerContacts;
        internal void SetWearer(Player player)
        {
            if(Wearer==player)return;
            Wearer=player;
            if(!ownerContacts)ownerContacts=gameObject.AddComponent<ShieldOwnerContacts>();
            ownerContacts.Bind(player,colliders);
        }
        Item shieldItem;
        MeshCollider[] colliders;
        bool[] windowParts;
        Bounds windowBounds;
        WindowCracks cracks;
        readonly HashSet<ShotId> bodyShots=new HashSet<ShotId>(),windowShots=new HashSet<ShotId>();
        readonly HashSet<ShotId> staminaShots=new HashSet<ShotId>();
        bool worn=true;
        bool lastBodyIntact,lastWindowIntact;
        internal static bool IsViewport(MeshFilter part)
        {
            var bounds=part.sharedMesh.bounds;
            return part.name=="Window" && bounds.size.x>.25f && bounds.center.y>.15f && bounds.center.y<.35f;
        }
        internal static ShieldProtection Attach(GameObject visual,Item item,Player player)
        {
            var instance=visual.AddComponent<ShieldProtection>();
            instance.shieldItem=item;instance.holder=player;instance.state=ShieldCondition.Load(item);
            int layer=LayerMask.NameToLayer("TransparentCollider");
            if(layer<0)throw new InvalidOperationException("Missing SPT projectile collider layer");
            var ancestorBody=visual.GetComponentInParent<Rigidbody>();
            if(ancestorBody && !ancestorBody.isKinematic){
                var body=visual.GetComponent<Rigidbody>() ?? visual.AddComponent<Rigidbody>();
                body.isKinematic=true;
                body.useGravity=false;
            }
            var pieces=visual.GetComponentsInChildren<MeshFilter>(true);
            instance.colliders=new MeshCollider[pieces.Length];
            instance.windowParts=new bool[pieces.Length];
            for(int i=0;i<pieces.Length;i++){
                var part=pieces[i];var mesh=part.sharedMesh;
                // The other transparent meshes are lamp lenses. Only the viewing port has this extent.
                bool window=IsViewport(part);
                var node=new GameObject(window?"Window projectile surface":"Shell projectile surface");
                node.transform.SetParent(part.transform,false);node.layer=layer;
                var col=node.AddComponent<MeshCollider>();col.sharedMesh=mesh;
                var surface=node.AddComponent<ShieldSurface>();surface.Protection=instance;surface.Window=window;
                surface.TypeOfMaterial=window?MaterialType.GlassVisor:MaterialType.MetalThick;
                surface.Associate(surface.TypeOfMaterial);
                surface.PenetrationLevel=window?40f:55f;surface.PenetrationChance=1;
                instance.colliders[i]=col;instance.windowParts[i]=window;
                if(window){
                    instance.windowBounds=mesh.bounds;
                    instance.cracks=WindowCracks.Create(part);
                }
            }
            instance.SetWearer(player);
            instance.SetWorn(true);
            return instance;
        }
        internal void SetWorn(bool value)
        {
            bool changed=worn!=value || lastBodyIntact!=(state.Body>0) || lastWindowIntact!=(state.Window>0);
            if(changed && ownerContacts)ownerContacts.ResetPairs();
            worn=value;lastBodyIntact=state.Body>0;lastWindowIntact=state.Window>0;
            for(int i=0;i<colliders.Length;i++)colliders[i].enabled=value && (windowParts[i]?state.Window:state.Body)>0;
            if(ownerContacts)ownerContacts.enabled=worn;
        }
        internal bool CanReceiveMelee(Player.BaseKnifeController attacker)
        {
            return worn && isActiveAndEnabled && holder && ShieldCarry.Current &&
                ShieldCarry.Current.HeldBy(holder) && !ReferenceEquals(holder.HandsController,attacker);
        }
        internal void DrainArms(float fraction)
        {
            var stamina=holder.Physical.HandsStamina;
            stamina.Current=Mathf.Max(0,stamina.Current-(float)stamina.TotalCapacity*fraction);
            if(stamina.Current<=.001f)ShieldCarry.Current.NoteDepletion();
        }
        internal static float ImpactWear(float penetration,float armorDamagePortion,float resistance,float destructibility)
        {
            float factor=Mathf.Clamp(penetration/resistance,.6f,1.1f);
            return Mathf.Max(1f,penetration*armorDamagePortion*factor*destructibility);
        }
        internal void Hit(ShieldSurface surface,DamageInfo info,ShotId shot)
        {
            if(!worn || !gameObject.activeInHierarchy)return;
            bool window=surface.Window;
            bool known=!shot.Equals(ShotId.EMPTY_SHOT_ID);
            if(known && !(window?windowShots:bodyShots).Add(shot))return;
            surface.Sound(info.HitPoint);
            var material=window?EArmorMaterial.Glass:EArmorMaterial.ArmoredSteel;
            float destructibility=Comfort.Common.Singleton<GlobalConfiguration>.Instance.ArmorMaterials[material].Destructibility;
            float wear=ImpactWear(info.PenetrationPower,info.ArmorDamage,surface.PenetrationLevel,destructibility);
            if(window){
                bool intact = state.Window > 0;
                state.Window=Mathf.Max(0,state.Window-wear);
                if (intact && state.Window == 0) ShieldImpactAudio.Play(MaterialType.GlassShattered, info.HitPoint);
                var p=surface.transform.InverseTransformPoint(info.HitPoint);
                state.Cracks.Add(new Vector2(Mathf.InverseLerp(windowBounds.min.x,windowBounds.max.x,p.x),Mathf.InverseLerp(windowBounds.min.y,windowBounds.max.y,p.y)));
            }else state.Body=Mathf.Max(0,state.Body-wear);
            if(holder && ShieldCarry.Current && ShieldCarry.Current.HeldBy(holder) && (!known || staminaShots.Add(shot))){
                var stamina=holder.Physical.HandsStamina;
                stamina.Current=Mathf.Max(0,stamina.Current-(float)stamina.TotalCapacity*.05f);
                if(stamina.Current<=.001f)ShieldCarry.Current.NoteDepletion();
            }
            ShieldRepair.Sync(shieldItem,state);
            SetWorn(worn);
        }
        void LateUpdate()
        {
            if(state==null)return;
            if(lastBodyIntact!=(state.Body>0) || lastWindowIntact!=(state.Window>0))SetWorn(worn);
            if(cracks)cracks.Refresh(state.Cracks);
        }
    }
}


