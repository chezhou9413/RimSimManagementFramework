using RimWorld;
using RimSimRestaurantExtension.Models;
using SimManagementLib.SimZone;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace RimSimRestaurantExtension.Tool
{
    //提供餐厅员工值班位置选择，负责把厨师和服务员留在当前店铺的合理工作区域。
    public static class RestaurantStandbyUtility
    {
        //查找指定岗位的值班位置，服务员优先靠近桌椅，厨师优先靠近炉灶或点餐台。
        public static bool TryFindStandbyCell(Pawn pawn, Zone_Shop shop, bool waiter, out IntVec3 cell, out Thing focus)
        {
            cell = IntVec3.Invalid;
            focus = null;
            if (pawn?.Map == null || shop?.Cells == null) return false;

            focus = waiter ? FindWaiterFocus(pawn.Map, shop) : FindChefFocus(pawn.Map, shop);
            if (focus != null && TryFindReachableCellNear(pawn, shop, focus.Position, out cell))
                return true;

            IntVec3 center = shop.Cells.Count > 0 ? shop.Cells.OrderBy(c => c.DistanceToSquared(pawn.Position)).First() : pawn.Position;
            return TryFindReachableCellNear(pawn, shop, center, out cell);
        }

        //判断指定店铺是否还有活跃餐厅订单。
        public static bool HasActiveRestaurantOrders(Zone_Shop shop)
        {
            return shop != null && RestaurantOrderUtility.OrderManager?.GetActiveOrders(shop.ID).Any(order => order != null
                && order.state != RestaurantOrderState.Completed
                && order.state != RestaurantOrderState.Canceled
                && order.state != RestaurantOrderState.Failed) == true;
        }

        //查找厨师值班关注对象，优先使用店内灶台，其次使用点餐台。
        private static Thing FindChefFocus(Map map, Zone_Shop shop)
        {
            ThingDef stoveDef = DefDatabase<ThingDef>.GetNamedSilentFail("ElectricStove");
            Thing stove = FindThingInShop(map, shop, thing => thing.def == stoveDef || thing is IBillGiver);
            if (stove != null) return stove;
            return FindThingInShop(map, shop, thing => thing.def == DefOfRefs.RSR_RestaurantOrderCounter);
        }

        //查找服务员值班关注对象，优先使用店内椅子，其次使用点餐台。
        private static Thing FindWaiterFocus(Map map, Zone_Shop shop)
        {
            Thing seat = FindThingInShop(map, shop, thing => thing.def?.building?.isSittable == true);
            if (seat != null) return seat;
            return FindThingInShop(map, shop, thing => thing.def == DefOfRefs.RSR_RestaurantOrderCounter);
        }

        //在店铺区域内查找满足条件的物品。
        private static Thing FindThingInShop(Map map, Zone_Shop shop, System.Func<Thing, bool> predicate)
        {
            if (map == null || shop?.Cells == null || predicate == null) return null;
            for (int i = 0; i < shop.Cells.Count; i++)
            {
                List<Thing> things = map.thingGrid.ThingsListAt(shop.Cells[i]);
                for (int j = 0; j < things.Count; j++)
                {
                    Thing thing = things[j];
                    if (thing != null && !thing.Destroyed && predicate(thing))
                        return thing;
                }
            }
            return null;
        }

        //在目标附近查找可站立且可到达的店内格。
        private static bool TryFindReachableCellNear(Pawn pawn, Zone_Shop shop, IntVec3 near, out IntVec3 result)
        {
            result = IntVec3.Invalid;
            if (pawn?.Map == null || shop?.Cells == null) return false;

            List<IntVec3> candidates = shop.Cells
                .Where(cell => cell.Standable(pawn.Map)
                    && !cell.IsForbidden(pawn)
                    && pawn.CanReach(cell, PathEndMode.OnCell, Danger.Some)
                    && pawn.CanReserve(cell))
                .OrderBy(cell => cell.DistanceToSquared(near))
                .Take(16)
                .ToList();

            if (candidates.Count <= 0) return false;
            result = candidates[0];
            return true;
        }
    }
}
