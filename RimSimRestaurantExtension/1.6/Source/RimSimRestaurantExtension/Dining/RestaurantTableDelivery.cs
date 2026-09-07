using System.Linq;
using RimSimRestaurantExtension.Inventory;
using RimSimRestaurantExtension.Models;
using RimSimRestaurantExtension.Tool;
using Verse;
using Verse.AI;
namespace RimSimRestaurantExtension.Dining
{
    //执行顾客桌面的整单交付，职责是允许用餐时其他商品并行送达。
    internal static class RestaurantTableDelivery
    {
        //把服务员持有的全部商品交入本人餐位，职责是核对顾客、数量和直接持有关系。
        public static bool Deliver(RestaurantOrder order, Pawn waiter)
        {
            if (waiter?.Map == null || order == null
                || !RestaurantOrderUtility.EnsureOrderStillValid(order, waiter.Map)) return false;
            var session = RestaurantOrderUtility.OrderManager.SessionFor(order);
            if (session == null || !RestaurantSessionUtility.Validate(session, waiter.Map, out _)) return false;
            Pawn customer = RestaurantOrderUtility.FindCustomer(waiter.Map, order);
            if (order?.state != RestaurantOrderState.Delivering || order.mealDelivered
                || session?.tray?.Spawned != true || session.IsTerminal
                || order.waiterThingId != waiter.thingIDNumber || customer?.Position != session.seat
                || !waiter.CanReachImmediate(customer, PathEndMode.Touch)
                || order.goods.Sum(t => t != null && !t.Destroyed ? t.stackCount : 0) != order.mealCount
                || order.goods.Any(t => !RestaurantOrderStock.Usable(order, t)
                    || t.holdingOwner != waiter.carryTracker.innerContainer && t.holdingOwner != waiter.inventory.innerContainer)) return false;
            foreach (var thing in order.goods)
                if (!RestaurantMealTransferUtility.TransferThing(thing, session.tray.GetDirectlyHeldThings())) return false;
            order.mealDelivered = true;
            order.deliveredTick = order.diningStartedTick = Find.TickManager.TicksGame;
            order.waiterThingId = -1;
            RestaurantOrderStock.Release(order);
            RestaurantOrderCoordinator.MoveTo(order, RestaurantOrderState.Dining);
            return true;
        }
    }
}
