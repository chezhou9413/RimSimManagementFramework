using System.Linq;
using RimSimRestaurantExtension.Models;
using SimManagementLib.Api;
using Verse;

namespace RimSimRestaurantExtension.Tool
{
    //生成运行中订单的阻塞原因，职责是让周期守护与总览使用具体人员和路径状态。
    internal static class RestaurantOrderDiagnostics
    {
        //更新当前订单阻塞说明，职责是只在待派工阶段执行可用性查询。
        public static void Refresh(RestaurantOrder order, Map map)
        {
            order.blockReason = "";
            var shop = RestaurantOrderUtility.FindShopById(map, order.shopZoneId);
            if (shop == null) return;
            if (order.state == RestaurantOrderState.WaitingCook)
            {
                order.blockReason = RestaurantBusinessAvailability.CheckOrder(shop, order);
                return;
            }
            if (order.state == RestaurantOrderState.WaitingOrder)
            {
                if (!RestaurantStaffAvailabilityUtility.HasStaffForWorkGiver(shop, DefOfRefs.RSR_WorkGiver_TakeRestaurantOrder))
                    order.blockReason = "当前没有可工作的接单服务员";
                else if (!RestaurantStaffAvailabilityUtility.HasWaiterForOrder(shop, order))
                    order.blockReason = "顾客旁的交谈位置受阻或已被预约";
            }
            if (order.state == RestaurantOrderState.ReadyToDeliver)
            {
                bool deliverable = map.mapPawns.AllPawnsSpawned.Any(pawn =>
                    RestaurantStaffAvailabilityUtility.IsAvailableNow(pawn)
                    && SimShopStaffApi.IsAssignedToWorkGiver(shop, pawn, DefOfRefs.RSR_WorkGiver_DeliverRestaurantOrder)
                    && RestaurantMealTransferUtility.CanCollect(pawn, order)
                    && RestaurantDiningSpotUtility.TryFindDeliveryCell(pawn, order, out _));
                if (!deliverable) order.blockReason = "暂无服务员能够完整取餐并到达顾客旁";
            }
        }
    }
}
