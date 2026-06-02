using SimManagementLib.Api;
using SimManagementLib.SimAI.CustomerVisit;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SimManagementLib.SimAI
{
    /// <summary>
    /// 为顾客分配外部注册的浏览阶段动作，负责在没有可用动作时让出给原购物和服务流程。
    /// </summary>
    public class JobGiver_Customer_RunExternalAction : ThinkNode_JobGiver
    {
        /// <summary>
        /// 尝试创建外部顾客动作 Job。
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            LordJob_CustomerVisit lordJob = pawn?.Map?.lordManager?.LordOf(pawn)?.LordJob as LordJob_CustomerVisit;
            if (lordJob == null) return null;
            CustomerVisitSession session = lordJob.GetOrCreateSession(pawn);
            if (session == null || !session.AllowsJobGiver(CustomerVisitStage.Browsing))
                return null;

            int pawnId = pawn.thingIDNumber;
            if (lordJob.IsPawnReadyForCheckout(pawnId))
                return null;

            if (lordJob.HasReachedConsumptionLimit(pawnId))
            {
                lordJob.TryMarkReadyForCheckoutAfterMinimumBrowse(pawn);
                return null;
            }

            Zone_Shop shopZone = lordJob.GetCurrentShop(pawn);
            if (shopZone == null) return null;

            CustomerActionContext context = SimShopCustomerApi.BuildActionContext(pawn, lordJob, shopZone);
            if (context == null || context.remainingBudget <= 0f)
            {
                lordJob.TryMarkReadyForCheckoutAfterMinimumBrowse(pawn);
                return null;
            }

            return SimShopCustomerApi.TryMakeCustomerActionJob(context, out Job job) ? job : null;
        }
    }
}
