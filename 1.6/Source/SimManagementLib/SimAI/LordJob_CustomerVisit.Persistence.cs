using SimManagementLib.Pojo;
using System.Collections.Generic;
using Verse;

namespace SimManagementLib.SimAI
{
    public partial class LordJob_CustomerVisit
    {
        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Defs.Look(ref customerKind, "customerKind");
            Scribe_Values.Look(ref customerKindId, "customerKindId", "");
            Scribe_Values.Look(ref targetShopZoneId, "targetShopZoneId", -1);
            Scribe_Values.Look(ref targetShopCell, "targetShopCell");
            Scribe_Values.Look(ref totalBudget, "totalBudget", 0);

            Scribe_Collections.Look(ref cartValues, "cartValues", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref satisfactionMap, "satisfactionMap", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref cartItems, "cartItems", LookMode.Value, LookMode.Deep, ref tmpCartItemKeys, ref tmpCartItemValues);
            Scribe_Collections.Look(ref pawnSettings, "pawnSettings", LookMode.Value, LookMode.Deep, ref tmpSettingKeys, ref tmpSettingValues);
            Scribe_Collections.Look(ref checkoutOrder, "checkoutOrder", LookMode.Value, LookMode.Value);
            Scribe_Values.Look(ref nextCheckoutOrder, "nextCheckoutOrder", 1);
            Scribe_Collections.Look(ref readyForCheckout, "readyForCheckout", LookMode.Value);
            Scribe_Collections.Look(ref browseWaitStartTick, "browseWaitStartTick", LookMode.Value, LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (cartValues == null) cartValues = new Dictionary<int, float>();
                if (satisfactionMap == null) satisfactionMap = new Dictionary<int, float>();
                if (cartItems == null) cartItems = new Dictionary<int, List<CustomerCartItem>>();
                if (pawnSettings == null) pawnSettings = new Dictionary<int, CustomerRuntimeSettings>();
                if (checkoutOrder == null) checkoutOrder = new Dictionary<int, int>();
                if (readyForCheckout == null) readyForCheckout = new List<int>();
                if (browseWaitStartTick == null) browseWaitStartTick = new Dictionary<int, int>();
            }
        }
    }
}
