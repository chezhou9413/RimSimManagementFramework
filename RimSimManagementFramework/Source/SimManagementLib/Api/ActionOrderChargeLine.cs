using Verse;
namespace SimManagementLib.Api
{
    //保存子业务账单，职责是冻结收费和成本并记录两个写入步骤。
    public sealed class ActionOrderChargeLine : IExposable
    {
        public string key, label;
        public int count;
        public float amount, cost;
        public bool billRegistered, financeRegistered;
        //保存明细与幂等提交状态。
        public void ExposeData()
        {
            Scribe_Values.Look(ref key, "key");
            Scribe_Values.Look(ref label, "label");
            Scribe_Values.Look(ref count, "count");
            Scribe_Values.Look(ref amount, "amount");
            Scribe_Values.Look(ref cost, "cost");
            Scribe_Values.Look(ref billRegistered, "billRegistered");
            Scribe_Values.Look(ref financeRegistered, "financeRegistered");
        }
    }
}
