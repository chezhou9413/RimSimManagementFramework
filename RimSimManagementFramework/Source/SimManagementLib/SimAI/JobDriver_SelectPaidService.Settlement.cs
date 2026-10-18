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
    //服务选择结算部分，职责是只在实际服务完成后追加一次账单和消费记录。
    public partial class JobDriver_SelectPaidService
    {
        //提交当前服务结果，职责是统一预付资格、先用后付账单和完成通知。
        private void CommitSelectedService()
        {
            LordJob_CustomerVisit lordJob = pawn.Map.lordManager.LordOf(pawn)?.LordJob as LordJob_CustomerVisit;
            if (selectionCommitted || lordJob == null || activeOrder == null || selectedService == null) return;
            selectionCommitted = true;
            serviceUseStarted = false;

            Zone_Shop shopZone = lordJob.GetCurrentShop(pawn);
            GameComponent_ShopFinanceManager finance = Current.Game?.GetComponent<GameComponent_ShopFinanceManager>();
            int pawnId = pawn.thingIDNumber;

            lordJob.EnsureCustomerBill(pawnId);

            if (selectedService.billingMode == ServiceBillingMode.UseBeforePay)
            {
                if (activeOrder.totalPrice <= 0f)
                {
                    activeOrder.state = ServiceOrderState.Completed;
                    activeOrder.paidTick = Find.TickManager.TicksGame;
                    SimDebugLogger.Journey("RSMF.SelectService", $"免费先用后付服务完成 service={selectedService.defName}", pawn, shopZone, activeOrder.orderId);
                    selectedService.Worker.NotifyServicePaid(pawn, Provider, activeOrder);
                    SimShopEvents.NotifyServiceOrderPaid(pawn, activeOrder, shopZone);
                }
                else
                {
                    activeOrder.state = ServiceOrderState.UsedAwaitingPayment;
                }
                activeOrder.completedTick = Find.TickManager.TicksGame;
                SimDebugLogger.Journey("RSMF.SelectService", $"先用后付服务完成，等待付款 service={selectedService.defName}", pawn, shopZone, activeOrder.orderId);
                selectedService.Worker.NotifyServiceCompleted(pawn, Provider, activeOrder);
            }
            else
            {
                if (activeOrder.totalPrice <= 0f)
                {
                    activeOrder.paidTick = Find.TickManager.TicksGame;
                    activeOrder.state = selectedService.billingMode == ServiceBillingMode.TicketBeforeUse
                        ? ServiceOrderState.TicketIssued
                        : ServiceOrderState.ReadyToUse;
                    SimDebugLogger.Journey("RSMF.SelectService", $"免费服务资格生成 service={selectedService.defName} state={activeOrder.state}", pawn, shopZone, activeOrder.orderId);
                }
                else
                {
                    activeOrder.state = ServiceOrderState.AwaitingPayment;
                    SimDebugLogger.Journey("RSMF.SelectService", $"服务票据生成，等待付款 service={selectedService.defName}", pawn, shopZone, activeOrder.orderId);
                }
            }

            lordJob.GetOrCreateSession(pawn)?.NotifyBrowseStarted(lordJob, pawn);
            lordJob.ClearCurrentShopNoProgressBrowse(pawn);
            SimShopEvents.NotifyServiceOrderCreated(pawn, activeOrder, shopZone);
            if (activeOrder.totalPrice > 0f)
            {
                lordJob.AddCustomerBill(pawnId, activeOrder.totalPrice);
                finance?.QueueServiceSale(pawn, shopZone, activeOrder.serviceDefName, selectedService.DisplayLabel, activeOrder.count, activeOrder.totalPrice);
            }
            ShopBubbleUtility.ShowTextBubble(pawn, SimTranslation.T("RSMF.Bubble.SelectService", selectedService.DisplayLabel.Named("service")), new Color(0.55f, 0.85f, 1f));

            CustomerVisitSession session = lordJob.GetOrCreateSession(pawn);
            session?.NotifyConsumptionCompleted(lordJob, pawn, "服务选择完成");
            if (activeOrder.totalPrice <= 0f && selectedService.billingMode != ServiceBillingMode.UseBeforePay)
            {
                lordJob.ResolveServiceOrdersOnCheckoutPaid(pawn, shopZone);
                lordJob.TryEnqueueFreeCompletedServiceReview(pawn, shopZone, "完成免费服务");
            }
            if (activeOrder.totalPrice <= 0f && selectedService.billingMode == ServiceBillingMode.UseBeforePay)
                lordJob.TryEnqueueFreeCompletedServiceReview(pawn, shopZone, "完成免费服务");
            if (selectedService.checkoutAfterSelection)
                session?.MarkReadyForCheckout(lordJob, pawn, "服务要求选择后结账");
        }
    }
}
