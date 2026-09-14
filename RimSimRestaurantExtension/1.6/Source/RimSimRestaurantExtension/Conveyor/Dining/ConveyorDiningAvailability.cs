using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.Conveyor.Placement;
using RimSimRestaurantExtension.Conveyor.Stocking;
using RimSimRestaurantExtension.Conveyor.Transport;
using RimSimRestaurantExtension.Inventory;
using RimSimRestaurantExtension.Tool;
using RimWorld;
using SimManagementLib.SimZone;
using Verse;

namespace RimSimRestaurantExtension.Conveyor.Dining
{
    //判断自助线路可售范围，职责是按实际餐盘、方向、预算与饮食限制筛选餐位。
    public static class ConveyorDiningAvailability
    {
        //枚举能够到达指定餐位的现有盘子，断电时只提供餐位当前格的盘子。
        public static IEnumerable<Building_SushiConveyor> ReachablePlates(Building_SushiConveyor seatBelt)
        {
            var line = seatBelt.Line;
            if (line == null || !line.SameShop) yield break;
            bool powered = line.Powered;
            foreach (var source in line.segments)
            {
                if (source.Food == null || ConveyorStockPlanner.HoldsPlate(source)) continue;
                var current = source;
                for (int i = 0; i < line.segments.Count && current != null; i++)
                {
                    if (current == seatBelt) { yield return source; break; }
                    if (!powered) break;
                    current = ConveyorLinks.Next(current);
                }
            }
        }

        //取得盘子当前销售规则，职责是拒绝下架、腐坏和来源失效食品。
        public static ConveyorStockRule Rule(Building_SushiConveyor source) =>
            source.Food == null || ConveyorStockPlanner.NeedsCleaning(source) ? null
                : source.Line.rules.FirstOrDefault(r => r.id == source.plate?.ruleId && r.enabled);

        //检查顾客是否愿意并有预算吃完该盘，职责是使用实际成分与原版饮食政策。
        public static bool Accepts(Pawn customer, Building_SushiConveyor source)
        {
            var rule = Rule(source);
            var shop = source.Line?.Shop;
            if (rule == null || shop == null || !source.Line.SameShop || !customer.WillEat(source.Food, customer, true, false)
                || customer.carryTracker.MaxStackSpaceEver(source.Food.def) < source.Food.stackCount) return false;
            var session = RestaurantOrderUtility.OrderManager.ForCustomer(customer.thingIDNumber, shop.ID);
            return rule.price * source.Food.stackCount <= RestaurantProductMenuUtility.RemainingBudget(customer, shop)
                && (customer.needs?.food?.NutritionWanted ?? 0f) - (session?.ExpectedNutrition ?? 0f) > 0.05f;
        }

        //检查指定座位是否有可到达且合适的现货。
        public static bool CanServe(Pawn customer, Zone_Shop shop, Building_SushiConveyor belt) =>
            belt.Line?.Shop == shop && ReachablePlates(belt).Any(source => Accepts(customer, source));

        //检查线路是否能提供自助服务，职责是让已有现货免于服务员和灶台门槛。
        public static bool HasOffer(Zone_Shop shop)
        {
            var manager = shop.Map.GetComponent<MapComponent_SushiConveyor>();
            manager.Rebuild();
            return manager.lines.Where(l => l.Shop == shop && l.SameShop).SelectMany(l => l.segments)
                .Any(b => Rule(b) != null && GenAdj.CardinalDirections.Any(d =>
                    shop.Cells.Contains(b.Position + d) && (b.Position + d).GetThingList(shop.Map)
                        .Any(t => t.def.building?.isSittable == true)));
        }

        //检查普通桌面是否有可供应菜单，职责是防止自助可营业时把顾客分配到空厨房餐位。
        public static bool HasOrdinaryOffer(Zone_Shop shop)
        {
            var settings = RestaurantOrderUtility.Settings.GetOrCreate(shop.ID);
            return RestaurantProductMenuUtility.AllMenus(shop, settings).Any(menu =>
                (menu.IsStockProduct ? RestaurantProductMenuUtility.Available(shop, menu, menu.minCount)
                    : RestaurantIngredientUtility.HasIngredients(null, shop, menu, menu.minCount))
                && RestaurantBusinessAvailability.CheckOrder(shop, RestaurantOrderCreationUtility.BuildPreview(
                    new Models.RestaurantMenuSelection { menuItem = menu, count = menu.minCount })).NullOrEmpty());
        }
    }
}
