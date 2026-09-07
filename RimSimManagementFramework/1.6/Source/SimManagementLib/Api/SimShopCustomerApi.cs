using SimManagementLib.Pojo;
using SimManagementLib.SimAI;
using SimManagementLib.SimAI.CustomerVisit;
using SimManagementLib.SimDef;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SimManagementLib.Api
{
    //提供顾客系统对外扩展入口，负责查询顾客类型、构建动作上下文和选择外部顾客动作。
    public static class SimShopCustomerApi
    {
        private static readonly List<CustomerVisitExtension> VisitExtensions = new List<CustomerVisitExtension>();

        //返回当前运行时顾客类型目录。
        public static IReadOnlyCollection<RuntimeCustomerKind> CustomerKinds => CustomerCatalog.Kinds ?? new List<RuntimeCustomerKind>();

        //判断当前是否存在顾客访问扩展，负责让 Session 热路径跳过无意义的上下文构造。
        public static bool HasCustomerVisitExtensions => VisitExtensions.Count > 0;

        //按 ID 查找运行时顾客类型。
        public static RuntimeCustomerKind GetCustomerKind(string kindId)
        {
            return CustomerCatalog.GetKind(kindId);
        }

        //查询顾客当前所属访问，负责让外部长期会话通过 Pawn 找回顾客状态。
        public static LordJob_CustomerVisit GetCustomerVisit(Pawn customer)
        {
            return customer?.Map?.lordManager?.LordOf(customer)?.LordJob as LordJob_CustomerVisit;
        }

        //查询顾客当前 Session，负责让外部扩展读取统一顾客阶段。
        public static CustomerVisitSession GetCustomerSession(Pawn customer)
        {
            LordJob_CustomerVisit visit = GetCustomerVisit(customer);
            return visit?.GetOrCreateSession(customer);
        }

        //注册顾客访问扩展，负责让外部玩法接入明确的 Session 阶段节点。
        public static bool RegisterCustomerVisitExtension(CustomerVisitExtension extension)
        {
            if (extension == null || VisitExtensions.Contains(extension)) return false;
            VisitExtensions.Add(extension);
            return true;
        }

        //取消注册顾客访问扩展。
        public static bool UnregisterCustomerVisitExtension(CustomerVisitExtension extension)
        {
            return extension != null && VisitExtensions.Remove(extension);
        }

        //查询顾客当前商店，负责让外部长期会话不直接读取 LordJob 内部字段。
        public static Zone_Shop GetCurrentShop(Pawn customer)
        {
            LordJob_CustomerVisit visit = GetCustomerVisit(customer);
            return visit?.GetCurrentShop(customer);
        }

        //查询顾客在当前商店的剩余预算，负责让外部扩展不直接读取访问对象。
        public static float GetRemainingBudget(Pawn customer, Zone_Shop shop)
        {
            if (customer == null || shop == null) return 0f;
            LordJob_CustomerVisit visit = GetCustomerVisit(customer);
            return visit?.GetRemainingTripBudget(customer, shop) ?? 0f;
        }

        //查询顾客当前待付款总额，负责让外部扩展不直接读取账单字典。
        public static float GetAmountOwed(Pawn customer)
        {
            if (customer == null) return 0f;
            LordJob_CustomerVisit visit = GetCustomerVisit(customer);
            return visit?.GetAmountOwedForCheckout(customer.thingIDNumber) ?? 0f;
        }

        //向顾客当前待结账金额追加费用，负责让外部长期会话接入现有收银流程。
        public static SimApiResult AddCustomerBill(Pawn customer, float amount)
        {
            if (customer == null) return SimApiResult.Fail("顾客无效");
            if (amount <= 0f) return SimApiResult.Fail("账单金额无效");
            LordJob_CustomerVisit visit = GetCustomerVisit(customer);
            if (visit == null) return SimApiResult.Fail("顾客访问状态不存在");
            return visit.AddCustomerBill(customer.thingIDNumber, amount)
                ? SimApiResult.Success()
                : SimApiResult.Fail("账单追加失败");
        }

        //确保顾客待付款金额至少达到指定值，负责让外部服务在多次收尾时避免重复追加账单。
        public static SimApiResult EnsureCustomerBillAtLeast(Pawn customer, float amount)
        {
            if (customer == null) return SimApiResult.Fail("顾客无效");
            if (amount <= 0f) return SimApiResult.Fail("账单金额无效");
            LordJob_CustomerVisit visit = GetCustomerVisit(customer);
            if (visit == null) return SimApiResult.Fail("顾客访问状态不存在");
            float current = visit.GetCartValue(customer.thingIDNumber);
            if (current >= amount) return SimApiResult.Success();
            return visit.AddCustomerBill(customer.thingIDNumber, amount - current)
                ? SimApiResult.Success()
                : SimApiResult.Fail("账单同步失败");
        }

        //追加顾客服务订单，负责让外部服务型玩法同步服务票据和收银账单。
        public static SimApiResult AddCustomerServiceOrder(Pawn customer, CustomerServiceOrder order)
        {
            if (customer == null) return SimApiResult.Fail("顾客无效");
            if (order == null) return SimApiResult.Fail("服务订单无效");
            LordJob_CustomerVisit visit = GetCustomerVisit(customer);
            if (visit == null) return SimApiResult.Fail("顾客访问状态不存在");
            visit.AddServiceOrder(customer.thingIDNumber, order);
            return SimApiResult.Success();
        }

        //标记顾客准备进入结账阶段，负责让外部长期会话在到期时交还给默认收银流程。
        public static SimApiResult MarkCustomerReadyForCheckout(Pawn customer)
        {
            if (customer == null) return SimApiResult.Fail("顾客无效");
            LordJob_CustomerVisit visit = GetCustomerVisit(customer);
            if (visit == null) return SimApiResult.Fail("顾客访问状态不存在");
            visit.MarkPawnReadyForCheckout(customer.thingIDNumber);
            return SimApiResult.Success();
        }

        //通知顾客 Session 阶段变化。
        public static void NotifyCustomerVisitStageChanged(CustomerVisitExtensionContext context)
        {
            InvokeVisitExtensions("阶段变化", extension => extension.OnStageChanged(context));
        }

        //通知顾客 Session 周期 Tick。
        public static void NotifyCustomerVisitExtensionTick(CustomerVisitExtensionContext context)
        {
            InvokeVisitExtensions("周期 Tick", extension => extension.TickLongStay(context));
        }

        //判断扩展是否延迟普通结账。
        public static bool ShouldDelayCustomerVisitCheckout(CustomerVisitExtensionContext context)
        {
            if (context == null) return false;
            return AnyVisitExtension("延迟普通结账", extension => extension.ShouldDelayCheckout(context));
        }

        //判断扩展是否延迟普通离店。
        public static bool ShouldDelayCustomerVisitLeave(CustomerVisitExtensionContext context)
        {
            if (context == null) return false;
            return AnyVisitExtension("延迟普通离店", extension => extension.ShouldDelayLeave(context));
        }

        //构建顾客动作上下文，负责统一预算、商店和顾客类型计算。
        public static CustomerActionContext BuildActionContext(Pawn customer, LordJob_CustomerVisit visit, Zone_Shop shop)
        {
            if (customer == null || visit == null || shop == null) return null;
            int pawnId = customer.thingIDNumber;
            return new CustomerActionContext
            {
                customer = customer,
                internalVisit = visit,
                shop = shop,
                pawnId = pawnId,
                customerKind = visit.RuntimeCustomerKind,
                remainingBudget = visit.GetRemainingTripBudget(customer, shop),
                currentTick = Find.TickManager?.TicksGame ?? 0
            };
        }

        //基于持久化动作订单构建顾客动作上下文，负责让外部 JobDriver 跨 Job 恢复业务状态。
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

        //尝试为顾客创建一个外部动作 Job，职责是先筛选候选，再只为最终选中的持久化动作落单。
        public static bool TryMakeCustomerActionJob(CustomerActionContext context, out Job job)
        {
            job = null;
            if (context == null) return false;
            if (TryResumeCustomerActionJob(context, out job)) return true;
            if (context.remainingBudget <= 0f) return false;

            List<CustomerActionCandidate> candidates = new List<CustomerActionCandidate>();
            foreach (CustomerActionDef actionDef in DefDatabase<CustomerActionDef>.AllDefsListForReading.Where(def => def != null))
            {
                CustomerActionContext localContext = CloneContextForAction(context, actionDef);
                CustomerActionWorker worker = actionDef.Worker;
                if (worker == null) continue;
                if (!worker.CanRun(localContext, out _)) continue;
                bool persistent = worker.ShouldCreateOrder(localContext);
                Job candidateJob = persistent ? null : worker.MakeJob(localContext);
                if (!persistent && candidateJob == null) continue;
                float weight = Mathf.Max(0.01f, worker.GetSelectionWeight(localContext));
                candidates.Add(new CustomerActionCandidate(actionDef, localContext, candidateJob, weight, persistent));
            }

            while (!candidates.NullOrEmpty())
            {
                CustomerActionCandidate selected = candidates.RandomElementByWeight(candidate => candidate.Weight);
                selected.Context.actionDef = selected.Def;
                Job selectedJob = selected.Persistent
                    ? TryMakePersistentOrderJob(selected.Context, selected.Def.Worker)
                    : selected.Job;
                if (selectedJob != null)
                {
                    selected.Def.Worker.NotifyJobCreated(selected.Context, selectedJob);
                    job = selectedJob;
                    return true;
                }

                candidates.Remove(selected);
            }

            return false;
        }

        //通知一个顾客动作已经完成，负责给外部 JobDriver 提供统一完成入口。
        public static void NotifyCustomerActionCompleted(CustomerActionContext context)
        {
            context?.actionDef?.Worker?.NotifyActionCompleted(context);
        }

        //创建并保存顾客动作订单。
        public static SimApiResult<CustomerActionOrder> CreateActionOrder(CustomerActionContext context)
        {
            if (context?.actionDef == null) return SimApiResult<CustomerActionOrder>.Fail("动作定义无效");
            GameComp.GameComponent_CustomerActionOrderManager manager = SimShopApi.CustomerActionOrderManager;
            if (manager == null) return SimApiResult<CustomerActionOrder>.Fail("顾客动作订单管理器不可用");
            CustomerActionWorker worker = context.actionDef.Worker;
            if (worker == null) return SimApiResult<CustomerActionOrder>.Fail("动作 Worker 无效");
            CustomerActionOrder order = worker.CreateOrder(context);
            if (order == null) return SimApiResult<CustomerActionOrder>.Fail("Worker 未创建动作订单");
            if (string.IsNullOrEmpty(order.actionDefName)) order.actionDefName = context.actionDef.defName;
            if (order.customerThingId < 0) order.customerThingId = context.customer?.thingIDNumber ?? -1;
            if (order.shopZoneId < 0) order.shopZoneId = context.shop?.ID ?? -1;
            if (order.createdTick <= 0) order.createdTick = Find.TickManager?.TicksGame ?? 0;

            manager.AddOrder(order);
            SimShopEvents.NotifyCustomerActionOrderCreated(order, context.customer);
            return SimApiResult<CustomerActionOrder>.Success(order);
        }

        //按编号查找顾客动作订单。
        public static CustomerActionOrder GetActionOrder(int orderId)
        {
            return SimShopApi.CustomerActionOrderManager?.GetOrder(orderId);
        }

        //按条件查询顾客动作订单。
        public static List<CustomerActionOrder> QueryActionOrders(CustomerActionOrderQuery query)
        {
            return SimShopApi.CustomerActionOrderManager?.QueryOrders(query) ?? new List<CustomerActionOrder>();
        }

        //判断顾客在指定商店是否有仍需顾客 Job 推进的动作订单。
        public static bool HasRunningCustomerActionOrder(Pawn customer, int shopZoneId = -1)
        {
            return FindRunningCustomerActionOrders(customer, shopZoneId).Count > 0;
        }

        //标记动作订单开始执行。
        public static SimApiResult StartActionOrder(CustomerActionOrder order)
        {
            if (order == null) return SimApiResult.Fail("动作订单无效");
            if (order.state == CustomerActionOrderState.InProgress) return SimApiResult.Success();
            if (!order.IsActiveState)
                return SimApiResult.Fail("动作订单已经结束");
            order.state = CustomerActionOrderState.InProgress;
            order.startedTick = Find.TickManager?.TicksGame ?? 0;
            SimShopEvents.NotifyCustomerActionOrderStarted(order);
            return SimApiResult.Success();
        }

        //标记动作订单等待员工处理。
        public static SimApiResult MarkActionOrderWaitingStaff(CustomerActionOrder order, Pawn staff = null)
        {
            if (order == null) return SimApiResult.Fail("动作订单无效");
            if (!order.IsActiveState) return SimApiResult.Fail("动作订单已经结束");
            if (order.state == CustomerActionOrderState.WaitingStaff)
            {
                if (staff != null) AddOrderStaff(order, staff);
                return SimApiResult.Success();
            }
            order.state = CustomerActionOrderState.WaitingStaff;
            if (staff != null)
                AddOrderStaff(order, staff);
            SimShopEvents.NotifyCustomerActionOrderWaitingStaff(order, staff);
            return SimApiResult.Success();
        }

        //尝试让员工加入顾客动作会话订单。
        public static SimApiResult TryAssignActionOrderStaff(Pawn staff, CustomerActionOrder order)
        {
            if (staff == null) return SimApiResult.Fail("员工无效");
            if (order == null) return SimApiResult.Fail("动作订单无效");
            if (!order.IsActiveState) return SimApiResult.Fail("动作订单已经结束");
            CustomerActionSessionWorker worker = order.ActionDef?.Worker as CustomerActionSessionWorker;
            if (worker == null) return SimApiResult.Fail("动作订单没有会话 Worker");
            CustomerActionContext context = BuildActionContext(FindActionOrderCustomer(staff.Map, order), order);
            if (!worker.CanStaffJoin(context, staff, out string reason))
                return SimApiResult.Fail(reason);
            AddOrderStaff(order, staff);
            SimShopEvents.NotifyCustomerActionOrderStaffAssigned(order, staff);
            return SimApiResult.Success();
        }

        //创建员工参与顾客动作会话的 Job。
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

        //完成顾客动作订单，并在需要时把顾客推向结账阶段。
        public static SimApiResult CompleteActionOrder(CustomerActionOrder order)
        {
            if (order == null) return SimApiResult.Fail("动作订单无效");
            if (order.state == CustomerActionOrderState.Completed) return SimApiResult.Success();
            if (!order.IsActiveState) return SimApiResult.Fail("动作订单已经取消或失败");
            order.state = CustomerActionOrderState.Completed;
            order.completedTick = Find.TickManager?.TicksGame ?? 0;
            SimShopEvents.NotifyCustomerActionOrderCompleted(order);
            return SimApiResult.Success();
        }

        //取消或失败顾客动作订单。
        public static SimApiResult CancelActionOrder(CustomerActionOrder order, string reason, bool failed = false)
        {
            if (order == null) return SimApiResult.Fail("动作订单无效");
            if (order.state == CustomerActionOrderState.Canceled || order.state == CustomerActionOrderState.Failed)
                return SimApiResult.Success();
            if (order.state == CustomerActionOrderState.Completed)
                return SimApiResult.Fail("已经完成的动作订单不能取消");
            order.state = failed ? CustomerActionOrderState.Failed : CustomerActionOrderState.Canceled;
            order.completedTick = Find.TickManager?.TicksGame ?? 0;
            CustomerActionContext context = BuildActionContext(FindOrderCustomer(order), order);
            context?.actionDef?.Worker?.NotifyOrderCanceled(context, reason ?? "");
            SimShopEvents.NotifyCustomerActionOrderCanceled(order, reason ?? "");
            return SimApiResult.Success();
        }

        //查找动作订单引用的顾客。
        public static Pawn FindActionOrderCustomer(Map map, CustomerActionOrder order)
        {
            if (map?.mapPawns == null || order == null || order.customerThingId < 0) return null;
            return map.mapPawns.AllPawnsSpawned.FirstOrDefault(p => p != null && p.thingIDNumber == order.customerThingId);
        }

        //查找动作订单引用的目标建筑。
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

        //为指定动作复制上下文，负责避免多个候选动作共享可变动作 Def。
        private static CustomerActionContext CloneContextForAction(CustomerActionContext source, CustomerActionDef actionDef)
        {
            return new CustomerActionContext
            {
                customer = source.customer,
                shop = source.shop,
                internalVisit = source.internalVisit,
                actionDef = actionDef,
                customerKind = source.customerKind,
                pawnId = source.pawnId,
                remainingBudget = source.remainingBudget,
                currentTick = source.currentTick
            };
        }

        //尝试通过持久化动作订单创建 Job，负责在未启用订单时返回 null 让非持久化动作继续选择。
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

        //恢复顾客已有的持久化动作订单，职责是让临时 Job 中断不会创建重复业务订单。
        internal static bool TryResumeCustomerActionJob(CustomerActionContext context, out Job job)
        {
            job = null;
            if (context?.customer == null) return false;

            List<CustomerActionOrder> orders = FindRunningCustomerActionOrders(context.customer, context.shop?.ID ?? -1);
            for (int i = 0; i < orders.Count; i++)
            {
                CustomerActionOrder order = orders[i];
                CustomerActionWorker worker = order.ActionDef?.Worker;
                CustomerActionContext orderContext = BuildActionContext(context.customer, order);
                if (worker == null || orderContext == null)
                {
                    CancelActionOrder(order, "动作订单无法恢复上下文", true);
                    continue;
                }
                if (!worker.CanStartOrder(orderContext, out string reason))
                {
                    CancelActionOrder(order, reason.NullOrEmpty() ? "动作订单已经无法继续" : reason, true);
                    continue;
                }

                Job resumed = worker.MakeJobForOrder(orderContext);
                if (resumed == null)
                {
                    CancelActionOrder(order, "动作订单无法恢复顾客 Job", true);
                    continue;
                }

                resumed.count = order.orderId;
                worker.NotifyJobCreated(orderContext, resumed);
                context.actionDef = order.ActionDef;
                context.order = order;
                context.actionOrderId = order.orderId;
                job = resumed;
                return true;
            }

            return false;
        }

        //查找顾客仍处于执行阶段的动作订单，职责是统一看门狗保护与 Job 恢复的状态语义。
        private static List<CustomerActionOrder> FindRunningCustomerActionOrders(Pawn customer, int shopZoneId)
        {
            if (customer == null) return new List<CustomerActionOrder>();
            CustomerActionOrderQuery query = new CustomerActionOrderQuery
            {
                customerThingId = customer.thingIDNumber,
                shopZoneId = shopZoneId,
                includeTerminalOrders = false,
                states = new List<CustomerActionOrderState>
                {
                    CustomerActionOrderState.Active,
                    CustomerActionOrderState.WaitingStaff,
                    CustomerActionOrderState.InProgress
                }
            };
            return QueryActionOrders(query)
                .Where(order => order != null)
                .OrderBy(order => order.createdTick)
                .ToList();
        }

        //按订单查找对应商店区域。
        private static Zone_Shop FindShopByOrder(Map map, CustomerActionOrder order)
        {
            if (map == null || order == null) return null;
            return map.zoneManager.AllZones
                .OfType<Zone_Shop>()
                .FirstOrDefault(zone => zone.ID == order.shopZoneId);
        }

        //按订单查找地图上的顾客。
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

        //把员工记录到订单，负责同步主员工字段和多人参与列表。
        private static void AddOrderStaff(CustomerActionOrder order, Pawn staff)
        {
            if (order == null || staff == null) return;
            order.staffThingId = staff.thingIDNumber;
            if (order.staffThingIds == null)
                order.staffThingIds = new List<int>();
            if (!order.staffThingIds.Contains(staff.thingIDNumber))
                order.staffThingIds.Add(staff.thingIDNumber);
        }

        //依次通知顾客访问扩展，负责隔离外部扩展异常。
        private static void InvokeVisitExtensions(string stage, Action<CustomerVisitExtension> action)
        {
            if (action == null || VisitExtensions.Count == 0) return;
            for (int i = 0; i < VisitExtensions.Count; i++)
            {
                CustomerVisitExtension extension = VisitExtensions[i];
                if (extension == null) continue;
                try
                {
                    action(extension);
                }
                catch (Exception ex)
                {
                    Log.Error($"[SimShop.CustomerApi] 顾客访问扩展在 {stage} 阶段执行失败: {ex}");
                }
            }
        }

        //依次询问顾客访问扩展布尔决策，负责采用任一扩展同意即生效的规则。
        private static bool AnyVisitExtension(string stage, Func<CustomerVisitExtension, bool> predicate)
        {
            if (predicate == null || VisitExtensions.Count == 0) return false;
            for (int i = 0; i < VisitExtensions.Count; i++)
            {
                CustomerVisitExtension extension = VisitExtensions[i];
                if (extension == null) continue;
                try
                {
                    if (predicate(extension))
                        return true;
                }
                catch (Exception ex)
                {
                    Log.Error($"[SimShop.CustomerApi] 顾客访问扩展在 {stage} 阶段执行失败: {ex}");
                }
            }
            return false;
        }

        //保存候选动作及其上下文，职责是区分轻量预览与最终持久化落单。
        private sealed class CustomerActionCandidate
        {
            public readonly CustomerActionDef Def;
            public readonly CustomerActionContext Context;
            public readonly Job Job;
            public readonly float Weight;
            public readonly bool Persistent;

            //构造动作候选，职责是保留选择阶段所需的全部只读数据。
            public CustomerActionCandidate(CustomerActionDef def, CustomerActionContext context, Job job, float weight, bool persistent)
            {
                Def = def;
                Context = context;
                Job = job;
                Weight = weight;
                Persistent = persistent;
            }
        }
    }
}
