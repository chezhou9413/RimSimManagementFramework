using SimManagementLib.GameComp;
using SimManagementLib.Pojo;
using System.Collections.Generic;
using Verse;

namespace SimManagementLib.Tool
{
    public static class PurchaseOutcomeCatalog
    {
        private static IReadOnlyList<RuntimePurchaseOutcomeRule> EmptyRules { get; } = new List<RuntimePurchaseOutcomeRule>();
        public static GameComponent_PurchaseOutcomeCatalog Manager => Current.Game?.GetComponent<GameComponent_PurchaseOutcomeCatalog>();
        public static void EnsureInitialized() => Manager?.EnsureInitialized();
        public static IReadOnlyList<RuntimePurchaseOutcomeRule> Rules => Manager?.Rules ?? EmptyRules;
        public static RuntimePurchaseOutcomeRule GetRule(string ruleId) => Manager?.GetRule(ruleId);
    }
}
