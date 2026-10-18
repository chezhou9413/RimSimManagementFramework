using SimManagementLib.Pojo;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimThingComp;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    public partial class Dialog_ShopManager
    {
        /// <summary>
        /// 确保套餐页缓存反映当前搜索、货柜商品和套餐内容，负责避免绘制阶段反复扫描列表。
        /// </summary>
        private void EnsureComboUiCache()
        {
            if (curCombo == null)
                return;

            int storageSignature = BuildComboStorageSignature();
            int itemsSignature = BuildComboItemsSignature(curCombo);
            string searchKey = searchQuery ?? "";
            bool comboChanged = cachedCombo != curCombo;

            if (comboChanged || comboStorageCacheSignature != storageSignature || comboItemsCacheSignature != itemsSignature)
                RebuildComboItemCache(itemsSignature);

            if (comboChanged || comboStorageCacheSignature != storageSignature || comboSearchCacheKey != searchKey)
                RebuildComboSellableCache(storageSignature, searchKey);

            cachedCombo = curCombo;
        }

        /// <summary>
        /// 标记套餐页缓存失效，负责在导入、删除或批量调整后强制刷新列表。
        /// </summary>
        private void InvalidateComboUiCache()
        {
            cachedCombo = null;
            comboSearchCacheKey = "";
            comboStorageCacheSignature = int.MinValue;
            comboItemsCacheSignature = int.MinValue;
            comboSellableCache.Clear();
            comboItemByDefCache.Clear();
            comboReferencePriceCache.Clear();
        }

        /// <summary>
        /// 标记当前套餐物品缓存失效，负责在列表绘制中避免清空正在使用的可售商品缓存。
        /// </summary>
        private void InvalidateComboItemCache()
        {
            comboItemsCacheSignature = int.MinValue;
        }

        /// <summary>
        /// 重建当前套餐物品字典，负责把行查询从线性扫描降为字典查询。
        /// </summary>
        private void RebuildComboItemCache(int itemsSignature)
        {
            comboItemByDefCache.Clear();
            if (curCombo?.items != null)
            {
                for (int i = 0; i < curCombo.items.Count; i++)
                {
                    ComboItem item = curCombo.items[i];
                    if (item?.def != null)
                        comboItemByDefCache[item.def] = item;
                }
            }

            PruneComboCountBuffers();
            comboItemsCacheSignature = itemsSignature;
        }

        /// <summary>
        /// 重建可售商品和参考价缓存，负责避免每帧调用 HasAnyStorageSellingThing。
        /// </summary>
        private void RebuildComboSellableCache(int storageSignature, string searchKey)
        {
            comboSellableCache.Clear();
            comboReferencePriceCache.Clear();

            for (int i = 0; i < availableGoodsDefs.Count; i++)
            {
                ThingDef def = availableGoodsDefs[i];
                if (def == null || !MatchSearch(def.label))
                    continue;

                if (TryGetComboSellableInfo(def, out float price))
                {
                    comboSellableCache.Add(def);
                    comboReferencePriceCache[def] = price;
                }
            }

            comboStorageCacheSignature = storageSignature;
            comboSearchCacheKey = searchKey ?? "";
        }

        /// <summary>
        /// 判断商品是否在任意货柜可售并返回参考价，负责合并可售检查和估价扫描。
        /// </summary>
        private bool TryGetComboSellableInfo(ThingDef thingDef, out float price)
        {
            price = 0f;
            if (thingDef == null)
                return false;

            bool sellable = false;
            float fallbackPrice = Mathf.Max(1f, thingDef.BaseMarketValue);
            float firstConfiguredPrice = 0f;
            Building_SimContainer selectedStorage = GetSelectedStorage();

            for (int i = 0; i < storages.Count; i++)
            {
                Building_SimContainer storage = storages[i];
                if (storage == null || storage.Destroyed)
                    continue;

                ThingComp_GoodsData comp = storage.GetComp<ThingComp_GoodsData>();
                GoodsItemData data = comp?.FindItemData(thingDef);
                if (data == null || data.count <= 0)
                    continue;

                sellable = true;
                if (data.price > 0f)
                {
                    float configuredPrice = Mathf.Max(1f, data.price);
                    if (storage == selectedStorage)
                    {
                        price = configuredPrice;
                        return true;
                    }

                    if (firstConfiguredPrice <= 0f)
                        firstConfiguredPrice = configuredPrice;
                }
            }

            if (!sellable)
                return false;

            price = firstConfiguredPrice > 0f ? firstConfiguredPrice : fallbackPrice;
            return true;
        }

        /// <summary>
        /// 返回当前套餐中指定商品项，负责给行绘制使用缓存字典。
        /// </summary>
        private ComboItem GetCachedComboItem(ThingDef thingDef)
        {
            if (thingDef == null)
                return null;

            comboItemByDefCache.TryGetValue(thingDef, out ComboItem item);
            return item;
        }

        /// <summary>
        /// 返回套餐项数量输入缓存，负责避免每帧重新创建数字文本。
        /// </summary>
        private string GetComboCountBuffer(ThingDef thingDef, int count)
        {
            string key = thingDef?.defName ?? "";
            if (string.IsNullOrEmpty(key))
                return count.ToString();

            if (!comboItemCountBuffers.TryGetValue(key, out string buffer))
            {
                buffer = count.ToString();
                comboItemCountBuffers[key] = buffer;
            }

            return buffer;
        }

        /// <summary>
        /// 更新套餐项数量输入缓存，负责让 TextFieldNumeric 的编辑中间态跨帧保留。
        /// </summary>
        private void SetComboCountBuffer(ThingDef thingDef, string buffer)
        {
            string key = thingDef?.defName ?? "";
            if (!string.IsNullOrEmpty(key))
                comboItemCountBuffers[key] = buffer ?? "";
        }

        /// <summary>
        /// 清理不再属于当前套餐的数量输入缓存，负责避免长期积累无效字符串。
        /// </summary>
        private void PruneComboCountBuffers()
        {
            List<string> removeKeys = new List<string>();
            foreach (string key in comboItemCountBuffers.Keys)
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(key);
                if (def == null || !comboItemByDefCache.ContainsKey(def))
                    removeKeys.Add(key);
            }

            for (int i = 0; i < removeKeys.Count; i++)
                comboItemCountBuffers.Remove(removeKeys[i]);
        }

        /// <summary>
        /// 返回套餐估价参考价，负责优先使用缓存并在缓存缺失时回退到旧逻辑。
        /// </summary>
        private float GetCachedReferencePriceForCombo(ThingDef thingDef)
        {
            if (thingDef == null)
                return 1f;

            if (comboReferencePriceCache.TryGetValue(thingDef, out float price))
                return price;

            return GetReferencePriceForCombo(thingDef);
        }

        /// <summary>
        /// 计算货柜商品状态签名，负责判断可售列表和参考价是否需要重建。
        /// </summary>
        private int BuildComboStorageSignature()
        {
            unchecked
            {
                int hash = 17;
                for (int i = 0; i < storages.Count; i++)
                {
                    Building_SimContainer storage = storages[i];
                    if (storage == null || storage.Destroyed)
                        continue;

                    hash = hash * 31 + storage.thingIDNumber;
                    ThingComp_GoodsData comp = storage.GetComp<ThingComp_GoodsData>();
                    hash = hash * 31 + (comp?.ActiveGoodsDefName?.GetHashCode() ?? 0);
                    if (comp?.itemData == null)
                        continue;

                    foreach (KeyValuePair<string, GoodsItemData> pair in comp.itemData)
                    {
                        GoodsItemData data = pair.Value;
                        if (data == null || !data.enabled)
                            continue;

                        hash = hash * 31 + (pair.Key?.GetHashCode() ?? 0);
                        hash = hash * 31 + data.count;
                        hash = hash * 31 + Mathf.RoundToInt(data.price * 100f);
                    }
                }

                hash = hash * 31 + selectedStorageThingId;
                return hash;
            }
        }

        /// <summary>
        /// 计算套餐物品签名，负责判断当前套餐物品字典是否需要重建。
        /// </summary>
        private static int BuildComboItemsSignature(ComboData combo)
        {
            unchecked
            {
                int hash = 23;
                List<ComboItem> items = combo?.items;
                if (items == null)
                    return hash;

                for (int i = 0; i < items.Count; i++)
                {
                    ComboItem item = items[i];
                    hash = hash * 31 + (item?.def?.defName?.GetHashCode() ?? 0);
                    hash = hash * 31 + (item?.count ?? 0);
                }

                return hash;
            }
        }
    }
}
