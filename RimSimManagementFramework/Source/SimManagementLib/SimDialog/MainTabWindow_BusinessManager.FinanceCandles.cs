using System.Collections.Generic;
using System.Linq;
using RimWorld;
using SimManagementLib.GameComp;
using SimManagementLib.Pojo;
using SimManagementLib.Tool;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    //仅在财务变动、跨日或切换周期后重建绘图数据。
    public partial class MainTabWindow_BusinessManager
    {
        private GameComponent_ShopFinanceManager candleFinance;
        private int candleRevision = -1;
        private int candleDay = -1;
        private int candleDays;
        private readonly List<ShopFinanceCandle> candlePoints = new List<ShopFinanceCandle>();

        //绘制真实 OHLC 和每日实收柱，负利润使用同一连续纵轴。
        private void DrawProfitCandles(Rect rect, GameComponent_ShopFinanceManager finance)
        {
            RefreshCandlePoints(finance);
            if (candlePoints.Count == 0) { DrawChartNoData(rect); return; }
            Text.Font = GameFont.Tiny;
            float line = Text.LineHeightOf(GameFont.Tiny) + 4f;
            float min = candlePoints.Min(point => point.low);
            float max = candlePoints.Max(point => point.high);
            float padding = Mathf.Max(1f, (max - min) * 0.08f);
            min -= padding;
            max += padding;
            float axisWidth = Mathf.Max(68f, Mathf.Max(Text.CalcSize(FormatMoney(min)).x, Text.CalcSize(FormatMoney(max)).x) + 12f);
            Rect plot = new Rect(rect.x + axisWidth, rect.y + line, rect.width - axisWidth - 12f, rect.height - line * 3f - 66f);
            Rect volume = new Rect(plot.x, plot.yMax + line, plot.width, 46f);
            float maxRevenue = Mathf.Max(1f, candlePoints.Max(point => finance.DailyRevenue.TryGetValue(point.day, out float value) ? value : 0));
            for (int i = 0; i <= 4; i++)
            {
                float value = Mathf.Lerp(min, max, i / 4f);
                float y = CandleValueY(plot, value, min, max);
                Widgets.DrawBoxSolid(new Rect(plot.x, y, plot.width, 1f), CGrid);
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = CDim;
                Widgets.Label(new Rect(rect.x, y - line / 2f, axisWidth - 8f, line), FormatMoney(value));
            }
            if (min < 0 && max > 0)
                Widgets.DrawBoxSolid(new Rect(plot.x, CandleValueY(plot, 0, min, max), plot.width, 1f), CDim);
            float step = plot.width / candlePoints.Count;
            float bodyWidth = Mathf.Clamp(step * 0.55f, 3f, 24f);
            for (int i = 0; i < candlePoints.Count; i++)
            {
                ShopFinanceCandle point = candlePoints[i];
                float x = plot.x + (i + 0.5f) * step;
                float openY = CandleValueY(plot, point.open, min, max);
                float closeY = CandleValueY(plot, point.close, min, max);
                Color color = point.close > point.open ? CProfit : point.close < point.open ? CLoss : CDim;
                float highY = CandleValueY(plot, point.high, min, max);
                float lowY = CandleValueY(plot, point.low, min, max);
                Widgets.DrawBoxSolid(new Rect(x - 1f, highY, 2f, Mathf.Max(1f, lowY - highY)), color);
                Widgets.DrawBoxSolid(new Rect(x - bodyWidth / 2f, Mathf.Min(openY, closeY), bodyWidth, Mathf.Max(2f, Mathf.Abs(closeY - openY))), color);
                finance.DailyRevenue.TryGetValue(point.day, out float revenue);
                float barHeight = volume.height * revenue / maxRevenue;
                Widgets.DrawBoxSolid(new Rect(x - bodyWidth / 2f, volume.yMax - barHeight, bodyWidth, barHeight), CAccent);
                Rect hit = new Rect(plot.x + i * step, plot.y, step, volume.yMax - plot.y);
                if (Mouse.IsOver(hit))
                {
                    TooltipHandler.TipRegion(hit, BuildCandleTooltip(point, revenue));
                    Widgets.DrawBoxSolid(new Rect(x, plot.y, 1f, volume.yMax - plot.y), new Color(1f, 1f, 1f, 0.35f));
                }
            }
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = CDim;
            Widgets.Label(new Rect(plot.x, rect.y, plot.width, line), SimTranslation.T("RSMF.Business.Finance.Candle.Legend"));
            Widgets.Label(new Rect(plot.x, plot.yMax, plot.width, line), SimTranslation.T("RSMF.Business.Finance.Candle.Volume"));
            Widgets.Label(new Rect(plot.x, volume.yMax + 3f, plot.width / 2f, line), DayLabel(candlePoints[0].day));
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(new Rect(plot.x + plot.width / 2f, volume.yMax + 3f, plot.width / 2f, line), DayLabel(candleDay));
            ResetText();
        }

        //只补齐开始记录后的无成交日期，不推测更早历史的日内高低点。
        private void RefreshCandlePoints(GameComponent_ShopFinanceManager finance)
        {
            int today = GenDate.DaysPassed;
            if (candleFinance == finance && candleRevision == finance.FinanceRevision && candleDay == today && candleDays == financeTrendDays) return;
            candleFinance = finance;
            candleRevision = finance.FinanceRevision;
            candleDay = today;
            candleDays = financeTrendDays;
            candlePoints.Clear();
            if (finance.DailyCandles.Count == 0) return;
            int first = Mathf.Max(finance.DailyCandles.Keys.Min(), Mathf.Max(0, today - financeTrendDays + 1));
            ShopFinanceCandle previous = finance.DailyCandles.Values.Where(point => point.day < first).OrderByDescending(point => point.day).FirstOrDefault();
            float previousClose = previous?.close ?? 0f;
            for (int day = first; day <= today; day++)
            {
                if (finance.DailyCandles.TryGetValue(day, out ShopFinanceCandle point))
                {
                    candlePoints.Add(point);
                    previousClose = point.close;
                }
                else
                    candlePoints.Add(new ShopFinanceCandle { day = day, open = previousClose, high = previousClose, low = previousClose, close = previousClose });
            }
        }

        //悬停提供开高低收、成交及采集完整性。
        private static string BuildCandleTooltip(ShopFinanceCandle point, float revenue)
        {
            string detail = SimTranslation.T("RSMF.Business.Finance.Candle.Tooltip", point.day.Named("day"),
                FormatMoney(point.open).Named("open"), FormatMoney(point.high).Named("high"),
                FormatMoney(point.low).Named("low"), FormatMoney(point.close).Named("close"),
                FormatMoney(point.close - point.open).Named("profit"), FormatMoney(revenue).Named("revenue"), point.bills.Named("bills"));
            if (point.day == GenDate.DaysPassed) detail += "\n" + SimTranslation.T("RSMF.Business.Finance.Candle.Live");
            if (point.partial) detail += "\n" + SimTranslation.T("RSMF.Business.Finance.Candle.Partial");
            return detail;
        }

        //将含负值的累计利润映射到坐标。
        private static float CandleValueY(Rect plot, float value, float min, float max) => Mathf.Lerp(plot.yMax, plot.y, (value - min) / (max - min));

        //统一经营日标签。
        private static string DayLabel(int day) => SimTranslation.T("RSMF.Business.Finance.Chart.DayShort", day.Named("day"));
    }
}
