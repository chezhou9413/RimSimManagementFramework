using SimManagementLib.GameComp;
using SimManagementLib.SimAI;
using SimManagementLib.SimZone;
using SimManagementLib.SimThingComp;
using System.Collections.Generic;
using System.Text;
using Verse;

namespace SimManagementLib.SimMapComp
{
    //类职责：提供顾客刷新协调器的地图级运行态入口，集中管理索引、快照和结账票据。
    public partial class CustomerArrivalManager
    {
        private int forcedExitCount;
        private int behaviorBudgetTick = -1;
        private int behaviorBudgetUsed;
        private readonly HashSet<int> behaviorPawnsThisTick = new HashSet<int>();
        private int reachabilityBudgetTick = -1;
        private int reachabilityBudgetUsed;
        private readonly Dictionary<int, Thing> customerTargetsById = new Dictionary<int, Thing>();
        private int lastTargetFallbackScanTick = -60;
        //确保地图级运行模块存在，职责是在读档后按真实 Lord 和区划重建易失状态。
        private void EnsureRuntime()
        {
            if (customerIndex == null)
                customerIndex = new CustomerRuntimeIndex(map);
            if (arrivalRuntime == null)
                arrivalRuntime = new CustomerArrivalRuntime(this, map);
        }

        //返回地图级顾客索引，职责是供行为、结账和诊断模块执行常数时间查询。
        internal CustomerRuntimeIndex RuntimeIndex
        {
            get
            {
                EnsureRuntime();
                return customerIndex;
            }
        }

        //返回地图级结账票据登记器，职责是供结账 JobGiver 和 JobDriver 共享队列顺序。
        internal CustomerCheckoutQueueRegistry CheckoutQueue => RuntimeIndex.CheckoutQueue;

        //释放指定顾客结账票据，职责是为失败、中断和离店提供公开幂等入口。
        public void ReleaseCheckoutTicket(Pawn pawn)
        {
            if (pawn == null) return;
            RuntimeIndex.CheckoutQueue.ReleasePawn(pawn.thingIDNumber);
        }

        //标记指定商店快照失效，职责是把库存和配置变化合并到预算队列。
        public void NotifyShopDirty(Zone_Shop shop)
        {
            EnsureRuntime();
            arrivalRuntime.MarkDirty(shop);
        }

        //标记全部商店吸引力失效，职责是响应商品或顾客目录整体变化。
        public void NotifyCustomerCatalogDirty()
        {
            EnsureRuntime();
            arrivalRuntime.MarkAllDirty();
        }

        //注销商店快照，职责是按区划 ID 立即删除运行态。
        public void NotifyShopUnregistered(int shopId)
        {
            EnsureRuntime();
            arrivalRuntime.Unregister(shopId);
        }

        //返回商店缓存入口格，职责是避免旅行状态反复扫描货柜和区划格。
        public bool TryGetShopEntryCell(int shopId, out IntVec3 cell)
        {
            EnsureRuntime();
            return arrivalRuntime.TryGetEntryCell(shopId, out cell);
        }

        //返回开放商店快照，职责是供低频跨店选择复用吸引力和入口缓存。
        internal List<CustomerArrivalShopContext> GetOpenShopContexts()
        {
            EnsureRuntime();
            return arrivalRuntime.GetOpenContexts(customerIndex);
        }

        //申请顾客行为候选预算，职责是每地图每 tick 最多让四位不同顾客执行目标选择。
        public bool TryConsumeBehaviorBudget(Pawn pawn)
        {
            int pawnId = pawn?.thingIDNumber ?? -1;
            if (pawnId <= 0) return false;
            int now = Find.TickManager?.TicksGame ?? 0;
            if (behaviorBudgetTick != now)
            {
                behaviorBudgetTick = now;
                behaviorBudgetUsed = 0;
                behaviorPawnsThisTick.Clear();
            }
            if (behaviorPawnsThisTick.Contains(pawnId)) return true;
            if (behaviorBudgetUsed >= 4) return false;
            behaviorBudgetUsed++;
            behaviorPawnsThisTick.Add(pawnId);
            return true;
        }

