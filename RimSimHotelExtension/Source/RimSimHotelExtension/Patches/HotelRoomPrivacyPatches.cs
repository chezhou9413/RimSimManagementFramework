using HarmonyLib;
using RimWorld;
using RimSimHotelExtension.GameComp;
using RimSimHotelExtension.Services;
using SimManagementLib.SimAI;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RimSimHotelExtension.Patches
{
    //把客房权限同时接入选点、门口寻路和实际迈步。
    internal static class HotelRoomPrivacyPatches
    {
        //门成本覆盖敞开门的捷径，迈步检查负责已经生成的旧路径。
        internal static void Apply(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(ForbidUtility), nameof(ForbidUtility.IsForbidden), new[] { typeof(IntVec3), typeof(Pawn) }),
                postfix: new HarmonyMethod(typeof(HotelRoomPrivacyPatches), nameof(CellForbiddenPostfix)));
            harmony.Patch(AccessTools.Method(typeof(ForbidUtility), nameof(ForbidUtility.IsForbiddenToPass)),
                postfix: new HarmonyMethod(typeof(HotelRoomPrivacyPatches), nameof(DoorForbiddenPostfix)));
            harmony.Patch(AccessTools.Method(typeof(Building_Door), nameof(Building_Door.PawnCanOpen)),
                postfix: new HarmonyMethod(typeof(HotelRoomPrivacyPatches), nameof(CanOpenPostfix)));
            harmony.Patch(AccessTools.Method(typeof(PathUtility), nameof(PathUtility.GetDoorCost)),
                postfix: new HarmonyMethod(typeof(HotelRoomPrivacyPatches), nameof(DoorCostPostfix)));
            harmony.Patch(AccessTools.Method(typeof(Pawn_PathFollower), "TryEnterNextPathCell"),
                prefix: new HarmonyMethod(typeof(HotelRoomPrivacyPatches), nameof(EnterCellPrefix)));
        }

        //所有经营顾客共用客房限制，殖民者、员工和其他访客不受影响。
        private static HotelRoomPrivacySnapshot SnapshotFor(Pawn pawn)
        {
            Map map = pawn?.Map;
            return map != null && pawn.GetLord()?.LordJob is LordJob_CustomerVisit
                ? HotelMapComponent.For(map).RoomPrivacy : HotelRoomPrivacySnapshot.Empty;
        }

        //选休息、进食和排队位置时避开其他人的客房。
        private static void CellForbiddenPostfix(IntVec3 c, Pawn pawn, ref bool __result)
        {
            if (!__result) __result = SnapshotFor(pawn).BlocksDestination(pawn?.thingIDNumber ?? -1, c);
        }

        //原版路径复查也能及时发现客房已被其他合同占用。
        private static void DoorForbiddenPostfix(Building_Door t, Pawn pawn, ref bool __result)
        {
            if (!__result && pawn != null) __result = BlocksDoor(pawn, t);
        }

        //顾客的通用开门权限不能覆盖已入住客房的限制。
        private static void CanOpenPostfix(Building_Door __instance, Pawn p, ref bool __result)
        {
            if (__result && p != null && BlocksDoor(p, __instance)) __result = false;
        }

        //1.7 的异步寻路只查整数和坐标，不在工作线程触发 GetRoom 或改动游戏状态。
        private static void DoorCostPostfix(Building_Door door, TraverseParms traverseParms, Pawn pawn, ref ushort __result)
        {
            if (traverseParms.mode == TraverseMode.ByPawn && pawn != null
                && BlocksDoor(pawn, door)) __result = ushort.MaxValue;
        }

        //仅同步订房检查可临时核实候选房间，实际寻路沿用已有合同权限。
        private static bool BlocksDoor(Pawn pawn, Building_Door door)
            => SnapshotFor(pawn).BlocksDoor(pawn.thingIDNumber, pawn.Position, door.Position,
                HotelBedReachabilityUtility.CandidateRoomFor(pawn));

        //入住或布局变化后，已缓存的路线也不能把顾客带入别人的房间。
        private static bool EnterCellPrefix(Pawn_PathFollower __instance, Pawn ___pawn)
        {
            if (!SnapshotFor(___pawn).BlocksStep(___pawn.thingIDNumber, ___pawn.Position, __instance.nextCell)) return true;
            __instance.StopDead();
            if (___pawn.CurJob != null) ___pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
            return false;
        }
    }
}
