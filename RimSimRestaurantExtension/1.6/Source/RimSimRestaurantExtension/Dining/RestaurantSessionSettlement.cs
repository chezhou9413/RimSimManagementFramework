using System.Linq;
using RimSimRestaurantExtension.Models;
using RimSimRestaurantExtension.Tool;
using SimManagementLib.Api;
using Verse;
namespace RimSimRestaurantExtension.Dining
{
    //汇总用餐会话账单，职责是只收费已吃喝或已接受的商品并等待框架收银回调。
    public static class RestaurantSessionSettlement
    {
        //幂等提交每张子订单的金额和成本，职责是保留失败制作的实际损耗。
        public static bool QueueBill(Pawn customer, RestaurantDiningSession session, CustomerActionOrder action)
        {
            if (session.billQueued) return true;
            var shop = RestaurantOrderUtility.FindShopById(customer.Map, session.shopId);
            var lines = session.Orders.Where(o => o.acceptedCount > 0 || o.ingredientCost > 0)
                .Select(o => new ActionOrderChargeLine { key = o.orderId.ToString(), label = o.menuItemLabel,
                    count = System.Math.Max(1, o.acceptedCount), amount = o.acceptedCount * o.unitPrice, cost = o.ingredientCost }).ToList();
            var result = SimShopFinanceApi.QueueActionOrderCharges(customer, shop, action, lines);
            if (!result.success) { Log.Error("[RimSimRestaurant] 会话记账失败：" + result.failReason); return false; }
            foreach (var order in session.Orders)
            {
                if (order.acceptedCount > 0) RestaurantOrderUtility.Preferences.RecordCompletedOrder(customer, order);
                order.chargeQueued = true;
            }
            session.billQueued = true;
            return true;
        }

        //完成动作并明确进入框架收银，职责是只在全部子订单处理完毕后释放餐位。
        public static bool Finish(Pawn customer, RestaurantDiningSession session)
        {
            if (!session.ReadyForCheckout || session.IsTerminal) return false;
            var action = SimShopCustomerApi.GetActionOrder(session.actionOrderId);
            if (action == null) return false;
            if (session.Amount <= 0)
            {
                RestaurantSessionUtility.Abort(session, session.reason.NullOrEmpty() ? "没有已接受的商品，本次用餐结束" : session.reason);
                return true;
            }
            if (!QueueBill(customer, session, action)) return false;
            if (!SimShopCustomerApi.CompleteActionOrder(action).success) return false;
            session.state = RestaurantSessionState.AwaitingCheckout;
            foreach (var order in session.Orders.Where(o => !o.IsTerminal))
                RestaurantOrderCoordinator.MoveTo(order, RestaurantOrderState.AwaitingCheckout);
            RestaurantSessionUtility.RemoveTray(session);
            foreach (var order in session.Orders) { order.seatCell = IntVec3.Invalid; order.tableThingId = -1; }
            return SimShopCustomerApi.MarkCustomerReadyForCheckout(customer).success;
        }
    }
}
