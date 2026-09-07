using SimManagementLib.GameComp;
using SimManagementLib.Pojo;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimThingComp;
using SimManagementLib.SimZone;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SimManagementLib.Tool
{
    //汇集店铺库存与购物数据，职责是协调零售筛选、套餐扣货和退货。
    public static partial class ShopDataUtility
    {
        //获取指定区域内所有的货柜（自动去重）
        public static HashSet<Building_SimContainer> GetStoragesInZone(Zone zone)
        {
            return new HashSet<Building_SimContainer>(GetStorageSnapshotInZone(zone));
        }

        //获取该区域内所有可以出售的商品（即启用的商品，并汇总库存）
        public static List<ShopItemStatus> GetAllSellableGoods(Zone zone)
        {
            var aggregatedData = new Dictionary<ThingDef, ShopItemStatus>();
            IReadOnlyList<Building_SimContainer> storages = GetStorageSnapshotInZone(zone);

            foreach (Building_SimContainer storage in storages)
            {
                if (!storage.AllowsCustomerSelfPurchase) continue;
                if (storage is Building_UniqueGoodsContainer uniqueContainer)
                {
                    foreach (UniqueGoodsSlotData slot in uniqueContainer.GetSellableSlots())
                    {
                        Thing uniqueThing = uniqueContainer.GetStoredThing(slot);
                        if (uniqueThing == null) continue;
                        if (aggregatedData.TryGetValue(uniqueThing.def, out ShopItemStatus uniqueStatus))
                        {
                            uniqueStatus.CurrentStock++;
                            uniqueStatus.Config.count++;
                            uniqueStatus.Config.price = (uniqueStatus.Config.price * (uniqueStatus.CurrentStock - 1) + slot.price) / uniqueStatus.CurrentStock;
                        }
                        else
                        {
                            aggregatedData[uniqueThing.def] = new ShopItemStatus
                            {
                                Def = uniqueThing.def,
                                CurrentStock = 1,
                                Config = new GoodsItemData { enabled = true, count = 1, price = Mathf.Max(1f, slot.price) }
                            };
                        }
                    }
                    continue;
                }

                var comp = storage.GetComp<ThingComp_GoodsData>();
                if (comp == null || string.IsNullOrEmpty(comp.ActiveGoodsDefName)) continue;

                foreach (ThingDef td in storage.ActiveDefs)
                {
                    GoodsItemData config = comp.FindItemData(td);
                    if (config == null || !config.enabled) continue;

                    int stock = storage.CountStored(td);

                    if (aggregatedData.TryGetValue(td, out ShopItemStatus existingStatus))
                    {
                        existingStatus.CurrentStock += stock;
                    }
                    else
                    {
                        aggregatedData[td] = new ShopItemStatus
                        {
                            Def = td,
                            Config = config,
                            CurrentStock = stock
                        };
                    }
                }
            }

            return aggregatedData.Values.ToList();
        }

        //获取这个区域里面目前有货的商品数据
        public static List<ShopItemStatus> GetInStockGoods(Zone zone)
        {
            return GetAllSellableGoods(zone).Where(item => item.CurrentStock > 0).ToList();
        }

        //获取这个区域里面缺货的商品数据
        public static List<ShopItemStatus> GetOutOfStockGoods(Zone zone)
        {
            return GetAllSellableGoods(zone).Where(item => item.CurrentStock <= 0).ToList();
        }

        //根据目标商店坐标查找商店区域。
        public static Zone_Shop FindShopZone(Map map, IntVec3 targetShopCell)
        {
            if (map == null || !targetShopCell.IsValid || !targetShopCell.InBounds(map)) return null;

            Zone zone = map.zoneManager.ZoneAt(targetShopCell);
            if (zone is Zone_Shop directShop)
                return directShop;

            List<Zone> zones = map.zoneManager.AllZones;
            for (int i = 0; i < zones.Count; i++)
            {
                if (zones[i] is Zone_Shop shop && shop.Cells.Contains(targetShopCell))
                    return shop;
            }

            return null;
        }

        //严格按“商店ID + 目标坐标”查找顾客所属商店，不会回退到其他商店。
        public static Zone_Shop FindAssignedShopZone(Map map, int targetShopZoneId, IntVec3 targetShopCell)
        {
            if (map == null) return null;

            if (targetShopZoneId >= 0)
            {
                List<Zone> zones = map.zoneManager.AllZones;
                for (int i = 0; i < zones.Count; i++)
                {
                    if (zones[i] is Zone_Shop byId && byId.ID == targetShopZoneId)
                        return byId;
                }
            }

            return FindShopZone(map, targetShopCell);
        }

        //获取指定预算内且当前库存可满足的套餐，按价格从高到低排序。
        public static List<ComboData> GetAffordableInStockCombos(Zone zone, float budget)
        {
            return GetAffordableInStockCombos(zone, budget, null);
        }

        //获取指定预算内、价格未被顾客拒绝且当前库存可满足的套餐，按价格从高到低排序。
        public static List<ComboData> GetAffordableInStockCombos(Zone zone, float budget, CustomerPriceSensitivityProps sensitivity)
        {
            if (zone == null || zone.Map == null || budget <= 0f)
                return new List<ComboData>();

            GameComponent_ShopComboManager comboManager = Current.Game?.GetComponent<GameComponent_ShopComboManager>();
            if (comboManager == null) return new List<ComboData>();

            List<ComboData> combos = comboManager.GetCombosForZone(zone);
            if (combos.NullOrEmpty()) return new List<ComboData>();

            return combos
                .Where(c => c != null && !c.items.NullOrEmpty())
                .Where(c => GetComboEffectivePrice(c) <= budget)
                .Where(c => !CustomerPriceUtility.EvaluateCombo(GetComboEffectivePrice(c), GetComboReferenceValue(c), sensitivity).rejected)
                .Where(c => HasEnoughStockForCombo(zone, c))
                .OrderByDescending(GetComboEffectivePrice)
                .ToList();
        }

        //尝试查找因高溢价被顾客拒绝的套餐，负责给无消费评价提供价格原因。
        public static bool TryFindRejectedComboPriceReason(Zone zone, CustomerPriceSensitivityProps sensitivity, System.Func<ComboData, bool> matchesCustomer, out string reason)
        {
            reason = "";
            if (zone == null || zone.Map == null)
                return false;

            GameComponent_ShopComboManager comboManager = Current.Game?.GetComponent<GameComponent_ShopComboManager>();
            List<ComboData> combos = comboManager?.GetCombosForZone(zone);
            if (combos.NullOrEmpty())
                return false;

            for (int i = 0; i < combos.Count; i++)
            {
                ComboData combo = combos[i];
                if (combo == null || combo.items.NullOrEmpty()) continue;
                if (matchesCustomer != null && !matchesCustomer(combo)) continue;
                if (!HasEnoughStockForCombo(zone, combo)) continue;

                float price = GetComboEffectivePrice(combo);
                CustomerPriceEvaluation evaluation = CustomerPriceUtility.EvaluateCombo(price, GetComboReferenceValue(combo), sensitivity);
                if (!evaluation.rejected) continue;

                string label = string.IsNullOrEmpty(combo.comboName) ? "未命名套餐" : combo.comboName;
                reason = $"套餐 {label} 售价约为市价 {evaluation.ratio:F1} 倍，顾客认为价格远高于市价而拒绝购买";
                return true;
            }

            return false;
        }

        //按套餐配置从整个商店区域扣减库存，成功返回 true 并输出应付金额。
        public static bool TryPurchaseCombo(Zone zone, ComboData combo, out float paidPrice)
        {
            paidPrice = 0f;
            if (zone == null || zone.Map == null || combo == null || combo.items.NullOrEmpty())
                return false;

            List<Building_SimContainer> storages = GetStorageSnapshotInZone(zone)
                .Where(storage => storage.AllowsCustomerSelfPurchase && !(storage is Building_UniqueGoodsContainer))
                .ToList();
            if (storages.NullOrEmpty()) return false;
            if (!HasEnoughStockForCombo(zone, combo)) return false;

            List<ComboExtractEntry> extracted = new List<ComboExtractEntry>();

            foreach (ComboItem item in combo.items)
            {
                if (item == null || item.def == null || item.count <= 0)
                {
                    RollbackComboExtract(extracted);
                    return false;
                }

                int remaining = item.count;
                foreach (Building_SimContainer storage in storages)
                {
                    if (remaining <= 0) break;

                    int available = storage.CountStored(item.def);
                    if (available <= 0) continue;

                    int toTake = System.Math.Min(remaining, available);
                    Thing taken = storage.TryVirtualBuy(item.def, toTake, out _);
                    if (taken == null || taken.stackCount <= 0) continue;

                    extracted.Add(new ComboExtractEntry(storage, taken));
                    remaining -= taken.stackCount;
                }

                if (remaining > 0)
                {
                    RollbackComboExtract(extracted);
                    return false;
                }
            }

            foreach (ComboExtractEntry entry in extracted)
            {
                if (entry.Thing != null && !entry.Thing.Destroyed)
                    entry.Thing.Destroy(DestroyMode.Vanish);
            }

            paidPrice = GetComboEffectivePrice(combo);
            return paidPrice > 0f;
        }

        //检查套餐实物库存，职责是只统计允许顾客自取的货柜。
        private static bool HasEnoughStockForCombo(Zone zone, ComboData combo)
        {
            List<Building_SimContainer> storages = GetStorageSnapshotInZone(zone)
                .Where(storage => storage.AllowsCustomerSelfPurchase && !(storage is Building_UniqueGoodsContainer))
                .ToList();
            if (storages.NullOrEmpty()) return false;

            foreach (ComboItem item in combo.items)
            {
                if (item == null || item.def == null || item.count <= 0) return false;
                int totalStock = storages.Sum(s => s.CountStored(item.def));
                if (totalStock < item.count) return false;
            }

            return true;
        }

        //计算套餐成交价，职责是统一配置价格与商品参考价。
        private static float GetComboEffectivePrice(ComboData combo)
        {
            if (combo == null) return 0f;
            if (combo.totalPrice > 0f) return combo.totalPrice;

            float estimated = 0f;
            if (!combo.items.NullOrEmpty())
            {
                foreach (ComboItem item in combo.items)
                {
                    if (item == null || item.def == null || item.count <= 0) continue;
                    estimated += item.def.BaseMarketValue * item.count;
                }
            }

            return estimated > 0f ? estimated : 1f;
        }

        //返回套餐按商品市价计算的参考价值。
        public static float GetComboReferenceValue(ComboData combo)
        {
            return CustomerPriceUtility.GetComboReferenceValue(combo);
        }

        //退回未完成的套餐提取，职责是归还已有实物而不重复生成。
        private static void RollbackComboExtract(List<ComboExtractEntry> extracted)
        {
            List<Building_SimContainer> storages = extracted
                .Where(e => e?.Storage != null && !e.Storage.Destroyed)
                .Select(e => e.Storage)
                .Distinct()
                .ToList();

            foreach (ComboExtractEntry entry in extracted)
            {
                if (entry == null || entry.Storage == null || entry.Storage.Destroyed) continue;
                if (entry.Thing == null || entry.Thing.Destroyed) continue;
                TryReturnThingToStorages(storages, entry.Storage, entry.Thing);
            }
        }

        private sealed class ComboExtractEntry
        {
            public Building_SimContainer Storage;
            public Thing Thing;

            //记录套餐提取来源与实物，职责是支持失败后的库存归还。
            public ComboExtractEntry(Building_SimContainer storage, Thing thing)
            {
                Storage = storage;
                Thing = thing;
            }
        }

        //将顾客购物车中的虚拟商品退回到商店货柜（用于结账超时/放弃结账）。
        public static void ReturnCartItemsToShop(Zone zone, List<CustomerCartItem> items)
        {
            if (zone == null || zone.Map == null || items.NullOrEmpty()) return;

            List<Building_SimContainer> storages = GetStorageSnapshotInZone(zone).Where(s => s.AllowsCustomerSelfPurchase).ToList();

            for (int i = 0; i < items.Count; i++)
            {
                CustomerCartItem cartItem = items[i];
                if (cartItem == null || cartItem.def == null || cartItem.count <= 0) continue;
                if (cartItem.HasExactThing)
                {
                    ReturnExactCartItem(zone, storages, cartItem);
                    continue;
                }
                if (storages.NullOrEmpty()) continue;
                ReturnSingleDefToStorages(storages, cartItem.def, cartItem.count);
            }
        }

        //将购物车托管的真实商品退回原专业货柜，原柜不可用时落到商店附近。
        private static void ReturnExactCartItem(Zone zone, List<Building_SimContainer> storages, CustomerCartItem item)
        {
            Thing thing = item?.TakeExactThing();
            if (thing == null) return;
            Building_UniqueGoodsContainer source = storages
                .OfType<Building_UniqueGoodsContainer>()
                .FirstOrDefault(s => s.thingIDNumber == item.sourceContainerId);
            if (source == null)
            {
                source = zone.Map.listerBuildings?.allBuildingsColonist
                    ?.OfType<Building_UniqueGoodsContainer>()
                    .FirstOrDefault(s => s.thingIDNumber == item.sourceContainerId);
            }
            if (source != null && source.TryRestoreCustomerThing(item.sourceSlotIndex, thing)) return;
            source?.FinalizeCustomerSale(item.sourceSlotIndex, -1);

            IntVec3 dropCell = source?.Position ?? storages.FirstOrDefault()?.Position ?? zone.Cells.FirstOrDefault();
            if (!GenPlace.TryPlaceThing(thing, dropCell, zone.Map, ThingPlaceMode.Near, out _) && !thing.Destroyed)
                thing.Destroy(DestroyMode.Vanish);
        }

        //退回购物车指定品种，职责是按货柜剩余容量分配归还数量。
        private static void ReturnSingleDefToStorages(List<Building_SimContainer> storages, ThingDef def, int count)
        {
            if (storages.NullOrEmpty() || def == null || count <= 0) return;

            // 优先回到本来就有该商品配置的货柜；没有则回到第一个货柜。
            Building_SimContainer preferred = storages.FirstOrDefault(s => s != null && !s.Destroyed && s.ActiveDefs.Contains(def))
                                            ?? storages.FirstOrDefault(s => s != null && !s.Destroyed);
            if (preferred == null) return;

            Map map = preferred.Map;
            int remaining = count;
            int stackLimit = def.stackLimit > 0 ? def.stackLimit : count;

            while (remaining > 0)
            {
                int chunk = System.Math.Min(remaining, stackLimit);
                Thing thing = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
                thing.stackCount = chunk;
                int placed = TryReturnThingToStorages(storages, preferred, thing);
                remaining -= System.Math.Max(0, placed);
                if (placed <= 0) break;
            }

            // 所有货柜都满了时，把剩余商品落地，避免物品被吞。
            while (remaining > 0 && map != null)
            {
                int chunk = System.Math.Min(remaining, stackLimit);
                Thing thing = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
                thing.stackCount = chunk;
                GenPlace.TryPlaceThing(thing, preferred.Position, map, ThingPlaceMode.Near, out _);
                remaining -= chunk;
            }
        }

        //归还一件购物车实物，职责是优先回到原柜并保留物品数据。
        private static int TryReturnThingToStorages(List<Building_SimContainer> storages, Building_SimContainer preferred, Thing thing)
        {
            if (thing == null || thing.Destroyed || thing.stackCount <= 0) return 0;
            if (storages.NullOrEmpty()) return 0;
            int initial = thing.stackCount;

            List<Building_SimContainer> ordered = storages
                .Where(s => s != null && !s.Destroyed)
                .OrderByDescending(s => s == preferred)
                .ThenByDescending(s => preferred != null && s.ActiveDefs.Contains(thing.def))
                .ToList();

            for (int i = 0; i < ordered.Count; i++)
            {
                if (thing == null || thing.Destroyed || thing.stackCount <= 0) return initial;
                ordered[i].TryReceiveReturnedThing(thing);
            }

            if (thing != null && !thing.Destroyed && thing.stackCount > 0)
            {
                Map map = preferred?.Map ?? ordered.FirstOrDefault()?.Map;
                if (map != null)
                {
                    IntVec3 dropCell = preferred?.Position ?? ordered.First().Position;
                    GenPlace.TryPlaceThing(thing, dropCell, map, ThingPlaceMode.Near, out _);
                }
                else
                {
                    thing.Destroy(DestroyMode.Vanish);
                }
            }

            return initial;
        }
    }
}
