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
    /// <summary>
    /// 执行顾客选择服务、预付入账或先用后付服务使用的流程。
    /// </summary>
    public class JobDriver_SelectPaidService : JobDriver
    {
        private const int MaxProviderReservations = 24;
        private Thing Provider => job.GetTarget(TargetIndex.A).Thing;

        private ShopServiceDef selectedService;
        private float selectedPrice;
        private CustomerServiceOrder activeOrder;
        private int serviceDurationTicks = 120;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            Thing provider = Provider;
            if (provider == null) return false;
            return pawn.Reserve(provider, job, MaxProviderReservations, 0, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
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

                activeOrder = ShopServiceUtility.CreateOrder(lordJob.nextServiceOrderId++, Provider, selectedService, selectedPrice);
                if (!selectedService.Worker.TryReserve(pawn, Provider, activeOrder))
                {
                    RegisterNoProgressAndCheckoutIfNeeded(lordJob);
                    SimDebugLogger.Journey("RSMF.SelectService", $"服务预约失败 service={selectedService.defName} provider={Provider?.thingIDNumber ?? -1}", pawn, shopZone, activeOrder.orderId);
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                lordJob.GetOrCreateSession(pawn)?.NotifyBrowseStarted(lordJob, pawn);
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
                LordJob_CustomerVisit lordJob = pawn.Map.lordManager.LordOf(pawn)?.LordJob as LordJob_CustomerVisit;
                if (lordJob == null || activeOrder == null || selectedService == null) return;

                Zone_Shop shopZone = lordJob.GetCurrentShop(pawn);
                GameComponent_ShopFinanceManager finance = Current.Game?.GetComponent<GameComponent_ShopFinanceManager>();
                int pawnId = pawn.thingIDNumber;

                lordJob.EnsureCustomerBill(pawnId);

                if (selectedService.billingMode == ServiceBillingMode.UseBeforePay)
                {
                    activeOrder.state = ServiceOrderState.UsedAwaitingPayment;
                    activeOrder.completedTick = Find.TickManager.TicksGame;
                    SimDebugLogger.Journey("RSMF.SelectService", $"先用后付服务完成，等待付款 service={selectedService.defName}", pawn, shopZone, activeOrder.orderId);
                    selectedService.Worker.NotifyServiceCompleted(pawn, Provider, activeOrder);
                }
                else
                {
                    activeOrder.state = ServiceOrderState.AwaitingPayment;
                    SimDebugLogger.Journey("RSMF.SelectService", $"服务票据生成，等待付款 service={selectedService.defName}", pawn, shopZone, activeOrder.orderId);
                }

                lordJob.AddServiceOrder(pawnId, activeOrder);
                lordJob.ClearCurrentShopNoProgressBrowse(pawn);
                SimShopEvents.NotifyServiceOrderCreated(pawn, activeOrder, shopZone);
                lordJob.AddCustomerBill(pawnId, activeOrder.totalPrice);
                finance?.QueueServiceSale(pawn, shopZone, activeOrder.serviceDefName, selectedService.DisplayLabel, activeOrder.count, activeOrder.totalPrice);
                ShopBubbleUtility.ShowTextBubble(pawn, SimTranslation.T("RSMF.Bubble.SelectService", selectedService.DisplayLabel.Named("service")), new Color(0.55f, 0.85f, 1f));

                CustomerVisitSession session = lordJob.GetOrCreateSession(pawn);
                session?.NotifyConsumptionCompleted(lordJob, pawn, "服务选择完成");
                if (selectedService.checkoutAfterSelection)
                    session?.MarkReadyForCheckout(lordJob, pawn, "服务要求选择后结账");
            };
            yield return finalize;
        }

        /// <summary>
        /// 从目标建筑上重新选择一项当前仍可用且预算足够的服务。
        /// </summary>
        private bool TryPickService(Zone_Shop shopZone, float remainingBudget, out ShopServiceDef serviceDef, out float price)
        {
            serviceDef = null;
            price = 0f;
            ThingComp_ServiceProvider comp = ShopServiceUtility.GetProviderComp(Provider);
            if (comp == null || !comp.enabled || remainingBudget <= 0f) return false;

            List<ShopServiceDef> candidates = comp.EnabledSlots
                .Select(s => s.ServiceDef)
                .Where(d => d != null)
                .Where(d => d.Worker.CanUse(pawn, Provider, shopZone, out _))
                .Where(d => d.Worker.GetPrice(pawn, Provider, shopZone) <= remainingBudget)
                .Where(d => ShopServiceUtility.CanAcceptMoreUsers(Provider, d))
                .ToList();

            if (candidates.NullOrEmpty()) return false;
            serviceDef = candidates.RandomElement();
            price = serviceDef.Worker.GetPrice(pawn, Provider, shopZone);
            return true;
        }

        /// <summary>
        /// 记录一次无进展服务选择，负责在服务反复不可用时让顾客结束浏览。
        /// </summary>
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
