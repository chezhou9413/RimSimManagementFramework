using System;
using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.GameComp;
using RimSimRestaurantExtension.Models;
using RimSimRestaurantExtension.Tool;
using RimWorld;
using Verse;

namespace RimSimRestaurantExtension.Debug
{
    //准备完整餐厅的默认菜单和实体食材，职责是让测试餐厅生成后立即具备可消耗库存。
    internal static class CompleteRestaurantInventoryUtility
    {
        private const int SupportedMaximumOrdersPerMenu = 10;

        //建立默认菜单和库存计划，职责是提前验证所有餐品与食材 Def 并计算测试库存量。
        public static bool TryBuildStockPlan(out List<RestaurantMenuItem> menuItems,
            out Dictionary<ThingDef, int> stockPlan, out string failReason)
        {
            menuItems = RestaurantShopSettings.CreateDefaultMenu();
            stockPlan = new Dictionary<ThingDef, int>();
            failReason = "";

            for (int i = 0; i < menuItems.Count; i++)
            {
                RestaurantMenuItem menuItem = menuItems[i];
                if (menuItem?.MealDef == null || !RestaurantCookingUtility.HasProductionRecipe(menuItem.MealDef))
                {
                    failReason = "默认菜单缺少可制作餐品：" + (menuItem?.mealDefName ?? "未定义");
                    return false;
                }

                for (int n = 0; n < menuItem.ingredients.Count; n++)
                {
                    RestaurantIngredientRequirement ingredient = menuItem.ingredients[n];
                    ThingDef ingredientDef = ingredient?.ThingDef;
                    if (ingredientDef == null)
                    {
                        failReason = "默认菜单缺少食材 Def：" + (ingredient?.thingDefName ?? "未定义");
                        return false;
                    }

                    int amount = Math.Max(1, ingredient.countPerMeal)
                                 * Math.Max(1, menuItem.maxCount)
                                 * SupportedMaximumOrdersPerMenu;
                    stockPlan[ingredientDef] = stockPlan.TryGetValue(ingredientDef, out int current)
                        ? current + amount
                        : amount;
                }
            }
            return menuItems.Count > 0 && stockPlan.Count > 0;
        }

        //判断燃料灶是否支持全部默认菜单，职责是避免生成设施齐全但无法制作菜品的餐厅。
        public static bool CanCookAllMenuItems(ThingDef stoveDef, IReadOnlyList<RestaurantMenuItem> menuItems,
            out string failReason)
        {
            failReason = "";
            if (stoveDef?.AllRecipes == null || menuItems == null)
            {
                failReason = "燃料灶或默认菜单不可用";
                return false;
            }

            for (int i = 0; i < menuItems.Count; i++)
            {
                RestaurantMenuItem item = menuItems[i];
                bool supported = RestaurantCookingUtility.GetProductionRecipes(item?.MealDef)
                    .Any(recipe => stoveDef.AllRecipes.Contains(recipe) && recipe.AvailableNow);
                if (supported) continue;
                failReason = "燃料灶不支持菜单：" + (item?.DisplayLabel ?? "未定义餐品");
                return false;
            }
            return true;
        }

        //把库存计划生成到厨房空地，职责是按堆叠上限拆分食材且保留真实地图 Thing。
        public static bool TrySpawnStock(Map map, IReadOnlyList<IntVec3> cells,
            Dictionary<ThingDef, int> stockPlan,
            out int spawnedCount, out string failReason)
        {
            spawnedCount = 0;
            failReason = "";
            int cellIndex = 0;

            foreach (KeyValuePair<ThingDef, int> stock in stockPlan.OrderBy(pair => pair.Key.defName))
            {
                int remaining = stock.Value;
                while (remaining > 0)
                {
                    if (cellIndex >= cells.Count)
                    {
                        failReason = "厨房食材区空间不足";
                        return false;
                    }
                    Thing stack = ThingMaker.MakeThing(stock.Key);
                    stack.stackCount = Math.Min(remaining, Math.Max(1, stock.Key.stackLimit));
                    remaining -= stack.stackCount;
                    spawnedCount += stack.stackCount;
                    GenSpawn.Spawn(stack, cells[cellIndex++], map, WipeMode.Vanish);
                }
            }
            return true;
        }
    }
}
