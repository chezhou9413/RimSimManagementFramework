using System.Collections.Generic;
using Verse;

namespace SimManagementLib.SimWorkGiver
{
    //补货工作单 tick 预算器，职责是把同一地图内多个员工的补货扫描硬性摊到多帧。
    internal static class RestockWorkTickBudget
    {
        private const int StorageCandidateChecksPerTick = 8;
        private const int StorageReachQueriesPerTick = 4;
        private const int PendingReconcilesPerTick = 1;
        private const int SupplyDefChecksPerTick = 4;
        private const int SupplyThingChecksPerTick = 32;
        private static readonly Dictionary<int, MapTickBudget> budgets = new Dictionary<int, MapTickBudget>();

        //尝试消耗货柜候选检查预算，职责是限制 NonScanJob 在同一 tick 检查的货柜总数。
        public static bool TryUseStorageCandidateCheck(Map map)
        {
            return TryUse(map, BudgetKind.StorageCandidateCheck, StorageCandidateChecksPerTick);
        }

        //尝试消耗货柜可达性查询预算，职责是限制同一 tick 进入原版寻路的次数。
        public static bool TryUseStorageReachQuery(Map map)
        {
            return TryUse(map, BudgetKind.StorageReachQuery, StorageReachQueriesPerTick);
        }

        //尝试消耗预约校正预算，职责是避免候选刷新同一 tick 扫描多次 Pawn 任务。
        public static bool TryUsePendingReconcile(Map map)
        {
            return TryUse(map, BudgetKind.PendingReconcile, PendingReconcilesPerTick);
        }

        //尝试消耗缺货 Def 扫描预算，职责是限制同一 tick 进入货源 Def 检查的次数。
        public static bool TryUseSupplyDefCheck(Map map)
        {
            return TryUse(map, BudgetKind.SupplyDefCheck, SupplyDefChecksPerTick);
        }

        //尝试消耗货源 Thing 扫描预算，职责是限制同一 tick 进入预约和可达查询的货源数量。
        public static bool TryUseSupplyThingCheck(Map map)
        {
            return TryUse(map, BudgetKind.SupplyThingCheck, SupplyThingChecksPerTick);
        }

        //按地图和当前 tick 获取预算并消耗一次，职责是集中处理计数清零和无效地图。
        private static bool TryUse(Map map, BudgetKind kind, int limit)
        {
            if (map == null)
                return false;

            MapTickBudget budget = GetBudget(map);
            int used = GetUsed(budget, kind);
            if (used >= limit)
                return false;

            SetUsed(budget, kind, used + 1);
            return true;
        }

        //读取指定预算计数，职责是集中匹配预算类型和字段。
        private static int GetUsed(MapTickBudget budget, BudgetKind kind)
        {
            if (kind == BudgetKind.StorageCandidateCheck)
                return budget.storageCandidateChecks;
            if (kind == BudgetKind.StorageReachQuery)
                return budget.storageReachQueries;
            if (kind == BudgetKind.PendingReconcile)
                return budget.pendingReconciles;
            if (kind == BudgetKind.SupplyDefCheck)
                return budget.supplyDefChecks;
            return budget.supplyThingChecks;
        }

        //写入指定预算计数，职责是集中匹配预算类型和字段。
        private static void SetUsed(MapTickBudget budget, BudgetKind kind, int value)
        {
            if (kind == BudgetKind.StorageCandidateCheck)
                budget.storageCandidateChecks = value;
            else if (kind == BudgetKind.StorageReachQuery)
                budget.storageReachQueries = value;
            else if (kind == BudgetKind.PendingReconcile)
                budget.pendingReconciles = value;
            else if (kind == BudgetKind.SupplyDefCheck)
                budget.supplyDefChecks = value;
            else
                budget.supplyThingChecks = value;
        }

        //返回当前地图的 tick 预算对象，职责是在 tick 前进时重置所有计数。
        private static MapTickBudget GetBudget(Map map)
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            int mapId = map.uniqueID;
            if (!budgets.TryGetValue(mapId, out MapTickBudget budget) || budget == null)
            {
                budget = new MapTickBudget();
                budgets[mapId] = budget;
            }

            if (budget.map != map || budget.tick != now)
            {
                budget.map = map;
                budget.tick = now;
                budget.storageCandidateChecks = 0;
                budget.storageReachQueries = 0;
                budget.pendingReconciles = 0;
                budget.supplyDefChecks = 0;
                budget.supplyThingChecks = 0;
            }

            return budget;
        }

        //预算类型，职责是把不同热路径计数映射到独立字段。
        private enum BudgetKind
        {
            StorageCandidateCheck,
            StorageReachQuery,
            PendingReconcile,
            SupplyDefCheck,
            SupplyThingCheck
        }

        //单张地图当前 tick 的补货预算，职责是记录各类扫描已经消耗的次数。
        private sealed class MapTickBudget
        {
            public Map map;
            public int tick = -1;
            public int storageCandidateChecks;
            public int storageReachQueries;
            public int pendingReconciles;
            public int supplyDefChecks;
            public int supplyThingChecks;
        }
    }
}
