using System;
using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.GameComp;
using RimSimRestaurantExtension.Models;
using RimWorld;
using SimManagementLib.Api;
using SimManagementLib.SimZone;
using UnityEngine;
using Verse;

namespace RimSimRestaurantExtension.Tool
{
    //提供菜单候选、顾客偏好评分和临时选择缓存，职责是让预算检查与最终落单使用完全相同的结果。
    public static class RestaurantMenuUtility
    {
        private const int SelectionLifetimeTicks = 600;
        private static readonly Dictionary<string, RestaurantMenuSelection> selections = new Dictionary<string, RestaurantMenuSelection>();

        //清空跨帧点菜缓存，职责是在新游戏或读档时防止旧游戏的菜单对象被错误复用。
        public static void ResetSelections()
        {
            selections.Clear();
        }

        //返回店铺启用且配置有效的菜单项，职责是统一 UI 与点菜逻辑的基础过滤。
        public static List<RestaurantMenuItem> GetEnabledMenuItems(RestaurantShopSettings settings)
        {
            settings?.Normalize();
            if (settings?.menuItems == null) return new List<RestaurantMenuItem>();
            return settings.menuItems
                .Where(item => item != null && item.enabled && item.unitPrice > 0f && RestaurantFoodUtility.IsMenuFood(item.MealDef))
                .ToList();
        }

        //取得或创建顾客本次点菜选择，职责是避免动作可用性、权重与订单创建重复随机。
        public static RestaurantMenuSelection GetOrCreateSelection(Pawn customer, Thing provider, Zone_Shop shop)
        {
            string key = MakeKey(customer, provider, shop);
            if (key.NullOrEmpty()) return null;
            int now = Find.TickManager?.TicksGame ?? 0;
            if (selections.TryGetValue(key, out RestaurantMenuSelection cached)
                && cached?.IsValid == true
                && now - cached.createdTick <= SelectionLifetimeTicks)
            {
                return cached;
            }

            selections.Remove(key);
            RestaurantMenuSelection selection = CreateSelection(customer, provider, shop);
            if (selection?.IsValid == true)
                selections[key] = selection;
            return selection;
        }

        //清理指定顾客的点菜缓存，职责是确保成功落单或失败退出后不会复用旧价格和库存。
        public static void ClearSelection(Pawn customer, Thing provider, Zone_Shop shop)
        {
            string key = MakeKey(customer, provider, shop);
            if (!key.NullOrEmpty()) selections.Remove(key);
        }

        //清理过期选择，职责是控制静态缓存规模并移除已离开流程的顾客数据。
        public static void CleanupSelections()
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            List<string> expired = selections
                .Where(pair => pair.Value == null || now - pair.Value.createdTick > SelectionLifetimeTicks)
                .Select(pair => pair.Key)
                .ToList();
            for (int i = 0; i < expired.Count; i++)
                selections.Remove(expired[i]);
            if (selections.Count > 512)
                selections.Clear();
        }

        //判断店铺是否至少有一道可点菜单，职责是给顾客动作入口提供无副作用的可用性结果。
        public static bool HasAvailableMenu(Pawn customer, Thing provider, Zone_Shop shop)
        {
            return GetOrCreateSelection(customer, provider, shop)?.IsValid == true;
        }

