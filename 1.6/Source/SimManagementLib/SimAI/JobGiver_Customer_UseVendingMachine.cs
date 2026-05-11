using SimManagementLib.SimThingClass;
using SimManagementLib.Tool;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SimManagementLib.SimAI
{
    /// <summary>
    /// 为自动售货机顾客分配直接在目标机器购买并结账的工作。
    /// </summary>
    public class JobGiver_Customer_UseVendingMachine : ThinkNode_JobGiver
    {
        /// <summary>
        /// 根据顾客 Lord 中记录的机器 ID 创建自动售货机使用 Job。
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            LordJob_VendingMachineVisit lordJob = pawn.Map.lordManager.LordOf(pawn)?.LordJob as LordJob_VendingMachineVisit;
            if (lordJob == null) return null;

            Building_SimContainer machine = VendingMachineUtility.FindVendingMachineById(pawn.Map, lordJob.vendingMachineThingId);
            if (machine == null || !VendingMachineUtility.IsUsableVendingMachine(machine)) 
            {
                lordJob.NotifyDone();
                return null;
            }

            if (!pawn.CanReach(machine, PathEndMode.Touch, Danger.Deadly))
            {
                lordJob.NotifyDone();
                return null;
            }

            return JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Customer_UseVendingMachine"), machine);
        }
    }
}
