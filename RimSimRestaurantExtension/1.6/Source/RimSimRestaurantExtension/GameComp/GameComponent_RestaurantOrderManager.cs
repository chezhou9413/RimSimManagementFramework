using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.Models;
using RimSimRestaurantExtension.Tool;
using SimManagementLib.Api;
using Verse;

namespace RimSimRestaurantExtension.GameComp
{
    //保存餐厅领域订单并提供索引，职责是周期检查设施、期限和被中断的员工认领。
    public partial class GameComponent_RestaurantOrderManager : GameComponent
    {
        private List<RestaurantOrder> orders = new List<RestaurantOrder>();
        private readonly Dictionary<int, RestaurantOrder> byId = new Dictionary<int, RestaurantOrder>();
        private readonly Dictionary<int, List<RestaurantOrder>> byShop = new Dictionary<int, List<RestaurantOrder>>();
        private int nextOrderId = 1;
        public IReadOnlyList<RestaurantOrder> Orders => orders;

        //初始化运行期缓存，职责是不让新游戏复用上个游戏的选择或经营状态。
        public GameComponent_RestaurantOrderManager(Game game)
        {
            RestaurantMenuUtility.ResetSelections();
            RestaurantBusinessAvailability.Reset();
            RestaurantThingQuery.Reset();
        }

        //按固定周期检查活跃会话，职责是避免顾客每 Tick 扫描地图。
        public override void GameComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now % 120 != 0) return;
            foreach (var session in sessions.ToList())
                Dining.RestaurantSessionUtility.Tick(session);
            foreach (RestaurantOrder order in orders.Where(o => !o.IsTerminal).ToList())
            {
                var session = SessionFor(order);
                if (session == null || session.IsTerminal || session.state == Dining.RestaurantSessionState.AwaitingCheckout) continue;
                Map map = Find.Maps.FirstOrDefault(item => item.uniqueID == order.mapId);
                if (map == null || !RestaurantOrderUtility.EnsureOrderStillValid(order, map)) continue;
                RecoverClaim(order, map);
                RestaurantOrderDiagnostics.Refresh(order, map);
                Inventory.RestaurantOrderStock.Validate(order, map);
            }
            if (now % 2520 == 0) RestaurantMenuUtility.CleanupSelections();
        }

        //恢复失去 Job 的员工认领，职责是不将已出餐订单退回制作。
        private static void RecoverClaim(RestaurantOrder order, Map map)
        {
            bool kitchen = order.state == RestaurantOrderState.Cooking || order.state == RestaurantOrderState.ChefBringingToPass;
            bool service = order.state == RestaurantOrderState.TakingOrder || order.state == RestaurantOrderState.Delivering;
            if (!kitchen && !service) return;
            int pawnId = kitchen ? order.cookThingId : order.waiterThingId;
            if (pawnId < 0) return;
            Pawn employee = RestaurantOrderUtility.FindThingById(map, pawnId) as Pawn;
            bool jobActive = employee != null
                && RestaurantJobUtility.GetOrderId(employee.CurJob) == order.orderId
                && (kitchen
                    ? employee.CurJobDef == DefOfRefs.RSR_CookRestaurantOrder || employee.CurJobDef == DefOfRefs.RSR_BringRestaurantMealToPass
                    : employee.CurJobDef == DefOfRefs.RSR_TakeRestaurantOrder || employee.CurJobDef == DefOfRefs.RSR_DeliverRestaurantOrder);
            if (jobActive) return;
            if (employee != null) RestaurantOrderCoordinator.ReleaseClaim(order, employee);
            else
            {
                order.cookThingId = kitchen ? -1 : order.cookThingId;
                order.waiterThingId = service ? -1 : order.waiterThingId;
                if (order.state == RestaurantOrderState.Cooking)
                {
                    Inventory.RestaurantOrderStock.Release(order);
                    RestaurantOrderCoordinator.MoveTo(order, RestaurantOrderState.WaitingCook);
                }
                if (order.state == RestaurantOrderState.TakingOrder)
                    RestaurantOrderCoordinator.MoveTo(order, RestaurantOrderState.WaitingOrder);
                if (order.state == RestaurantOrderState.Delivering)
                    RestaurantOrderCoordinator.MoveTo(order, RestaurantOrderState.ReadyToDeliver);
            }
        }

        //持久化领域会话，职责是加载后重建运行期查询索引。
        public override void ExposeData()
        {
            Scribe_Collections.Look(ref orders, "restaurantOrders", LookMode.Deep);
            Scribe_Collections.Look(ref sessions, "restaurantSessions", LookMode.Deep);
            Scribe_Values.Look(ref nextOrderId, "restaurantNextOrderId", 1);
            if (Scribe.mode != LoadSaveMode.PostLoadInit) return;
            orders = orders ?? new List<RestaurantOrder>();
            RebuildIndices();
        }

        //创建商品子订单，职责是分配稳定编号并维护运行期查询索引。
        public RestaurantOrder AddOrder(RestaurantOrder order)
        {
            if (order == null) return null;
            order.orderId = nextOrderId++;
            orders.Add(order);
            Index(order);
            return order;
        }

        //按编号查询订单，职责是让高频 Job 查询不遍历历史订单。
        public RestaurantOrder GetOrder(int orderId)
        {
            return byId.TryGetValue(orderId, out RestaurantOrder order) ? order : null;
        }

        //按店铺筛选运行订单，职责是把岗位查询限制在当前商店。
        public List<RestaurantOrder> GetActiveOrders(int shopZoneId = -1)
        {
            IEnumerable<RestaurantOrder> source = shopZoneId < 0 ? orders
                : byShop.TryGetValue(shopZoneId, out var list) ? list : Enumerable.Empty<RestaurantOrder>();
            return source.Where(order => !order.IsTerminal).ToList();
        }

        //建立单条索引，职责是让添加与读档使用一致的查询结构。
        private void Index(RestaurantOrder order)
        {
            byId.Add(order.orderId, order);
            if (!byShop.TryGetValue(order.shopZoneId, out var list))
                byShop[order.shopZoneId] = list = new List<RestaurantOrder>();
            list.Add(order);
        }

        //重建全部索引，职责是同步读档和历史清理后的集合。
        private void RebuildIndices()
        {
            sessionsById.Clear();
            foreach (var session in sessions) sessionsById.Add(session.sessionId, session);
            byId.Clear();
            byShop.Clear();
            foreach (var order in orders) Index(order);
        }
    }
}
