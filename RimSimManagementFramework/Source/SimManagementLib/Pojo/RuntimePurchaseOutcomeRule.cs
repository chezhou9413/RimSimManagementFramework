using SimManagementLib.SimDef;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SimManagementLib.Pojo
{
    public sealed class RuntimePurchaseOutcomeRule
    {
        public string ruleId;
        public string label;
        public PurchaseOutcomeDef sourceDef;
        public List<ThingDef> triggerThingDefs = new List<ThingDef>();
        public List<string> triggerGoodsCategoryIds = new List<string>();
        public bool triggerOncePerCheckout = true;
        public int maxJobsToQueue = 1;
        public PurchaseOutcomeWorker worker;

        public bool MatchesThing(ThingDef purchasedDef)
        {
            if (purchasedDef == null) return false;
            bool hasThingRules = triggerThingDefs != null && triggerThingDefs.Count > 0;
            bool hasGoodsRules = triggerGoodsCategoryIds != null && triggerGoodsCategoryIds.Count > 0;
            if (!hasThingRules && !hasGoodsRules) return true;
            if (hasThingRules && triggerThingDefs.Contains(purchasedDef)) return true;

            if (hasGoodsRules)
            {
                for (int i = 0; i < triggerGoodsCategoryIds.Count; i++)
                {
                    if (Tool.GoodsCatalog.Contains(triggerGoodsCategoryIds[i], purchasedDef))
                        return true;
                }
            }

            return false;
        }

        public static RuntimePurchaseOutcomeRule FromDef(PurchaseOutcomeDef def)
        {
            if (def == null) return null;
            return new RuntimePurchaseOutcomeRule
            {
                ruleId = def.defName,
                label = def.LabelCap.RawText,
                sourceDef = def,
                triggerThingDefs = def.triggerThingDefs?.Where(t => t != null).ToList() ?? new List<ThingDef>(),
                triggerGoodsCategoryIds = def.GetTriggerGoodsCategoryIds(),
                triggerOncePerCheckout = def.triggerOncePerCheckout,
                maxJobsToQueue = def.maxJobsToQueue,
                worker = def.Worker
            };
        }
    }
}
