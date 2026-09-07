using SimManagementLib.Api;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SimManagementLib.GameComp
{
    /// <summary>
    /// 管理全局现做订单队列，负责订单编号、存档读写、查询和基础状态维护。
    /// </summary>
    public class GameComponent_PreparedShopOrderManager : GameComponent
    {
        private List<PreparedShopOrder> orders = new List<PreparedShopOrder>();
        private int nextOrderId = 1;

        /// <summary>
        /// 创建现做订单管理器。
        /// </summary>
        public GameComponent_PreparedShopOrderManager(Game game)
        {
        }

        /// <summary>
        /// 返回所有订单的只读快照。
        /// </summary>
        public IReadOnlyList<PreparedShopOrder> Orders => orders;

        /// <summary>
        /// 读写现做订单管理器数据，并在读档后修复集合和编号。
        /// </summary>
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref orders, "preparedShopOrders", LookMode.Deep);
            Scribe_Values.Look(ref nextOrderId, "preparedShopOrderNextId", 1);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (orders == null) orders = new List<PreparedShopOrder>();
                orders.RemoveAll(o => o == null);
                int maxId = orders.Count > 0 ? orders.Max(o => o.orderId) : 0;
                if (nextOrderId <= maxId) nextOrderId = maxId + 1;
                if (nextOrderId <= 0) nextOrderId = 1;
            }
        }

        /// <summary>
        /// 把订单加入队列，并确保订单编号唯一递增。
        /// </summary>
        public PreparedShopOrder AddOrder(PreparedShopOrder order)
        {
            if (order == null) return null;
            if (orders == null) orders = new List<PreparedShopOrder>();
            if (order.orderId <= 0) order.orderId = nextOrderId++;
            else if (order.orderId >= nextOrderId) nextOrderId = order.orderId + 1;
            orders.Add(order);
            return order;
        }

        /// <summary>
        /// 按编号查找现做订单。
        /// </summary>
        public PreparedShopOrder GetOrder(int orderId)
        {
            if (orderId <= 0 || orders == null) return null;
            return orders.FirstOrDefault(o => o != null && o.orderId == orderId);
        }

        /// <summary>
        /// 按查询条件返回现做订单列表。
        /// </summary>
        public List<PreparedShopOrder> QueryOrders(PreparedShopOrderQuery query)
        {
            if (orders == null) return new List<PreparedShopOrder>();
            if (query == null) return orders.Where(o => o != null).ToList();
            return orders.Where(o => query.Matches(o)).ToList();
        }

        /// <summary>
        /// 删除结束时间过久的终态订单，避免存档无限增长。
        /// </summary>
        public void TrimTerminalOrders(int maxAgeTicks)
        {
            if (orders == null || maxAgeTicks <= 0) return;
            int now = Find.TickManager?.TicksGame ?? 0;
            orders.RemoveAll(order =>
            {
                if (order == null) return true;
                if (!IsTerminal(order.state)) return false;
                int endTick = order.completedTick > 0 ? order.completedTick : order.createdTick;
                return endTick > 0 && now - endTick > maxAgeTicks;
            });
        }

        /// <summary>
        /// 判断订单状态是否已经结束。
        /// </summary>
        private static bool IsTerminal(PreparedShopOrderState state)
        {
            return state == PreparedShopOrderState.Delivered
                || state == PreparedShopOrderState.Canceled
                || state == PreparedShopOrderState.Failed;
        }
    }
}
