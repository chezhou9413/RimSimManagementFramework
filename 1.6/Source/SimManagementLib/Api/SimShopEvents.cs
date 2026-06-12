using SimManagementLib.Pojo;
using SimManagementLib.SimZone;
using System;
using Verse;

namespace SimManagementLib.Api
{
    /// <summary>
    /// 提供商店框架对外事件，负责把内部流程变化安全通知给外部模组。
    /// </summary>
    public static class SimShopEvents
    {
        public static event Action<Pawn, CustomerServiceOrder, Zone_Shop> ServiceOrderCreated;
        public static event Action<Pawn, CustomerServiceOrder, Zone_Shop> ServiceOrderPaid;
        public static event Action<Pawn, CustomerServiceOrder, Zone_Shop> ServiceOrderCompleted;
        public static event Action<PreparedShopOrder> PreparedOrderCreated;
        public static event Action<PreparedShopOrder, Pawn> PreparedOrderAssigned;
        public static event Action<PreparedShopOrder, Pawn> PreparedOrderStarted;
        public static event Action<PreparedShopOrder, Pawn> PreparedOrderCompleted;
        public static event Action<PreparedShopOrder, string> PreparedOrderCanceled;
        public static event Action<PreparedShopOrder, Pawn> PreparedOrderDelivered;
        public static event Action<CustomerActionOrder, Pawn> CustomerActionOrderCreated;
        public static event Action<CustomerActionOrder> CustomerActionOrderStarted;
        public static event Action<CustomerActionOrder, Pawn> CustomerActionOrderWaitingStaff;
        public static event Action<CustomerActionOrder, Pawn> CustomerActionOrderStaffAssigned;
        public static event Action<CustomerActionOrder> CustomerActionOrderCompleted;
        public static event Action<CustomerActionOrder, string> CustomerActionOrderCanceled;
        public static event Action<ShopCheckoutContext> CheckoutBeforeCommit;
        public static event Action<ShopCheckoutContext> CheckoutPaid;
        public static event Action<ShopCheckoutContext> CheckoutFailed;
        public static event Action<Pawn> CustomerArrived;

        /// <summary>
        /// 安全触发服务订单创建事件。
        /// </summary>
        public static void NotifyServiceOrderCreated(Pawn customer, CustomerServiceOrder order, Zone_Shop shop)
        {
            InvokeSafe(ServiceOrderCreated, customer, order, shop, "服务订单创建");
        }

        /// <summary>
        /// 安全触发服务订单付款事件。
        /// </summary>
        public static void NotifyServiceOrderPaid(Pawn customer, CustomerServiceOrder order, Zone_Shop shop)
        {
            InvokeSafe(ServiceOrderPaid, customer, order, shop, "服务订单付款");
        }

        /// <summary>
        /// 安全触发服务订单完成事件。
        /// </summary>
        public static void NotifyServiceOrderCompleted(Pawn customer, CustomerServiceOrder order, Zone_Shop shop)
        {
            InvokeSafe(ServiceOrderCompleted, customer, order, shop, "服务订单完成");
        }

        /// <summary>
        /// 安全触发现做订单创建事件。
        /// </summary>
        public static void NotifyPreparedOrderCreated(PreparedShopOrder order)
        {
            InvokeSafe(PreparedOrderCreated, order, "现做订单创建");
        }

        /// <summary>
        /// 安全触发现做订单认领事件。
        /// </summary>
        public static void NotifyPreparedOrderAssigned(PreparedShopOrder order, Pawn staff)
        {
            InvokeSafe(PreparedOrderAssigned, order, staff, "现做订单认领");
        }

        /// <summary>
        /// 安全触发现做订单开始制作事件。
        /// </summary>
        public static void NotifyPreparedOrderStarted(PreparedShopOrder order, Pawn staff)
        {
            InvokeSafe(PreparedOrderStarted, order, staff, "现做订单开始制作");
        }

        /// <summary>
        /// 安全触发现做订单制作完成事件。
        /// </summary>
        public static void NotifyPreparedOrderCompleted(PreparedShopOrder order, Pawn staff)
        {
            InvokeSafe(PreparedOrderCompleted, order, staff, "现做订单制作完成");
        }

        /// <summary>
        /// 安全触发现做订单取消事件。
        /// </summary>
        public static void NotifyPreparedOrderCanceled(PreparedShopOrder order, string reason)
        {
            InvokeSafe(PreparedOrderCanceled, order, reason, "现做订单取消");
        }

        /// <summary>
        /// 安全触发现做订单交付事件。
        /// </summary>
        public static void NotifyPreparedOrderDelivered(PreparedShopOrder order, Pawn customer)
        {
            InvokeSafe(PreparedOrderDelivered, order, customer, "现做订单交付");
        }

