using Verse;

namespace RimSimRestaurantExtension.Conveyor.Transport
{
    //保存一盘实物的业务来源，职责是随运输保留成本并在取餐时匹配上架规则。
    public sealed class ConveyorPlate : IExposable
    {
        public string ruleId;
        public float cost;
        public bool wasteRecorded;
        internal ConveyorTransportNode previousDrawNode;
        internal float previousDrawProgress;
        public int previousDrawTick = -1;

        //保存成本和清理标记，实物由所在建筑容器独立持有。
        public void ExposeData()
        {
            Scribe_Values.Look(ref ruleId, "ruleId");
            Scribe_Values.Look(ref cost, "cost");
            Scribe_Values.Look(ref wasteRecorded, "wasteRecorded");
        }
    }
}
