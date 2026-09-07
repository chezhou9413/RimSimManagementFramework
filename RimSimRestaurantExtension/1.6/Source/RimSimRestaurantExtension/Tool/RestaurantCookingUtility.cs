using System;
using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.Models;
using RimWorld;
using SimManagementLib.Api;
using SimManagementLib.SimZone;
using UnityEngine;
using Verse;

namespace RimSimRestaurantExtension.Tool
{
    //提供餐厅厨房设施和产出工具，职责是让菜单、派工和实际制作共同遵守原版配方约束。
    public static class RestaurantCookingUtility
    {
        //返回店内可处理指定订单的工作台，职责是同时验证工作格、能源、配方和菜单食材。
        public static List<Building_WorkTable> FindUsableStoves(Zone_Shop shop, RestaurantOrder order = null)
        {
            if (shop?.Map == null) return new List<Building_WorkTable>();
            HashSet<Building_WorkTable> result = new HashSet<Building_WorkTable>();
            foreach (IntVec3 cell in shop.Cells)
            {
                List<Thing> things = shop.Map.thingGrid.ThingsListAt(cell);
                for (int i = 0; i < things.Count; i++)
                {
                    if (things[i] is Building_WorkTable table
                        && IsWorkCellInsideShop(shop, table)
                        && CanCookOrderAt(table, order))
                    {
                        result.Add(table);
                    }
                }
            }
            return result.ToList();
        }

        //判断工作台交互格是否属于同一商店，职责是避免食材被搬到区域外后订单无法恢复。
        public static bool IsWorkCellInsideShop(Zone_Shop shop, Thing stove)
        {
            if (shop == null || stove == null || !stove.InteractionCell.IsValid) return false;
            foreach (IntVec3 cell in shop.Cells)
                if (cell == stove.InteractionCell) return true;
            return false;
        }

        //判断工作台是否可以处理订单，职责是统一能源、故障、研究、产物与食材数量检查。
        public static bool CanCookOrderAt(Thing thing, RestaurantOrder order)
        {
            if (!(thing is Building_WorkTable table) || table.Destroyed || !table.Spawned || table.IsBurning())
                return false;
            if (!table.CurrentlyUsableForBills()) return false;
            if (order == null)
            {
                return table.def?.AllRecipes?.Any(recipe => IsRestaurantProductionRecipe(recipe)
                    && recipe.AvailableNow
                    && recipe.AvailableOnNow(table)) == true;
            }
            return GetRecipeForOrder(order, table) != null;
        }

        //判断厨师能否在工作台制作订单，职责是补充原版配方技能门槛检查。
        public static bool CanPawnCookOrderAt(Pawn cook, Thing stove, RestaurantOrder order)
        {
            if (!(stove is Building_WorkTable table) || table.Destroyed || !table.Spawned)
                return false;
            RecipeDef recipe = GetRecipeForOrder(order, table);
            return cook != null && recipe != null && recipe.PawnSatisfiesSkillRequirements(cook);
        }

        //判断商店是否有当前可接单的厨师，职责是把岗位分配、人员状态、灶台和配方技能统一为菜单硬条件。
        public static bool HasAvailableCook(Zone_Shop shop, RestaurantOrder order)
        {
            if (shop?.Map?.mapPawns == null || order == null || DefOfRefs.RSR_WorkGiver_CookRestaurantOrder == null)
                return false;
            List<Building_WorkTable> stoves = FindUsableStoves(shop, order);
            return stoves.Count > 0 && shop.Map.mapPawns.AllPawnsSpawned.Any(pawn => pawn != null
                && pawn.Faction == Faction.OfPlayer && (pawn.RaceProps.Humanlike || pawn.IsColonyMech)
                && !pawn.Dead && !pawn.Downed && !pawn.Drafted && !pawn.InMentalState
                && pawn.health?.capacities?.CapableOf(PawnCapacityDefOf.Manipulation) == true
                && SimShopStaffApi.IsAssignedToWorkGiver(shop, pawn, DefOfRefs.RSR_WorkGiver_CookRestaurantOrder)
                && stoves.Any(stove => CanPawnCookOrderAt(pawn, stove, order)));
        }

        //返回所有能生产指定餐品的普通配方，职责是支持其他模组餐品而不依赖硬编码 DefName。
        public static List<RecipeDef> GetProductionRecipes(ThingDef mealDef)
        {
            if (mealDef == null) return new List<RecipeDef>();
            return DefDatabase<RecipeDef>.AllDefsListForReading
                .Where(recipe => IsRestaurantProductionRecipe(recipe)
                    && recipe.products[0].thingDef == mealDef)
                .ToList();
        }

        //判断餐品是否存在可识别的普通生产配方，职责是阻止界面创建永远无法制作的菜单。
        public static bool HasProductionRecipe(ThingDef mealDef)
        {
            return GetProductionRecipes(mealDef).Count > 0;
        }

        //根据订单和工作台选择真实配方，职责是只返回产物完全匹配且菜单食材足量兼容的配方。
        public static RecipeDef GetRecipeForOrder(RestaurantOrder order, Building_WorkTable table = null)
        {
            if (order?.mealDef == null) return null;
            IEnumerable<RecipeDef> recipes = table?.def?.AllRecipes ?? GetProductionRecipes(order.mealDef);
            if (order.recipe != null) recipes = recipes.Where(recipe => recipe == order.recipe);
            return recipes
                .Where(recipe => IsRestaurantProductionRecipe(recipe)
                    && recipe.products[0].thingDef == order.mealDef
                    && recipe.AvailableNow
                    && (table == null || recipe.AvailableOnNow(table))
                    && CanRecipeUseOrderIngredients(recipe, order))
                .OrderBy(recipe => recipe.displayPriority)
                .FirstOrDefault();
        }

