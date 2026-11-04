using System.Linq;
using RimSimRestaurantExtension.Dining;
using RimSimRestaurantExtension.Models;

namespace RimSimRestaurantExtension.GameComp
{
    //把员工和顾客查询限定在活跃记录，定期释放已经结束的用餐明细。
    public partial class GameComponent_RestaurantOrderManager
    {
        private const int HistoryRetentionTicks = 300000;
        private int nextHistoryCleanupTick;

        //直接定位框架动作对应的首张订单，避免每次执行顾客 Job 都遍历历史。
        public RestaurantOrder GetOrderForAction(int actionId) =>
            byAction.TryGetValue(actionId, out var order) ? order : null;

        //只检查当前商店的活跃订单，不为关店后的派工查询创建列表。
        public bool HasActiveOrders(int shopId, bool needsStaffWork = false)
        {
            if (!byShop.TryGetValue(shopId, out var list)) return false;
            for (int i = 0; i < list.Count; i++)
                if (!list[i].IsTerminal && (!needsStaffWork || list[i].NeedsStaffWork)) return true;
            return false;
        }

        //关店后仍在取餐的顾客需要线路继续运输，等待结账的顾客不再占用线路。
        public bool HasConveyorGuests(int mapId, int lineId)
        {
            for (int i = 0; i < activeSessions.Count; i++)
            {
                var session = activeSessions[i];
                if (!session.IsTerminal && session.selfService && session.mapId == mapId
                    && session.conveyorLineId == lineId && session.state != RestaurantSessionState.AwaitingCheckout)
                    return true;
            }
            return false;
        }

        //状态变更后在下次业务巡检收紧索引，遍历中的收尾不会修改正在枚举的集合。
        private void CompactActiveRecords()
        {
            activeSessions.RemoveAll(session => session.IsTerminal);
            if (activeOrders.Count == 0) return;
            activeOrders.RemoveAll(order => order.IsTerminal);
            foreach (var list in byShop.Values) list.RemoveAll(order => order.IsTerminal);
        }

        //保留五天已结束明细；活跃会话的所有子订单一直保留到整个会话结束。
        private void TrimHistory(int now)
        {
            int removedSessions = sessions.RemoveAll(session =>
            {
                if (!session.IsTerminal || now - session.completedTick <= HistoryRetentionTicks
                    || session.Orders.Any(order => !order.IsTerminal)) return false;
                sessionsById.Remove(session.sessionId);
                return true;
            });
            int removedOrders = orders.RemoveAll(order => order.IsTerminal
                && now - order.completedTick > HistoryRetentionTicks && !sessionsById.ContainsKey(order.sessionId));
            if (removedSessions > 0 || removedOrders > 0) RebuildIndices();
        }
    }
}
