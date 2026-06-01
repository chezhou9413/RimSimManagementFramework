using SimManagementLib.Pojo;
using SimManagementLib.SimDef;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimThingComp;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 提供货柜快速补货、分类库存统计和容量目标生成的工具方法。
    /// </summary>
    public static class ShopStorageUtility
    {
        /// <summary>
        /// 统计单个货柜中指定货品分类的实际库存总数。
        /// </summary>
        public static int CountStoredByCategory(Building_SimContainer storage, string categoryId)
        {
            if (storage == null || string.IsNullOrEmpty(categoryId)) return 0;

            int total = 0;
            IReadOnlyList<RuntimeGoodsItem> items = GoodsCatalog.GetItems(categoryId);
            for (int i = 0; i < items.Count; i++)
            {
                ThingDef thingDef = items[i]?.thingDef;
                if (thingDef != null)
                    total += storage.CountStored(thingDef);
            }

            return total;
        }

        /// <summary>
        /// 统计多个货柜中指定货品分类的实际库存总数。
        /// </summary>
        public static int CountStoredByCategory(IEnumerable<Building_SimContainer> storages, string categoryId)
        {
            if (storages == null || string.IsNullOrEmpty(categoryId)) return 0;

            int total = 0;
            foreach (Building_SimContainer storage in storages)
            {
                if (storage == null || storage.Destroyed) continue;
                total += CountStoredByCategory(storage, categoryId);
            }

            return total;
        }

        /// <summary>
        /// 获取单个货柜中指定货品分类的分物品库存数量。
        /// </summary>
        public static Dictionary<ThingDef, int> GetStoredItemCountsByCategory(Building_SimContainer storage, string categoryId)
        {
            Dictionary<ThingDef, int> result = new Dictionary<ThingDef, int>();
            if (storage == null || string.IsNullOrEmpty(categoryId)) return result;

            IReadOnlyList<RuntimeGoodsItem> items = GoodsCatalog.GetItems(categoryId);
            for (int i = 0; i < items.Count; i++)
            {
                ThingDef thingDef = items[i]?.thingDef;
                if (thingDef == null) continue;

                int count = storage.CountStored(thingDef);
                if (count > 0)
                    result[thingDef] = count;
            }

            return result;
        }

        /// <summary>
        /// 统计单个货柜中指定货品分类的配置目标总数。
        /// </summary>
        public static int CountTargetByCategory(Building_SimContainer storage, string categoryId)
        {
            if (storage == null || string.IsNullOrEmpty(categoryId)) return 0;

            int total = 0;
            IReadOnlyList<RuntimeGoodsItem> items = GoodsCatalog.GetItems(categoryId);
            for (int i = 0; i < items.Count; i++)
            {
                ThingDef thingDef = items[i]?.thingDef;
                if (thingDef != null)
                    total += storage.GetTargetCount(thingDef);
            }

            return total;
        }

        /// <summary>
        /// 按货柜已经配置好的目标量补齐库存，返回实际生成并存入的件数。
        /// </summary>
        public static int FillConfiguredTargets(Building_SimContainer storage)
        {
            if (storage == null || storage.Destroyed) return 0;
            storage.ReconcilePendingReservations();

            int totalAdded = 0;
            List<ThingDef> defs = storage.ActiveDefs.Where(def => def != null).ToList();
            for (int i = 0; i < defs.Count; i++)
            {
                ThingDef thingDef = defs[i];
                int need = Mathf.Max(0, storage.GetTargetCount(thingDef) - storage.CountStored(thingDef));
                if (need <= 0) continue;

                totalAdded += storage.TryCreateAndStore(thingDef, need);
                if (storage.GetRemainingCapacityForStored() <= 0)
                    break;
            }

            storage.ReconcilePendingReservations();
            return totalAdded;
        }

        /// <summary>
        /// 将货柜设置为指定 GoodsDef 分类并按容量铺满，返回实际生成并存入的件数。
        /// </summary>
        public static int ConfigureCategoryAndFillToCapacity(Building_SimContainer storage, GoodsDef goodsDef, int maxItemTypes = 0)
        {
            if (goodsDef == null) return 0;
            return ConfigureCategoryAndFillToCapacity(storage, goodsDef.defName, goodsDef.GoodsList, maxItemTypes);
        }

        /// <summary>
        /// 将货柜设置为指定运行时分类并按容量铺满，返回实际生成并存入的件数。
        /// </summary>
        public static int ConfigureCategoryAndFillToCapacity(Building_SimContainer storage, string categoryId, int maxItemTypes = 0)
        {
            return ConfigureCategoryAndFillToCapacity(storage, categoryId, null, maxItemTypes);
        }

        /// <summary>
        /// 按指定商品列表生成容量目标并补满货柜，运行时分类不可用时使用传入列表兜底。
        /// </summary>
        private static int ConfigureCategoryAndFillToCapacity(
            Building_SimContainer storage,
            string categoryId,
            IEnumerable<ThingDef> fallbackDefs,
            int maxItemTypes)
        {
            if (storage == null || storage.Destroyed || string.IsNullOrEmpty(categoryId)) return 0;

            ThingComp_GoodsData comp = storage.GetComp<ThingComp_GoodsData>();
            if (comp == null) return 0;
            if (!comp.AllowsGoodsCategory(categoryId)) return 0;

            GoodsCatalog.EnsureInitialized();
            List<ThingDef> defs = ResolveCategoryThingDefs(categoryId, fallbackDefs, maxItemTypes, storage.MaxTotalCapacity);
            if (defs.Count <= 0) return 0;

            Dictionary<string, GoodsItemData> settings = BuildCapacitySettings(defs, storage.MaxTotalCapacity);
            comp.ApplySettings(categoryId, settings);
            return FillConfiguredTargets(storage);
        }

        /// <summary>
        /// 从运行时分类和兜底列表中取出可用物品，并按限制数量去重。
        /// </summary>
        private static List<ThingDef> ResolveCategoryThingDefs(
            string categoryId,
            IEnumerable<ThingDef> fallbackDefs,
            int maxItemTypes,
            int capacity)
        {
            List<ThingDef> result = new List<ThingDef>();
            HashSet<string> seen = new HashSet<string>();
            int limit = maxItemTypes > 0 ? maxItemTypes : int.MaxValue;
            limit = Mathf.Min(limit, Mathf.Max(1, capacity));

            IReadOnlyList<RuntimeGoodsItem> items = GoodsCatalog.GetItems(categoryId);
            for (int i = 0; i < items.Count && result.Count < limit; i++)
                TryAddThingDef(result, seen, items[i]?.thingDef);

            if (fallbackDefs != null)
            {
                foreach (ThingDef thingDef in fallbackDefs)
                {
                    if (result.Count >= limit) break;
                    TryAddThingDef(result, seen, thingDef);
                }
            }

            return result;
        }

        /// <summary>
        /// 将一个物品定义加入列表，并避免同一个 defName 重复出现。
        /// </summary>
        private static void TryAddThingDef(List<ThingDef> result, HashSet<string> seen, ThingDef thingDef)
        {
            if (thingDef == null || string.IsNullOrEmpty(thingDef.defName)) return;
            if (!seen.Add(thingDef.defName)) return;
            result.Add(thingDef);
        }

        /// <summary>
        /// 将货柜容量平均分配到选中的物品上，并生成可直接保存的商品配置。
        /// </summary>
        private static Dictionary<string, GoodsItemData> BuildCapacitySettings(List<ThingDef> defs, int capacity)
        {
            Dictionary<string, GoodsItemData> settings = new Dictionary<string, GoodsItemData>();
            if (defs.NullOrEmpty() || capacity <= 0) return settings;

            int baseCount = Mathf.Max(1, capacity / defs.Count);
            int remainder = Mathf.Max(0, capacity - baseCount * defs.Count);

            for (int i = 0; i < defs.Count; i++)
            {
                ThingDef thingDef = defs[i];
                int count = baseCount + (i < remainder ? 1 : 0);
                settings[thingDef.defName] = new GoodsItemData
                {
                    enabled = true,
                    count = count,
                    price = Mathf.Max(1f, Mathf.Round(thingDef.BaseMarketValue))
                };
            }

            return settings;
        }
    }
}
