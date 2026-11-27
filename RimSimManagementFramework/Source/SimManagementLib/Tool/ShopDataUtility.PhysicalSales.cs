using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.SimMapComp;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimThingComp;
using Verse;

namespace SimManagementLib.Tool
{
    //购物车直接持有货柜取出的原物，每次拆栈独立保留其来源和状态。
    public static partial class ShopDataUtility
    {
        //列出指定品种可取的实物，专业柜继续按槽销售。
        private static IEnumerable<ThingCount> SaleStacks(IShopStorage storage, ThingDef def)
        {
            if (!storage.AllowsCustomerSelfPurchase) return Enumerable.Empty<ThingCount>();
            if (storage is ThingComp_StorageSales sales) return sales.ListedStock(def);
            if (storage is not Building_SimContainer native || native is Building_UniqueGoodsContainer
                || native.GetComp<ThingComp_GoodsData>()?.FindItemData(def)?.enabled != true)
                return Enumerable.Empty<ThingCount>();
            var ledger = native.Map.GetComponent<MapComponent_InventoryReservations>();
            return native.GetDirectlyHeldThings().Where(t => t.def == def)
                .Select(t => new ThingCount(t, ledger.Available(t))).Where(stack => stack.Count > 0);
        }

        //从当前仍属该柜且未被预留的堆叠提取指定数量。
        internal static CustomerCartItem TakeCartStack(IShopStorage storage, Thing thing, int count, float price)
        {
            if (count <= 0 || !storage.AllowsCustomerSelfPurchase || thing.Destroyed) return null;
            var host = storage.Host;
            var ledger = host.Map.GetComponent<MapComponent_InventoryReservations>();
            if (storage is ThingComp_StorageSales sales)
            {
                if (sales.Available(thing) < count) return null;
            }
            else if (storage is not Building_SimContainer native || !native.GetDirectlyHeldThings().Contains(thing)
                || ledger.Available(thing) < count) return null;
            var item = new CustomerCartItem
            {
                sourceContainerId = host.thingIDNumber, sourceBuilding = host, sourceMap = host.Map,
                sourceCell = thing.Spawned ? thing.Position : host.Position, marketValue = thing.MarketValue, unitPrice = price
            };
            bool releaseProtection = ledger.IsProtected(thing);
            Thing part = thing.SplitOff(count);
            if (!item.StoreExactThing(part))
            {
                ReturnPhysicalThing(item, part);
                return null;
            }
            if (releaseProtection) part.SetForbidden(false, false);
            if (storage is ThingComp_StorageSales physical) physical.NotifyStockChanged(thing.def);
            else ((Building_SimContainer)storage).NotifyInventoryChanged();
            return item;
        }

        //同一品种可跨堆提取，每段实物独立保存在购物车内。
        internal static int TakeSaleStacks(IShopStorage storage, ThingDef def, int count, float price, List<CustomerCartItem> items)
        {
            int remaining = count;
            foreach (var stack in SaleStacks(storage, def).ToList())
            {
                int amount = Math.Min(remaining, stack.Count);
                var held = TakeCartStack(storage, stack.Thing, amount, price);
                if (held == null) continue;
                items.Add(held);
                remaining -= held.count;
                if (remaining == 0) break;
            }
            return count - remaining;
        }

        //收集每个品种的实际需求，同一品种重复出现时合并校验。
        private static Dictionary<ThingDef, int> ComboDemand(ComboData combo)
        {
            var demand = new Dictionary<ThingDef, int>();
            if (combo?.items.NullOrEmpty() != false) return demand;
            foreach (var item in combo.items)
            {
                if (item?.def == null || item.count <= 0) return new();
                demand.TryGetValue(item.def, out int previous);
                if (item.count > int.MaxValue - previous) return new();
                demand[item.def] = previous + item.count;
            }
            return demand;
        }

        //按成交前的上架份额选齐套餐，取走第一件后腾出的名额留给下一次购买。
        private static bool TryPlanCombo(Zone zone, ComboData combo, List<(IShopStorage storage, ThingCount stack)> picks)
        {
            var demand = ComboDemand(combo);
            if (demand.Count == 0 || zone?.Map == null) return false;
            var storages = GetRetailStorages(zone);
            foreach (var entry in demand)
            {
                int remaining = entry.Value;
                foreach (var storage in storages)
                {
                    foreach (var stack in SaleStacks(storage, entry.Key))
                    {
                        int count = Math.Min(remaining, stack.Count);
                        picks?.Add((storage, stack.WithCount(count)));
                        remaining -= count;
                        if (remaining == 0) break;
                    }
                    if (remaining == 0) break;
                }
                if (remaining > 0) return false;
            }
            return true;
        }

        //逐堆复核并提取套餐，全部到齐后才向调用方交付整份购物车。
        internal static bool TryPurchaseCombo(Zone zone, ComboData combo, out float paidPrice, out List<CustomerCartItem> items)
        {
            items = new();
            paidPrice = 0f;
            if (zone?.Map == null) return false;
            float price = GetComboEffectivePrice(zone, combo);
            var picks = new List<(IShopStorage storage, ThingCount stack)>();
            if (price <= 0f || !TryPlanCombo(zone, combo, picks)) return false;
            try
            {
                foreach (var pick in picks)
                {
                    var held = TakeCartStack(pick.storage, pick.stack.Thing, pick.stack.Count, 0f);
                    if (held == null) return false;
                    items.Add(held);
                }
                paidPrice = price;
                return true;
            }
            finally
            {
                if (paidPrice == 0f)
                {
                    foreach (var held in items) ReturnPhysicalThing(held, held.TakeExactThing());
                    items.Clear();
                }
            }
        }

        //将实物退回原建筑，原柜拆除或容量变化时只放置未被接收的余量。
        internal static void ReturnPhysicalThing(CustomerCartItem item, Thing thing, Map fallbackMap = null)
        {
            if (thing == null || thing.Destroyed) return;
            Building host = item.sourceBuilding;
            Map map = item.sourceMap ?? host?.Map ?? fallbackMap;
            if (host?.Spawned == true && host.Map == map)
            {
                if (ShopStorage.For(host) is ThingComp_StorageSales sales) sales.ReturnToHost(thing);
                else if (host is Building_SimContainer native) native.TryReceiveReturnedThing(thing);
            }
            PlaceLooseGoods(thing, map, item.sourceCell);
        }
    }
}
