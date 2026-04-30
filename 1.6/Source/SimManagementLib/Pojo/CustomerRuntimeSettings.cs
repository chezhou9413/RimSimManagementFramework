using RimWorld;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SimManagementLib.Pojo
{
    public class CustomerRuntimeSettings : IExposable
    {
        public string profileLabel = "";
        public int budget = 0;
        public int queuePatienceTicks = 2500;
        public FloatRange activeHourRange = new FloatRange(0f, 24f);
        public List<WeatherDef> allowedWeathers = new List<WeatherDef>();
        public List<ThingDef> preferredThings = new List<ThingDef>();
        public List<string> preferredGoodsCategoryIds = new List<string>();
        private List<SimDef.GoodsDef> legacyPreferredGoodsCategories = new List<SimDef.GoodsDef>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref profileLabel, "profileLabel", "");
            Scribe_Values.Look(ref budget, "budget", 0);
            Scribe_Values.Look(ref queuePatienceTicks, "queuePatienceTicks", 2500);
            Scribe_Values.Look(ref activeHourRange, "activeHourRange", new FloatRange(0f, 24f));
            Scribe_Collections.Look(ref allowedWeathers, "allowedWeathers", LookMode.Def);
            Scribe_Collections.Look(ref preferredThings, "preferredThings", LookMode.Def);
            Scribe_Collections.Look(ref preferredGoodsCategoryIds, "preferredGoodsCategoryIds", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
                Scribe_Collections.Look(ref legacyPreferredGoodsCategories, "preferredGoodsCategories", LookMode.Def);

            if (allowedWeathers == null) allowedWeathers = new List<WeatherDef>();
            if (preferredThings == null) preferredThings = new List<ThingDef>();
            if (preferredGoodsCategoryIds == null) preferredGoodsCategoryIds = new List<string>();

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
            }
        }

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
