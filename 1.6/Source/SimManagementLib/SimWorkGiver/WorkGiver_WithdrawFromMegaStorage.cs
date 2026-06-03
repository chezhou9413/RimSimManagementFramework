using RimWorld;
using SimManagementLib.SimAI;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimWorkGiver
{
    /// <summary>
    /// 扫描 Building_SimContainer，找出超出目标量或已被移出配置的物品，
    /// 分配 JobDriver_WithdrawFromMegaStorage 任务让 pawn 走过去取走。
    /// </summary>
    public class WorkGiver_WithdrawFromMegaStorage : WorkGiver_Scanner
    {
        private const int CandidateCacheTicks = 191;
        private const int CandidateCacheJitterTicks = 47;
        private const int CandidateWindowTicks = 13;
        private const int CandidateWindowSize = 24;
        private const int CacheStaggerSalt = 211;
        private const int StorageReachCacheTicks = 17;
        private static readonly Dictionary<int, WithdrawCandidateCache> candidateCaches = new Dictionary<int, WithdrawCandidateCache>();
        private static WorkGiverDef cachedWorkGiverDef;

        /// <summary>
        /// 获取当前清理库存 WorkGiverDef，避免每次扫描都查询 DefDatabase。
        /// </summary>
        private static WorkGiverDef CurrentWorkGiverDef
        {
            get
            {
                if (cachedWorkGiverDef == null)
                    cachedWorkGiverDef = DefDatabase<WorkGiverDef>.GetNamedSilentFail("WithdrawFromMegaStorage");
                return cachedWorkGiverDef;
            }
        }

        /// <summary>
        /// 返回地图上的货柜候选。这里只做轻量营业状态过滤，避免候选枚举阶段反复扫描虚拟库存。
        /// </summary>
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            List<Thing> candidates = GetCandidateStorages(pawn);
            for (int i = 0; i < candidates.Count; i++)
                yield return candidates[i];
        }

        /// <summary>
        /// 判断指定货柜是否存在需要移出的多余商品。
        /// </summary>
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Building_SimContainer storage)) return false;
            storage.ReconcilePendingReservationsForWorkScan();
            if (!HasExcess(storage, pawn)) return false;
            if (!WorkGiverThingQueryCache.CanReachThingCached(pawn, storage, PathEndMode.Touch, Danger.Deadly, StorageReachCacheTicks)) return false;
            return true;
        }

        /// <summary>
        /// 为指定货柜生成移出多余商品的任务。
        /// </summary>
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Building_SimContainer storage)) return null;
            storage.ReconcilePendingReservations();
            if (!pawn.CanReach(storage, PathEndMode.Touch, Danger.Deadly)) return null;

            foreach ((ThingDef td, int excess) in storage.GetExcessItems())
            {
                if (excess <= 0) continue;

                int reserved = storage.ReservePendingOut(td, excess);
                if (reserved <= 0) continue;

                Job job = JobMaker.MakeJob(
                    DefDatabase<JobDef>.GetNamed("WithdrawFromMegaStorage"),
                    storage);
                job.count = reserved;
                job.plantDefToSow = td;
                return job;
            }
            return null;
        }

        /// <summary>
        /// 判断货柜是否有超过目标量或已移出配置的库存。
        /// </summary>
        private static bool HasExcess(Building_SimContainer storage, Pawn pawn)
        {
            if (storage.Destroyed || !storage.Spawned) return false;
            Zone_Shop shop = ShopStaffUtility.FindShopFor(storage);
            if (!VendingMachineUtility.IsVendingMachine(storage) && !ShopStaffUtility.IsShopOpenForWork(shop)) return false;
            if (!VendingMachineUtility.IsVendingMachine(storage) && !ShopStaffUtility.AllowsPawnForWorkGiver(shop, pawn, CurrentWorkGiverDef))
                return false;

            foreach ((ThingDef _, int excess) in storage.GetExcessItems())
            {
                if (excess > 0) return true;
            }
            return false;
        }

        /// <summary>
        /// 判断货柜是否允许被清理库存扫描，自动售货机不依赖商店营业状态。
        /// </summary>
        private static bool IsAllowedByBusinessState(Building_SimContainer storage)
        {
            if (VendingMachineUtility.IsVendingMachine(storage))
                return true;
            return ShopStaffUtility.IsShopOpenForWork(ShopStaffUtility.FindShopFor(storage));
        }

        /// <summary>
        /// 返回短时间缓存的清理货柜候选，负责避免每个搬运者重复枚举全部殖民地建筑。
        /// </summary>
        private static List<Thing> GetCandidateStorages(Pawn pawn)
        {
            if (pawn?.Map?.listerBuildings == null) return EmptyThingList;

            int mapId = pawn.Map.uniqueID;
            int now = Find.TickManager?.TicksGame ?? 0;
            if (!candidateCaches.TryGetValue(mapId, out WithdrawCandidateCache cache) || cache == null)
            {
                cache = new WithdrawCandidateCache();
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
        /// 刷新当前地图的清理货柜候选，负责只保留营业状态允许扫描的货柜。
        /// </summary>
        private static void RefreshCandidateStorages(Map map, WithdrawCandidateCache cache, int now)
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
                if (!HasAnyExcess(storage)) continue;
                cache.allCandidates.Add(storage);
            }

            RefreshCandidateWindow(cache, now);
        }

        /// <summary>
        /// 刷新清理货柜候选窗口，负责把全量货柜扫描错峰拆成较小批次。
        /// </summary>
        private static void RefreshCandidateWindow(WithdrawCandidateCache cache, int now)
        {
            WorkGiverScanUtility.BuildThingWindow(cache.allCandidates, cache.windowCandidates, ref cache.windowCursor, CandidateWindowSize);
            cache.nextWindowTick = now + CandidateWindowTicks;
        }

        private static readonly List<Thing> EmptyThingList = new List<Thing>(0);

        /// <summary>
        /// 判断货柜是否存在任意多余库存，负责在候选刷新阶段提前排除空转货柜。
        /// </summary>
        private static bool HasAnyExcess(Building_SimContainer storage)
        {
            foreach ((ThingDef _, int excess) in storage.GetExcessItems())
            {
                if (excess > 0)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 保存单张地图的清理货柜候选缓存，负责降低 WorkGiver 高频扫描成本。
        /// </summary>
        private class WithdrawCandidateCache
        {
            public int nextRefreshTick = -1;
            public int nextWindowTick = -1;
            public int windowCursor;
            public readonly List<Thing> allCandidates = new List<Thing>();
            public readonly List<Thing> windowCandidates = new List<Thing>();
        }
    }
}
