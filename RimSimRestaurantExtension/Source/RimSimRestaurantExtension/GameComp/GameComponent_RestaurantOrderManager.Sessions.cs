using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.Dining;
using RimSimRestaurantExtension.Models;
namespace RimSimRestaurantExtension.GameComp
{
    //管理独立用餐会话，职责是将一名顾客的多个商品订单绑定到一个框架动作。
    public partial class GameComponent_RestaurantOrderManager
    {
        private List<RestaurantDiningSession> sessions = new List<RestaurantDiningSession>();
        private readonly Dictionary<int, RestaurantDiningSession> sessionsById = new Dictionary<int, RestaurantDiningSession>();
        public IReadOnlyList<RestaurantDiningSession> Sessions => sessions;

        //创建初始会话，职责是以首次请求编号提供稳定关联。
        public RestaurantDiningSession CreateSession(RestaurantOrder first)
        {
            var session = new RestaurantDiningSession { sessionId = first.orderId, anchorOrderId = first.orderId,
                customerId = first.customerThingId, mapId = first.mapId, shopId = first.shopZoneId,
                tableId = first.tableThingId, seat = first.seatCell, lastOrderTick = first.createdTick };
            first.sessionId = session.sessionId;
            first.round = 1;
            session.orderIds.Add(first.orderId);
            sessions.Add(session);
            sessionsById.Add(session.sessionId, session);
            return session;
        }

        //从商品子订单查找所属会话。
        public RestaurantDiningSession SessionFor(RestaurantOrder order) => order == null ? null : GetSession(order.sessionId);

        //按持久化编号查询会话。
        public RestaurantDiningSession GetSession(int id) => sessionsById.TryGetValue(id, out var session) ? session : null;

        //按顾客查询当前会话，职责是计算未结算承诺和追加需求。
        public RestaurantDiningSession ForCustomer(int customerId, int shopId) =>
            sessions.FirstOrDefault(s => !s.IsTerminal && s.customerId == customerId && s.shopId == shopId);

        //同步框架动作关联，职责是让现有和随后创建的子订单共享同一动作。
        public void BindAction(RestaurantOrder anchor, int actionId)
        {
            var session = SessionFor(anchor);
            session.actionOrderId = actionId;
            foreach (var order in session.Orders) order.actionOrderId = actionId;
        }

        //创建下一轮待接单请求，职责是保持同顾客最多只有一个未确认请求。
        public void RequestNext(RestaurantDiningSession session)
        {
            if (session.stopOrdering || session.Orders.Any(o => !o.menuConfirmed && !o.IsTerminal)) return;
            var anchor = session.Anchor;
            int now = Verse.Find.TickManager.TicksGame;
            var order = AddOrder(new RestaurantOrder { sessionId = session.sessionId, actionOrderId = session.actionOrderId,
                customerThingId = session.customerId, shopZoneId = session.shopId, mapId = session.mapId,
                providerThingId = anchor.providerThingId, seatCell = session.seat, tableThingId = session.tableId,
                createdTick = now, seatedTick = now, lastProgressTick = now, round = session.rounds + 1,
                state = RestaurantOrderState.WaitingOrder });
            session.orderIds.Add(order.orderId);
        }
    }
}
