using System.Linq;
using RimSimRestaurantExtension.Dining;
using RimSimRestaurantExtension.Tool;
using SimManagementLib.Api;
using Verse;
namespace RimSimRestaurantExtension.Services
{
    //汇总顾客整次餐厅体验，职责是把多个子订单和最终付款结果一起交给评价系统。
    public class RestaurantReviewSnapshotWorker : CustomerReviewSnapshotWorker
    {
        //构造会话评价摘要，职责是不以最后一杯酒或某个失败子订单替代整次用餐。
        public override void BeforeEnqueue(CustomerReviewSnapshotContext context)
        {
            if (context?.snapshot == null || context.customer == null) return;
            int shopId = context.shop?.ID ?? context.snapshot.zoneId;
            var session = RestaurantOrderUtility.OrderManager.Sessions
                .Where(s => s.customerId == context.customer.thingIDNumber && s.shopId == shopId)
                .OrderByDescending(s => s.sessionId).FirstOrDefault();
            if (session == null) return;
            string goods = string.Join("；", session.Orders.Select(o =>
                $"第{o.round}轮 {o.menuItemLabel} ×{o.mealCount}，实际接受{o.acceptedCount}，{RestaurantBusinessUiState(o)}"));
            context.snapshot.serviceSummary = Append(context.snapshot.serviceSummary,
                $"餐厅用餐共接单{session.rounds}次；{goods}；实际应付{session.Amount:F0}，已付{session.Orders.Sum(o => o.paidAmount):F0}");
            context.snapshot.postPurchaseSummary = Append(context.snapshot.postPurchaseSummary,
                session.state == RestaurantSessionState.Completed ? "顾客在原座吃喝或收下商品后完成收银付款"
                    : "餐厅付款未完成：" + session.reason);
            if (session.Orders.Any(o => !o.failReason.NullOrEmpty()))
                context.snapshot.personalityBiasSummary = Append(context.snapshot.personalityBiasSummary, "部分餐厅商品服务失败，可酌情影响服务评价");
        }

        //说明子订单体验，职责是区分现场食用、带走和失败原因。
        private static string RestaurantBusinessUiState(Models.RestaurantOrder order) =>
            !order.failReason.NullOrEmpty() ? order.failReason
                : order.mode == Inventory.RestaurantDeliveryMode.TakeAway ? "带走商品" : "现场吃喝";

        //追加餐厅摘要，职责是保留框架已有购物和服务评价内容。
        private static string Append(string existing, string text) =>
            existing.NullOrEmpty() ? text : existing + "；" + text;
    }
}
