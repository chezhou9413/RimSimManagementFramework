using System;
using System.Collections.Generic;
using RimSimRestaurantExtension.Models;
using Verse;

namespace RimSimRestaurantExtension.GameComp
{
    //管理厨房派工的失败重试，职责是让员工和订单避开同一 Tick 内的递归派工。
    public partial class GameComponent_RestaurantOrderManager
    {
        private readonly Dictionary<int, int> cookRetryTicks = new Dictionary<int, int>();

        //判断员工是否可以领取制作任务，职责是让一次失败后先退出当前派工链再重新检查条件。
        public bool CanDispatchCooking(Pawn cook)
        {
            if (cook?.Map == null || cook.carryTracker?.CarriedThing != null) return false;
            if (!cookRetryTicks.TryGetValue(cook.thingIDNumber, out int retryTick)) return true;
            if (Find.TickManager.TicksGame < retryTick) return false;
            cookRetryTicks.Remove(cook.thingIDNumber);
            return true;
        }

        //登记失败后的重试时点，职责是保留阻塞原因且不改变顾客既有上菜期限。
        public void DeferCooking(Pawn cook, RestaurantOrder order, string reason)
        {
            int retryTick = Find.TickManager.TicksGame + 120;
            cookRetryTicks[cook.thingIDNumber] = retryTick;
            if (order == null || order.IsTerminal) return;
            order.nextCookingAttemptTick = Math.Max(order.nextCookingAttemptTick, retryTick);
            order.blockReason = reason;
        }
    }
}
