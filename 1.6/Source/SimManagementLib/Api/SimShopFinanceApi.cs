using SimManagementLib.GameComp;
using SimManagementLib.Pojo;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimZone;
using System.Collections.Generic;
using Verse;

namespace SimManagementLib.Api
{
    /// <summary>
    /// 提供财务账单的安全包装入口，负责让外部模组使用现有账单系统而不直接操作内部状态。
    /// </summary>
    public static class SimShopFinanceApi
    {
        /// <summary>
        /// 返回财务管理器，缺少游戏实例时返回 null。
        /// </summary>
        public static GameComponent_ShopFinanceManager Manager => Current.Game?.GetComponent<GameComponent_ShopFinanceManager>();

        /// <summary>
        /// 把服务费用加入顾客待结账账单。
        /// </summary>
        public static void QueueServiceSale(Pawn customer, Zone_Shop zone, string serviceDefName, string serviceLabel, int count, float amount)
        {
            Manager?.QueueServiceSale(customer, zone, serviceDefName, serviceLabel, count, amount);
        }

        /// <summary>
        /// 把外部自定义财务明细加入顾客待结账账单。
        /// </summary>
        public static SimApiResult QueueCustomLine(Pawn customer, Zone_Shop zone, FinanceLineItem line)
        {
            if (customer == null) return SimApiResult.Fail("顾客无效");
            if (line == null) return SimApiResult.Fail("财务明细无效");
            if (line.amount <= 0f) return SimApiResult.Fail("金额必须大于零");
            Manager?.QueueCustomLine(customer, zone, line);
            return SimApiResult.Success();
        }

        /// <summary>
        /// 清除顾客待结账账单。
        /// </summary>
        public static void ClearPendingBill(Pawn customer)
        {
            Manager?.ClearPendingBill(customer);
        }

        /// <summary>
        /// 返回顾客当前待结账账单明细副本。
        /// </summary>
        public static List<FinanceLineItem> GetPendingBillLines(Pawn customer)
        {
            return Manager?.GetPendingBillLines(customer) ?? new List<FinanceLineItem>();
        }

        /// <summary>
        /// 提交顾客收银台结账金额。
        /// </summary>
        public static void CommitCheckout(Pawn customer, Building_CashRegister register, int paidSilver)
        {
            Manager?.CommitCheckout(customer, register, paidSilver);
        }
    }
}