        //创建一次点菜选择，职责是结合预算、库存、饮食限制、份量和个体偏好生成加权候选。
        private static RestaurantMenuSelection CreateSelection(Pawn customer, Thing provider, Zone_Shop shop)
        {
            if (customer == null || provider == null || shop == null) return null;
            RestaurantShopSettings settings = RestaurantOrderUtility.Settings?.GetOrCreate(shop.ID);
            if (settings == null || !settings.enabled) return null;
            float remainingBudget = Inventory.RestaurantProductMenuUtility.RemainingBudget(customer, shop);
            if (remainingBudget <= 0f) return null;

            RestaurantCustomerPreference preference = settings.useCustomerPreferences
                ? RestaurantOrderUtility.Preferences?.GetOrCreate(customer)
                : null;
            float strength = settings.useCustomerPreferences ? settings.preferenceStrength : 0f;
            List<RestaurantMenuSelection> candidates = new List<RestaurantMenuSelection>();
            List<RestaurantMenuItem> items = Inventory.RestaurantProductMenuUtility.AllMenus(shop, settings);
            for (int i = 0; i < items.Count; i++)
            {
                RestaurantMenuItem item = items[i];
                if (!Inventory.RestaurantProductMenuUtility.Accepts(customer, item) || !Inventory.RestaurantProductMenuUtility.WantsMore(customer, shop, item))
                    continue;
                float unitPrice = Mathf.Max(1f, item.unitPrice * settings.priceMultiplier);
                int maxByBudget = Mathf.FloorToInt(remainingBudget / unitPrice);
                int maxCount = Mathf.Min(item.maxCount, Mathf.Min(item.MealDef.stackLimit, maxByBudget));
                if (maxCount < item.minCount) continue;
                int count = ChoosePortionCount(customer, item, preference, maxCount);
                if (count <= 0) continue;
                if (item.IsStockProduct ? !Inventory.RestaurantProductMenuUtility.Available(shop, item, count, customer)
                    : !RestaurantIngredientUtility.HasIngredients(customer, shop, item, count)) continue;
                RestaurantOrder preview = new RestaurantOrder
                {
                    stockProduct = item.IsStockProduct, sourceCabinet = item.sourceCabinet, mode = item.deliveryMode,
                    mealDef = item.MealDef,
                    mealCount = count,
                    ingredients = RestaurantIngredientUtility.BuildNeeds(item, count)
                };
                if (!item.IsStockProduct && RestaurantCookingUtility.FindUsableStoves(shop, preview).Count == 0) continue;
                if (!item.IsStockProduct && !RestaurantCookingUtility.HasAvailableCook(shop, preview)) continue;
                if (!RestaurantBusinessAvailability.CheckOrder(shop, preview).NullOrEmpty()) continue;

                float score = ScoreMenuItem(item, count, unitPrice, remainingBudget, preference, strength);
                candidates.Add(new RestaurantMenuSelection
                {
                    menuItem = item,
                    count = count,
                    totalPrice = Mathf.Max(1f, unitPrice * count),
                    preference = preference,
                    preferenceScore = score * item.selectionWeight,
                    selectionReason = BuildSelectionReason(item, count, preference),
                    createdTick = Find.TickManager?.TicksGame ?? 0
                });
            }
            return candidates.Count == 0 ? null : candidates.RandomElementByWeight(candidate => Mathf.Max(0.05f, candidate.preferenceScore));
        }

        //选择点菜份数，职责是把顾客饥饿程度和稳定份量偏好限制在菜单与堆叠上限内。
        private static int ChoosePortionCount(Pawn customer, RestaurantMenuItem item, RestaurantCustomerPreference preference, int maxCount)
        {
            float nutrition = Mathf.Max(0.01f, item.MealDef.ingestible?.CachedNutrition ?? 0f);
            int appetite = Mathf.Max(1, Mathf.CeilToInt((customer.needs?.food?.NutritionWanted ?? nutrition) / nutrition));
            int preferred = preference?.preferredPortions ?? appetite;
            var shop = SimShopCustomerApi.GetCurrentShop(customer);
            var session = shop == null ? null : RestaurantOrderUtility.OrderManager.ForCustomer(customer.thingIDNumber, shop.ID);
            float wanted = (customer.needs?.food?.NutritionWanted ?? nutrition) - (session?.ExpectedNutrition ?? 0f);
            int byNutrition = item.MealDef.IsDrug || item.deliveryMode == Inventory.RestaurantDeliveryMode.TakeAway
                ? maxCount : Mathf.Max(item.minCount, Mathf.CeilToInt(wanted / nutrition));
            return Mathf.Clamp(preferred, item.minCount, Mathf.Min(maxCount, byNutrition));
        }

