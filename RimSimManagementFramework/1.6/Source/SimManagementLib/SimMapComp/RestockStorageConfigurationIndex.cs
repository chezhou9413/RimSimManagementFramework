using SimManagementLib.SimThingClass;
using System;
using System.Collections.Generic;
using Verse;

namespace SimManagementLib.SimMapComp
{
    //货柜补货配置项，职责是保存热路径所需的目标、阈值和商品定义快照。
    internal struct RestockTargetSettings
    {
        public readonly ThingDef ThingDef;
        public readonly int TargetCount;
        public readonly int Threshold;

        //创建一项已经校验的普通货柜补货配置。
        public RestockTargetSettings(ThingDef thingDef, int targetCount, int threshold)
        {
            ThingDef = thingDef;
            TargetCount = Math.Max(0, targetCount);
            Threshold = Math.Max(0, Math.Min(TargetCount, threshold));
        }
    }

    //货柜补货配置快照，职责是让每 tick 巡检只读取稳定列表和库存版本，不再枚举完整商品目录。
    internal sealed class RestockStorageConfigurationIndex
    {
        private static readonly RestockTargetSettings[] EmptyItems = new RestockTargetSettings[0];
        private readonly Dictionary<int, List<RestockTargetSettings>> itemsByStorage = new Dictionary<int, List<RestockTargetSettings>>();
        private readonly Dictionary<int, Dictionary<ThingDef, RestockTargetSettings>> lookupByStorage = new Dictionary<int, Dictionary<ThingDef, RestockTargetSettings>>();
        private readonly Dictionary<int, int> observedInventoryVersions = new Dictionary<int, int>();

        //从货柜权威配置刷新普通商品快照，专业货柜只登记库存版本。
        public void Refresh(Building_SimContainer storage)
        {
            if (storage == null)
                return;
            int storageId = storage.thingIDNumber;
            observedInventoryVersions[storageId] = storage.StoredCountVersion;
            itemsByStorage.Remove(storageId);
            lookupByStorage.Remove(storageId);
            if (storage is Building_UniqueGoodsContainer)
                return;

            List<RestockTargetSettings> items = new List<RestockTargetSettings>();
            Dictionary<ThingDef, RestockTargetSettings> lookup = new Dictionary<ThingDef, RestockTargetSettings>();
            foreach (ThingDef thingDef in storage.ActiveDefs)
            {
                if (thingDef == null || lookup.ContainsKey(thingDef))
                    continue;
                int target = storage.GetTargetCount(thingDef);
                if (target <= 0)
                    continue;
                RestockTargetSettings settings = new RestockTargetSettings(
                    thingDef,
                    target,
                    storage.GetRestockThreshold(thingDef));
                items.Add(settings);
                lookup.Add(thingDef, settings);
            }
            itemsByStorage[storageId] = items;
            lookupByStorage[storageId] = lookup;
        }

        //返回指定普通货柜的稳定补货配置列表。
        public IReadOnlyList<RestockTargetSettings> GetItems(int storageId)
        {
            if (itemsByStorage.TryGetValue(storageId, out List<RestockTargetSettings> items))
                return items;
            return EmptyItems;
        }

        //按货柜和商品定义读取已缓存的补货目标。
        public bool TryGetSettings(int storageId, ThingDef thingDef, out RestockTargetSettings settings)
        {
            settings = default(RestockTargetSettings);
            return thingDef != null
                && lookupByStorage.TryGetValue(storageId, out Dictionary<ThingDef, RestockTargetSettings> lookup)
                && lookup.TryGetValue(thingDef, out settings);
        }

        //记录库存变化已经由精确通知处理，避免兜底巡检重复重算同一货柜。
        public void ObserveInventory(Building_SimContainer storage)
        {
            if (storage != null)
                observedInventoryVersions[storage.thingIDNumber] = storage.StoredCountVersion;
        }

        //判断货柜库存版本是否绕过了精确通知发生变化。
        public bool HasUnobservedInventoryChange(Building_SimContainer storage)
        {
            if (storage == null)
                return false;
            return !observedInventoryVersions.TryGetValue(storage.thingIDNumber, out int observed)
                || observed != storage.StoredCountVersion;
        }

        //移除已经离开地图的货柜配置和版本记录。
        public void Remove(int storageId)
        {
            itemsByStorage.Remove(storageId);
            lookupByStorage.Remove(storageId);
            observedInventoryVersions.Remove(storageId);
        }

        //清空全部运行时配置快照。
        public void Clear()
        {
            itemsByStorage.Clear();
            lookupByStorage.Clear();
            observedInventoryVersions.Clear();
        }
    }
}
