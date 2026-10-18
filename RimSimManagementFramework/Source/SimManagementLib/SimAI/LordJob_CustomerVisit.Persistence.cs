using Verse;

namespace SimManagementLib.SimAI
{
    public partial class LordJob_CustomerVisit
    {
        private System.Collections.Generic.List<int> tmpVisitSessionKeys;
        private System.Collections.Generic.List<CustomerVisit.CustomerVisitSession> tmpVisitSessionValues;
        private System.Collections.Generic.List<int> tmpPriceRejectionKeys;
        private System.Collections.Generic.List<string> tmpPriceRejectionValues;

        /// <summary>
        /// 读写顾客访问的群体状态和 Session 运行状态。
        /// </summary>
        public override void ExposeData()
        {
            base.ExposeData();
            EnsureStateObjects();

            Scribe_Defs.Look(ref customerKind, "customerKind");
            Scribe_Values.Look(ref customerKindId, "customerKindId", "");
            Scribe_Values.Look(ref targetShopZoneId, "targetShopZoneId", -1);
            Scribe_Values.Look(ref targetShopCell, "targetShopCell");
            Scribe_Values.Look(ref totalBudget, "totalBudget", 0);

            cartState.ExposeData();
            serviceOrderState.ExposeData();
            pawnSettingsState.ExposeData();
            checkoutState.ExposeData();
            Scribe_Collections.Look(ref visitSessions, "visitSessions", LookMode.Value, LookMode.Deep, ref tmpVisitSessionKeys, ref tmpVisitSessionValues);
            Scribe_Collections.Look(ref priceRejectionReasons, "priceRejectionReasons", LookMode.Value, LookMode.Value, ref tmpPriceRejectionKeys, ref tmpPriceRejectionValues);
            EnsureStateObjects();
            if (priceRejectionReasons == null)
                priceRejectionReasons = new System.Collections.Generic.Dictionary<int, string>();
        }
    }
}
