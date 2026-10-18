using SimManagementLib.Api;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SimManagementLib.GameComp
{
    //管理全局顾客动作订单队列，负责订单编号、存档读写、查询和终态清理。
    public class GameComponent_CustomerActionOrderManager : GameComponent
    {
        private const int TerminalRetentionTicks = 300000;
        private List<CustomerActionOrder> orders = new List<CustomerActionOrder>();
        private int nextOrderId = 1;

        //创建顾客动作订单管理器。
        public GameComponent_CustomerActionOrderManager(Game game)
        {
        }

        //返回所有顾客动作订单的只读快照。
        public IReadOnlyList<CustomerActionOrder> Orders => orders;

        //周期清理过期终态订单，职责是避免长档中的动作历史无限增大存档和查询成本。
        public override void GameComponentTick()
        {
            base.GameComponentTick();
            int now = Find.TickManager?.TicksGame ?? 0;
            if (now > 0 && now % 2500 == 0)
                TrimTerminalOrders(TerminalRetentionTicks);
        }

        //读写顾客动作订单管理器数据，并在读档后修复集合和编号。
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref orders, "customerActionOrders", LookMode.Deep);
            Scribe_Values.Look(ref nextOrderId, "customerActionOrderNextId", 1);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (orders == null) orders = new List<CustomerActionOrder>();
                orders.RemoveAll(o => o == null);
                int maxId = orders.Count > 0 ? orders.Max(o => o.orderId) : 0;
                if (nextOrderId <= maxId) nextOrderId = maxId + 1;
                if (nextOrderId <= 0) nextOrderId = 1;
            }
        }

        //把订单加入队列，并确保订单编号唯一递增。
        public CustomerActionOrder AddOrder(CustomerActionOrder order)
        {
            if (order == null) return null;
            if (orders == null) orders = new List<CustomerActionOrder>();
            if (order.orderId <= 0) order.orderId = nextOrderId++;
            else if (order.orderId >= nextOrderId) nextOrderId = order.orderId + 1;
            orders.Add(order);
            return order;
        }

        //按编号查找顾客动作订单。
        public CustomerActionOrder GetOrder(int orderId)
        {
            if (orderId <= 0 || orders == null) return null;
            return orders.FirstOrDefault(o => o != null && o.orderId == orderId);
        }

        //按查询条件返回顾客动作订单列表。
        public List<CustomerActionOrder> QueryOrders(CustomerActionOrderQuery query)
        {
            if (orders == null) return new List<CustomerActionOrder>();
            if (query == null) return orders.Where(o => o != null).ToList();
            return orders.Where(o => query.Matches(o)).ToList();
        }

        //删除结束时间过久的终态订单，避免存档无限增长。
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

        //判断订单状态是否已经结束。
        private static bool IsTerminal(CustomerActionOrderState state)
        {
            return state == CustomerActionOrderState.Completed
                || state == CustomerActionOrderState.Canceled
                || state == CustomerActionOrderState.Failed;
        }
    }
}
