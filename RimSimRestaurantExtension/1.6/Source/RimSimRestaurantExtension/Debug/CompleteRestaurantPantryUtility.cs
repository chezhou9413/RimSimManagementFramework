using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.Inventory;
using RimWorld;
using SimManagementLib.SimThingComp;
using SimManagementLib.SimZone;
using Verse;

namespace RimSimRestaurantExtension.Debug
{
    //配置样板餐厅后厨和库存，职责是将真实食材保留在店内储存架并上架酒水。
    internal static class CompleteRestaurantPantryUtility
    {
        //将后厨食材格布置成店内储存架并配置补货目标，职责是保留整店区域和库存归属。
        public static bool TryConfigure(Map map, Zone_Shop shop, IReadOnlyList<IntVec3> cells,
            Dictionary<ThingDef, int> stock, out string failReason)
        {
            failReason = "";
            var cabinets = RestaurantStockUtility.Cabinets(shop).ToList();
            var wine = cabinets.FirstOrDefault(c => c.def.defName == "RSR_WallWineCabinet");
            if (wine == null)
            {
                failReason = "样板餐厅店内缺少设施：RSR_WallWineCabinet";
                return false;
            }
            if (cells.Count == 0)
            {
                failReason = "样板餐厅没有可放置后厨储存架的空地";
                return false;
            }

            var shelfDef = ThingDefOf.ShelfSmall;
            foreach (IntVec3 cell in cells)
            {
                var shelf = (Building_Storage)CompleteRestaurantCreationUtility.SpawnBuilding(map, shelfDef, cell, Rot4.North);
                shelf.GetStoreSettings().filter.SetDisallowAll();
                foreach (var item in stock.Keys) shelf.GetStoreSettings().filter.SetAllow(item, true);
                shelf.GetStoreSettings().filter.SetAllow(DefDatabase<ThingDef>.GetNamed("Beer"), true);
            }
            foreach (var pair in stock)
            {
                var source = RestaurantKitchenStorage.Items(shop).FirstOrDefault(t => t.def == pair.Key);
                if (source == null)
                {
                    failReason = "后厨货源缺少样板食材：" + pair.Key.defName;
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
