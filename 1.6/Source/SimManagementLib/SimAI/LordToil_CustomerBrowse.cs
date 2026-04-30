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
    public class LordToil_CustomerBrowse : LordToil
    {
        public IntVec3 shopCenter;

        public LordToil_CustomerBrowse(IntVec3 shopCenter)
        {
            this.shopCenter = shopCenter;
        }

        // 【隐患6修复】：这里必须重写为 true，否则后续的 JobGiver 不会被原版执行！
        public override bool AssignsDuties => true;

        public override void UpdateAllDuties()
        {
            foreach (Pawn pawn in lord.ownedPawns)
            {
                // 给小人挂上我们自定义的 "浏览货架" 的职责
                // 注意："Customer_BrowseShelf" 是一个 DefName，我们稍后要在 XML 里定义它
                PawnDuty duty = new PawnDuty(DefDatabase<DutyDef>.GetNamed("Customer_BrowseShelf"))
                {
                    focus = shopCenter,
                    locomotion = LocomotionUrgency.Amble // 用闲逛的步态，看起来像在逛街
                };
                pawn.mindState.duty = duty;
            }
        }
    }
}
