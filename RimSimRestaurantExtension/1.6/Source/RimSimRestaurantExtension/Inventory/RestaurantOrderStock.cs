using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.Models;
using RimSimRestaurantExtension.Tool;
using RimWorld;
using SimManagementLib.Api;
using SimManagementLib.SimMapComp;
using SimManagementLib.SimZone;
using Verse;
using Verse.AI;
namespace RimSimRestaurantExtension.Inventory
{
    //管理子订单的真实库存承诺，职责是在接单、取料、消费和中断之间维护唯一归属。
    public static class RestaurantOrderStock
    {
        //在确认时分配真实食材或商品，职责是避让共享后厨及其他订单。
        public static bool Reserve(RestaurantOrder order, Zone_Shop shop)
        {
            var ledger = shop.Map.GetComponent<MapComponent_InventoryReservations>();
            List<ThingCount> selected = null;
            if (order.stockProduct)
            {
                if (order.sourceCabinet?.Spawned != true || !order.sourceCabinet.AllowsInventoryItem(order.mealDef)) return false;
                int left = order.mealCount;
                selected = new List<ThingCount>();
                foreach (Thing thing in order.sourceCabinet.GetDirectlyHeldThings())
                {
                    if (thing.def != order.mealDef || !Usable(order, thing)) continue;
                    Pawn customer = RestaurantOrderUtility.FindCustomer(shop.Map, order);
                    if (order.mode == RestaurantDeliveryMode.Consume && !customer.WillEat(thing, customer, true, false)) continue;
                    int count = System.Math.Min(left, ledger.Available(thing));
                    if (count <= 0) continue;
                    selected.Add(new ThingCount(thing, count));
                    left -= count;
                    if (left == 0) break;
                }
                if (left > 0) return false;
            }
            else
            {
                var stoves = RestaurantCookingUtility.FindUsableStoves(shop, order);
                Thing pass = RestaurantOrderCreationUtility.FindOrderCounter(shop);
                foreach (Pawn cook in shop.Map.mapPawns.AllPawnsSpawned.Where(p =>
                    RestaurantStaffAvailabilityUtility.IsAvailableNow(p)
                    && SimShopStaffApi.IsAssignedToWorkGiver(shop, p, DefOfRefs.RSR_WorkGiver_CookRestaurantOrder)
                    && p.carryTracker.MaxStackSpaceEver(order.mealDef) >= order.mealCount
                    && pass != null && p.CanReach(pass, PathEndMode.Touch, Danger.Some)
                    && stoves.Any(stove => RestaurantCookingUtility.CanPawnCookOrderAt(p, stove, order)
                        && p.CanReach(stove, PathEndMode.InteractionCell, Danger.Some))))
                {
                    if (RestaurantStockUtility.Select(shop, order, cook, out selected)) break;
                    selected = null;
                }
            }
            return selected != null && ledger.Reserve(RestaurantStockUtility.Key(order), selected);
        }

        //返回有效预留，职责是在存档恢复和实际取货前核实数量。
        public static List<ThingCount> Reserved(RestaurantOrder order, Map map) =>
            map.GetComponent<MapComponent_InventoryReservations>().Query(RestaurantStockUtility.Key(order));

        //判断订单实物是否还能交付或食用。
        public static bool Usable(RestaurantOrder order, Thing thing)
        {
            return thing != null && !thing.Destroyed && thing.stackCount > 0
                && (order.mode == RestaurantDeliveryMode.TakeAway || thing.IngestibleNow);
        }

        //核查预留和成品归属，职责是明确报告实物丢失而不重复生成。
        public static void Validate(RestaurantOrder order, Map map)
        {
            if (order.IsTerminal || order.mealConsumed || !order.menuConfirmed) return;
            if (order.mealProduced)
            {
                if (order.goods.Any(t => t != null && !t.Destroyed && !Usable(order, t))
                    || order.goods.Where(t => t != null && !t.Destroyed).Sum(t => t.stackCount) + order.acceptedCount != order.mealCount
                    || order.goods.Any(t => t != null && !t.Destroyed && (!t.SpawnedOrAnyParentSpawned || t.MapHeld != map)))
                    RestaurantOrderUtility.FailOrder(order, "订单实物丢失、腐坏或数量发生变化");
                else if (!order.mealDelivered) RestaurantMealTransferUtility.Protect(order);
                return;
            }
            var reserved = Reserved(order, map);
            var currentShop = RestaurantOrderUtility.FindShopById(map, order.shopZoneId);
            if (!order.stockProduct && reserved.Any(t => t.Thing != null && !t.Thing.Destroyed
                && !IsWithdrawn(order, t.Thing, map) && !IsCurrentKitchenSource(currentShop, t.Thing)))
            {
                RestaurantOrderUtility.FailOrder(order, "已预留食材的冰箱或后厨储存区已失效");
                return;
            }
            if (reserved.Count == 0 && !order.stockProduct && order.state == RestaurantOrderState.WaitingCook)
            {
                if (Find.TickManager.TicksGame < order.nextCookingAttemptTick) return;
                var shop = RestaurantOrderUtility.FindShopById(map, order.shopZoneId);
                if (!Reserve(order, shop)) order.blockReason = "制作中断后，绑定货源暂时不足";
                return;
            }
            int invalid = reserved.FindIndex(t => t.Thing == null || t.Thing.Destroyed || t.Count <= 0 || t.Count > t.Thing.stackCount);
            if (invalid >= 0)
            {
                ThingCount item = reserved[invalid];
                RestaurantOrderUtility.FailOrder(order, $"已预留的真实物资已毁坏或数量不足：{item.Thing}，预留={item.Count}，实物={item.Thing?.stackCount ?? 0}");
            }
            else if (order.stockProduct && (order.sourceCabinet?.Spawned != true || reserved.Sum(t => t.Count) != order.mealCount))
                RestaurantOrderUtility.FailOrder(order, "商品柜已移除或已预留商品丢失");
        }

        //检查食材是否已经搬离货源，职责是允许灶台旁及厨师携带中的实物继续制作。
        public static bool IsWithdrawn(RestaurantOrder order, Thing thing, Map map)
        {
            Pawn cook = RestaurantOrderUtility.FindThingById(map, order.cookThingId) as Pawn;
            return cook?.CurJobDef == DefOfRefs.RSR_CookRestaurantOrder
                && RestaurantJobUtility.GetOrderId(cook.CurJob) == order.orderId
                && (thing.holdingOwner == cook.carryTracker.innerContainer
                    || cook.CurJob.placedThings?.Any(t => t.thing == thing) == true);
        }

        //核对尚未取出的食材来源，职责是感知储存区删除、范围调整和冰箱拆除。
        private static bool IsCurrentKitchenSource(Zone_Shop shop, Thing thing)
        {
            if (shop == null) return false;
            if (thing.ParentHolder is Buildings.Building_RestaurantStorage fridge)
                return fridge.Spawned && fridge.IsRefrigerator && fridge.Shop == shop && fridge.AllowsInventoryItem(thing.def);
            return thing.Spawned && RestaurantStockUtility.Pantries(shop).Any(z => z.ContainsCell(thing.Position));
        }

        //释放订单所有实物预留，职责是不改变已生成餐品的身份。
        public static void Release(RestaurantOrder order)
        {
            var map = Find.Maps.FirstOrDefault(m => m.uniqueID == order.mapId);
            map?.GetComponent<MapComponent_InventoryReservations>()?.Release(RestaurantStockUtility.Key(order));
        }
    }
}
