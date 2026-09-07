using SimManagementLib.SimThingClass;
using System;
using System.Collections.Generic;
using Verse;

namespace SimManagementLib.SimMapComp
{
    //地图补货协调器，职责是持有可靠需求、幂等租约并在固定预算内协调两类补货工作。
    public partial class MapComponent_RestockTaskQueue : MapComponent
    {
        private const int QueueRebuildVersion = 2;
        private const int TemporaryRetryTicks = 30;
        private const int MissingSupplyRetryTicks = 60;
        private const int LeasePruneIntervalTicks = 30;
        private const int ReconcileTargetTicks = 120;
        private const int IdleDispatchIntervalTicks = 15;

        private readonly Dictionary<int, Building_SimContainer> storagesById = new Dictionary<int, Building_SimContainer>();
        private readonly List<Building_SimContainer> storageOrder = new List<Building_SimContainer>();
        private readonly Dictionary<RestockTaskKey, RestockTask> requests = new Dictionary<RestockTaskKey, RestockTask>();
        private readonly Queue<RestockTaskKey> dirtyQueue = new Queue<RestockTaskKey>();
        private readonly HashSet<RestockTaskKey> dirtySet = new HashSet<RestockTaskKey>();
        private readonly Queue<int> dirtyConfigurationQueue = new Queue<int>();
        private readonly HashSet<int> dirtyConfigurationSet = new HashSet<int>();
        private readonly RestockReadyQueue bulkDispatchQueue = new RestockReadyQueue();
        private readonly RestockReadyQueue uniqueDispatchQueue = new RestockReadyQueue();
        private readonly Dictionary<RestockTaskKey, int> delayedDispatchTicks = new Dictionary<RestockTaskKey, int>();
        private readonly Dictionary<int, HashSet<RestockTaskKey>> delayedDispatchBuckets = new Dictionary<int, HashSet<RestockTaskKey>>();
        private readonly HashSet<RestockTaskKey> activeRestockCycles = new HashSet<RestockTaskKey>();
        private readonly Dictionary<int, int> assignedUniqueSourceCounts = new Dictionary<int, int>();
        private readonly List<RestockTaskKey> tmpRequestKeys = new List<RestockTaskKey>();
        private readonly HashSet<RestockTaskKey> tmpUniqueKeys = new HashSet<RestockTaskKey>();
        private readonly RestockLeaseRegistry leases = new RestockLeaseRegistry();
        private readonly RestockDispatchBudget budget = new RestockDispatchBudget();
        private readonly RestockStorageConfigurationIndex configurationIndex = new RestockStorageConfigurationIndex();
        private readonly RestockSourceResolver sourceResolver;

        private List<int> savedActiveCycleStorageIds = new List<int>();
        private List<ThingDef> savedActiveCycleThingDefs = new List<ThingDef>();
        private int loadedQueueVersion;
        private int storageReconcileCursor;
        private int idlePawnCursor;
        private int lastLeasePruneTick = int.MinValue;
        private int nextIdleDispatchTick;
        private int lastProcessTick = -1;
        private int lastRebuildTick = -1;
        private string lastProcessReason = "";
        private bool runtimeInitialized;
        private bool dispatchUniqueNext;

        //创建地图补货协调器并绑定当前地图。
        public MapComponent_RestockTaskQueue(Map map) : base(map)
        {
            sourceResolver = new RestockSourceResolver(map);
        }

        //保存普通补货周期锁存，读档后按权威货柜和 Pawn Job 重建易失运行态。
        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                loadedQueueVersion = QueueRebuildVersion;
                CaptureActiveRestockCycles();
            }

