using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using RimSimRestaurantExtension.GameComp;
using RimSimRestaurantExtension.Models;
using SimManagementLib.Tool;
using Verse;

namespace RimSimRestaurantExtension.Tool
{
    //读写餐厅设置分享文本，校验失败时不改动现有草稿。
    internal static class RestaurantSettingsTransferUtility
    {
        //将菜单和经营参数编码为带格式标识的 Base64 JSON。
        public static string Export(RestaurantShopSettings settings)
        {
            var package = new RestaurantSettingsTransferPackage
            {
                enabled = settings.enabled, priceMultiplier = settings.priceMultiplier,
                maxWaitTicks = settings.maxWaitTicks, maxServiceWaitTicks = settings.maxServiceWaitTicks,
                maxOrderRounds = settings.maxOrderRounds, reorderIntervalTicks = settings.reorderIntervalTicks,
                useCustomerPreferences = settings.useCustomerPreferences, preferenceStrength = settings.preferenceStrength,
                dishes = settings.menuItems.Select(item => new RestaurantSettingsTransferPackage.Dish
                {
                    label = item.label, meal = item.mealDefName, enabled = item.enabled,
                    price = item.unitPrice, minimum = item.minCount, maximum = item.maxCount,
                    ingredients = item.ingredients.GroupBy(i => i.thingDefName)
                        .ToDictionary(g => g.Key, g => g.Sum(i => i.countPerMeal))
                }).ToList()
            };
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(RestaurantSettingsTransferPackage)).WriteObject(stream, package);
                return Convert.ToBase64String(stream.ToArray());
            }
        }

        //只接受当前格式和实际存在的可制作菜单，缺失模组时明确报告对应定义。
        public static bool TryImport(string text, out RestaurantShopSettings settings, out string error)
        {
            settings = null;
            error = "";
            try
            {
                if (string.IsNullOrWhiteSpace(text)) throw new FormatException(SimTranslation.T("RSR.Transfer.Empty"));
                RestaurantSettingsTransferPackage package;
                using (var stream = new MemoryStream(Convert.FromBase64String(text.Trim())))
                    package = (RestaurantSettingsTransferPackage)new DataContractJsonSerializer(typeof(RestaurantSettingsTransferPackage)).ReadObject(stream);
                if (package == null || package.format != "RimSimRestaurantSettings" || package.version != 1)
                    throw new FormatException(SimTranslation.T("RSR.Transfer.Format"));
                ValidateParameters(package);
                var imported = new RestaurantShopSettings
                {
                    enabled = package.enabled, priceMultiplier = package.priceMultiplier,
                    maxWaitTicks = package.maxWaitTicks, maxServiceWaitTicks = package.maxServiceWaitTicks,
                    maxOrderRounds = package.maxOrderRounds, reorderIntervalTicks = package.reorderIntervalTicks,
                    useCustomerPreferences = package.useCustomerPreferences, preferenceStrength = package.preferenceStrength
                };
                if (package.dishes == null) throw new FormatException(SimTranslation.T("RSR.Transfer.InvalidMenu"));
                foreach (var dish in package.dishes)
                {
                    if (dish == null || dish.meal.NullOrEmpty() || dish.ingredients == null)
                        throw new FormatException(SimTranslation.T("RSR.Transfer.InvalidMenu"));
                    ThingDef meal = DefDatabase<ThingDef>.GetNamedSilentFail(dish.meal);
                    if (meal == null) throw new FormatException(SimTranslation.T("RSR.Transfer.MissingDef", dish.meal.Named("def")));
                    if (!RestaurantFoodUtility.IsMenuFood(meal) || !InRange(dish.price, 1f, 100000f)
                        || dish.minimum < 1 || dish.maximum < dish.minimum || dish.maximum > meal.stackLimit)
                        throw new FormatException(SimTranslation.T("RSR.Transfer.InvalidDish", dish.meal.Named("dish")));
                    var item = new RestaurantMenuItem
                    {
                        id = RestaurantShopSettings.MakeMenuId(), label = dish.label ?? "", mealDefName = dish.meal,
                        enabled = dish.enabled, unitPrice = dish.price, minCount = dish.minimum, maxCount = dish.maximum
                    };
                    foreach (var ingredient in dish.ingredients)
                    {
                        if (DefDatabase<ThingDef>.GetNamedSilentFail(ingredient.Key) == null)
                            throw new FormatException(SimTranslation.T("RSR.Transfer.MissingDef", ingredient.Key.Named("def")));
                        if (ingredient.Value < 1 || ingredient.Value > 100000)
                            throw new FormatException(SimTranslation.T("RSR.Transfer.InvalidDish", dish.meal.Named("dish")));
                        item.ingredients.Add(new RestaurantIngredientRequirement { thingDefName = ingredient.Key, countPerMeal = ingredient.Value });
                    }
                    var request = new RestaurantProductionRequest { mealDef = meal, mealCount = 1, ingredients = item.ingredients };
                    if (!RestaurantCookingUtility.GetProductionRecipes(meal).Any(r => RestaurantCookingUtility.CanRecipeUseOrderIngredients(r, request)))
                        throw new FormatException(SimTranslation.T("RSR.Transfer.InvalidDish", dish.meal.Named("dish")));
                    imported.menuItems.Add(item);
                }
                settings = imported;
                return true;
            }
            catch (Exception ex) when (ex is FormatException || ex is SerializationException || ex is System.Xml.XmlException)
            {
                error = SimTranslation.T("RSR.Transfer.Error", ex.Message.Named("reason"));
                return false;
            }
        }

        //验证外部参数范围，避免导入时静默夹紧数值。
        private static void ValidateParameters(RestaurantSettingsTransferPackage package)
        {
            if (!InRange(package.priceMultiplier, 0.1f, 5f) || !InRange(package.preferenceStrength, 0f, 2f)
                || package.maxWaitTicks < 6000 || package.maxWaitTicks > 30000
                || package.maxServiceWaitTicks < 6000 || package.maxServiceWaitTicks > 30000
                || package.maxOrderRounds < 1 || package.maxOrderRounds > 5
                || package.reorderIntervalTicks < 120 || package.reorderIntervalTicks > 12000)
                throw new FormatException(SimTranslation.T("RSR.Transfer.InvalidParameters"));
        }

        //排除非有限数字并检查允许区间。
        private static bool InRange(float value, float minimum, float maximum) =>
            !float.IsNaN(value) && !float.IsInfinity(value) && value >= minimum && value <= maximum;
    }
}
