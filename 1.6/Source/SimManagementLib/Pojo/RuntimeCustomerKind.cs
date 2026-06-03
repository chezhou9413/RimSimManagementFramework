using RimWorld;
using SimManagementLib.SimDef;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.Pojo
{
    /// <summary>
    /// 保存 Def 顾客和玩家自定义顾客的运行时合并结果，负责生成顾客访问参数并提供偏好查询。
    /// </summary>
    public sealed class RuntimeCustomerKind
    {
        public string kindId;
        public string label;
        public CustomerKindDef sourceDef;
        public List<PawnKindDef> pawnKindDefs = new List<PawnKindDef>();
        public SimpleCurve arrivalCurve;
        public float baseMtbDays = 0.25f;
        public IntRange budgetRange = new IntRange(100, 400);
        public IntRange queuePatienceRange = new IntRange(900, 3000);
        public FloatRange activeHourRange = new FloatRange(0f, 24f);
        public List<WeatherDef> allowedWeathers = new List<WeatherDef>();
        public float minShopReputation;
        public List<string> targetGoodsCategoryIds = new List<string>();
        public List<string> targetServiceCategoryIds = new List<string>();
        public List<RuntimeItemPreference> itemPreferences = new List<RuntimeItemPreference>();
        public ShoppingBehaviorProps shoppingBehavior = new ShoppingBehaviorProps();
        public CustomerPriceSensitivityProps priceSensitivity = CustomerPriceSensitivityProps.Default();
        public List<RuntimeCustomerProfile> spawnProfiles = new List<RuntimeCustomerProfile>();

        /// <summary>
        /// 判断当前地图时间和天气是否允许该顾客类型出现。
        /// </summary>
        public bool CanAppearNow(Map map)
        {
            float hour = GenLocalDate.HourFloat(map);
            WeatherDef curWeather = map.weatherManager?.curWeather;
            return IsHourAllowed(activeHourRange, hour) && IsWeatherAllowed(allowedWeathers, curWeather);
        }

        /// <summary>
        /// 计算指定小时的到访权重，负责兼容未配置到访曲线的顾客。
        /// </summary>
        public float EvaluateArrivalWeight(float hour)
        {
            return Mathf.Max(0.01f, arrivalCurve != null ? arrivalCurve.Evaluate(hour) : 1f);
        }

        /// <summary>
        /// 计算该顾客对商店当前商品分类的兴趣倍率，负责让目标分类商店更容易吸引顾客。
        /// </summary>
        public float GetInterestMultiplier(string shopActiveCategoryId)
        {
            if (targetGoodsCategoryIds.NullOrEmpty()) return 1f;
            return !string.IsNullOrEmpty(shopActiveCategoryId) && targetGoodsCategoryIds.Contains(shopActiveCategoryId) ? 1.5f : 0f;
        }

        /// <summary>
        /// 返回运行时顾客明确感兴趣的商品分类，空列表表示不限制商品分类。
        /// </summary>
        public List<string> GetTargetGoodsCategoryIds()
        {
            return targetGoodsCategoryIds?
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct()
                .ToList() ?? new List<string>();
        }

        /// <summary>
        /// 返回运行时顾客明确感兴趣的服务分类，空列表表示不限制服务分类。
        /// </summary>
        public List<string> GetTargetServiceCategoryIds()
        {
            return targetServiceCategoryIds?
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct()
                .ToList() ?? new List<string>();
        }

        /// <summary>
        /// 构建单次访问的顾客参数，负责选择档案并补齐默认价格敏感度。
        /// </summary>
        public CustomerRuntimeSettings BuildRuntimeSettings(Map map)
        {
            RuntimeCustomerProfile profile = PickProfile(map);
            CustomerRuntimeSettings settings = profile != null
                ? profile.BuildSettings(label, priceSensitivity)
                : BuildFallbackSettings();

            if (settings.budget <= 0) settings.budget = 1;
            if (settings.queuePatienceTicks <= 0) settings.queuePatienceTicks = GetFallbackQueuePatience();
            settings.EnsureDefaults();
            return settings;
        }

        /// <summary>
        /// 返回指定商品对该顾客类型的偏好倍率，负责在没有档案偏好时提供基础偏好。
        /// </summary>
        public float GetPreferenceMultiplier(ThingDef item)
        {
            float mul = 1f;
            if (itemPreferences.NullOrEmpty()) return mul;

            for (int i = 0; i < itemPreferences.Count; i++)
            {
                RuntimeItemPreference pref = itemPreferences[i];
                if (pref != null && pref.Matches(item))
                    mul *= Mathf.Max(1f, pref.weight);
            }

            return mul;
        }

        /// <summary>
        /// 按当前地图条件选择一个生成档案。
        /// </summary>
        private RuntimeCustomerProfile PickProfile(Map map)
        {
            if (spawnProfiles.NullOrEmpty()) return null;
            List<RuntimeCustomerProfile> candidates = spawnProfiles.Where(p => p != null && p.CanAppearNow(map)).ToList();
            if (candidates.NullOrEmpty()) return null;
            return candidates.RandomElementByWeight(p => Mathf.Max(0.01f, p.weight));
        }

        /// <summary>
        /// 构建无档案命中时的访问参数，负责使用顾客类型本身的预算、偏好和价格配置。
        /// </summary>
        private CustomerRuntimeSettings BuildFallbackSettings()
        {
            return new CustomerRuntimeSettings
            {
                profileLabel = label,
                budget = budgetRange.RandomInRange,
                queuePatienceTicks = GetFallbackQueuePatience(),
                activeHourRange = activeHourRange,
                allowedWeathers = allowedWeathers?.ToList() ?? new List<WeatherDef>(),
                preferredThings = itemPreferences.Where(p => p?.preferredThing != null).Select(p => p.preferredThing).Distinct().ToList(),
                preferredGoodsCategoryIds = itemPreferences.Select(p => p?.preferredGoodsCategoryId).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList(),
                priceSensitivity = CustomerPriceSensitivityProps.Resolve(null, priceSensitivity)
            };
        }

        /// <summary>
        /// 返回无档案时的排队耐心，负责兼容旧购物行为配置。
        /// </summary>
        private int GetFallbackQueuePatience()
        {
            if (queuePatienceRange.max > 0) return queuePatienceRange.RandomInRange;
            if (shoppingBehavior != null && shoppingBehavior.queuePatience > 0) return shoppingBehavior.queuePatience;
            return 2500;
        }

        /// <summary>
        /// 从 Def 顾客构建运行时顾客类型。
        /// </summary>
        public static RuntimeCustomerKind FromDef(CustomerKindDef def)
        {
            if (def == null) return null;
            return new RuntimeCustomerKind
            {
                kindId = def.defName,
                label = def.LabelCap.RawText,
                sourceDef = def,
                pawnKindDefs = def.pawnKindDefs?.Where(p => p != null).ToList() ?? new List<PawnKindDef>(),
                arrivalCurve = def.arrivalCurve,
                baseMtbDays = def.baseMtbDays,
                budgetRange = def.budgetRange,
                queuePatienceRange = def.queuePatienceRange,
                activeHourRange = def.activeHourRange,
                allowedWeathers = def.allowedWeathers?.Where(w => w != null).ToList() ?? new List<WeatherDef>(),
                minShopReputation = def.minShopReputation,
                targetGoodsCategoryIds = def.GetTargetGoodsCategoryIds(),
                targetServiceCategoryIds = def.GetTargetServiceCategoryIds(),
                itemPreferences = RuntimeItemPreference.FromDefs(def.itemPreferences),
                shoppingBehavior = def.shoppingBehavior ?? new ShoppingBehaviorProps(),
                priceSensitivity = CustomerPriceSensitivityProps.Resolve(def.priceSensitivity),
                spawnProfiles = RuntimeCustomerProfile.FromDefs(def.spawnProfiles)
            };
        }

        /// <summary>
        /// 根据玩家注册数据构建运行时顾客类型。
        /// </summary>
        public static RuntimeCustomerKind FromCustomRecord(CustomCustomerKindRecord record)
        {
            if (record == null) return null;

            List<PawnKindDef> pawnKinds = record.pawnKindDefNames?
                .Select(defName => DefDatabase<PawnKindDef>.GetNamedSilentFail(defName))
                .Where(def => Tool.CustomCustomerDatabase.IsValidCandidatePawnKind(def))
                .Distinct()
                .ToList() ?? new List<PawnKindDef>();
            if (pawnKinds.Count == 0) return null;

            return new RuntimeCustomerKind
            {
                kindId = record.kindId,
                label = string.IsNullOrEmpty(record.label) ? record.kindId : record.label,
                sourceDef = null,
                pawnKindDefs = pawnKinds,
                arrivalCurve = null,
                baseMtbDays = Mathf.Clamp(record.baseMtbDays, 0.01f, 20f),
                budgetRange = new IntRange(Mathf.Min(record.budgetMin, record.budgetMax), Mathf.Max(record.budgetMin, record.budgetMax)),
                queuePatienceRange = new IntRange(Mathf.Min(record.queuePatienceMin, record.queuePatienceMax), Mathf.Max(record.queuePatienceMin, record.queuePatienceMax)),
                activeHourRange = new FloatRange(Mathf.Clamp(record.activeHourMin, 0f, 24f), Mathf.Clamp(record.activeHourMax, 0f, 24f)),
                allowedWeathers = record.allowedWeatherDefNames?
                    .Select(defName => DefDatabase<WeatherDef>.GetNamedSilentFail(defName))
                    .Where(def => def != null)
                    .Distinct()
                    .ToList() ?? new List<WeatherDef>(),
                minShopReputation = Mathf.Clamp(record.minShopReputation, 0f, 100f),
                targetGoodsCategoryIds = record.targetGoodsCategoryIds?
                    .Where(id => !string.IsNullOrEmpty(id))
                    .Distinct()
                    .ToList() ?? new List<string>(),
                targetServiceCategoryIds = record.targetServiceCategoryIds?
                    .Where(id => !string.IsNullOrEmpty(id))
                    .Distinct()
                    .ToList() ?? new List<string>(),
                itemPreferences = RuntimeItemPreference.FromCustomRecords(record.itemPreferences),
                shoppingBehavior = new ShoppingBehaviorProps(),
                priceSensitivity = CustomerPriceSensitivityProps.Resolve(record.priceSensitivity),
                spawnProfiles = RuntimeCustomerProfile.FromCustomRecords(record.spawnProfiles)
            };
        }

        /// <summary>
        /// 判断指定小时是否位于允许活动时间段内，负责支持跨午夜时间段。
        /// </summary>
        private static bool IsHourAllowed(FloatRange range, float hour)
        {
            if (range.TrueMin <= range.TrueMax)
                return hour >= range.TrueMin && hour <= range.TrueMax;
            return hour >= range.TrueMin || hour <= range.TrueMax;
        }

        /// <summary>
        /// 判断当前天气是否允许该顾客类型出现，空列表表示不限制天气。
        /// </summary>
        private static bool IsWeatherAllowed(List<WeatherDef> allowed, WeatherDef current)
        {
            if (allowed.NullOrEmpty()) return true;
            if (current == null) return false;
            return allowed.Contains(current);
        }
    }
}
