using SimManagementLib.Tool;
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
        private static LoadedLanguage snapshotLanguage;

        //清除跨游戏快照，职责是让新游戏和读档重新检查设施。
        public static void Reset()
        {
            snapshots.Clear();
        }

        //取得短周期经营提示，职责是避免界面每帧遍历库存与路径，并支持手动刷新立即重算。
        public static string Snapshot(Zone_Shop shop, bool refresh = false)
        {
            if (snapshotLanguage != LanguageDatabase.activeLanguage)
            {
                snapshots.Clear();
                snapshotLanguage = LanguageDatabase.activeLanguage;
            }
            int now = Find.TickManager?.TicksGame ?? 0;
            if (shop == null) return SimTranslation.T("RSR.Issue.NoShop");
            if (!refresh && snapshots.TryGetValue(shop, out var cached) && now - cached.tick < 120) return cached.issue;
            string issue = CheckShop(shop, RestaurantOrderUtility.Settings?.GetOrCreate(shop.ID));
            snapshots[shop] = (now, issue);
            return issue;
        }

        //检查当前菜品的厨房和出餐路径，职责是筛选能搬运整单实物的厨师与服务员。
        public static string CheckOrder(Zone_Shop shop, RestaurantOrder order)
        {
            Thing pass = RestaurantOrderCreationUtility.FindOrderCounter(shop);
            if (!(pass is Building_RestaurantPass)) return SimTranslation.T("RSR.Issue.NoPass");
            if (order.stockProduct)
            {
                var menu = Inventory.RestaurantProductMenuUtility.StockMenus(shop).FirstOrDefault(m => m.sourceCabinet == order.sourceCabinet && m.MealDef == order.mealDef);
                if (menu == null || !Inventory.RestaurantProductMenuUtility.Available(shop, menu, order.mealCount))
                    return SimTranslation.T("RSR.Issue.StockOrPickupBlocked");
                bool delivery = shop.Map.mapPawns.AllPawnsSpawned.Any(pawn =>
                    RestaurantStaffAvailabilityUtility.IsAvailableNow(pawn)
                    && SimShopStaffApi.IsAssignedToWorkGiver(shop, pawn, DefOfRefs.RSR_WorkGiver_DeliverRestaurantOrder)
                    && pawn.CanReach(menu.sourceCabinet.InventoryInteractionTarget, menu.sourceCabinet.InventoryInteractionEndMode, Danger.Some)
                    && pawn.carryTracker.MaxStackSpaceEver(menu.MealDef) >= order.mealCount
                    && (!order.seatCell.IsValid || RestaurantDiningSpotUtility.TryFindDeliveryCell(pawn, order, out _)));
                return delivery ? "" : SimTranslation.T("RSR.Issue.NoCabinetWaiter");
            }
            var stoves = RestaurantCookingUtility.FindUsableStoves(shop, order);
            if (stoves.Count == 0) return SimTranslation.T("RSR.Issue.NoStove");
            bool cook = shop.Map.mapPawns.AllPawnsSpawned.Any(pawn =>
                RestaurantStaffAvailabilityUtility.IsAvailableNow(pawn)
                && SimShopStaffApi.IsAssignedToWorkGiver(shop, pawn, DefOfRefs.RSR_WorkGiver_CookRestaurantOrder)
                && (order.mealDef == null || pawn.carryTracker.MaxStackSpaceEver(order.mealDef) >= order.mealCount)
                && pawn.CanReach(pass, PathEndMode.Touch, Danger.Some)
                && stoves.Any(stove => RestaurantCookingUtility.CanPawnCookOrderAt(pawn, stove, order)
                    && pawn.CanReach(stove, PathEndMode.InteractionCell, Danger.Some))
                && RestaurantIngredientUtility.TryFindIngredientThingCounts(pawn, shop, order, out _, out _));
            if (!cook) return SimTranslation.T("RSR.Issue.NoCookOrIngredients");
            bool waiter = shop.Map.mapPawns.AllPawnsSpawned.Any(pawn =>
                RestaurantStaffAvailabilityUtility.IsAvailableNow(pawn)
                && SimShopStaffApi.IsAssignedToWorkGiver(shop, pawn, DefOfRefs.RSR_WorkGiver_DeliverRestaurantOrder)
                && pawn.CanReach(pass, PathEndMode.Touch, Danger.Some)
                && (order.mealDef == null || pawn.carryTracker.MaxStackSpaceEver(order.mealDef) >= order.mealCount)
                && (!order.seatCell.IsValid || RestaurantDiningSpotUtility.TryFindDeliveryCell(pawn, order, out _)));
            return waiter ? "" : SimTranslation.T("RSR.Issue.NoPassWaiter");
        }

        //检查店铺配置和至少一道可制作菜单，职责是不把单纯启用误报为可营业。
        public static string CheckShop(Zone_Shop shop, RestaurantShopSettings settings)
        {
            if (shop?.Map == null) return SimTranslation.T("RSR.Issue.ShopUnavailable");
            if (settings?.enabled != true) return SimTranslation.T("RSR.Issue.RestaurantDisabled");
            if (!(RestaurantOrderCreationUtility.FindOrderCounter(shop) is Building_RestaurantPass))
                return SimTranslation.T("RSR.Issue.NoPass");
            var registers = shop.Cells.SelectMany(cell => cell.GetThingList(shop.Map))
                .OfType<Building_CashRegister>().Distinct().ToList();
            if (registers.Count == 0) return SimTranslation.T("RSR.Issue.NoRegister");
            if (!registers.Any(register => register.IsManned)) return SimTranslation.T("RSR.Issue.NoCashier");
            if (Conveyor.Dining.ConveyorDiningAvailability.HasOffer(shop)) return "";
            var menus = Inventory.RestaurantProductMenuUtility.AllMenus(shop, settings);
            if (menus.Count == 0) return SimTranslation.T("RSR.Issue.NoEnabledMenu");
            string last = SimTranslation.T("RSR.Issue.NoCookableMenu");
            foreach (var menu in menus)
            {
                if ((menu.IsStockProduct ? !Inventory.RestaurantProductMenuUtility.Available(shop, menu, menu.minCount) : !RestaurantIngredientUtility.HasIngredients(null, shop, menu, menu.minCount)))
                {
                    last = SimTranslation.T("RSR.Issue.MenuIngredients", (menu.DisplayLabel).Named("dish"));
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
                    return seats ? "" : SimTranslation.T("RSR.Issue.NoSeats");
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
                reason = SimTranslation.T("RSR.Issue.CustomerCannotDine");
                return false;
            }
            bool checkout = shop.Cells.SelectMany(cell => cell.GetThingList(shop.Map))
                .OfType<Building_CashRegister>().Distinct()
                .Any(register => register.IsManned && customer.CanReach(register, PathEndMode.Touch, Danger.Some));
            if (!checkout)
            {
                reason = SimTranslation.T("RSR.Issue.CustomerCannotCheckout");
                return false;
            }
            if (RestaurantOrderUtility.HasActiveCustomerOrder(customer, shop))
            {
                reason = SimTranslation.T("RSR.Issue.CustomerHasOrder");
                return false;
            }
            if (!RestaurantDiningSpotUtility.TryFindDiningSpot(customer, shop, out IntVec3 seat, out Thing table))
            {
                reason = SimTranslation.T("RSR.Issue.SeatsFullOrBlocked");
                return false;
            }
            var preview = new RestaurantOrder
            {
                shopZoneId = shop.ID, seatCell = seat, tableThingId = table.thingIDNumber,
                customerThingId = customer.thingIDNumber
            };
            if (table is Conveyor.Transport.Building_SushiConveyor belt)
                return Conveyor.Dining.ConveyorDiningAvailability.CanServe(customer, shop, belt);
            if (!RestaurantStaffAvailabilityUtility.HasWaiterForOrder(shop, preview))
            {
                reason = SimTranslation.T("RSR.Issue.WaiterCannotReach");
                return false;
            }
            var settings = RestaurantOrderUtility.Settings.GetOrCreate(shop.ID);
            bool affordable = Inventory.RestaurantProductMenuUtility.AllMenus(shop, settings).Any(menu =>
                Inventory.RestaurantProductMenuUtility.Accepts(customer, menu)
                && menu.unitPrice * settings.priceMultiplier * menu.minCount <= SimShopCustomerApi.GetRemainingBudget(customer, shop)
                && (menu.IsStockProduct ? Inventory.RestaurantProductMenuUtility.Available(shop, menu, menu.minCount, customer) : RestaurantIngredientUtility.HasIngredients(customer, shop, menu, menu.minCount)));
            if (!affordable) reason = SimTranslation.T("RSR.Issue.NoSuitableMenu");
            return affordable;
        }
    }
}
