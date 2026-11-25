using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Threading.Tasks;
using EFT;
using EFT.CameraControl;
using EFT.InventoryLogic;
using EFT.UI.DragAndDrop;
using EFT.UI.WeaponModding;
using HarmonyLib;
using UnityEngine;

namespace Hawks.RonShields
{
    public sealed class ShieldItemPreview : MonoBehaviour
    {
        internal static void Install(Harmony patches)
        {
            patches.Patch(AccessTools.Method(typeof(ItemViewFactory), nameof(ItemViewFactory.LoadItemIcon)),
                prefix:new HarmonyMethod(typeof(ShieldItemPreview), nameof(UseShieldPartIcon)));
            patches.Patch(AccessTools.Method(typeof(GridItemView),"UpdateItemValue"),prefix:new HarmonyMethod(typeof(ShieldItemPreview),nameof(GridCondition)));
            patches.Patch(AccessTools.Method(typeof(EFT.UI.ItemSpecificationPanel),"RecreateAttributeBars"),transpiler:new HarmonyMethod(typeof(ShieldItemPreview),nameof(ShieldAttributes)));
            patches.Patch(AccessTools.Method(typeof(EFT.UI.ItemSpecificationPanel),"method_1"),postfix:new HarmonyMethod(typeof(ShieldItemPreview),nameof(HideGunCondition)));
            patches.Patch(AccessTools.Method(typeof(EFT.UI.StaticIcons),"GetAttributeIcon",new[]{typeof(Enum)}),prefix:new HarmonyMethod(typeof(ShieldItemPreview),nameof(ConditionIcon)));
            patches.Patch(AccessTools.Method(typeof(EFT.UI.ItemSpecificationPanel),"InitInteractionButtonsPanel"), transpiler:new HarmonyMethod(typeof(ShieldItemPreview),nameof(InspectMenu)));
            patches.Patch(AccessTools.Method(typeof(Slot),"CheckConflictingItems"), prefix:new HarmonyMethod(typeof(ShieldItemPreview),nameof(OneShield)));
            patches.Patch(AccessTools.Method(typeof(SlotItemView),"ValidateDurability"),postfix:new HarmonyMethod(typeof(ShieldItemPreview),nameof(SlotBar)));
            patches.Patch(AccessTools.PropertyGetter(typeof(RagfairOfferItemView),"ShowDurabilitySlider"),postfix:new HarmonyMethod(typeof(ShieldItemPreview),nameof(OfferBar)));
            patches.Patch(AccessTools.Method(typeof(EFT.UI.Ragfair.OfferItemDescription), "AddCounter"),
                prefix: new HarmonyMethod(typeof(ShieldItemPreview), nameof(ShieldOfferCondition)));

            var swap=new HarmonyMethod(typeof(ShieldItemPreview),nameof(PreviewFactory));
            patches.Patch(AccessTools.Method(AccessTools.Inner(typeof(WeaponPreview),"CG_SetupItemPreview"),"method_1"),transpiler:swap);
            patches.Patch(AccessTools.Method(AccessTools.Inner(typeof(ItemIconCreator),"CG_RenderModel"),"MoveNext"),transpiler:swap);
            patches.Patch(AccessTools.Method(typeof(GridItemView),"SetInscription"),prefix:new HarmonyMethod(typeof(ShieldItemPreview),nameof(Caption)));
            patches.Patch(AccessTools.Method(typeof(IconsHash),"GetItemHash",new[]{typeof(Item)}),postfix:new HarmonyMethod(typeof(ShieldItemPreview),nameof(IconHash)));
            patches.Patch(AccessTools.Method(typeof(ItemIconCreator),"PoseModelByPivot"),prefix:new HarmonyMethod(typeof(ShieldItemPreview),nameof(FaceCamera)));
        }
        static IEnumerable<CodeInstruction> PreviewFactory(IEnumerable<CodeInstruction> code)
        {
            var sync=AccessTools.Method(typeof(ObjectsFactory),"CreateCleanLootPrefab");
            var asyncFactory=AccessTools.Method(typeof(ObjectsFactory),"CreateCleanLootPrefabAsync");
            int replaced=0;
            foreach(var op in code){
                bool async=op.Calls(asyncFactory);
                if(async || op.Calls(sync)){
                    op.opcode=OpCodes.Call;
                    op.operand=AccessTools.Method(typeof(ShieldItemPreview),async?nameof(ForIcon):nameof(ForInspection));
                    replaced++;
                }
                yield return op;
            }
            if(replaced!=1)throw new InvalidOperationException("Expected one preview model factory");
        }
        static GameObject ForInspection(ObjectsFactory factory,Item item,ECameraType camera,IPlayer player)
        {
            var model=Shields.Model(item);
            return model?Create(model):factory.CreateCleanLootPrefab(item,camera,player);
        }
        static Task<GameObject> ForIcon(ObjectsFactory factory,Item item,IPlayer player)
        {
            var model=Shields.Model(item);
            return model?Task.FromResult(Create(model)):factory.CreateCleanLootPrefabAsync(item,player);
        }
        static GameObject Create(GameObject model)
        {
            var go=Instantiate(model);
            go.AddComponent<ShieldItemPreview>();
            var pivot=go.AddComponent<PreviewPivot>();
            pivot.pivotPosition=ShieldDisplay.DepthBounds(model).center;
            pivot.scale=Vector3.one;
            pivot.Icon.rotation=Quaternion.Euler(0,180,0);
            return go;
        }
        static void FaceCamera(GameObject __0,Camera __1,PreviewPivot.IconSettings __3)
        {
            if(!__0.GetComponent<ShieldItemPreview>())return;
            __3.rotation=Quaternion.LookRotation(-__1.transform.forward,__1.transform.up);
            __3.boundsScale=.88f;
        }
        enum ConditionId { Body=6144001, Window }
        static void ConditionIcon(ref Enum __0){if(__0 is ConditionId)__0=EItemAttributeId.Durability;}
        static string Points(float current,float capacity)=>$"{Mathf.CeilToInt(current)}/{capacity}";
        static void GridCondition(GridItemView __instance,ref string __0)
        {
            if (!IsShield(__instance.Item)) return;
            var hp=ShieldCondition.Load(__instance.Item);
            __0=hp.BodyMax+hp.WindowMax>0 ? Points(hp.Body+hp.Window,hp.BodyMax+hp.WindowMax) : "";
        }
        internal static bool IsShield(Item item) => item != null &&
            (item.TemplateId=="692340510000000000000001" || item.TemplateId=="692340510000000000000002");

