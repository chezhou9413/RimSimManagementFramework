using System;
using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.Models;
using RimWorld;
using SimManagementLib.SimZone;
using Verse;
using Verse.AI;

namespace RimSimRestaurantExtension.Tool
{
    //提供餐厅食材库存与取料工具，职责是让点菜检查、厨师派工和实际消耗使用同一份总需求。
    public static class RestaurantIngredientUtility
    {
        //判断菜单份数是否有足量店内库存，职责是阻止顾客点到当前无法制作的菜品。
        public static bool HasIngredients(Pawn actor, Zone_Shop shop, RestaurantMenuItem item, int mealCount)
        {
            if (shop?.Map == null || item == null || mealCount <= 0) return false;
            List<RestaurantIngredientRequirement> needs = BuildNeeds(item, mealCount);
            return needs.Count > 0 && needs.All(need => need.ThingDef != null
                && CountUncommitted(shop, need.ThingDef) >= need.countPerMeal);
        }

        //为订单挑选实际食材堆，职责是按总需求生成与 Job 目标队列一一对应的数量列表。
        public static bool TryFindIngredientThingCounts(Pawn actor, Zone_Shop shop, RestaurantOrder order, out List<ThingCount> chosen, out string failReason)
        {
            failReason = "";
            chosen = new List<ThingCount>();
            if (shop?.Map == null || order == null) { failReason = "店铺或订单无效"; return false; }
            if (order.orderId > 0 && order.menuConfirmed)
            {
                chosen = Inventory.RestaurantOrderStock.Reserved(order, shop.Map);
                if (chosen.Count == 0 && order.state == RestaurantOrderState.WaitingCook)
                {
                    if (Inventory.RestaurantOrderStock.Reserve(order, shop))
                        chosen = Inventory.RestaurantOrderStock.Reserved(order, shop.Map);
                }
                var reservedItems = chosen;
                bool valid = chosen.Count > 0 && chosen.All(t => t.Thing != null && !t.Thing.Destroyed
                    && t.Count <= t.Thing.stackCount && Inventory.RestaurantStockUtility.Reachable(actor, t.Thing, Inventory.RestaurantStockUtility.Key(order)))
                    && order.GetTotalIngredientNeeds().All(n => reservedItems.Where(t => t.Thing.def == n.ThingDef).Sum(t => t.Count) == n.countPerMeal);
                if (!valid) failReason = "预留食材不足或当前厨师无法取料";
                return valid;
            }
            bool found = Inventory.RestaurantStockUtility.Select(shop, order, actor, out chosen);
            if (!found) failReason = "冰箱和绑定后厨储存区食材不足或不可达";
            return found;
        }

        //汇总菜单项的订单总需求，职责是把每份配置乘以点菜份数并合并重复食材。
        public static List<RestaurantIngredientRequirement> BuildNeeds(RestaurantMenuItem item, int mealCount)
        {
            Dictionary<string, int> totals = new Dictionary<string, int>();
            if (item?.ingredients == null) return new List<RestaurantIngredientRequirement>();
            int count = Math.Max(1, mealCount);
            for (int i = 0; i < item.ingredients.Count; i++)
            {
                RestaurantIngredientRequirement ingredient = item.ingredients[i];
                if (ingredient == null || ingredient.thingDefName.NullOrEmpty()) continue;
                int amount = Math.Max(1, ingredient.countPerMeal) * count;
                totals[ingredient.thingDefName] = totals.TryGetValue(ingredient.thingDefName, out int current)
                    ? current + amount
                    : amount;
            }
            return totals.Select(pair => new RestaurantIngredientRequirement
            {
                thingDefName = pair.Key,
                countPerMeal = pair.Value
            }).ToList();
        }