        //判断菜单食材能否满足配方，职责是按原版 IngredientValueGetter 计算整单批次数量并避免凭空出餐。
        public static bool CanRecipeUseOrderIngredients(RecipeDef recipe, RestaurantOrder order)
        {
            if (recipe == null || order == null || recipe.ingredients.NullOrEmpty()) return false;
            List<RestaurantIngredientRequirement> needs = order.GetTotalIngredientNeeds();
            if (needs.Count == 0 || needs.Any(need => need.ThingDef == null || need.countPerMeal <= 0)) return false;

            if (needs.Any(need => !recipe.ingredients.Any(slot => AllowsIngredient(recipe, slot, need.ThingDef))))
                return false;
            int output = Mathf.Max(1, recipe.products[0].count);
            int batches = Mathf.CeilToInt(order.mealCount / (float)output);
            Dictionary<ThingDef, int> available = needs
                .GroupBy(need => need.ThingDef)
                .ToDictionary(group => group.Key, group => group.Sum(need => need.countPerMeal));

            IEnumerable<IngredientCount> slots = recipe.ingredients.OrderByDescending(slot => slot.IsFixedIngredient);
            foreach (IngredientCount slot in slots)
            {
                float requiredValue = slot.GetBaseCount() * batches;
                int usedKinds = 0;
                foreach (ThingDef def in available.Keys.ToList())
                {
                    int count = available[def];
                    if (count <= 0 || !AllowsIngredient(recipe, slot, def)) continue;
                    float valuePerUnit = recipe.IngredientValueGetter.ValuePerUnitOf(def);
                    if (valuePerUnit <= 0f) continue;
                    int take = Mathf.Min(count, Mathf.CeilToInt(requiredValue / valuePerUnit));
                    if (take <= 0) continue;
                    available[def] -= take;
                    requiredValue -= take * valuePerUnit;
                    usedKinds++;
                    if (requiredValue <= 0.0001f) break;
                }
                if (requiredValue > 0.0001f) return false;
                if (!recipe.allowMixingIngredients && usedKinds > 1) return false;
            }
            return true;
        }

        //计算订单制作时长，职责是按真实配方批次、厨师速度和工作台速度换算工作量。
        public static int GetCookTicks(Pawn cook, Thing stove, RestaurantOrder order)
        {
            RecipeDef recipe = GetRecipeForOrder(order, stove as Building_WorkTable);
            float work = recipe?.WorkAmountTotal(null) ?? 900f;
            int output = Mathf.Max(1, recipe?.products?.FirstOrDefault()?.count ?? 1);
            int batches = Mathf.CeilToInt(Mathf.Max(1, order?.mealCount ?? 1) / (float)output);
            work *= Mathf.Max(1, batches);
            if (cook != null && recipe?.workSpeedStat != null)
                work /= Mathf.Max(0.1f, cook.GetStatValue(recipe.workSpeedStat));
            if (stove != null && recipe?.workTableSpeedStat != null)
                work /= Mathf.Max(0.1f, stove.GetStatValue(recipe.workTableSpeedStat));
            return Mathf.Clamp(Mathf.RoundToInt(work), 180, 12000);
        }

        //生成订单餐品，职责是使用原版配方生成品质与成分并按订单批量合并成一堆。
        public static Thing MakeCookedMeal(Pawn cook, Thing stove, RestaurantOrder order, List<Thing> ingredients)
        {
            RecipeDef recipe = GetRecipeForOrder(order, stove as Building_WorkTable);
            if (cook?.Map == null || order?.mealDef == null || recipe == null) return null;
            Thing dominant = ingredients.OrderByDescending(thing => thing.stackCount).FirstOrDefault();
            Thing meal = GenRecipe.MakeRecipeProducts(recipe, cook, ingredients, dominant, stove as IBillGiver)
                .FirstOrDefault(product => product != null && product.def == order.mealDef);
            if (meal == null) return null;
            meal.stackCount = Mathf.Clamp(order.mealCount, 1, Mathf.Max(1, order.mealDef.stackLimit));
            return meal;
        }

        //通知工作台本 Tick 正在工作，职责是维持燃料消耗和工作特效。
        public static void NotifyUsedThisTick(Thing stove)
        {
            (stove as Building_WorkTable)?.UsedThisTick();
        }

        //判断配方是否为单一餐品产出的普通制作配方，职责是排除手术、特殊产物和副产品流程。
        private static bool IsRestaurantProductionRecipe(RecipeDef recipe)
        {
            ThingDef product = recipe?.products?.Count == 1 ? recipe.products[0]?.thingDef : null;
            return recipe != null && !recipe.IsSurgery && product != null && recipe.products[0].count == 1
                && product?.ingestible?.IsMeal == true && product.IsNutritionGivingIngestible;
        }

        //判断食材是否能填入指定配方槽，职责是同时遵守槽过滤器与配方总过滤器。
        private static bool AllowsIngredient(RecipeDef recipe, IngredientCount slot, ThingDef def)
        {
            return recipe != null && slot != null && def != null
                && slot.filter.Allows(def)
                && (slot.IsFixedIngredient || recipe.fixedIngredientFilter.Allows(def));
        }

    }
}