        static void SlotBar(SlotItemView __instance,DurabilitySlider ____durabilitySlider)
        {
            if(IsShield(__instance.Item)) ____durabilitySlider.ShowDurability=false;
        }
        static void ShieldOfferCondition(EFT.UI.Ragfair.Offer ____offer, ref EItemAttributeId iconType,
            ref float value, ref float maxValue, ref string textColor)
        {
            if (iconType != EItemAttributeId.Durability || !IsShield(____offer?.Item)) return;
            iconType = EItemAttributeId.ArmorPoints;

        }

        static void OfferBar(RagfairOfferItemView __instance,ref bool __result)
        {
            if(IsShield(__instance.Item)) __result=false;
        }

        static bool OneShield(Slot __instance, Item __0, ref Diz.LanguageExtensions.Option<bool> __result)
        {
            if (!IsShield(__0) || !(__instance.ParentItem is InventoryEquipment equipment)) return true;
            foreach (var slot in equipment.Slots) {
                var other = slot.ContainedItem;
                if (slot == __instance || !IsShield(other) || other.Id == __0.Id) continue;
                __result = new Slot.ConflictingItemError(__0,equipment,other);
                return false;
            }
            return true;
        }

        static IEnumerable<CodeInstruction> InspectMenu(IEnumerable<CodeInstruction> code)
        {
            var show=AccessTools.Method(typeof(EFT.UI.InteractionButtonsContainer),"Show").MakeGenericMethod(typeof(EItemInfoButton));
            foreach(var op in code) {
                if(op.Calls(show)) {
                    op.opcode=OpCodes.Call;
                    op.operand=AccessTools.Method(typeof(ShieldItemPreview),nameof(ShowButtons));
                }
                yield return op;
            }
        }
        static void ShowButtons(EFT.UI.InteractionButtonsContainer container,EFT.UI.ContextInteractions<EItemInfoButton> actions,
            IReadOnlyDictionary<EItemInfoButton,string> names,List<EItemInfoButton> exclusions,Item item,
            IReadOnlyDictionary<EItemInfoButton,Sprite> icons,bool autoClose)
        {
            ShieldButtons(item,ref exclusions);
            container.Show(actions,names,exclusions,item,icons,autoClose);
        }
        static void ShieldButtons(Item __3, ref List<EItemInfoButton> __2)
        {
            if (!IsShield(__3)) return;
            var exclusions = __2 == null ? new List<EItemInfoButton>() : new List<EItemInfoButton>(__2);
            exclusions.AddRange(new[]{EItemInfoButton.EditBuild,EItemInfoButton.CheckMagazine,EItemInfoButton.LoadAmmo,
                EItemInfoButton.UnloadAmmo,EItemInfoButton.Reload,EItemInfoButton.Load,EItemInfoButton.Unload,
                EItemInfoButton.Disassemble,EItemInfoButton.ApplyMagPreset,EItemInfoButton.TopUp});
            __2 = exclusions;
        }

