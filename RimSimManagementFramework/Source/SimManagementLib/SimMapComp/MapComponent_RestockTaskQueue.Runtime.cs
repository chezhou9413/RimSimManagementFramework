using RimWorld;
using SimManagementLib.SimThingClass;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimMapComp
{
    //补货运行维护模块，职责是重建租约、回收失效 Job、分片巡检并给真正空闲的 Pawn 派工。
    public partial class MapComponent_RestockTaskQueue
    {
        //清空易失运行态，可按需同时清空普通补货周期锁存。
        private void ClearRuntimeState(bool clearCycles)
        {
            requests.Clear();
            assignedUniqueSourceCounts.Clear();
            dirtyQueue.Clear();
            dirtySet.Clear();
            dirtyConfigurationQueue.Clear();
            dirtyConfigurationSet.Clear();
            bulkDispatchQueue.Clear();
            uniqueDispatchQueue.Clear();
            delayedDispatchTicks.Clear();
            delayedDispatchBuckets.Clear();
            leases.Clear();
            sourceResolver.Clear();
            configurationIndex.Clear();
            budget.Clear();
            tmpRequestKeys.Clear();
            tmpUniqueKeys.Clear();
            storageReconcileCursor = 0;
            idlePawnCursor = 0;
            lastLeasePruneTick = int.MinValue;
            nextIdleDispatchTick = 0;
            dispatchUniqueNext = false;
            if (clearCycles)
                activeRestockCycles.Clear();
        }

        //从当前和排队 Job 重建读档后的租约聚合。
        private void RebuildLeasesFromPawnJobs()
        {
            IReadOnlyList<Pawn> pawns = map?.mapPawns?.AllPawnsSpawned;
            if (pawns == null)
                return;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn?.jobs == null)
                    continue;
                foreach (Job job in pawn.jobs.AllJobs())
                    RestoreLeaseFromJob(pawn, job);
            }
        }

        //从一个补货 Job 的稳定目标字段恢复租约。
        private void RestoreLeaseFromJob(Pawn pawn, Job job)
        {
            if (job?.def?.defName == "DepositToMegaStorage")
            {
                Building_SimContainer storage = job.GetTarget(TargetIndex.B).Thing as Building_SimContainer;
                ThingDef thingDef = job.plantDefToSow
                    ?? job.GetTarget(TargetIndex.A).Thing?.def
                    ?? pawn?.carryTracker?.CarriedThing?.def;
                if (!IsValidStorage(storage) || storage is Building_UniqueGoodsContainer || thingDef == null)
                    return;
                RestockTaskKey key = new RestockTaskKey(storage.thingIDNumber, thingDef);
                activeRestockCycles.Add(key);
                int available = CalculateBulkAvailable(storage, thingDef);
                leases.TryAcquire(pawn, job, key, Math.Max(1, job.count), available, out _);
                return;
            }
            if (job?.def?.defName != "RestockUniqueGoodsContainer")
                return;
            Building_UniqueGoodsContainer uniqueStorage = job.GetTarget(TargetIndex.B).Thing as Building_UniqueGoodsContainer;
            Thing source = job.GetTarget(TargetIndex.A).Thing ?? pawn?.carryTracker?.CarriedThing;
            if (!IsValidStorage(uniqueStorage)
                || source == null
                || !uniqueStorage.TryGetPendingSlotIndex(source.thingIDNumber, out int slotIndex))
                return;
            sourceResolver.RememberExactThing(source);
            RestockTaskKey keyUnique = RestockTaskKey.ForUnique(uniqueStorage.thingIDNumber, slotIndex, source.thingIDNumber);
            leases.TryAcquire(pawn, job, keyUnique, 1, leases.HasLease(keyUnique) ? 0 : 1, out _);
        }

        //定期回收已经离开 Pawn 当前和排队工作链的租约。
        private void PruneLeasesIfDue(int now)
        {
            if (lastLeasePruneTick != int.MinValue && now - lastLeasePruneTick < LeasePruneIntervalTicks)
                return;
            lastLeasePruneTick = now;
            leases.PruneStaleLeases(key => MarkReleasedRequestDirty(key, "失效补货租约回收"));
        }

        //把无原因的租约变化重新转换成对应需求通知。
        private void MarkReleasedRequestDirty(RestockTaskKey key)
        {
            MarkReleasedRequestDirty(key, "补货租约变化");
        }

        //把带原因的租约变化重新转换成对应需求通知。
        private void MarkReleasedRequestDirty(RestockTaskKey key, string reason)
        {
            if (requests.ContainsKey(key))
            {
                MarkRequestDirty(key, reason);
                return;
            }
            if (!storagesById.TryGetValue(key.StorageId, out Building_SimContainer storage))
                return;
            if (key.Kind == RestockRequestKind.Bulk)
                EnsureBulkRequest(storage, key.ThingDef, reason);
            else if (storage is Building_UniqueGoodsContainer uniqueStorage
                && uniqueStorage.IsPendingSlotMatch(key.SlotIndex, key.SourceThingId))
                EnsureUniqueRequest(uniqueStorage, key.SlotIndex, key.SourceThingId, reason);
        }

        //在固定 120 tick 周期内动态分片巡检全部已注册货柜。
        private void ReconcileStorageSlice()
        {
            if (storageOrder.Count <= 0)
                return;
            int slice = Math.Max(1, (storageOrder.Count + ReconcileTargetTicks - 1) / ReconcileTargetTicks);
            for (int i = 0; i < slice && storageOrder.Count > 0; i++)
            {
                if (storageReconcileCursor >= storageOrder.Count)
                    storageReconcileCursor = 0;
                Building_SimContainer storage = storageOrder[storageReconcileCursor++];
                if (!IsValidStorage(storage))
                    continue;
                if (!configurationIndex.HasUnobservedInventoryChange(storage))
                    continue;

                //库存精确通知若被外部模组绕过，只在版本变化时补做一次权威重算。
                RefreshStorageRequests(storage, "补货库存版本兜底巡检", false);
            }
        }

        //每 tick 最多刷新一个因商品目录变化而失效的货柜配置快照。
        private void ProcessOneDirtyConfiguration()
        {
            int checkedCount = 0;
            while (dirtyConfigurationQueue.Count > 0 && checkedCount++ < 8)
            {
                int storageId = dirtyConfigurationQueue.Dequeue();
                if (!dirtyConfigurationSet.Remove(storageId)
                    || !storagesById.TryGetValue(storageId, out Building_SimContainer storage)
                    || !IsValidStorage(storage)
                    || storage is Building_UniqueGoodsContainer)
                    continue;
                configurationIndex.Refresh(storage);
                RefreshStorageRequests(storage, "商品目录变化", true);
                return;
            }
        }

        //按 Pawn 游标给真正空闲的殖民者和玩家机械体立即派发补货。
        private void TryDispatchIdlePawns(int now)
        {
            if (CountReadyDispatches() <= 0 || map?.mapPawns == null)
                return;
            List<Pawn> colonists = map.mapPawns.FreeColonists;
            List<Pawn> mechs = map.mapPawns.SpawnedColonyMechs;
            int total = (colonists?.Count ?? 0) + (mechs?.Count ?? 0);
            if (total <= 0)
                return;

            int checkedCount = 0;
            while (checkedCount < total && budget.TryUseIdlePawnCheck())
            {
                if (idlePawnCursor >= total)
                    idlePawnCursor = 0;
                Pawn pawn = GetPawnAtCombinedIndex(colonists, mechs, idlePawnCursor++);
                checkedCount++;
                if (!IsTrulyIdle(pawn))
                    continue;
                Job job = TryMakeJobForPawn(pawn, null);
                if (job == null)
                    continue;
                try
                {
                    pawn.jobs.StartJob(
                        job,
                        JobCondition.InterruptForced,
                        null,
                        resumeCurJobAfterwards: false,
                        cancelBusyStances: true,
                        thinkTree: null,
                        tag: JobTag.MiscWork,
                        fromQueue: false,
                        canReturnCurJobToPool: false,
                        keepCarryingThingOverride: null,
                        continueSleeping: false,
                        addToJobsThisTick: true,
                        preToilReservationsCanFail: true);
                }
                catch (Exception exception)
                {
                    ReleaseLease(job, "主动派工异常");
                    Log.Error($"补货主动派工失败：Pawn={pawn?.LabelShort ?? "无"}，Job={job?.def?.defName ?? "无"}，异常={exception}");
                }
            }
        }

        //从殖民者与玩家机械体组合列表读取指定索引。
        private static Pawn GetPawnAtCombinedIndex(List<Pawn> colonists, List<Pawn> mechs, int index)
        {
            int colonistCount = colonists?.Count ?? 0;
            if (index < colonistCount)
                return colonists[index];
            int mechIndex = index - colonistCount;
            return mechs != null && mechIndex >= 0 && mechIndex < mechs.Count ? mechs[mechIndex] : null;
        }

        //判断 Pawn 是否没有排队工作且当前只处于可安全打断的空闲状态。
        private static bool IsTrulyIdle(Pawn pawn)
        {
            if (pawn == null || pawn.jobs == null || pawn.Drafted || pawn.Downed || pawn.InMentalState)
                return false;
            if (pawn.jobs.jobQueue != null && pawn.jobs.jobQueue.Count > 0)
                return false;
            if (pawn.InBed() || pawn.jobs.curDriver?.asleep == true)
                return false;
            return pawn.CurJob == null || pawn.mindState?.IsIdle == true || pawn.CurJob.def?.isIdle == true;
        }

        //保存当前普通补货周期锁存。
        private void CaptureActiveRestockCycles()
        {
            savedActiveCycleStorageIds.Clear();
            savedActiveCycleThingDefs.Clear();
            foreach (RestockTaskKey key in activeRestockCycles)
            {
                if (key.Kind != RestockRequestKind.Bulk || key.StorageId < 0 || key.ThingDef == null)
                    continue;
                savedActiveCycleStorageIds.Add(key.StorageId);
                savedActiveCycleThingDefs.Add(key.ThingDef);
            }
        }

        //恢复保存的普通补货周期锁存。
        private void RestoreActiveRestockCycles()
        {
            activeRestockCycles.Clear();
            if (savedActiveCycleStorageIds == null)
                savedActiveCycleStorageIds = new List<int>();
            if (savedActiveCycleThingDefs == null)
                savedActiveCycleThingDefs = new List<ThingDef>();
            int count = Math.Min(savedActiveCycleStorageIds.Count, savedActiveCycleThingDefs.Count);
            for (int i = 0; i < count; i++)
            {
                ThingDef thingDef = savedActiveCycleThingDefs[i];
                if (savedActiveCycleStorageIds[i] >= 0 && thingDef != null)
                    activeRestockCycles.Add(new RestockTaskKey(savedActiveCycleStorageIds[i], thingDef));
            }
        }
    }
}
