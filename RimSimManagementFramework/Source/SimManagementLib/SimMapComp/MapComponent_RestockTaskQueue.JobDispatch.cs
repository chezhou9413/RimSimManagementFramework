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
                    if (!storagesById.TryGetValue(task.StorageId, out IShopStorage storage) || !IsValidStorage(storage))
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

        //为手动调用在指定普通货柜上确定性创建补货 Job，不提前改写自动补货队列状态，玩家强制指令忽略名单、排班与深搜冷却。
        public Job TryMakeBulkJobForStorage(Pawn pawn, Building_SimContainer storage, bool forced)
            => TryMakeBulkJobForStorage(pawn, (IShopStorage)storage, forced);

        //通过统一货柜入口处理宿主建筑。
        internal Job TryMakeBulkJobForStorage(Pawn pawn, IShopStorage storage, bool forced)
        {
            if (!IsValidStorage(storage) || storage is Building_UniqueGoodsContainer || pawn?.Map != map)
                return null;
            EnsureRuntimeInitialized();
            if (!CanPawnUseRequest(pawn, storage, RestockRequestKind.Bulk, forced)
                || !pawn.CanReach(storage.InventoryInteractionTarget, storage.InventoryInteractionEndMode, Danger.Deadly))
            {
                if (forced)
                    JobFailReason.Is("RSMF.Restock.Forced.NoAccess".Translate());
                return null;
            }
            bool hasShortfall = false;
            int now = Find.TickManager?.TicksGame ?? 0;
            foreach (ThingDef thingDef in storage.ActiveDefs)
            {
                if (thingDef == null)
                    continue;
                int target = storage.GetTargetCount(thingDef);
                int stored = storage.CountStored(thingDef);
                int needed = CalculateBulkAvailable(storage, thingDef, target, stored);
                if (needed <= 0)
                    continue;
                hasShortfall = true;
                if (!sourceResolver.HasPotentialBulkSupply(storage, thingDef))
                    continue;
                Job job = TryCreateBulkJob(pawn, storage, thingDef, needed, now, ignoreBudget: true, forced: forced, out _, out _, out _);
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

        //为手动调用在指定专业货柜上确定性创建补货 Job，玩家强制指令忽略名单、排班、深搜冷却与普通预约。
        public Job TryMakeUniqueJobForStorage(Pawn pawn, Building_UniqueGoodsContainer storage, bool forced = true)
        {
            if (!IsValidStorage(storage) || pawn?.Map != map)
                return null;
            EnsureRuntimeInitialized();
            if (!CanPawnUseRequest(pawn, storage, RestockRequestKind.Unique, forced)
                || !pawn.CanReach(storage.InventoryInteractionTarget, storage.InventoryInteractionEndMode, Danger.Deadly))
            {
                if (forced)
                    JobFailReason.Is("RSMF.Restock.Forced.NoAccess".Translate());
                return null;
            }
            bool hasPending = false;
            int now = Find.TickManager?.TicksGame ?? 0;
            System.Collections.Generic.IReadOnlyList<Pojo.UniqueGoodsSlotData> slots = storage.UniqueSlots;
            for (int i = 0; i < slots.Count; i++)
            {
                Pojo.UniqueGoodsSlotData slot = slots[i];
                if (slot == null || !slot.HasPendingSource)
                    continue;
                RestockTaskKey key = RestockTaskKey.ForUnique(storage.thingIDNumber, i, slot.pendingSourceThingId);
                if (leases.HasLease(key))
                    continue;
                hasPending = true;
                Job job = TryCreateUniqueJob(pawn, storage, i, slot.pendingSourceThingId, now, ignoreBudget: true, forced: forced, out _, out _, out _, out _);
                if (job != null)
                    return job;
            }
            if (forced)
            {
                JobFailReason.Is((hasPending
                    ? "RSMF.Restock.Forced.NoSupply"
                    : "RSMF.Restock.Forced.AtTarget").Translate());
            }
            return null;
        }

        //尝试把一个可靠请求转换成具体 Job，并通过 failureScope 区分单人条件与全局阻塞。
        private Job TryBuildJob(Pawn pawn, IShopStorage storage, RestockTask task, int now, bool ignoreBudget, out BuildFailureScope failureScope, bool forced = false)
        {
            failureScope = BuildFailureScope.None;
            if (!IsValidStorage(storage))
            {
                RemoveRequest(task.Key);
                return null;
            }
            if (!CanPawnUseRequest(pawn, storage, task.Kind, forced))
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
                ? TryBuildUniqueJob(pawn, storage as Building_UniqueGoodsContainer, task, now, ignoreBudget, forced, out failureScope)
                : TryBuildBulkJob(pawn, storage, task, now, ignoreBudget, forced, out failureScope);
        }

        //创建普通补货 Job，并把结果状态同步回可靠请求。
        private Job TryBuildBulkJob(Pawn pawn, IShopStorage storage, RestockTask task, int now, bool ignoreBudget, bool forced, out BuildFailureScope failureScope)
        {
            int needed = CalculateBulkAvailable(storage, task.ThingDef);
            if (needed <= 0)
            {
                task.NeededCount = 0;
                task.StateReason = SimTranslation.T("RSMF.RestockReason.InTransit");
                task.RetryTick = now + TemporaryRetryTicks;
                failureScope = BuildFailureScope.GlobalRetry;
                return null;
            }

            Job job = TryCreateBulkJob(pawn, storage, task.ThingDef, needed, now, ignoreBudget, forced, out failureScope, out string reason, out int retryDelay);
            if (job == null)
            {
                if (!reason.NullOrEmpty())
                    task.StateReason = reason;
                if (failureScope == BuildFailureScope.GlobalRetry)
                {
                    task.SupplyId = -1;
                    task.RetryTick = now + retryDelay;
                }
                return null;
            }

            task.SupplyId = job.targetA.Thing?.thingIDNumber ?? -1;
            task.StateReason = SimTranslation.T("RSMF.RestockReason.JobCreated");
            task.RetryTick = now + 1;
            return job;
        }

        //搜索普通货源并按原版手搬容量构造补货 Job。
        private Job TryCreateBulkJob(
            Pawn pawn,
            IShopStorage storage,
            ThingDef thingDef,
            int needed,
            int now,
            bool ignoreBudget,
            bool forced,
            out BuildFailureScope failureScope,
            out string stateReason,
            out int retryDelay)
        {
            failureScope = BuildFailureScope.None;
            stateReason = null;
            retryDelay = MissingSupplyRetryTicks;
            int carryMax = pawn.carryTracker?.MaxStackSpaceEver(thingDef) ?? 0;
            if (carryMax <= 0)
            {
                failureScope = BuildFailureScope.PawnSpecific;
                return null;
            }
            if (!ignoreBudget && !budget.TryUseReachQuery())
            {
                stateReason = SimTranslation.T("RSMF.RestockReason.WaitSourceSearch");
                failureScope = BuildFailureScope.BudgetExhausted;
                return null;
            }

            Thing supply = sourceResolver.FindBulkSupply(pawn, storage, thingDef, false, forced);
            bool deepSearched = false;
            if (supply == null
                && (forced || ((ignoreBudget || budget.TryUseReachQuery()) && budget.TryUseDeepSearch(now))))
            {
                deepSearched = true;
                supply = sourceResolver.FindBulkSupply(pawn, storage, thingDef, true, forced);
            }
            if (supply == null)
            {
                stateReason = deepSearched ? SimTranslation.T("RSMF.RestockReason.BulkSourceUnavailable") : SimTranslation.T("RSMF.RestockReason.WaitDeepSearch");
                failureScope = BuildFailureScope.GlobalRetry;
                return null;
            }

            int availableSupply = InventoryReservations != null ? InventoryReservations.Available(supply) : supply.stackCount;
            LocalTargetInfo target = LocalTargetInfo.Invalid;
            if (storage is SimThingComp.ThingComp_StorageSales sales)
            {
                target = sales.FindDeliveryTarget(pawn, supply);
                needed = Math.Min(needed, sales.DeliveryRoom(pawn, supply, target));
            }
            int amount = Math.Min(needed, Math.Min(carryMax, availableSupply));
            if (amount <= 0)
            {
                stateReason = SimTranslation.T("RSMF.RestockReason.NoCarryCapacity");
                failureScope = BuildFailureScope.PawnSpecific;
                return null;
            }
            JobDef jobDef = cachedDepositJobDef ??= DefDatabase<JobDef>.GetNamed("DepositToMegaStorage");
            Job job = JobMaker.MakeJob(jobDef, supply, storage.Host);
            if (target.IsValid) job.targetC = target;
            job.count = amount;
            job.haulMode = HaulMode.ToCellStorage;
            job.plantDefToSow = thingDef;
            job.workGiverDef = GetWorkGiverDef(RestockRequestKind.Bulk);
            return job;
        }

        //创建专业补货 Job，来源暂时不可用时始终保留玩家指派和请求。
        private Job TryBuildUniqueJob(Pawn pawn, Building_UniqueGoodsContainer storage, RestockTask task, int now, bool ignoreBudget, bool forced, out BuildFailureScope failureScope)
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

            Job job = TryCreateUniqueJob(pawn, storage, task.SlotIndex, task.SourceThingId, now, ignoreBudget, forced, out failureScope, out string reason, out int retryDelay, out bool confirmedDestroyed);
            if (confirmedDestroyed)
            {
                RemoveRequest(task.Key);
                return null;
            }
            if (job == null)
            {
                if (!reason.NullOrEmpty())
                    task.StateReason = reason;
                if (failureScope == BuildFailureScope.GlobalRetry)
                    task.RetryTick = now + retryDelay;
                return null;
            }

            task.SupplyId = job.targetA.Thing?.thingIDNumber ?? -1;
            task.StateReason = SimTranslation.T("RSMF.RestockReason.UniqueJobCreated");
            task.RetryTick = now + 1;
            return job;
        }

        //解析专业货源并构造精确搬运 Job，玩家强制指令绕过深搜冷却并允许抢占普通预约。
        private Job TryCreateUniqueJob(
            Pawn pawn,
            Building_UniqueGoodsContainer storage,
            int slotIndex,
            int sourceThingId,
            int now,
            bool ignoreBudget,
            bool forced,
            out BuildFailureScope failureScope,
            out string stateReason,
            out int retryDelay,
            out bool confirmedDestroyed)
        {
            failureScope = BuildFailureScope.None;
            stateReason = null;
            retryDelay = MissingSupplyRetryTicks;
            confirmedDestroyed = false;
            if (storage == null || !storage.IsPendingSlotMatch(slotIndex, sourceThingId))
                return null;

            Thing source = sourceResolver.ResolveExactThing(sourceThingId, false, out confirmedDestroyed);
            bool deepSearched = false;
            if (source == null && !confirmedDestroyed && (forced || budget.TryUseDeepSearch(now)))
            {
                deepSearched = true;
                source = sourceResolver.ResolveExactThing(sourceThingId, true, out confirmedDestroyed);
            }
            if (confirmedDestroyed)
            {
                storage.ClearDestroyedPendingSource(slotIndex, sourceThingId);
                return null;
            }
            if (source == null || !source.Spawned || source.Map != map)
            {
                stateReason = deepSearched ? SimTranslation.T("RSMF.RestockReason.UniqueNotSpawned") : SimTranslation.T("RSMF.RestockReason.WaitUniqueIndex");
                failureScope = BuildFailureScope.GlobalRetry;
                return null;
            }
            if (!storage.IsEligiblePendingSource(slotIndex, source))
            {
                stateReason = SimTranslation.T("RSMF.RestockReason.UniqueIneligible");
                failureScope = BuildFailureScope.GlobalRetry;
                return null;
            }
            if ((pawn.carryTracker?.MaxStackSpaceEver(source.def) ?? 0) <= 0)
            {
                failureScope = BuildFailureScope.PawnSpecific;
                return null;
            }
            if (!forced && map.reservationManager.IsReserved(source))
            {
                stateReason = SimTranslation.T("RSMF.RestockReason.UniqueReserved");
                retryDelay = TemporaryRetryTicks;
                failureScope = BuildFailureScope.GlobalRetry;
                return null;
            }
            if (source.IsForbidden(pawn) || !pawn.CanReserve(source, 1, 1, null, forced))
            {
                failureScope = BuildFailureScope.PawnSpecific;
                return null;
            }
            if (!ignoreBudget && !budget.TryUseReachQuery())
            {
                stateReason = SimTranslation.T("RSMF.RestockReason.WaitUniqueReach");
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

        //判断 Pawn 是否具备指定补货工作的能力、岗位权限和地图状态，玩家强制指令忽略固定名单与排班小时。
        private static bool CanPawnUseRequest(Pawn pawn, IShopStorage storage, RestockRequestKind kind, bool forced = false)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned || pawn.Downed || pawn.InMentalState || pawn.Drafted)
                return false;
            if (storage == null || storage.Host.Destroyed || !storage.Host.Spawned || storage.Host.Map != pawn.Map)
                return false;
            WorkGiverDef workGiverDef = GetWorkGiverDef(kind);
            if (workGiverDef == null)
                return false;
            ShopStaffRoleDef role = GetRestockerRoleDef();
            WorkTypeDef requiredWorkType = role?.requiredWorkType ?? workGiverDef.workType;
            if (!ShopStaffUtility.CanPawnPerformWorkType(pawn, requiredWorkType, !forced))
                return false;
            if (!HasRequiredCapacities(pawn, workGiverDef))
                return false;
            if (VendingMachineUtility.IsVendingMachine(storage as Building_SimContainer))
                return true;
            SimZone.Zone_Shop shop = ShopDataUtility.FindShopZone(storage.Host.Map, storage.Host.Position);
            return forced
                ? ShopStaffUtility.AllowsPawnForForcedWorkGiver(shop, pawn, workGiverDef)
                : ShopStaffUtility.AllowsPawnForWorkGiver(shop, pawn, workGiverDef, 0, false);
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
        private int CalculateBulkAvailable(IShopStorage storage, ThingDef thingDef)
        {
            if (!IsValidStorage(storage) || thingDef == null)
                return 0;
            if (!configurationIndex.TryGetSettings(storage.Host.thingIDNumber, thingDef, out RestockTargetSettings settings))
                return 0;
            int target = settings.TargetCount;
            int stored = storage.CountStored(thingDef);
            return CalculateBulkAvailable(storage, thingDef, target, stored);
        }

        //使用已经读取的目标和库存计算普通商品可派发数量，避免同一次需求重算重复访问配置。
        private int CalculateBulkAvailable(IShopStorage storage, ThingDef thingDef, int target, int stored)
        {
            if (target <= 0 || stored >= target)
                return 0;
            int pendingForDef = leases.CountPending(storage.Host.thingIDNumber, thingDef);
            int perDefNeed = Math.Max(0, target - stored - pendingForDef);
            if (storage is not Building_SimContainer native) return perDefNeed;
            int capacity = Math.Max(0, native.MaxTotalCapacity - native.CountTotalStored() - leases.CountTotalPending(storage.Host.thingIDNumber));
            return Math.Min(perDefNeed, capacity);
        }
    }
}
