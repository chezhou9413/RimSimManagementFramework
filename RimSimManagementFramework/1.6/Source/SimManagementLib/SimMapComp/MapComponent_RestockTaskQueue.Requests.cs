using SimManagementLib.SimThingClass;
using System.Collections.Generic;
using Verse;

namespace SimManagementLib.SimMapComp
{
    //补货请求管理模块，职责是从货柜权威状态建立、重算和清理持久需求。
    public partial class MapComponent_RestockTaskQueue
    {
        //确保地图读档或新建后的运行态只重建一次。
        private void EnsureRuntimeInitialized()
        {
            if (runtimeInitialized)
                return;
            runtimeInitialized = true;
            ClearRuntimeState(false);
            RebuildStorageRegistry();
            RebuildLeasesFromPawnJobs();
            for (int i = 0; i < storageOrder.Count; i++)
                RefreshStorageRequests(storageOrder[i], "补货运行态重建", true);
            loadedQueueVersion = QueueRebuildVersion;
            lastRebuildTick = Find.TickManager?.TicksGame ?? 0;
        }

        //从原版殖民地建筑列表一次性建立货柜注册表。
        private void RebuildStorageRegistry()
        {
            storagesById.Clear();
            storageOrder.Clear();
            configurationIndex.Clear();
            List<Building> buildings = map?.listerBuildings?.allBuildingsColonist;
            if (buildings == null)
                return;
            for (int i = 0; i < buildings.Count; i++)
            {
                if (buildings[i] is Building_SimContainer storage && IsValidStorage(storage))
                {
                    storagesById[storage.thingIDNumber] = storage;
                    storageOrder.Add(storage);
                    configurationIndex.Refresh(storage);
                }
            }
        }

        //刷新指定货柜的普通或专业请求，并按需清理失效配置。
        private void RefreshStorageRequests(Building_SimContainer storage, string reason, bool cleanupMissing)
        {
            if (storage is Building_UniqueGoodsContainer uniqueStorage)
            {
                RefreshUniqueRequests(uniqueStorage, reason, cleanupMissing);
                configurationIndex.ObserveInventory(storage);
                return;
            }
            IReadOnlyList<RestockTargetSettings> items = configurationIndex.GetItems(storage.thingIDNumber);
            for (int i = 0; i < items.Count; i++)
                EnsureBulkRequest(storage, items[i].ThingDef, reason);
            if (cleanupMissing)
                CleanupMissingBulkRequests(storage);
            configurationIndex.ObserveInventory(storage);
        }

        //确保普通商品在满足阈值或已启动周期时拥有可靠请求。
        private void EnsureBulkRequest(Building_SimContainer storage, ThingDef thingDef, string reason)
        {
            RestockTaskKey key = new RestockTaskKey(storage.thingIDNumber, thingDef);
            if (!configurationIndex.TryGetSettings(storage.thingIDNumber, thingDef, out RestockTargetSettings settings))
            {
                activeRestockCycles.Remove(key);
                RemoveRequest(key);
                return;
            }
            int target = settings.TargetCount;
            int stored = storage.CountStored(thingDef);
            int pending = leases.CountPending(storage.thingIDNumber, thingDef);
            if (target <= 0 || stored >= target)
            {
                activeRestockCycles.Remove(key);
                RemoveRequest(key);
                return;
            }
            if (!activeRestockCycles.Contains(key))
            {
                if (stored + pending > settings.Threshold)
                {
                    RemoveRequest(key);
                    return;
                }
                activeRestockCycles.Add(key);
            }
            if (!requests.ContainsKey(key))
                requests.Add(key, new RestockTask(storage, thingDef, Find.TickManager?.TicksGame ?? 0));
            MarkRequestDirty(key, reason);
        }

        //刷新专业货柜全部待搬运槽位并清理已经完成或取消的请求。
        private void RefreshUniqueRequests(Building_UniqueGoodsContainer storage, string reason, bool cleanupMissing)
        {
            tmpUniqueKeys.Clear();
            IReadOnlyList<Pojo.UniqueGoodsSlotData> slots = storage.UniqueSlots;
            for (int i = 0; i < slots.Count; i++)
            {
                Pojo.UniqueGoodsSlotData slot = slots[i];
                if (slot == null || !slot.HasPendingSource)
                    continue;
                RestockTaskKey key = RestockTaskKey.ForUnique(storage.thingIDNumber, i, slot.pendingSourceThingId);
                tmpUniqueKeys.Add(key);
                EnsureUniqueRequest(storage, i, slot.pendingSourceThingId, reason);
            }
            if (!cleanupMissing)
                return;
            tmpRequestKeys.Clear();
            foreach (RestockTaskKey key in requests.Keys)
            {
                if (key.Kind == RestockRequestKind.Unique
                    && key.StorageId == storage.thingIDNumber
                    && !tmpUniqueKeys.Contains(key))
                    tmpRequestKeys.Add(key);
            }
            for (int i = 0; i < tmpRequestKeys.Count; i++)
                RemoveRequest(tmpRequestKeys[i]);
            tmpRequestKeys.Clear();
        }

        //确保专业槽位拥有一个可长期重试的精确请求。
        private void EnsureUniqueRequest(Building_UniqueGoodsContainer storage, int slotIndex, int sourceThingId, string reason)
        {
            RestockTaskKey key = RestockTaskKey.ForUnique(storage.thingIDNumber, slotIndex, sourceThingId);
            if (!requests.ContainsKey(key))
            {
                requests.Add(key, new RestockTask(storage, slotIndex, sourceThingId, Find.TickManager?.TicksGame ?? 0));
                assignedUniqueSourceCounts.TryGetValue(sourceThingId, out int assignedCount);
                assignedUniqueSourceCounts[sourceThingId] = assignedCount + 1;
            }
            MarkRequestDirty(key, reason);
        }

