using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.Buildings;
using RimSimRestaurantExtension.GameComp;
using RimSimRestaurantExtension.Models;
using SimManagementLib.Api;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimZone;
using Verse;
using Verse.AI;

namespace RimSimRestaurantExtension.Tool
{
    //汇总经营可执行条件，职责是供界面、入座和接单共享相同的设施、岗位与路径判断。
    public static class RestaurantBusinessAvailability
    {
        private static readonly Dictionary<Zone_Shop, (int tick, string issue)> snapshots =
            new Dictionary<Zone_Shop, (int, string)>();

        //清除跨游戏快照，职责是让新游戏和读档重新检查设施。
        public static void Reset()
        {
            snapshots.Clear();
        }

        //取得短周期经营提示，职责是避免界面每帧遍历库存与路径，并支持手动刷新立即重算。
        public static string Snapshot(Zone_Shop shop, bool refresh = false)
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            if (shop == null) return "商店不存在";
            if (!refresh && snapshots.TryGetValue(shop, out var cached) && now - cached.tick < 120) return cached.issue;
            string issue = CheckShop(shop, RestaurantOrderUtility.Settings?.GetOrCreate(shop.ID));
            snapshots[shop] = (now, issue);
            return issue;
        }

        //检查当前菜品的厨房和出餐路径，职责是筛选能搬运整单实物的厨师与服务员。
        public static string CheckOrder(Zone_Shop shop, RestaurantOrder order)
        {
            Thing pass = RestaurantOrderCreationUtility.FindOrderCounter(shop);
            if (!(pass is Building_RestaurantPass)) return "缺少接待与出餐台";
            if (order.stockProduct)
            {
                var menu = Inventory.RestaurantProductMenuUtility.StockMenus(shop).FirstOrDefault(m => m.sourceCabinet == order.sourceCabinet && m.MealDef == order.mealDef);
                if (menu == null || !Inventory.RestaurantProductMenuUtility.Available(shop, menu, order.mealCount))
                    return "柜中现货不足或服务员取货路线受阻";
                bool delivery = shop.Map.mapPawns.AllPawnsSpawned.Any(pawn =>
                    RestaurantStaffAvailabilityUtility.IsAvailableNow(pawn)
                    && SimShopStaffApi.IsAssignedToWorkGiver(shop, pawn, DefOfRefs.RSR_WorkGiver_DeliverRestaurantOrder)
                    && pawn.CanReach(menu.sourceCabinet.InventoryInteractionTarget, menu.sourceCabinet.InventoryInteractionEndMode, Danger.Some)
                    && pawn.carryTracker.MaxStackSpaceEver(menu.MealDef) >= order.mealCount
                    && (!order.seatCell.IsValid || RestaurantDiningSpotUtility.TryFindDeliveryCell(pawn, order, out _)));
                return delivery ? "" : "没有能从商品柜到达顾客旁的服务员";
            }
            var stoves = RestaurantCookingUtility.FindUsableStoves(shop, order);
            if (stoves.Count == 0) return "没有兼容配方且可工作的灶台";
            bool cook = shop.Map.mapPawns.AllPawnsSpawned.Any(pawn =>
                RestaurantStaffAvailabilityUtility.IsAvailableNow(pawn)
                && SimShopStaffApi.IsAssignedToWorkGiver(shop, pawn, DefOfRefs.RSR_WorkGiver_CookRestaurantOrder)
                && (order.mealDef == null || pawn.carryTracker.MaxStackSpaceEver(order.mealDef) >= order.mealCount)
                && pawn.CanReach(pass, PathEndMode.Touch, Danger.Some)
                && stoves.Any(stove => RestaurantCookingUtility.CanPawnCookOrderAt(pawn, stove, order)
                    && pawn.CanReach(stove, PathEndMode.InteractionCell, Danger.Some))
                && RestaurantIngredientUtility.TryFindIngredientThingCounts(pawn, shop, order, out _, out _));
            if (!cook) return "食材不足、被预约，或没有能取料并送到出餐台的厨师";
            bool waiter = shop.Map.mapPawns.AllPawnsSpawned.Any(pawn =>
                RestaurantStaffAvailabilityUtility.IsAvailableNow(pawn)
                && SimShopStaffApi.IsAssignedToWorkGiver(shop, pawn, DefOfRefs.RSR_WorkGiver_DeliverRestaurantOrder)
                && pawn.CanReach(pass, PathEndMode.Touch, Danger.Some)
                && (order.mealDef == null || pawn.carryTracker.MaxStackSpaceEver(order.mealDef) >= order.mealCount)
                && (!order.seatCell.IsValid || RestaurantDiningSpotUtility.TryFindDeliveryCell(pawn, order, out _)));
            return waiter ? "" : "没有能从出餐台到达顾客旁的服务员";
        }

