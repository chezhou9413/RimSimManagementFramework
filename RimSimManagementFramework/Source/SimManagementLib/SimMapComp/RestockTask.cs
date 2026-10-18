using SimManagementLib.SimThingClass;
using Verse;

namespace SimManagementLib.SimMapComp
{
    //补货请求，职责是保存可长期重试的缺货需求及其最近运行状态。
    public sealed class RestockTask
    {
        public readonly RestockTaskKey Key;
        public readonly int CreatedTick;
        public int NeededCount;
        public int SupplyId = -1;
        public int LastCheckedTick;
        public int RetryTick;
        public string StateReason = "";

        public RestockRequestKind Kind => Key.Kind;
        public int StorageId => Key.StorageId;
        public ThingDef ThingDef => Key.ThingDef;
        public int SlotIndex => Key.SlotIndex;
        public int SourceThingId => Key.SourceThingId;

        //创建普通货柜补货请求。
        public RestockTask(Building_SimContainer storage, ThingDef thingDef, int createdTick)
        {
            Key = new RestockTaskKey(storage?.thingIDNumber ?? -1, thingDef);
            CreatedTick = createdTick;
        }

        //创建专业货柜精确补货请求。
        public RestockTask(Building_UniqueGoodsContainer storage, int slotIndex, int sourceThingId, int createdTick)
        {
            Key = RestockTaskKey.ForUnique(storage?.thingIDNumber ?? -1, slotIndex, sourceThingId);
            CreatedTick = createdTick;
            NeededCount = 1;
            SupplyId = sourceThingId;
        }
    }
}
