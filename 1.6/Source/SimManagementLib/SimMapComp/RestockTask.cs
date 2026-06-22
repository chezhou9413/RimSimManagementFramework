using SimManagementLib.SimThingClass;
using Verse;

namespace SimManagementLib.SimMapComp
{
    //补货队列任务，职责是记录一个货柜指定商品的可派发补货请求。
    public sealed class RestockTask
    {
        public readonly int StorageId;
        public readonly ThingDef ThingDef;
        public readonly int CreatedTick;
        public int NeededCount;
        public int SupplyId;
        public int LastCheckedTick;
        public int RetryTick;
        public string StateReason = "";

        //创建补货任务，职责是绑定货柜、商品和生成时刻。
        public RestockTask(Building_SimContainer storage, ThingDef thingDef, int neededCount, int createdTick)
        {
            StorageId = storage?.thingIDNumber ?? -1;
            ThingDef = thingDef;
            NeededCount = neededCount;
            CreatedTick = createdTick;
            SupplyId = -1;
        }

        //返回任务键，职责是让队列按货柜和商品去重。
        public RestockTaskKey Key => new RestockTaskKey(StorageId, ThingDef);
    }
}
