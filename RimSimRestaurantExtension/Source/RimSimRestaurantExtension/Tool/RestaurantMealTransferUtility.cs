using RimSimRestaurantExtension.Buildings;
using RimSimRestaurantExtension.Models;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimSimRestaurantExtension.Tool
{
    //管理订单实物所有权，职责是在容器和携带者之间整单转移并保留中断后的独立堆叠。
    public static partial class RestaurantMealTransferUtility
    {
        //解析取餐目标，职责是把容器内实物映射为可以寻路的建筑。
        public static Thing PickupTarget(RestaurantOrder order, Pawn actor)
        {
            if (order?.stockProduct == true && !order.mealProduced) return order.sourceCabinet;
            Thing meal = order?.meal;
            if (meal == null || meal.Destroyed) return null;
            if (meal.Spawned) return meal;
            if (meal.ParentHolder is Building_RestaurantPass pass) return pass;
            if (meal.ParentHolder is Building_RestaurantStorage cabinet) return cabinet;
            if (actor?.carryTracker?.CarriedThing == meal || meal.holdingOwner == actor?.inventory?.innerContainer) return actor;
            return null;
        }

        //判断员工能否完整取走餐品，职责是同时检查载荷、物品归属和起点路径。
        public static bool CanCollect(Pawn actor, RestaurantOrder order)
        {
            if (order?.stockProduct == true) return CanCollectProduct(actor, order);
            Thing meal = order?.meal;
            Thing target = PickupTarget(order, actor);
            if (actor?.Map == null || target == null || meal.stackCount != order.mealCount
                || !meal.IngestibleNow) return false;
            if (actor.carryTracker.CarriedThing == meal) return true;
            return actor.carryTracker.CarriedThing == null
                && actor.carryTracker.MaxStackSpaceEver(meal.def) >= meal.stackCount
                && actor.CanReach(target, PathEndMode.Touch, Danger.Some)
                && RestaurantPickupReservationUtility.CanReserve(actor, target);
        }

        //将一整堆实物转入指定容器，职责是禁止合堆并核对实际转移数量与物品身份。
        public static bool Transfer(RestaurantOrder order, ThingOwner destination)
        {
            Thing meal = order?.meal;
            if (meal == null || meal.Destroyed || destination == null || destination.Owner is Map) return false;
            if (meal.holdingOwner == destination) return true;
            if (destination.GetCountCanAccept(meal, false) < meal.stackCount) return false;
            int count = meal.stackCount;
            //落地餐品由地图持有，必须先卸载地图实体；只有未生成的库存才走容器转移。
            if (!meal.Spawned && meal.holdingOwner != null)
            {
                Thing result;
                int transferred = meal.holdingOwner.TryTransferToContainer(meal, destination, count, out result, false);
                if (transferred != count || result == null) return false;
                order.meal = result;
                order.mealThingId = result.thingIDNumber;
                return true;
            }
            if (meal.Spawned) meal.DeSpawn();
            if (!destination.TryAdd(meal, false))
                throw new System.InvalidOperationException("餐厅容器已接受检查但无法接收实物");
            return true;
        }

        //在取餐目标旁收取实物，职责是不通过地面落餐改变订单身份。
        public static bool Collect(Pawn actor, RestaurantOrder order)
        {
            if (order?.stockProduct == true) return CollectProduct(actor, order);
            if (!CanCollect(actor, order)) return false;
            Thing target = PickupTarget(order, actor);
            if (target != actor && !actor.CanReachImmediate(target, PathEndMode.Touch)) return false;
            return Transfer(order, actor.carryTracker.innerContainer);
        }

        //保护地面订单餐品，职责是阻止普通搬运和进食工作抢走订单所有权。
        public static void Protect(RestaurantOrder order)
        {
            if (order == null) return;
            foreach (var thing in order.goods)
                if (thing != null && !thing.Destroyed) thing.SetForbidden(true, false);
            order.mealLockedForbidden = true;
        }

        //将中断携带物独立放回地面，职责是绕过合堆并保留直接物品引用。
        public static void DropCarried(Pawn actor, RestaurantOrder order)
        {
            if (actor?.Map == null || order == null) return;
            foreach (var thing in order.goods)
            {
                if (thing == null || thing.Destroyed || thing.holdingOwner != actor.carryTracker.innerContainer
                    && thing.holdingOwner != actor.inventory.innerContainer) continue;
                thing.holdingOwner.Remove(thing);
                GenSpawn.Spawn(thing, actor.Position, actor.Map);
            }
            Protect(order);
        }

        //解除终态餐品保护并释放出餐台库存，职责是避免取消订单留下无法取出的剩餐。
        public static void Release(RestaurantOrder order)
        {
            if (order == null) return;
            foreach (var thing in order.goods)
            {
                if (thing == null || thing.Destroyed) continue;
                thing.SetForbidden(false, false);
                if (order.IsTerminal && !order.mealConsumed && !thing.Spawned && thing.holdingOwner != null && thing.MapHeld != null)
                    thing.holdingOwner.TryDrop(thing, thing.PositionHeld, thing.MapHeld, ThingPlaceMode.Near, out _);
            }
            order.mealLockedForbidden = false;
        }
    }
}
