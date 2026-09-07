using System;
using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.GameComp;
using RimSimRestaurantExtension.Inventory;
using RimWorld;
using SimManagementLib.SimThingComp;
using SimManagementLib.SimZone;
using Verse;

namespace RimSimRestaurantExtension.Debug
{
    //配置样板餐厅后厨和库存，职责是从调试场景中的真实食材填充冰箱并上架酒水。
    internal static class CompleteRestaurantPantryUtility
    {
        //绑定已规划的后厨空地并配置补货目标，职责是保留柜体的商店归属并报告配置失败原因。
        public static bool TryConfigure(Map map, Zone_Shop shop, IReadOnlyList<IntVec3> cells,
            RestaurantShopSettings settings, Dictionary<ThingDef, int> stock, out string failReason)
        {
            failReason = "";
            var cabinets = RestaurantStockUtility.Cabinets(shop).ToList();
            var fridge = cabinets.FirstOrDefault(c => c.IsRefrigerator);
            var wine = cabinets.FirstOrDefault(c => c.def.defName == "RSR_WallWineCabinet");
            if (fridge == null || wine == null)
            {
                failReason = "样板餐厅店内缺少设施：" + (fridge == null ? "RSR_Refrigerator" : "RSR_WallWineCabinet");
                return false;
            }
            if (cells.Count == 0)
            {
                failReason = "样板餐厅没有可划为后厨货源的空地";
                return false;
            }

            var pantry = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager);
            pantry.label = "样板餐厅后厨货源";
            map.zoneManager.RegisterZone(pantry);
            foreach (IntVec3 cell in cells)
            {
                shop.RemoveCell(cell);
                pantry.AddCell(cell);
            }
            settings.pantryZoneIds.Add(pantry.ID);
            var targets = stock.ToDictionary(p => p.Key.defName, p => new GoodsItemData
            { enabled = true, count = Math.Min(p.Value, 500), restockThreshold = 50, price = 1 });
            fridge.Goods.ApplySettings(fridge.CatalogId, targets);
            foreach (var pair in stock)
            {
                var source = map.listerThings.ThingsOfDef(pair.Key)
                    .FirstOrDefault(t => t.Spawned && pantry.ContainsCell(t.Position));
                if (source == null)
                {
                    failReason = "后厨货源缺少样板食材：" + pair.Key.defName;
                    return false;
                }
                IntVec3 sourceCell = source.Position;
                int count = Math.Min(50, source.stackCount);
                Thing part = source.SplitOff(count);
                if (part.Spawned) part.DeSpawn();
                if (fridge.TryReceiveReturnedThing(part) != count)
                {
                    //入库未接收的实物归还原格，避免配置失败时留下无归属物品。
                    if (!part.Destroyed && part.holdingOwner == null)
                        GenSpawn.Spawn(part, sourceCell, map);
                    failReason = "样板冰箱无法接收食材：" + pair.Key.defName;
                    return false;
                }
            }

            var beer = DefDatabase<ThingDef>.GetNamed("Beer");
            wine.Goods.ApplySettings(wine.CatalogId, new Dictionary<string, GoodsItemData>
            { [beer.defName] = new GoodsItemData { enabled = true, count = 60, restockThreshold = 10, price = 15 } });
            wine.Rule(beer).onSale = true;
            if (wine.TryCreateAndStore(beer, 30) != 30)
            {
                failReason = "样板酒柜无法存入 30 份 Beer";
                return false;
            }
            Thing spareBeer = ThingMaker.MakeThing(beer);
            spareBeer.stackCount = 60;
            GenSpawn.Spawn(spareBeer, cells[cells.Count - 1], map);
            return true;
        }
    }
}
