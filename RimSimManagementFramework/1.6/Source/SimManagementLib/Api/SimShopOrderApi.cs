using RimWorld;
using SimManagementLib.GameComp;
using SimManagementLib.SimDef;
using SimManagementLib.SimService;
using SimManagementLib.SimZone;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace SimManagementLib.Api
{
    /// <summary>
    /// 提供现做订单的稳定公开入口，负责 Worker 注册、订单创建、查询和状态推进。
    /// </summary>
    public static class SimShopOrderApi
    {
        private static readonly Dictionary<string, PreparedShopOrderWorker> Workers = new Dictionary<string, PreparedShopOrderWorker>();
        private static readonly Dictionary<string, PreparedShopOrderWorker> DefWorkers = new Dictionary<string, PreparedShopOrderWorker>();
        private static bool defsLoaded;

        /// <summary>
        /// 注册指定服务的现做订单 Worker。
        /// </summary>
        public static bool RegisterPreparedOrderWorker(string serviceDefName, PreparedShopOrderWorker worker, bool replace = false)
        {
            if (string.IsNullOrEmpty(serviceDefName) || worker == null) return false;
            if (Workers.ContainsKey(serviceDefName) && !replace) return false;

            worker.serviceDef = DefDatabase<ShopServiceDef>.GetNamedSilentFail(serviceDefName);
            Workers[serviceDefName] = worker;
            return true;
        }

        /// <summary>
        /// 返回指定服务是否已经注册现做订单 Worker。
        /// </summary>
        public static bool HasPreparedOrderWorker(string serviceDefName)
        {
            return GetPreparedOrderWorker(serviceDefName) != null;
        }

        /// <summary>
        /// 返回指定服务的现做订单 Worker。
        /// </summary>
        public static PreparedShopOrderWorker GetPreparedOrderWorker(string serviceDefName)
        {
            if (string.IsNullOrEmpty(serviceDefName)) return null;
            EnsureDefWorkersLoaded();
            Workers.TryGetValue(serviceDefName, out PreparedShopOrderWorker worker);
            if (worker != null) return worker;
            DefWorkers.TryGetValue(serviceDefName, out worker);
            return worker;
        }

        /// <summary>
        /// 从 DefDatabase 加载现做订单 Worker 绑定，负责让外部模组通过 XML 绑定服务逻辑。
        /// </summary>
        private static void EnsureDefWorkersLoaded()
        {
            if (defsLoaded) return;
            defsLoaded = true;
            DefWorkers.Clear();
            foreach (PreparedShopOrderWorkerDef def in DefDatabase<PreparedShopOrderWorkerDef>.AllDefsListForReading)
            {
                if (def?.serviceDef == null || def.Worker == null) continue;
                string serviceName = def.serviceDef.defName;
                if (string.IsNullOrEmpty(serviceName)) continue;
                if (DefWorkers.ContainsKey(serviceName) && !def.replaceRuntimeRegistration) continue;
                def.Worker.serviceDef = def.serviceDef;
                DefWorkers[serviceName] = def.Worker;
            }
        }

        /// <summary>
        /// 创建并保存现做订单。
        /// </summary>
        public static PreparedShopOrderResult CreatePreparedOrder(Pawn customer, Thing provider, Zone_Shop shop, ShopServiceDef serviceDef, float totalPrice)
        {
            if (serviceDef == null) return PreparedShopOrderResult.Fail("服务定义无效");
            PreparedShopOrderWorker worker = GetPreparedOrderWorker(serviceDef.defName);
            if (worker == null) return PreparedShopOrderResult.Fail("服务未注册现做订单 Worker");
            worker.serviceDef = serviceDef;
            if (!worker.CanCreateOrder(customer, provider, shop, out string reason))
                return PreparedShopOrderResult.Fail(reason);

            GameComponent_PreparedShopOrderManager manager = SimShopApi.OrderManager;
            if (manager == null) return PreparedShopOrderResult.Fail("现做订单管理器不可用");

            PreparedShopOrder order = worker.CreateOrder(customer, provider, shop, totalPrice);
            if (order == null) return PreparedShopOrderResult.Fail("Worker 未创建订单");
            order.serviceDefName = serviceDef.defName;
            if (order.createdTick <= 0) order.createdTick = Find.TickManager?.TicksGame ?? 0;
            manager.AddOrder(order);
            SimShopEvents.NotifyPreparedOrderCreated(order);
            return PreparedShopOrderResult.Success(order);
        }

        /// <summary>
        /// 按编号查找现做订单。
        /// </summary>
        public static PreparedShopOrder GetOrder(int orderId)
        {
            return SimShopApi.OrderManager?.GetOrder(orderId);
        }

        /// <summary>
        /// 按条件查询现做订单。
        /// </summary>
        public static List<PreparedShopOrder> QueryOrders(PreparedShopOrderQuery query)
        {
            return SimShopApi.OrderManager?.QueryOrders(query) ?? new List<PreparedShopOrder>();
        }

        /// <summary>
        /// 查找地图上订单引用的服务建筑。
        /// </summary>
        public static Thing FindOrderProvider(Map map, PreparedShopOrder order)
        {
            if (map == null || order == null || order.providerThingId < 0) return null;
            IReadOnlyList<Thing> things = map.listerThings.AllThings;
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (thing != null && thing.thingIDNumber == order.providerThingId)
                    return thing;
            }
            return null;
        }

        /// <summary>
        /// 查找地图上订单引用的顾客。
        /// </summary>
        public static Pawn FindOrderCustomer(Map map, PreparedShopOrder order)
        {
            if (map?.mapPawns == null || order == null || order.customerThingId < 0) return null;
            return map.mapPawns.AllPawnsSpawned.FirstOrDefault(p => p != null && p.thingIDNumber == order.customerThingId);
        }

        /// <summary>
        /// 标记订单已付款并等待制作。
        /// </summary>
        public static PreparedShopOrderResult MarkPaid(int orderId)
        {
            PreparedShopOrder order = GetOrder(orderId);
            if (order == null) return PreparedShopOrderResult.Fail("订单不存在");
            if (order.state != PreparedShopOrderState.Draft && order.state != PreparedShopOrderState.AwaitingPayment)
                return PreparedShopOrderResult.Fail("订单状态不允许付款");

            order.state = PreparedShopOrderState.PaidWaitingPreparation;
            order.paidTick = Find.TickManager?.TicksGame ?? 0;
            return PreparedShopOrderResult.Success(order);
        }

        /// <summary>
        /// 尝试让员工认领现做订单。
        /// </summary>
        public static PreparedShopOrderResult TryAssignOrder(Pawn staff, PreparedShopOrder order)
        {
            if (staff == null) return PreparedShopOrderResult.Fail("员工无效");
            if (order == null) return PreparedShopOrderResult.Fail("订单无效");
            if (order.state != PreparedShopOrderState.PaidWaitingPreparation && order.state != PreparedShopOrderState.Assigned)
                return PreparedShopOrderResult.Fail("订单不可认领");

            PreparedShopOrderWorker worker = GetPreparedOrderWorker(order.serviceDefName);
            if (worker == null) return PreparedShopOrderResult.Fail("订单缺少 Worker");
            if (!worker.CanStaffWork(staff, order, out string reason))
                return PreparedShopOrderResult.Fail(reason);

            order.staffThingId = staff.thingIDNumber;
            order.assignedTick = Find.TickManager?.TicksGame ?? 0;
            order.state = PreparedShopOrderState.Assigned;
            SimShopEvents.NotifyPreparedOrderAssigned(order, staff);
            return PreparedShopOrderResult.Success(order);
        }

        /// <summary>
        /// 标记员工开始制作订单。
        /// </summary>
        public static PreparedShopOrderResult StartPreparation(Pawn staff, PreparedShopOrder order)
        {
            if (staff == null) return PreparedShopOrderResult.Fail("员工无效");
            if (order == null) return PreparedShopOrderResult.Fail("订单无效");
            if (order.state != PreparedShopOrderState.Assigned && order.state != PreparedShopOrderState.PaidWaitingPreparation)
                return PreparedShopOrderResult.Fail("订单状态不允许开始制作");

            order.staffThingId = staff.thingIDNumber;
            order.startedTick = Find.TickManager?.TicksGame ?? 0;
            order.state = PreparedShopOrderState.Preparing;
            GetPreparedOrderWorker(order.serviceDefName)?.NotifyPreparationStarted(staff, order);
            SimShopEvents.NotifyPreparedOrderStarted(order, staff);
            return PreparedShopOrderResult.Success(order);
        }

        /// <summary>
        /// 标记员工完成制作订单。
        /// </summary>
        public static PreparedShopOrderResult CompletePreparation(Pawn staff, PreparedShopOrder order)
        {
            if (order == null) return PreparedShopOrderResult.Fail("订单无效");
            if (order.state != PreparedShopOrderState.Preparing && order.state != PreparedShopOrderState.Assigned)
                return PreparedShopOrderResult.Fail("订单状态不允许完成制作");

            order.completedTick = Find.TickManager?.TicksGame ?? 0;
            order.state = PreparedShopOrderState.ReadyForPickup;
            GetPreparedOrderWorker(order.serviceDefName)?.NotifyPreparationCompleted(staff, order);
            SimShopEvents.NotifyPreparedOrderCompleted(order, staff);
            return PreparedShopOrderResult.Success(order);
        }

        /// <summary>
        /// 标记订单已交付给顾客。
        /// </summary>
        public static PreparedShopOrderResult DeliverOrder(Pawn customer, PreparedShopOrder order)
        {
            if (order == null) return PreparedShopOrderResult.Fail("订单无效");
            if (order.state != PreparedShopOrderState.ReadyForPickup)
                return PreparedShopOrderResult.Fail("订单尚未可交付");

            order.state = PreparedShopOrderState.Delivered;
            if (order.completedTick <= 0) order.completedTick = Find.TickManager?.TicksGame ?? 0;
            GetPreparedOrderWorker(order.serviceDefName)?.NotifyDelivered(customer, order);
            SimShopEvents.NotifyPreparedOrderDelivered(order, customer);
            return PreparedShopOrderResult.Success(order);
        }

        /// <summary>
        /// 取消或失败现做订单。
        /// </summary>
        public static PreparedShopOrderResult CancelOrder(PreparedShopOrder order, string reason, bool failed = false)
        {
            if (order == null) return PreparedShopOrderResult.Fail("订单无效");
            order.state = failed ? PreparedShopOrderState.Failed : PreparedShopOrderState.Canceled;
            order.completedTick = Find.TickManager?.TicksGame ?? 0;
            GetPreparedOrderWorker(order.serviceDefName)?.NotifyCanceled(order, reason);
            SimShopEvents.NotifyPreparedOrderCanceled(order, reason ?? "");
            return PreparedShopOrderResult.Success(order);
        }

        /// <summary>
        /// 创建订单对应的员工制作 Job。
        /// </summary>
        public static Job MakeStaffPrepareJob(Pawn staff, PreparedShopOrder order)
        {
            return GetPreparedOrderWorker(order?.serviceDefName)?.MakeStaffPrepareJob(staff, order);
        }

        /// <summary>
        /// 创建订单对应的顾客领取或使用 Job。
        /// </summary>
        public static Job MakeCustomerReceiveJob(Pawn customer, PreparedShopOrder order)
        {
            return GetPreparedOrderWorker(order?.serviceDefName)?.MakeCustomerReceiveJob(customer, order);
        }
    }
}
