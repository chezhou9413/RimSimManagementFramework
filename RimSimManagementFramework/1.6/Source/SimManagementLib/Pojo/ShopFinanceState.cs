using Verse;

namespace SimManagementLib.Pojo
{
    public class ShopFinanceState : IExposable
    {
        public string label = "";
        public float revenue;
        public float profit;

        public void ExposeData()
        {
            Scribe_Values.Look(ref label, "label", "");
            Scribe_Values.Look(ref revenue, "revenue", 0f);
            Scribe_Values.Look(ref profit, "profit", 0f);
        }
    }
}
