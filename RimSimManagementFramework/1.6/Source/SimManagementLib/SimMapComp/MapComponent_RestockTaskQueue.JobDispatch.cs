using RimWorld;
using SimManagementLib.SimDef;
using SimManagementLib.SimThingClass;
using SimManagementLib.Tool;
using System;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimMapComp
{
    //补货 Job 构建模块，职责是从可靠请求按预算选择货源、校验 Pawn 并创建兼容 Job 形状。
    public partial class MapComponent_RestockTaskQueue
    {
        //为普通补货 WorkGiver 从可靠请求队列创建 Job。
        public Job TryMakeJobForPawn(Pawn pawn)
        {
            return TryMakeJobForPawn(pawn, RestockRequestKind.Bulk);
        }

        //为指定 Pawn 创建限定类型或任意类型的补货 Job。
        public Job TryMakeJobForPawn(Pawn pawn, RestockRequestKind? kind)
        {
            if (pawn?.Map != map)
                return null;
            EnsureRuntimeInitialized();
            int now = Find.TickManager?.TicksGame ?? 0;
            budget.BeginTick(now);
            ProcessDirtyQueue(now);
            PromoteDueDispatches(now);
            int scanCount = kind == RestockRequestKind.Bulk
                ? bulkDispatchQueue.Count
                : kind == RestockRequestKind.Unique
                    ? uniqueDispatchQueue.Count
                    : bulkDispatchQueue.Count + uniqueDispatchQueue.Count;
            for (int i = 0; i < scanCount; i++)
            {
                if (!TryDequeueReadyDispatch(kind, out RestockTaskKey key))
                    break;
                if (!requests.TryGetValue(key, out RestockTask task))
                    continue;
                if (task.RetryTick > now)
                {
                    ScheduleDispatch(key, task.RetryTick, now);
                    continue;
                }
                if (!budget.TryUseDispatchAttempt())
                {
                    EnqueueReadyDispatch(key);
                    break;
                }

                Job job = TryBuildJob(pawn, task, now, false);
                if (requests.ContainsKey(key))
                    ScheduleDispatch(key, task.RetryTick, now);
                if (job != null)
                    return job;
            }
            return null;
        }

        //为手动调用在指定普通货柜上确定性创建补货 Job。
        public Job TryMakeBulkJobForStorage(Pawn pawn, Building_SimContainer storage, bool forced)
        {
            if (!IsValidStorage(storage) || storage is Building_UniqueGoodsContainer || pawn?.Map != map)
                return null;
            EnsureRuntimeInitialized();
            if (!CanPawnUseRequest(pawn, storage, RestockRequestKind.Bulk)
                || !pawn.CanReach(storage.InventoryInteractionTarget, storage.InventoryInteractionEndMode, Danger.Deadly))
            {
                if (forced)
                    JobFailReason.Is("RSMF.Restock.Forced.NoAccess".Translate());
                return null;
            }
            bool hasShortfall = false;
            foreach (ThingDef thingDef in storage.ActiveDefs)
            {
                if (forced)
                    activeRestockCycles.Add(new RestockTaskKey(storage.thingIDNumber, thingDef));
                EnsureBulkRequest(storage, thingDef, forced ? "强制补货" : "手动补货检查");
                RestockTaskKey key = new RestockTaskKey(storage.thingIDNumber, thingDef);
                if (!requests.TryGetValue(key, out RestockTask task) || CalculateBulkAvailable(storage, thingDef) <= 0)
                    continue;
                hasShortfall = true;
                int now = Find.TickManager?.TicksGame ?? 0;
                Job job = TryBuildJob(pawn, task, now, true);
                ScheduleDispatch(key, task.RetryTick, now);
                if (job != null)
                    return job;
            }
            if (forced)
            {
                JobFailReason.Is((hasShortfall
                    ? "RSMF.Restock.Forced.NoSupply"
                    : "RSMF.Restock.Forced.AtTarget").Translate());
            }
            return null;
        }

        //为手动调用在指定专业货柜上确定性创建补货 Job。
        public Job TryMakeUniqueJobForStorage(Pawn pawn, Building_UniqueGoodsContainer storage)
        {
            if (!IsValidStorage(storage) || pawn?.Map != map)
                return null;
            EnsureRuntimeInitialized();
            RefreshUniqueRequests(storage, "专业货柜手动检查", false);
            System.Collections.Generic.IReadOnlyList<Pojo.UniqueGoodsSlotData> slots = storage.UniqueSlots;
            for (int i = 0; i < slots.Count; i++)
            {
                Pojo.UniqueGoodsSlotData slot = slots[i];
                if (slot == null || !slot.HasPendingSource)
                    continue;
                RestockTaskKey key = RestockTaskKey.ForUnique(storage.thingIDNumber, i, slot.pendingSourceThingId);
                if (requests.TryGetValue(key, out RestockTask task))
                {
                    int now = Find.TickManager?.TicksGame ?? 0;
                    Job job = TryBuildJob(pawn, task, now, true);
                    ScheduleDispatch(key, task.RetryTick, now);
                    if (job != null)
                        return job;
                }
            }
            return null;
        }

        //尝试把一个可靠请求转换成具体 Job。
        private Job TryBuildJob(Pawn pawn, RestockTask task, int now, bool ignoreBudget)
        {
            if (!storagesById.TryGetValue(task.StorageId, out Building_SimContainer storage)
                || !CanPawnUseRequest(pawn, storage, task.Kind))
            {
                task.StateReason = SimTranslation.T("RSMF.RestockReason.StaffNotAllowed");
                task.RetryTick = now + TemporaryRetryTicks;
                return null;
            }
            if (task.Kind == RestockRequestKind.Bulk
                && !sourceResolver.HasPotentialBulkSupply(storage, task.ThingDef))
            {
                task.SupplyId = -1;
                task.StateReason = storage.RestockSourceIssue.NullOrEmpty() ? SimTranslation.T("RSMF.RestockReason.LinkedSourceEmpty") : storage.RestockSourceIssue;
                task.RetryTick = now + MissingSupplyRetryTicks;
                return null;
            }
            if (!ignoreBudget && !budget.TryUseReachQuery())
            {
                task.StateReason = SimTranslation.T("RSMF.RestockReason.WaitCabinetReach");
                task.RetryTick = now + TemporaryRetryTicks;
                return null;
            }
            if (!pawn.CanReach(storage.InventoryInteractionTarget, storage.InventoryInteractionEndMode, Danger.Deadly))
            {
                task.StateReason = SimTranslation.T("RSMF.RestockReason.CabinetUnreachable");
                task.RetryTick = now + TemporaryRetryTicks;
                return null;
            }
            return task.Kind == RestockRequestKind.Unique
                ? TryBuildUniqueJob(pawn, storage as Building_UniqueGoodsContainer, task, now, ignoreBudget)
                : TryBuildBulkJob(pawn, storage, task, now, ignoreBudget);
        }

        //创建普通补货 Job，货源搜索使用原版区域搜索并受地图预算限制。
        private Job TryBuildBulkJob(Pawn pawn, Building_SimContainer storage, RestockTask task, int now, bool ignoreBudget)
        {
            int needed = CalculateBulkAvailable(storage, task.ThingDef);
            if (needed <= 0)
            {
                task.NeededCount = 0;
                task.StateReason = SimTranslation.T("RSMF.RestockReason.InTransit");
                task.RetryTick = now + TemporaryRetryTicks;
                return null;
            }
            if (!ignoreBudget && !budget.TryUseReachQuery())
            {
                task.StateReason = SimTranslation.T("RSMF.RestockReason.WaitSourceSearch");
                task.RetryTick = now + TemporaryRetryTicks;
                return null;
            }

            Thing supply = sourceResolver.FindBulkSupply(pawn, storage, task.ThingDef, false);
            bool deepSearched = false;
            if (supply == null
                && (ignoreBudget || budget.TryUseReachQuery())
                && budget.TryUseDeepSearch(now))
            {
                deepSearched = true;
                supply = sourceResolver.FindBulkSupply(pawn, storage, task.ThingDef, true);
            }
            if (supply == null)
            {
                task.SupplyId = -1;
                task.StateReason = deepSearched ? SimTranslation.T("RSMF.RestockReason.BulkSourceUnavailable") : SimTranslation.T("RSMF.RestockReason.WaitDeepSearch");
                task.RetryTick = now + MissingSupplyRetryTicks;
                return null;
            }

            int carryMax = MassUtility.CountToPickUpUntilOverEncumbered(pawn, supply);
            int amount = Math.Min(needed, Math.Min(carryMax, map.GetComponent<MapComponent_InventoryReservations>().Available(supply)));
            if (amount <= 0)
            {
                task.StateReason = SimTranslation.T("RSMF.RestockReason.NoCarryCapacity");
                task.RetryTick = now + TemporaryRetryTicks;
                return null;
            }
            Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("DepositToMegaStorage"), supply, storage);
            job.count = amount;
            job.haulMode = HaulMode.ToCellStorage;
            job.plantDefToSow = task.ThingDef;
            job.workGiverDef = GetWorkGiverDef(RestockRequestKind.Bulk);
            task.SupplyId = supply.thingIDNumber;
            task.StateReason = SimTranslation.T("RSMF.RestockReason.JobCreated");
            task.RetryTick = now + 1;
            return job;
        }

        //创建专业补货 Job，来源暂时不可用时始终保留玩家指派和请求。
        private Job TryBuildUniqueJob(Pawn pawn, Building_UniqueGoodsContainer storage, RestockTask task, int now, bool ignoreBudget)
        {
            if (storage == null || !storage.IsPendingSlotMatch(task.SlotIndex, task.SourceThingId))
            {
                RemoveRequest(task.Key);
                return null;
            }
            if (leases.HasLease(task.Key))
            {
                task.StateReason = SimTranslation.T("RSMF.RestockReason.UniqueInProgress");
                task.RetryTick = now + TemporaryRetryTicks;
                return null;
            }

            Thing source = sourceResolver.ResolveExactThing(task.SourceThingId, false, out bool confirmedDestroyed);
            bool deepSearched = false;
            if (source == null && !confirmedDestroyed && budget.TryUseDeepSearch(now))
            {
                deepSearched = true;
                source = sourceResolver.ResolveExactThing(task.SourceThingId, true, out confirmedDestroyed);
            }
            if (confirmedDestroyed)
            {
                storage.ClearDestroyedPendingSource(task.SlotIndex, task.SourceThingId);
                RemoveRequest(task.Key);
                return null;
            }
            if (source == null || !source.Spawned || source.Map != map)
            {
                task.StateReason = deepSearched ? SimTranslation.T("RSMF.RestockReason.UniqueNotSpawned") : SimTranslation.T("RSMF.RestockReason.WaitUniqueIndex");
                task.RetryTick = now + MissingSupplyRetryTicks;
                return null;
            }
            if (!storage.IsEligiblePendingSource(task.SlotIndex, source))
            {
                task.StateReason = SimTranslation.T("RSMF.RestockReason.UniqueIneligible");
                task.RetryTick = now + MissingSupplyRetryTicks;
                return null;
            }
            if (source.IsForbidden(pawn) || !pawn.CanReserve(source))
            {
                task.StateReason = SimTranslation.T("RSMF.RestockReason.UniqueReserved");
                task.RetryTick = now + TemporaryRetryTicks;
                return null;
            }
            if (!ignoreBudget && !budget.TryUseReachQuery())
            {
                task.StateReason = SimTranslation.T("RSMF.RestockReason.WaitUniqueReach");
                task.RetryTick = now + TemporaryRetryTicks;
                return null;
            }
            if (!pawn.CanReach(source, PathEndMode.ClosestTouch, Danger.Deadly))
            {
                task.StateReason = SimTranslation.T("RSMF.RestockReason.UniqueUnreachable");
                task.RetryTick = now + TemporaryRetryTicks;
                return null;
            }

            Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("RestockUniqueGoodsContainer"), source, storage);
            job.count = 1;
            job.haulMode = HaulMode.ToCellNonStorage;
            job.workGiverDef = GetWorkGiverDef(RestockRequestKind.Unique);
            task.SupplyId = source.thingIDNumber;
            task.StateReason = SimTranslation.T("RSMF.RestockReason.UniqueJobCreated");
            task.RetryTick = now + 1;
            return job;
        }

        //判断 Pawn 是否具备指定补货工作的能力、岗位权限和地图状态。
        private static bool CanPawnUseRequest(Pawn pawn, Building_SimContainer storage, RestockRequestKind kind)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned || pawn.Downed || pawn.InMentalState || pawn.Drafted)
                return false;
            if (storage == null || storage.Destroyed || !storage.Spawned || storage.Map != pawn.Map)
                return false;
            WorkGiverDef workGiverDef = GetWorkGiverDef(kind);
            if (workGiverDef == null)
                return false;
            ShopStaffRoleDef role = DefDatabase<ShopStaffRoleDef>.GetNamedSilentFail("SimShopRole_Restocker");
            WorkTypeDef requiredWorkType = role?.requiredWorkType ?? workGiverDef.workType;
            if (!ShopStaffUtility.CanPawnPerformWorkType(pawn, requiredWorkType, true))
                return false;
            if (workGiverDef.requiredCapacities != null)
            {
                for (int i = 0; i < workGiverDef.requiredCapacities.Count; i++)
                {
                    PawnCapacityDef capacity = workGiverDef.requiredCapacities[i];
                    if (capacity != null && pawn.health?.capacities?.CapableOf(capacity) == false)
                        return false;
                }
            }
            return VendingMachineUtility.IsVendingMachine(storage)
                || ShopStaffUtility.AllowsPawnForWorkGiver(ShopStaffUtility.FindShopFor(storage), pawn, workGiverDef);
        }

        //返回请求类型对应的 WorkGiverDef。
        private static WorkGiverDef GetWorkGiverDef(RestockRequestKind kind)
        {
            string defName = kind == RestockRequestKind.Unique ? "RestockUniqueGoodsContainer" : "RestockMegaStorage";
            return DefDatabase<WorkGiverDef>.GetNamedSilentFail(defName);
        }

        //计算普通商品扣除全部有效租约后的可派发数量。
        private int CalculateBulkAvailable(Building_SimContainer storage, ThingDef thingDef)
        {
            if (!IsValidStorage(storage) || thingDef == null)
                return 0;
            if (!configurationIndex.TryGetSettings(storage.thingIDNumber, thingDef, out RestockTargetSettings settings))
                return 0;
            int target = settings.TargetCount;
            int stored = storage.CountStored(thingDef);
            return CalculateBulkAvailable(storage, thingDef, target, stored);
        }

        //使用已经读取的目标和库存计算普通商品可派发数量，避免同一次需求重算重复访问配置。
        private int CalculateBulkAvailable(Building_SimContainer storage, ThingDef thingDef, int target, int stored)
        {
            if (target <= 0 || stored >= target)
                return 0;
            int pendingForDef = leases.CountPending(storage.thingIDNumber, thingDef);
            int perDefNeed = Math.Max(0, target - stored - pendingForDef);
            int capacity = Math.Max(0, storage.MaxTotalCapacity - storage.CountTotalStored() - leases.CountTotalPending(storage.thingIDNumber));
            return Math.Min(perDefNeed, capacity);
        }
    }
}
