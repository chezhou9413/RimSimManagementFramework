using SimManagementLib.Api;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SimManagementLib.SimWorkGiver
{
    /// <summary>
    /// 提供现做订单按服务建筑分组的工具，负责减少 WorkGiver 对订单列表的重复扫描。
    /// </summary>
    internal static class PrepareOrderGroupingUtility
    {
        private const int ParallelOrderGroupThreshold = 64;

        /// <summary>
        /// 将等待制作的订单按服务建筑编号分组，负责让 HasJobOnThing 直接读取指定建筑的订单列表。
        /// </summary>
        public static Dictionary<int, List<PreparedShopOrder>> BuildGroups(IReadOnlyList<PreparedShopOrder> orders)
        {
            Dictionary<int, List<PreparedShopOrder>> result = new Dictionary<int, List<PreparedShopOrder>>();
            if (orders == null || orders.Count <= 0)
                return result;

            if (orders.Count >= ParallelOrderGroupThreshold)
                return BuildGroupsParallel(orders);

            for (int i = 0; i < orders.Count; i++)
                TryAddOrderToGroup(result, orders[i]);

            return result;
        }

        /// <summary>
        /// 并行按服务建筑编号分组大量订单，负责只读取订单快照并在线程安全集合中聚合。
        /// </summary>
        private static Dictionary<int, List<PreparedShopOrder>> BuildGroupsParallel(IReadOnlyList<PreparedShopOrder> orders)
        {
            PreparedShopOrder[] snapshot = new PreparedShopOrder[orders.Count];
            for (int i = 0; i < orders.Count; i++)
                snapshot[i] = orders[i];

            ConcurrentDictionary<int, ConcurrentBag<PreparedShopOrder>> groups = new ConcurrentDictionary<int, ConcurrentBag<PreparedShopOrder>>();
            Parallel.ForEach(snapshot, order =>
            {
                if (!IsWaitingPreparationOrder(order) || order.providerThingId < 0)
                    return;

                ConcurrentBag<PreparedShopOrder> bag = groups.GetOrAdd(order.providerThingId, _ => new ConcurrentBag<PreparedShopOrder>());
                bag.Add(order);
            });

            Dictionary<int, List<PreparedShopOrder>> result = new Dictionary<int, List<PreparedShopOrder>>();
            foreach (KeyValuePair<int, ConcurrentBag<PreparedShopOrder>> entry in groups)
                result[entry.Key] = new List<PreparedShopOrder>(entry.Value);
            return result;
        }

        /// <summary>
        /// 尝试把单个等待制作订单加入分组，负责供顺序路径复用。
        /// </summary>
        private static void TryAddOrderToGroup(Dictionary<int, List<PreparedShopOrder>> groups, PreparedShopOrder order)
        {
            if (!IsWaitingPreparationOrder(order) || order.providerThingId < 0)
                return;

            if (!groups.TryGetValue(order.providerThingId, out List<PreparedShopOrder> providerOrders))
            {
                providerOrders = new List<PreparedShopOrder>();
                groups[order.providerThingId] = providerOrders;
            }

            providerOrders.Add(order);
        }

        /// <summary>
        /// 判断订单是否正在等待员工制作。
        /// </summary>
        private static bool IsWaitingPreparationOrder(PreparedShopOrder order)
        {
            return order != null && order.state == PreparedShopOrderState.PaidWaitingPreparation;
        }
    }
}