        //计算菜单偏好权重，职责是让价格、品质、饮食方向与新鲜感共同影响点菜而不形成硬编码必选项。
        private static float ScoreMenuItem(RestaurantMenuItem item, int count, float unitPrice, float budget, RestaurantCustomerPreference preference, float strength)
        {
            if (preference == null || strength <= 0f) return 1f;
            float total = unitPrice * count;
            float spendRatio = Mathf.Clamp01(total / Mathf.Max(1f, budget));
            float desiredSpend = Mathf.Lerp(0.72f, 0.22f, preference.frugality);
            float priceFit = 1f - Mathf.Abs(spendRatio - desiredSpend);

            float quality = Mathf.InverseLerp((int)FoodPreferability.MealTerrible, (int)FoodPreferability.MealLavish,
                (int)(item.MealDef.ingestible?.preferability ?? FoodPreferability.MealSimple));
            float qualityFit = 1f - Mathf.Abs(quality - preference.qualityPreference);
            float dietFit = GetDietFit(item, preference.dietPreference);
            float portionFit = 1f - Mathf.Min(1f, Mathf.Abs(count - preference.preferredPortions) / 2f);

            float noveltyFit = 1f;
            if (item.id == preference.lastMenuItemId)
                noveltyFit = Mathf.Lerp(1.35f, 0.55f, preference.noveltyPreference);
            else if (item.id == preference.favoriteMenuItemId)
                noveltyFit = Mathf.Lerp(1.45f, 0.9f, preference.noveltyPreference);
            else
                noveltyFit = Mathf.Lerp(0.9f, 1.35f, preference.noveltyPreference);

            float combined = Mathf.Max(0.1f, priceFit * 0.27f + qualityFit * 0.23f + dietFit * 0.27f
                + noveltyFit * 0.13f + portionFit * 0.10f);
            return Mathf.Lerp(1f, combined * 1.35f, Mathf.Clamp01(strength / 2f));
        }

        //计算饮食方向匹配度，职责是根据菜单餐品和食材中的肉食信息响应顾客偏好。
        private static float GetDietFit(RestaurantMenuItem item, RestaurantDietPreference preference)
        {
            bool meat = IsMeatMenu(item);
            if (preference == RestaurantDietPreference.Vegetarian) return meat ? 0.15f : 1.35f;
            if (preference == RestaurantDietPreference.Meat) return meat ? 1.35f : 0.65f;
            return 1f;
        }

        //判断菜单是否带有明显肉食属性，职责是兼容原版荤食 Def 名和菜单食材分类。
        private static bool IsMeatMenu(RestaurantMenuItem item)
        {
            if (item?.mealDefName?.IndexOf("Meat", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (item?.mealDefName?.IndexOf("Veg", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            return item?.ingredients?.Any(ingredient => ingredient?.ThingDef != null && FoodUtility.GetFoodKind(ingredient.ThingDef) == FoodKind.Meat) == true;
        }

        //构造点菜理由，职责是把偏好评分转换为订单详情和评价可读文本。
        private static string BuildSelectionReason(RestaurantMenuItem item, int count, RestaurantCustomerPreference preference)
        {
            if (preference == null) return $"从当前可售菜单中选择 {count} 份";
            string favorite = item.id == preference.favoriteMenuItemId ? "常点菜" : item.id == preference.lastMenuItemId ? "熟悉口味" : "换个口味";
            return $"{favorite}；{preference.BuildSummary()}";
        }

        //生成缓存键，职责是区分顾客、店铺和点餐台上的独立选择流程。
        private static string MakeKey(Pawn customer, Thing provider, Zone_Shop shop)
        {
            if (customer == null || provider == null || shop == null) return "";
            return $"{customer.thingIDNumber}:{provider.thingIDNumber}:{shop.ID}";
        }
    }
}
