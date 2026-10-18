using SimManagementLib.SimZone;
using RimWorld;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimAI.CustomerVisit
{
    //选择顾客浏览站位，职责是让派工和预约共用可站立、可达及空闲条件。
    internal static class CustomerWindowShopTargetUtility
    {
        //从店内随机起点寻找空闲格，职责是避免所有顾客反复争抢缓存入口。
        public static bool TryFindCell(Pawn pawn, Zone_Shop shop, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            if (pawn?.Map == null || shop?.Map != pawn.Map || shop.Cells.Count == 0) return false;
            int start = Rand.Range(0, shop.Cells.Count);
            for (int i = 0; i < shop.Cells.Count; i++)
            {
                IntVec3 candidate = shop.Cells[(start + i) % shop.Cells.Count];
                if (!CanUseCell(pawn, candidate)) continue;
                cell = candidate;
                return true;
            }
            return false;
        }

        //核实站位与原版预约条件，职责是避让其他顾客、冥想者及工作台交互预约。
        public static bool CanUseCell(Pawn pawn, IntVec3 cell)
        {
            if (pawn?.Map == null || !cell.InBounds(pawn.Map) || !cell.Standable(pawn.Map)
                || cell.IsForbidden(pawn) || !pawn.CanReserveSittableOrSpot(cell)
                || !pawn.CanReserveAndReach(cell, PathEndMode.OnCell, Danger.Some)) return false;
            foreach (Thing thing in cell.GetThingList(pawn.Map))
                if (thing is Pawn occupant && occupant != pawn) return false;
            return true;
        }
    }
}
