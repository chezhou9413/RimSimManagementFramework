using System.Linq;
using RimSimRestaurantExtension.Buildings;
using RimSimRestaurantExtension.Inventory;
using RimSimRestaurantExtension.Models;
using RimWorld;
using SimManagementLib.SimMapComp;
using Verse;
using Verse.AI;
namespace RimSimRestaurantExtension.Tool
{
    //转移柜中商品实物，职责是保留不同组件属性的独立物品引用。
    public static partial class RestaurantMealTransferUtility
    {
        //完整转移单个实物，职责是拒绝自动合堆并保持直接引用。
        public static bool TransferThing(Thing thing, ThingOwner target)
        {
            if (thing == null || thing.Destroyed || target == null) return false;
            if (thing.holdingOwner == target) return true;
            if (target.GetCountCanAccept(thing, false) < thing.stackCount) return false;
            int count = thing.stackCount;
            if (thing.holdingOwner != null)
                return thing.holdingOwner.TryTransferToContainer(thing, target, count, out _, false) == count;
            if (thing.Spawned) thing.DeSpawn();
            if (!target.TryAdd(thing, false)) throw new System.InvalidOperationException("餐厅实物转移失败：" + thing);
            return true;
        }

        //检查整单商品的取货路径，职责是允许恢复同订单的多个独立堆叠。
        public static bool CanCollectProduct(Pawn actor, RestaurantOrder order)
        {
            if (actor?.Map == null || actor.inventory == null
                || actor.carryTracker.CarriedThing != null && !order.goods.Contains(actor.carryTracker.CarriedThing)) return false;
            if (!order.mealProduced)
            {
                var source = order.sourceCabinet;
                var items = RestaurantOrderStock.Reserved(order, actor.Map);
                return source?.Spawned == true
                    && items.All(t => t.Thing?.ParentHolder == source && RestaurantOrderStock.Usable(order, t.Thing))
                    && items.Sum(t => t.Thing.GetStatValue(StatDefOf.Mass) * t.Count) <= MassUtility.Capacity(actor) - MassUtility.GearAndInventoryMass(actor)
                    && items.Sum(t => t.Count) == order.mealCount
                    && actor.CanReach(source.InventoryInteractionTarget, source.InventoryInteractionEndMode, Danger.Some)
                    && RestaurantPickupReservationUtility.CanReserve(actor, source);
            }
            return order.goods.Count > 0 && order.goods.All(t => RestaurantOrderStock.Usable(order, t)
                && (t.holdingOwner == actor.inventory.innerContainer || t.holdingOwner == actor.carryTracker.innerContainer
                    || t.Spawned && actor.CanReach(t, PathEndMode.Touch, Danger.Some)))
                && order.goods.Sum(t => t.stackCount) == order.mealCount;
        }

        //从柜中提取已冻结实物，职责是一次性取出整单并允许独立堆叠随员工存档。
        public static bool CollectProduct(Pawn actor, RestaurantOrder order)
        {
            if (!CanCollectProduct(actor, order)) return false;
            if (!order.mealProduced)
            {
                var source = order.sourceCabinet;
                if (!actor.CanReachImmediate(source.InventoryInteractionTarget, source.InventoryInteractionEndMode)) return false;
                var ledger = actor.Map.GetComponent<MapComponent_InventoryReservations>();
                foreach (var item in RestaurantOrderStock.Reserved(order, actor.Map))
                {
                    Thing part = ledger.Extract(RestaurantStockUtility.Key(order), item.Thing, item.Count, actor.inventory.innerContainer);
                    if (part == null) throw new System.InvalidOperationException("预留商品提取失败：" + item.Thing);
                    order.goods.Add(part);
                }
                order.mealProduced = true;
                order.ingredientCost = order.goods.Sum(t => t.MarketValue * t.stackCount);
                order.cookedTick = Find.TickManager.TicksGame;
            }
            else
            {
                foreach (var thing in order.goods)
                {
                    if (thing.Spawned && !actor.CanReachImmediate(thing, PathEndMode.Touch)) return false;
                    if (!TransferThing(thing, actor.inventory.innerContainer)) return false;
                }
            }
            order.meal = order.goods.First();
            order.mealThingId = order.meal.thingIDNumber;
            if (actor.carryTracker.CarriedThing == null) Transfer(order, actor.carryTracker.innerContainer);
            Protect(order);
            return true;
        }
    }
}
