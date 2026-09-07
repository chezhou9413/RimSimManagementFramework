using Verse;

namespace SimManagementLib.SimMapComp
{
    //补货调度预算，职责是限制单张地图每个 tick 的需求计算、派工和可达查询次数。
    internal sealed class RestockDispatchBudget
    {
        private const int DemandChecksPerTick = 8;
        private const int DispatchAttemptsPerTick = 4;
        private const int IdlePawnChecksPerTick = 4;
        private const int ReachQueriesPerTick = 4;
        private const int DeepSearchIntervalTicks = 60;

        private int tick = -1;
        private int demandChecks;
        private int dispatchAttempts;
        private int idlePawnChecks;
        private int reachQueries;
        private int lastDeepSearchTick = int.MinValue;

        public int DemandChecksUsed => demandChecks;
        public int DispatchAttemptsUsed => dispatchAttempts;
        public int IdlePawnChecksUsed => idlePawnChecks;
        public int ReachQueriesUsed => reachQueries;

        //进入指定 tick 并重置本 tick 计数。
        public void BeginTick(int now)
        {
            if (tick == now)
                return;
            tick = now;
            demandChecks = 0;
            dispatchAttempts = 0;
            idlePawnChecks = 0;
            reachQueries = 0;
        }

        //尝试消耗一次需求重算预算。
        public bool TryUseDemandCheck()
        {
            if (demandChecks >= DemandChecksPerTick)
                return false;
            demandChecks++;
            return true;
        }

        //尝试消耗一次派工候选预算。
        public bool TryUseDispatchAttempt()
        {
            if (dispatchAttempts >= DispatchAttemptsPerTick)
                return false;
            dispatchAttempts++;
            return true;
        }

        //尝试消耗一次空闲员工检查预算。
        public bool TryUseIdlePawnCheck()
        {
            if (idlePawnChecks >= IdlePawnChecksPerTick)
                return false;
            idlePawnChecks++;
            return true;
        }

        //尝试消耗一次原版可达或区域搜索预算。
        public bool TryUseReachQuery()
        {
            if (reachQueries >= ReachQueriesPerTick)
                return false;
            reachQueries++;
            return true;
        }

        //尝试取得低频深度货源搜索权。
        public bool TryUseDeepSearch(int now)
        {
            if (lastDeepSearchTick != int.MinValue && now - lastDeepSearchTick < DeepSearchIntervalTicks)
                return false;
            lastDeepSearchTick = now;
            return true;
        }

        //清空全部预算状态。
        public void Clear()
        {
            tick = -1;
            lastDeepSearchTick = int.MinValue;
            demandChecks = 0;
            dispatchAttempts = 0;
            idlePawnChecks = 0;
            reachQueries = 0;
        }
    }
}
