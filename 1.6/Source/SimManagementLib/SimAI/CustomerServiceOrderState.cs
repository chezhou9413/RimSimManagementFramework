using SimManagementLib.Pojo;
using SimManagementLib.SimService;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SimManagementLib.SimAI
{
    /// <summary>
    /// 保存顾客服务订单和订单编号，负责管理服务型消费的持久状态。
    /// </summary>
    internal class CustomerServiceOrderState
    {
        internal Dictionary<int, List<CustomerServiceOrder>> serviceOrders = new Dictionary<int, List<CustomerServiceOrder>>();
        internal int nextServiceOrderId = 1;

        private List<int> tmpServiceOrderKeys;
        private List<List<CustomerServiceOrder>> tmpServiceOrderValues;

        /// <summary>
        /// 读写服务订单存档数据，并在读档后补齐集合实例。
        /// </summary>
        public void ExposeData()
        {
            Scribe_Collections.Look(ref serviceOrders, "serviceOrders", LookMode.Value, LookMode.Deep, ref tmpServiceOrderKeys, ref tmpServiceOrderValues);
            Scribe_Values.Look(ref nextServiceOrderId, "nextServiceOrderId", 1);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (serviceOrders == null) serviceOrders = new Dictionary<int, List<CustomerServiceOrder>>();
                if (nextServiceOrderId <= 0) nextServiceOrderId = 1;
            }
        }

        /// <summary>
        /// 添加服务订单，并确保订单编号单调递增。
        /// </summary>
        public void AddServiceOrder(int pawnId, CustomerServiceOrder order)
        {
            if (pawnId <= 0 || order == null) return;
            if (order.orderId <= 0) order.orderId = nextServiceOrderId++;
            else if (order.orderId >= nextServiceOrderId) nextServiceOrderId = order.orderId + 1;

            if (!serviceOrders.TryGetValue(pawnId, out List<CustomerServiceOrder> list))
            {
                list = new List<CustomerServiceOrder>();
                serviceOrders[pawnId] = list;
            }

            for (int i = 0; i < list.Count; i++)
            {
                CustomerServiceOrder existing = list[i];
                if (existing == null || existing.orderId != order.orderId) continue;
                list[i] = order;
                return;
            }

            list.Add(order);
        }

        /// <summary>
        /// 返回指定顾客的服务订单列表。
        /// </summary>
        public List<CustomerServiceOrder> GetServiceOrders(int pawnId)
        {
            return serviceOrders.TryGetValue(pawnId, out List<CustomerServiceOrder> list) ? list : null;
        }

        /// <summary>
        /// 按订单编号查找指定顾客的服务订单。
        /// </summary>
        public CustomerServiceOrder GetServiceOrder(int pawnId, int orderId)
        {
            List<CustomerServiceOrder> list = GetServiceOrders(pawnId);
            if (list.NullOrEmpty()) return null;
            return list.FirstOrDefault(o => o != null && o.orderId == orderId);
        }

        /// <summary>
        /// 统计指定服务建筑和服务 Def 当前正在占用并发名额的订单数量。
        /// </summary>
        public int CountActiveServiceOrders(int providerThingId, string serviceDefName)
        {
            if (providerThingId < 0 || string.IsNullOrEmpty(serviceDefName)) return 0;
            int count = 0;
            foreach (List<CustomerServiceOrder> list in serviceOrders.Values)
            {
                if (list.NullOrEmpty()) continue;
                for (int i = 0; i < list.Count; i++)
                {
                    CustomerServiceOrder order = list[i];
                    if (order == null) continue;
                    if (order.providerThingId != providerThingId || order.serviceDefName != serviceDefName) continue;
                    if (order.state == ServiceOrderState.AwaitingPayment
                        || order.state == ServiceOrderState.ReadyToUse
                        || order.state == ServiceOrderState.TicketIssued
                        || order.state == ServiceOrderState.InUse
                        || order.state == ServiceOrderState.UsedAwaitingPayment)
                        count++;
                }
            }

            return count;
        }

        /// <summary>
        /// 清除指定顾客的服务订单。
        /// </summary>
        public void ClearCustomerServiceOrders(int pawnId)
        {
            if (pawnId <= 0) return;
            serviceOrders.Remove(pawnId);
        }
    }
}
