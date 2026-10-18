using SimManagementLib.Tool;
using System;
using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.Models;
using RimWorld;
using Verse;

namespace RimSimRestaurantExtension.Tool
{
    //收集餐厅食品并区分可制作菜品和只能供应现货的商品。
    public static class RestaurantFoodUtility
    {
        private static List<ThingDef> cachedFoods;

        //缓存全部有营养的可食用商品，选择界面另行标明制作能力。
        public static List<ThingDef> AllMenuFoods()
        {
            if (cachedFoods != null)
                return cachedFoods;

            cachedFoods = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(def => IsEdibleProduct(def) && def.IsNutritionGivingIngestible)
                .OrderByDescending(def => def.ingestible?.IsMeal == true)
                .ThenBy(def => def.LabelCap.ToString())
                .ThenBy(def => def.defName)
                .ToList();
            return cachedFoods;
        }

        //识别可供人类食用的可搬运食品，酒水不受营养值限制。
        public static bool IsEdibleProduct(ThingDef def)
        {
            return def != null && def.category == ThingCategory.Item && def.EverHaulable
                && !def.IsCorpse && def.ingestible?.HumanEdible == true;
        }

        //厨房菜单只接受能由普通配方制作的食品，不依赖原版正餐分类。
        public static bool IsMenuFood(ThingDef def)
        {
            return IsEdibleProduct(def) && def.IsNutritionGivingIngestible
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
            if (def?.ingestible == null) return SimTranslation.T("RSR.Food.NoNutrition");
            string nutrition = def.ingestible.CachedNutrition.ToString("F2");
            string preferability = def.ingestible.preferability >= FoodPreferability.MealLavish ? SimTranslation.T("RSR.Food.Lavish")
                : def.ingestible.preferability >= FoodPreferability.MealFine ? SimTranslation.T("RSR.Food.Fine")
                : def.ingestible.IsMeal ? SimTranslation.T("RSR.Food.Simple") : SimTranslation.T("RSR.Food.Edible");
            string source = def.modContentPack?.Name ?? "Core";
            return SimTranslation.T("RSR.Food.Summary", (nutrition).Named("nutrition"), (preferability).Named("quality"), (source).Named("source"));
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
