using SimManagementLib.Pojo;
using SimManagementLib.SimZone;
using System.Collections.Generic;
using Verse;
using RimWorld;

namespace SimManagementLib.GameComp
{
    //记录不经过购物车的营业收入和退款，使用业务编号避免重复入账。
    public partial class GameComponent_ShopFinanceManager
    {
        private HashSet<string> immediateTransactions = new HashSet<string>();

        //读取直接收款的完成记录。
        public bool HasImmediateTransaction(string key) => immediateTransactions.Contains(key);

        //保存业务收款的幂等标识。
        private void ExposeImmediateTransactions()
        {
            Scribe_Collections.Look(ref immediateTransactions, "immediateTransactions", LookMode.Value);
            if (immediateTransactions == null) immediateTransactions = new HashSet<string>();
        }

        //同步更新账本、每日利润和店铺营业统计，不触碰该顾客的其他待付账单。
        internal void RecordImmediateTransaction(Pawn customer, Zone_Shop shop, string key, string label, int amount, float cost)
        {
            if (!immediateTransactions.Add(key)) return;
            int day = GenDate.DaysPassed;
            var line = new FinanceLineItem { lineType = FinanceLineTypes.Service, defName = label,
                label = label, count = 1, amount = amount, cost = cost };
            billRecords.Add(new FinanceBillRecord { tickAbs = Find.TickManager.TicksAbs, gameDay = day,
                zoneId = shop.ID, zoneLabel = shop.label, customerName = customer.LabelShortCap,
                paidSilver = amount, lines = new List<FinanceLineItem> { line } });
            totalIncome += amount;
            AddFloat(dailyRevenue, day, amount);
            AddFloat(dailyProfit, day, amount - cost);
            AddFloat(serviceRevenues, label, amount);
            AddInt(serviceSoldCounts, label, amount >= 0 ? 1 : -1);
            var state = GetOrCreateShopState(shop.ID, shop.label);
            state.revenue += amount;
            state.profit += amount - cost;
            RecordProfitCandle(day, amount - cost);
            TrimBillRecordsIfNeeded();
        }
    }
}
