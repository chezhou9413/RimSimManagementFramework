using System.Collections.Generic;

namespace SimManagementLib.SimMapComp
{
    //补货协调器诊断快照，职责是把请求、租约和预算状态安全交给调试界面。
    public sealed class RestockQueueDebugSnapshot
    {
        public int DirtyCount;
        public int ReadyCount;
        public int BlockedCount;
        public int ActiveCycleCount;
        public int BulkRequestCount;
        public int UniqueRequestCount;
        public int LeaseCount;
        public int OldestRequestAge;
        public int DemandChecksUsed;
        public int DispatchAttemptsUsed;
        public int IdlePawnChecksUsed;
        public int ReachQueriesUsed;
        public int LastProcessTick;
        public int LastRebuildTick;
        public string LastReason;
        public List<RestockTaskKey> DirtyTasks = new List<RestockTaskKey>();
        public List<RestockTask> ReadyTasks = new List<RestockTask>();
        public List<RestockTask> BlockedTasks = new List<RestockTask>();
        public List<RestockTaskKey> ActiveCycles = new List<RestockTaskKey>();
        public Dictionary<string, int> WaitingReasonCounts = new Dictionary<string, int>();
    }
}
