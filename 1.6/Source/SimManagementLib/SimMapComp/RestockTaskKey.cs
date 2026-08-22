using System;
using Verse;

namespace SimManagementLib.SimMapComp
{
    //补货请求类型，职责是区分按定义计数的普通补货和按槽位搬运的专业补货。
    public enum RestockRequestKind
    {
        Bulk,
        Unique
    }

    //补货请求键，职责是稳定标识普通货柜商品或专业货柜槽位。
    public struct RestockTaskKey : IEquatable<RestockTaskKey>
    {
        public readonly RestockRequestKind Kind;
        public readonly int StorageId;
        public readonly ThingDef ThingDef;
        public readonly int SlotIndex;
        public readonly int SourceThingId;

        //创建普通货柜补货请求键。
        public RestockTaskKey(int storageId, ThingDef thingDef)
        {
            Kind = RestockRequestKind.Bulk;
            StorageId = storageId;
            ThingDef = thingDef;
            SlotIndex = -1;
            SourceThingId = -1;
        }

        //创建完整补货请求键。
        private RestockTaskKey(RestockRequestKind kind, int storageId, ThingDef thingDef, int slotIndex, int sourceThingId)
        {
            Kind = kind;
            StorageId = storageId;
            ThingDef = thingDef;
            SlotIndex = slotIndex;
            SourceThingId = sourceThingId;
        }

        //创建专业货柜精确来源请求键。
        public static RestockTaskKey ForUnique(int storageId, int slotIndex, int sourceThingId)
        {
            return new RestockTaskKey(RestockRequestKind.Unique, storageId, null, slotIndex, sourceThingId);
        }

        //判断两个补货请求键是否表示同一项需求。
        public bool Equals(RestockTaskKey other)
        {
            return Kind == other.Kind
                && StorageId == other.StorageId
                && ThingDef == other.ThingDef
                && SlotIndex == other.SlotIndex
                && SourceThingId == other.SourceThingId;
        }

        //判断对象是否为相同补货请求键。
        public override bool Equals(object obj)
        {
            return obj is RestockTaskKey other && Equals(other);
        }

        //生成补货请求键哈希。
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Kind;
                hash = hash * 397 ^ StorageId;
                hash = hash * 397 ^ (ThingDef?.shortHash ?? 0);
                hash = hash * 397 ^ SlotIndex;
                hash = hash * 397 ^ SourceThingId;
                return hash;
            }
        }
    }
}
