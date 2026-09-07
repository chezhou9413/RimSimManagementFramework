using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.Buildings;
using RimSimRestaurantExtension.Models;
using RimSimRestaurantExtension.Tool;
using RimWorld;
using SimManagementLib.SimMapComp;
using SimManagementLib.SimZone;
using Verse;
using Verse.AI;
namespace RimSimRestaurantExtension.Inventory
{
    //解析厨房真实货源，职责是统一冰箱优先级、后厨绑定和跨店预留查询。
    public static class RestaurantStockUtility
    {
        //取得同地图仍然存在的绑定原版储存区。
        public static IEnumerable<Zone_Stockpile> Pantries(Zone_Shop shop)
        {
            if (shop?.Map == null) return Enumerable.Empty<Zone_Stockpile>();
            var ids = RestaurantOrderUtility.Settings.GetOrCreate(shop.ID).pantryZoneIds;
            return shop.Map.zoneManager.AllZones.OfType<Zone_Stockpile>().Where(z => ids.Contains(z.ID));
        }

        //取得本店餐厅货柜，职责是保留不同柜体的来源身份。
        public static IEnumerable<Building_RestaurantStorage> Cabinets(Zone_Shop shop)
        {
            return SimManagementLib.Tool.ShopDataUtility.GetStoragesInZone(shop).OfType<Building_RestaurantStorage>();
        }

        //返回业务预留标识，职责是让不同地图和不同子订单互不串用。
        public static string Key(RestaurantOrder order) => "Restaurant/" + order.orderId;

        //检查实际取货路径，职责是支持容器前方和地面物品两种交互方式。
        public static bool Reachable(Pawn actor, Thing item, string ownKey = null)
        {
            if (item == null || item.Destroyed || item.IsBurning()
                || item.TryGetComp<CompRottable>()?.Stage > RotStage.Fresh) return false;
            bool protectedStock = item.MapHeld?.GetComponent<MapComponent_InventoryReservations>().IsProtected(item) == true;
            if (actor == null) return protectedStock || !item.IsForbidden(Faction.OfPlayer);
            if (item.ParentHolder is Building_RestaurantStorage cabinet)
                return cabinet.Spawned && actor.CanReach(cabinet.InventoryInteractionTarget, cabinet.InventoryInteractionEndMode, Danger.Some);
            return item.Spawned && (ownKey != null || protectedStock || !item.IsForbidden(actor))
                && actor.CanReserveAndReach(item, PathEndMode.Touch, Danger.Some);
        }

        //按冰箱、后厨及距离排序实际物资，职责是不把商店全域地面当厨房库存。
        public static IEnumerable<Thing> Sources(Zone_Shop shop, ThingDef item, Pawn actor = null)
        {
            if (shop?.Map == null || item == null) return Enumerable.Empty<Thing>();
            IntVec3 origin = actor?.Position ?? shop.Cells.First();
            var fridges = Cabinets(shop).Where(c => c.IsRefrigerator && c.AllowsInventoryItem(item))
                .OrderBy(c => c.Position.DistanceToSquared(origin))
                .SelectMany(c => c.GetDirectlyHeldThings().Cast<Thing>().Where(t => t.def == item));
            var zones = Pantries(shop).ToList();
            var ground = shop.Map.listerThings.ThingsOfDef(item)
                .Where(t => t.Spawned && zones.Any(z => z.ContainsCell(t.Position)))
                .OrderBy(t => t.Position.DistanceToSquared(origin));
            var ledger = shop.Map.GetComponent<MapComponent_InventoryReservations>();
            return fridges.Concat(ground).Where(t => Reachable(actor, t) && ledger.Available(t) > 0);
        }

        //为整单生成具体物资清单，职责是同时供菜单判断和接单预留使用。
        public static bool Select(Zone_Shop shop, RestaurantOrder order, Pawn actor, out List<ThingCount> result)
        {
            result = new List<ThingCount>();
            var ledger = shop.Map.GetComponent<MapComponent_InventoryReservations>();
            foreach (var need in order.GetTotalIngredientNeeds())
            {
                int left = need.countPerMeal;
                foreach (Thing thing in Sources(shop, need.ThingDef, actor))
                {
                    int count = System.Math.Min(left, ledger.Available(thing));
                    if (count <= 0) continue;
                    result.Add(new ThingCount(thing, count));
                    left -= count;
                    if (left == 0) break;
                }
                if (left > 0) return false;
            }
            return result.Count > 0;
        }
    }
}
