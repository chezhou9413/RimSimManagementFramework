using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDef
{
    public class CustomerKindDef : Def
    {
        // 基础配置
        public List<PawnKindDef> pawnKindDefs = new List<PawnKindDef>();
        public SimpleCurve arrivalCurve;
        // 基础刷新周期（单位：天）。默认 0.25 天（约每 6 小时一波，随后再乘到访曲线权重）。
        public float baseMtbDays = 0.25f;
        public IntRange budgetRange = new IntRange(100, 400);
        public IntRange queuePatienceRange = new IntRange(900, 3000);
        public FloatRange activeHourRange = new FloatRange(0f, 24f);
        public List<WeatherDef> allowedWeathers = new List<WeatherDef>();
        public float minShopReputation = 0f;
        public List<GoodsDef> targetGoodsCategories = new List<GoodsDef>();
        public List<string> targetGoodsCategoryIds = new List<string>();
        public List<ItemPreference> itemPreferences = new List<ItemPreference>();
        public ShoppingBehaviorProps shoppingBehavior = new ShoppingBehaviorProps();

        // 扩展配置：一类顾客下可定义多个“顾客档案”。
        public List<CustomerSpawnProfile> spawnProfiles = new List<CustomerSpawnProfile>();

        /// <summary>
        /// 评估该类顾客对指定商店正在销售的 GoodsDef 的兴趣倍率。
        /// 0 表示毫无兴趣（绝对不会去），1 表示正常，大于1表示极度渴望。
        /// </summary>
        public float GetInterestMultiplier(string shopActiveCategoryId)
        {
            List<string> targetIds = GetTargetGoodsCategoryIds();
            if (targetIds.NullOrEmpty())
            {
                return 1f;
            }

            if (!string.IsNullOrEmpty(shopActiveCategoryId) && targetIds.Contains(shopActiveCategoryId))
            {
                return 1.5f;
            }

            return 0f;
        }

        public bool CanAppearNow(Map map)
        {
            float hour = GenLocalDate.HourFloat(map);
            WeatherDef curWeather = map.weatherManager?.curWeather;

            bool baseOk = IsHourAllowed(activeHourRange, hour) && IsWeatherAllowed(allowedWeathers, curWeather);
            if (!baseOk) return false;

            // 有档案时也不再“硬性要求至少一个档案命中”，避免时间/天气把整类顾客完全卡死。
            // 具体档案会在 BuildRuntimeSettings 中按当前条件挑选，挑不到则回退到基础参数。
            return true;
        }

        public CustomerRuntimeSettings BuildRuntimeSettings(Map map)
        {
            CustomerSpawnProfile profile = PickProfile(map);
            var settings = new CustomerRuntimeSettings();

            if (profile != null)
            {
                settings.profileLabel = string.IsNullOrEmpty(profile.label) ? LabelCap.RawText : profile.label;
                settings.budget = profile.budgetRange.RandomInRange;
                settings.queuePatienceTicks = profile.queuePatienceRange.RandomInRange;
                settings.activeHourRange = profile.activeHourRange;
                settings.allowedWeathers = profile.allowedWeathers?.ToList() ?? new List<WeatherDef>();
                settings.preferredThings = profile.preferredThings?.Where(t => t != null).Distinct().ToList() ?? new List<ThingDef>();
                settings.preferredGoodsCategoryIds = profile.GetPreferredGoodsCategoryIds();
            }
            else
            {
                settings.profileLabel = LabelCap.RawText;
                settings.budget = budgetRange.RandomInRange;
                settings.queuePatienceTicks = GetFallbackQueuePatience();
                settings.activeHourRange = activeHourRange;
                settings.allowedWeathers = allowedWeathers?.ToList() ?? new List<WeatherDef>();
                settings.preferredThings = itemPreferences.Where(p => p?.preferredThing != null).Select(p => p.preferredThing).Distinct().ToList();
                settings.preferredGoodsCategoryIds = itemPreferences
                    .SelectMany(p => p?.GetPreferredGoodsCategoryIds() ?? new List<string>())
                    .Where(id => !string.IsNullOrEmpty(id))
                    .Distinct()
                    .ToList();
            }

            if (settings.budget <= 0) settings.budget = 1;
            if (settings.queuePatienceTicks <= 0) settings.queuePatienceTicks = 2500;
            return settings;
        }

        public float GetPreferenceMultiplier(ThingDef item)
        {
            float mul = 1f;
            if (itemPreferences.NullOrEmpty()) return mul;

            for (int i = 0; i < itemPreferences.Count; i++)
            {
                ItemPreference pref = itemPreferences[i];
                if (pref != null && pref.Matches(item))
                {
                    mul *= Mathf.Max(1f, pref.weight);
                }
            }

            return mul;
        }

        private CustomerSpawnProfile PickProfile(Map map)
        {
            if (spawnProfiles.NullOrEmpty()) return null;

            List<CustomerSpawnProfile> candidates = spawnProfiles
                .Where(p => p != null && p.CanAppearNow(map))
                .ToList();
            if (candidates.NullOrEmpty()) return null;

            return candidates.RandomElementByWeight(p => Mathf.Max(0.01f, p.weight));
        }

        public List<string> GetTargetGoodsCategoryIds()
        {
            List<string> ids = new List<string>();
            if (!targetGoodsCategoryIds.NullOrEmpty())
                ids.AddRange(targetGoodsCategoryIds.Where(id => !string.IsNullOrEmpty(id)));
            if (!targetGoodsCategories.NullOrEmpty())
                ids.AddRange(targetGoodsCategories.Where(g => g != null).Select(g => g.defName));
            return ids.Distinct().ToList();
        }

        private int GetFallbackQueuePatience()
        {
            if (queuePatienceRange.max > 0) return queuePatienceRange.RandomInRange;
            if (shoppingBehavior != null && shoppingBehavior.queuePatience > 0) return shoppingBehavior.queuePatience;
            return 2500;
        }

        private static bool IsHourAllowed(FloatRange range, float hour)
        {
            if (range.TrueMin <= range.TrueMax)
                return hour >= range.TrueMin && hour <= range.TrueMax;

            // 允许跨午夜，例如 20~4
            return hour >= range.TrueMin || hour <= range.TrueMax;
        }

        private static bool IsWeatherAllowed(List<WeatherDef> allowed, WeatherDef current)
        {
            if (allowed.NullOrEmpty()) return true;
            if (current == null) return false;
            return allowed.Contains(current);
        }
    }

    /// <summary>
    /// 一类顾客中的可选档案：支持预算/等待/偏好/时间/天气的差异化配置。
    /// </summary>
    public class CustomerSpawnProfile
    {
        public string label = "";
        public float weight = 1f;
        public IntRange budgetRange = new IntRange(100, 400);
        public IntRange queuePatienceRange = new IntRange(900, 3000);
        public FloatRange activeHourRange = new FloatRange(0f, 24f);
        public List<WeatherDef> allowedWeathers = new List<WeatherDef>();
        public List<ThingDef> preferredThings = new List<ThingDef>();
        public List<GoodsDef> preferredGoodsCategories = new List<GoodsDef>();
        public List<string> preferredGoodsCategoryIds = new List<string>();

        public bool CanAppearNow(Map map)
        {
            float hour = GenLocalDate.HourFloat(map);
            WeatherDef curWeather = map.weatherManager?.curWeather;
            return IsHourAllowed(activeHourRange, hour) && IsWeatherAllowed(allowedWeathers, curWeather);
        }

        private static bool IsHourAllowed(FloatRange range, float hour)
        {
            if (range.TrueMin <= range.TrueMax)
                return hour >= range.TrueMin && hour <= range.TrueMax;
            return hour >= range.TrueMin || hour <= range.TrueMax;
        }

        private static bool IsWeatherAllowed(List<WeatherDef> allowed, WeatherDef current)
        {
            if (allowed.NullOrEmpty()) return true;
            if (current == null) return false;
            return allowed.Contains(current);
        }

        public List<string> GetPreferredGoodsCategoryIds()
        {
            List<string> ids = new List<string>();
            if (!preferredGoodsCategoryIds.NullOrEmpty())
                ids.AddRange(preferredGoodsCategoryIds.Where(id => !string.IsNullOrEmpty(id)));
            if (!preferredGoodsCategories.NullOrEmpty())
                ids.AddRange(preferredGoodsCategories.Where(g => g != null).Select(g => g.defName));
            return ids.Distinct().ToList();
        }
    }

    /// <summary>
    /// 物品偏好：深度支持 GoodsDef 和具体的 ThingDef
    /// </summary>
    public class ItemPreference
    {
        public GoodsDef preferredGoodsCategory;
        public string preferredGoodsCategoryId;
        public ThingDef preferredThing;
        public string tag;
        public float weight = 1.0f;

        public bool Matches(ThingDef item)
        {
            if (preferredThing != null && preferredThing == item)
                return true;

            List<string> categoryIds = GetPreferredGoodsCategoryIds();
            for (int i = 0; i < categoryIds.Count; i++)
            {
                if (GoodsCatalog.Contains(categoryIds[i], item))
                    return true;
            }

            return false;
        }

        public List<string> GetPreferredGoodsCategoryIds()
        {
            List<string> ids = new List<string>();
            if (!string.IsNullOrEmpty(preferredGoodsCategoryId))
                ids.Add(preferredGoodsCategoryId);
            if (preferredGoodsCategory != null)
                ids.Add(preferredGoodsCategory.defName);
            return ids.Distinct().ToList();
        }
    }

    public class ShoppingBehaviorProps
    {
        public IntRange browseTimeRange = new IntRange(300, 900);
        public int maxShelvesToVisit = 4;
        public int queuePatience = 600;
    }
}
