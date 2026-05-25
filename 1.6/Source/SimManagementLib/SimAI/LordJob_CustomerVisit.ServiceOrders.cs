using SimManagementLib.Api;
using SimManagementLib.Pojo;
using SimManagementLib.SimService;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimAI
{
    public partial class LordJob_CustomerVisit
    {
        /// <summary>
        /// 为顾客添加一条服务订单，并按订单编号保持可追踪性。
        /// </summary>
        public void AddServiceOrder(int pawnId, CustomerServiceOrder order)
        {
            serviceOrderState.AddServiceOrder(pawnId, order);
        }

        /// <summary>
        /// 返回指定顾客的服务订单列表。
        /// </summary>
        public List<CustomerServiceOrder> GetServiceOrders(int pawnId)
        {
            return serviceOrderState.GetServiceOrders(pawnId);
        }

        /// <summary>
        /// 按订单编号查找指定顾客的服务订单。
        /// </summary>
        public CustomerServiceOrder GetServiceOrder(int pawnId, int orderId)
        {
            return serviceOrderState.GetServiceOrder(pawnId, orderId);
        }

        /// <summary>
        /// 统计指定服务建筑和服务 Def 当前正在占用并发名额的订单数量。
        /// </summary>
        public int CountActiveServiceOrders(int providerThingId, string serviceDefName)
        {
            return serviceOrderState.CountActiveServiceOrders(providerThingId, serviceDefName);
        }

        /// <summary>
        /// 清理顾客未完成的服务订单，已使用未付款服务会标记为结账失败。
        /// </summary>
        public void ResolveServiceOrdersOnCheckoutFailure(int pawnId)
        {
            List<CustomerServiceOrder> list = GetServiceOrders(pawnId);
            if (list.NullOrEmpty()) return;

            for (int i = 0; i < list.Count; i++)
            {
                CustomerServiceOrder order = list[i];
                if (order == null) continue;
                if (order.HasBeenUsed)
                {
                    if (order.state != ServiceOrderState.Completed)
                        order.state = ServiceOrderState.CheckoutFailed;
                }
                else
                {
                    order.state = ServiceOrderState.Canceled;
                }
            }
        }

        /// <summary>
        /// 付款完成后更新服务订单状态，并把需要付款后执行的服务 Job 加入购后队列。
        /// </summary>
        public void ResolveServiceOrdersOnCheckoutPaid(Pawn pawn, Zone_Shop shopZone)
        {
            if (pawn == null) return;
            int pawnId = pawn.thingIDNumber;
            List<CustomerServiceOrder> list = GetServiceOrders(pawnId);
            if (list.NullOrEmpty()) return;

            List<Job> followUps = new List<Job>();
            for (int i = 0; i < list.Count; i++)
            {
                CustomerServiceOrder order = list[i];
                if (order == null) continue;

                Thing provider = ShopServiceUtility.FindProviderByOrder(pawn.Map, order);
                ShopServiceDef serviceDef = DefDatabase<ShopServiceDef>.GetNamedSilentFail(order.serviceDefName);

                if (order.state == ServiceOrderState.AwaitingPayment)
                {
                    order.paidTick = Find.TickManager.TicksGame;
                    if (order.billingMode == ServiceBillingMode.UseBeforePay)
                    {
                        order.state = ServiceOrderState.Completed;
                        serviceDef?.Worker.NotifyServicePaid(pawn, provider, order);
                        SimShopEvents.NotifyServiceOrderPaid(pawn, order, shopZone);
                        continue;
                    }

                    order.state = order.billingMode == ServiceBillingMode.TicketBeforeUse
                        ? ServiceOrderState.TicketIssued
                        : ServiceOrderState.ReadyToUse;
                    serviceDef?.Worker.NotifyServicePaid(pawn, provider, order);
                    SimShopEvents.NotifyServiceOrderPaid(pawn, order, shopZone);

                    if (serviceDef != null && SimShopOrderApi.HasPreparedOrderWorker(serviceDef.defName))
                    {
                        PreparedShopOrderResult prepared = SimShopOrderApi.CreatePreparedOrder(pawn, provider, shopZone, serviceDef, order.totalPrice);
                        if (prepared.success)
                        {
                            prepared.order.sourceServiceOrderId = order.orderId;
                            SimShopOrderApi.MarkPaid(prepared.order.orderId);
                            order.state = ServiceOrderState.Completed;
                            continue;
                        }
                    }

                    Job job = ShopServiceUtility.MakeServiceUseJob(pawn, order);
                    if (job != null) followUps.Add(job);
                }
                else if (order.state == ServiceOrderState.UsedAwaitingPayment)
                {
                    order.paidTick = Find.TickManager.TicksGame;
                    order.state = ServiceOrderState.Completed;
                    serviceDef?.Worker.NotifyServicePaid(pawn, provider, order);
                    SimShopEvents.NotifyServiceOrderPaid(pawn, order, shopZone);
                }
            }

            QueuePostCheckoutJobs(pawnId, followUps);
        }

        /// <summary>
        /// 清除顾客的服务订单，用于购后服务全部完成或无需继续保留订单时释放运行状态。
        /// </summary>
        public void ClearCustomerServiceOrders(int pawnId)
        {
            serviceOrderState.ClearCustomerServiceOrders(pawnId);
        }
    }
}
