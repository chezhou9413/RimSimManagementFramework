using SimManagementLib.Pojo;
using SimManagementLib.SimAI;
using SimManagementLib.SimDef;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SimManagementLib.Api
{
    /// <summary>
    /// 提供顾客系统对外扩展入口，负责查询顾客类型、构建动作上下文和选择外部顾客动作。
    /// </summary>
    public static class SimShopCustomerApi
    {
        /// <summary>
        /// 返回当前运行时顾客类型目录。
        /// </summary>
        public static IReadOnlyCollection<RuntimeCustomerKind> CustomerKinds => CustomerCatalog.Kinds ?? new List<RuntimeCustomerKind>();

        /// <summary>
        /// 按 ID 查找运行时顾客类型。
        /// </summary>
        public static RuntimeCustomerKind GetCustomerKind(string kindId)
        {
            return CustomerCatalog.GetKind(kindId);
        }

        /// <summary>
        /// 构建顾客动作上下文，负责统一预算、商店和顾客类型计算。
        /// </summary>
        public static CustomerActionContext BuildActionContext(Pawn customer, LordJob_CustomerVisit visit, Zone_Shop shop)
        {
            if (customer == null || visit == null || shop == null) return null;
            int pawnId = customer.thingIDNumber;
            return new CustomerActionContext
            {
                customer = customer,
                visit = visit,
                shop = shop,
                pawnId = pawnId,
                customerKind = visit.RuntimeCustomerKind,
                remainingBudget = visit.GetRemainingTripBudget(customer, shop),
                currentTick = Find.TickManager?.TicksGame ?? 0
            };
        }

        /// <summary>
        /// 基于持久化动作订单构建顾客动作上下文，负责让外部 JobDriver 跨 Job 恢复业务状态。
        /// </summary>
        public static CustomerActionContext BuildActionContext(Pawn customer, CustomerActionOrder order)
        {
            if (customer == null || order == null || customer.Map == null) return null;
            LordJob_CustomerVisit visit = customer.Map.lordManager.LordOf(customer)?.LordJob as LordJob_CustomerVisit;
            Zone_Shop shop = FindShopByOrder(customer.Map, order);
            CustomerActionContext context = BuildActionContext(customer, visit, shop);
            if (context == null) return null;
            context.order = order;
            context.actionOrderId = order.orderId;
            context.actionDef = order.ActionDef;
            return context;
        }

        /// <summary>
        /// 尝试为顾客创建一个外部动作 Job，负责在没有合适动作时安全返回 false。
        /// </summary>
        public static bool TryMakeCustomerActionJob(CustomerActionContext context, out Job job)
        {
            job = null;
            if (context == null || context.remainingBudget <= 0f) return false;

            List<CustomerActionCandidate> candidates = new List<CustomerActionCandidate>();
            foreach (CustomerActionDef actionDef in DefDatabase<CustomerActionDef>.AllDefsListForReading.Where(def => def != null))
            {
                CustomerActionContext localContext = CloneContextForAction(context, actionDef);
                CustomerActionWorker worker = actionDef.Worker;
                if (worker == null) continue;
                if (!worker.CanRun(localContext, out _)) continue;
                Job candidateJob = TryMakePersistentOrderJob(localContext, worker);
                if (candidateJob == null)
                    candidateJob = worker.MakeJob(localContext);
                if (candidateJob == null) continue;
                float weight = Mathf.Max(0.01f, worker.GetSelectionWeight(localContext));
                candidates.Add(new CustomerActionCandidate(actionDef, localContext, candidateJob, weight));
            }

            if (candidates.NullOrEmpty()) return false;
            CustomerActionCandidate selected = candidates.RandomElementByWeight(candidate => candidate.Weight);
            selected.Context.actionDef = selected.Def;
            selected.Def.Worker.NotifyJobCreated(selected.Context, selected.Job);
            job = selected.Job;
            return job != null;
        }

        /// <summary>
        /// 通知一个顾客动作已经完成，负责给外部 JobDriver 提供统一完成入口。
        /// </summary>
        public static void NotifyCustomerActionCompleted(CustomerActionContext context)
        {
            context?.actionDef?.Worker?.NotifyActionCompleted(context);
        }

        /// <summary>
        /// 创建并保存顾客动作订单。
        /// </summary>
        public static SimApiResult<CustomerActionOrder> CreateActionOrder(CustomerActionContext context)
        {
            if (context?.actionDef == null) return SimApiResult<CustomerActionOrder>.Fail("动作定义无效");
            CustomerActionWorker worker = context.actionDef.Worker;
            if (worker == null) return SimApiResult<CustomerActionOrder>.Fail("动作 Worker 无效");
            CustomerActionOrder order = worker.CreateOrder(context);
            if (order == null) return SimApiResult<CustomerActionOrder>.Fail("Worker 未创建动作订单");
            if (string.IsNullOrEmpty(order.actionDefName)) order.actionDefName = context.actionDef.defName;
            if (order.customerThingId < 0) order.customerThingId = context.customer?.thingIDNumber ?? -1;
            if (order.shopZoneId < 0) order.shopZoneId = context.shop?.ID ?? -1;
            if (order.createdTick <= 0) order.createdTick = Find.TickManager?.TicksGame ?? 0;

            GameComp.GameComponent_CustomerActionOrderManager manager = SimShopApi.CustomerActionOrderManager;
            if (manager == null) return SimApiResult<CustomerActionOrder>.Fail("顾客动作订单管理器不可用");
            manager.AddOrder(order);
            SimShopEvents.NotifyCustomerActionOrderCreated(order, context.customer);
            return SimApiResult<CustomerActionOrder>.Success(order);
        }

        /// <summary>
        /// 按编号查找顾客动作订单。
        /// </summary>
        public static CustomerActionOrder GetActionOrder(int orderId)
        {
            return SimShopApi.CustomerActionOrderManager?.GetOrder(orderId);
        }

        /// <summary>
        /// 按条件查询顾客动作订单。
        /// </summary>
        public static List<CustomerActionOrder> QueryActionOrders(CustomerActionOrderQuery query)
        {
            return SimShopApi.CustomerActionOrderManager?.QueryOrders(query) ?? new List<CustomerActionOrder>();
        }

        /// <summary>
        /// 标记动作订单开始执行。
        /// </summary>
        public static SimApiResult StartActionOrder(CustomerActionOrder order)
        {
            if (order == null) return SimApiResult.Fail("动作订单无效");
            if (order.state == CustomerActionOrderState.Completed || order.state == CustomerActionOrderState.Canceled || order.state == CustomerActionOrderState.Failed)
                return SimApiResult.Fail("动作订单已经结束");
            order.state = CustomerActionOrderState.InProgress;
            order.startedTick = Find.TickManager?.TicksGame ?? 0;
            SimShopEvents.NotifyCustomerActionOrderStarted(order);
            return SimApiResult.Success();
        }

        /// <summary>
        /// 标记动作订单等待员工处理。
        /// </summary>
        public static SimApiResult MarkActionOrderWaitingStaff(CustomerActionOrder order, Pawn staff = null)
        {
            if (order == null) return SimApiResult.Fail("动作订单无效");
            order.state = CustomerActionOrderState.WaitingStaff;
            if (staff != null)
                AddOrderStaff(order, staff);
            SimShopEvents.NotifyCustomerActionOrderWaitingStaff(order, staff);
            return SimApiResult.Success();
        }

        /// <summary>
        /// 尝试让员工加入顾客动作会话订单。
        /// </summary>
        public static SimApiResult TryAssignActionOrderStaff(Pawn staff, CustomerActionOrder order)
        {
            if (staff == null) return SimApiResult.Fail("员工无效");
            if (order == null) return SimApiResult.Fail("动作订单无效");
            CustomerActionSessionWorker worker = order.ActionDef?.Worker as CustomerActionSessionWorker;
            if (worker == null) return SimApiResult.Fail("动作订单没有会话 Worker");
            CustomerActionContext context = BuildActionContext(FindActionOrderCustomer(staff.Map, order), order);
            if (!worker.CanStaffJoin(context, staff, out string reason))
                return SimApiResult.Fail(reason);
            AddOrderStaff(order, staff);
            SimShopEvents.NotifyCustomerActionOrderStaffAssigned(order, staff);
            return SimApiResult.Success();
        }

        /// <summary>
        /// 创建员工参与顾客动作会话的 Job。
        /// </summary>
        public static Job MakeStaffSessionJob(Pawn staff, CustomerActionOrder order)
        {
            if (staff == null || order == null) return null;
            CustomerActionSessionWorker worker = order.ActionDef?.Worker as CustomerActionSessionWorker;
            if (worker == null) return null;
            CustomerActionContext context = BuildActionContext(FindActionOrderCustomer(staff.Map, order), order);
            Job job = worker.MakeStaffSessionJob(context, staff);
            if (job != null) job.count = order.orderId;
            return job;
        }

        /// <summary>
        /// 完成顾客动作订单，并在需要时把顾客推向结账阶段。
        /// </summary>
        public static SimApiResult CompleteActionOrder(CustomerActionOrder order)
        {
            if (order == null) return SimApiResult.Fail("动作订单无效");
            order.state = CustomerActionOrderState.Completed;
            order.completedTick = Find.TickManager?.TicksGame ?? 0;
            SimShopEvents.NotifyCustomerActionOrderCompleted(order);
            return SimApiResult.Success();
        }

        /// <summary>
        /// 取消或失败顾客动作订单。
        /// </summary>
        public static SimApiResult CancelActionOrder(CustomerActionOrder order, string reason, bool failed = false)
        {
            if (order == null) return SimApiResult.Fail("动作订单无效");
            order.state = failed ? CustomerActionOrderState.Failed : CustomerActionOrderState.Canceled;
            order.completedTick = Find.TickManager?.TicksGame ?? 0;
            CustomerActionContext context = BuildActionContext(FindOrderCustomer(order), order);
            context?.actionDef?.Worker?.NotifyOrderCanceled(context, reason ?? "");
            SimShopEvents.NotifyCustomerActionOrderCanceled(order, reason ?? "");
            return SimApiResult.Success();
        }

        /// <summary>
        /// 查找动作订单引用的顾客。
        /// </summary>
        public static Pawn FindActionOrderCustomer(Map map, CustomerActionOrder order)
        {
            if (map?.mapPawns == null || order == null || order.customerThingId < 0) return null;
            return map.mapPawns.AllPawnsSpawned.FirstOrDefault(p => p != null && p.thingIDNumber == order.customerThingId);
        }

        /// <summary>
        /// 查找动作订单引用的目标建筑。
        /// </summary>
        public static Thing FindActionOrderTarget(Map map, CustomerActionOrder order)
        {
            if (map == null || order == null || order.targetThingId < 0) return null;
            IReadOnlyList<Thing> things = map.listerThings.AllThings;
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (thing != null && thing.thingIDNumber == order.targetThingId)
                    return thing;
            }
            return null;
        }

        /// <summary>
        /// 为指定动作复制上下文，负责避免多个候选动作共享可变动作 Def。
        /// </summary>
        private static CustomerActionContext CloneContextForAction(CustomerActionContext source, CustomerActionDef actionDef)
        {
            return new CustomerActionContext
            {
                customer = source.customer,
                shop = source.shop,
                visit = source.visit,
                actionDef = actionDef,
                customerKind = source.customerKind,
                pawnId = source.pawnId,
                remainingBudget = source.remainingBudget,
                currentTick = source.currentTick
            };
        }

        /// <summary>
        /// 尝试通过持久化动作订单创建 Job，负责在未启用订单时返回 null 让旧流程接管。
        /// </summary>
        private static Job TryMakePersistentOrderJob(CustomerActionContext context, CustomerActionWorker worker)
        {
            if (context == null || worker == null || !worker.ShouldCreateOrder(context))
                return null;

            SimApiResult<CustomerActionOrder> created = CreateActionOrder(context);
            if (!created.success || created.value == null)
                return null;

            CustomerActionContext orderContext = CloneContextForAction(context, context.actionDef);
            orderContext.order = created.value;
            orderContext.actionOrderId = created.value.orderId;
            if (!worker.CanStartOrder(orderContext, out _))
            {
                CancelActionOrder(created.value, "动作订单无法开始", true);
                return null;
            }

            Job job = worker.MakeJobForOrder(orderContext);
            if (job == null)
            {
                CancelActionOrder(created.value, "动作订单没有可执行 Job", true);
                return null;
            }

            job.count = created.value.orderId;
            context.order = created.value;
            context.actionOrderId = created.value.orderId;
            return job;
        }

        /// <summary>
        /// 按订单查找对应商店区域。
        /// </summary>
        private static Zone_Shop FindShopByOrder(Map map, CustomerActionOrder order)
        {
            if (map == null || order == null) return null;
            return map.zoneManager.AllZones
                .OfType<Zone_Shop>()
                .FirstOrDefault(zone => zone.ID == order.shopZoneId);
        }

        /// <summary>
        /// 按订单查找地图上的顾客。
        /// </summary>
        private static Pawn FindOrderCustomer(CustomerActionOrder order)
        {
            if (order == null || order.customerThingId < 0 || Find.Maps == null) return null;
            for (int i = 0; i < Find.Maps.Count; i++)
            {
                Map map = Find.Maps[i];
                Pawn pawn = map?.mapPawns?.AllPawnsSpawned?.FirstOrDefault(p => p != null && p.thingIDNumber == order.customerThingId);
                if (pawn != null) return pawn;
            }
            return null;
        }

        /// <summary>
        /// 把员工记录到订单，负责兼容旧单员工字段和新多人列表。
        /// </summary>
        private static void AddOrderStaff(CustomerActionOrder order, Pawn staff)
        {
            if (order == null || staff == null) return;
            order.staffThingId = staff.thingIDNumber;
            if (order.staffThingIds == null)
                order.staffThingIds = new List<int>();
            if (!order.staffThingIds.Contains(staff.thingIDNumber))
                order.staffThingIds.Add(staff.thingIDNumber);
        }

        /// <summary>
        /// 保存候选动作及其上下文，负责随机选择时携带已创建的 Job。
        /// </summary>
        private sealed class CustomerActionCandidate
        {
            public readonly CustomerActionDef Def;
            public readonly CustomerActionContext Context;
            public readonly Job Job;
            public readonly float Weight;

            public CustomerActionCandidate(CustomerActionDef def, CustomerActionContext context, Job job, float weight)
            {
                Def = def;
                Context = context;
                Job = job;
                Weight = weight;
            }
        }
    }
}
