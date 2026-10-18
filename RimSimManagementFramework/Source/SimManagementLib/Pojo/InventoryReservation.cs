using Verse;

namespace SimManagementLib.Pojo
{
    //记录一份真实库存的业务预留，职责是保存稳定业务标识、实物和数量。
    public sealed class InventoryReservation : IExposable
    {
        public string key;
        public Thing thing;
        public int count;

        //保存业务预留，职责是在读档后继续引用同一份实物。
        public void ExposeData()
        {
            Scribe_Values.Look(ref key, "key");
            Scribe_References.Look(ref thing, "thing");
            Scribe_Values.Look(ref count, "count");
        }
    }
}
