using Verse;

namespace SimManagementLib.Pojo
{
    //单件商品槽位数据，职责是保存真实物品来源、售价和顾客暂存状态。
    public sealed class UniqueGoodsSlotData : IExposable
    {
        public int index;
        public int storedThingId = -1;
        public int pendingSourceThingId = -1;
        public string pendingSourceLabel = "";
        public float price;
        public int reservedCustomerId = -1;

        public bool HasStoredThing => storedThingId >= 0 && reservedCustomerId < 0;
        public bool HasPendingSource => pendingSourceThingId >= 0;
        public bool IsReservedByCustomer => reservedCustomerId >= 0;
        public bool IsOccupied => HasStoredThing || HasPendingSource || IsReservedByCustomer;

        //清空槽位，职责是移除库存、来源、价格和顾客预约状态。
        public void Clear()
        {
            storedThingId = -1;
            pendingSourceThingId = -1;
            pendingSourceLabel = "";
            price = 0f;
            reservedCustomerId = -1;
        }

        //保存或读取槽位状态。
        public void ExposeData()
        {
            Scribe_Values.Look(ref index, "index", 0);
            Scribe_Values.Look(ref storedThingId, "storedThingId", -1);
            Scribe_Values.Look(ref pendingSourceThingId, "pendingSourceThingId", -1);
            Scribe_Values.Look(ref pendingSourceLabel, "pendingSourceLabel", "");
            Scribe_Values.Look(ref price, "price", 0f);
            Scribe_Values.Look(ref reservedCustomerId, "reservedCustomerId", -1);
        }
    }
}
