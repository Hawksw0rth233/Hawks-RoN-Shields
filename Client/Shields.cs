using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.InventoryLogic.Operations;
using EFT.Ballistics;
using HarmonyLib;
using RootMotion.FinalIK;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Hawks.RonShields
{
 [BepInPlugin("com.hawks.ronshields.client","Ready or Not Shields","0.5.0")]
 public sealed class Shields : BaseUnityPlugin
 {
    internal static GameObject Patrol,Hrt;
    internal static readonly Vector3 ShieldOnlyCm = new Vector3(0f, 0f, -2f);
    internal static readonly Vector3 ArmsAndShieldCm = new Vector3(0f, -7.1f, 0f);
    internal static readonly Vector3 RightRestCm = new Vector3(0f, 0f, 0f);
    internal static readonly Vector3 RightAimCm = new Vector3(9f, 0f, -8f);
    internal static readonly Vector3 RightRestDegrees = new Vector3(0f, -25f, 1f);
    internal static readonly Vector3 RightAimDegrees = new Vector3(0f, 0f, 0f);
    internal static GameObject Model(Item i)
    {
        if (i==null) return null;
        if (i.TemplateId=="692340510000000000000001") return Patrol;
        if (i.TemplateId=="692340510000000000000002") return Hrt;
        return null;
    }
    AssetBundle viewportBank;
    Material cleanGlass;
    void LoadViewport()
    {
        viewportBank=AssetBundle.LoadFromFile(Path.Combine(Path.GetDirectoryName(Info.Location),"hawks.ron.viewport.bundle"));
        var shader=viewportBank ? viewportBank.LoadAsset<Shader>("ClearViewport") : null;
        if(!shader || !shader.isSupported)throw new InvalidDataException("Shield viewport shader unavailable");
        cleanGlass=new Material(shader){name="RoN clean sight glass"};
        foreach(var model in new[]{Patrol,Hrt})
            foreach(var part in model.GetComponentsInChildren<MeshFilter>(true)){
                if(!ShieldProtection.IsViewport(part))continue;
                var pane=part.GetComponent<MeshRenderer>();
                pane.sharedMaterial=cleanGlass;
                pane.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                pane.receiveShadows=false;
            }
    }
    void OnDestroy()
    {
        if(cleanGlass)Destroy(cleanGlass);
        if(viewportBank)viewportBank.Unload(false);
    }
    void Awake()
    {
        var bank=AssetBundle.LoadFromFile(Path.Combine(Path.GetDirectoryName(Info.Location),"hawks.ron.shields.bundle"));
        if (!bank) throw new InvalidDataException("Missing shield bundle");
        Patrol=bank.LoadAsset<GameObject>("Patrol"); Hrt=bank.LoadAsset<GameObject>("HRT");
        if (!Patrol||!Hrt) throw new InvalidDataException("Missing shield prefabs");
        LoadViewport();
        var h=new Harmony(Info.Metadata.GUID);
        foreach(var m in typeof(ObjectsFactory).GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance)
            .Where(x=>new[]{"CreateItem","CreateItemAsync","CreateCleanLootPrefab","CreateCleanLootPrefabAsync","CreateLootPrefab"}.Contains(x.Name)
                && x.GetParameters().Any(y=>y.ParameterType==typeof(Item)&&y.Name=="item")))
        {
            var hook=m.ReturnType==typeof(GameObject)?nameof(Created):m.ReturnType==typeof(Task<GameObject>)?nameof(CreatedAsync):null;
            if(hook!=null) h.Patch(m,postfix:new HarmonyMethod(typeof(Shields),hook));
        }
        ShieldItemPreview.Install(h);
        FireteamPortraits.Install(h, Logger);
        ShieldMelee.Install(h);
        ShieldImpactAudio.Install(h);
        ShieldImpactMarks.Install(h);
        foreach(var name in new[]{"InteractionRaycast","ExternalInteraction"})
            h.Patch(AccessTools.Method(typeof(Player),name),transpiler:new HarmonyMethod(typeof(Shields),nameof(WorldInteractionCast)));
        h.Patch(AccessTools.Method(typeof(GamePlayerOwner),"TranslateCommand"),
            prefix:new HarmonyMethod(typeof(Shields),nameof(WorldInteractionInput)));
        ShieldOwnerContacts.Install(h);
        var followerCombat = AccessTools.TypeByName("pitTeam.BigBrain.FollowerCombatCommon");
        if(followerCombat != null)
        {
            foreach(var name in new[]{"GetFirstPrimaryWeapon","GetSecondPrimaryWeapon"})
            {
                var method = AccessTools.Method(followerCombat,name,new[]{typeof(BotOwner)});
                if(method != null)
                    h.Patch(method,postfix:new HarmonyMethod(typeof(Shields),nameof(ExcludeShieldFromBotWeapons)));
            }
        }
        GroundShield.Install(h);
        ShieldCondition.Install(h);
        h.Patch(AccessTools.Method(typeof(MenuPlayerPoser),"OnRandomizeAnimation"),prefix:new HarmonyMethod(typeof(ShieldPreview),nameof(ShieldPreview.AllowRandomIdle)));
        h.Patch(AccessTools.Method(typeof(Player),"TryProceed",new[]{typeof(Item),typeof(Callback<IHandsController>),typeof(bool)}),
            prefix:new HarmonyMethod(typeof(Shields),nameof(Select)));
        h.Patch(AccessTools.Method(typeof(Player),"SetEmptyHands",new[]{typeof(Callback<IEmptyHandsController>)}),
            prefix:new HarmonyMethod(typeof(Shields),nameof(EmptyHands)));
        h.Patch(AccessTools.Method(typeof(Player),"Proceed",new[]{typeof(Weapon),typeof(Callback<IFirearmHandsController>),typeof(bool)}),prefix:new HarmonyMethod(typeof(Shields),nameof(LeaveEmptyShield)));
        h.Patch(AccessTools.Method(typeof(Player),"Proceed",new[]{typeof(Weapon),typeof(Callback<IFirearmHandsController>),typeof(bool)}),prefix:new HarmonyMethod(typeof(Shields),nameof(SelectAiWeapon)));
        h.Patch(AccessTools.Method(typeof(EFT.UI.PlayerModelLoader),"GetItemInHands"),postfix:new HarmonyMethod(typeof(ShieldPreview),nameof(ShieldPreview.ResolveHands)));
        h.Patch(AccessTools.Method(typeof(EFT.UI.PlayerModelLoader),"CreateWeapon"),prefix:new HarmonyMethod(typeof(ShieldPreview),nameof(ShieldPreview.Prepare)),postfix:new HarmonyMethod(typeof(ShieldPreview),nameof(ShieldPreview.Bind)));
        h.Patch(AccessTools.Method(typeof(MenuPlayerPoser),"LateUpdate"),prefix:new HarmonyMethod(typeof(ShieldPreview),nameof(ShieldPreview.Before)),postfix:new HarmonyMethod(typeof(ShieldPreview),nameof(ShieldPreview.After)));
        h.Patch(AccessTools.Method(typeof(Player),"ToggleProne"),prefix:new HarmonyMethod(typeof(Shields),nameof(AllowProne)));
        h.Patch(AccessTools.PropertyGetter(typeof(MovementContext),"CanProne"),postfix:new HarmonyMethod(typeof(Shields),nameof(CanProne)));
        h.Patch(AccessTools.Method(typeof(Physical),"Sprint"),postfix:new HarmonyMethod(typeof(Shields),nameof(SprintChanged)));
        h.Patch(AccessTools.Method(typeof(Stamina),"Process"),prefix:new HarmonyMethod(typeof(Shields),nameof(BeforeStamina)),postfix:new HarmonyMethod(typeof(Shields),nameof(AfterStamina)));
        foreach(var name in new[]{"ApplyComplexRotation","ApplySimpleRotation"})
            h.Patch(AccessTools.Method(typeof(EFT.Animations.ProceduralWeaponAnimation),name),
                prefix:new HarmonyMethod(typeof(Shields),nameof(BeforeWeaponMotion)),postfix:new HarmonyMethod(typeof(Shields),nameof(AfterWeaponMotion)));
        h.Patch(AccessTools.Method(typeof(FirearmsAnimator),"Reload",new[]{typeof(int),typeof(int),typeof(bool)}),
            prefix:new HarmonyMethod(typeof(Shields),nameof(SkipNativeShieldReloadAnimation)));
        h.Patch(AccessTools.PropertySetter(typeof(EFT.Animations.ProceduralWeaponAnimation),"TacticalReload"),
            prefix:new HarmonyMethod(typeof(Shields),nameof(SkipShieldTacticalReload)));
        h.Patch(AccessTools.Method(typeof(Player.FirearmController.ReloadExternalMagOperation),"Start",
            new[]{typeof(Player.FirearmController.ReloadExternalMagResult),typeof(Callback)}),
            postfix:new HarmonyMethod(typeof(Shields),nameof(ResetShieldBodyReload)));
        var gun=typeof(Player.FirearmController);
        foreach(var name in new[]{"CheckAmmo","CheckChamber","CheckFireMode","ExamineWeapon"})
            h.Patch(AccessTools.Method(gun,name),prefix:new HarmonyMethod(typeof(Shields),nameof(CanReload)));
        h.Patch(AccessTools.Method(typeof(Player.EmptyHandsController),"Drop"),prefix:new HarmonyMethod(typeof(Shields),nameof(DropEmpty)));
        h.Patch(AccessTools.Method(typeof(Player.EmptyHandsController),"ExamineWeapon"),prefix:new HarmonyMethod(typeof(Shields),nameof(EmptyInspect)));
        h.Patch(AccessTools.Method(gun,"Drop"),postfix:new HarmonyMethod(typeof(Shields),nameof(FinishDrop)));
        h.Patch(AccessTools.Method(gun,"SetTriggerPressed",new[]{typeof(bool)}),prefix:new HarmonyMethod(typeof(Shields),nameof(Trigger)));
        h.Patch(AccessTools.Method(gun,"Shot"),postfix:new HarmonyMethod(typeof(Shields),nameof(Fired)));
        h.Patch(AccessTools.Method(gun,"CanStartReload"),prefix:new HarmonyMethod(typeof(Shields),nameof(CanStartShieldReload)));
        h.Patch(AccessTools.Method(gun,"ReloadMag",new[]{typeof(Magazine),typeof(ItemAddress),typeof(Callback)}),
            prefix:new HarmonyMethod(typeof(Shields),nameof(ReloadShieldMagazine)));
        h.Patch(AccessTools.Method(gun,"QuickReloadMag",new[]{typeof(Magazine),typeof(Callback)}),
            prefix:new HarmonyMethod(typeof(Shields),nameof(QuickReloadShieldMagazine)));
        foreach(var method in new[]{"ReloadWithAmmo","ReloadBarrels","ReloadCylinderMagazine","ReloadGrenadeLauncher"})
            h.Patch(AccessTools.Method(gun,method),prefix:new HarmonyMethod(typeof(Shields),nameof(Reload)));
        h.Patch(AccessTools.Method(gun,"CanExecute"),prefix:new HarmonyMethod(typeof(Shields),nameof(CanMove)));
        h.Patch(AccessTools.Method(gun,"Execute"),prefix:new HarmonyMethod(typeof(Shields),nameof(Move)));
        var ui=typeof(EFT.UI.ItemUiContext);
        h.Patch(AccessTools.Method(ui,"ReloadWeapon"),prefix:new HarmonyMethod(typeof(Shields),nameof(MenuReload)));
        h.Patch(AccessTools.Method(ui,"UnloadAmmo",new[]{typeof(ItemContext)}),
            prefix:new HarmonyMethod(typeof(Shields),nameof(BlockShieldMagazineUnloadMenu)));
        h.Patch(AccessTools.Method(typeof(InventoryController),"UnloadMagazine",new[]{typeof(Magazine),typeof(bool)}),
            prefix:new HarmonyMethod(typeof(Shields),nameof(BlockShieldMagazineUnload)));
        foreach(var method in new[]{"LoadWeapon","UnloadWeapon"})
            h.Patch(AccessTools.Method(ui,method),prefix:new HarmonyMethod(typeof(Shields),nameof(MenuLoad)));
        h.Patch(AccessTools.Method(gun,"SetAim",new[]{typeof(bool)}),prefix:new HarmonyMethod(typeof(Shields),nameof(Aim)));
        h.Patch(AccessTools.Method(gun,"ToggleAim"),prefix:new HarmonyMethod(typeof(Shields),nameof(ToggleAim)));
        h.Patch(AccessTools.Method(typeof(Player),"VisualPass"),prefix:new HarmonyMethod(typeof(Shields),nameof(BeforeVisuals)),postfix:new HarmonyMethod(typeof(Shields),nameof(ThirdPerson)));
        h.Patch(AccessTools.Method(typeof(Player),"IkApply"),prefix:new HarmonyMethod(typeof(Shields),nameof(Arm)),postfix:new HarmonyMethod(typeof(Shields),nameof(RestoreArm)));
    }
    struct WeaponMotionSample
    {
        internal ShieldCarry Carry;
        internal Transform Root;
        internal Vector3 Position;
        internal Quaternion Rotation;
    }
    static void BeforeWeaponMotion(EFT.Animations.ProceduralWeaponAnimation __instance,out WeaponMotionSample __state)
    {
        __state=default;
        var c=ShieldCarry.Current;
        if(!c || !c.Active || !c.HasSidearm || c.Owner.ProceduralWeaponAnimation!=__instance)return;
        var root=__instance.HandsContainer?.WeaponRootAnim;
        if(root)__state=new WeaponMotionSample{Carry=c,Root=root,Position=root.position,Rotation=root.rotation};
    }
    static void AfterWeaponMotion(WeaponMotionSample __state)
    {
        if(__state.Carry && __state.Root)
            __state.Carry.CaptureWeaponMotion(__state.Position,__state.Rotation,__state.Root);
    }
    static bool WorldActionsAllowed(bool native,Player player)
        =>native && (!ShieldCarry.Current || !ShieldCarry.Current.HeldBy(player));
    static System.Collections.Generic.IEnumerable<CodeInstruction> WorldInteractionCast(System.Collections.Generic.IEnumerable<CodeInstruction> source)
    {
        var check=AccessTools.Method(typeof(Player.AbstractHandsController),"CanInteract");
        int replacements=0;
        foreach(var instruction in source){
            yield return instruction;
            if(!instruction.Calls(check))continue;
            replacements++;
            yield return new CodeInstruction(System.Reflection.Emit.OpCodes.Ldarg_0);
            yield return new CodeInstruction(System.Reflection.Emit.OpCodes.Call,AccessTools.Method(typeof(Shields),nameof(WorldActionsAllowed)));
        }
        if(replacements!=1)throw new InvalidOperationException("Unexpected EFT world interaction gate count: "+replacements);
    }
    static bool WorldInteractionInput(GamePlayerOwner __instance,EFT.InputSystem.ECommand __0,ref EFT.InputSystem.InputNode.ETranslateResult __result)
    {
        if(__0!=EFT.InputSystem.ECommand.BeginInteracting && __0!=EFT.InputSystem.ECommand.BeginSpecialInteracting)return true;
        if(WorldActionsAllowed(true,__instance.Player))return true;
        __result=EFT.InputSystem.InputNode.ETranslateResult.BlockAll;
        return false;
    }
    static bool AllowProne(Player __instance)=>!ShieldCarry.Current || !ShieldCarry.Current.HeldBy(__instance) || __instance.IsInPronePose;
    static void CanProne(MovementContext __instance,ref bool __result)
    {
        var held=ShieldCarry.Current;
        if(held && held.HasShield && held.Owner.MovementContext==__instance && !held.Owner.IsInPronePose)__result=false;
    }
    static void SprintChanged(Physical __instance)
    {
        var carry=ShieldCarry.Current;
        if(carry && ReferenceEquals(carry.Owner.Physical,__instance) && __instance.Sprinting)carry.CancelForSprint();
    }
    static void BeforeStamina(Stamina __instance,out float __state)
    {
        var held=ShieldCarry.Current;
        __state=held && held.Active && ReferenceEquals(held.Owner.Physical.HandsStamina,__instance)?__instance.Current:float.NaN;
    }
    static void AfterStamina(Stamina __instance,float __0,float __state)
    {
        if(float.IsNaN(__state) || __0<=0)return;
        if(__instance.Current<=.001f || __state<=.001f)ShieldCarry.Current?.NoteDepletion();
        float capacity=(float)__instance.TotalCapacity;
        if(ShieldCarry.Current && ShieldCarry.Current.InAim){
            var gun=(Player.FirearmController)ShieldCarry.Current.Owner.HandsController;
            float aimed=Mathf.Max(0,__state-capacity*ShieldCarry.AimCost(gun.TotalErgonomics)*__0);
            __instance.Current=Mathf.Min(__instance.Current,aimed);
            if(__instance.Current<=.001f)ShieldCarry.Current.NoteDepletion();
            return;
        }
        float gain=Mathf.Max((__instance.Current-__state)*.5f,capacity*.005f*__0);
        __instance.Current=Mathf.Clamp(__state+gain,0,capacity);
    }
    static void Created(Item item,GameObject __result) => Configure(item,__result);
    static void CreatedAsync(Item item,ref Task<GameObject> __result)
    {
        // Pooled ordinary weapons can acquire a visual from a previous shield.
        __result=Finish(__result,item);
    }
    static async Task<GameObject> Finish(Task<GameObject> result,Item item)
    {
        var go=await result;Configure(item,go);return go;
    }
    static void Configure(Item item, GameObject go)
    {
        if (!go) return;
        var model=Model(item);var display=go.GetComponent<ShieldDisplay>();
        if (model) (display?display:go.AddComponent<ShieldDisplay>()).Bind(model,item);
        else if(display) display.Clear();
    }
    static bool SkipNativeShieldReloadAnimation(FirearmsAnimator __instance)
    {
        var carry=ShieldCarry.Current;
        return !carry || !carry.OwnsReloadAnimator(__instance);
    }

    static bool SkipShieldTacticalReload(EFT.Animations.ProceduralWeaponAnimation __instance)
    {
        var carry=ShieldCarry.Current;
        return !carry || !carry.NativeShieldExchange ||
               !ReferenceEquals(carry.Owner.ProceduralWeaponAnimation,__instance);
    }

    static void ResetShieldBodyReload(Player.FirearmController.ReloadExternalMagOperation __instance)
    {
        var carry=ShieldCarry.Current;
        if(carry && carry.NativeShieldExchange && ShieldCarry.Controls(__instance.Controller))
            carry.Owner.BodyAnimatorCommon.SetFloat(PlayerAnimator.RELOAD_FLOAT_PARAM_HASH,0f);
    }

    static bool Trigger(Player.FirearmController __instance,bool pressed)
        => !ShieldCarry.Controls(__instance) || ShieldCarry.Current.Trigger(__instance,pressed);
    static void Fired(Player.FirearmController __instance,Weapon weapon)
    {
        if(ShieldCarry.Controls(__instance))ShieldCarry.Current.RecordShot(weapon);
    }
    static bool EmptyInspect(Player.EmptyHandsController __instance)
        => !ShieldCarry.Current || !ShieldCarry.Current.OwnsHands(__instance);
    static void FinishDrop(Player.FirearmController __instance)
    {
        if(ShieldCarry.FullyLowered==__instance){
            ShieldCarry.FullyLowered=null;
            if(__instance.IsInRemoveOperation())__instance.FastForwardCurrentState();
        }
    }
    static bool CanReload(Player.FirearmController __instance,ref bool __result)
    {
        if(!ShieldCarry.Controls(__instance))return true;
        __result=false;return false;
    }
    static bool CanStartShieldReload(Player.FirearmController __instance,ref bool __result)
    {
        if(!ShieldCarry.Controls(__instance) || ShieldCarry.Current.CanReloadMagazine)return true;
        __result=false;
        return false;
    }

    static bool ReloadShieldMagazine(Player.FirearmController __instance, Magazine __0, ItemAddress __1, Callback __2) =>
        ScheduleShieldReload(__instance, __0, __2, done => __instance.ReloadMag(__0,__1,done));

    static bool QuickReloadShieldMagazine(Player.FirearmController __instance, Magazine __0, Callback __1) =>
        ScheduleShieldReload(__instance, __0, __1, done => __instance.QuickReloadMag(__0,done));

    static bool ScheduleShieldReload(Player.FirearmController gun,Magazine magazine,Callback callback,Action<Callback> replay)
    {
        if(!ShieldCarry.Controls(gun) || ShieldCarry.Current.ReplayingMagazineReload)return true;
        if(magazine!=null && ShieldCarry.Current.TryMagazineReload(replay,callback))return false;
        callback?.Fail("Shield reload is only available for detachable-magazine pistols.");
        return false;
    }

    static bool Reload(Player.FirearmController __instance,Callback callback)
    {
        if(!ShieldCarry.Controls(__instance))return true;
        callback?.Invoke(new Result<IHandsController>(null,"Stow shield and weapon before reloading"));
        return false;
    }
    static bool ChangesFeed(Item item,ItemAddress from,ItemAddress to)
    {
        if(!(item is Magazine) && !(item is Ammo))return false;
        return InsideSidearm(from) || InsideSidearm(to) || InsideSidearm(item.Parent);
    }
    static bool InsideSidearm(ItemAddress address)
    {
        var visited=new HashSet<Item>();
        for(var parent=address?.Container?.ParentItem;parent!=null && visited.Add(parent);parent=parent.Parent?.Container?.ParentItem)
            if(parent is Weapon w && ShieldCarry.Using(w))return true;
        return false;
    }
    static bool BlockMove(Player.FirearmController controller,IInventoryOperation op)
    {
        if(!ShieldCarry.Controls(controller))return false;
        if(op is LoadMagOperation load)op=load.InternalOperation;
        else if(op is UnloadMagOperation unload)op=unload.InternalOperation;
        return (op is IOneItemOperation one && ChangesFeed(one.Item1,one.From1,one.To1))
            || (op is ITwoItemOperation two && ChangesFeed(two.Item2,two.From2,two.To2));
    }
    static bool CanMove(Player.FirearmController __instance,IInventoryOperation __0,ref bool __result)
    {
        if(!BlockMove(__instance,__0))return true;
        __result=false;return false;
    }
    static bool Move(Player.FirearmController __instance,IInventoryOperation __0,Callback __1)
    {
        if(!BlockMove(__instance,__0))return true;
        __1?.Invoke(new Result<IHandsController>(null,"Stow shield and weapon before reloading"));return false;
    }
    const string ShieldAmmoUnloadMessage="Stow the shield or remove the magazine before unloading its ammunition.";

    static bool BlockShieldMagazineUnloadMenu(ItemContext __0,ref Task __result)
    {
        var item=__0?.Item;
        var magazine=item as Magazine ?? (item as Weapon)?.GetCurrentMagazine();
        if(!ShieldCarry.UsingMagazine(magazine))return true;
        EFT.Communications.NotificationManager.DisplayWarningNotification(ShieldAmmoUnloadMessage);
        __result=Task.CompletedTask;
        return false;
    }

    static bool BlockShieldMagazineUnload(Magazine __0,ref Task<IResult> __result)
    {
        if(!ShieldCarry.UsingMagazine(__0))return true;
        __result=Task.FromResult<IResult>(new FailedResult(ShieldAmmoUnloadMessage,0));
        return false;
    }

    static bool MenuReload(Weapon __0)=>!ShieldCarry.Using(__0);
    static bool MenuLoad(Weapon __0,ref Task __result)
    {
        if(!ShieldCarry.Using(__0))return true;
        __result=Task.CompletedTask;return false;
    }
    static bool Aim(Player.FirearmController __instance,bool value)
    {
        if(!ShieldCarry.Controls(__instance))return true;
        ShieldCarry.Current.SetShieldAim(value);
        return false;
    }
    static bool ToggleAim(Player.FirearmController __instance)
    {
        if(!ShieldCarry.Controls(__instance))return true;
        ShieldCarry.Current.SetShieldAim(!ShieldCarry.Current.Aiming);
        return false;
    }
    static bool DropEmpty(Player.EmptyHandsController __instance,float __0,Action __1,bool __2,Item __3)
    {
        var carry=ShieldCarry.Current;
        if(!carry || carry.HasSidearm || !carry.OwnsHands(__instance))return true;
        carry.BeginStow(()=>__instance.Drop(__0,__1,__2,__3),()=>{});
        return false;
    }
    static bool LeaveEmptyShield(Player __instance,Weapon __0,Callback<IFirearmHandsController> __1,bool __2)
    {
        var carry=ShieldCarry.Current;
        if(!carry || !carry.HeldBy(__instance) || carry.HasSidearm)return true;
        carry.BeginStow(()=>__instance.Proceed(__0,__1,__2),()=>__1?.Invoke(new Result<IFirearmHandsController>(null,"Shield switch interrupted")));
        return false;
    }
    static bool EmptyHands(Player __instance,Callback<IEmptyHandsController> callback)
    {
        var carry=ShieldCarry.Current;
        if(!__instance.IsYourPlayer || !carry || !carry.HasShield)return true;
        carry.BeginStow(()=>__instance.SetEmptyHands(callback),
            ()=>callback?.Invoke(new Result<IEmptyHandsController>(null,"Shield switch interrupted")));
        return false;
    }
    static void ExcludeShieldFromBotWeapons(BotOwner __0,ref Weapon __result)
    {
        var player=__0?.GetPlayer;
        if(!player || AiShieldCarry.FindShield(player)==null)return;
        var carry=AiShieldCarry.For(player);
        if(Model(__result)) __result=AiShieldCarry.PreferredWeapon(player);
    }

    static bool SelectAiWeapon(Player __instance,ref Weapon __0,Callback<IFirearmHandsController> __1)
    {
        if(!__instance.IsAI || AiShieldCarry.FindShield(__instance)==null)return true;
        var carry=AiShieldCarry.For(__instance);
        if(carry.Switching)return true;
        var preferred=AiShieldCarry.PreferredWeapon(__instance);
        if(preferred==null)
        {
            carry.UseEmptyHands();
            __1?.Invoke(new Result<IFirearmHandsController>(null,"Shield-only defensive stance"));
            return false;
        }
        __0=preferred;
        if(__instance.HandsController is Player.FirearmController gun && gun.Item==preferred)
        {
            __1?.Invoke(new Result<IFirearmHandsController>(gun));
            return false;
        }
        return true;
    }

    static bool Select(Player __instance,ref Item item,Callback<IHandsController> completeCallback,bool scheduled)
    {
        if(!__instance.IsYourPlayer)
        {
            if(!__instance.IsAI || AiShieldCarry.FindShield(__instance)==null || !(item is Weapon))return true;
            var ai=AiShieldCarry.For(__instance);
            if(ai.Switching)return true;
            var preferred=AiShieldCarry.PreferredWeapon(__instance);
            if(preferred==null)
            {
                ai.UseEmptyHands(completeCallback);
                return false;
            }
            if(__instance.HandsController is Player.FirearmController gun && gun.Item==preferred)
            {
                completeCallback?.Invoke(new Result<IHandsController>(gun));
                return false;
            }
            item=preferred;
            return true;
        }
        var carry=__instance.GetComponent<ShieldCarry>();
        if(carry && carry.HasShield){
            if(carry.Selected==item){
                completeCallback?.Invoke(new Result<IHandsController>(__instance.HandsController));
                return false;
            }
            var next=item;
            bool keepGun=next!=null && __instance.HandsController?.Item==next;
            carry.BeginStow(()=>{
                if(keepGun && __instance.HandsController?.Item==next){
                    __instance.SetEmptyHands(result=>{
                        if(result.Failed)Player.Call(result,completeCallback);
                        else __instance.TryProceed(next,completeCallback,scheduled);
                    });
                }else __instance.TryProceed(next,completeCallback,scheduled);
            },()=>completeCallback?.Invoke(new Result<IHandsController>(null,"Shield switch interrupted")));
            return false;
        }
        if(!Model(item))return true;
        if(__instance.IsInPronePose){completeCallback?.Invoke(new Result<IHandsController>(null,"Stand up before raising the shield"));return false;}
        var equipment=__instance.Inventory.Equipment;
        if(!IsInPrimarySlot(__instance,item))
        { completeCallback?.Invoke(new Result<IHandsController>(null,"Equip the shield in a primary slot")); return false; }
        var pistol=equipment.GetSlot(EquipmentSlot.Holster).ContainedItem as Weapon;
        carry=carry?carry:__instance.gameObject.AddComponent<ShieldCarry>();
        if(__instance.HandsController is Player.FirearmController currentGun && currentGun.IsAiming)currentGun.SetAim(false);
        if(pistol!=null && __instance.HandsController?.Item==pistol){
            var selected=item;
            __instance.SetEmptyHands(result=>{
                if(result.Failed)Player.Call(result,completeCallback);
                else __instance.TryProceed(selected,completeCallback,scheduled);
            });
            return false;
        }
        if(pistol==null){
            DrawWithoutSidearm(__instance,carry,item,completeCallback);
            return false;
        }
        carry.Select(__instance,item,pistol);
        item=pistol; return true;
    }
    static void DrawWithoutSidearm(Player player,ShieldCarry carry,Item selected,Callback<IHandsController> complete)
    {
        if(player.HandsController is Player.EmptyHandsController empty){
            carry.Select(player,selected,null);
            complete?.Invoke(new Result<IHandsController>(empty));
            return;
        }
        // EFT may drop the outgoing controller on a later frame.
        // Publish the shield only after the empty controller has taken over.
        player.SetEmptyHands(result=>{
            if(result.Failed){Player.Call(result,complete);return;}
            if(!carry || !player || player.HandsController is not Player.EmptyHandsController ||
                !ReferenceEquals(player.HandsController,result.Value) || carry.HasShield ||
                !IsInPrimarySlot(player,selected)){
                complete?.Invoke(new Result<IHandsController>(null,"Shield switch interrupted"));
                return;
            }
            carry.Select(player,selected,null);
            Player.Call(result,complete);
        });
    }
    static bool IsInPrimarySlot(Player player,Item item)
    {
        var equipment=player.Inventory?.Equipment;
        return equipment!=null && (equipment.GetSlot(EquipmentSlot.FirstPrimaryWeapon).ContainedItem==item ||
            equipment.GetSlot(EquipmentSlot.SecondPrimaryWeapon).ContainedItem==item);
    }
    static void BeforeVisuals(Player __instance)
    {
        if(__instance.IsYourPlayer && ShieldCarry.Current)ShieldCarry.Current.RestoreArmOffsets();
    }
    static void ThirdPerson(Player __instance,LimbIK[] ____limbs,Transform[] ____markers)
    {
        var carry=ShieldCarry.Current;
        if(!__instance.IsYourPlayer || __instance.FirstPersonPointOfView || !carry || !carry.Active ||
            ____limbs==null || ____limbs.Length<2 || !____limbs[0] || !____limbs[1])return;
        var state=new Arms{Left=Save(____limbs[0]),Right=Save(____limbs[1])};
        try{
            carry.Solve(____limbs[0],____limbs[1],____markers!=null && ____markers.Length>1?____markers[1]:null);
            ____limbs[0].solver.Update();____limbs[1].solver.Update();
            carry.PoseLeftHand(____limbs[0]);
        }finally{Restore(state.Left);Restore(state.Right);}
    }
    struct ArmState
    {
        internal LimbIK Limb;
        internal Transform Target;
        internal Vector3 Position;
        internal float Weight,RotationWeight,BendWeight;
        internal Vector3 BendNormal;
        internal IKSolverLimb.BendModifier BendMode;
        internal Quaternion Rotation;
    }
    struct Arms { internal ArmState Left,Right; }
    static ArmState Save(LimbIK limb)
    {
        var state=new ArmState{Limb=limb,Target=limb.solver.target,Position=limb.solver.IKPosition,Weight=limb.solver.IKPositionWeight,RotationWeight=limb.solver.IKRotationWeight,Rotation=limb.solver.IKRotation,BendWeight=limb.solver.bendModifierWeight,BendNormal=limb.solver.bendNormal,BendMode=limb.solver.bendModifier};
        limb.solver.target=null;
        return state;
    }
    static void Arm(Player __instance,LimbIK[] ____limbs,Transform[] ____markers,out Arms __state)
    {
        __state=default;
        if(!__instance.IsYourPlayer || !__instance.FirstPersonPointOfView || ____limbs==null || ____limbs.Length<2 || !____limbs[0] || !____limbs[1])return;
        var carry=ShieldCarry.Current;
        if(!carry || !carry.Active)return;
        __state.Left=Save(____limbs[0]); __state.Right=Save(____limbs[1]);
        carry.Solve(____limbs[0],____limbs[1],____markers!=null && ____markers.Length>1 ? ____markers[1]:null);
    }
    static void Restore(ArmState state)
    {
        if(!state.Limb)return;
        var solver=state.Limb.solver;
        solver.target=state.Target;solver.IKPosition=state.Position;solver.IKPositionWeight=state.Weight;solver.IKRotation=state.Rotation;solver.IKRotationWeight=state.RotationWeight;
        solver.bendModifier=state.BendMode;solver.bendModifierWeight=state.BendWeight;solver.bendNormal=state.BendNormal;
    }
    static void RestoreArm(Arms __state){
        Restore(__state.Left);Restore(__state.Right);
        if(__state.Left.Limb && ShieldCarry.Current)ShieldCarry.Current.PoseLeftHand(__state.Left.Limb);
    }

 }
 public sealed class ShieldDisplay : MonoBehaviour
 {
    internal GameObject Visual;
    GameObject source;
    Renderer[] original;
    bool[] enabledBefore;
    Item equippedItem;
    PlayerBody body;
    bool onBack;
    ShieldProtection protection;
    Transform lastHost;
    void OnTransformParentChanged(){body=GetComponentInParent<PlayerBody>();}
    void LateUpdate()
    {
        if(!Visual || equippedItem==null)return;
        if(lastHost!=transform.root){lastHost=transform.root;body=GetComponentInParent<PlayerBody>();}
        bool worn=body && body.Equipment!=null &&
            (body.Equipment.GetSlot(EquipmentSlot.FirstPrimaryWeapon).ContainedItem==equippedItem ||
             body.Equipment.GetSlot(EquipmentSlot.SecondPrimaryWeapon).ContainedItem==equippedItem)
            && body.GetSlotViewByItem(equippedItem)?.Model==gameObject;
        if(worn && body.PlayerBones){
            var aiWearer=body.GetComponentInParent<Player>();
            if(aiWearer && aiWearer.GetComponent<AiShieldCarry>() is AiShieldCarry ai && ai.IsRaised(equippedItem))
            {
                if(protection)protection.SetWorn(false);
                Visual.SetActive(false);
                return;
            }
            if(!Visual.activeSelf && aiWearer && aiWearer.IsAI)Visual.SetActive(true);
            if(PlaceOnBack(body.PlayerBones,body.Equipment,ShieldCarry.Current?.Selected)){
                var wearer=body.GetComponentInParent<Player>();
                if(wearer){
                    if(!protection)protection=ShieldProtection.Attach(Visual,equippedItem,null);
                    protection.SetWearer(wearer);
                    protection.SetWorn(true);
                }
                onBack=true;
            }else if(protection)protection.SetWorn(false);
        }else if(onBack){if(protection)protection.SetWorn(false);Visual.transform.localPosition=Vector3.zero;Visual.transform.localRotation=Quaternion.identity;onBack=false;}
    }
    internal bool PlaceOnBack(PlayerBones bones,InventoryEquipment equipment,Item held)
    {
        if(!Visual || !ShieldPreview.BodyFrame(bones,out var origin,out var facing,out var scale))return false;
        var first=equipment.GetSlot(EquipmentSlot.FirstPrimaryWeapon)?.ContainedItem;
        var second=equipment.GetSlot(EquipmentSlot.SecondPrimaryWeapon)?.ContainedItem;
        var offset=new Vector3(0,-.25f,-.21f);
        if(Shields.Model(first) && Shields.Model(second) && held?.Id!=first.Id && held?.Id!=second.Id){
            bool outer=equippedItem.Id==second.Id;
            offset.x=outer?.035f:-.035f;
            if(outer){
                var innerBounds=DepthBounds(Shields.Model(first));
                var outerBounds=DepthBounds(Shields.Model(second));
                offset.z-=innerBounds.max.z-outerBounds.min.z+.012f;
            }
        }
        Visual.transform.SetPositionAndRotation(origin+facing*(offset*scale),facing*Quaternion.Euler(180,0,0));
        return true;
    }
    static readonly System.Collections.Generic.Dictionary<GameObject,Bounds> boundsByModel=new System.Collections.Generic.Dictionary<GameObject,Bounds>();
    internal static Bounds DepthBounds(GameObject prefab)
    {
        if(boundsByModel.TryGetValue(prefab,out var result))return result;
        bool first=true;
        foreach(var part in prefab.GetComponentsInChildren<MeshFilter>(true)){
            var b=part.sharedMesh.bounds;
            var local=prefab.transform.worldToLocalMatrix*part.transform.localToWorldMatrix;
            for(int i=0;i<8;i++){
                var point=local.MultiplyPoint3x4(new Vector3((i&1)==0?b.min.x:b.max.x,(i&2)==0?b.min.y:b.max.y,(i&4)==0?b.min.z:b.max.z));
                if(first){result=new Bounds(point,Vector3.zero);first=false;}else result.Encapsulate(point);
            }
        }
        boundsByModel[prefab]=result;return result;
    }
    internal void Bind(GameObject model,Item item)
    {
        if(source==model && Visual && equippedItem==item)return;
        Clear();equippedItem=item;source=model;original=GetComponentsInChildren<Renderer>(true);
        enabledBefore=original.Select(x=>x.enabled).ToArray();foreach(var r in original)r.enabled=false;
        Visual=Instantiate(model,transform,false);
        ShieldImpactMarks.Bind(Visual,item);
        foreach(var t in Visual.GetComponentsInChildren<Transform>())t.gameObject.layer=gameObject.layer;
    }
    internal void Clear()
    {
        if(Visual){Visual.SetActive(false);Destroy(Visual);}
        if(original!=null)for(int i=0;i<original.Length;i++)if(original[i])original[i].enabled=enabledBefore[i];
        protection=null;equippedItem=null;body=null;lastHost=null;onBack=false;source=null; Visual=null; original=null;
    }
 }
 public sealed class ShieldCarry : MonoBehaviour
 {
    Player owner;Item shield;Weapon pistol;GameObject prop;ShieldDisplay slung;
    internal static ShieldCarry Current;
    internal Player Owner=>owner;
    internal bool HeldBy(Player player)=>HasShield && owner==player;
    internal bool HasShield=>prop;
    internal Item Selected=>shield;
    internal bool Aiming {get;private set;}
    internal void SetShieldAim(bool value)=>Aiming=value && HasSidearm && !stowing && !MagazineReloadActive && !owner.Physical.Sprinting;
    internal bool InAim=>Aiming && !owner.Physical.Sprinting && owner.HandsController is Player.FirearmController;
    internal static float AimCost(float ergonomics)=>Mathf.Lerp(.03f,.01f,Mathf.Clamp01(ergonomics*.01f));
    internal void CancelForSprint()
    {
        Aiming=held=pending=false;
        if(delivered && owner.HandsController is Player.FirearmController gun)gun.SetTriggerPressed(false);
    }
    const float MagazineLowerTime=.36f, MagazineRaiseTime=.40f;
    const float MagazineSettledTime=.12f, MagazineIdleTimeout=1f;
    enum MagazineReloadPhase { None, Retract, Lower, StartExchange, Exchange, Settle, Raise }
    MagazineReloadPhase magazineReload;
    float magazineTime, magazinePose;
    Action startMagazineExchange;
    int magazineExchangeToken;
    bool nativeMagazineCompleted;
    Callback cancelMagazineExchange;
    Player.FirearmController.ReloadExternalMagOperation nativeMagazineExchange;
    int nativeSoundStep;
    float nativeSoundEnd;
    bool magazineWasPresent, magazineWasLocked, magazineChamberEmpty;
    internal bool NativeShieldExchange => magazineReload==MagazineReloadPhase.Exchange;
    internal bool OwnsReloadAnimator(FirearmsAnimator animator) =>
        NativeShieldExchange && owner.HandsController is Player.FirearmController gun &&
        ReferenceEquals(gun.FirearmsAnimator,animator);

    void PlayMagazineSound(Player.FirearmController gun,params string[] keys)
    {
        var sound=gun.WeaponSoundPlayer;
        if(!sound)return;
        foreach(var key in keys)
        {
            var entry=sound.AdditionalSounds.FirstOrDefault(clip=>clip.EventName==key && clip.SoundClips?.Length>0);
            if(entry==null)continue;
            sound.SoundEventHandler(key);
            foreach(var clip in entry.SoundClips)
                if(clip)nativeSoundEnd=Mathf.Max(nativeSoundEnd,magazineTime+clip.length);
            break;
        }
    }

    void RunNativeMagazineEvents(Player.FirearmController gun,Player.FirearmController.ReloadExternalMagOperation operation)
    {
        switch(nativeSoundStep)
        {
            case 0:
                if(magazineWasPresent)PlayMagazineSound(gun,"SndMagButton","SndMagSbrosnik");
                nativeSoundStep++;
                break;
            case 1:
                if(magazineTime<.13f)return;
                if(magazineWasPresent)PlayMagazineSound(gun,"SndMagOut","SndMagOutAll","SndMagPullOut");
                nativeSoundStep++;
                break;
            case 2:
                if(magazineTime<.40f)return;
                operation.RemoveAmmoFromChamber();
                operation.OnMagPulledOutFromWeapon();
                operation.OnMagPuttedToRig();
                operation.OnMagAppeared();
                PlayMagazineSound(gun,"SndMagIn","SndMagPullIn");
                operation.OnMagInsertedToWeapon();
                nativeSoundStep++;
                break;
            case 3:
                if(magazineTime<.63f)return;
                if(magazineChamberEmpty && !magazineWasLocked)PlayMagazineSound(gun,"SndSlideOut","SndSlideGrab");
                nativeSoundStep++;
                break;
            case 4:
                if(magazineTime<.76f)return;
                if(magazineChamberEmpty)
                {
                    if(magazineWasLocked)PlayMagazineSound(gun,"SndCatchButton","SndCatchSlider","SndSlideIn");
                    else PlayMagazineSound(gun,"SndSlideIn","SndCatchSlider");
                }
                operation.OnAddAmmoInChamber();
                if(ReferenceEquals(gun.CurrentOperation,operation))
                {
                    operation.OnOnOffBoltCatchEvent(false);
                    operation.OnIdleStartEvent();
                }
                nativeSoundStep++;
                break;
        }
    }

    internal bool ReplayingMagazineReload { get; private set; }
    bool MagazineReloadActive => magazineReload!=MagazineReloadPhase.None;
    internal bool CanReloadMagazine =>
        pistol!=null && owner.IsYourPlayer && draw>=1f && !stowing && !owner.Physical.Sprinting &&
        !(pistol is Revolver) && string.Equals(pistol.WeapClass,"pistol",StringComparison.OrdinalIgnoreCase) &&
        pistol.ReloadMode==Weapon.EReloadMode.ExternalMagazine &&
        (!MagazineReloadActive || ReplayingMagazineReload);

    internal bool TryMagazineReload(Action<Callback> replay,Callback cancel)
    {
        if(!CanReloadMagazine)return false;
        if(delivered && owner.HandsController is Player.FirearmController gun)gun.SetTriggerPressed(false);
        held=pending=delivered=Aiming=false;
        int token=++magazineExchangeToken;
        nativeMagazineCompleted=false;
        startMagazineExchange=() => replay(result =>
        {
            if(token==magazineExchangeToken)nativeMagazineCompleted=true;
            cancel?.Invoke(result);
        });
        cancelMagazineExchange=cancel;
        magazineReload=MagazineReloadPhase.Retract;
        return true;
    }

    void CancelQueuedMagazine()
    {
        var cancel=cancelMagazineExchange;
        cancelMagazineExchange=null;
        startMagazineExchange=null;
        cancel?.Fail("Shield reload interrupted before the exchange.");
    }

    void StepMagazineReload()
    {
        if(!MagazineReloadActive)return;
        if(owner.Physical.Sprinting && startMagazineExchange!=null){
            CancelQueuedMagazine();
            magazineTime=MagazineRaiseTime*(1f-magazinePose);
            magazineReload=MagazineReloadPhase.Raise;
        }
        switch(magazineReload)
        {
            case MagazineReloadPhase.Retract:
                if(displayedExtension<=.001f){
                    magazineTime=0;
                    magazineReload=MagazineReloadPhase.Lower;
                }
                break;
            case MagazineReloadPhase.Lower:
                magazineTime+=Time.deltaTime;
                magazinePose=Mathf.Clamp01(magazineTime/MagazineLowerTime);
                if(magazinePose>=1f)magazineReload=MagazineReloadPhase.StartExchange;
                break;
            case MagazineReloadPhase.StartExchange:
                magazineTime=0;
                magazineReload=MagazineReloadPhase.Exchange;
                var exchange=startMagazineExchange;
                startMagazineExchange=null;
                cancelMagazineExchange=null;
                var gun=owner.HandsController as Player.FirearmController;
                var previousOperation=gun?.CurrentOperation;
                magazineWasPresent=pistol.GetCurrentMagazine()!=null;
                magazineWasLocked=gun?.FirearmsAnimator?.GetBoltCatch()==true;
                magazineChamberEmpty=pistol.ChamberAmmoCount==0;
                nativeSoundStep=0;
                nativeSoundEnd=0;
                ReplayingMagazineReload=true;
                try { exchange?.Invoke(); }
                finally { ReplayingMagazineReload=false; }
                if(gun?.CurrentOperation!=previousOperation)
                    nativeMagazineExchange=gun?.CurrentOperation as Player.FirearmController.ReloadExternalMagOperation;
                break;
            case MagazineReloadPhase.Exchange:
                if(owner.HandsController is Player.FirearmController controller)
                {
                    if(controller.CurrentOperation is Player.FirearmController.ReloadExternalMagOperation active)
                    {
                        if(nativeMagazineExchange==null){nativeMagazineExchange=active;magazineTime=0;}
                        magazineTime+=Time.deltaTime;
                        RunNativeMagazineEvents(controller,active);
                        break;
                    }
                    // The concealed exchange does not play the idle-entry animation event.
                    // Run the native idle handler even when insertion ended the reload early.
                    if(nativeMagazineExchange!=null && controller.CurrentOperation is Player.FirearmController.Idling idle)
                    {
                        idle.OnIdleStartEvent();
                        nativeMagazineExchange=null;
                    }
                    // The native operation can finish as soon as the fresh magazine is inserted.
                    // Keep the pistol lowered until the sound sequence has finished as well.
                    magazineTime+=Time.deltaTime;
                    if(nativeSoundStep>0 && magazineTime<Mathf.Max(.83f,nativeSoundEnd))break;
                    if(!nativeMagazineCompleted && nativeMagazineExchange==null && magazineTime<MagazineIdleTimeout)break;
                }
                else magazineTime+=Time.deltaTime;
                nativeMagazineExchange=null;
                magazineTime=0;
                magazineReload=MagazineReloadPhase.Settle;
                break;
            case MagazineReloadPhase.Settle:
                magazineTime+=Time.deltaTime;
                if(magazineTime<MagazineSettledTime)break;
                magazineTime=0;
                magazineReload=MagazineReloadPhase.Raise;
                break;
            case MagazineReloadPhase.Raise:
                magazineTime+=Time.deltaTime;
                magazinePose=1f-Mathf.Clamp01(magazineTime/MagazineRaiseTime);
                if(magazinePose<=0f)magazineReload=MagazineReloadPhase.None;
                break;
        }
    }

    static float EaseMagazinePose(float t) => t*t*t*(t*(6f*t-15f)+10f);

    float draw,extension,holster,displayedExtension;
    bool held,pending,delivered,replaying,stowing,extending;
    Action afterStow,cancelSwitch;
    Transform movedRoot;
    Vector3 originalLocal,writtenLocal;
    Quaternion originalRot,writtenRot,poseFacing;
    readonly ShieldPose pose=new ShieldPose(), sampled=new ShieldPose(), blendStart=new ShieldPose();
    Transform fingerRoot;
    readonly Transform[] digits=new Transform[15];
    int phaseId=-1;
    float switchedAt;
    bool positioned;
    int lastSolved=-1;
    Vector3 lastShoulder;
    float? carryHeight;
    bool wasSprinting,recoveringCarryHeight;
    float carryRecovery;
    Quaternion lastFacing;
    internal static bool Controls(Player.FirearmController gun)=>Current && Current.Active && Current.owner.HandsController==gun;
    internal bool HasSidearm=>pistol!=null;
    internal static bool Using(Weapon item)=>item!=null && Current && Current.HasShield && Current.pistol==item;
    internal static bool UsingMagazine(Magazine magazine)=>
        magazine!=null && Current && Current.HasShield &&
        ReferenceEquals(Current.pistol?.GetCurrentMagazine(),magazine);
    bool manualSpent;
    static bool NeedsManualCycle(Weapon w)=>w.BoltAction || w.ReloadMode==Weapon.EReloadMode.OnlyBarrel
        || (w.Template.ItemSound=="weap_pump" && w.SelectedFireMode!=Weapon.EFireMode.semiauto && w.SelectedFireMode!=Weapon.EFireMode.fullauto);
    internal void RecordShot(Weapon fired)
    {
        if(fired!=pistol)return;
        if(NeedsManualCycle(fired))manualSpent=true;
    }
    internal bool Trigger(Player.FirearmController gun,bool pressed)
    {
        if(pressed && (manualSpent || owner.Physical.Sprinting || MagazineReloadActive))return false;
        if(pressed && pistol.SelectedFireMode==Weapon.EFireMode.doublet && NeedsManualCycle(pistol)){
            gun.ChangeFireMode(Weapon.EFireMode.single);
            if(pistol.SelectedFireMode!=Weapon.EFireMode.single)return false;
        }
        if(replaying)return true;
        if(stowing)return !pressed;
        if(pressed && !held){held=true;pending=true;}
        if(!pressed){held=false;if(delivered){delivered=false;return true;}}
        return false;
    }
    internal static Player.FirearmController FullyLowered;
    float fatigue;
    bool depleted;
    internal void NoteDepletion(){depleted=true;}

    internal bool OwnsHands(IHandsController controller)=>HasShield && ReferenceEquals(owner.HandsController,controller);
    internal void BeginStow(Action resume,Action cancel)
    {
        if(stowing){if(afterStow==null){afterStow=resume;cancelSwitch=cancel;}else cancel?.Invoke();return;}
        if(!positioned && !Ready()){Stow();resume?.Invoke();return;}
        if(delivered && owner.HandsController is Player.FirearmController gun)gun.SetTriggerPressed(false);
        CancelQueuedMagazine();
        magazineReload=MagazineReloadPhase.None;
        magazinePose=0;
        held=pending=Aiming=false;stowing=true;holster=0;

        afterStow=resume;cancelSwitch=cancel;
    }
    internal void Select(Player player,Item selected,Weapon sidearm)
    {
        Stow();FullyLowered=null;nativeMotionFrame=-1;fatigue=0;depleted=false;carryFrame=-1;calibratedRoot=calibratedMarker=null;manualSpent=false;owner=player;shield=selected;pistol=sidearm;
        prop=Instantiate(Shields.Model(shield),transform,false);prop.SetActive(false);
        ShieldProtection.Attach(prop,selected,player);
        draw=extension=holster=displayedExtension=0; positioned=false;carryHeight=null;wasSprinting=recoveringCarryHeight=false;carryRecovery=0;phaseId=-1;held=pending=delivered=replaying=stowing=Aiming=false;
        Current=this;
    }
    internal void Stow()
    {
        CancelQueuedMagazine();
        magazineReload=MagazineReloadPhase.None;
        magazineTime=magazinePose=0;
        magazineExchangeToken++;
        nativeMagazineExchange=null;
        nativeMagazineCompleted=false;
        RestoreRoot();
        RestoreArmOffsets();
        if(Current==this)Current=null;
        var cancel=cancelSwitch;afterStow=cancelSwitch=null;
        if(slung && slung.Visual)slung.Visual.SetActive(true);
        if(prop){prop.SetActive(false);Destroy(prop);} slung=null;prop=null;shield=null;pistol=null;cancel?.Invoke();
    }
    void OnDisable()=>Stow();
    void LateUpdate()
    {
        if(!prop) return;
        var eq=owner.Inventory?.Equipment;
        if(eq==null || owner.HealthController?.IsAlive!=true){Stow();return;}
        bool equipped=eq.GetSlot(EquipmentSlot.FirstPrimaryWeapon).ContainedItem==shield ||
            eq.GetSlot(EquipmentSlot.SecondPrimaryWeapon).ContainedItem==shield;
        bool sidearmPresent=pistol==null || eq.GetSlot(EquipmentSlot.Holster).ContainedItem==pistol;
        if(!stowing && (!equipped || !sidearmPresent || !Ready())){
            if(positioned)BeginStow(null,null);
            else if(!equipped || !sidearmPresent)Stow();
            return;
        }
        if(owner.Physical.Sprinting)CancelForSprint();
        var arms=owner.Physical.HandsStamina;
        if(arms.Current<=.001f)depleted=true;
        else if(!arms.Exhausted)depleted=false;
        fatigue=Mathf.MoveTowards(fatigue,depleted?1:0,Time.deltaTime*4);
        if(stowing){
            holster=Mathf.Min(1,holster+Time.deltaTime/PoseTracks.HolsterDuration);
            if(positioned && lastSolved!=Time.frameCount){
                Evaluate();
                prop.transform.SetPositionAndRotation(lastShoulder+lastFacing*(pose.Shield+Shields.ShieldOnlyCm*.01f),
                    lastFacing*pose.ShieldRotation*Quaternion.Inverse(ShieldMotion.Frames[48].ShieldRotation));
            }
            if(holster>=1){
                if(pistol!=null)FullyLowered=owner.HandsController as Player.FirearmController;
                var next=afterStow;afterStow=cancelSwitch=null;Stow();next?.Invoke();
            }
            return;
        }
        draw=Mathf.Min(1,draw+Time.deltaTime/PoseTracks.DrawDuration);
        extending=pistol!=null && (Aiming||held||pending);
        if(draw>=1)extension=Mathf.MoveTowards(extension,extending?1:0,Time.deltaTime/(extending?PoseTracks.RaiseDuration:PoseTracks.LowerDuration));
        StepMagazineReload();
        if(displayedExtension>=1 && pending && positioned && owner.HandsController is Player.FirearmController gun){
            pending=false;replaying=true;
            try{gun.SetTriggerPressed(true);delivered=true;}
            finally{replaying=false;}
        }else if(delivered && !held && owner.HandsController is Player.FirearmController released){
            released.SetTriggerPressed(false);
        }
    }
    internal bool Active=>prop && (Ready() || stowing);
    bool Ready()=>pistol==null?owner.HandsController is Player.EmptyHandsController:owner.HandsController?.Item==pistol;
    void Evaluate()
    {
        int id=stowing?3:draw<1?0:extending?1:2;
        if(id==0)ShieldMotion.Sample(1,49,draw,sampled);
        else if(id==3)ShieldMotion.Sample(142,179,holster,sampled);
        else if(id==1)ShieldMotion.Sample(80,100,extension,sampled);
        else ShieldMotion.Sample(121,141,1-extension,sampled);
        if(phaseId!=id){
            if(phaseId<0){pose.Blend(sampled,sampled,0);phaseId=id;switchedAt=Time.time-1;blendStart.Blend(sampled,sampled,0);return;}
            switchedAt=Time.time;
            blendStart.Blend(pose,pose,0);phaseId=id;
        }
        pose.Blend(blendStart,sampled,Mathf.SmoothStep(0,1,(Time.time-switchedAt)/.10f));
    }
    Transform calibratedMarker,calibratedRoot;
    Vector3 localGrip;

    Quaternion rootFromAim,wristFromRoot;
    int nativeMotionFrame=-1;
    Vector3 pivotAfter,pivotTravel;
    Quaternion nativeTurn;
    internal void CaptureWeaponMotion(Vector3 before,Quaternion rotation,Transform root)
    {
        nativeMotionFrame=Time.frameCount;
        pivotAfter=root.position;
        pivotTravel=root.position-before;
        nativeTurn=root.rotation*Quaternion.Inverse(rotation);
    }
    void PlaceRightHand(Player.FirearmController gun,Transform marker,LimbIK right,Quaternion facing,ref Vector3 wrist,float weight,ShieldPose armPose)
    {
        if(!marker)return;
        if(calibratedMarker!=marker || !calibratedRoot){
            var links=marker.GetComponentInParent<TransformLinks>();
            var model=links?links.GetTransformOutOfRangeSafe(ECharacterWeaponBones.weapon):null;
            var mount=model && marker.IsChildOf(model)?model:gun.WeaponRoot;
            if(!mount || !model || !marker.IsChildOf(mount))return;
            var forward=gun.WeaponDirection;
            var up=Vector3.ProjectOnPlane(model.forward,forward);
            if(forward.sqrMagnitude<.001f || up.sqrMagnitude<.001f)return;
            rootFromAim=Quaternion.Inverse(Quaternion.LookRotation(forward,up))*mount.rotation;
            wristFromRoot=Quaternion.Inverse(mount.rotation)*marker.rotation;
            localGrip=mount.InverseTransformPoint(marker.position);
            calibratedRoot=mount;calibratedMarker=marker;
        }
        var root=calibratedRoot;
        movedRoot=root;originalLocal=root.localPosition;originalRot=root.localRotation;
        var motion=ShieldMotion.WristDelta(armPose);
        float tuck=EaseMagazinePose(magazinePose);
        motion=Quaternion.Slerp(motion,motion*Quaternion.Euler(29f,-17f,15f),tuck);
        var nativeGrip=marker.position;
        wrist=Vector3.Lerp(nativeGrip,wrist,weight);
        var turn=Quaternion.identity;
        if(!MagazineReloadActive && nativeMotionFrame>=0 && Time.frameCount-nativeMotionFrame<=1){
            turn=nativeTurn;
            var radius=nativeGrip-pivotAfter;
            wrist+=(pivotTravel+radius-Quaternion.Inverse(turn)*radius)*weight;
        }
        root.rotation=Quaternion.Slerp(root.rotation,turn*facing*motion*rootFromAim,weight);
        root.position=wrist-root.TransformVector(localGrip);
        writtenLocal=root.localPosition;writtenRot=root.localRotation;
        right.solver.SetIKRotation(root.rotation*wristFromRoot);right.solver.IKRotationWeight=1;
    }
    Quaternion carryFacing;
    int carryFrame=-1;
    Quaternion UpdateCarryFacing()
    {
        var target=owner.HandsRotation;
        if(carryFrame<0 || Time.frameCount-carryFrame>2 || Quaternion.Angle(carryFacing,target)>75f)
            carryFacing=target;
        else if(carryFrame!=Time.frameCount)
            carryFacing=Quaternion.Slerp(carryFacing,target,1f-Mathf.Exp(-14f*Time.deltaTime));
        carryFrame=Time.frameCount;
        return carryFacing;
    }
    internal struct ArmTranslation
    {
        Transform bone;
        Vector3 before,after;
        internal void Apply(Transform target,Vector3 worldOffset)
        {
            bone=target;before=bone.localPosition;
            bone.position+=worldOffset;after=bone.localPosition;
        }
        internal void Restore()
        {
            if(bone && (bone.localPosition-after).sqrMagnitude<1e-12f)bone.localPosition=before;
            bone=null;
        }
    }
    ArmTranslation leftTranslation,rightTranslation;
    internal void RestoreArmOffsets()
    {
        leftTranslation.Restore();rightTranslation.Restore();
    }
    void RestoreRoot()
    {
        if(movedRoot){
            if((movedRoot.localPosition-writtenLocal).sqrMagnitude<1e-10f)movedRoot.localPosition=originalLocal;
            if(Quaternion.Angle(movedRoot.localRotation,writtenRot)<.01f)movedRoot.localRotation=originalRot;
        }
        movedRoot=null;
    }
    internal void PoseLeftHand(LimbIK limb)
    {
        if(positioned)PoseFingers(limb,pose,poseFacing,ref fingerRoot,digits);
    }
    internal static void PoseFingers(LimbIK limb,ShieldPose pose,Quaternion poseFacing,ref Transform fingerRoot,Transform[] digits)
    {
        var wrist=limb.solver.bone3.transform;
        if(!wrist)return;
        if(fingerRoot!=wrist){
            fingerRoot=wrist;Array.Clear(digits,0,digits.Length);
            foreach(var t in wrist.GetComponentsInChildren<Transform>(true)){
                for(int i=0;i<digits.Length;i++)
                    if(t.name=="Base HumanLDigit"+(i/3+1)+(i%3+1))digits[i]=t;
            }
        }
        if(!digits[3]||!digits[6]||!digits[12])return;
        var src=Quaternion.LookRotation(digits[6].position-wrist.position,digits[3].position-digits[12].position);
        var dst=Quaternion.LookRotation(pose.Fingers[6]-pose.Left,pose.Fingers[3]-pose.Fingers[12]);
        wrist.rotation=poseFacing*dst*Quaternion.Inverse(src)*wrist.rotation;
        for(int i=0;i<digits.Length;i++){
            if(i%3==2 || !digits[i] || !digits[i+1])continue;
            var axis=digits[i+1].position-digits[i].position;
            digits[i].rotation=Quaternion.FromToRotation(axis,poseFacing*(pose.Fingers[i+1]-pose.Fingers[i]))*digits[i].rotation;
        }
    }
    internal void Solve(LimbIK left,LimbIK right,Transform marker)
    {
        if(!Active || !left.solver.bone1.transform || !right.solver.bone1.transform)return;
        RestoreRoot();
        RestoreArmOffsets();
        var view=owner.PlayerBody?.GetSlotViewByItem(shield)?.Model;
        if(view){slung=view.GetComponent<ShieldDisplay>();if(slung&&slung.Visual)slung.Visual.SetActive(false);}
        var facing=UpdateCarryFacing();
        var groupOffset=facing*(Shields.ArmsAndShieldCm*.01f);
        var shieldOffset=facing*(Shields.ShieldOnlyCm*.01f);
        var leftShoulder=left.solver.bone1.transform.position;
        var rightShoulder=right.solver.bone1.transform.position;
        var shoulder=(leftShoulder+rightShoulder)*.5f+groupOffset;
        Evaluate();
        var eye=owner.CameraPosition;
        if(eye && !stowing && draw>=1f){
            var up=eye.up;
            float height=Vector3.Dot(shoulder+facing*pose.Shield+shieldOffset-eye.position,up);
            bool sprinting=owner.Physical.Sprinting;
            if(wasSprinting && !sprinting){carryRecovery=.58f;recoveringCarryHeight=true;}
            wasSprinting=sprinting;
            if(!carryHeight.HasValue)carryHeight=height;
            if(!sprinting)
            {
                if(carryRecovery<=0f && height<=carryHeight.Value+.002f)recoveringCarryHeight=false;
                if(!recoveringCarryHeight)carryHeight=height;
                else if(carryRecovery>0)carryRecovery=Mathf.Max(0,carryRecovery-Time.deltaTime);
                else carryHeight=Mathf.MoveTowards(carryHeight.Value,height,Time.deltaTime*.35f);
            }
            var correction=up*Mathf.Min(0f,carryHeight.Value-height);
            groupOffset+=correction;
            shoulder+=correction;
        }
        var leftOffset=leftShoulder-left.solver.bone1.transform.position+groupOffset;
        if(leftOffset.sqrMagnitude>0f)leftTranslation.Apply(left.solver.bone1.transform,leftOffset);
        float t=Time.time;
        var tired=Quaternion.Euler(fatigue*4f*Mathf.Sin(t*3.8f),fatigue*2.8f*Mathf.Sin(t*2.7f),fatigue*3f*Mathf.Sin(t*3.1f));
        var frame=new ShieldPoseFrame(pose,shoulder,facing,tired,1);
        Vector3 l=frame.Left,r=frame.Right;
        var armPose=pose;
        if(Aiming && !stowing && HasSidearm){
            float phase=Time.time*2.8f;
            r+=facing*(new Vector3(Mathf.Sin(phase)*.0018f,Mathf.Sin(phase*.71f)*.0012f,0)*extension);
        }
        Vector3 upper=frame.Upper;
        Vector3 elbow=frame.Elbow;
        if(magazinePose>0f && !stowing){
            float tuck=EaseMagazinePose(magazinePose);
            float arc=4f*tuck*(1f-tuck);
            r+=facing*(new Vector3(.13f,-.34f,-.17f)*tuck+new Vector3(.023f,.012f,-.02f)*arc);
            elbow+=facing*(new Vector3(.09f,-.13f,-.095f)*tuck);
        }
        rightTranslation.Apply(right.solver.bone1.transform,upper-right.solver.bone1.transform.position);
        var plane=Vector3.Cross(elbow-upper,r-upper);
        if(plane.sqrMagnitude>1e-10f){
            right.solver.bendModifier=IKSolverLimb.BendModifier.Custom;
            right.solver.bendModifierWeight=0f;
            right.solver.bendNormal=plane.normalized;
        }
        poseFacing=frame.LeftBasis;
        prop.transform.SetPositionAndRotation(frame.Center+shieldOffset,frame.Rotation);
        if(!prop.activeSelf)prop.SetActive(true);
        if(pistol!=null && owner.HandsController is Player.FirearmController gun)
            PlaceRightHand(gun,marker,right,facing,ref r,1f,armPose);
        left.solver.IKPositionWeight=right.solver.IKPositionWeight=1;
        left.solver.SetIKPosition(l);right.solver.SetIKPosition(r);
        lastSolved=Time.frameCount;lastShoulder=shoulder;lastFacing=facing;positioned=true;displayedExtension=extension;
    }
 }
}
