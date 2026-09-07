using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.Models;
using SimManagementLib.SimZone;
using Verse;
using Verse.AI;

namespace RimSimRestaurantExtension.Tool
{
    //提供餐厅桌椅查询与餐位占用检查，职责是只选择紧邻进食桌面的原版可坐位置。
    public static class RestaurantDiningSpotUtility
    {
        //查找顾客可达的桌椅餐位，职责是同时验证商店边界、订单占用、预约与相邻进食桌面。
        public static bool TryFindDiningSpot(Pawn customer, Zone_Shop shop, out IntVec3 seatCell, out Thing table, RestaurantOrder ignoredOrder = null)
        {
            seatCell = IntVec3.Invalid;
            table = null;
            if (customer?.Map == null || shop?.Map != customer.Map) return false;

            var candidates = shop.Cells.Where(cell => HasSittableBuilding(shop.Map, cell))
                .Distinct().OrderBy(cell => cell.DistanceToSquared(customer.Position));
            foreach (var cell in candidates)
            {
                if (!IsAvailableSeat(customer, shop, cell, ignoredOrder) || !IsUsableCell(customer, cell)) continue;
                Thing surface = FindAdjacentEatSurface(shop, cell);
                if (surface == null) continue;
                var preview = new RestaurantOrder { shopZoneId = shop.ID, customerThingId = customer.thingIDNumber,
                    seatCell = cell, tableThingId = surface.thingIDNumber };
                if (!RestaurantStaffAvailabilityUtility.HasWaiterForOrder(shop, preview)) continue;
                seatCell = cell;
                table = surface;
                break;
            }
            return table != null;
        }

        //按编号恢复订单桌面，职责是给配送和顾客朝向逻辑提供稳定引用。
        public static Thing FindTableById(Map map, int tableThingId)
        {
            return RestaurantOrderUtility.FindThingById(map, tableThingId);
        }

        //确保订单餐位仍可使用，职责是在建筑被拆除或座位失效时重新分配同店餐位。
        public static bool TryEnsureDiningSpot(Pawn customer, RestaurantOrder order)
        {
            if (customer?.Map == null || order == null) return false;
            Zone_Shop shop = RestaurantOrderUtility.FindShopById(customer.Map, order.shopZoneId);
            if (shop == null) return false;
            Thing table = FindTableById(customer.Map, order.tableThingId);
            if (order.seatCell.IsValid
                && IsCellInShop(shop, order.seatCell)
                && HasSittableBuilding(customer.Map, order.seatCell)
                && IsAvailableSeat(customer, shop, order.seatCell, order)
                && IsUsableCell(customer, order.seatCell)
                && IsDiningTableForSeat(shop, table, order.seatCell))
            {
                return true;
            }

            if (order.state != RestaurantOrderState.GoingToSeat) return false;
            if (!TryFindDiningSpot(customer, shop, out IntVec3 seatCell, out Thing newTable, order))
                return false;
            order.seatCell = seatCell;
            order.tableThingId = newTable.thingIDNumber;
            order.TouchProgress();
            return true;
        }

        //判断已分配餐位是否仍能使用，职责是在顾客等待期间发现桌椅拆除、区域变化或路径阻断。
        public static bool IsDiningSpotValid(Pawn customer, RestaurantOrder order)
        {
            if (customer?.Map == null || order == null || !order.seatCell.IsValid) return false;
            Zone_Shop shop = RestaurantOrderUtility.FindShopById(customer.Map, order.shopZoneId);
            Thing table = FindTableById(customer.Map, order.tableThingId);
            return shop != null
                && IsCellInShop(shop, order.seatCell)
                && order.seatCell.InBounds(customer.Map)
                && !order.seatCell.Impassable(customer.Map)
                && HasSittableBuilding(customer.Map, order.seatCell)
                && IsAvailableSeat(customer, shop, order.seatCell, order)
                && customer.CanReach(order.seatCell, PathEndMode.OnCell, Danger.Some)
                && IsDiningTableForSeat(shop, table, order.seatCell);
        }

        //判断餐位格是否可达且可预约，职责是拒绝墙体、危险路径和被其他 Job 占用的位置。
        public static bool IsUsableCell(Pawn pawn, IntVec3 cell)
        {
            return pawn?.Map != null
                && cell.IsValid
                && cell.InBounds(pawn.Map)
                && !cell.Impassable(pawn.Map)
                && pawn.CanReserveSittableOrSpot(cell)
                && pawn.CanReach(cell, PathEndMode.OnCell, Danger.Some);
        }

        //判断座位是否未被其他活跃餐厅订单占用，职责是阻止多个顾客共享同一餐位。
        public static bool IsAvailableSeat(Pawn customer, Zone_Shop shop, IntVec3 cell, RestaurantOrder ignoredOrder = null)
        {
            if (customer?.Map == null || shop == null || !cell.IsValid) return false;
            return RestaurantOrderUtility.OrderManager.Sessions.All(session => session.IsTerminal
                || session.shopId != shop.ID || session.seat != cell
                || session.customerId == customer.thingIDNumber);
        }

