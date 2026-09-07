using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SimManagementLib.SimAI
{
    /// <summary>
    /// 为自动售货机顾客分配使用机器的职责。
    /// </summary>
    public class LordToil_VendingMachineUse : LordToil
    {
        private readonly IntVec3 vendingCell;

        public LordToil_VendingMachineUse(IntVec3 vendingCell)
        {
            this.vendingCell = vendingCell;
        }

        public override bool AssignsDuties => true;

        /// <summary>
        /// 给所有自动售货机顾客设置前往机器购买的 Duty。
        /// </summary>
        public override void UpdateAllDuties()
        {
            foreach (Pawn pawn in lord.ownedPawns)
            {
                pawn.mindState.duty = new PawnDuty(DefDatabase<DutyDef>.GetNamed("Customer_UseVendingMachine"))
                {
                    focus = vendingCell,
                    locomotion = LocomotionUrgency.Amble
                };
            }
        }
    }
}
