using RimWorld;
using RimSimRestaurantExtension.Models;
using SimManagementLib.SimZone;
using SimManagementLib.SimAI;
using SimManagementLib.SimThingClass;
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
            HashSet<IntVec3> workCells = CollectWorkCells(pawn, shop);

            //续班优先保留已预约的位置，避免同岗员工反复交换站位。
            if (pawn.CurJobDef == DefOfRefs.RSR_RestaurantStandby
                && pawn.CurJob.targetA.Cell == pawn.Position && shop.ContainsCell(pawn.Position)
                && !workCells.Contains(pawn.Position) && CanStandAt(pawn, pawn.Position))
            {
                result = pawn.Position;
                return true;
            }
            //先按距离排序，再逐格验证路径，避免每次派工对全店格子执行寻路检查。
            foreach (var cell in shop.Cells.OrderBy(c => c.DistanceToSquared(near)))
            {
                if (workCells.Contains(cell) || !CanStandAt(pawn, cell)) continue;
                result = cell;
                return true;
            }
            return false;
        }

        //续班时重查工作格，及时让开新建设施及已经登记的结账队列。
        public static bool CanContinueAt(Pawn pawn, Zone_Shop shop, IntVec3 cell)
        {
            return shop.ContainsCell(cell) && !CollectWorkCells(pawn, shop).Contains(cell)
                && CanStandAt(pawn, cell);
        }

        //收集建筑操作格和收银队列，待机员工不能预约这些业务必需位置。
        private static HashSet<IntVec3> CollectWorkCells(Pawn pawn, Zone_Shop shop)
        {
            var cells = new HashSet<IntVec3>();
            foreach (var building in shop.Cells.SelectMany(c => c.GetThingList(shop.Map)).OfType<Building>().Distinct())
            {
                if (building.def.hasInteractionCell) cells.Add(building.InteractionCell);
                if (building is Building_CashRegister register)
                    cells.Add(CheckoutQueueCellUtility.FindServiceCell(register, pawn));
            }
            foreach (var customer in shop.Map.mapPawns.AllPawnsSpawned)
            {
                Job current = customer.CurJob;
                if (current?.def.defName != "Customer_PayAtRegister"
                    || !(current.targetA.Thing is Building_CashRegister register)
                    || !shop.ContainsCell(register.Position)) continue;
                cells.Add(current.targetB.Cell);
                cells.Add(current.targetC.Cell);
            }
            return cells;
        }

        //核对站位可用性，职责是避让顾客座椅、储存架、建筑主体及其他员工预约。
        private static bool CanStandAt(Pawn pawn, IntVec3 cell)
        {
            return cell.Standable(pawn.Map) && cell.GetEdifice(pawn.Map) == null
                && !cell.GetThingList(pawn.Map).Any(t => t is Pawn other && other != pawn)
                && !cell.IsForbidden(pawn) && pawn.CanReserve(cell)
                && pawn.CanReach(cell, PathEndMode.OnCell, Danger.Some);
        }
    }
}
