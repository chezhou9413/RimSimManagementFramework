using RimWorld;
using SimManagementLib.GameComp;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    public partial class MainTabWindow_BusinessManager
    {
        private const float FinanceChartsHeight = 352f;
        private const int FinanceTrendDays = 14;

        private static readonly Color CProfit = new Color(0.35f, 0.80f, 0.45f, 1f);
        private static readonly Color CService = new Color(0.86f, 0.56f, 0.28f, 1f);
        private static readonly Color CCombo = new Color(0.74f, 0.52f, 0.92f, 1f);
        private static readonly Color CGrid = new Color(1f, 1f, 1f, 0.08f);

        /// <summary>
        /// 绘制财务图表总览，负责把趋势、商店对比和收入构成放在账目总览顶部。
        /// </summary>
        private float DrawFinanceCharts(float width, float startY, GameComponent_ShopFinanceManager finance)
        {
            Rect panel = new Rect(0f, startY, width, FinanceChartsHeight);
            Widgets.DrawBoxSolid(panel, new Color(0f, 0f, 0f, 0.16f));
            DrawBorder(panel, new Color(1f, 1f, 1f, 0.08f));

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            float titleHeight = Mathf.Max(28f, Text.LineHeightOf(GameFont.Small) + 8f);
            Widgets.Label(new Rect(panel.x + 10f, panel.y + 2f, panel.width - 20f, titleHeight), SimTranslation.T("RSMF.Business.Finance.Chart.Title"));

            Rect body = new Rect(panel.x + 10f, panel.y + titleHeight + 4f, panel.width - 20f, panel.height - titleHeight - 14f);
            float gap = 10f;
            float leftWidth = Mathf.Max(360f, body.width * 0.58f);
            Rect trendRect = new Rect(body.x, body.y, leftWidth, body.height);
            Rect rightRect = new Rect(trendRect.xMax + gap, body.y, Mathf.Max(260f, body.width - leftWidth - gap), body.height);
            Rect shopRect = new Rect(rightRect.x, rightRect.y, rightRect.width, (rightRect.height - gap) * 0.52f);
            Rect mixRect = new Rect(rightRect.x, shopRect.yMax + gap, rightRect.width, rightRect.yMax - shopRect.yMax - gap);

            DrawDailyStepChart(trendRect, finance);
            DrawShopRevenueBars(shopRect, finance);
            DrawRevenueMixBar(mixRect, finance);
            ResetText();
            return panel.yMax + 8f;
        }

        /// <summary>
        /// 绘制每日收入和利润阶梯趋势图，负责让玩家快速对比最近多天变化。
        /// </summary>
        private void DrawDailyStepChart(Rect rect, GameComponent_ShopFinanceManager finance)
        {
            DrawChartFrame(rect, SimTranslation.T("RSMF.Business.Finance.Chart.DailyTrend"));
            List<FinanceDayPoint> points = BuildDailyPoints(finance, FinanceTrendDays);
            if (points.NullOrEmpty())
            {
                DrawChartNoData(rect);
                return;
            }

            Rect plot = new Rect(rect.x + 42f, rect.y + 42f, rect.width - 56f, rect.height - 72f);
            float maxValue = Mathf.Max(1f, points.Max(p => Mathf.Max(p.revenue, p.profit)));
            DrawFinanceGrid(plot);
            DrawStepSeries(plot, points.Select(p => p.revenue).ToList(), maxValue, CAccent);
            DrawStepSeries(plot, points.Select(p => p.profit).ToList(), maxValue, CProfit);

            DrawTrendAxisLabels(rect, plot, points, maxValue);
            DrawChartLegend(new Rect(plot.x, rect.y + 24f, plot.width, 18f),
                SimTranslation.T("RSMF.Business.Finance.Chart.Revenue"), CAccent,
                SimTranslation.T("RSMF.Business.Finance.Chart.Profit"), CProfit);
        }

        /// <summary>
        /// 绘制商店收入横向柱状图，负责展示不同店铺或售货机的收入对比。
        /// </summary>
        private void DrawShopRevenueBars(Rect rect, GameComponent_ShopFinanceManager finance)
        {
            DrawChartFrame(rect, SimTranslation.T("RSMF.Business.Finance.Chart.ShopCompare"));
            List<FinanceChartValue> values = finance.ShopRevenue
                .OrderByDescending(kv => kv.Value)
                .Take(6)
                .Select(kv => new FinanceChartValue(finance.GetShopLabel(kv.Key), kv.Value))
                .Where(v => v.value > 0.001f)
                .ToList();

            DrawHorizontalBars(new Rect(rect.x + 10f, rect.y + 36f, rect.width - 20f, rect.height - 46f), values, CAccent);
        }

        /// <summary>
        /// 绘制收入构成条，负责展示商品、套餐和服务收入占比。
        /// </summary>
        private void DrawRevenueMixBar(Rect rect, GameComponent_ShopFinanceManager finance)
        {
            DrawChartFrame(rect, SimTranslation.T("RSMF.Business.Finance.Chart.RevenueMix"));
            float product = finance.ProductRevenues.Values.Sum();
            float combo = finance.ComboRevenues.Values.Sum();
            float service = finance.ServiceRevenues.Values.Sum();
            float total = product + combo + service;
            if (total <= 0.001f)
            {
                DrawChartNoData(rect);
                return;
            }

            Rect fullBar = new Rect(rect.x + 12f, rect.y + 38f, rect.width - 24f, 24f);
            Rect remainingBar = fullBar;
            DrawStackSegment(ref remainingBar, product / total, CAccent);
            DrawStackSegment(ref remainingBar, combo / total, CCombo);
            DrawStackSegment(ref remainingBar, service / total, CService);
            DrawBorder(fullBar, new Color(1f, 1f, 1f, 0.12f));

            float labelTop = fullBar.yMax + 8f;
            float labelAreaHeight = Mathf.Max(Text.LineHeightOf(GameFont.Tiny) + 4f, rect.yMax - labelTop - 8f);
            float labelRowHeight = Mathf.Max(Text.LineHeightOf(GameFont.Tiny) + 2f, labelAreaHeight / 3f);
            DrawMixLabel(new Rect(rect.x + 12f, labelTop, rect.width - 24f, labelRowHeight), SimTranslation.T("RSMF.Business.Finance.Chart.Product"), product, total, CAccent);
            DrawMixLabel(new Rect(rect.x + 12f, labelTop + labelRowHeight, rect.width - 24f, labelRowHeight), SimTranslation.T("RSMF.Business.Finance.Chart.Combo"), combo, total, CCombo);
            DrawMixLabel(new Rect(rect.x + 12f, labelTop + labelRowHeight * 2f, rect.width - 24f, labelRowHeight), SimTranslation.T("RSMF.Business.Finance.Chart.Service"), service, total, CService);
        }

        /// <summary>
        /// 绘制图表外框和标题，负责统一财务图表的面板样式。
        /// </summary>
        private static void DrawChartFrame(Rect rect, string title)
        {
            Widgets.DrawBoxSolid(rect, new Color(1f, 1f, 1f, 0.03f));
            DrawBorder(rect, new Color(1f, 1f, 1f, 0.08f));
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 6f, rect.width - 20f, Mathf.Max(22f, Text.LineHeightOf(GameFont.Tiny) + 4f)), title);
        }

        /// <summary>
        /// 绘制图表无数据状态，负责在没有账目时给出清晰占位。
        /// </summary>
        private static void DrawChartNoData(Rect rect)
        {
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = CDim;
            Widgets.Label(rect.ContractedBy(10f), SimTranslation.T("RSMF.Common.NoData"));
            ResetText();
        }

        /// <summary>
        /// 绘制图表网格，负责给阶梯图提供读数参考线。
        /// </summary>
        private static void DrawFinanceGrid(Rect plot)
        {
            for (int i = 0; i <= 4; i++)
            {
                float y = Mathf.Lerp(plot.yMax, plot.y, i / 4f);
                Widgets.DrawBoxSolid(new Rect(plot.x, y, plot.width, 1f), CGrid);
            }
        }

        /// <summary>
        /// 绘制阶梯线序列，负责用水平线和垂直线表达每日数据变化。
        /// </summary>
        private static void DrawStepSeries(Rect plot, List<float> values, float maxValue, Color color)
        {
            if (values.NullOrEmpty())
                return;

            float stepX = values.Count <= 1 ? plot.width : plot.width / (values.Count - 1);
            for (int i = 0; i < values.Count; i++)
            {
                float x = plot.x + stepX * i;
                float y = ValueToY(plot, values[i], maxValue);
                if (i < values.Count - 1)
                {
                    float nextX = plot.x + stepX * (i + 1);
                    float nextY = ValueToY(plot, values[i + 1], maxValue);
                    DrawLineRect(new Vector2(x, y), new Vector2(nextX, y), color);
                    DrawLineRect(new Vector2(nextX, y), new Vector2(nextX, nextY), color);
                }

                Widgets.DrawBoxSolid(new Rect(x - 2f, y - 2f, 4f, 4f), color);
            }
        }

        /// <summary>
        /// 绘制水平或垂直细线，负责避免依赖旋转纹理绘制普通直线。
        /// </summary>
        private static void DrawLineRect(Vector2 a, Vector2 b, Color color)
        {
            if (Mathf.Abs(a.y - b.y) <= Mathf.Abs(a.x - b.x))
            {
                Widgets.DrawBoxSolid(new Rect(Mathf.Min(a.x, b.x), a.y - 1f, Mathf.Abs(a.x - b.x) + 1f, 2f), color);
            }
            else
            {
                Widgets.DrawBoxSolid(new Rect(a.x - 1f, Mathf.Min(a.y, b.y), 2f, Mathf.Abs(a.y - b.y) + 1f), color);
            }
        }

        /// <summary>
        /// 把数值映射到绘图区 Y 坐标，负责统一收入和利润的纵轴比例。
        /// </summary>
        private static float ValueToY(Rect plot, float value, float maxValue)
        {
            return Mathf.Lerp(plot.yMax, plot.y, Mathf.Clamp01(value / Mathf.Max(1f, maxValue)));
        }

        /// <summary>
        /// 绘制趋势图坐标轴文本，负责显示最大值、起止天数和最近天数范围。
        /// </summary>
        private static void DrawTrendAxisLabels(Rect rect, Rect plot, List<FinanceDayPoint> points, float maxValue)
        {
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = CDim;
            Widgets.Label(new Rect(rect.x + 6f, plot.y - 8f, 32f, 18f), FormatMoney(maxValue));
            Widgets.Label(new Rect(rect.x + 6f, plot.yMax - 10f, 32f, 18f), FormatMoney(0f));

            Text.Anchor = TextAnchor.UpperLeft;
            if (!points.NullOrEmpty())
            {
                Widgets.Label(new Rect(plot.x, plot.yMax + 6f, plot.width * 0.5f, 18f),
                    SimTranslation.T("RSMF.Business.Finance.Chart.DayShort", points.First().day.Named("day")));
                Text.Anchor = TextAnchor.UpperRight;
                Widgets.Label(new Rect(plot.x + plot.width * 0.5f, plot.yMax + 6f, plot.width * 0.5f, 18f),
                    SimTranslation.T("RSMF.Business.Finance.Chart.DayShort", points.Last().day.Named("day")));
            }

            ResetText();
        }

        /// <summary>
        /// 绘制图例，负责标识趋势图中的两条数据线。
        /// </summary>
        private static void DrawChartLegend(Rect rect, string leftLabel, Color leftColor, string rightLabel, Color rightColor)
        {
            DrawLegendEntry(new Rect(rect.x, rect.y, rect.width * 0.45f, rect.height), leftLabel, leftColor);
            DrawLegendEntry(new Rect(rect.x + rect.width * 0.45f, rect.y, rect.width * 0.45f, rect.height), rightLabel, rightColor);
        }

        /// <summary>
        /// 绘制单个图例项，负责用色块和文本说明一组数据。
        /// </summary>
        private static void DrawLegendEntry(Rect rect, string label, Color color)
        {
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y + 5f, 18f, 6f), color);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = CDim;
            Widgets.Label(new Rect(rect.x + 24f, rect.y, rect.width - 24f, Mathf.Max(rect.height, Text.LineHeightOf(GameFont.Tiny) + 4f)), label);
        }

        /// <summary>
        /// 绘制横向柱状图，负责显示排名型财务数据。
        /// </summary>
        private void DrawHorizontalBars(Rect rect, List<FinanceChartValue> values, Color color)
        {
            if (values.NullOrEmpty())
            {
                DrawChartNoData(rect);
                return;
            }

            float maxValue = Mathf.Max(1f, values.Max(v => v.value));
            float rowHeight = Mathf.Max(24f, Text.LineHeightOf(GameFont.Tiny) + 8f);
            float labelWidth = Mathf.Min(132f, rect.width * 0.36f);
            for (int i = 0; i < values.Count; i++)
            {
                FinanceChartValue item = values[i];
                float y = rect.y + i * rowHeight;
                if (y + rowHeight > rect.yMax)
                    break;

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = i % 2 == 0 ? Color.white : CDim;
                Widgets.Label(new Rect(rect.x, y, labelWidth - 6f, rowHeight), item.label.Truncate(labelWidth - 8f));

                Rect bar = new Rect(rect.x + labelWidth, y + 6f, Mathf.Max(20f, rect.width - labelWidth - 74f), rowHeight - 12f);
                Widgets.DrawBoxSolid(bar, new Color(1f, 1f, 1f, 0.06f));
                Widgets.DrawBoxSolid(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(item.value / maxValue), bar.height), color);

                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = Color.white;
                Widgets.Label(new Rect(bar.xMax + 6f, y, 68f, rowHeight), FormatMoney(item.value));
            }

            ResetText();
        }

        /// <summary>
        /// 绘制堆叠条中的一个片段，负责按占比推进剩余绘制区域。
        /// </summary>
        private static void DrawStackSegment(ref Rect remain, float ratio, Color color)
        {
            float width = remain.width * Mathf.Clamp01(ratio);
            if (width > 0.5f)
                Widgets.DrawBoxSolid(new Rect(remain.x, remain.y, width, remain.height), color);
            remain = new Rect(remain.x + width, remain.y, Mathf.Max(0f, remain.width - width), remain.height);
        }

        /// <summary>
        /// 绘制收入构成标签，负责显示构成名称、金额和百分比。
        /// </summary>
        private static void DrawMixLabel(Rect rect, string label, float value, float total, Color color)
        {
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y + 7f, 10f, 6f), color);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = CDim;
            string text = SimTranslation.T("RSMF.Business.Finance.Chart.MixLine",
                label.Named("label"),
                FormatMoney(value).Named("value"),
                (value / Mathf.Max(1f, total) * 100f).ToString("F0").Named("percent"));
            Widgets.Label(new Rect(rect.x + 16f, rect.y, rect.width - 16f, Mathf.Max(rect.height, Text.LineHeightOf(GameFont.Tiny) + 4f)), text);
        }

        /// <summary>
        /// 构造最近多天的趋势点，负责补齐没有收入记录的日期。
        /// </summary>
        private static List<FinanceDayPoint> BuildDailyPoints(GameComponent_ShopFinanceManager finance, int days)
        {
            List<FinanceDayPoint> result = new List<FinanceDayPoint>();
            if (finance == null || days <= 0)
                return result;

            int today = GenDate.DaysPassed;
            int latestDay = today;
            if (finance.DailyRevenue.Keys.Any())
                latestDay = Mathf.Max(latestDay, finance.DailyRevenue.Keys.Max());
            if (finance.DailyProfit.Keys.Any())
                latestDay = Mathf.Max(latestDay, finance.DailyProfit.Keys.Max());

            int firstDay = latestDay - days + 1;
            for (int day = firstDay; day <= latestDay; day++)
            {
                finance.DailyRevenue.TryGetValue(day, out float revenue);
                finance.DailyProfit.TryGetValue(day, out float profit);
                result.Add(new FinanceDayPoint(day, revenue, profit));
            }

            return result.Any(p => p.revenue > 0.001f || p.profit > 0.001f) ? result : new List<FinanceDayPoint>();
        }

        /// <summary>
        /// 格式化财务金额，负责统一图表中的金额显示。
        /// </summary>
        private static string FormatMoney(float value)
        {
            return SimTranslation.T("RSMF.Business.Finance.Chart.Value", value.ToString("F0").Named("value"));
        }

        /// <summary>
        /// 保存单日收入和利润点，负责为趋势图提供轻量数据结构。
        /// </summary>
        private sealed class FinanceDayPoint
        {
            public readonly int day;
            public readonly float revenue;
            public readonly float profit;

            /// <summary>
            /// 初始化单日财务点，负责保存日期、收入和利润。
            /// </summary>
            public FinanceDayPoint(int day, float revenue, float profit)
            {
                this.day = day;
                this.revenue = revenue;
                this.profit = profit;
            }
        }

        /// <summary>
        /// 保存一个图表排名值，负责为横向柱状图提供标签和数值。
        /// </summary>
        private sealed class FinanceChartValue
        {
            public readonly string label;
            public readonly float value;

            /// <summary>
            /// 初始化图表排名值，负责保存显示名称和收入数值。
            /// </summary>
            public FinanceChartValue(string label, float value)
            {
                this.label = string.IsNullOrEmpty(label) ? SimTranslation.T("RSMF.Common.Unknown") : label;
                this.value = value;
            }
        }
    }
}
