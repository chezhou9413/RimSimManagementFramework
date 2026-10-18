using SimManagementLib.Tool;
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
                SimTranslation.T("RSR.Review.Order", (o.round).Named("round"), (o.menuItemLabel).Named("dish"), (o.mealCount).Named("count"), (o.acceptedCount).Named("accepted"), (RestaurantBusinessUiState(o)).Named("status"))));
            context.snapshot.serviceSummary = Append(context.snapshot.serviceSummary,
                SimTranslation.T("RSR.Review.Session", (session.rounds).Named("rounds"), (goods).Named("goods"), (session.Amount.ToString("F0")).Named("amount"), (session.Orders.Sum(o => o.paidAmount).ToString("F0")).Named("paid")));
            context.snapshot.postPurchaseSummary = Append(context.snapshot.postPurchaseSummary,
                session.state == RestaurantSessionState.Completed ? SimTranslation.T("RSR.Review.Completed")
                    : SimTranslation.T("RSR.Review.Unpaid", (session.reason).Named("reason")));
            if (session.Orders.Any(o => !o.failReason.NullOrEmpty()))
                context.snapshot.personalityBiasSummary = Append(context.snapshot.personalityBiasSummary, SimTranslation.T("RSR.Review.PartialFailure"));
        }

        //说明子订单体验，职责是区分现场食用、带走和失败原因。
        private static string RestaurantBusinessUiState(Models.RestaurantOrder order) =>
            !order.failReason.NullOrEmpty() ? order.failReason
                : order.mode == Inventory.RestaurantDeliveryMode.TakeAway ? SimTranslation.T("RSR.Review.TakeAwayGoods") : SimTranslation.T("RSR.UI.Consume");

        //追加餐厅摘要，职责是保留框架已有购物和服务评价内容。
        private static string Append(string existing, string text) =>
            existing.NullOrEmpty() ? text : existing + "；" + text;
    }
}