        static IEnumerable<CodeInstruction> ShieldAttributes(IEnumerable<CodeInstruction> code)
        {
            var field = AccessTools.Field(typeof(Item),nameof(Item.Attributes));
            foreach (var op in code) {
                if (op.LoadsField(field)) {
                    op.opcode = OpCodes.Call;
                    op.operand = AccessTools.Method(typeof(ShieldItemPreview),nameof(PageAttributes));
                }
                yield return op;
            }
        }
        static string Local(string en,string zh,string ja)
        {
            var lang=LocalizationManager.Instance.Culture ?? "en";
            if(lang=="ch" || lang.StartsWith("zh")) return zh;
            if(lang=="jp" || lang.StartsWith("ja")) return ja;
            return en;
        }

        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Item,List<ItemAttribute>> pages =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Item,List<ItemAttribute>>();

        static List<ItemAttribute> PageAttributes(Item item) =>
            IsShield(item) ? pages.GetValue(item,MakeArmorRows) : item.Attributes;

        static List<ItemAttribute> MakeArmorRows(Item item)
        {
            var condition=ShieldCondition.Load(item);
            var rows=new List<ItemAttribute>();
            Add(EItemAttributeId.ArmorPoints,()=>Local("Armor points","护甲点数","アーマーポイント"),
                ()=>condition.Body+condition.Window,()=>(condition.Body+condition.Window).ToString("0.#"));
            Add(EItemAttributeId.ArmorZone,()=>Local("Armor areas","防护区域","防護部位"),()=>2,
                ()=>Local("Shield body / Viewport","盾身 / 观察窗","盾本体 / 防弾窓"));
            Add(EItemAttributeId.ArmorClass,()=>Local("Armor class","防弹等级","アーマークラス"),()=>5.5f,()=>"5.5 / 4");
            return rows;
            void Add(Enum id,Func<string> title,Func<float> number,Func<string> value) {
                rows.Add(new ItemAttribute(id){DisplayNameFunc=title,Base=number,StringValue=value,
                    DisplayType=()=>EItemAttributeDisplayType.Compact});
            }
        }

        const string GlassTemplate="59e366c186f7741778269d85";
        static Item glassIconItem;

        static void UseShieldPartIcon(ref Item __0)
        {
            if (__0 == null) return;
            if (__0.TemplateId == "692340510000000000000011" &&
                __0.CurrentAddress is SlotItemAddress address && IsShield(address.Slot.ParentItem))
            {
                __0=address.Slot.ParentItem;
            }
            else if (__0.TemplateId == "692340510000000000000012")
            {
                glassIconItem ??= Comfort.Common.Singleton<ItemFactory>.Instance.CreateItem(MongoID.Generate(),GlassTemplate,null);
                __0=glassIconItem;
            }
        }

        static void HideGunCondition(Item ____item, UnityEngine.Component ____charDurability,
            GameObject ____masteringPanel, UnityEngine.Component ____charMastering, EFT.UI.ItemInfoWindowLabels ____itemLabels)
        {
            if (!IsShield(____item)) return;
            ____charDurability.gameObject.SetActive(false);
            ____masteringPanel.SetActive(false);
            ____charMastering.gameObject.SetActive(false);
            ____itemLabels.SetItemTypeText(Local("Gear > Shields","装备 > 盾牌","装備 > シールド"),false);
        }
        static void Caption(GridItemView __instance,ref string __0)
        {
            if(Shields.Model(__instance.Item))__0=string.Empty;
        }
        static void IconHash(Item __0,ref int __result)
        {
            if(Shields.Model(__0))__result^=0x524F4E46;
        }
    }
}
