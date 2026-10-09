using System.Collections.Generic;
using RimWorld;
using SimManagementLib.SimThingClass;
using Verse;

namespace RimSimRestaurantExtension.Inventory
{
    //识别殖民地仓储，为冰箱补货和自动配料提供相同的库存范围。
    public static class RestaurantColonyStorage
    {
        //只接受原版储存区或玩家储存建筑中的物品，排除框架商品柜。
        public static bool Contains(Map map, Thing thing)
        {
            if (thing?.Spawned != true || thing.Map != map) return false;
            var parent = thing.GetSlotGroup()?.parent;
            if (!(parent is Zone_Stockpile) && !(parent is Building_Storage shelf && shelf.Faction == Faction.OfPlayer))
                return false;
            return !(parent is Building_SimContainer);
        }

        //枚举实际仓储中的物品，供一键配料统计殖民地库存。
        public static IEnumerable<Thing> Items(Map map)
        {
            foreach (var group in map.haulDestinationManager.AllGroupsListForReading)
                foreach (Thing thing in group.HeldThings)
                    if (Contains(map, thing)) yield return thing;
        }
    }
}
