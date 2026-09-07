using RimSimRestaurantExtension.Models;
using Verse;

namespace RimSimRestaurantExtension.Tool
{
    //记录餐厅订单的关键阶段，职责是让一次完整流程的首个中断点能从 Player.log 直接定位。
    public static class RestaurantFlowLog
    {
        //记录订单正常推进，职责是输出订单、状态、执行者和必要上下文且不在每 Tick 重复写入。
        public static void Stage(RestaurantOrder order, string stage, Pawn actor = null, string details = null)
        {
            Log.Message(BuildMessage(order, stage, actor, details));
        }

        //记录订单失败或 Job 无法启动，职责是把实际原因写入警告日志供开发诊断。
        public static void Failure(RestaurantOrder order, string stage, string reason, Pawn actor = null)
        {
            Log.Warning(BuildMessage(order, stage, actor, reason));
        }

        //构造统一日志文本，职责是保持不同状态机节点的诊断字段一致。
        private static string BuildMessage(RestaurantOrder order, string stage, Pawn actor, string details)
        {
            string orderText = order == null ? "无订单" : $"订单#{order.orderId} 状态={order.state}";
            string actorText = actor == null ? "无执行者" : $"{actor.LabelShortCap}#{actor.thingIDNumber}";
            string detailText = details.NullOrEmpty() ? "" : "；" + details;
            return $"[RimSimRestaurant][流程] {orderText}；阶段={stage}；执行者={actorText}{detailText}";
        }
    }
}
