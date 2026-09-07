using System.Linq;
using SimManagementLib.SimThingClass;
using Verse;
namespace SimManagementLib.Api
{
    //管理已经交付的实物商品，职责是接入独立动作结算而不重复操作购物车。
    public static class SimShopDeliveredGoodsApi
    {
        //登记已经交入顾客库存的实物，职责是以直接引用防止重复交付和回收。
        public static SimApiResult Register(Pawn customer, CustomerActionOrder order, string key,
            Thing thing, Building_SimContainer source)
        {
            if (customer?.inventory == null || order == null || order.customerThingId != customer.thingIDNumber
                || !ReferenceEquals(order, SimShopCustomerApi.GetActionOrder(order.orderId))
                || thing?.holdingOwner != customer.inventory.innerContainer || key.NullOrEmpty())
                return SimApiResult.Fail("已交付商品的顾客、动作或持有关系无效");
            if (!order.deliveredThings.Any(r => r.thing == thing))
                order.deliveredThings.Add(new ActionDeliveredThing { key = key, thing = thing, source = source });
            return SimApiResult.Success();
        }

        //完成付款登记，职责是释放已付款商品的追踪记录。
        public static void Complete(CustomerActionOrder order) => order?.deliveredThings.Clear();

        //收回仍存在的未付款实物，职责是不重建已被消费的物品。
        public static void Recover(Pawn customer, CustomerActionOrder order)
        {
            if (order == null) return;
            foreach (var record in order.deliveredThings.ToList())
            {
                Thing thing = record.thing;
                if (thing == null || thing.Destroyed) { order.deliveredThings.Remove(record); continue; }
                var source = record.source;
                if (source?.Spawned == true) source.TryReceiveReturnedThing(thing);
                if (source != null && thing.ParentHolder == source) { order.deliveredThings.Remove(record); continue; }
                Map map = source?.Map ?? thing.MapHeld ?? customer?.MapHeld;
                IntVec3 origin = source?.Spawned == true ? source.Position : thing.PositionHeld;
                if (map == null) { Log.Error("无法回收未付款实物：" + thing); continue; }
                if (thing.Spawned) thing.DeSpawn();
                thing.holdingOwner?.Remove(thing);
                if (GenPlace.TryPlaceThing(thing, origin, map, ThingPlaceMode.Near))
                    order.deliveredThings.Remove(record);
                else Log.Error("未付款实物无法放回地图：" + thing);
            }
        }
    }
}