        //查找顾客身旁的交谈与交付格，职责是避开餐位、家具预约和商店边界外位置。
        public static bool TryFindDeliveryCell(Pawn waiter, RestaurantOrder order, out IntVec3 deliveryCell)
        {
            deliveryCell = IntVec3.Invalid;
            if (waiter?.Map == null || order == null) return false;
            IEnumerable<IntVec3> candidates = GenAdj.CellsAdjacent8Way(order.seatCell, Rot4.North, IntVec2.One);
            Zone_Shop shop = RestaurantOrderUtility.FindShopById(waiter.Map, order.shopZoneId);
            HashSet<IntVec3> shopCells = new HashSet<IntVec3>(shop?.Cells ?? Enumerable.Empty<IntVec3>());
            HashSet<IntVec3> occupiedSeats = new HashSet<IntVec3>(
                (RestaurantOrderUtility.OrderManager?.GetActiveOrders(order.shopZoneId) ?? new List<RestaurantOrder>())
                .Where(activeOrder => activeOrder != null && activeOrder.seatCell.IsValid)
                .Select(activeOrder => activeOrder.seatCell));
            List<IntVec3> reachable = candidates
                .Distinct()
                .Where(cell => cell.IsValid && cell.InBounds(waiter.Map) && cell.Standable(waiter.Map))
                .Where(cell => shopCells.Contains(cell) && !occupiedSeats.Contains(cell))
                .Where(cell => !HasSittableBuilding(waiter.Map, cell) && waiter.CanReserveSittableOrSpot(cell))
                .Where(cell => waiter.CanReach(cell, PathEndMode.OnCell, Danger.Some))
                .Where(cell => ReachabilityImmediate.CanReachImmediate(cell, order.seatCell, waiter.Map, PathEndMode.Touch, waiter))
                .OrderBy(cell => (cell - order.seatCell).LengthHorizontalSquared)
                .ToList();
            deliveryCell = reachable.Count > 0 ? reachable[0] : IntVec3.Invalid;
            return deliveryCell.IsValid;
        }

        //查找座位四周的进食桌面，职责是匹配原版进食对 SurfaceType.Eat 的判定。
        private static Building FindAdjacentEatSurface(Zone_Shop shop, IntVec3 seatCell)
        {
            if (shop?.Map == null || !seatCell.IsValid) return null;
            for (int i = 0; i < GenAdj.CardinalDirections.Length; i++)
            {
                IntVec3 cell = seatCell + GenAdj.CardinalDirections[i];
                if (!cell.InBounds(shop.Map) || !IsCellInShop(shop, cell)) continue;
                Building building = cell.GetEdifice(shop.Map);
                if (building != null && !building.Destroyed && building.Spawned && building.def?.surfaceType == SurfaceType.Eat)
                    return building;
            }
            return null;
        }

        //判断建筑是否为可坐家具，职责是复用原版 building.isSittable 语义。
        private static bool IsChair(Building building)
        {
            return building != null && !building.Destroyed && building.Spawned && building.def?.building?.isSittable == true;
        }

        //判断餐位上是否仍有可坐家具，职责是在椅子被拆除时拒绝继续把地面当作已分配餐位。
        private static bool HasSittableBuilding(Map map, IntVec3 seatCell)
        {
            if (map == null || !seatCell.IsValid || !seatCell.InBounds(map)) return false;
            List<Thing> things = seatCell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
                if (things[i] is Building building && IsChair(building)) return true;
            return false;
        }

        //判断桌面仍位于同店且与餐位正交相邻，职责是拒绝被移出区域、失去进食表面或只在对角线接触的桌子。
        private static bool IsDiningTableForSeat(Zone_Shop shop, Thing table, IntVec3 seatCell)
        {
            if (!(table is Building building) || shop?.Map == null || !seatCell.IsValid
                || building.Destroyed || !building.Spawned || building.Map != shop.Map
                || building.def?.surfaceType != SurfaceType.Eat)
            {
                return false;
            }
            CellRect occupied = building.OccupiedRect();
            for (int i = 0; i < GenAdj.CardinalDirections.Length; i++)
            {
                IntVec3 adjacent = seatCell + GenAdj.CardinalDirections[i];
                if (occupied.Contains(adjacent) && IsCellInShop(shop, adjacent)) return true;
            }
            return false;
        }

        //判断格子是否属于商店区域，职责是防止餐厅动作占用店外家具。
        private static bool IsCellInShop(Zone_Shop shop, IntVec3 cell)
        {
            if (shop == null || !cell.IsValid) return false;
            foreach (IntVec3 zoneCell in shop.Cells)
                if (zoneCell == cell) return true;
            return false;
        }
    }
}
