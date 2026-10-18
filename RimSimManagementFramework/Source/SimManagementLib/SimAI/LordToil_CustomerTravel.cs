using RimWorld;
using SimManagementLib.SimZone;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SimManagementLib.SimAI
{
    //类职责：管理顾客前往已缓存商店入口的旅行阶段，不在 LordToil 中扫描货柜或执行寻路。
    public class LordToil_CustomerTravel : LordToil
    {
        public override IntVec3 FlagLoc
        {
            get
            {
                Pawn pawn = FirstActivePawn();
                LordJob_CustomerVisit visit = lord?.LordJob as LordJob_CustomerVisit;
                return ResolveTravelTargetCell(pawn, visit);
            }
        }

        public override bool AllowSatisfyLongNeeds => false;

        //给顾客分配前往缓存入口的职责。
        public override void UpdateAllDuties()
        {
            LordJob_CustomerVisit visit = lord?.LordJob as LordJob_CustomerVisit;
            for (int i = 0; i < lord.ownedPawns.Count; i++)
            {
                Pawn pawn = lord.ownedPawns[i];
                IntVec3 dest = ResolveTravelTargetCell(pawn, visit);
                PawnDuty duty = new PawnDuty(DutyDefOf.TravelOrLeave, dest)
                {
                    maxDanger = Danger.Deadly,
                    locomotion = LocomotionUrgency.Sprint
                };
                pawn.mindState.duty = duty;
            }
        }

        //周期检查顾客是否到达当前目标店，职责是用区划包含和缓存格执行常数时间判断。
        public override void LordToilTick()
        {
            if (Find.TickManager.TicksGame % 205 != 0) return;
            LordJob_CustomerVisit visit = lord?.LordJob as LordJob_CustomerVisit;
            if (visit == null) return;

            bool allArrived = true;
            for (int i = 0; i < lord.ownedPawns.Count; i++)
            {
                Pawn pawn = lord.ownedPawns[i];
                if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned) continue;
                if (!HasArrivedAtCurrentShop(pawn, visit))
                {
                    allArrived = false;
                    break;
                }
            }

            if (allArrived)
            {
                for (int i = 0; i < lord.ownedPawns.Count; i++)
                {
                    Pawn pawn = lord.ownedPawns[i];
                    if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned) continue;
                    visit.GetOrCreateSession(pawn)?.NotifyArrived(visit, pawn);
                }
                lord.ReceiveMemo("TravelArrived");
            }
        }

        //返回当前活跃顾客，职责是为旗帜位置提供目标查询对象。
        private Pawn FirstActivePawn()
        {
            return (lord?.LordJob as LordJob_CustomerVisit)?.FirstActivePawn();
        }

        //返回顾客进入商店的缓存目标，职责是避免旅行职责重复选择目标。
        private static IntVec3 ResolveTravelTargetCell(Pawn pawn, LordJob_CustomerVisit visit)
        {
            if (pawn?.Map == null || visit == null) return IntVec3.Invalid;
            return visit.GetCurrentShopCell(pawn);
        }

        //判断顾客是否到达当前商店，职责是只读取区划包含关系和缓存入口格。
        private static bool HasArrivedAtCurrentShop(Pawn pawn, LordJob_CustomerVisit visit)
        {
            if (pawn?.Map == null || visit == null) return false;
            Zone_Shop shop = visit.GetCurrentShop(pawn);
            if (shop == null) return false;

            if (shop.Cells.Contains(pawn.Position))
                return true;
            IntVec3 dest = ResolveTravelTargetCell(pawn, visit);
            return dest.IsValid && pawn.Position.DistanceToSquared(dest) <= 2f;
        }
    }
}
