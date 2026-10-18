using System.Collections.Generic;
using System.Linq;
using SimManagementLib.Pojo;
using Verse;

namespace SimManagementLib.GameComp
{
    //在成交时增量记录 K 线，避免绘图反复扫描完整账单。
    public partial class GameComponent_ShopFinanceManager
    {
        private Dictionary<int, ShopFinanceCandle> dailyCandles = new Dictionary<int, ShopFinanceCandle>();
        private List<int> candleKeys;
        private List<ShopFinanceCandle> candleValues;
        public IReadOnlyDictionary<int, ShopFinanceCandle> DailyCandles => dailyCandles;
        public int FinanceRevision { get; private set; }
        public float RecordedProfit => dailyProfit.Values.Sum();

        //读写 K 线；没有逐笔记录的旧日期保持缺失，不推测日内高低点。
        private void ExposeCandles()
        {
            Scribe_Collections.Look(ref dailyCandles, "dailyProfitCandles", LookMode.Value, LookMode.Deep, ref candleKeys, ref candleValues);
            if (dailyCandles == null) dailyCandles = new Dictionary<int, ShopFinanceCandle>();
            if (Scribe.mode == LoadSaveMode.PostLoadInit) FinanceRevision++;
        }

        //在当日利润入账之前记录成交，日初累计值只在首笔交易时计算。
        private void RecordProfitCandle(int day, float profit)
        {
            if (!dailyCandles.TryGetValue(day, out ShopFinanceCandle candle))
            {
                float opening = RecordedProfit;
                candle = new ShopFinanceCandle
                {
                    day = day, open = opening, high = opening, low = opening, close = opening,
                    partial = dailyRevenue.ContainsKey(day)
                };
                dailyCandles.Add(day, candle);
            }
            candle.close += profit;
            candle.high = System.Math.Max(candle.high, candle.close);
            candle.low = System.Math.Min(candle.low, candle.close);
            candle.bills++;
            FinanceRevision++;
        }
    }
}
