using RimWorld;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SimManagementLib.Pojo
{
    /// <summary>
    /// 保存单个顾客访问商店时的预算、偏好和价格敏感度，负责在购物、排队和评价流程中提供运行时参数。
    /// </summary>
    public class CustomerRuntimeSettings : IExposable
    {
        public string profileLabel = "";
        public int budget = 0;
        public int queuePatienceTicks = 2500;
        public FloatRange activeHourRange = new FloatRange(0f, 24f);
        public List<WeatherDef> allowedWeathers = new List<WeatherDef>();
        public List<ThingDef> preferredThings = new List<ThingDef>();
        public List<string> preferredGoodsCategoryIds = new List<string>();
        public CustomerPriceSensitivityProps priceSensitivity = CustomerPriceSensitivityProps.Default();
        private List<SimDef.GoodsDef> legacyPreferredGoodsCategories = new List<SimDef.GoodsDef>();

        /// <summary>
        /// 读写顾客运行时参数，负责在旧存档缺少字段时补齐默认集合和价格敏感度。
        /// </summary>
        public void ExposeData()
        {
            Scribe_Values.Look(ref profileLabel, "profileLabel", "");
            Scribe_Values.Look(ref budget, "budget", 0);
            Scribe_Values.Look(ref queuePatienceTicks, "queuePatienceTicks", 2500);
            Scribe_Values.Look(ref activeHourRange, "activeHourRange", new FloatRange(0f, 24f));
            Scribe_Collections.Look(ref allowedWeathers, "allowedWeathers", LookMode.Def);
            Scribe_Collections.Look(ref preferredThings, "preferredThings", LookMode.Def);
            Scribe_Collections.Look(ref preferredGoodsCategoryIds, "preferredGoodsCategoryIds", LookMode.Value);
            Scribe_Deep.Look(ref priceSensitivity, "priceSensitivity");
            if (Scribe.mode == LoadSaveMode.LoadingVars)
                Scribe_Collections.Look(ref legacyPreferredGoodsCategories, "preferredGoodsCategories", LookMode.Def);

            if (allowedWeathers == null) allowedWeathers = new List<WeatherDef>();
            if (preferredThings == null) preferredThings = new List<ThingDef>();
            if (preferredGoodsCategoryIds == null) preferredGoodsCategoryIds = new List<string>();
            EnsureDefaults();

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (!legacyPreferredGoodsCategories.NullOrEmpty())
                {
                    preferredGoodsCategoryIds.AddRange(legacyPreferredGoodsCategories
                        .Where(g => g != null)
                        .Select(g => g.defName));
                }

                preferredGoodsCategoryIds = preferredGoodsCategoryIds
                    .Where(id => !string.IsNullOrEmpty(id))
                    .Distinct()
                    .ToList();
                EnsureDefaults();
            }
        }

        /// <summary>
        /// 补齐顾客运行时设置默认值，负责兼容旧存档和缺省顾客配置。
        /// </summary>
        public void EnsureDefaults()
        {
            if (priceSensitivity == null)
                priceSensitivity = CustomerPriceSensitivityProps.Default();
            priceSensitivity.EnsureDefaults();
        }

        /// <summary>
        /// 返回指定商品对该顾客的偏好倍率，负责叠加指定物品和商品分类偏好。
        /// </summary>
        public float GetPreferenceMultiplier(ThingDef def)
        {
            if (def == null) return 1f;

            float mul = 1f;
            if (preferredThings.Contains(def))
                mul *= 2.5f;

            for (int i = 0; i < preferredGoodsCategoryIds.Count; i++)
            {
                string categoryId = preferredGoodsCategoryIds[i];
                if (GoodsCatalog.Contains(categoryId, def))
                {
                    mul *= 1.8f;
                    break;
                }
            }

            return mul;
        }
    }
}