            Scribe_Values.Look(ref loadedQueueVersion, "rsmfRestockQueueVersion", 0);
            Scribe_Collections.Look(ref savedActiveCycleStorageIds, "rsmfActiveRestockCycleStorageIds", LookMode.Value);
            Scribe_Collections.Look(ref savedActiveCycleThingDefs, "rsmfActiveRestockCycleThingDefs", LookMode.Def);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                RestoreActiveRestockCycles();
                runtimeInitialized = false;
            }
        }

        //推进需求重算、租约回收、兜底巡检和空闲员工派工。
        public override void MapComponentTick()
        {
            base.MapComponentTick();
            if (map == null)
                return;
            int now = Find.TickManager?.TicksGame ?? 0;
            budget.BeginTick(now);
            EnsureRuntimeInitialized();
            PromoteDueDispatches(now);
            PruneLeasesIfDue(now);
            ProcessOneDirtyConfiguration();
            ReconcileStorageSlice();
            ProcessDirtyQueue(now);
            if (now >= nextIdleDispatchTick)
            {
                nextIdleDispatchTick = now + IdleDispatchIntervalTicks;
                TryDispatchIdlePawns(now);
            }
        }

        //注册货柜并在运行态已建立时立即刷新其需求。
        public void RegisterStorage(Building_SimContainer storage, string reason)
        {
            if (!IsValidStorage(storage))
                return;
            int storageId = storage.thingIDNumber;
            bool added = !storagesById.ContainsKey(storageId);
            storagesById[storageId] = storage;
            if (added)
            {
                storageOrder.Add(storage);
                configurationIndex.Refresh(storage);
            }
            if (runtimeInitialized && added)
                RefreshStorageRequests(storage, reason, true);
        }

        //注销货柜并只移除属于该货柜的请求、周期和租约。
        public void UnregisterStorage(Building_SimContainer storage, string reason)
        {
            if (storage == null)
                return;
            int storageId = storage.thingIDNumber;
            storagesById.Remove(storageId);
            storageOrder.Remove(storage);
            configurationIndex.Remove(storageId);
            dirtyConfigurationSet.Remove(storageId);
            RemoveRequestsForStorage(storageId, true);
            leases.RemoveStorage(storageId, MarkReleasedRequestDirty);
            lastProcessReason = reason ?? "";
            if (storageReconcileCursor > storageOrder.Count)
                storageReconcileCursor = 0;
        }

        //标记整个货柜配置需要重新计算，并清理不再存在的请求。
        public void MarkStorageDirty(Building_SimContainer storage, string reason)
        {
            if (!IsValidStorage(storage))
                return;
            RegisterStorage(storage, reason);
            if (runtimeInitialized)
            {
                configurationIndex.Refresh(storage);
                RefreshStorageRequests(storage, reason, true);
            }
        }

        //标记普通货柜指定商品需求需要重新计算。
        public void MarkDirty(Building_SimContainer storage, ThingDef thingDef, string reason)
        {
            if (!IsValidStorage(storage) || storage is Building_UniqueGoodsContainer || thingDef == null)
                return;
            RegisterStorage(storage, reason);
            if (runtimeInitialized)
            {
                configurationIndex.ObserveInventory(storage);
                EnsureBulkRequest(storage, thingDef, reason);
            }
        }

        //标记专业货柜全部槽位需求需要重新计算。
        public void MarkUniqueStorageDirty(Building_UniqueGoodsContainer storage, string reason)
        {
            if (!IsValidStorage(storage))
                return;
            RegisterStorage(storage, reason);
            if (runtimeInitialized)
            {
                RefreshUniqueRequests(storage, reason, true);
                configurationIndex.ObserveInventory(storage);
            }
        }

        //记录玩家刚指定或运行中已解析出的专业货源。
        public void RememberUniqueSource(Thing source)
        {
            sourceResolver.RememberExactThing(source);
        }

        //把运行时商品目录变化分片传播到已注册普通货柜，避免玩家更新目录时同步重扫全部货柜。
        public void NotifyGoodsCatalogChanged()
        {
            if (!runtimeInitialized)
                return;
            for (int i = 0; i < storageOrder.Count; i++)
            {
                Building_SimContainer storage = storageOrder[i];
                if (!(storage is Building_UniqueGoodsContainer) && IsValidStorage(storage)
                    && dirtyConfigurationSet.Add(storage.thingIDNumber))
                    dirtyConfigurationQueue.Enqueue(storage.thingIDNumber);
            }
        }

        //通过集中编号索引解析专业货源，未命中时至多每 60 tick 合批扫描一次地图。
        public Thing ResolveUniqueSource(int sourceThingId, out bool confirmedDestroyed)
        {
            EnsureRuntimeInitialized();
            int now = Find.TickManager?.TicksGame ?? 0;
            budget.BeginTick(now);
            Thing source = sourceResolver.ResolveExactThing(sourceThingId, false, out confirmedDestroyed);
            if (source != null || confirmedDestroyed || !budget.TryUseDeepSearch(now))
                return source;
            return sourceResolver.ResolveExactThing(sourceThingId, true, out confirmedDestroyed);
        }

        //判断专业货源是否已被任一可靠槽位请求指派。
        public bool IsUniqueSourceAssigned(int sourceThingId)
        {
            if (sourceThingId < 0)
                return false;
            EnsureRuntimeInitialized();
            return assignedUniqueSourceCounts.TryGetValue(sourceThingId, out int count) && count > 0;
        }

        //激活普通商品补货周期，使补货持续到配置目标量。
        public void ActivateRestockCycle(Building_SimContainer storage, ThingDef thingDef)
        {
            if (!IsValidStorage(storage) || storage is Building_UniqueGoodsContainer || thingDef == null)
                return;
            RestockTaskKey key = new RestockTaskKey(storage.thingIDNumber, thingDef);
            activeRestockCycles.Add(key);
            if (runtimeInitialized)
                EnsureBulkRequest(storage, thingDef, "激活补货周期");
        }

        //清空运行态并从当前地图货柜和 Pawn 工作重建全部补货状态。
        public int ResetAndRebuildAll(string reason)
        {
            ClearRuntimeState(true);
            runtimeInitialized = true;
            loadedQueueVersion = QueueRebuildVersion;
            lastRebuildTick = Find.TickManager?.TicksGame ?? 0;
            lastProcessReason = reason ?? "";
            RebuildStorageRegistry();
            RebuildLeasesFromPawnJobs();
            for (int i = 0; i < storageOrder.Count; i++)
                RefreshStorageRequests(storageOrder[i], reason, true);
            return storageOrder.Count;
        }

        //返回指定普通商品当前全部有效在途数量。
        public int CountPending(Building_SimContainer storage, ThingDef thingDef)
        {
            if (storage == null || thingDef == null)
                return 0;
            EnsureRuntimeInitialized();
            return leases.CountPending(storage.thingIDNumber, thingDef);
        }

        //返回指定货柜当前全部普通补货在途数量。
        public int CountTotalPending(Building_SimContainer storage)
        {
            if (storage == null)
                return 0;
            EnsureRuntimeInitialized();
            return leases.CountTotalPending(storage.thingIDNumber);
        }

        //创建按需分配的诊断快照，不让调试对象进入运行热路径。
        public RestockQueueDebugSnapshot CreateDebugSnapshot()
        {
            EnsureRuntimeInitialized();
            int now = Find.TickManager?.TicksGame ?? 0;
            RestockQueueDebugSnapshot snapshot = new RestockQueueDebugSnapshot
            {
                DirtyCount = dirtySet.Count,
                ActiveCycleCount = activeRestockCycles.Count,
                LeaseCount = leases.Count,
                LastProcessTick = lastProcessTick,
                LastRebuildTick = lastRebuildTick,
                LastReason = lastProcessReason ?? "",
                DemandChecksUsed = budget.DemandChecksUsed,
                DispatchAttemptsUsed = budget.DispatchAttemptsUsed,
                IdlePawnChecksUsed = budget.IdlePawnChecksUsed,
                ReachQueriesUsed = budget.ReachQueriesUsed
            };
            foreach (RestockTaskKey key in dirtyQueue)
            {
                if (dirtySet.Contains(key))
                    snapshot.DirtyTasks.Add(key);
            }
            foreach (RestockTask task in requests.Values)
            {
                if (task.Kind == RestockRequestKind.Bulk)
                    snapshot.BulkRequestCount++;
                else
                    snapshot.UniqueRequestCount++;
                snapshot.OldestRequestAge = Math.Max(snapshot.OldestRequestAge, Math.Max(0, now - task.CreatedTick));
                if (task.RetryTick <= now && !leases.HasLease(task.Key) && task.NeededCount > 0)
                    snapshot.ReadyTasks.Add(task);
                else
                    snapshot.BlockedTasks.Add(task);
                string reason = string.IsNullOrEmpty(task.StateReason) ? "未记录" : task.StateReason;
                snapshot.WaitingReasonCounts.TryGetValue(reason, out int reasonCount);
                snapshot.WaitingReasonCounts[reason] = reasonCount + 1;
            }
            snapshot.ReadyCount = snapshot.ReadyTasks.Count;
            snapshot.BlockedCount = snapshot.BlockedTasks.Count;
            foreach (RestockTaskKey key in activeRestockCycles)
                snapshot.ActiveCycles.Add(key);
            return snapshot;
        }
    }
}
