using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SimManagementLib.SimAI
{
    /// <summary>
    /// 管理顾客浏览阶段职责，负责让顾客逛店并在商店关门后进入结账收尾。
    /// </summary>
    public class LordToil_CustomerBrowse : LordToil
    {
        private const int CloseCheckIntervalTicks = 250;

        public IntVec3 shopCenter;

        /// <summary>
        /// 创建顾客浏览阶段，负责记录顾客围绕闲逛的商店中心点。
        /// </summary>
        public LordToil_CustomerBrowse(IntVec3 shopCenter)
        {
            this.shopCenter = shopCenter;
        }

        /// <summary>
        /// 标记本阶段会主动分配职责，负责让原版执行顾客浏览 ThinkTree。
        /// </summary>
        public override bool AssignsDuties => true;

        /// <summary>
        /// 禁止顾客在浏览阶段改去满足长期需求，负责把饥饿等异常交给顾客访问看门狗处理。
        /// </summary>
        public override bool AllowSatisfyLongNeeds => false;

        /// <summary>
        /// 给所有顾客分配浏览货架职责，负责驱动顾客在商店内寻找商品或服务。
        /// </summary>
        public override void UpdateAllDuties()
        {
            foreach (Pawn pawn in lord.ownedPawns)
            {
                LordJob_CustomerVisit visit = lord?.LordJob as LordJob_CustomerVisit;
                IntVec3 focus = visit?.GetCurrentShopCell(pawn) ?? shopCenter;
                PawnDuty duty = new PawnDuty(DefDatabase<DutyDef>.GetNamed("Customer_BrowseShelf"))
                {
                    focus = focus,
                    locomotion = LocomotionUrgency.Amble
                };
                pawn.mindState.duty = duty;
            }
        }

        /// <summary>
        /// 周期性检查商店营业状态，负责在关店后停止继续浏览并推进顾客收尾。
        /// </summary>
        public override void LordToilTick()
        {
            if (Find.TickManager.TicksGame % CloseCheckIntervalTicks != 0) return;

            LordJob_CustomerVisit visit = lord?.LordJob as LordJob_CustomerVisit;
            if (visit == null) return;

            Pawn pawn = visit.FirstActivePawn();
            Zone_Shop shop = visit.GetCurrentShop(pawn);
            if (shop != null && shop.IsOpenNow()) return;

            MarkActivePawnsReadyForCheckout(visit);
        }

        /// <summary>
        /// 将仍在地图上的顾客标记为准备结账，负责让关店后的顾客进入付款或离店流程。
        /// </summary>
        private void MarkActivePawnsReadyForCheckout(LordJob_CustomerVisit visit)
        {
            if (visit == null || lord?.ownedPawns == null) return;

            for (int i = 0; i < lord.ownedPawns.Count; i++)
            {
                Pawn pawn = lord.ownedPawns[i];
                if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned) continue;
                visit.MarkPawnReadyForCheckout(pawn.thingIDNumber);
            }

            lord.ReceiveMemo("Customer_ReadyToCheckout");
            visit.CheckAllCheckoutsDone();
        }
    }
}