        /// <summary>
        /// 安全触发顾客动作订单创建事件。
        /// </summary>
        public static void NotifyCustomerActionOrderCreated(CustomerActionOrder order, Pawn customer)
        {
            InvokeSafe(CustomerActionOrderCreated, order, customer, "顾客动作订单创建");
        }

        /// <summary>
        /// 安全触发顾客动作订单开始事件。
        /// </summary>
        public static void NotifyCustomerActionOrderStarted(CustomerActionOrder order)
        {
            InvokeSafe(CustomerActionOrderStarted, order, "顾客动作订单开始");
        }

        /// <summary>
        /// 安全触发顾客动作订单等待员工事件。
        /// </summary>
        public static void NotifyCustomerActionOrderWaitingStaff(CustomerActionOrder order, Pawn staff)
        {
            InvokeSafe(CustomerActionOrderWaitingStaff, order, staff, "顾客动作订单等待员工");
        }

        /// <summary>
        /// 安全触发顾客动作订单员工加入事件。
        /// </summary>
        public static void NotifyCustomerActionOrderStaffAssigned(CustomerActionOrder order, Pawn staff)
        {
            InvokeSafe(CustomerActionOrderStaffAssigned, order, staff, "顾客动作订单员工加入");
        }

        /// <summary>
        /// 安全触发顾客动作订单完成事件。
        /// </summary>
        public static void NotifyCustomerActionOrderCompleted(CustomerActionOrder order)
        {
            InvokeSafe(CustomerActionOrderCompleted, order, "顾客动作订单完成");
        }

        /// <summary>
        /// 安全触发顾客动作订单取消事件。
        /// </summary>
        public static void NotifyCustomerActionOrderCanceled(CustomerActionOrder order, string reason)
        {
            InvokeSafe(CustomerActionOrderCanceled, order, reason, "顾客动作订单取消");
        }

        /// <summary>
        /// 安全触发结账提交前事件。
        /// </summary>
        public static void NotifyCheckoutBeforeCommit(ShopCheckoutContext context)
        {
            InvokeSafe(CheckoutBeforeCommit, context, "结账提交前");
        }

        /// <summary>
        /// 安全触发结账付款完成事件。
        /// </summary>
        public static void NotifyCheckoutPaid(ShopCheckoutContext context)
        {
            InvokeSafe(CheckoutPaid, context, "结账付款完成");
        }

        /// <summary>
        /// 安全触发结账失败事件。
        /// </summary>
        public static void NotifyCheckoutFailed(ShopCheckoutContext context)
        {
            InvokeSafe(CheckoutFailed, context, "结账失败");
        }

        // 安全触发顾客到达事件，负责让外部模组拿到刚生成的顾客 Pawn。
        public static void NotifyCustomerArrived(Pawn customer)
        {
            InvokeSafe(CustomerArrived, customer, "顾客到达");
        }

        /// <summary>
        /// 调用一个参数的外部事件，并隔离订阅者异常。
        /// </summary>
        private static void InvokeSafe<T>(Action<T> action, T arg, string eventName)
        {
            if (action == null) return;
            foreach (Action<T> handler in action.GetInvocationList())
            {
                try
                {
                    handler(arg);
                }
                catch (Exception ex)
                {
                    Log.Error($"[SimShop API] 外部事件 {eventName} 执行失败: {ex}");
                }
            }
        }

        /// <summary>
        /// 调用两个参数的外部事件，并隔离订阅者异常。
        /// </summary>
        private static void InvokeSafe<T1, T2>(Action<T1, T2> action, T1 arg1, T2 arg2, string eventName)
        {
            if (action == null) return;
            foreach (Action<T1, T2> handler in action.GetInvocationList())
            {
                try
                {
                    handler(arg1, arg2);
                }
                catch (Exception ex)
                {
                    Log.Error($"[SimShop API] 外部事件 {eventName} 执行失败: {ex}");
                }
            }
        }

        /// <summary>
        /// 调用三个参数的外部事件，并隔离订阅者异常。
        /// </summary>
        private static void InvokeSafe<T1, T2, T3>(Action<T1, T2, T3> action, T1 arg1, T2 arg2, T3 arg3, string eventName)
        {
            if (action == null) return;
            foreach (Action<T1, T2, T3> handler in action.GetInvocationList())
            {
                try
                {
                    handler(arg1, arg2, arg3);
                }
                catch (Exception ex)
                {
                    Log.Error($"[SimShop API] 外部事件 {eventName} 执行失败: {ex}");
                }
            }
        }
    }
}
