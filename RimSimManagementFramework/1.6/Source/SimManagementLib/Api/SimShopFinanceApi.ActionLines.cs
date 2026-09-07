using System.Collections.Generic;
using System.Linq;
using SimManagementLib.Pojo;
using SimManagementLib.SimZone;
using Verse;

namespace SimManagementLib.Api
{
    //动作账单批量接口，职责是按动作和子业务标识提交一次多条真实成本明细。
    public static partial class SimShopFinanceApi
    {
        //验证整批明细并逐项幂等登记，职责是避免重试重复追加应付款。
        public static SimApiResult QueueActionOrderCharges(Pawn customer, Zone_Shop shop, CustomerActionOrder order,
            IList<ActionOrderChargeLine> lines)
        {
            if (customer == null || shop == null || order == null || Manager == null
                || !ReferenceEquals(order, SimShopCustomerApi.GetActionOrder(order.orderId))
                || order.customerThingId != customer.thingIDNumber || order.shopZoneId != shop.ID
                || SimShopCustomerApi.GetCurrentShop(customer) != shop)
                return SimApiResult.Fail("动作订单与顾客或商店不匹配");
            if (lines == null || lines.Any(l => l == null || l.key.NullOrEmpty() || l.count <= 0
                || l.amount < 0 || l.cost < 0 || float.IsNaN(l.amount) || float.IsInfinity(l.amount)
                || float.IsNaN(l.cost) || float.IsInfinity(l.cost))
                || lines.Select(l => l.key).Distinct().Count() != lines.Count)
                return SimApiResult.Fail("子订单账单标识、数量、金额或成本无效");
            foreach (var line in lines)
            {
                var saved = order.chargeLines.FirstOrDefault(l => l.key == line.key);
                if (saved != null && (saved.amount != line.amount || saved.count != line.count || saved.cost != line.cost))
                    return SimApiResult.Fail("已经登记的子订单账单不能改变：" + line.key);
            }
            foreach (var line in lines)
            {
                var saved = order.chargeLines.FirstOrDefault(l => l.key == line.key);
                if (saved == null)
                {
                    saved = new ActionOrderChargeLine { key = line.key, label = line.label, count = line.count,
                        amount = line.amount, cost = line.cost };
                    order.chargeLines.Add(saved);
                }
                if (!saved.billRegistered)
                {
                    if (saved.amount > 0)
                    {
                        var result = SimShopCustomerApi.AddCustomerBill(customer, saved.amount);
                        if (!result.success) return result;
                    }
                    order.billAmount += saved.amount;
                    saved.billRegistered = true;
                }
                if (!saved.financeRegistered)
                {
                    Manager.QueueCustomLine(customer, shop, new FinanceLineItem { lineType = FinanceLineTypes.Service,
                        defName = ActionOrderChargeKey(order.orderId) + "/" + saved.key, label = saved.label,
                        count = saved.count, amount = saved.amount, cost = saved.cost });
                    saved.financeRegistered = true;
                }
            }
            order.billRegistered = order.financeRegistered = true;
            return SimApiResult.Success();
        }
    }
}
