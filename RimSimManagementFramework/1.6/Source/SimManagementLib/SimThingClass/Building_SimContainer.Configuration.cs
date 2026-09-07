using SimManagementLib.Pojo;
using SimManagementLib.SimDef;
using SimManagementLib.SimThingComp;
using SimManagementLib.Tool;
using System.Collections.Generic;
using Verse;

namespace SimManagementLib.SimThingClass
{
    //货柜商品配置模块，职责是枚举启用商品并把玩家设置限制在货柜总容量内。
    public partial class Building_SimContainer
    {
        //枚举当前配置中全部允许销售的商品定义。
        public virtual IEnumerable<ThingDef> ActiveDefs
        {
            get
            {
                ThingComp_GoodsData comp = GoodsComp;
                if (comp == null || string.IsNullOrEmpty(comp.ActiveGoodsDefName))
                    yield break;
                if (!comp.AllowsGoodsCategory(comp.ActiveGoodsDefName))
                    yield break;

                IReadOnlyList<RuntimeGoodsItem> items = GoodsCatalog.GetItems(comp.ActiveGoodsDefName);
                for (int i = 0; i < items.Count; i++)
                {
                    ThingDef thingDef = items[i]?.thingDef;
                    if (thingDef != null && comp.AllowsThingDef(thingDef))
                        yield return thingDef;
                }
            }
        }

        //将传入的商品设置限制到当前货柜容量内。
        public Dictionary<string, GoodsItemData> ClampSettingsToCapacity(
            string activeDefName,
            Dictionary<string, GoodsItemData> source,
            out int trimmedCount)
        {
            trimmedCount = 0;
            ThingComp_GoodsData comp = GoodsComp;
            if (comp != null && !comp.AllowsGoodsCategory(activeDefName))
                return new Dictionary<string, GoodsItemData>();

            Dictionary<string, GoodsItemData> result = CloneSettings(source);
            IReadOnlyList<RuntimeGoodsItem> items = GoodsCatalog.GetItems(activeDefName);
            int used = 0;
            for (int i = 0; i < items.Count; i++)
            {
                ThingDef thingDef = items[i]?.thingDef;
                if (thingDef == null || !result.TryGetValue(thingDef.defName, out GoodsItemData data) || data == null)
                    continue;
                if (comp != null && !comp.AllowsThingDef(thingDef))
                {
                    if (data.enabled)
                        trimmedCount += UnityEngine.Mathf.Max(0, data.count);
                    DisableItem(data);
                    continue;
                }
                if (!data.enabled || data.count <= 0)
                {
                    DisableItem(data);
                    continue;
                }

                int allowed = MaxTotalCapacity - used;
                if (allowed <= 0)
                {
                    trimmedCount += data.count;
                    DisableItem(data);
                    continue;
                }
                if (data.count > allowed)
                {
                    trimmedCount += data.count - allowed;
                    data.count = allowed;
                }
                data.restockThreshold = GoodsItemData.NormalizeRestockThreshold(data.restockThreshold, data.count);
                used += data.count;
            }
            return result;
        }

        //把单个商品设置为未启用状态。
        private static void DisableItem(GoodsItemData data)
        {
            data.enabled = false;
            data.count = 0;
            data.restockThreshold = 0;
        }

        //复制商品配置，职责是避免 UI 编辑直接修改原始配置对象。
        private static Dictionary<string, GoodsItemData> CloneSettings(Dictionary<string, GoodsItemData> source)
        {
            Dictionary<string, GoodsItemData> result = new Dictionary<string, GoodsItemData>();
            if (source == null)
                return result;
            foreach (KeyValuePair<string, GoodsItemData> entry in source)
            {
                GoodsItemData item = entry.Value;
                int count = UnityEngine.Mathf.Max(0, item?.count ?? 0);
                result[entry.Key] = new GoodsItemData
                {
                    enabled = item?.enabled ?? false,
                    count = count,
                    price = UnityEngine.Mathf.Max(0f, item?.price ?? 0f),
                    restockThreshold = GoodsItemData.NormalizeRestockThreshold(item?.restockThreshold ?? -1, count)
                };
            }
            return result;
        }
    }
}
