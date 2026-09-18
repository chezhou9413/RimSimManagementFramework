using System.Collections.Generic;
using RimWorld;
using SimManagementLib.SimZone;
using Verse;

namespace RimSimRestaurantExtension.Inventory
{
    //解析商店区域内的储存架，职责是让取料、现货补餐和回收使用相同的实物边界。
    public static class RestaurantKitchenStorage
    {
        //枚举本店储存架格位，职责是按物品所在格划分跨越商店边界的架子。
        public static IEnumerable<IntVec3> Cells(Zone_Shop shop)
        {
            if (shop?.Map == null) yield break;
            foreach (var group in shop.Map.haulDestinationManager.AllGroupsListForReading)
            {
                if (!(group.parent is Building_Storage shelf) || shelf.Faction != Faction.OfPlayer) continue;
                foreach (var cell in group.CellsList)
                    if (shop.ContainsCell(cell)) yield return cell;
            }
        }

        //判断实物是否仍在本店储存架上，职责是排除桌面餐品、携带物品和邻店货源。
        public static bool Contains(Zone_Shop shop, Thing thing)
        {
            return shop?.Map != null && thing?.Spawned == true && thing.Map == shop.Map
                && shop.ContainsCell(thing.Position)
                && thing.GetSlotGroup()?.parent is Building_Storage shelf && shelf.Faction == Faction.OfPlayer;
        }

        //枚举本店储存架上的真实物品，职责是不扫描地图全部散落食物。
        public static IEnumerable<Thing> Items(Zone_Shop shop)
        {
            foreach (var cell in Cells(shop))
                foreach (var thing in cell.GetThingList(shop.Map))
                    if (thing.def.category == ThingCategory.Item) yield return thing;
        }

        //检查回收格是否接受当前食品，职责是遵循储存架过滤器并避免覆盖已有物品。
        public static bool Accepts(Zone_Shop shop, IntVec3 cell, Thing food)
        {
            return food != null && shop?.Map != null && shop.ContainsCell(cell)
                && cell.GetSlotGroup(shop.Map)?.Settings.AllowedToAccept(food) == true
                && cell.GetItemCount(shop.Map) == 0;
        }
    }
}
