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
        private enum BuildFailureScope
        {
            None,
            PawnSpecific,
            BudgetExhausted,
            GlobalRetry
        }

        private static ShopStaffRoleDef cachedRestockerRoleDef;
        private static WorkGiverDef cachedBulkWorkGiverDef;
        private static WorkGiverDef cachedUniqueWorkGiverDef;
        private static JobDef cachedDepositJobDef;
        private static JobDef cachedUniqueRestockJobDef;

        //缓存地图的跨岗位库存预留组件，避免补货派工重复遍历地图组件列表。
        private MapComponent_InventoryReservations InventoryReservations
            => cachedInventoryReservations ??= map?.GetComponent<MapComponent_InventoryReservations>();

        //为普通补货 WorkGiver 从可靠请求队列创建 Job。
        public Job TryMakeJobForPawn(Pawn pawn)
        {
            return TryMakeJobForPawn(pawn, RestockRequestKind.Bulk);
        }

        //为指定 Pawn 创建限定类型或任意类型的补货 Job，单人条件不符时不推迟共享任务以免饿死其他店铺员工。
        public Job TryMakeJobForPawn(Pawn pawn, RestockRequestKind? kind)
        {
            if (pawn?.Map != map || !CanPawnAutomaticallyRestock(pawn, kind))
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
            if (scanCount <= 0)
                return null;

            tmpDeferredReadyKeys.Clear();
            try
            {
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
                    if (!storagesById.TryGetValue(task.StorageId, out Building_SimContainer storage) || !IsValidStorage(storage))
                    {
                        RemoveRequest(key);
                        continue;
                    }
                    if (!CanPawnUseRequest(pawn, storage, task.Kind))
                    {
                        tmpDeferredReadyKeys.Add(key);
                        continue;
                    }
                    if (!budget.TryUseDispatchAttempt())
                    {
                        tmpDeferredReadyKeys.Add(key);
                        break;
                    }

                    Job job = TryBuildJob(pawn, storage, task, now, false, out BuildFailureScope failureScope);
                    if (job != null)
                    {
                        if (requests.ContainsKey(key))
                            ScheduleDispatch(key, task.RetryTick, now);
                        return job;
                    }
                    if (failureScope == BuildFailureScope.BudgetExhausted)
                    {
                        tmpDeferredReadyKeys.Add(key);
                        break;
                    }
                    if (failureScope == BuildFailureScope.PawnSpecific)
                    {
                        tmpDeferredReadyKeys.Add(key);
                        continue;
                    }
                    if (requests.ContainsKey(key))
                        ScheduleDispatch(key, task.RetryTick, now);
                }
                return null;
            }
            finally
            {
                for (int i = 0; i < tmpDeferredReadyKeys.Count; i++)
                    EnqueueReadyDispatch(tmpDeferredReadyKeys[i]);
                tmpDeferredReadyKeys.Clear();
            }
        }

        //为手动调用在指定普通货柜上确定性创建补货 Job，玩家强制指令允许忽略排班时段。
        public Job TryMakeBulkJobForStorage(Pawn pawn, Building_SimContainer storage, bool forced)
        {
            if (!IsValidStorage(storage) || storage is Building_UniqueGoodsContainer || pawn?.Map != map)
                return null;
            EnsureRuntimeInitialized();
            if (!CanPawnUseRequest(pawn, storage, RestockRequestKind.Bulk, ignoreShift: forced)
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
                Job job = TryBuildJob(pawn, storage, task, now, true, out BuildFailureScope failureScope, ignoreShift: forced);
                if (job != null || failureScope == BuildFailureScope.GlobalRetry)
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

        //为手动调用在指定专业货柜上确定性创建补货 Job，手动右键允许忽略排班时段。
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
                    Job job = TryBuildJob(pawn, storage, task, now, true, out BuildFailureScope failureScope, ignoreShift: true);
                    if (job != null || failureScope == BuildFailureScope.GlobalRetry)
                        ScheduleDispatch(key, task.RetryTick, now);
                    if (job != null)
                        return job;
                }
            }
            return null;
        }

        //尝试把一个可靠请求转换成具体 Job，并通过 failureScope 区分单人条件与全局阻塞。
        private Job TryBuildJob(Pawn pawn, Building_SimContainer storage, RestockTask task, int now, bool ignoreBudget, out BuildFailureScope failureScope, bool ignoreShift = false)
        {
            failureScope = BuildFailureScope.None;
            if (!IsValidStorage(storage))
            {
                RemoveRequest(task.Key);
                return null;
            }
            if (!CanPawnUseRequest(pawn, storage, task.Kind, ignoreShift))
            {
                failureScope = BuildFailureScope.PawnSpecific;
                return null;
            }
            if (task.Kind == RestockRequestKind.Bulk
                && !sourceResolver.HasPotentialBulkSupply(storage, task.ThingDef))
            {
                task.SupplyId = -1;
                task.StateReason = storage.RestockSourceIssue.NullOrEmpty() ? SimTranslation.T("RSMF.RestockReason.LinkedSourceEmpty") : storage.RestockSourceIssue;
                task.RetryTick = now + MissingSupplyRetryTicks;
                failureScope = BuildFailureScope.GlobalRetry;
                return null;
            }
            if (!ignoreBudget && !budget.TryUseReachQuery())
            {
                task.StateReason = SimTranslation.T("RSMF.RestockReason.WaitCabinetReach");
                failureScope = BuildFailureScope.BudgetExhausted;
                return null;
            }
            if (!pawn.CanReach(storage.InventoryInteractionTarget, storage.InventoryInteractionEndMode, Danger.Deadly))
            {
                failureScope = BuildFailureScope.PawnSpecific;
                return null;
            }
            return task.Kind == RestockRequestKind.Unique
                ? TryBuildUniqueJob(pawn, storage as Building_UniqueGoodsContainer, task, now, ignoreBudget, out failureScope)
                : TryBuildBulkJob(pawn, storage, task, now, ignoreBudget, out failureScope);
        }

        //创建普通补货 Job，货源搜索使用原版区域搜索并按原版手搬容量计算携带上限。
        private Job TryBuildBulkJob(Pawn pawn, Building_SimContainer storage, RestockTask task, int now, bool ignoreBudget, out BuildFailureScope failureScope)
        {
            failureScope = BuildFailureScope.None;
            int needed = CalculateBulkAvailable(storage, task.ThingDef);
            if (needed <= 0)
            {
                task.NeededCount = 0;
                task.StateReason = SimTranslation.T("RSMF.RestockReason.InTransit");
                task.RetryTick = now + TemporaryRetryTicks;
                failureScope = BuildFailureScope.GlobalRetry;
                return null;
            }
            int carryMax = pawn.carryTracker?.MaxStackSpaceEver(task.ThingDef) ?? 0;
            if (carryMax <= 0)
            {
                failureScope = BuildFailureScope.PawnSpecific;
                return null;
            }
            if (!ignoreBudget && !budget.TryUseReachQuery())
            {
                task.StateReason = SimTranslation.T("RSMF.RestockReason.WaitSourceSearch");
                failureScope = BuildFailureScope.BudgetExhausted;
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
                failureScope = BuildFailureScope.GlobalRetry;
                return null;
            }

            int availableSupply = InventoryReservations != null ? InventoryReservations.Available(supply) : supply.stackCount;
            int amount = Math.Min(needed, Math.Min(carryMax, availableSupply));
            if (amount <= 0)
            {
                task.StateReason = SimTranslation.T("RSMF.RestockReason.NoCarryCapacity");
                failureScope = BuildFailureScope.PawnSpecific;
                return null;
            }
            JobDef jobDef = cachedDepositJobDef ??= DefDatabase<JobDef>.GetNamed("DepositToMegaStorage");
            Job job = JobMaker.MakeJob(jobDef, supply, storage);
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
        private Job TryBuildUniqueJob(Pawn pawn, Building_UniqueGoodsContainer storage, RestockTask task, int now, bool ignoreBudget, out BuildFailureScope failureScope)
        {
            failureScope = BuildFailureScope.None;
            if (storage == null || !storage.IsPendingSlotMatch(task.SlotIndex, task.SourceThingId))
            {
                RemoveRequest(task.Key);
                return null;
            }
            if (leases.HasLease(task.Key))
            {
                task.StateReason = SimTranslation.T("RSMF.RestockReason.UniqueInProgress");
                task.RetryTick = now + TemporaryRetryTicks;
                failureScope = BuildFailureScope.GlobalRetry;
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
                failureScope = BuildFailureScope.GlobalRetry;
                return null;
            }
            if (!storage.IsEligiblePendingSource(task.SlotIndex, source))
            {
                task.StateReason = SimTranslation.T("RSMF.RestockReason.UniqueIneligible");
                task.RetryTick = now + MissingSupplyRetryTicks;
                failureScope = BuildFailureScope.GlobalRetry;
                return null;
            }
            if ((pawn.carryTracker?.MaxStackSpaceEver(source.def) ?? 0) <= 0)
            {
                failureScope = BuildFailureScope.PawnSpecific;
                return null;
            }
            if (map.reservationManager.IsReserved(source))
            {
                task.StateReason = SimTranslation.T("RSMF.RestockReason.UniqueReserved");
                task.RetryTick = now + TemporaryRetryTicks;
                failureScope = BuildFailureScope.GlobalRetry;
                return null;
            }
            if (source.IsForbidden(pawn) || !pawn.CanReserve(source))
            {
                failureScope = BuildFailureScope.PawnSpecific;
                return null;
            }
            if (!ignoreBudget && !budget.TryUseReachQuery())
            {
                task.StateReason = SimTranslation.T("RSMF.RestockReason.WaitUniqueReach");
                failureScope = BuildFailureScope.BudgetExhausted;
                return null;
            }
            if (!pawn.CanReach(source, PathEndMode.ClosestTouch, Danger.Deadly))
            {
                failureScope = BuildFailureScope.PawnSpecific;
                return null;
            }

            JobDef jobDef = cachedUniqueRestockJobDef ??= DefDatabase<JobDef>.GetNamed("RestockUniqueGoodsContainer");
            Job job = JobMaker.MakeJob(jobDef, source, storage);
            job.count = 1;
            job.haulMode = HaulMode.ToCellNonStorage;
            job.workGiverDef = GetWorkGiverDef(RestockRequestKind.Unique);
            task.SupplyId = source.thingIDNumber;
            task.StateReason = SimTranslation.T("RSMF.RestockReason.UniqueJobCreated");
            task.RetryTick = now + 1;
            return job;
        }

        //判断 Pawn 当前是否允许自动接取补货工作，遵守作息表、机械体模式与基础补货能力。
        private static bool CanPawnAutomaticallyRestock(Pawn pawn, RestockRequestKind? kind)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned || pawn.Downed || pawn.InMentalState || pawn.Drafted)
                return false;
            if (ShopStaffUtility.IsPawnOffDutyForAutomaticWork(pawn))
                return false;
            if (pawn.needs?.food?.CurCategory >= HungerCategory.UrgentlyHungry
                || pawn.needs?.rest?.CurCategory >= RestCategory.VeryTired)
                return false;

            WorkGiverDef workGiverDef = GetWorkGiverDef(kind ?? RestockRequestKind.Bulk);
            if (workGiverDef == null)
                return false;
            ShopStaffRoleDef role = GetRestockerRoleDef();
            WorkTypeDef requiredWorkType = role?.requiredWorkType ?? workGiverDef.workType;
            if (!ShopStaffUtility.CanPawnPerformWorkType(pawn, requiredWorkType, true))
                return false;
            return HasRequiredCapacities(pawn, workGiverDef);
        }

        //判断 Pawn 是否具备指定补货工作的能力、岗位权限和地图状态，手动强制时可忽略排班小时。
        private static bool CanPawnUseRequest(Pawn pawn, Building_SimContainer storage, RestockRequestKind kind, bool ignoreShift = false)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned || pawn.Downed || pawn.InMentalState || pawn.Drafted)
                return false;
            if (storage == null || storage.Destroyed || !storage.Spawned || storage.Map != pawn.Map)
                return false;
            WorkGiverDef workGiverDef = GetWorkGiverDef(kind);
            if (workGiverDef == null)
                return false;
            ShopStaffRoleDef role = GetRestockerRoleDef();
            WorkTypeDef requiredWorkType = role?.requiredWorkType ?? workGiverDef.workType;
            if (!ShopStaffUtility.CanPawnPerformWorkType(pawn, requiredWorkType, !ignoreShift))
                return false;
            if (!HasRequiredCapacities(pawn, workGiverDef))
                return false;
            return VendingMachineUtility.IsVendingMachine(storage)
                || ShopStaffUtility.AllowsPawnForWorkGiver(ShopStaffUtility.FindShopFor(storage), pawn, workGiverDef, 0, ignoreShift);
        }

        //校验 Pawn 是否满足补货 WorkGiver 要求的生理能力。
        private static bool HasRequiredCapacities(Pawn pawn, WorkGiverDef workGiverDef)
        {
            if (workGiverDef.requiredCapacities == null)
                return true;
            for (int i = 0; i < workGiverDef.requiredCapacities.Count; i++)
            {
                PawnCapacityDef capacity = workGiverDef.requiredCapacities[i];
                if (capacity != null && pawn.health?.capacities?.CapableOf(capacity) == false)
                    return false;
            }
            return true;
        }

        //返回缓存的补货岗位定义。
        private static ShopStaffRoleDef GetRestockerRoleDef()
            => cachedRestockerRoleDef ??= DefDatabase<ShopStaffRoleDef>.GetNamedSilentFail("SimShopRole_Restocker");

        //返回缓存的请求类型对应 WorkGiverDef。
        private static WorkGiverDef GetWorkGiverDef(RestockRequestKind kind)
        {
            if (kind == RestockRequestKind.Unique)
                return cachedUniqueWorkGiverDef ??= DefDatabase<WorkGiverDef>.GetNamedSilentFail("RestockUniqueGoodsContainer");
            return cachedBulkWorkGiverDef ??= DefDatabase<WorkGiverDef>.GetNamedSilentFail("RestockMegaStorage");
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
