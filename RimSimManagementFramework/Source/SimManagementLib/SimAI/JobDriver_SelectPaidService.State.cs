using SimManagementLib.Pojo;
using SimManagementLib.SimService;
using SimManagementLib.Tool;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SimManagementLib.SimAI
{
    //服务选择持久状态，职责是绑定 Lord 内订单并处理读档和中断。
    public partial class JobDriver_SelectPaidService
    {
        private int activeOrderId = -1;
        private bool selectionCommitted;
        private bool serviceUseStarted;
        private LordJob_CustomerVisit Visit => pawn?.GetLord()?.LordJob as LordJob_CustomerVisit;

        //提供当前订单执行状态，已取消、已记账或失去建筑的订单不能继续保护顾客停留。
        internal bool HasActiveService => !selectionCommitted && Provider != null && Provider.Spawned
            && (activeOrder?.state == ServiceOrderState.Draft || activeOrder?.state == ServiceOrderState.InUse);
        internal bool IsUsingService => HasActiveService && serviceUseStarted;

        //保存订单标识和读条上下文，实际订单统一由 Lord 保存，剩余工时由原版 JobDriver 保存。
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref activeOrderId, "activeOrderId", -1);
            Scribe_Defs.Look(ref selectedService, "selectedService");
            Scribe_Values.Look(ref selectedPrice, "selectedPrice");
            Scribe_Values.Look(ref serviceDurationTicks, "serviceDurationTicks", 120);
            Scribe_Values.Look(ref selectionCommitted, "selectionCommitted");
            Scribe_Values.Look(ref serviceUseStarted, "serviceUseStarted");
        }

        //取消未提交的服务并释放名额，职责是防止中断留下永久占用或未完成账单。
        private void HandleSelectionFinished(JobCondition condition)
        {
            ShopProgressBarUtility.Clear(pawn);
            if (selectionCommitted) return;
            CustomerServiceOrder order = activeOrder;
            if (order == null || (order.state != ServiceOrderState.Draft && order.state != ServiceOrderState.InUse)) return;
            order.state = ServiceOrderState.Canceled;
            serviceUseStarted = false;
            selectedService?.Worker.NotifyServiceCanceled(pawn, Provider, order);
            RegisterNoProgressAndCheckoutIfNeeded(Visit);
            SimDebugLogger.Journey("RSMF.SelectService", "服务未完成即中断：" + condition, pawn, Visit?.GetCurrentShop(pawn), order.orderId);
        }
    }
}
