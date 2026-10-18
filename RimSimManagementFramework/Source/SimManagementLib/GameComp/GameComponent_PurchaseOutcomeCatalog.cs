using SimManagementLib.Pojo;
using SimManagementLib.SimDef;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SimManagementLib.GameComp
{
    public class GameComponent_PurchaseOutcomeCatalog : GameComponent
    {
        private Dictionary<string, RuntimePurchaseOutcomeRule> rulesById = new Dictionary<string, RuntimePurchaseOutcomeRule>();
        private List<RuntimePurchaseOutcomeRule> orderedRules = new List<RuntimePurchaseOutcomeRule>();
        private bool initialized;

        public GameComponent_PurchaseOutcomeCatalog(Game game)
        {
        }

        public IReadOnlyList<RuntimePurchaseOutcomeRule> Rules
        {
            get
            {
                EnsureInitialized();
                return orderedRules;
            }
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            EnsureInitialized();
        }

        public void EnsureInitialized()
        {
            if (initialized) return;
            RebuildFromDefs();
        }

        public void RebuildFromDefs()
        {
            rulesById.Clear();
            orderedRules.Clear();
            foreach (PurchaseOutcomeDef def in DefDatabase<PurchaseOutcomeDef>.AllDefsListForReading.Where(d => d != null))
            {
                RuntimePurchaseOutcomeRule rule = RuntimePurchaseOutcomeRule.FromDef(def);
                if (rule == null) continue;
                rulesById[rule.ruleId] = rule;
                orderedRules.Add(rule);
            }
            initialized = true;
        }

        public RuntimePurchaseOutcomeRule GetRule(string ruleId)
        {
            EnsureInitialized();
            if (string.IsNullOrEmpty(ruleId)) return null;
            rulesById.TryGetValue(ruleId, out RuntimePurchaseOutcomeRule rule);
            return rule;
        }
    }
}
