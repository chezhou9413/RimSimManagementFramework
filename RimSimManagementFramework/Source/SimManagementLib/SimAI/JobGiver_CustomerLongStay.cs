using SimManagementLib.Api;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimAI
{
    //取得住客生活任务，扩展可以同步安排新的标准商店访问。
    public sealed class JobGiver_CustomerLongStay : ThinkNode_JobGiver
    {
        //生活调度只返回当前任务，切店仍通过统一顾客接口完成。
        protected override Job TryGiveJob(Pawn pawn) => SimShopCustomerApi.MakeLongStayJob(pawn);
    }
}
