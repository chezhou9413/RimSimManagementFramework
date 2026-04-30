using SimManagementLib.GameComp;
using SimManagementLib.Pojo;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimThingComp;
using SimManagementLib.SimZone;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace SimManagementLib.Tool
{
    public static class ShopDataUtility
    {
        /// <summary>
        /// 获取指定区域内所有的货柜（自动去重）
        /// </summary>
        public static HashSet<Building_SimContainer> GetStoragesInZone(Zone zone)
        {
            var storages = new HashSet<Building_SimContainer>();
            if (zone == null || zone.Map == null) return storages;

            foreach (IntVec3 cell in zone.Cells)
            {
                List<Thing> things = zone.Map.thingGrid.ThingsListAt(cell);
                foreach (Thing t in things)
                {
                    if (t is Building_SimContainer storage)
                    {
                        storages.Add(storage);
                    }
                }
            }
            return storages;
        }

        /// <summary>
        /// 获取该区域内所有可以出售的商品（即启用的商品，并汇总库存）
        /// </summary>
        public static List<ShopItemStatus> GetAllSellableGoods(Zone zone)
        {
            var aggregatedData = new Dictionary<ThingDef, ShopItemStatus>();
            var storages = GetStoragesInZone(zone);

            foreach (Building_SimContainer storage in storages)
            {
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

        /// <summary>
        /// 获取这个区域里面目前有货的商品数据
        /// </summary>
        public static List<ShopItemStatus> GetInStockGoods(Zone zone)
        {
            return GetAllSellableGoods(zone).Where(item => item.CurrentStock > 0).ToList();
        }

        /// <summary>
        /// 获取这个区域里面缺货的商品数据
        /// </summary>
        public static List<ShopItemStatus> GetOutOfStockGoods(Zone zone)
        {
            return GetAllSellableGoods(zone).Where(item => item.CurrentStock <= 0).ToList();
        }

        /// <summary>
        /// 根据目标商店坐标查找商店区域。
        /// </summary>
        public static Zone_Shop FindShopZone(Map map, IntVec3 targetShopCell)
        {
            if (map == null) return null;

            return map.zoneManager.AllZones
                .OfType<Zone_Shop>()
                .FirstOrDefault(z => z.Cells.Contains(targetShopCell));
        }

        /// <summary>
        /// 严格按“商店ID + 目标坐标”查找顾客所属商店，不会回退到其他商店。
        /// </summary>
        public static Zone_Shop FindAssignedShopZone(Map map, int targetShopZoneId, IntVec3 targetShopCell)
        {
            if (map == null) return null;

            if (targetShopZoneId >= 0)
            {
                Zone_Shop byId = map.zoneManager.AllZones
                    .OfType<Zone_Shop>()
                    .FirstOrDefault(z => z.ID == targetShopZoneId);
                if (byId != null) return byId;
            }

            return FindShopZone(map, targetShopCell);
        }

        /// <summary>
        /// 获取指定预算内且当前库存可满足的套餐，按价格从高到低排序。
        /// </summary>
        public static List<ComboData> GetAffordableInStockCombos(Zone zone, float budget)
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
                .Where(c => HasEnoughStockForCombo(zone, c))
                .OrderByDescending(GetComboEffectivePrice)
                .ToList();
        }

        /// <summary>
        /// 按套餐配置从整个商店区域扣减库存，成功返回 true 并输出应付金额。
        /// </summary>
        public static bool TryPurchaseCombo(Zone zone, ComboData combo, out float paidPrice)
        {
            paidPrice = 0f;
            if (zone == null || zone.Map == null || combo == null || combo.items.NullOrEmpty())
                return false;

            List<Building_SimContainer> storages = GetStoragesInZone(zone).ToList();
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

        private static bool HasEnoughStockForCombo(Zone zone, ComboData combo)
        {
            List<Building_SimContainer> storages = GetStoragesInZone(zone).ToList();
            if (storages.NullOrEmpty()) return false;

            foreach (ComboItem item in combo.items)
            {
                if (item == null || item.def == null || item.count <= 0) return false;
                int totalStock = storages.Sum(s => s.CountStored(item.def));
                if (totalStock < item.count) return false;
            }

            return true;
        }

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

            public ComboExtractEntry(Building_SimContainer storage, Thing thing)
            {
                Storage = storage;
                Thing = thing;
            }
        }

        /// <summary>
        /// 将顾客购物车中的虚拟商品退回到商店货柜（用于结账超时/放弃结账）。
        /// </summary>
        public static void ReturnCartItemsToShop(Zone zone, List<CustomerCartItem> items)
        {
            if (zone == null || zone.Map == null || items.NullOrEmpty()) return;

            List<Building_SimContainer> storages = GetStoragesInZone(zone).ToList();
            if (storages.NullOrEmpty()) return;

            for (int i = 0; i < items.Count; i++)
            {
                CustomerCartItem cartItem = items[i];
                if (cartItem == null || cartItem.def == null || cartItem.count <= 0) continue;
                ReturnSingleDefToStorages(storages, cartItem.def, cartItem.count);
            }
        }

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
