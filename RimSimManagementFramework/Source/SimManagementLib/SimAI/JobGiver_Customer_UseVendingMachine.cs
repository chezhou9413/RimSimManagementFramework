using SimManagementLib.SimThingClass;
using SimManagementLib.SimMapComp;
using SimManagementLib.Tool;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SimManagementLib.SimAI
{
    //类职责：在地图行为预算内为自动售货机顾客生成机器使用 Job。
    public class JobGiver_Customer_UseVendingMachine : ThinkNode_JobGiver
    {
        //根据顾客 Lord 中记录的机器 ID 创建使用 Job，职责是把真实路径交给 Job 路径器。
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (pawn?.Map?.GetComponent<CustomerArrivalManager>()?.TryConsumeBehaviorBudget(pawn) != true)
                return null;
            LordJob_VendingMachineVisit lordJob = pawn.Map.lordManager.LordOf(pawn)?.LordJob as LordJob_VendingMachineVisit;
            if (lordJob == null) return null;

            Building_SimContainer machine = VendingMachineUtility.FindVendingMachineById(pawn.Map, lordJob.vendingMachineThingId);
            if (machine == null || !VendingMachineUtility.IsUsableVendingMachine(machine)) 
            {
                lordJob.NotifyDone();
                return null;
            }

            return JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Customer_UseVendingMachine"), machine);
        }
    }
}
