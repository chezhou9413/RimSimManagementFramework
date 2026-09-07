using System;
using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.Models;
using RimWorld;
using Verse;

namespace RimSimRestaurantExtension.Tool
{
    //收集和筛选餐厅可售食物 Def，职责是只向菜单提供确实存在普通生产配方的餐品。
    public static class RestaurantFoodUtility
    {
        private static List<ThingDef> cachedFoods;

        //返回所有适合作为餐厅菜单产物的食物，职责是缓存稳定的 Def 列表供选择器使用。
        public static List<ThingDef> AllMenuFoods()
        {
            if (cachedFoods != null)
                return cachedFoods;

            cachedFoods = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(IsMenuFood)
                .OrderByDescending(def => def.ingestible?.IsMeal == true)
                .ThenBy(def => def.LabelCap.ToString())
                .ThenBy(def => def.defName)
                .ToList();
            return cachedFoods;
        }

        //判断物品是否能作为菜单餐品，职责是排除非餐品、不可食用物和没有生产配方的假菜单。
        public static bool IsMenuFood(ThingDef def)
        {
            if (def == null || def.IsCorpse || def.IsDrug) return false;
            if (!def.IsNutritionGivingIngestible || def.ingestible == null) return false;
            return def.ingestible.HumanEdible
                && def.ingestible.IsMeal
                && RestaurantCookingUtility.HasProductionRecipe(def);
        }

        //判断食物是否符合搜索文本，职责是支持中文标签、英文 DefName 和模组名检索。
        public static bool MatchesSearch(ThingDef def, string search)
        {
            if (def == null) return false;
            if (search.NullOrEmpty()) return true;
            string text = search.Trim();
            return Contains(def.LabelCap.ToString(), text)
                || Contains(def.label, text)
                || Contains(def.defName, text)
                || Contains(def.modContentPack?.Name, text);
        }

        //返回食物摘要，职责是在选择器和菜单卡片中显示营养、偏好和来源。
        public static string BuildFoodSummary(ThingDef def)
        {
            if (def?.ingestible == null) return "无营养数据";
            string nutrition = def.ingestible.CachedNutrition.ToString("F2");
            string preferability = def.ingestible.preferability.ToString();
            string source = def.modContentPack?.Name ?? "Core";
            return $"营养 {nutrition} · {preferability} · {source}";
        }

        //返回食物是否为正式餐品，职责是给界面显示稳定分类。
        public static bool IsCookedMealLike(ThingDef def)
        {
            return def?.ingestible?.IsMeal == true;
        }

        //判断顾客是否接受菜单的真实成分，职责是把餐品 Def、食物政策和禁忌动物食材一起纳入点菜过滤。
        public static bool WillCustomerEatMenu(Pawn customer, RestaurantMenuItem item)
        {
            if (customer == null || item?.MealDef == null) return false;
            Thing sample = ThingMaker.MakeThing(item.MealDef);
            CompIngredients comp = sample.TryGetComp<CompIngredients>();
            if (comp != null && item.ingredients != null)
            {
                for (int i = 0; i < item.ingredients.Count; i++)
                {
                    ThingDef ingredient = item.ingredients[i]?.ThingDef;
                    if (ingredient != null) comp.RegisterIngredient(ingredient);
                }
            }
            return customer.WillEat(sample, customer, careIfNotAcceptableForTitle: true, allowVenerated: false);
        }

        //执行不区分大小写的包含判断，职责是兼容空文本。
        private static bool Contains(string value, string search)
        {
            return !value.NullOrEmpty()
                && value.IndexOf(search ?? "", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