        //清理普通货柜配置中已经不存在的商品请求和周期。
        private void CleanupMissingBulkRequests(Building_SimContainer storage)
        {
            tmpRequestKeys.Clear();
            foreach (RestockTaskKey key in requests.Keys)
            {
                if (key.Kind == RestockRequestKind.Bulk
                    && key.StorageId == storage.thingIDNumber
                    && !configurationIndex.TryGetSettings(storage.thingIDNumber, key.ThingDef, out _))
                    tmpRequestKeys.Add(key);
            }
            for (int i = 0; i < tmpRequestKeys.Count; i++)
            {
                activeRestockCycles.Remove(tmpRequestKeys[i]);
                RemoveRequest(tmpRequestKeys[i]);
            }
            tmpRequestKeys.Clear();
        }

        //把请求加入去重脏队列。
        private void MarkRequestDirty(RestockTaskKey key, string reason)
        {
            if (!requests.ContainsKey(key))
                return;
            if (dirtySet.Add(key))
                dirtyQueue.Enqueue(key);
            lastProcessReason = reason ?? "";
        }

        //按固定预算重新计算脏请求并放入公平派工队列。
        private void ProcessDirtyQueue(int now)
        {
            int processed = 0;
            while (dirtyQueue.Count > 0 && budget.TryUseDemandCheck())
            {
                RestockTaskKey key = dirtyQueue.Dequeue();
                if (!dirtySet.Remove(key) || !requests.TryGetValue(key, out RestockTask task))
                    continue;
                processed++;
                EvaluateRequest(task, now);
            }
            if (processed > 0)
                lastProcessTick = now;
        }

        //根据权威库存、槽位和租约状态更新一个请求。
        private void EvaluateRequest(RestockTask task, int now)
        {
            if (!storagesById.TryGetValue(task.StorageId, out Building_SimContainer storage) || !IsValidStorage(storage))
            {
                RemoveRequest(task.Key);
                return;
            }
            task.LastCheckedTick = now;
            if (task.Kind == RestockRequestKind.Bulk)
            {
                if (!configurationIndex.TryGetSettings(task.StorageId, task.ThingDef, out RestockTargetSettings settings))
                {
                    activeRestockCycles.Remove(task.Key);
                    RemoveRequest(task.Key);
                    return;
                }
                int stored = storage.CountStored(task.ThingDef);
                if (settings.TargetCount <= 0 || stored >= settings.TargetCount)
                {
                    activeRestockCycles.Remove(task.Key);
                    RemoveRequest(task.Key);
                    return;
                }
                int available = CalculateBulkAvailable(storage, task.ThingDef, settings.TargetCount, stored);
                task.NeededCount = available;
                task.StateReason = available > 0 ? "等待派工" : "等待途中补货";
                task.RetryTick = available > 0 ? now : now + TemporaryRetryTicks;
                ScheduleDispatch(task.Key, task.RetryTick, now);
                return;
            }
            if (!(storage is Building_UniqueGoodsContainer uniqueStorage)
                || !uniqueStorage.IsPendingSlotMatch(task.SlotIndex, task.SourceThingId))
            {
                RemoveRequest(task.Key);
                return;
            }
            task.NeededCount = 1;
            task.StateReason = leases.HasLease(task.Key) ? "专业补货任务执行中" : "等待专业补货派工";
            task.RetryTick = leases.HasLease(task.Key) ? now + TemporaryRetryTicks : now;
            ScheduleDispatch(task.Key, task.RetryTick, now);
        }

        //移除一个请求，队列中的旧键由去重集合自动忽略。
        private void RemoveRequest(RestockTaskKey key)
        {
            bool removed = requests.Remove(key);
            if (removed && key.Kind == RestockRequestKind.Unique)
            {
                assignedUniqueSourceCounts.TryGetValue(key.SourceThingId, out int assignedCount);
                int next = assignedCount - 1;
                if (next <= 0)
                    assignedUniqueSourceCounts.Remove(key.SourceThingId);
                else
                    assignedUniqueSourceCounts[key.SourceThingId] = next;
            }
            dirtySet.Remove(key);
            RemoveReadyDispatch(key);
            RemoveDelayedDispatch(key);
        }

        //移除指定货柜的全部请求，并按需清理普通补货周期。
        private void RemoveRequestsForStorage(int storageId, bool removeCycles)
        {
            tmpRequestKeys.Clear();
            foreach (RestockTaskKey key in requests.Keys)
            {
                if (key.StorageId == storageId)
                    tmpRequestKeys.Add(key);
            }
            for (int i = 0; i < tmpRequestKeys.Count; i++)
                RemoveRequest(tmpRequestKeys[i]);
            tmpRequestKeys.Clear();
            if (!removeCycles)
                return;
            foreach (RestockTaskKey key in activeRestockCycles)
            {
                if (key.StorageId == storageId)
                    tmpRequestKeys.Add(key);
            }
            for (int i = 0; i < tmpRequestKeys.Count; i++)
                activeRestockCycles.Remove(tmpRequestKeys[i]);
            tmpRequestKeys.Clear();
        }

        //判断货柜是否仍属于当前地图并可参与补货。
        private bool IsValidStorage(Building_SimContainer storage)
        {
            return storage != null && !storage.Destroyed && storage.Spawned && storage.Map == map;
        }
    }
}
