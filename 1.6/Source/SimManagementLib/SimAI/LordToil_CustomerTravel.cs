using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SimManagementLib.SimAI
{
    /// <summary>
    /// 管理顾客前往当前目标商店的旅行阶段，负责支持跨店后动态更新目标。
    /// </summary>
    public class LordToil_CustomerTravel : LordToil
    {
        private const float ArrivedRadius = 10f;

        public override IntVec3 FlagLoc
        {
            get
            {
                Pawn pawn = FirstActivePawn();
                LordJob_CustomerVisit visit = lord?.LordJob as LordJob_CustomerVisit;
                return visit?.GetCurrentShopCell(pawn) ?? IntVec3.Invalid;
            }
        }

        public override bool AllowSatisfyLongNeeds => false;

        /// <summary>
        /// 给顾客分配前往当前目标商店的职责。
        /// </summary>
        public override void UpdateAllDuties()
        {
            LordJob_CustomerVisit visit = lord?.LordJob as LordJob_CustomerVisit;
            for (int i = 0; i < lord.ownedPawns.Count; i++)
            {
                Pawn pawn = lord.ownedPawns[i];
                IntVec3 dest = visit?.GetCurrentShopCell(pawn) ?? IntVec3.Invalid;
                PawnDuty duty = new PawnDuty(DutyDefOf.TravelOrLeave, dest)
                {
                    maxDanger = Danger.Deadly
                };
                pawn.mindState.duty = duty;
            }
        }

        /// <summary>
        /// 周期性检查顾客是否到达当前目标店，负责推进到浏览阶段。
        /// </summary>
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
                IntVec3 dest = visit.GetCurrentShopCell(pawn);
                if (!dest.IsValid || !pawn.Position.InHorDistOf(dest, ArrivedRadius) || !pawn.CanReach(dest, PathEndMode.ClosestTouch, Danger.Deadly))
                {
                    allArrived = false;
                    break;
                }
            }

            if (allArrived)
                lord.ReceiveMemo("TravelArrived");
        }

        /// <summary>
        /// 返回当前活跃顾客，负责为旗帜位置提供目标查询对象。
        /// </summary>
        private Pawn FirstActivePawn()
        {
            return (lord?.LordJob as LordJob_CustomerVisit)?.FirstActivePawn();
        }
    }
}
