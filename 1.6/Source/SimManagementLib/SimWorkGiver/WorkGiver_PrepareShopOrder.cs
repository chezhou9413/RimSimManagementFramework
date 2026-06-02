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
        private const int CandidateCacheTicks = 37;
        private static readonly Dictionary<int, PrepareOrderCandidateCache> candidateCaches = new Dictionary<int, PrepareOrderCandidateCache>();
        private static WorkGiverDef cachedWorkGiverDef;

        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial);
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
            return FindOrderFor(pawn, t) != null;
        }

        /// <summary>
        /// 创建员工制作现做订单的 Job。
        /// </summary>
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            PreparedShopOrder order = FindOrderFor(pawn, t);
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
        private static PreparedShopOrder FindOrderFor(Pawn pawn, Thing provider)
        {
            if (pawn?.Map == null || provider == null || provider.Destroyed) return null;
            Zone_Shop shop = ShopDataUtility.FindShopZone(provider.Map, provider.Position);
            if (!ShopStaffUtility.IsShopOpenForWork(shop)) return null;
            if (CurrentWorkGiverDef != null && !ShopStaffUtility.AllowsPawnForWorkGiver(shop, pawn, CurrentWorkGiverDef))
                return null;

            GameComponent_PreparedShopOrderManager manager = SimShopApi.OrderManager;
            IReadOnlyList<PreparedShopOrder> orders = manager?.Orders;
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
            return pawn.CanReserve(provider, 1, -1, null, false) ? selected : null;
        }

        /// <summary>
        /// 返回短时间缓存的现做订单服务建筑候选，负责减少 WorkGiver 高频查询订单和反查建筑。
        /// </summary>
        private static List<Thing> GetCandidateProviders(Pawn pawn)
        {
            if (pawn?.Map == null) return EmptyThingList;

            int mapId = pawn.Map.uniqueID;
            int now = Find.TickManager?.TicksGame ?? 0;
            if (!candidateCaches.TryGetValue(mapId, out PrepareOrderCandidateCache cache) || cache == null)
            {
                cache = new PrepareOrderCandidateCache();
                candidateCaches[mapId] = cache;
            }

            if (now < cache.nextRefreshTick)
                return cache.candidates;

            RefreshCandidateProviders(pawn.Map, cache, now);
            return cache.candidates;
        }

        /// <summary>
        /// 刷新现做订单服务建筑候选，负责把待制作订单按提供者建筑去重。
        /// </summary>
        private static void RefreshCandidateProviders(Map map, PrepareOrderCandidateCache cache, int now)
        {
            cache.candidates.Clear();
            cache.providerIds.Clear();
            cache.nextRefreshTick = now + CandidateCacheTicks;

            GameComponent_PreparedShopOrderManager manager = SimShopApi.OrderManager;
            IReadOnlyList<PreparedShopOrder> orders = manager?.Orders;
            if (orders == null || orders.Count <= 0) return;

            Dictionary<int, Thing> thingById = BuildThingIdLookup(map);
            for (int i = 0; i < orders.Count; i++)
            {
                PreparedShopOrder order = orders[i];
                if (!IsWaitingPreparationOrder(order)) continue;
                int providerId = order.providerThingId;
                if (providerId < 0 || cache.providerIds.Contains(providerId)) continue;
                if (!thingById.TryGetValue(providerId, out Thing provider)) continue;
                if (provider == null || provider.Destroyed || !provider.Spawned) continue;

                cache.providerIds.Add(providerId);
                cache.candidates.Add(provider);
            }
        }

        /// <summary>
        /// 构建当前地图 Thing 编号索引，负责让候选刷新时避免每个订单都全图反查一次建筑。
        /// </summary>
        private static Dictionary<int, Thing> BuildThingIdLookup(Map map)
        {
            Dictionary<int, Thing> result = new Dictionary<int, Thing>();
            IReadOnlyList<Thing> things = map?.listerThings?.AllThings;
            if (things == null) return result;

            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (thing == null) continue;
                result[thing.thingIDNumber] = thing;
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
            public int nextRefreshTick = -1;
            public readonly List<Thing> candidates = new List<Thing>();
            public readonly HashSet<int> providerIds = new HashSet<int>();
        }
    }
}
