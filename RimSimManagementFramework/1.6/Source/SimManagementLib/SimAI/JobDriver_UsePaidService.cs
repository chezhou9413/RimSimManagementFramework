using SimManagementLib.Api;
using SimManagementLib.Pojo;
using SimManagementLib.SimAI.CustomerVisit;
using SimManagementLib.SimService;
using SimManagementLib.Tool;
using SimManagementLib.SimZone;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SimManagementLib.SimAI
{
    /// <summary>
    /// 执行付款后服务的通用使用流程，负责消耗服务资格并通知服务 Worker 完成状态。
    /// </summary>
    public class JobDriver_UsePaidService : JobDriver
    {
        private Thing Provider => job.GetTarget(TargetIndex.A).Thing;
        private IntVec3 UseCell => job.GetTarget(TargetIndex.B).Cell;
        private CustomerServiceOrder order;
        private ShopServiceDef serviceDef;
        private int durationTicks = 300;
        private bool completedNormally;

        //预约服务建筑并提前恢复订单上下文，职责是保证 Job 在任意阶段中断时都能恢复服务资格。
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            Thing provider = Provider;
            ResolveOrderContext();
            if (provider == null || order == null || serviceDef == null) return false;
            return pawn.Reserve(provider, job, ShopServiceUtility.CustomerServiceProviderReservationSlots, 0, null, false);
        }

        //构建付款后服务流程，职责是完成移动、持续使用和订单结算。
        protected override IEnumerable<Toil> MakeNewToils()
        {
            completedNormally = false;
            AddFinishAction(HandleServiceJobFinished);
            this.FailOnDespawnedOrNull(TargetIndex.A);

            Toil init = new Toil();
            init.defaultCompleteMode = ToilCompleteMode.Instant;
            init.initAction = () =>
            {
                ResolveOrderContext();
                if (order == null || serviceDef == null)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                order.state = ServiceOrderState.InUse;
                order.startedTick = Find.TickManager.TicksGame;
                durationTicks = serviceDef.Worker.GetDurationTicks();
                serviceDef.Worker.NotifyServiceStarted(pawn, Provider, order);
            };
            yield return init;

            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);

            Toil use = new Toil();
            use.defaultCompleteMode = ToilCompleteMode.Never;
            use.initAction = () =>
            {
                ticksLeftThisToil = Mathf.Max(60, durationTicks);
                serviceDef?.Worker.NotifyServiceUseStarted(pawn, Provider, order);
            };
            use.tickAction = () =>
            {
                float progress = 1f - ticksLeftThisToil / (float)Mathf.Max(1, durationTicks);
                ShopProgressBarUtility.Report(pawn, progress);
                serviceDef?.Worker.TickServiceUse(pawn, Provider, order);
                ticksLeftThisToil--;
                if (ticksLeftThisToil <= 0)
                    ReadyForNextToil();
            };
            use.AddFinishAction(() => ShopProgressBarUtility.Clear(pawn));
            yield return use;

            Toil finalize = new Toil();
            finalize.defaultCompleteMode = ToilCompleteMode.Instant;
            finalize.initAction = () =>
            {
                if (order == null || serviceDef == null) return;
                order.completedTick = Find.TickManager.TicksGame;
                order.state = ServiceOrderState.Completed;
                serviceDef.Worker.NotifyServiceCompleted(pawn, Provider, order);
                LordJob_CustomerVisit lordJob = pawn.Map.lordManager.LordOf(pawn)?.LordJob as LordJob_CustomerVisit;
                Zone_Shop shopZone = lordJob?.GetCurrentShop(pawn);
                SimShopEvents.NotifyServiceOrderCompleted(pawn, order, shopZone);
                ShopBubbleUtility.ShowTextBubble(pawn, SimTranslation.T("RSMF.Bubble.ServiceCompleted", serviceDef.DisplayLabel.Named("service")), new Color(0.55f, 0.85f, 1f));

                if (order.totalPrice <= 0f)
                    lordJob?.TryEnqueueFreeCompletedServiceReview(pawn, shopZone, "完成免费服务");
                lordJob?.GetOrCreateSession(pawn)?.NotifyCheckoutPaid(lordJob, pawn, "购后服务完成");
                lordJob?.CheckAllCheckoutsDone();
                completedNormally = true;
            };
            yield return finalize;
        }

        //恢复当前 Job 对应的服务订单和 Def，职责是统一预约、执行和中断处理使用的上下文。
        private void ResolveOrderContext()
        {
            if (order != null && serviceDef != null) return;
            LordJob_CustomerVisit lordJob = pawn?.Map?.lordManager?.LordOf(pawn)?.LordJob as LordJob_CustomerVisit;
            order = lordJob?.GetServiceOrder(pawn.thingIDNumber, job?.count ?? -1);
            serviceDef = DefDatabase<ShopServiceDef>.GetNamedSilentFail(order?.serviceDefName);
        }

        //处理服务 Job 的异常结束，职责是把已付款但未完成的服务重新放回购后执行队列。
        private void HandleServiceJobFinished(JobCondition condition)
        {
            ShopProgressBarUtility.Clear(pawn);
            if (completedNormally || order == null || order.state != ServiceOrderState.InUse) return;

            LordJob_CustomerVisit lordJob = pawn?.Map?.lordManager?.LordOf(pawn)?.LordJob as LordJob_CustomerVisit;
            order.state = order.billingMode == ServiceBillingMode.TicketBeforeUse
                ? ServiceOrderState.TicketIssued
                : ServiceOrderState.ReadyToUse;
            Job retryJob = lordJob == null ? null : ShopServiceUtility.MakeServiceUseJob(pawn, order);
            if (retryJob != null)
            {
                lordJob.QueuePostCheckoutJobs(pawn.thingIDNumber, new[] { retryJob });
                SimDebugLogger.Journey("RSMF.ServiceOrder", $"服务被中断并重新排队 condition={condition} serviceOrder={order.orderId}", pawn, lordJob.GetCurrentShop(pawn), order.orderId);
                return;
            }

            order.state = ServiceOrderState.Canceled;
            serviceDef?.Worker.NotifyServiceCanceled(pawn, Provider, order);
        }
    }
}
