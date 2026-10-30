using SimManagementLib.Api;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SimManagementLib.SimAI
{
    //让已入住顾客按扩展生活日程行动，关闭原版自行取用殖民地食物。
    public sealed class LordToil_CustomerLongStay : LordToil
    {
        public override bool AssignsDuties => true;
        public override bool AllowSatisfyLongNeeds => false;

        //生活目标由扩展按床位和日程决定。
        public override void UpdateAllDuties()
        {
            foreach (var pawn in lord.ownedPawns)
                pawn.mindState.duty = new PawnDuty(DefDatabase<DutyDef>.GetNamed("Customer_LongStay"), pawn.Position);
        }
    }
}
