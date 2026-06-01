using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SimManagementLib.SimAI
{
    /// <summary>
    /// 管理顾客结账阶段职责，负责把有账单顾客送去收银台并让零账单顾客等待收尾。
    /// </summary>
    public class LordToil_CustomerCheckout : LordToil
    {
        public IntVec3 shopCenter;

        /// <summary>
        /// 创建顾客结账阶段，负责保存收尾等待的商店中心点。
        /// </summary>
        public LordToil_CustomerCheckout(IntVec3 center) { shopCenter = center; }

        public override bool AssignsDuties => true;

        /// <summary>
        /// 禁止顾客在结账阶段改去满足长期需求，负责避免排队或收尾时被原版找饭逻辑打断。
        /// </summary>
        public override bool AllowSatisfyLongNeeds => false;

        /// <summary>
        /// 按顾客账单状态分配结账或等待职责。
        /// </summary>
        public override void UpdateAllDuties()
        {
            var lordJob = lord.LordJob as LordJob_CustomerVisit;

            foreach (Pawn pawn in lord.ownedPawns)
            {
                IntVec3 focus = lordJob?.GetCurrentShopCell(pawn) ?? shopCenter;
                // 如果这个人没有待付金额，直接让他闲逛等队友，不用去排队。
                if (lordJob == null || lordJob.GetAmountOwedForCheckout(pawn.thingIDNumber) <= 0f)
                {
                    pawn.mindState.duty = new PawnDuty(DutyDefOf.WanderClose) { focus = focus };
                }
                else
                {
                    // 有账单的人，下达排队结账的指令
                    pawn.mindState.duty = new PawnDuty(DefDatabase<DutyDef>.GetNamed("Customer_Checkout")) { focus = focus };
                }
            }

            // 兜底：如果所有顾客都没有待支付账单，立即结束结账阶段，避免整团顾客在店内长期游荡。
            lordJob?.CheckAllCheckoutsDone();
        }
    }
}
