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
            IReadOnlyList<IShopStorage> storages = GetRetailStorages(zone);

            foreach (IShopStorage storage in storages)
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

                if (storage is ThingComp_StorageSales physical)
                {
                    var listed = physical.ListedStock().GroupBy(stack => stack.Thing.def)
                        .ToDictionary(group => group.Key, group => group.Sum(stack => stack.Count));
                    foreach (var def in physical.ActiveDefs)
                    {
                        int count = listed.GetValueOrDefault(def);
                        if (aggregatedData.TryGetValue(def, out var status)) status.CurrentStock += count;
                        else aggregatedData[def] = new ShopItemStatus { Def = def, CurrentStock = count,
                            Config = physical.Rule(def)?.Copy() ?? new GoodsItemData { enabled = true, count = 0 } };
                    }
                    continue;
                }
                var comp = storage.Host.GetComp<ThingComp_GoodsData>();
                if (comp == null || string.IsNullOrEmpty(comp.ActiveGoodsDefName)) continue;

                foreach (ThingDef td in storage.ActiveDefs)
                {
                    GoodsItemData config = comp.FindItemData(td);
                    if (config == null || !config.enabled) continue;

                    int stock = storage.SaleCount(td);

                    if (aggregatedData.TryGetValue(td, out ShopItemStatus existingStatus))
                    {
                        existingStatus.CurrentStock += stock;
                    }
                    else
                    {
                        aggregatedData[td] = new ShopItemStatus
                        {
                            Def = td,
                            Config = config.Copy(),
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
                .Where(c => GetComboEffectivePrice(zone, c) <= budget)
                .Where(c => !CustomerPriceUtility.EvaluateCombo(GetComboEffectivePrice(zone, c), GetComboReferenceValue(c), sensitivity).rejected)
                .Where(c => HasEnoughStockForCombo(zone, c))
                .OrderByDescending(c => GetComboEffectivePrice(zone, c))
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

                float price = GetComboEffectivePrice(zone, combo);
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
            if (!TryPurchaseCombo(zone, combo, out paidPrice, out var items)) return false;
            //此旧入口代表直接消耗；顾客结账使用带实物购物车的重载。
            foreach (var item in items) item.TakeExactThing().Destroy(DestroyMode.Vanish);
            return true;
        }

        //检查套餐实际可取数量。
        private static bool HasEnoughStockForCombo(Zone zone, ComboData combo) => TryPlanCombo(zone, combo, null);

        //计算套餐成交价，职责是统一配置价格与商品参考价。
        public static float GetComboEffectivePrice(Zone zone, ComboData combo)
        {
            if (combo == null) return 0f;
            if (combo.totalPrice > 0f) return ShopPricingUtility.ApplyCashierPrice(zone as Zone_Shop, combo.totalPrice);

            float estimated = 0f;
            if (!combo.items.NullOrEmpty())
            {
                foreach (ComboItem item in combo.items)
                {
                    if (item == null || item.def == null || item.count <= 0) continue;
                    estimated += item.def.BaseMarketValue * item.count;
                }
            }

            return ShopPricingUtility.ApplyCashierPrice(zone as Zone_Shop, estimated > 0f ? estimated : 1f);
        }

        //返回套餐按商品市价计算的参考价值。
        public static float GetComboReferenceValue(ComboData combo)
        {
            return CustomerPriceUtility.GetComboReferenceValue(combo);
        }

        //取消结账时优先按每一堆实物的原始位置退货。
        public static void ReturnCartItemsToShop(Zone zone, List<CustomerCartItem> items)
        {
            if (items.NullOrEmpty()) return;
            foreach (var held in items)
            {
                if (held == null || !held.sourceCell.IsValid || held.count <= 0 || held.deliveredThingId >= 0) continue;
                held.count = 0;
                var thing = held.TakeExactThing();
                if (thing != null) ReturnPhysicalThing(held, thing, zone?.Map);
                else Log.Error($"[RSMF] 退货条目缺少实物：{held.def}，来源 {held.sourceCell}。");
            }
            if (zone?.Map == null) return;

            List<Building_SimContainer> storages = GetStorageSnapshotInZone(zone)
                .Where(s => s != null && s.Spawned && !s.Destroyed && s.Map == zone.Map
                    && s.GetDirectlyHeldThings() != null && s.AllowsCustomerSelfPurchase).ToList();
            HashSet<Building_SimContainer> failedStorages = new HashSet<Building_SimContainer>();

            for (int i = 0; i < items.Count; i++)
            {
                CustomerCartItem cartItem = items[i];
                if (cartItem == null || cartItem.def == null || cartItem.count <= 0 || cartItem.deliveredThingId >= 0) continue;
                if (cartItem.HasExactThing)
                {
                    //先认领本次归还，容器回调重入时不能再生成同一件商品。
                    cartItem.count = 0;
                    ReturnExactCartItem(zone, storages, cartItem);
                    continue;
                }
                if (cartItem.sourceSlotIndex >= 0)
                {
                    Log.Error($"[RSMF] 退货条目缺少托管实物：{cartItem.def.defName}，来源货柜 {cartItem.sourceContainerId}，槽位 {cartItem.sourceSlotIndex}；已停止重复归还。");
                    cartItem.count = 0;
                    continue;
                }
                ReturnSingleDefToStorages(zone, storages, cartItem, failedStorages);
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
                    .FirstOrDefault(s => s.Spawned && !s.Destroyed && s.GetDirectlyHeldThings() != null
                        && s.thingIDNumber == item.sourceContainerId);
            }
            try
            {
                if (source != null && source.TryRestoreCustomerThing(item.sourceSlotIndex, thing)) return;
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RSMF] 专业商品退回失败：货柜 {item.sourceContainerId}，物品 {thing.thingIDNumber}。{ex}");
                //加入容器后的通知也可能抛错，此时实物已经归还，不能再次落地或清空槽位。
                if (thing.holdingOwner != null) return;
            }
            try
            {
                source?.FinalizeCustomerSale(item.sourceSlotIndex, -1);
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RSMF] 退货释放槽位失败：货柜 {item.sourceContainerId}，槽位 {item.sourceSlotIndex}。{ex}");
            }

            IntVec3 dropCell = source?.Position ?? storages.FirstOrDefault()?.Position ?? zone.Cells.FirstOrDefault();
            PlaceLooseGoods(thing, zone.Map, dropCell);
        }

        //退回购物车指定品种，职责是按货柜剩余容量分配归还数量。
        private static void ReturnSingleDefToStorages(Zone zone, List<Building_SimContainer> storages,
            CustomerCartItem item, HashSet<Building_SimContainer> failedStorages)
        {
            ThingDef def = item.def;
            Building_SimContainer preferred = storages.FirstOrDefault(s => s.thingIDNumber == item.sourceContainerId)
                ?? storages.FirstOrDefault();
            int stackLimit = System.Math.Max(1, def.stackLimit);
            while (item.count > 0)
            {
                int chunk = System.Math.Min(item.count, stackLimit);
                Thing thing = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
                thing.stackCount = chunk;
                //数量条目每次只实体化一栈，后续通知失败也不能重新生成已归还的数量。
                item.count -= chunk;
                TryReturnThingToStorages(storages, preferred, thing, failedStorages);
                IntVec3 dropCell = preferred?.Position ?? zone.Cells.FirstOrDefault();
                PlaceLooseGoods(thing, zone.Map, dropCell);
            }
        }

        //归还一件购物车实物，职责是优先回到原柜并保留物品数据。
        private static int TryReturnThingToStorages(List<Building_SimContainer> storages, Building_SimContainer preferred,
            Thing thing, HashSet<Building_SimContainer> failedStorages = null)
        {
            if (thing == null || thing.Destroyed || thing.stackCount <= 0) return 0;
            if (storages.NullOrEmpty()) return 0;
            int initial = thing.stackCount;

            List<Building_SimContainer> ordered = storages
                .Where(s => s != null && s.Spawned && !s.Destroyed && s.GetDirectlyHeldThings() != null)
                .OrderByDescending(s => s == preferred)
                .ToList();

            for (int i = 0; i < ordered.Count; i++)
            {
                if (thing == null || thing.Destroyed || thing.stackCount <= 0) return initial;
                Building_SimContainer storage = ordered[i];
                if (failedStorages?.Contains(storage) == true) continue;
                int remaining = thing.stackCount;
                try
                {
                    if (storage.TryReceiveReturnedThing(thing) >= remaining) return initial;
                }
                catch (System.Exception ex)
                {
                    failedStorages?.Add(storage);
                    Log.Error($"[RSMF] 货柜 {storage.thingIDNumber} 接收退货 {thing.thingIDNumber} 失败，本次退货跳过该柜。{ex}");
                    if (thing.Destroyed || thing.holdingOwner == storage.GetDirectlyHeldThings()) return initial;
                }
            }

            return initial - thing.stackCount;
        }

        //在原址附近放置剩余商品，周围已满时仍保留同一堆实物。
        internal static void PlaceLooseGoods(Thing thing, Map map, IntVec3 dropCell)
        {
            if (thing.Destroyed || thing.Spawned || thing.holdingOwner != null) return;
            if (map == null || !dropCell.InBounds(map))
            {
                Log.Error($"[RSMF] 商品缺少落地位置：{thing.def.defName} × {thing.stackCount}，位置 {dropCell}。");
                return;
            }
            if (GenPlace.TryPlaceThing(thing, dropCell, map, ThingPlaceMode.Near,
                extraValidator: cell => !map.reservationManager.IsReserved(cell))) return;

            //原版近距离放置已用尽周围空间，选择最近的空地，避免强行生成时挤走其他商品。
            var ground = map.AllCells.Where(c => c.Standable(map) && c.GetEdifice(map) == null && c.GetSlotGroup(map) == null)
                .OrderBy(c => c.GetItemCount(map) >= c.GetMaxItemsAllowedInCell(map))
                .ThenBy(c => c.DistanceToSquared(dropCell)).FirstOrDefault(dropCell);
            thing.Position = ground;
            thing.SpawnSetup(map, false);
        }
    }
}