        //检查店铺配置和至少一道可制作菜单，职责是不把单纯启用误报为可营业。
        public static string CheckShop(Zone_Shop shop, RestaurantShopSettings settings)
        {
            if (shop?.Map == null) return "商店区域不可用";
            if (settings?.enabled != true) return "餐厅已停用";
            if (!(RestaurantOrderCreationUtility.FindOrderCounter(shop) is Building_RestaurantPass))
                return "缺少接待与出餐台";
            var registers = shop.Cells.SelectMany(cell => cell.GetThingList(shop.Map))
                .OfType<Building_CashRegister>().Distinct().ToList();
            if (registers.Count == 0) return "店内缺少收银台";
            if (!registers.Any(register => register.IsManned)) return "当前没有在岗收银员";
            var menus = Inventory.RestaurantProductMenuUtility.AllMenus(shop, settings);
            if (menus.Count == 0) return "没有启用的有效菜单";
            string last = "没有可制作菜单";
            foreach (var menu in menus)
            {
                if ((menu.IsStockProduct ? !Inventory.RestaurantProductMenuUtility.Available(shop, menu, menu.minCount) : !RestaurantIngredientUtility.HasIngredients(null, shop, menu, menu.minCount)))
                {
                    last = "食材不足：" + menu.DisplayLabel;
                    continue;
                }
                var preview = new RestaurantOrder
                {
                    stockProduct = menu.IsStockProduct, sourceCabinet = menu.sourceCabinet, mode = menu.deliveryMode,
                    shopZoneId = shop.ID, mealDef = menu.MealDef, mealCount = menu.minCount,
                    ingredients = RestaurantIngredientUtility.BuildNeeds(menu, menu.minCount)
                };
                last = CheckOrder(shop, preview);
                if (last.NullOrEmpty())
                {
                    bool seats = shop.Map.mapPawns.AllPawnsSpawned.Any(pawn =>
                        RestaurantStaffAvailabilityUtility.IsAvailableNow(pawn)
                        && RestaurantDiningSpotUtility.TryFindDiningSpot(pawn, shop, out _, out _));
                    return seats ? "" : "没有可用且可达的桌椅餐位";
                }
            }
            return last;
        }

        //检查顾客入座条件，职责是只验证候选可用性而不固定菜单或占用食材。
        public static bool CanSeat(Pawn customer, Zone_Shop shop, out string reason)
        {
            reason = CheckShop(shop, RestaurantOrderUtility.Settings?.GetOrCreate(shop.ID));
            if (!reason.NullOrEmpty()) return false;
            if (customer?.Map != shop.Map || customer.carryTracker.CarriedThing != null)
            {
                reason = "顾客当前无法接收堂食餐品";
                return false;
            }
            bool checkout = shop.Cells.SelectMany(cell => cell.GetThingList(shop.Map))
                .OfType<Building_CashRegister>().Distinct()
                .Any(register => register.IsManned && customer.CanReach(register, PathEndMode.Touch, Danger.Some));
            if (!checkout)
            {
                reason = "顾客无法到达同店收银台";
                return false;
            }
            if (RestaurantOrderUtility.HasActiveCustomerOrder(customer, shop))
            {
                reason = "顾客已有餐厅订单";
                return false;
            }
            if (!RestaurantDiningSpotUtility.TryFindDiningSpot(customer, shop, out IntVec3 seat, out Thing table))
            {
                reason = "当前满座或顾客无法到达餐位";
                return false;
            }
            var preview = new RestaurantOrder
            {
                shopZoneId = shop.ID, seatCell = seat, tableThingId = table.thingIDNumber,
                customerThingId = customer.thingIDNumber
            };
            if (!RestaurantStaffAvailabilityUtility.HasWaiterForOrder(shop, preview))
            {
                reason = "服务员无法到达顾客旁";
                return false;
            }
            var settings = RestaurantOrderUtility.Settings.GetOrCreate(shop.ID);
            bool affordable = Inventory.RestaurantProductMenuUtility.AllMenus(shop, settings).Any(menu =>
                Inventory.RestaurantProductMenuUtility.Accepts(customer, menu)
                && menu.unitPrice * settings.priceMultiplier * menu.minCount <= SimShopCustomerApi.GetRemainingBudget(customer, shop)
                && (menu.IsStockProduct ? Inventory.RestaurantProductMenuUtility.Available(shop, menu, menu.minCount, customer) : RestaurantIngredientUtility.HasIngredients(customer, shop, menu, menu.minCount)));
            if (!affordable) reason = "没有预算与饮食条件允许的菜单";
            return affordable;
        }
    }
}
