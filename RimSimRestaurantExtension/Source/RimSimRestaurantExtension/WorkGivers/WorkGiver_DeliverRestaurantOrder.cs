using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.Models;
using RimSimRestaurantExtension.Tool;
using SimManagementLib.Api;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimSimRestaurantExtension.WorkGivers
{
    //派发服务员任务，职责是筛选完整取餐和桌边路径后再按先后顺序选单。
    public class WorkGiver_DeliverRestaurantOrder : WorkGiver_Scanner
    {
        protected virtual bool Reception => false;
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForDef(DefOfRefs.RSR_RestaurantOrderCounter);
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        //枚举餐厅服务台，职责是把工作扫描限制到指定设施。
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            return pawn.Map.listerThings.ThingsOfDef(DefOfRefs.RSR_RestaurantOrderCounter);
        }

        //查询是否存在完整可执行任务，职责是不让最早受阻订单挡住其他顾客。
        public override bool HasJobOnThing(Pawn pawn, Thing provider, bool forced = false)
        {
            return FindOrderFor(pawn, provider) != null;
        }

        //创建接单或交付任务，职责是保存顾客、取餐目标和员工站位。
        public override Job JobOnThing(Pawn pawn, Thing provider, bool forced = false)
        {
            var order = FindOrderFor(pawn, provider);
            if (order == null || !RestaurantDiningSpotUtility.TryFindDeliveryCell(pawn, order, out IntVec3 cell)) return null;
            Thing target = Reception ? RestaurantOrderUtility.FindCustomer(pawn.Map, order)
                : RestaurantMealTransferUtility.PickupTarget(order, pawn);
            var job = JobMaker.MakeJob(Reception ? DefOfRefs.RSR_TakeRestaurantOrder : DefOfRefs.RSR_DeliverRestaurantOrder, target, cell);
            job.workGiverDef = def;
            RestaurantJobUtility.SetOrderId(job, order.orderId);
            return job;
        }

        //按同店同台筛选可接近顾客，职责是结合员工权限、状态和取送全程路径。
        private RestaurantOrder FindOrderFor(Pawn pawn, Thing provider)
        {
            var shop = SimShopServiceApi.FindShop(provider.Map, provider.Position);
            if (shop == null || !RestaurantOrderUtility.CanRestaurantStaffWorkAt(shop)
                || !SimShopStaffApi.IsAssignedToWorkGiver(shop, pawn, def)) return null;
            return RestaurantOrderUtility.OrderManager.GetActiveOrders(shop.ID)
                .Where(order => order.providerThingId == provider.thingIDNumber
                    && order.state == (Reception ? RestaurantOrderState.WaitingOrder : RestaurantOrderState.ReadyToDeliver))
                .Where(order =>
                {
                    Pawn customer = RestaurantOrderUtility.FindCustomer(pawn.Map, order);
                    return customer?.Position == order.seatCell
                        && customer.CurJobDef == DefOfRefs.RSR_WaitRestaurantOrderAtDiningSpot
                        && RestaurantDiningSpotUtility.TryFindDeliveryCell(pawn, order, out _)
                        && (Reception || RestaurantMealTransferUtility.CanCollect(pawn, order));
                })
                .OrderBy(order => Reception ? order.seatedTick : order.orderedTick).ThenBy(order => order.orderId).FirstOrDefault();
        }
    }
}
