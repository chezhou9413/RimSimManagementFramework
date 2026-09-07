using System.Linq;
using RimSimRestaurantExtension.Dining;
using RimSimRestaurantExtension.Models;
using RimSimRestaurantExtension.Tool;
using SimManagementLib.Api;
using Verse;
namespace RimSimRestaurantExtension.Services
{
    //接入框架收银生命周期，职责是按用餐会话完成结算并回收未付款带走商品。
    public class RestaurantCheckoutWorker : ShopCheckoutWorker
    {
        //阻止会话未结束时提前收银，职责是不以单个子订单完成为结账条件。
        public override bool CanPawnEnterCheckout(ShopCheckoutReadinessContext context)
        {
            if (context == null) return true;
            bool dining = RestaurantOrderUtility.OrderManager.Sessions.Any(s => !s.IsTerminal
                && s.customerId == context.pawnId && s.state != RestaurantSessionState.AwaitingCheckout);
            if (dining) context.Defer("顾客尚有接单请求、未处理商品或制作配送订单");
            return !dining;
        }

        //核对会话账单来源并完成支付，职责是保持重复回调幂等且收入只由框架提交。
        public override void AfterCheckoutPaid(ShopCheckoutContext context)
        {
            if (context?.customer == null || !context.success) return;
            foreach (var session in RestaurantOrderUtility.OrderManager.Sessions.Where(s => !s.IsTerminal
                && s.customerId == context.customer.thingIDNumber && s.shopId == context.shop?.ID
                && s.state == RestaurantSessionState.AwaitingCheckout && s.billQueued))
            {
                string prefix = SimShopFinanceApi.ActionOrderChargeKey(session.actionOrderId) + "/";
                if (session.Amount > 0 && !context.billLines.Any(l => l.defName?.StartsWith(prefix) == true)) continue;
                float total = context.billLines.Sum(l => l.amount);
                foreach (var order in session.Orders)
                {
                    if (order.acceptedCount <= 0) continue;
                    order.paidAmount = total > 0 ? context.paidSilver * order.unitPrice * order.acceptedCount / total : 0;
                    if (!order.IsTerminal) RestaurantOrderUtility.CompleteOrder(order);
                }
                session.state = RestaurantSessionState.Completed;
                session.completedTick = Find.TickManager.TicksGame;
                SimShopDeliveredGoodsApi.Complete(SimShopCustomerApi.GetActionOrder(session.actionOrderId));
            }
        }

        //记录会话付款失败，职责是回收仍存在的带走商品并保留已消费和损耗记录。
        public override void OnCheckoutFailed(ShopCheckoutContext context)
        {
            if (context?.customer == null) return;
            foreach (var session in RestaurantOrderUtility.OrderManager.Sessions.Where(s => !s.IsTerminal
                && s.customerId == context.customer.thingIDNumber && s.shopId == context.shop?.ID))
                RestaurantSessionUtility.Abort(session, context.timedOut ? "收银等待超时" : "结账失败：" + context.failReason);
        }
    }
}
