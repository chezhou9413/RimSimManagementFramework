using RimWorld;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimWorkGiver
{
    //补货工作分配器，职责是以分帧预算扫描货柜和货源，避免单次找工作执行全图补货搜索。
    public class WorkGiver_RestockMegaStorage : WorkGiver_Scanner
    {
        private const int CandidateCacheTicks = 179;
        private const int CandidateCacheJitterTicks = 43;
        private const int CandidateWindowTicks = 11;
        private const int CandidateWindowSize = 4;
        private const int CandidateRefreshBudgetPerTick = 2;
        private const int CacheStaggerSalt = 101;
        private const int StorageReachCacheTicks = 17;
        private static readonly Dictionary<int, RestockCandidateCache> candidateCaches = new Dictionary<int, RestockCandidateCache>();
        private static WorkGiverDef cachedWorkGiverDef;

        //返回当前补货 WorkGiverDef，职责是避免每次扫描查询 DefDatabase。
        private static WorkGiverDef CurrentWorkGiverDef
        {
            get
            {
                if (cachedWorkGiverDef == null)
                    cachedWorkGiverDef = DefDatabase<WorkGiverDef>.GetNamedSilentFail("RestockMegaStorage");
                return cachedWorkGiverDef;
            }
        }

        //返回地图上的补货货柜候选，职责是把全量货柜拆成小窗口交给原版扫描器。
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            List<Thing> candidates = GetCandidateStorages(pawn);
            for (int i = 0; i < candidates.Count; i++)
                yield return candidates[i];
        }

        //直接尝试创建补货工作，职责是绕开原版全局 Thing 扫描并按本类预算窗口派工。
        public override Job NonScanJob(Pawn pawn)
        {
            List<Thing> candidates = GetCandidateStorages(pawn);
            for (int i = 0; i < candidates.Count; i++)
            {
                if (!RestockWorkTickBudget.TryUseStorageCandidateCheck(pawn.Map))
                    return null;

                if (!(candidates[i] is Building_SimContainer storage))
                    continue;

                if (!NeedsRestockForScan(storage, pawn))
                    continue;

                Thing supply = RestockSupplySearchStateCache.FindBestSupplyBudgeted(pawn, storage, true);
                Job job = MakeRestockJobFromSupply(pawn, storage, supply, false);
                if (job != null)
                    return job;
            }

            return null;
        }

        //判断指定货柜是否有可执行补货任务，职责是在高频扫描中只执行轻量判断和预算货源搜索。
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Building_SimContainer storage))
                return false;

            if (!NeedsRestockForScan(storage, pawn))
                return false;

            return RestockSupplySearchStateCache.FindBestSupplyBudgeted(pawn, storage, true) != null;
        }

        //为指定货柜生成补货任务，职责是复用扫描阶段找到的货源并在真正派工前执行强校验。
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Building_SimContainer storage))
                return null;

            storage.ReconcilePendingReservations();
            if (!NeedsRestockForJob(storage, pawn))
                return null;

            Thing supply = RestockSupplySearchStateCache.FindBestSupplyBudgeted(pawn, storage, false);
            return MakeRestockJobFromSupply(pawn, storage, supply, true);
        }

        //按已找到的货源创建补货 Job，职责是让扫描派工和兼容入口复用同一套任务构建规则。
        private static Job MakeRestockJobFromSupply(Pawn pawn, Building_SimContainer storage, Thing supply, bool strongNeedCheck)
        {
            if (pawn == null || storage == null || supply == null || supply.Destroyed || !supply.Spawned || supply.stackCount <= 0)
                return null;

            if (supply.Map != pawn.Map)
                return null;

            ThingDef thingDef = supply.def;
            int needed = strongNeedCheck ? storage.CountNeeded(thingDef) : storage.CountNeededForWorkScan(thingDef);
            if (needed <= 0)
                return null;

            int carryMax = MassUtility.CountToPickUpUntilOverEncumbered(pawn, supply);
            int amount = System.Math.Min(needed, System.Math.Min(carryMax, supply.stackCount));
            if (amount <= 0)
                return null;

            Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("DepositToMegaStorage"), supply, storage);
            job.count = amount;
            job.haulMode = HaulMode.ToCellStorage;
            job.plantDefToSow = thingDef;
            return job;
        }

        //判断货柜在工作扫描阶段是否值得继续找货源，职责是避免触发完整预约校正。
        private static bool NeedsRestockForScan(Building_SimContainer storage, Pawn pawn)
        {
            if (!IsValidStorageForPawn(storage, pawn))
                return false;

            if (!storage.TryFindRestockDefForWorkScan(out _))
                return false;

            if (!AllowsPawnForStorage(storage, pawn))
                return false;

            if (!RestockWorkTickBudget.TryUseStorageReachQuery(pawn.Map))
                return false;

            return WorkGiverThingQueryCache.CanReachThingCached(pawn, storage, PathEndMode.Touch, Danger.Deadly, StorageReachCacheTicks);
        }

        //判断货柜在创建工作前是否仍能补货，职责是使用原始强校验保证最终任务有效。
        private static bool NeedsRestockForJob(Building_SimContainer storage, Pawn pawn)
        {
            if (!IsValidStorageForPawn(storage, pawn))
                return false;

            if (!AllowsPawnForStorage(storage, pawn))
                return false;

            if (!pawn.CanReach(storage, PathEndMode.Touch, Danger.Deadly))
                return false;

            foreach (ThingDef thingDef in storage.ActiveDefs)
            {
                if (storage.CountNeeded(thingDef) > 0)
                    return true;
            }
            return false;
        }

        //判断货柜和小人是否处于同一有效地图，职责是集中处理通用失效条件。
        private static bool IsValidStorageForPawn(Building_SimContainer storage, Pawn pawn)
        {
            return pawn?.Map != null
                && storage != null
                && storage.Map == pawn.Map
                && storage.Spawned
                && !storage.Destroyed;
        }

        //判断当前小人是否允许给货柜补货，职责是应用商店岗位分配规则。
        private static bool AllowsPawnForStorage(Building_SimContainer storage, Pawn pawn)
        {
            Zone_Shop shop = ShopStaffUtility.FindShopFor(storage);
            return VendingMachineUtility.IsVendingMachine(storage)
                || CurrentWorkGiverDef == null
                || ShopStaffUtility.AllowsPawnForWorkGiver(shop, pawn, CurrentWorkGiverDef);
        }

        //返回短时间缓存的货柜候选，职责是避免每个员工重复扫描全部殖民地建筑。
        private static List<Thing> GetCandidateStorages(Pawn pawn)
        {
            if (pawn?.Map?.listerBuildings == null)
                return EmptyThingList;

            int mapId = pawn.Map.uniqueID;
            int now = Find.TickManager?.TicksGame ?? 0;
            if (!candidateCaches.TryGetValue(mapId, out RestockCandidateCache cache) || cache == null || cache.map != pawn.Map)
            {
                cache = new RestockCandidateCache();
                cache.map = pawn.Map;
                candidateCaches[mapId] = cache;
                StartCandidateRefresh(pawn.Map, cache, now);
            }

            if (!cache.refreshInProgress && now >= cache.nextRefreshTick)
                StartCandidateRefresh(pawn.Map, cache, now);

            AdvanceCandidateRefresh(pawn.Map, cache, now);
            if (now >= cache.nextWindowTick)
                RefreshCandidateWindow(cache, now);
            return cache.windowCandidates;
        }

        //启动补货候选增量刷新，职责是准备临时列表但继续保留旧候选供派工使用。
        private static void StartCandidateRefresh(Map map, RestockCandidateCache cache, int now)
        {
            cache.map = map;
            cache.refreshInProgress = true;
            cache.refreshCursor = 0;
            cache.refreshCheckedThisTick = 0;
            cache.refreshBudgetTick = -1;
            cache.stagingCandidates.Clear();
        }

        //推进补货候选增量刷新，职责是把全图建筑扫描和预约校正摊到多个 tick。
        private static void AdvanceCandidateRefresh(Map map, RestockCandidateCache cache, int now)
        {
            if (!cache.refreshInProgress)
                return;

            if (cache.refreshBudgetTick != now)
            {
                cache.refreshBudgetTick = now;
                cache.refreshCheckedThisTick = 0;
            }

            List<Building> buildings = map.listerBuildings.allBuildingsColonist;
            while (cache.refreshCursor < buildings.Count && cache.refreshCheckedThisTick < CandidateRefreshBudgetPerTick)
            {
                Building_SimContainer storage = buildings[cache.refreshCursor++] as Building_SimContainer;
                cache.refreshCheckedThisTick++;
                if (storage == null || storage.Destroyed || !storage.Spawned)
                    continue;

                if (RestockWorkTickBudget.TryUsePendingReconcile(map))
                    storage.ReconcilePendingReservationsForWorkScan();
                if (!storage.TryFindRestockDefForWorkScan(out _))
                    continue;

                cache.stagingCandidates.Add(storage);
            }

            if (cache.refreshCursor < buildings.Count)
                return;

            FinishCandidateRefresh(map, cache, now);
        }

        //完成补货候选增量刷新，职责是一次性替换候选列表并安排下一轮刷新。
        private static void FinishCandidateRefresh(Map map, RestockCandidateCache cache, int now)
        {
            cache.allCandidates.Clear();
            cache.allCandidates.AddRange(cache.stagingCandidates);
            cache.stagingCandidates.Clear();
            cache.refreshInProgress = false;
            cache.refreshCursor = 0;
            cache.nextRefreshTick = WorkGiverScanUtility.NextStaggeredTick(now, CandidateCacheTicks, map.uniqueID, CacheStaggerSalt, CandidateCacheJitterTicks);
            cache.nextWindowTick = now;
            RefreshCandidateWindow(cache, now);
        }

        //刷新补货候选窗口，职责是把货柜检查摊到多次工作扫描中。
        private static void RefreshCandidateWindow(RestockCandidateCache cache, int now)
        {
            WorkGiverScanUtility.BuildThingWindow(cache.allCandidates, cache.windowCandidates, ref cache.windowCursor, CandidateWindowSize);
            cache.nextWindowTick = now + CandidateWindowTicks;
        }

        private static readonly List<Thing> EmptyThingList = new List<Thing>(0);

        //单张地图的补货货柜候选缓存，职责是保存全量候选和当前扫描窗口。
        private class RestockCandidateCache
        {
            public Map map;
            public int nextRefreshTick = -1;
            public int nextWindowTick = -1;
            public int windowCursor;
            public int refreshCursor;
            public int refreshBudgetTick = -1;
            public int refreshCheckedThisTick;
            public bool refreshInProgress;
            public readonly List<Thing> allCandidates = new List<Thing>();
            public readonly List<Thing> windowCandidates = new List<Thing>();
            public readonly List<Thing> stagingCandidates = new List<Thing>();

            //判断缓存是否属于当前地图，职责是防止跨地图复用候选。
            public bool IsForMap(Map currentMap)
            {
                return map == currentMap;
            }
        }

    }
}
