using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.GameComp;
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
    //提供柜中现货菜单，职责是保留同一商品在不同柜中的来源与售价。
    public static class RestaurantProductMenuUtility
    {
        //枚举上架商品，职责是从货柜草稿提交后的运行配置生成菜单快照。
        public static IEnumerable<RestaurantMenuItem> StockMenus(Zone_Shop shop)
        {
            foreach (var cabinet in RestaurantStockUtility.Cabinets(shop).Where(c => !c.IsRefrigerator))
                foreach (var rule in cabinet.saleRules.Where(r => r.onSale && r.item != null))
                {
                    var data = cabinet.Goods.FindItemData(rule.item);
                    var product = cabinet.Product(rule.item);
                    if (product == null || data?.enabled != true || data.price <= 0
                        || rule.mode == RestaurantDeliveryMode.Consume && rule.item.ingestible == null) continue;
                    yield return new RestaurantMenuItem { id = "Cabinet/" + cabinet.thingIDNumber + "/" + rule.item.defName,
                        label = rule.item.LabelCap, mealDefName = rule.item.defName, unitPrice = data.price,
                        minCount = rule.portions, maxCount = rule.portions, sourceCabinet = cabinet,
                        deliveryMode = rule.mode, selectionWeight = product.selectionWeight };
                }
        }

        //合并厨房菜单和柜中现货，职责是让经营、入座和接单共用候选集合。
        public static List<RestaurantMenuItem> AllMenus(Zone_Shop shop, RestaurantShopSettings settings) =>
            RestaurantMenuUtility.GetEnabledMenuItems(settings).Concat(StockMenus(shop)).ToList();

        //检查顾客饮食条件，职责是柜中商品直接检查实际物品成分。
        public static bool Accepts(Pawn customer, RestaurantMenuItem item)
        {
            if (!item.IsStockProduct) return RestaurantFoodUtility.WillCustomerEatMenu(customer, item);
            if (item.deliveryMode == RestaurantDeliveryMode.TakeAway) return customer.inventory != null;
            return item.sourceCabinet.GetDirectlyHeldThings().Any(t => t.def == item.MealDef && t.IngestibleNow
                && customer.WillEat(t, customer, careIfNotAcceptableForTitle: true, allowVenerated: false));
        }

        //检查完整现货及服务员路线，职责是让现成商品避开厨房配方要求。
        public static bool Available(Zone_Shop shop, RestaurantMenuItem menu, int count, Pawn customer = null)
        {
            var cabinet = menu.sourceCabinet;
            if (cabinet?.Spawned != true || !cabinet.AllowsInventoryItem(menu.MealDef)) return false;
            var ledger = shop.Map.GetComponent<MapComponent_InventoryReservations>();
            int available = cabinet.GetDirectlyHeldThings().Where(t => t.def == menu.MealDef
                && (menu.deliveryMode == RestaurantDeliveryMode.TakeAway || t.IngestibleNow
                    && (customer == null || customer.WillEat(t, customer, true, false)))).Sum(t => ledger.Available(t));
            return available >= count && shop.Map.mapPawns.AllPawnsSpawned.Any(p =>
                RestaurantStaffAvailabilityUtility.IsAvailableNow(p)
                && SimShopStaffApi.IsAssignedToWorkGiver(shop, p, DefOfRefs.RSR_WorkGiver_DeliverRestaurantOrder)
                && p.carryTracker.MaxStackSpaceEver(menu.MealDef) >= count
                && p.CanReach(cabinet.InventoryInteractionTarget, cabinet.InventoryInteractionEndMode, Danger.Some));
        }

        //扣除本次会话的全部未结算承诺，职责是防止并行子订单超出顾客预算。
        public static float RemainingBudget(Pawn customer, Zone_Shop shop)
        {
            var session = RestaurantOrderUtility.OrderManager.ForCustomer(customer.thingIDNumber, shop.ID);
            float committed = session?.Orders.Sum(o => o.IsTerminal ? o.acceptedCount * o.unitPrice : o.menuConfirmed ? o.price : 0f) ?? 0f;
            return System.Math.Max(0, SimShopCustomerApi.GetRemainingBudget(customer, shop) - committed);
        }

        //判断追加需求，职责是把未吃完餐品的预期营养和已经点过的商品计入选择。
        public static bool WantsMore(Pawn customer, Zone_Shop shop, RestaurantMenuItem item)
        {
            var session = RestaurantOrderUtility.OrderManager.ForCustomer(customer.thingIDNumber, shop.ID);
            if (session == null || session.rounds == 0) return true;
            if (item.deliveryMode == RestaurantDeliveryMode.TakeAway || item.MealDef.IsDrug)
                return !session.Orders.Any(o => o.menuConfirmed && o.mealDef == item.MealDef && !o.IsTerminal)
                    && (item.deliveryMode == RestaurantDeliveryMode.TakeAway || (customer.needs?.joy?.CurLevelPercentage ?? 0f) < 0.9f);
            return (customer.needs?.food?.NutritionWanted ?? 0) - session.ExpectedNutrition > 0.05f;
        }
    }
}
