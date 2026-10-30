using SimManagementLib.SimThingComp;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Linq;
using Verse;

namespace SimManagementLib.Api
{
    //让住宿、取款手续费和预收票款使用同一现金与营业账本。
    public static partial class SimShopFinanceApi
    {
        //检验直接收款的参数和实际现金容器。
        private static SimApiResult FindImmediateCash(Pawn customer, Zone_Shop shop, Thing receiver,
            string key, int amount, float cost, out ThingComp_CashStorage cash)
        {
            cash = null;
            if (customer == null || shop?.Map == null || customer.Map != shop.Map || key.NullOrEmpty()
                || amount < 0 || cost < 0f || float.IsNaN(cost) || float.IsInfinity(cost) || Manager == null)
                return SimApiResult.Fail("直接收款的顾客、店铺、编号或金额无效");
            receiver = receiver ?? ShopDataUtility.GetCashRegisterSnapshotInZone(shop).FirstOrDefault(register => register.Spawned && !register.Destroyed);
            if (receiver == null || !receiver.Spawned || receiver.Destroyed || receiver.Map != shop.Map)
                return SimApiResult.Fail("商店没有有效收款建筑");
            cash = (receiver as ThingWithComps)?.GetComp<ThingComp_CashStorage>();
            return cash != null ? SimApiResult.Success() : SimApiResult.Fail("收款建筑缺少现金容器");
        }

        //将已经扣除的托管款转为商店收入，不再次扣钱包。
        public static SimApiResult CommitImmediateIncome(Pawn customer, Zone_Shop shop, Thing cashReceiver,
            string transactionKey, string label, int amount, float cost = 0f)
        {
            if (Manager?.HasImmediateTransaction(transactionKey) == true) return SimApiResult.Success();
            var valid = FindImmediateCash(customer, shop, cashReceiver, transactionKey, amount, cost, out var cash);
            if (!valid.success) return valid;
            cash.DepositSilver(amount);
            Manager.RecordImmediateTransaction(customer, shop, transactionKey, label ?? "直接营业收入", amount, cost);
            return SimApiResult.Success();
        }

        //一次完成钱包扣款、现金入库与营业入账。
        public static SimApiResult TryPayImmediate(Pawn customer, Zone_Shop shop, Thing cashReceiver,
            string transactionKey, string label, int amount, float cost = 0f)
        {
            if (Manager?.HasImmediateTransaction(transactionKey) == true) return SimApiResult.Success();
            var valid = FindImmediateCash(customer, shop, cashReceiver, transactionKey, amount, cost, out var cash);
            if (!valid.success) return valid;
            var debit = SimShopCustomerApi.TrySpendCustomerFunds(customer, amount);
            if (!debit.success) return debit;
            cash.DepositSilver(amount);
            Manager.RecordImmediateTransaction(customer, shop, transactionKey, label ?? "直接营业收入", amount, cost);
            return SimApiResult.Success();
        }

        //从真实营业现金退款，并记录负收入；现金不足时保留原业务待处理。
        public static SimApiResult TryRefundImmediate(Pawn customer, Zone_Shop shop, Thing cashReceiver,
            string transactionKey, string label, int amount)
        {
            if (Manager?.HasImmediateTransaction(transactionKey) == true) return SimApiResult.Success();
            var valid = FindImmediateCash(customer, shop, cashReceiver, transactionKey, amount, 0f, out var cash);
            if (!valid.success) return valid;
            if (cash.AvailableForWithdraw < amount) return SimApiResult.Fail("收款建筑现金不足，退款尚未完成");
            int reserved = cash.ReserveWithdrawSilver(amount);
            if (reserved != amount)
            {
                cash.CancelWithdrawReservation(reserved);
                return SimApiResult.Fail("退款现金已被其他取款预约");
            }
            cash.WithdrawReservedSilver(amount);
            SimShopCustomerApi.RefundCustomerFunds(customer, amount);
            Manager.RecordImmediateTransaction(customer, shop, transactionKey, label ?? "营业退款", -amount, 0f);
            return SimApiResult.Success();
        }
    }
}
