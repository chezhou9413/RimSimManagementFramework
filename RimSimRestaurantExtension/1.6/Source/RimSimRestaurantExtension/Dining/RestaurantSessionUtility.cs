using System.Linq;
using RimSimRestaurantExtension.Models;
using RimSimRestaurantExtension.Tool;
using SimManagementLib.Api;
using Verse;
namespace RimSimRestaurantExtension.Dining
{
    //协调用餐会话，职责是维护餐位、追加节奏及订单失败后的统一收尾。
    public static class RestaurantSessionUtility
    {
        //检查会话依赖，职责是不因为单个子订单终止而结束其他已收餐品。
        public static bool Validate(RestaurantDiningSession session, Map map, out string reason)
        {
            reason = "";
            if (session == null || map == null) { reason = "用餐会话或地图已不存在"; return false; }
            Pawn customer = RestaurantOrderUtility.FindThingById(map, session.customerId) as Pawn;
            if (customer == null || customer.Dead || !customer.Spawned) reason = "顾客已离开地图";
            else if (RestaurantOrderUtility.FindShopById(map, session.shopId) == null) reason = "餐厅商店区域已不存在";
            else if (session.state == RestaurantSessionState.Serving && session.tray?.Spawned != true) reason = "本人桌面托盘已丢失";
            else if (session.state != RestaurantSessionState.AwaitingCheckout
                && !RestaurantDiningSpotUtility.IsDiningSpotValid(customer, session.Anchor)) reason = "餐桌或座位已不可用";
            return reason.NullOrEmpty();
        }

        //登记入座并创建私有桌面容器，职责是仅启动首次接单等待。
        public static void Seated(RestaurantDiningSession session, Pawn customer)
        {
            if (session.state == RestaurantSessionState.Seating)
            {
                session.state = RestaurantSessionState.Serving;
                RestaurantOrderCoordinator.Seated(session.Anchor);
            }
            if (session.tray != null && !session.tray.Destroyed) return;
            Thing table = RestaurantDiningSpotUtility.FindTableById(customer.Map, session.tableId);
            IntVec3 cell = GenAdj.CardinalDirections.Select(d => session.seat + d).First(c => table.OccupiedRect().Contains(c));
            var tray = (RestaurantTableTray)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("RSR_TableTray"));
            tray.sessionId = session.sessionId;
            tray.displayOffset = (session.seat - cell).ToVector3() * 0.26f;
            session.tray = tray;
            GenSpawn.Spawn(tray, cell, customer.Map);
        }

        //周期推进追加请求，职责是在等待或进食时使用相同预算和营养承诺。
        public static void Tick(RestaurantDiningSession session)
        {
            if (session.IsTerminal) return;
            Map map = Find.Maps.FirstOrDefault(m => m.uniqueID == session.mapId);
            if (!Validate(session, map, out string reason)) { Abort(session, reason); return; }
            if (session.state == RestaurantSessionState.AwaitingCheckout) return;
            var action = SimShopCustomerApi.GetActionOrder(session.actionOrderId);
            if (action == null || !action.IsActiveState)
            {
                if (Find.TickManager.TicksGame - session.Anchor.createdTick > 600) Abort(session, "框架用餐动作已结束或丢失");
                return;
            }
            if (session.state != RestaurantSessionState.Serving || session.stopOrdering) return;
            var settings = RestaurantOrderUtility.Settings.GetOrCreate(session.shopId);
            if (session.rounds >= settings.maxOrderRounds) { session.stopOrdering = true; return; }
            if (session.Orders.Any(o => !o.menuConfirmed && !o.IsTerminal)) return;
            if (Find.TickManager.TicksGame - session.lastOrderTick < settings.reorderIntervalTicks) return;
            Pawn customer = RestaurantOrderUtility.FindThingById(map, session.customerId) as Pawn;
            var shop = RestaurantOrderUtility.FindShopById(map, session.shopId);
            Thing provider = RestaurantOrderUtility.FindProvider(map, session.Anchor);
            RestaurantMenuUtility.ClearSelection(customer, provider, shop);
            if (RestaurantMenuUtility.GetOrCreateSelection(customer, provider, shop) == null)
            {
                session.stopOrdering = true;
                session.reason = "需求已满足或没有符合剩余预算的商品";
                return;
            }
            RestaurantOrderUtility.OrderManager.RequestNext(session);
        }

        //停止追加并取消未交付子订单，职责是不重置其他订单期限且保留已收餐品。
        public static void StopUndelivered(RestaurantDiningSession session, string reason)
        {
            if (session == null || session.IsTerminal) return;
            session.stopOrdering = true;
            session.reason = reason;
            foreach (var order in session.Orders.Where(o => !o.IsTerminal && !o.mealDelivered).ToList())
                RestaurantOrderUtility.CancelOrder(order, reason);
        }

        //终止失去顾客或餐位的会话，职责是释放员工、托盘及未付款带走实物。
        public static void Abort(RestaurantDiningSession session, string reason)
        {
            if (session == null || session.IsTerminal) return;
            session.state = RestaurantSessionState.Failed;
            session.stopOrdering = true;
            session.reason = reason;
            session.completedTick = Find.TickManager.TicksGame;
            var map = Find.Maps.FirstOrDefault(m => m.uniqueID == session.mapId);
            Pawn customer = RestaurantOrderUtility.FindThingById(map, session.customerId) as Pawn;
            SimShopDeliveredGoodsApi.Recover(customer, SimShopCustomerApi.GetActionOrder(session.actionOrderId));
            foreach (var order in session.Orders.Where(o => !o.IsTerminal).ToList()) RestaurantOrderUtility.FailOrder(order, reason);
            RemoveTray(session);
            RestaurantActionUtility.Cancel(customer, session.Anchor, reason, true);
        }

        //释放会话桌面，职责是在进入收银或终止时放回剩余实物。
        public static void RemoveTray(RestaurantDiningSession session)
        {
            if (session.tray?.Spawned == true) session.tray.Destroy();
            session.tray = null;
            session.seat = IntVec3.Invalid;
        }
    }
}
