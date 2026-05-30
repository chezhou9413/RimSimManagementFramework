using SimManagementLib.Api;
using SimManagementLib.GameComp;
using SimManagementLib.Pojo;
using SimManagementLib.SimService;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
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
            Pawn pawn = lord?.ownedPawns?.FirstOrDefault(item => item != null && item.thingIDNumber == pawnId);
            Tool.SimDebugLogger.Journey("RSMF.ServiceOrder", $"添加服务订单 serviceOrder={order?.orderId ?? -1} service={order?.serviceDefName ?? "null"} state={order?.state.ToString() ?? "null"} price={order?.totalPrice ?? 0f}", pawn, pawn != null ? GetCurrentShop(pawn) : null, order?.orderId ?? -1);
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
        /// 返回顾客服务订单中尚未结清的金额，负责让先用后付服务也能进入收银台结账。
        /// </summary>
        public float GetPendingServiceOrderAmount(int pawnId)
        {
            List<CustomerServiceOrder> list = GetServiceOrders(pawnId);
            if (list.NullOrEmpty()) return 0f;

            float total = 0f;
            for (int i = 0; i < list.Count; i++)
            {
                CustomerServiceOrder order = list[i];
                if (order == null || order.totalPrice <= 0f) continue;
                if (order.state == ServiceOrderState.AwaitingPayment || order.state == ServiceOrderState.UsedAwaitingPayment)
                    total += order.totalPrice;
            }

            return total;
        }

        /// <summary>
        /// 返回顾客当前仍需支付的总金额，负责统一商品购物车和服务订单的付款判定。
        /// </summary>
        public float GetAmountOwedForCheckout(int pawnId)
        {
            float total = 0f;
            if (cartValues != null && cartValues.TryGetValue(pawnId, out float cartValue) && cartValue > 0f)
                total += cartValue;
            float pendingService = GetPendingServiceOrderAmount(pawnId);
            if (pendingService > total)
                total = pendingService;
            float pendingFinance = GetPendingFinanceBillAmount(pawnId);
            if (pendingFinance > total)
                total = pendingFinance;
            return total;
        }

        /// <summary>
        /// 返回顾客财务待结账账单金额，负责在购物车状态丢失时仍保留收银判定。
        /// </summary>
        private float GetPendingFinanceBillAmount(int pawnId)
        {
            if (pawnId <= 0) return 0f;
            Pawn pawn = lord?.ownedPawns?.FirstOrDefault(item => item != null && item.thingIDNumber == pawnId);
            if (pawn == null) return 0f;

            List<FinanceLineItem> lines = Current.Game?.GetComponent<GameComponent_ShopFinanceManager>()?.GetPendingBillLines(pawn);
            if (lines.NullOrEmpty()) return 0f;

            float total = 0f;
            for (int i = 0; i < lines.Count; i++)
            {
                FinanceLineItem line = lines[i];
                if (line == null || line.amount <= 0f) continue;
                total += line.amount;
            }

            return total;
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
                        Tool.SimDebugLogger.Journey("RSMF.ServiceOrder", $"先用后付服务付款完成 serviceOrder={order.orderId} service={order.serviceDefName}", pawn, shopZone, order.orderId);
                        serviceDef?.Worker.NotifyServicePaid(pawn, provider, order);
                        SimShopEvents.NotifyServiceOrderPaid(pawn, order, shopZone);
                        continue;
                    }

                    order.state = order.billingMode == ServiceBillingMode.TicketBeforeUse
                        ? ServiceOrderState.TicketIssued
                        : ServiceOrderState.ReadyToUse;
                    serviceDef?.Worker.NotifyServicePaid(pawn, provider, order);
                    SimShopEvents.NotifyServiceOrderPaid(pawn, order, shopZone);
                    Tool.SimDebugLogger.Journey("RSMF.ServiceOrder", $"服务订单付款完成 serviceOrder={order.orderId} newState={order.state} service={order.serviceDefName}", pawn, shopZone, order.orderId);

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
                    Tool.SimDebugLogger.Journey("RSMF.ServiceOrder", $"已使用待付款服务完成付款 serviceOrder={order.orderId} service={order.serviceDefName}", pawn, shopZone, order.orderId);
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
