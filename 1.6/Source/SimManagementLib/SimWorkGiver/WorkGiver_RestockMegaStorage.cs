using RimWorld;
using SimManagementLib.SimAI;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
using static SimManagementLib.SimWorkGiver.RestockSupplyScoringUtility;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimWorkGiver
{
    /// <summary>
    /// 扫描地图上的 Building_SimContainer，为需要补货的货柜分配
    /// JobDriver_DepositToMegaStorage 任务。
    /// </summary>
    public class WorkGiver_RestockMegaStorage : WorkGiver_Scanner
    {
        private const int CandidateCacheTicks = 179;
        private const int CandidateCacheJitterTicks = 43;
        private const int CandidateWindowTicks = 11;
        private const int CandidateWindowSize = 24;
        private const int CacheStaggerSalt = 101;
        private const int SupplySearchCacheTicks = 29;
        private const int StorageReachCacheTicks = 17;
        private const int SupplyThingQueryCacheTicks = 11;
        private static readonly Dictionary<int, RestockCandidateCache> candidateCaches = new Dictionary<int, RestockCandidateCache>();
        private int cachedSupplyExpireTick = -1;
        private int cachedSupplyPawnId = -1;
        private int cachedSupplyStorageId = -1;
        private Thing cachedSupply;
        private static WorkGiverDef cachedWorkGiverDef;

        /// <summary>
        /// 获取当前补货 WorkGiverDef，避免每次扫描都查询 DefDatabase。
        /// </summary>
        private static WorkGiverDef CurrentWorkGiverDef
        {
            get
            {
                if (cachedWorkGiverDef == null)
                    cachedWorkGiverDef = DefDatabase<WorkGiverDef>.GetNamedSilentFail("RestockMegaStorage");
                return cachedWorkGiverDef;
            }
        }

        /// <summary>
        /// 返回地图上的货柜候选。这里只做轻量过滤，重判断交给 HasJobOnThing，避免 RimWorld 工作扫描重复执行重逻辑。
        /// </summary>
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            List<Thing> candidates = GetCandidateStorages(pawn);
            for (int i = 0; i < candidates.Count; i++)
                yield return candidates[i];
        }

        /// <summary>
        /// 判断指定货柜是否有可执行补货任务。
        /// </summary>
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Building_SimContainer storage)) return false;
            storage.ReconcilePendingReservationsForWorkScan();
            if (!NeedsRestock(storage, pawn, true)) return false;
            return FindBestSupplyCached(pawn, storage, true) != null;
        }

        /// <summary>
        /// 为指定货柜生成补货任务，并复用同一 tick 中 HasJobOnThing 找到的供应物。
        /// </summary>
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Building_SimContainer storage)) return null;
            storage.ReconcilePendingReservations();
            if (!NeedsRestock(storage, pawn, false)) return null;

            Thing supply = FindBestSupplyCached(pawn, storage, false);
            if (supply == null) return null;

            ThingDef td = supply.def;
            int needed = storage.CountNeeded(td);
            if (needed <= 0) return null;

            int carryMax = MassUtility.CountToPickUpUntilOverEncumbered(pawn, supply);
            int amount = System.Math.Min(needed, System.Math.Min(carryMax, supply.stackCount));
            if (amount <= 0) return null;

            Job job = JobMaker.MakeJob(
                DefDatabase<JobDef>.GetNamed("DepositToMegaStorage"),
                supply,
                storage);
            job.count = amount;
            job.haulMode = HaulMode.ToCellStorage;
            job.plantDefToSow = td;
            return job;
        }

        /// <summary>
        /// 判断货柜是否缺少任意已配置商品，并确认 pawn 能到达货柜。
        /// </summary>
        private static bool NeedsRestock(Building_SimContainer storage, Pawn pawn, bool useCachedReach)
        {
            if (storage.Destroyed || !storage.Spawned) return false;
            Zone_Shop shop = ShopStaffUtility.FindShopFor(storage);
            if (!VendingMachineUtility.IsVendingMachine(storage) && !ShopStaffUtility.IsShopOpenForWork(shop)) return false;
            if (!VendingMachineUtility.IsVendingMachine(storage) && CurrentWorkGiverDef != null && !ShopStaffUtility.AllowsPawnForWorkGiver(shop, pawn, CurrentWorkGiverDef))
                return false;
            if (useCachedReach)
            {
                if (!WorkGiverThingQueryCache.CanReachThingCached(pawn, storage, PathEndMode.Touch, Danger.Deadly, StorageReachCacheTicks)) return false;
            }
            else if (!pawn.CanReach(storage, PathEndMode.Touch, Danger.Deadly)) return false;

            foreach (ThingDef td in storage.ActiveDefs)
            {
                if (storage.CountNeeded(td) > 0) return true;
            }
            return false;
        }

        /// <summary>
        /// 判断货柜是否允许被补货扫描，自动售货机不依赖商店营业状态。
        /// </summary>
        private static bool IsAllowedByBusinessState(Building_SimContainer storage)
        {
            if (VendingMachineUtility.IsVendingMachine(storage))
                return true;
            return ShopStaffUtility.IsShopOpenForWork(ShopStaffUtility.FindShopFor(storage));
        }

        /// <summary>
        /// 返回短时间缓存的补货货柜候选，负责避免每个找工作的员工都重复全图扫描建筑。
        /// </summary>
        private static List<Thing> GetCandidateStorages(Pawn pawn)
        {
            if (pawn?.Map?.listerBuildings == null) return EmptyThingList;

            int mapId = pawn.Map.uniqueID;
            int now = Find.TickManager?.TicksGame ?? 0;
            if (!candidateCaches.TryGetValue(mapId, out RestockCandidateCache cache) || cache == null)
            {
                cache = new RestockCandidateCache();
                candidateCaches[mapId] = cache;
            }

            if (now < cache.nextRefreshTick)
            {
                if (now >= cache.nextWindowTick)
                    RefreshCandidateWindow(cache, now);
                return cache.windowCandidates;
            }

            RefreshCandidateStorages(pawn.Map, cache, now);
            return cache.windowCandidates;
        }

        /// <summary>
        /// 刷新当前地图的补货货柜候选，负责只保留营业状态允许扫描的货柜。
        /// </summary>
        private static void RefreshCandidateStorages(Map map, RestockCandidateCache cache, int now)
        {
            cache.allCandidates.Clear();
            cache.nextRefreshTick = WorkGiverScanUtility.NextStaggeredTick(now, CandidateCacheTicks, map.uniqueID, CacheStaggerSalt, CandidateCacheJitterTicks);
            cache.nextWindowTick = now;

            List<Building> buildings = map.listerBuildings.allBuildingsColonist;
            for (int i = 0; i < buildings.Count; i++)
            {
                Building_SimContainer storage = buildings[i] as Building_SimContainer;
                if (storage == null || storage.Destroyed || !storage.Spawned) continue;
                if (!IsAllowedByBusinessState(storage)) continue;
                if (!HasAnyRestockNeed(storage)) continue;
                cache.allCandidates.Add(storage);
            }

            RefreshCandidateWindow(cache, now);
        }

        /// <summary>
        /// 刷新补货候选窗口，负责把全量货柜扫描错峰拆成较小批次。
        /// </summary>
        private static void RefreshCandidateWindow(RestockCandidateCache cache, int now)
        {
            WorkGiverScanUtility.BuildThingWindow(cache.allCandidates, cache.windowCandidates, ref cache.windowCursor, CandidateWindowSize);
            cache.nextWindowTick = now + CandidateWindowTicks;
        }

        private static readonly List<Thing> EmptyThingList = new List<Thing>(0);

        /// <summary>
        /// 判断货柜是否存在任何补货缺口，负责在候选刷新阶段提前排除空转货柜。
        /// </summary>
        private static bool HasAnyRestockNeed(Building_SimContainer storage)
        {
            foreach (ThingDef thingDef in storage.ActiveDefs)
            {
                if (storage.CountNeeded(thingDef) > 0)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 保存单张地图的补货货柜候选缓存，负责降低 WorkGiver 高频扫描成本。
        /// </summary>
        private class RestockCandidateCache
        {
            public int nextRefreshTick = -1;
            public int nextWindowTick = -1;
            public int windowCursor;
            public readonly List<Thing> allCandidates = new List<Thing>();
            public readonly List<Thing> windowCandidates = new List<Thing>();
        }

        /// <summary>
        /// 在同一游戏 tick 内缓存供货搜索结果，避免 HasJobOnThing 和 JobOnThing 连续全图搜索两次。
        /// </summary>
        private Thing FindBestSupplyCached(Pawn pawn, Building_SimContainer storage, bool useCachedThingQueries)
        {
            int tick = Find.TickManager?.TicksGame ?? -1;
            int pawnId = pawn?.thingIDNumber ?? -1;
            int storageId = storage?.thingIDNumber ?? -1;
            if (cachedSupplyPawnId == pawnId && cachedSupplyStorageId == storageId && tick <= cachedSupplyExpireTick)
            {
                if (cachedSupply == null)
                    return null;
                if (IsValidCachedSupply(pawn, storage, cachedSupply))
                    return cachedSupply;
            }

            cachedSupplyExpireTick = tick + SupplySearchCacheTicks;
            cachedSupplyPawnId = pawnId;
            cachedSupplyStorageId = storageId;
            cachedSupply = FindBestSupply(pawn, storage, useCachedThingQueries);
            return cachedSupply;
        }

        /// <summary>
        /// 判断缓存中的供应物是否仍然可用，负责避免短缓存返回已经被预约或已不缺货的物品。
        /// </summary>
        private static bool IsValidCachedSupply(Pawn pawn, Building_SimContainer storage, Thing thing)
        {
            if (pawn == null || storage == null || thing == null) return false;
            if (thing.Destroyed || !thing.Spawned || thing.stackCount <= 0) return false;
            if (IsInsideAnyStorageContainer(thing)) return false;
            if (thing.IsForbidden(pawn)) return false;
            if (storage.CountNeeded(thing.def) <= 0) return false;
            if (!pawn.CanReserve(thing)) return false;
            return pawn.CanReach(thing, PathEndMode.ClosestTouch, Danger.Deadly);
        }

        /// <summary>
        /// 从地图物品中查找距离最近且满足缺货配置的供应物。
        /// </summary>
        private static Thing FindBestSupply(Pawn pawn, Building_SimContainer storage, bool useCachedThingQueries)
        {
            Zone_Shop shop = ShopStaffUtility.FindShopFor(storage);
            bool pawnAssigned = ShopStaffUtility.IsAssignedToWorkGiver(shop, pawn, CurrentWorkGiverDef);
            List<SupplyCandidateSnapshot> snapshots = new List<SupplyCandidateSnapshot>();

            foreach (ThingDef td in storage.ActiveDefs)
            {
                if (storage.CountNeeded(td) <= 0) continue;

                List<Thing> candidates = pawn.Map.listerThings.ThingsOfDef(td);
                foreach (Thing candidate in candidates)
                {
                    if (!candidate.Spawned || candidate.IsForbidden(pawn)) continue;
                    if (IsInsideAnyStorageContainer(candidate)) continue;
                    snapshots.Add(new SupplyCandidateSnapshot(candidate, candidate.Position, pawn.Position, pawnAssigned));
                }
            }

            List<ScoredSupplyCandidate> scoredCandidates = RestockSupplyScoringUtility.ScoreSupplyCandidates(snapshots);
            for (int i = 0; i < scoredCandidates.Count; i++)
            {
                Thing candidate = scoredCandidates[i].Thing;
                if (candidate == null || candidate.Destroyed || !candidate.Spawned || candidate.stackCount <= 0) continue;
                if (candidate.IsForbidden(pawn)) continue;
                if (useCachedThingQueries)
                {
                    if (!WorkGiverThingQueryCache.CanReserveThingCached(pawn, candidate, 1, -1, false, SupplyThingQueryCacheTicks)) continue;
                }
                else if (!pawn.CanReserve(candidate)) continue;

                if (storage.CountNeeded(candidate.def) <= 0) continue;

                if (useCachedThingQueries)
                {
                    if (WorkGiverThingQueryCache.CanReachThingCached(pawn, candidate, PathEndMode.ClosestTouch, Danger.Deadly, SupplyThingQueryCacheTicks))
                        return candidate;
                }
                else if (pawn.CanReach(candidate, PathEndMode.ClosestTouch, Danger.Deadly)) return candidate;
            }

            return null;
        }

        private static bool IsInsideAnyStorageContainer(Thing t)
        {
            if (t == null || !t.Spawned) return true;

            // 普通仓库区的物品允许补货使用；建筑作为 SlotGroup 父级时，通常表示冷柜、展示柜或槽位存储正在持有该物品。
            return t.GetSlotGroup()?.parent is Thing;
        }
    }
}
