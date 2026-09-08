using SimManagementLib.GameComp;
using SimManagementLib.Api;
using SimManagementLib.Pojo;
using SimManagementLib.SimAI.CustomerVisit;
using SimManagementLib.SimService;
using SimManagementLib.SimThingComp;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SimManagementLib.SimAI
{
    //执行顾客选择服务、预付入账或先用后付服务使用的流程。
    public partial class JobDriver_SelectPaidService : JobDriver
    {
        private Thing Provider => job.GetTarget(TargetIndex.A).Thing;

        private ShopServiceDef selectedService;
        private float selectedPrice;
        private CustomerServiceOrder activeOrder => Visit?.GetServiceOrder(pawn.thingIDNumber, activeOrderId);
        private int serviceDurationTicks = 120;

        //预约共享服务建筑，职责是允许服务并发同时避开独占工程预约。
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            Thing provider = Provider;
            if (provider == null) return false;
            return pawn.Reserve(provider, job, ShopServiceUtility.CustomerServiceProviderReservationSlots, 0, null, false);
        }

        //构建服务选择和使用流程，职责是完成预约、移动、读条和统一记账。
        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddFinishAction(HandleSelectionFinished);
            this.FailOnDespawnedOrNull(TargetIndex.A);

            Toil init = new Toil();
            init.defaultCompleteMode = ToilCompleteMode.Instant;
            init.initAction = () =>
            {
                LordJob_CustomerVisit lordJob = pawn.Map.lordManager.LordOf(pawn)?.LordJob as LordJob_CustomerVisit;
                Zone_Shop shopZone = lordJob?.GetCurrentShop(pawn);
                if (lordJob == null || shopZone == null)
                {
                    SimDebugLogger.Journey("RSMF.SelectService", "选择服务失败：没有 LordJob 或当前店铺", pawn);
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                int pawnId = pawn.thingIDNumber;
                float remainingBudget = lordJob.GetRemainingTripBudget(pawn, shopZone);
                if (!TryPickService(shopZone, remainingBudget, out selectedService, out selectedPrice))
                {
                    RegisterNoProgressAndCheckoutIfNeeded(lordJob);
                    SimDebugLogger.Journey("RSMF.SelectService", $"选择服务失败：无可用服务 remainingBudget={remainingBudget}", pawn, shopZone, -1);
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                CustomerServiceOrder newOrder = ShopServiceUtility.CreateOrder(lordJob.nextServiceOrderId++, Provider, selectedService, selectedPrice);
                if (!selectedService.Worker.TryReserve(pawn, Provider, newOrder))
                {
                    RegisterNoProgressAndCheckoutIfNeeded(lordJob);
                    SimDebugLogger.Journey("RSMF.SelectService", $"服务预约失败 service={selectedService.defName} provider={Provider?.thingIDNumber ?? -1}", pawn, shopZone, newOrder.orderId);
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                activeOrderId = newOrder.orderId;
                lordJob.AddServiceOrder(pawnId, newOrder);
                SimDebugLogger.Journey("RSMF.SelectService", $"选择服务成功 service={selectedService.defName} price={selectedPrice} provider={Provider?.thingIDNumber ?? -1}", pawn, shopZone, activeOrder.orderId);
            };
            yield return init;

            Toil selectOrUse = new Toil();
            selectOrUse.defaultCompleteMode = ToilCompleteMode.Delay;
            selectOrUse.defaultDuration = 120;
            selectOrUse.initAction = () =>
            {
                if (selectedService == null || activeOrder == null)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                if (selectedService.billingMode != ServiceBillingMode.UseBeforePay)
                {
                    ticksLeftThisToil = 1;
                    return;
                }

                serviceUseStarted = true;
                serviceDurationTicks = selectedService.Worker.GetDurationTicks();
                ticksLeftThisToil = serviceDurationTicks;
                activeOrder.state = ServiceOrderState.InUse;
                activeOrder.startedTick = Find.TickManager.TicksGame;
                SimDebugLogger.Journey("RSMF.SelectService", $"服务开始使用 service={selectedService.defName} duration={serviceDurationTicks}", pawn, pawn.Map.lordManager.LordOf(pawn)?.LordJob is LordJob_CustomerVisit visit ? visit.GetCurrentShop(pawn) : null, activeOrder.orderId);
                selectedService.Worker.NotifyServiceStarted(pawn, Provider, activeOrder);
            };
            selectOrUse.tickAction = () =>
            {
                if (selectedService?.billingMode != ServiceBillingMode.UseBeforePay)
                {
                    ticksLeftThisToil = 0;
                    return;
                }

                float progress = 1f - ticksLeftThisToil / (float)Mathf.Max(1, serviceDurationTicks);
                ShopProgressBarUtility.Report(pawn, progress);
                selectedService?.Worker.TickServiceUse(pawn, Provider, activeOrder);
            };
            selectOrUse.AddFinishAction(() => ShopProgressBarUtility.Clear(pawn));

            yield return Toils_Jump.JumpIf(selectOrUse, () => selectedService?.billingMode != ServiceBillingMode.UseBeforePay);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return selectOrUse;

            Toil finalize = new Toil();
            finalize.defaultCompleteMode = ToilCompleteMode.Instant;
            finalize.initAction = () =>
            {
                CommitSelectedService();
            };
            yield return finalize;
        }

        //从目标建筑上重新选择一项当前仍可用且预算足够的服务。
        private bool TryPickService(Zone_Shop shopZone, float remainingBudget, out ShopServiceDef serviceDef, out float price)
        {
            return ShopServiceUtility.TryFindServiceAtProvider(pawn, shopZone, Provider, remainingBudget, out serviceDef, out price);
        }

        //记录一次无进展服务选择，负责在服务反复不可用时让顾客结束浏览。
        private void RegisterNoProgressAndCheckoutIfNeeded(LordJob_CustomerVisit lordJob)
        {
            if (lordJob == null || pawn == null) return;
            lordJob.RegisterCurrentShopBrowseAttempt(pawn);
            lordJob.RegisterCurrentShopNoProgressBrowse(pawn);
            if (!lordJob.HasCompletedCurrentShopMinimumBrowse(pawn)) return;
            if (!lordJob.HasReachedCurrentShopBrowseLimit(pawn) && !lordJob.HasReachedCurrentShopNoProgressLimit(pawn)) return;

            int pawnId = pawn.thingIDNumber;
            lordJob.EnsureCustomerBill(pawnId);
            lordJob.MarkPawnReadyForCheckout(pawnId);
        }
    }
}
