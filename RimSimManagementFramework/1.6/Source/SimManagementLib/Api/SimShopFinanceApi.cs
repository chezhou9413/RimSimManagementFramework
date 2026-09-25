using SimManagementLib.Tool;
using SimManagementLib.GameComp;
using SimManagementLib.Pojo;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimZone;
using System.Collections.Generic;
using Verse;

namespace SimManagementLib.Api
{
    //提供财务账单的安全包装入口，负责让外部模组使用现有账单系统而不直接操作内部状态。
    public static partial class SimShopFinanceApi
    {
        //返回财务管理器，缺少游戏实例时返回 null。
        public static GameComponent_ShopFinanceManager Manager => Current.Game?.GetComponent<GameComponent_ShopFinanceManager>();

        //按动作订单登记一次应付款和成本明细，职责是让重试分别跳过已经完成的写入。
        public static SimApiResult QueueActionOrderCharge(Pawn customer, Zone_Shop shop, CustomerActionOrder order,
            string label, int count, float amount, float cost)
        {
            if (customer == null || shop == null || order == null || order.orderId <= 0
                || order.customerThingId != customer.thingIDNumber || order.shopZoneId != shop.ID)
                return SimApiResult.Fail(SimTranslation.T("RSMF.Api.Error.ActionOwnerMismatch"));
            if (!ReferenceEquals(order, SimShopCustomerApi.GetActionOrder(order.orderId)))
                return SimApiResult.Fail(SimTranslation.T("RSMF.Api.Error.ActionOrderNotManaged"));
            if (count <= 0 || amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount)
                || cost < 0f || float.IsNaN(cost) || float.IsInfinity(cost))
                return SimApiResult.Fail(SimTranslation.T("RSMF.Api.Error.ActionBillInvalid"));
            if (order.billRegistered && order.financeRegistered) return SimApiResult.Success();
            if (Manager == null || SimShopCustomerApi.GetCurrentShop(customer) != shop)
                return SimApiResult.Fail(SimTranslation.T("RSMF.Api.Error.FinanceContextUnavailable"));
            if (!order.billRegistered)
            {
                SimApiResult result = SimShopCustomerApi.AddCustomerBill(customer, amount);
                if (!result.success) return result;
                order.billAmount = amount;
                order.billRegistered = true;
            }
            if (!order.financeRegistered)
            {
                Manager.QueueCustomLine(customer, shop, new FinanceLineItem
                {
                    lineType = FinanceLineTypes.Service,
                    defName = ActionOrderChargeKey(order.orderId),
                    label = label,
                    count = count,
                    amount = order.billAmount,
                    cost = cost
                });
                order.financeRegistered = true;
            }
            return SimApiResult.Success();
        }

        //返回动作账单的独立标识，职责是保持订单明细独立并供付款回调核对来源。
        public static string ActionOrderChargeKey(int orderId)
        {
            return "CustomerActionOrder/" + orderId;
        }

        //把服务费用加入顾客待结账账单。
        public static void QueueServiceSale(Pawn customer, Zone_Shop zone, string serviceDefName, string serviceLabel, int count, float amount)
        {
            Manager?.QueueServiceSale(customer, zone, serviceDefName, serviceLabel, count, amount);
        }

        //把带实际成本的服务费用加入待结账账单，职责是让会消耗物料的扩展正确计入利润。
        public static SimApiResult QueueCostedServiceSale(Pawn customer, Zone_Shop zone, string serviceDefName,
            string serviceLabel, int count, float amount, float cost)
        {
            if (customer == null) return SimApiResult.Fail(SimTranslation.T("RSMF.Api.Error.CustomerInvalid"));
            if (string.IsNullOrEmpty(serviceDefName)) return SimApiResult.Fail(SimTranslation.T("RSMF.Api.Error.ServiceNameInvalid"));
            if (count <= 0 || amount <= 0f) return SimApiResult.Fail(SimTranslation.T("RSMF.Api.Error.PositiveQuantityAmountRequired"));
            if (Manager == null) return SimApiResult.Fail(SimTranslation.T("RSMF.Api.Error.FinanceManagerUnavailable"));
            Manager.QueueCustomLine(customer, zone, new FinanceLineItem
            {
                lineType = FinanceLineTypes.Service,
                isCombo = false,
                label = string.IsNullOrEmpty(serviceLabel) ? serviceDefName : serviceLabel,
                defName = serviceDefName,
                count = count,
                amount = amount,
                cost = UnityEngine.Mathf.Max(0f, cost)
            });
            return SimApiResult.Success();
        }

        //把外部自定义财务明细加入顾客待结账账单。
        public static SimApiResult QueueCustomLine(Pawn customer, Zone_Shop zone, FinanceLineItem line)
        {
            if (customer == null) return SimApiResult.Fail(SimTranslation.T("RSMF.Api.Error.CustomerInvalid"));
            if (line == null) return SimApiResult.Fail(SimTranslation.T("RSMF.Api.Error.FinanceLineInvalid"));
            if (line.amount <= 0f) return SimApiResult.Fail(SimTranslation.T("RSMF.Api.Error.PositiveAmountRequired"));
            Manager?.QueueCustomLine(customer, zone, line);
            return SimApiResult.Success();
        }

        //清除顾客待结账账单。
        public static void ClearPendingBill(Pawn customer)
        {
            Manager?.ClearPendingBill(customer);
        }

        //返回顾客当前待结账账单明细副本。
        public static List<FinanceLineItem> GetPendingBillLines(Pawn customer)
        {
            return Manager?.GetPendingBillLines(customer) ?? new List<FinanceLineItem>();
        }

        //提交顾客收银台结账金额。
        public static void CommitCheckout(Pawn customer, Building_CashRegister register, int paidSilver)
        {
            Manager?.CommitCheckout(customer, register, paidSilver);
        }
    }
}
