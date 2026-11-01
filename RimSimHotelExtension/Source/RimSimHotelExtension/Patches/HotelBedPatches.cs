using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimSimHotelExtension.GameComp;
using RimSimHotelExtension.Tool;
using RimSimHotelExtension.UI;
using Verse;

namespace RimSimHotelExtension.Patches
{
    //把旅客用途接入原版床菜单、用途切换和找床流程，不替换床的建筑类型。
    internal static class HotelBedPatches
    {
        //按已核对的原版方法挂接，普通床和模组派生床共用这些入口。
        internal static void Apply(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(Building_Bed), nameof(Building_Bed.GetGizmos)),
                postfix: new HarmonyMethod(typeof(HotelBedPatches), nameof(GizmosPostfix)));
            harmony.Patch(AccessTools.Method(typeof(Building_Bed), nameof(Building_Bed.SetBedOwnerTypeByInterface)),
                prefix: new HarmonyMethod(typeof(HotelBedPatches), nameof(OwnerInterfacePrefix)));
            harmony.Patch(AccessTools.PropertySetter(typeof(Building_Bed), nameof(Building_Bed.ForOwnerType)),
                prefix: new HarmonyMethod(typeof(HotelBedPatches), nameof(OwnerTypePrefix)));
            harmony.Patch(AccessTools.PropertySetter(typeof(Building_Bed), nameof(Building_Bed.ForPrisoners)),
                prefix: new HarmonyMethod(typeof(HotelBedPatches), nameof(PrisonerPrefix)));
            harmony.Patch(AccessTools.PropertySetter(typeof(Building_Bed), nameof(Building_Bed.Medical)),
                prefix: new HarmonyMethod(typeof(HotelBedPatches), nameof(MedicalPrefix)));
            harmony.Patch(AccessTools.Method(typeof(RestUtility), nameof(RestUtility.CanUseBedNow)),
                postfix: new HarmonyMethod(typeof(HotelBedPatches), nameof(CanUseBedPostfix)));
            harmony.Patch(AccessTools.Method(typeof(CompAssignableToPawn_Bed), "ShouldShowAssignmentGizmo"),
                postfix: new HarmonyMethod(typeof(HotelBedPatches), nameof(AssignmentGizmoPostfix)));
            harmony.Patch(AccessTools.Method(typeof(CompAssignableToPawn_Bed), nameof(CompAssignableToPawn_Bed.CanAssignTo)),
                postfix: new HarmonyMethod(typeof(HotelBedPatches), nameof(CanAssignPostfix)));
        }

        //只为玩家已建成的床扩展菜单，原版医疗等其他按钮保留。
        private static void GizmosPostfix(Building_Bed __instance, ref IEnumerable<Gizmo> __result)
        {
            if (__instance.Spawned && __instance.Faction == Faction.OfPlayer && HotelMapComponent.SupportsRentalBed(__instance))
                __result = ReplaceOwnerCommand(__instance, __result);
        }

        //没有理念 DLC 或没有原版用途按钮的床也提供相同登记入口。
        private static IEnumerable<Gizmo> ReplaceOwnerCommand(Building_Bed bed, IEnumerable<Gizmo> gizmos)
        {
            bool replaced = false;
            foreach (var gizmo in gizmos)
            {
                if (gizmo is Command_SetBedOwnerType || (gizmo is Command_Toggle toggle
                    && toggle.hotKey == KeyBindingDefOf.Misc3
                    && toggle.defaultLabel == "CommandBedSetForPrisonersLabel".Translate().ToString()))
                {
                    yield return new Command_HotelBedUse(bed) { hotKey = ((Command)gizmo).hotKey };
                    replaced = true;
                }
                else yield return gizmo;
            }
            if (!replaced) yield return new Command_HotelBedUse(bed);
        }

        //先检查原版会连带切换的同房床位，避免囚犯用途把入住中的客床一并改掉。
        private static bool OwnerInterfacePrefix(Building_Bed __instance, BedOwnerType ownerType)
        {
            var selected = Find.Selector.SelectedObjects.OfType<Building_Bed>().ToList();
            if (!selected.Contains(__instance)) selected.Add(__instance);
            var affected = new HashSet<Building_Bed>(selected);
            foreach (var bed in selected)
            {
                if (bed.ForOwnerType == ownerType) continue;
                var room = bed.GetRoom();
                if (room == null) continue;
                foreach (var other in room.ContainedBeds)
                    if (other.ForPrisoners || ownerType == BedOwnerType.Prisoner) affected.Add(other);
            }
            foreach (var bed in affected)
            {
                var manager = HotelMapComponent.For(bed.Map);
                if (manager?.IsBedBooked(bed) != true) continue;
                Messages.Message(HotelText.T("RSH.Bed.Booked"), bed, MessageTypeDefOf.RejectInput, false);
                return false;
            }
            //旅客用途沿用原版的普通床类型，切回普通用途时须显式取消登记。
            if (ownerType == BedOwnerType.Colonist)
                foreach (var bed in selected)
                    if (bed.ForOwnerType == BedOwnerType.Colonist && !CancelRegistration(bed)) return false;
            return true;
        }

        //真正改变普通、囚犯或奴隶用途时同步退出出租登记。
        private static bool OwnerTypePrefix(Building_Bed __instance, BedOwnerType value)
            => value == __instance.ForOwnerType || (value == BedOwnerType.Slave && !ModsConfig.IdeologyActive)
                || CancelRegistration(__instance);

        //兼顾仍直接使用原版囚犯属性的操作入口。
        private static bool PrisonerPrefix(Building_Bed __instance, bool value)
            => value == __instance.ForPrisoners || CancelRegistration(__instance);

        //医疗用途和出租互斥，已有合同的床拒绝切换。
        private static bool MedicalPrefix(Building_Bed __instance, bool value)
            => !value || __instance.Medical || CancelRegistration(__instance);

        //取消失败时让原版 setter 停在原状态，并向玩家解释原因。
        private static bool CancelRegistration(Building_Bed bed)
        {
            var manager = HotelMapComponent.For(bed.Map);
            if (manager?.IsRegisteredBed(bed) != true) return true;
            if (manager.RegisterBed(null, bed, false, out string reason)) return true;
            Messages.Message(reason, bed, MessageTypeDefOf.RejectInput, false);
            return false;
        }

        //保留原版床型、体型和可用性检查，再阻止普通殖民者占用出租床。
        private static void CanUseBedPostfix(Thing bedThing, Pawn sleeper, ref bool __result)
        {
            if (__result && bedThing is Building_Bed bed)
                __result = HotelMapComponent.For(bed.Map).AllowsBedUser(bed, sleeper);
        }

        //旅客床由合同分配，隐藏会把床重新分给殖民者的原版按钮。
        private static void AssignmentGizmoPostfix(CompAssignableToPawn_Bed __instance, ref bool __result)
        {
            if (__result && __instance.parent is Building_Bed bed && HotelMapComponent.For(bed.Map)?.IsRegisteredBed(bed) == true)
                __result = false;
        }

        //其他界面请求分配时也沿用旅客专属约束。
        private static void CanAssignPostfix(CompAssignableToPawn_Bed __instance, Pawn pawn, ref AcceptanceReport __result)
        {
            if (__result.Accepted && __instance.parent is Building_Bed bed
                && HotelMapComponent.For(bed.Map)?.AllowsBedUser(bed, pawn) == false)
                __result = HotelText.T("RSH.Bed.GuestOnly");
        }
    }
}
