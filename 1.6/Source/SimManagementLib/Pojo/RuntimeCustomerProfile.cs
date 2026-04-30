using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SimManagementLib.Pojo
{
    public sealed class RuntimeCustomerProfile
    {
        public string label = "";
        public float weight = 1f;
        public IntRange budgetRange = new IntRange(100, 400);
        public IntRange queuePatienceRange = new IntRange(900, 3000);
        public FloatRange activeHourRange = new FloatRange(0f, 24f);
        public List<WeatherDef> allowedWeathers = new List<WeatherDef>();
        public List<ThingDef> preferredThings = new List<ThingDef>();
        public List<string> preferredGoodsCategoryIds = new List<string>();

        public bool CanAppearNow(Map map)
        {
            float hour = GenLocalDate.HourFloat(map);
            WeatherDef curWeather = map.weatherManager?.curWeather;
            return IsHourAllowed(activeHourRange, hour) && IsWeatherAllowed(allowedWeathers, curWeather);
        }

        public CustomerRuntimeSettings BuildSettings(string fallbackLabel)
        {
            return new CustomerRuntimeSettings
            {
                profileLabel = string.IsNullOrEmpty(label) ? fallbackLabel : label,
                budget = budgetRange.RandomInRange,
                queuePatienceTicks = queuePatienceRange.RandomInRange,
                activeHourRange = activeHourRange,
                allowedWeathers = allowedWeathers?.ToList() ?? new List<WeatherDef>(),
                preferredThings = preferredThings?.Where(t => t != null).Distinct().ToList() ?? new List<ThingDef>(),
                preferredGoodsCategoryIds = preferredGoodsCategoryIds?.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList() ?? new List<string>()
            };
        }

        public static List<RuntimeCustomerProfile> FromDefs(IEnumerable<SimDef.CustomerSpawnProfile> defs)
        {
            if (defs == null) return new List<RuntimeCustomerProfile>();

            return defs
                .Where(p => p != null)
                .Select(p => new RuntimeCustomerProfile
                {
                    label = p.label,
                    weight = p.weight,
                    budgetRange = p.budgetRange,
                    queuePatienceRange = p.queuePatienceRange,
                    activeHourRange = p.activeHourRange,
                    allowedWeathers = p.allowedWeathers?.Where(w => w != null).ToList() ?? new List<WeatherDef>(),
                    preferredThings = p.preferredThings?.Where(t => t != null).ToList() ?? new List<ThingDef>(),
                    preferredGoodsCategoryIds = p.GetPreferredGoodsCategoryIds()
                })
                .ToList();
        }

        /// <summary>
        /// 将玩家注册的生成档案记录转换为运行时档案条目。
        /// </summary>
        public static List<RuntimeCustomerProfile> FromCustomRecords(IEnumerable<CustomCustomerProfileRecord> records)
        {
            if (records == null) return new List<RuntimeCustomerProfile>();

            return records
                .Where(p => p != null)
                .Select(p => new RuntimeCustomerProfile
                {
                    label = p.label,
                    weight = p.weight,
                    budgetRange = new IntRange(System.Math.Min(p.budgetMin, p.budgetMax), System.Math.Max(p.budgetMin, p.budgetMax)),
                    queuePatienceRange = new IntRange(System.Math.Min(p.queuePatienceMin, p.queuePatienceMax), System.Math.Max(p.queuePatienceMin, p.queuePatienceMax)),
                    activeHourRange = new FloatRange(UnityEngine.Mathf.Clamp(p.activeHourMin, 0f, 24f), UnityEngine.Mathf.Clamp(p.activeHourMax, 0f, 24f)),
                    allowedWeathers = p.allowedWeatherDefNames?
                        .Select(defName => DefDatabase<WeatherDef>.GetNamedSilentFail(defName))
                        .Where(def => def != null)
                        .ToList() ?? new List<WeatherDef>(),
                    preferredThings = p.preferredThingDefNames?
                        .Select(defName => DefDatabase<ThingDef>.GetNamedSilentFail(defName))
                        .Where(def => def != null)
                        .ToList() ?? new List<ThingDef>(),
                    preferredGoodsCategoryIds = p.preferredGoodsCategoryIds?
                        .Where(id => !string.IsNullOrEmpty(id))
                        .Distinct()
                        .ToList() ?? new List<string>()
                })
                .ToList();
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
    }
}