        //为新选择的餐品生成一份有效食材配置，职责是按当前商店兼容配方优先选择库存最多的原料。
        public static List<RestaurantIngredientRequirement> BuildDefaultMenuIngredients(Zone_Shop shop, ThingDef mealDef)
        {
            RecipeDef recipe = FindMenuRecipe(shop, mealDef);
            if (recipe == null || recipe.ingredients.NullOrEmpty()) return new List<RestaurantIngredientRequirement>();
            Dictionary<string, int> totals = new Dictionary<string, int>();
            for (int i = 0; i < recipe.ingredients.Count; i++)
            {
                IngredientCount slot = recipe.ingredients[i];
                ThingDef selected = slot.filter.AllowedThingDefs
                    .Where(def => def != null && (slot.IsFixedIngredient || recipe.fixedIngredientFilter.Allows(def)))
                    .OrderByDescending(def => CountAvailable(null, shop, def))
                    .ThenBy(def => def.BaseMarketValue)
                    .FirstOrDefault();
                if (selected == null) return new List<RestaurantIngredientRequirement>();
                int count = Math.Max(1, slot.CountRequiredOfFor(selected, recipe));
                totals[selected.defName] = totals.TryGetValue(selected.defName, out int current) ? current + count : count;
            }
            return totals.Select(pair => new RestaurantIngredientRequirement
            {
                thingDefName = pair.Key,
                countPerMeal = pair.Value
            }).ToList();
        }

        //统计店内可用食材数量，职责是给菜单状态和点菜预算筛选提供一致库存值。
        public static int CountAvailable(Pawn actor, Zone_Shop shop, ThingDef ingredientDef)
        {
            return shop?.Map == null ? 0 : FindIngredientStacks(actor, shop, ingredientDef)
                .Sum(stack => shop.Map.GetComponent<SimManagementLib.SimMapComp.MapComponent_InventoryReservations>().Available(stack));
        }

        //计算整单食材市场成本，职责是让财务账单按实际消耗记录利润而不是把全部收入视为净利。
        public static float CalculateIngredientCost(List<RestaurantIngredientRequirement> needs)
        {
            if (needs == null) return 0f;
            float total = 0f;
            for (int i = 0; i < needs.Count; i++)
            {
                ThingDef def = needs[i]?.ThingDef;
                if (def != null) total += Math.Max(0f, def.BaseMarketValue) * Math.Max(0, needs[i].countPerMeal);
            }
            return total;
        }

        //统计可供新订单使用的库存，职责是扣除已接单但尚未完成消耗的食材承诺。
        public static int CountUncommitted(Zone_Shop shop, ThingDef ingredientDef)
        {
            return CountAvailable(null, shop, ingredientDef);
        }

        //查找商店实际工作台可用的餐品配方，职责是让自动食材配置匹配当前餐厅设施。
        private static RecipeDef FindMenuRecipe(Zone_Shop shop, ThingDef mealDef)
        {
            List<RecipeDef> recipes = RestaurantCookingUtility.GetProductionRecipes(mealDef);
            if (recipes.Count == 0) return null;
            if (shop?.Map != null)
            {
                foreach (IntVec3 cell in shop.Cells)
                {
                    List<Thing> things = shop.Map.thingGrid.ThingsListAt(cell);
                    for (int i = 0; i < things.Count; i++)
                    {
                        if (!(things[i] is Building_WorkTable table) || !RestaurantCookingUtility.IsWorkCellInsideShop(shop, table))
                            continue;
                        RecipeDef match = table.def?.AllRecipes?
                            .Where(recipes.Contains)
                            .Where(recipe => recipe.AvailableNow && recipe.AvailableOnNow(table))
                            .OrderBy(recipe => recipe.displayPriority)
                            .FirstOrDefault();
                        if (match != null) return match;
                    }
                }
            }
            return recipes.Where(recipe => recipe.AvailableNow).OrderBy(recipe => recipe.displayPriority).FirstOrDefault();
        }

        //枚举店内可用食材堆，职责是按需要附加厨师的禁用、预约和可达检查。
        private static IEnumerable<Thing> FindIngredientStacks(Pawn actor, Zone_Shop shop, ThingDef ingredientDef)
        {
            return Inventory.RestaurantStockUtility.Sources(shop, ingredientDef, actor);
        }
    }
}
