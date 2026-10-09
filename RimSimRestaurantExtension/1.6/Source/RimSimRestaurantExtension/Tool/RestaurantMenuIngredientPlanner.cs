using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.Inventory;
using RimSimRestaurantExtension.Models;
using RimWorld;
using SimManagementLib.SimMapComp;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using UnityEngine;
using Verse;

namespace RimSimRestaurantExtension.Tool
{
    //根据殖民地现有库存为菜品生成足量的一份配料，不改变已经确认的订单。
    internal static class RestaurantMenuIngredientPlanner
    {
        //只采用本店当前工作台能制作的配方，库存不足时保留原草稿并报告缺口。
        public static bool TryFill(Zone_Shop shop, RestaurantMenuItem item, int portions, out string message)
        {
            var ledger = shop.Map.GetComponent<MapComponent_InventoryReservations>();
            var sources = RestaurantColonyStorage.Items(shop.Map).Concat(RestaurantStockUtility.Cabinets(shop)
                .Where(c => c.IsRefrigerator).SelectMany(c => c.GetDirectlyHeldThings().Where(t => c.AllowsInventoryItem(t.def))));
            var inventory = sources.Distinct().Where(t => RestaurantStockUtility.Reachable(null, t))
                .GroupBy(t => t.def).ToDictionary(g => g.Key, g => g.Sum(t => ledger.Available(t)) / portions);
            var supported = RestaurantCookingUtility.GetProductionRecipes(item.MealDef);
            var recipes = RestaurantCookingUtility.FindUsableStoves(shop).SelectMany(s => s.def.AllRecipes
                .Where(r => supported.Contains(r) && r.AvailableNow && r.AvailableOnNow(s))).Distinct()
                .OrderBy(r => r.products[0].count).ThenBy(r => r.displayPriority).ToList();
            message = SimTranslation.T("RSR.UI.NoUsableRecipe");
            foreach (var recipe in recipes)
            {
                if (!TryAllocate(recipe, inventory, out var ingredients, out message)) continue;
                var preview = new RestaurantOrder { mealDef = item.MealDef, mealCount = 1, ingredients = ingredients };
                if (!RestaurantCookingUtility.CanRecipeUseOrderIngredients(recipe, preview)) continue;
                item.ingredients = ingredients;
                message = SimTranslation.T("RSR.UI.AutoIngredientsDone");
                return true;
            }
            return false;
        }

        //按槽分配库存，禁止混料的槽必须由一种足量食材满足。
        private static bool TryAllocate(RecipeDef recipe, Dictionary<ThingDef, int> inventory,
            out List<RestaurantIngredientRequirement> ingredients, out string message)
        {
            ingredients = new List<RestaurantIngredientRequirement>();
            message = "";
            var remaining = new Dictionary<ThingDef, int>(inventory);
            var used = new Dictionary<ThingDef, int>();
            foreach (IngredientCount slot in RestaurantCookingUtility.OrderedIngredientSlots(recipe))
            {
                float required = slot.GetBaseCount() / recipe.products[0].count;
                var candidates = remaining.Keys.Where(d => RestaurantCookingUtility.AllowsIngredient(recipe, slot, d)
                    && remaining[d] > 0 && recipe.IngredientValueGetter.ValuePerUnitOf(d) > 0)
                    .OrderByDescending(d => remaining[d]).ThenBy(d => d.BaseMarketValue).ToList();
                if (!recipe.allowMixingIngredients)
                    candidates = candidates.Where(d => remaining[d] * recipe.IngredientValueGetter.ValuePerUnitOf(d)
                        + 0.0001f >= required).Take(1).ToList();
                foreach (ThingDef def in candidates)
                {
                    float value = recipe.IngredientValueGetter.ValuePerUnitOf(def);
                    int count = Mathf.Min(remaining[def], Mathf.CeilToInt((required - 0.0001f) / value));
                    if (count <= 0) continue;
                    remaining[def] -= count;
                    used[def] = used.TryGetValue(def, out int previous) ? previous + count : count;
                    required -= count * value;
                    if (required <= 0.0001f) break;
                }
                if (required > 0.0001f)
                {
                    message = SimTranslation.T("RSR.UI.AutoIngredientsMissing", slot.filter.Summary.Named("ingredient"));
                    return false;
                }
            }
            ingredients = used.Select(pair => new RestaurantIngredientRequirement
            { thingDefName = pair.Key.defName, countPerMeal = pair.Value }).ToList();
            return ingredients.Count > 0;
        }
    }
}