        //申请轻量可达性预算，职责是每地图每 tick 最多执行一次候选区域可达判断。
        public bool TryConsumeReachabilityBudget()
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            if (reachabilityBudgetTick != now)
            {
                reachabilityBudgetTick = now;
                reachabilityBudgetUsed = 0;
            }
            if (reachabilityBudgetUsed >= 1) return false;
            reachabilityBudgetUsed++;
            return true;
        }

        //登记顾客行为目标，职责是让服务订单和运行 Job 按 Thing ID 常数时间解析对象。
        internal void RegisterCustomerTarget(Thing thing)
        {
            if (thing != null && !thing.Destroyed && thing.thingIDNumber >= 0)
                customerTargetsById[thing.thingIDNumber] = thing;
        }

        //按 Thing ID 解析顾客目标，职责是在索引未命中时每 60 tick 最多合批扫描一次地图。
        public Thing FindCustomerTargetById(int thingId)
        {
            if (thingId < 0) return null;
            if (customerTargetsById.TryGetValue(thingId, out Thing cached))
            {
                if (cached != null && !cached.Destroyed && cached.Map == map) return cached;
                customerTargetsById.Remove(thingId);
            }

            int now = Find.TickManager?.TicksGame ?? 0;
            if (now - lastTargetFallbackScanTick < 60) return null;
            lastTargetFallbackScanTick = now;
            IReadOnlyList<Thing> things = map?.listerThings?.AllThings;
            if (things == null) return null;
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (thing is ThingWithComps withComps && withComps.GetComp<ThingComp_ServiceProvider>() != null)
                    RegisterCustomerTarget(thing);
            }
            return customerTargetsById.TryGetValue(thingId, out cached) ? cached : null;
        }

        //登记普通商店顾客，职责是在生成完成后立即更新容量计数而不等待巡检。
        internal void RegisterShopCustomer(Pawn pawn, LordJob_CustomerVisit visit)
        {
            EnsureRuntime();
            customerIndex.RegisterShopCustomer(pawn, visit);
        }

        //登记自动售货机顾客，职责是在生成完成后立即更新机器容量计数。
        internal void RegisterVendingCustomer(Pawn pawn, LordJob_VendingMachineVisit visit)
        {
            EnsureRuntime();
            customerIndex.RegisterVendingCustomer(pawn, visit);
        }

        //释放顾客运行态，职责是让离店顾客立即退出容量和结账统计。
        public void UnregisterCustomer(Pawn pawn)
        {
            EnsureRuntime();
            customerIndex.Unregister(pawn);
        }

        //记录一次强制退出，职责是为地图级可靠性诊断累计次数。
        public void NotifyForcedExit()
        {
            forcedExitCount++;
        }

        //构建地图级顾客诊断，职责是汇总索引、阶段、脏快照、预算游标、票据和日志积压。
        public string BuildRuntimeDiagnostics()
        {
            EnsureRuntime();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("顾客索引数: " + customerIndex.TotalCount);
            sb.AppendLine("阶段人数: " + customerIndex.BuildStageDiagnostics());
            sb.AppendLine("脏商店快照: " + arrivalRuntime.DirtyCount);
            sb.AppendLine("刷新候选进度: " + arrivalRuntime.CycleProcessed + "/" + arrivalRuntime.CycleTotal);
            sb.AppendLine("当 tick 行为预算: " + behaviorBudgetUsed + "/4");
            sb.AppendLine("当 tick 可达预算: " + reachabilityBudgetUsed + "/1");
            sb.AppendLine("结账票据: " + customerIndex.CheckoutQueue.TicketCount);
            sb.AppendLine("最老无进展 Tick: " + customerIndex.GetOldestNoProgressTicks());
            sb.AppendLine("指标计算: " + (Current.Game?.GetComponent<GameComponent_ShopAnalyticsManager>()?.BuildMetricsBudgetDiagnostics(map) ?? "无"));
            sb.AppendLine("强制离店次数: " + forcedExitCount);
            sb.AppendLine("日志积压: " + Tool.SimDebugLogger.PendingCount);
            sb.AppendLine("最后刷新失败: " + (string.IsNullOrEmpty(lastSpawnFailureReason) ? "无" : lastSpawnFailureReason));
            return sb.ToString().TrimEnd();
        }
    }
}
