using RimWorld;
using SimManagementLib.Api;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimWorkGiver
{
    /// <summary>
    /// 为店员分配现做订单制作工作，负责扫描已付款待制作订单并创建员工制作 Job。
    /// </summary>
    public class WorkGiver_PrepareShopOrder : WorkGiver_Scanner
    {
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
            if (pawn?.Map == null) return Enumerable.Empty<Thing>();
            PreparedShopOrderQuery query = new PreparedShopOrderQuery
            {
                states = new List<PreparedShopOrderState> { PreparedShopOrderState.PaidWaitingPreparation },
                includeTerminalOrders = false
            };

            return SimShopOrderApi.QueryOrders(query)
                .Select(order => SimShopOrderApi.FindOrderProvider(pawn.Map, order))
                .Where(thing => thing != null && !thing.Destroyed)
                .Distinct();
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

            PreparedShopOrderQuery query = new PreparedShopOrderQuery
            {
                states = new List<PreparedShopOrderState> { PreparedShopOrderState.PaidWaitingPreparation },
                includeTerminalOrders = false
            };

            List<PreparedShopOrder> orders = SimShopOrderApi.QueryOrders(query)
                .Where(order => order.providerThingId == provider.thingIDNumber)
                .OrderBy(order => order.paidTick > 0 ? order.paidTick : order.createdTick)
                .ToList();

            for (int i = 0; i < orders.Count; i++)
            {
                PreparedShopOrder order = orders[i];
                PreparedShopOrderWorker worker = SimShopOrderApi.GetPreparedOrderWorker(order.serviceDefName);
                if (worker != null && worker.CanStaffWork(pawn, order, out _) && pawn.CanReserve(provider, 1, -1, null, false))
                    return order;
            }

            return null;
        }
    }
}
