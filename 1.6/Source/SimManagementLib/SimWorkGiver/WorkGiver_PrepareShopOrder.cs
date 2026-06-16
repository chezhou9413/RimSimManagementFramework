using RimWorld;
using SimManagementLib.Api;
using SimManagementLib.GameComp;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimWorkGiver
{
    /// <summary>
    /// 为店员分配现做订单制作工作，负责扫描已付款待制作订单并创建员工制作 Job。
    /// </summary>
    public class WorkGiver_PrepareShopOrder : WorkGiver_Scanner
    {
        private const int CandidateCacheTicks = 97;
        private const int CandidateCacheJitterTicks = 29;
        private const int CandidateWindowTicks = 7;
        private const int CandidateWindowSize = 12;
        private const int CacheStaggerSalt = 307;
        private const int ProviderReserveCacheTicks = 11;
        private static readonly Dictionary<int, PrepareOrderCandidateCache> candidateCaches = new Dictionary<int, PrepareOrderCandidateCache>();
        private static WorkGiverDef cachedWorkGiverDef;

        public override PathEndMode PathEndMode => PathEndMode.Touch;

        /// <summary>
        /// 返回当前 WorkGiverDef，用于岗位分配检查。
        /// </summary>
        private static WorkGiverDef CurrentWorkGiverDef
        {
            get
            {
                if (cachedWorkGiverDef == null)
                    cachedWorkGiverDef = DefDatabase<WorkGiverDef>.GetNamedSilentFail("Sim_WorkGiver_PrepareShopOrder");
                return cachedWorkGiverDef;
            }
        }

        /// <summary>
        /// 返回当前地图中拥有待制作订单的服务建筑。
        /// </summary>
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            List<Thing> candidates = GetCandidateProviders(pawn);
            for (int i = 0; i < candidates.Count; i++)
                yield return candidates[i];
        }

        /// <summary>
        /// 判断员工是否能在指定服务建筑上处理一个现做订单。
        /// </summary>
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return FindOrderFor(pawn, t, GetExistingCache(pawn?.Map), true) != null;
        }

        /// <summary>
        /// 创建员工制作现做订单的 Job。
        /// </summary>
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            PreparedShopOrder order = FindOrderFor(pawn, t, GetExistingCache(pawn?.Map), false);
            if (order == null) return null;

            PreparedShopOrderResult assigned = SimShopOrderApi.TryAssignOrder(pawn, order);
            if (!assigned.success) return null;

            Job job = SimShopOrderApi.MakeStaffPrepareJob(pawn, assigned.order);
            if (job == null)
            {
                SimShopOrderApi.CancelOrder(assigned.order, "无法创建制作工作", failed: true);
                return null;
            }

            return job;
        }

        /// <summary>
        /// 为员工和服务建筑查找一个可制作订单。
        /// </summary>
        private static PreparedShopOrder FindOrderFor(Pawn pawn, Thing provider, PrepareOrderCandidateCache cache, bool useCachedReserve)
        {
            if (pawn?.Map == null || provider == null || provider.Destroyed) return null;
            if (provider.Map != pawn.Map || !provider.Spawned) return null;
            Zone_Shop shop = ShopDataUtility.FindShopZone(provider.Map, provider.Position);
            if (!ShopStaffUtility.IsShopOpenForWork(shop)) return null;
            if (CurrentWorkGiverDef != null && !ShopStaffUtility.AllowsPawnForWorkGiver(shop, pawn, CurrentWorkGiverDef))
                return null;

            IReadOnlyList<PreparedShopOrder> orders = GetOrdersForProvider(pawn.Map, provider, cache);
            if (orders == null || orders.Count <= 0) return null;

            PreparedShopOrder selected = null;
            int selectedSortTick = int.MaxValue;
            for (int i = 0; i < orders.Count; i++)
            {
                PreparedShopOrder order = orders[i];
                if (!IsWaitingPreparationOrderForProvider(order, provider.thingIDNumber)) continue;
                PreparedShopOrderWorker worker = SimShopOrderApi.GetPreparedOrderWorker(order.serviceDefName);
                if (worker == null || !worker.CanStaffWork(pawn, order, out _)) continue;

                int sortTick = GetOrderSortTick(order);
                if (selected == null || sortTick < selectedSortTick)
                {
                    selected = order;
                    selectedSortTick = sortTick;
                }
            }

            if (selected == null) return null;
            bool canReserve = useCachedReserve
                ? WorkGiverThingQueryCache.CanReserveThingCached(pawn, provider, 1, -1, false, ProviderReserveCacheTicks)
                : pawn.CanReserve(provider, 1, -1, null, false);
            return canReserve ? selected : null;
        }

        /// <summary>
        /// 返回短时间缓存的现做订单服务建筑候选，负责减少 WorkGiver 高频查询订单和反查建筑。
        /// </summary>
        private static List<Thing> GetCandidateProviders(Pawn pawn)
        {
            if (pawn?.Map == null) return EmptyThingList;

            int mapId = pawn.Map.uniqueID;
            int now = Find.TickManager?.TicksGame ?? 0;
            if (!candidateCaches.TryGetValue(mapId, out PrepareOrderCandidateCache cache) || cache == null || cache.map != pawn.Map)
            {
                cache = new PrepareOrderCandidateCache();
                cache.map = pawn.Map;
                candidateCaches[mapId] = cache;
            }

            if (now < cache.nextRefreshTick && cache.IsForMap(pawn.Map))
            {
                if (now >= cache.nextWindowTick)
                    RefreshCandidateWindow(cache, now);
                return cache.windowCandidates;
            }

            RefreshCandidateProviders(pawn.Map, cache, now);
            return cache.windowCandidates;
        }

        /// <summary>
        /// 刷新现做订单服务建筑候选，负责把待制作订单按提供者建筑去重。
        /// </summary>
        private static void RefreshCandidateProviders(Map map, PrepareOrderCandidateCache cache, int now)
        {
            cache.map = map;
            cache.allCandidates.Clear();
            cache.windowCandidates.Clear();
            cache.providerIds.Clear();
            cache.ordersByProviderId.Clear();
            cache.nextRefreshTick = WorkGiverScanUtility.NextStaggeredTick(now, CandidateCacheTicks, map.uniqueID, CacheStaggerSalt, CandidateCacheJitterTicks);
            cache.nextWindowTick = now;

            GameComponent_PreparedShopOrderManager manager = SimShopApi.OrderManager;
            IReadOnlyList<PreparedShopOrder> orders = manager?.Orders;
            if (orders == null || orders.Count <= 0) return;

            Dictionary<int, List<PreparedShopOrder>> groupedOrders = PrepareOrderGroupingUtility.BuildGroups(orders);
            foreach (KeyValuePair<int, List<PreparedShopOrder>> entry in groupedOrders)
                cache.ordersByProviderId[entry.Key] = entry.Value;

            Dictionary<int, Thing> thingById = BuildBuildingIdLookup(map);
            foreach (int providerId in cache.ordersByProviderId.Keys)
            {
                if (providerId < 0 || cache.providerIds.Contains(providerId)) continue;
                if (!thingById.TryGetValue(providerId, out Thing provider))
                {
                    provider = FindThingByIdFallback(map, providerId);
                    if (provider != null)
                        thingById[providerId] = provider;
                }
                if (provider == null || provider.Destroyed || !provider.Spawned) continue;

                cache.providerIds.Add(providerId);
                cache.allCandidates.Add(provider);
            }

            RefreshCandidateWindow(cache, now);
        }

        /// <summary>
        /// 刷新现做订单候选窗口，负责把服务建筑扫描错峰拆成较小批次。
        /// </summary>
        private static void RefreshCandidateWindow(PrepareOrderCandidateCache cache, int now)
        {
            WorkGiverScanUtility.BuildThingWindow(cache.allCandidates, cache.windowCandidates, ref cache.windowCursor, CandidateWindowSize);
            cache.nextWindowTick = now + CandidateWindowTicks;
        }

        /// <summary>
        /// 构建当前地图建筑编号索引，负责让候选刷新时避免每个订单都全图反查一次建筑。
        /// </summary>
        private static Dictionary<int, Thing> BuildBuildingIdLookup(Map map)
        {
            Dictionary<int, Thing> result = new Dictionary<int, Thing>();
            List<Building> buildings = map?.listerBuildings?.allBuildingsColonist;
            if (buildings == null) return result;

            for (int i = 0; i < buildings.Count; i++)
            {
                Building building = buildings[i];
                if (building == null) continue;
                result[building.thingIDNumber] = building;
            }
            return result;
        }

        /// <summary>
        /// 按编号从全图 Thing 中兜底查找订单提供者，负责兼容外部 API 创建的非殖民地建筑订单。
        /// </summary>
        private static Thing FindThingByIdFallback(Map map, int thingId)
        {
            IReadOnlyList<Thing> things = map?.listerThings?.AllThings;
            if (things == null) return null;

            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (thing != null && thing.thingIDNumber == thingId)
                    return thing;
            }
            return null;
        }

        /// <summary>
        /// 获取当前地图已有订单候选缓存，负责让 HasJobOnThing 和 JobOnThing 复用候选刷新阶段的分组结果。
        /// </summary>
        private static PrepareOrderCandidateCache GetExistingCache(Map map)
        {
            if (map == null) return null;
            candidateCaches.TryGetValue(map.uniqueID, out PrepareOrderCandidateCache cache);
            if (cache == null || !cache.IsForMap(map))
                return null;
            return cache;
        }

        /// <summary>
        /// 返回服务建筑对应的等待订单列表，负责在缺少缓存时回退到公开订单管理器。
        /// </summary>
        private static IReadOnlyList<PreparedShopOrder> GetOrdersForProvider(Map map, Thing provider, PrepareOrderCandidateCache cache)
        {
            if (provider == null)
                return null;
            if (cache != null && cache.ordersByProviderId.TryGetValue(provider.thingIDNumber, out List<PreparedShopOrder> cachedOrders))
                return cachedOrders;
            if (cache != null)
                return null;
            GameComponent_PreparedShopOrderManager manager = SimShopApi.OrderManager;
            IReadOnlyList<PreparedShopOrder> orders = manager?.Orders;
            if (orders == null || orders.Count <= 0)
                return null;
            List<PreparedShopOrder> result = new List<PreparedShopOrder>();
            for (int i = 0; i < orders.Count; i++)
            {
                PreparedShopOrder order = orders[i];
                if (IsWaitingPreparationOrderForProvider(order, provider.thingIDNumber))
                    result.Add(order);
            }

            return result;
        }

        /// <summary>
        /// 判断订单是否正在等待员工制作。
        /// </summary>
        private static bool IsWaitingPreparationOrder(PreparedShopOrder order)
        {
            return order != null && order.state == PreparedShopOrderState.PaidWaitingPreparation;
        }

        /// <summary>
        /// 判断订单是否属于指定服务建筑且正在等待员工制作。
        /// </summary>
        private static bool IsWaitingPreparationOrderForProvider(PreparedShopOrder order, int providerThingId)
        {
            return IsWaitingPreparationOrder(order) && order.providerThingId == providerThingId;
        }

        /// <summary>
        /// 返回订单排序 Tick，负责让较早付款或创建的订单优先被员工处理。
        /// </summary>
        private static int GetOrderSortTick(PreparedShopOrder order)
        {
            if (order == null) return int.MaxValue;
            if (order.paidTick > 0) return order.paidTick;
            return order.createdTick > 0 ? order.createdTick : int.MaxValue;
        }

        private static readonly List<Thing> EmptyThingList = new List<Thing>(0);

        /// <summary>
        /// 保存单张地图的现做订单服务建筑候选缓存，负责降低高频工作扫描成本。
        /// </summary>
        private class PrepareOrderCandidateCache
        {
            public Map map;
            public int nextRefreshTick = -1;
            public int nextWindowTick = -1;
            public int windowCursor;
            public readonly List<Thing> allCandidates = new List<Thing>();
            public readonly List<Thing> windowCandidates = new List<Thing>();
            public readonly HashSet<int> providerIds = new HashSet<int>();
            public readonly Dictionary<int, List<PreparedShopOrder>> ordersByProviderId = new Dictionary<int, List<PreparedShopOrder>>();

            public bool IsForMap(Map currentMap)
            {
                return map == currentMap;
            }
        }
    }
}
