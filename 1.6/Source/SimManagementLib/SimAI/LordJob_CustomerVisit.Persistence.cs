using Verse;

namespace SimManagementLib.SimAI
{
    public partial class LordJob_CustomerVisit
    {
        /// <summary>
        /// 读写顾客访问的群体状态和拆分后的运行状态，负责保持旧存档字段名兼容。
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
        }
    }
}
