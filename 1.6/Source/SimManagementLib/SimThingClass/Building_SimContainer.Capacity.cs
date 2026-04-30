using SimManagementLib.SimDef;
using SimManagementLib.SimThingComp;
using SimManagementLib.Tool;
using System.Collections.Generic;
using Verse;

namespace SimManagementLib.SimThingClass
{
    public partial class Building_SimContainer
    {
        public int CountTotalStored()
        {
            int total = 0;
            if (virtualStorage == null) return 0;
            for (int i = 0; i < virtualStorage.Count; i++)
            {
                Thing thing = virtualStorage[i];
                if (thing == null || thing.Destroyed) continue;
                total += UnityEngine.Mathf.Max(0, thing.stackCount);
            }

            return total;
        }

        public int CountTotalPendingIn()
        {
            int total = 0;
            if (pendingIn == null || pendingIn.Count == 0) return 0;

            foreach (int value in pendingIn.Values)
            {
                total += UnityEngine.Mathf.Max(0, value);
            }

            return total;
        }

        public int GetRemainingCapacityForPending()
        {
            int remain = MaxTotalCapacity - CountTotalStored() - CountTotalPendingIn();
            return UnityEngine.Mathf.Max(0, remain);
        }

        public int GetRemainingCapacityForStored()
        {
            int remain = MaxTotalCapacity - CountTotalStored();
            return UnityEngine.Mathf.Max(0, remain);
        }

        public int CountConfiguredTargets()
        {
            int total = 0;
            foreach (ThingDef thingDef in ActiveDefs)
            {
                int target = GetTargetCount(thingDef);
                if (target > 0)
                    total += target;
            }

            return total;
        }

        public int GetTargetCount(ThingDef thingDef)
        {
            ThingComp_GoodsData comp = GoodsComp;
            if (comp == null || string.IsNullOrEmpty(comp.ActiveGoodsDefName)) return 0;
            if (!GoodsCatalog.Contains(comp.ActiveGoodsDefName, thingDef)) return 0;
            GoodsItemData item = comp.FindItemData(thingDef);
            if (item == null || !item.enabled) return 0;
            return UnityEngine.Mathf.Max(0, item.count);
        }

        public int CountStored(ThingDef thingDef)
        {
            return virtualStorage.TotalStackCountOfDef(thingDef);
        }

        public int CountPending(ThingDef thingDef)
        {
            return pendingIn.TryGetValue(thingDef, out int value) ? value : 0;
        }

        public int CountNeeded(ThingDef thingDef)
        {
            int perDefNeed = System.Math.Max(0, GetTargetCount(thingDef) - CountStored(thingDef) - CountPending(thingDef));
            if (perDefNeed <= 0) return 0;

            int capacityRemain = GetRemainingCapacityForPending();
            if (capacityRemain <= 0) return 0;

            return System.Math.Min(perDefNeed, capacityRemain);
        }

        public IEnumerable<ThingDef> ActiveDefs
        {
            get
            {
                ThingComp_GoodsData comp = GoodsComp;
                if (comp == null || string.IsNullOrEmpty(comp.ActiveGoodsDefName)) yield break;

                IReadOnlyList<Pojo.RuntimeGoodsItem> items = GoodsCatalog.GetItems(comp.ActiveGoodsDefName);
                for (int i = 0; i < items.Count; i++)
                {
                    ThingDef thingDef = items[i]?.thingDef;
                    if (thingDef != null)
                        yield return thingDef;
                }
            }
        }

        public Dictionary<string, GoodsItemData> ClampSettingsToCapacity(string activeDefName, Dictionary<string, GoodsItemData> source, out int trimmedCount)
        {
            trimmedCount = 0;
            Dictionary<string, GoodsItemData> result = CloneSettings(source);
            IReadOnlyList<Pojo.RuntimeGoodsItem> items = GoodsCatalog.GetItems(activeDefName);
            if (items.Count <= 0) return result;

            int used = 0;
            int max = MaxTotalCapacity;

            for (int i = 0; i < items.Count; i++)
            {
                ThingDef thingDef = items[i]?.thingDef;
                if (thingDef == null) continue;
                if (!result.TryGetValue(thingDef.defName, out GoodsItemData data) || data == null) continue;

                if (!data.enabled || data.count <= 0)
                {
                    data.enabled = false;
                    data.count = 0;
                    continue;
                }

                int allow = max - used;
                if (allow <= 0)
                {
                    trimmedCount += data.count;
                    data.enabled = false;
                    data.count = 0;
                    continue;
                }

                if (data.count > allow)
                {
                    trimmedCount += data.count - allow;
                    data.count = allow;
                }

                used += data.count;
            }

            return result;
        }

        private static Dictionary<string, GoodsItemData> CloneSettings(Dictionary<string, GoodsItemData> source)
        {
            Dictionary<string, GoodsItemData> result = new Dictionary<string, GoodsItemData>();
            if (source == null) return result;

            foreach (KeyValuePair<string, GoodsItemData> kvp in source)
            {
                GoodsItemData item = kvp.Value;
                result[kvp.Key] = new GoodsItemData
                {
                    enabled = item?.enabled ?? false,
                    count = UnityEngine.Mathf.Max(0, item?.count ?? 0),
                    price = UnityEngine.Mathf.Max(0f, item?.price ?? 0f)
                };
            }

            return result;
        }
    }
}
