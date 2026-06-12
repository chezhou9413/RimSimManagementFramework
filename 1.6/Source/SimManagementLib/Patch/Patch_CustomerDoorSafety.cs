using HarmonyLib;
using RimWorld;
using SimManagementLib.Tool;
using Verse;

namespace SimManagementLib.Patch
{
    // 阻止顾客打开玩家手动禁用的门，负责补齐寻路检查以外的实际开门入口。
    [HarmonyPatch(typeof(Building_Door), nameof(Building_Door.PawnCanOpen))]
    public static class Patch_CustomerDoorSafety
    {
        // 在门判断前识别模拟经营顾客，遇到玩家禁用门时直接返回不可打开。
        public static bool Prefix(Building_Door __instance, Pawn p, ref bool __result)
        {
            if (!CustomerSafetyUtility.IsCustomerPawn(p) || !CustomerSafetyUtility.IsPlayerForbiddenDoor(__instance))
                return true;

            __result = false;
            return false;
        }
    }
}
