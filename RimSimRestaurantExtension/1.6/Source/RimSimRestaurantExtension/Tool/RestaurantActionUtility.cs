using RimSimRestaurantExtension.Models;
using SimManagementLib.Api;
using Verse;
using Verse.AI;

namespace RimSimRestaurantExtension.Tool
{
    //连接框架动作订单与餐厅领域订单，职责是集中处理跨存档解析、成功结算和失败取消。
    public static class RestaurantActionUtility
    {
        //从 Job 恢复框架动作订单，职责是优先使用不受进食数量覆盖的持久化标签。
        public static CustomerActionOrder ResolveActionOrder(Job job)
        {
            int actionOrderId = RestaurantJobUtility.GetOrderId(job);
            return SimShopCustomerApi.GetActionOrder(actionOrderId);
        }

        //从框架动作订单恢复餐厅订单，职责是使用 externalData 的稳定领域编号建立关联。
        public static RestaurantOrder ResolveRestaurantOrder(CustomerActionOrder actionOrder)
        {
            if (actionOrder == null) return null;
            if (int.TryParse(actionOrder.externalData, out int restaurantOrderId))
                return RestaurantOrderUtility.OrderManager?.GetOrder(restaurantOrderId);
            return RestaurantOrderUtility.FindByActionOrderId(actionOrder.orderId);
        }

        //完成顾客餐厅动作，职责是按 Worker 回调一次性写入账单后再结束框架动作订单。
        public static bool Complete(Pawn customer, RestaurantOrder restaurantOrder)
        {
            return Dining.RestaurantSessionSettlement.Finish(customer,
                RestaurantOrderUtility.OrderManager.SessionFor(restaurantOrder));
        }

        //取消顾客餐厅动作，职责是让中断和领域失败通过框架统一回调释放订单。
        public static void Cancel(Pawn customer, RestaurantOrder restaurantOrder, string reason, bool failed)
        {
            if (restaurantOrder == null || restaurantOrder.actionOrderId <= 0) return;
            CustomerActionOrder actionOrder = SimShopCustomerApi.GetActionOrder(restaurantOrder.actionOrderId);
            if (actionOrder == null || !actionOrder.IsActiveState) return;
            SimShopCustomerApi.CancelActionOrder(actionOrder, reason ?? "餐厅动作中断", failed);
        }
    }
}
