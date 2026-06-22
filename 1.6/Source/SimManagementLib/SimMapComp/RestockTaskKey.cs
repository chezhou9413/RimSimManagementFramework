using System;
using Verse;

namespace SimManagementLib.SimMapComp
{
    //补货任务键，职责是按货柜编号和商品定义唯一标识一个补货需求。
    public struct RestockTaskKey : IEquatable<RestockTaskKey>
    {
        public readonly int StorageId;
        public readonly ThingDef ThingDef;

        //创建补货任务键，职责是保存去重所需的稳定字段。
        public RestockTaskKey(int storageId, ThingDef thingDef)
        {
            StorageId = storageId;
            ThingDef = thingDef;
        }

        //判断任务键是否相同，职责是支持字典和集合去重。
        public bool Equals(RestockTaskKey other)
        {
            return StorageId == other.StorageId && ThingDef == other.ThingDef;
        }

        //判断对象是否为相同任务键。
        public override bool Equals(object obj)
        {
            return obj is RestockTaskKey other && Equals(other);
        }

        //返回任务键哈希，职责是支持哈希集合快速查找。
        public override int GetHashCode()
        {
            unchecked
            {
                return (StorageId * 397) ^ (ThingDef?.shortHash ?? 0);
            }
        }
    }
}
