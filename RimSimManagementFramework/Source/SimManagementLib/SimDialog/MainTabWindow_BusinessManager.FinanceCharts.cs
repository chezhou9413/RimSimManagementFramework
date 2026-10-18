using System.Linq;
using RimWorld;
using SimManagementLib.GameComp;
using SimManagementLib.Tool;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    //财务看板统一时间口径和颜色，金额细节通过悬停查看。
    public partial class MainTabWindow_BusinessManager
    {
        private int financeTrendDays = 14;
        private static float FinanceChartsHeight => 530f + Text.LineHeightOf(GameFont.Small) * 2f;
        private static readonly Color CProfit = new Color(0.35f, 0.80f, 0.45f);
        private static readonly Color CLoss = new Color(0.92f, 0.38f, 0.36f);
        private static readonly Color CService = new Color(0.86f, 0.56f, 0.28f);
        private static readonly Color CCombo = new Color(0.74f, 0.52f, 0.92f);
        private static readonly Color CGrid = new Color(1f, 1f, 1f, 0.08f);

        //绘制利润 K 线、每日收入柱、店铺排名和收入构成。
        private float DrawFinanceCharts(float width, float startY, GameComponent_ShopFinanceManager finance)
        {
            Rect panel = new Rect(0f, startY, width, FinanceChartsHeight);
            float row = Mathf.Max(32f, Text.LineHeightOf(GameFont.Small) + 8f);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(new Rect(8f, startY, width - 300f, row), SimTranslation.T("RSMF.Business.Finance.Candle.Title"));
            DrawFinancePeriodButtons(new Rect(width - 284f, startY, 276f, row));
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = true;
            string note = SimTranslation.T("RSMF.Business.Finance.Candle.Note");
            float noteHeight = Text.CalcHeight(note, width - 16f);
            GUI.color = CDim;
            Widgets.Label(new Rect(8f, startY + row + 4f, width - 16f, noteHeight), note);
            float top = startY + row + noteHeight + 12f;
            float rankingHeight = 44f + 4f * Mathf.Max(26f, Text.LineHeightOf(GameFont.Tiny) + 6f);
            Rect candles = new Rect(0f, top, width, panel.yMax - top - rankingHeight - 12f);
            DrawProfitCandles(candles, finance);
            float half = (width - 10f) / 2f;
            DrawShopRevenueBars(new Rect(0f, candles.yMax + 10f, half, rankingHeight), finance);
            DrawRevenueMixBar(new Rect(half + 10f, candles.yMax + 10f, half, rankingHeight), finance);
            ResetText();
            return panel.yMax + 8f;
        }

        //切换最近经营日数量，图表与顾问使用相同周期。
        private void DrawFinancePeriodButtons(Rect rect)
        {
            int[] ranges = { 7, 14, 30, 60 };
            float width = (rect.width - 18f) / ranges.Length;
            for (int i = 0; i < ranges.Length; i++)
            {
                string label = SimTranslation.T("RSMF.Business.Finance.Period", ranges[i].Named("days"));
                if (SimUiStyle.DrawTabButton(new Rect(rect.x + i * (width + 6f), rect.y, width, rect.height), label, financeTrendDays == ranges[i], CDim))
                    financeTrendDays = ranges[i];
            }
        }

        //展示累计实收排名，明确与按日图表的统计范围不同。
        private void DrawShopRevenueBars(Rect rect, GameComponent_ShopFinanceManager finance)
        {
            DrawChartFrame(rect, SimTranslation.T("RSMF.Business.Finance.Candle.ShopRank"));
            var values = finance.ShopRevenue.OrderByDescending(pair => pair.Value).Where(pair => pair.Value > 0).Take(4).ToList();
            if (values.Count == 0) { DrawChartNoData(rect); return; }
            float row = Mathf.Max(26f, Text.LineHeightOf(GameFont.Tiny) + 6f);
            float labelWidth = rect.width * 0.38f;
            float barWidth = rect.width - labelWidth - 100f;
            for (int i = 0; i < values.Count; i++)
            {
                float y = rect.y + 36f + i * row;
                string label = finance.GetShopLabel(values[i].Key);
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = Color.white;
                Rect nameRect = new Rect(rect.x + 10f, y, labelWidth - 16f, row);
                Widgets.Label(nameRect, label.Truncate(nameRect.width));
                TooltipHandler.TipRegion(nameRect, label);
                Rect bar = new Rect(rect.x + labelWidth, y + 8f, barWidth, row - 16f);
                Widgets.DrawBoxSolid(bar, CGrid);
                Widgets.DrawBoxSolid(new Rect(bar.x, bar.y, bar.width * values[i].Value / values[0].Value, bar.height), CAccent);
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(new Rect(bar.xMax + 6f, y, 80f, row), FormatMoney(values[i].Value));
            }
        }

        //用总宽度计算占比并纳入扩展账单，避免后续色块重复缩小。
        private void DrawRevenueMixBar(Rect rect, GameComponent_ShopFinanceManager finance)
        {
            DrawChartFrame(rect, SimTranslation.T("RSMF.Business.Finance.Candle.Mix"));
            float[] values = { finance.ProductRevenues.Values.Sum(), finance.ComboRevenues.Values.Sum(), finance.ServiceRevenues.Values.Sum(), finance.CustomLineRevenues.Values.Sum() };
            string[] keys = { "Product", "Combo", "Service", "Other" };
            Color[] colors = { CAccent, CCombo, CService, CDim };
            float total = values.Sum();
            if (total <= 0) { DrawChartNoData(rect); return; }
            Rect bar = new Rect(rect.x + 10f, rect.y + 36f, rect.width - 20f, 12f);
            float x = bar.x;
            for (int i = 0; i < values.Length; i++)
            {
                float segment = bar.width * values[i] / total;
                Widgets.DrawBoxSolid(new Rect(x, bar.y, segment, bar.height), colors[i]);
                x += segment;
            }
            float row = Mathf.Max(26f, Text.LineHeightOf(GameFont.Tiny) + 6f);
            for (int i = 0; i < values.Length; i++)
            {
                Rect line = new Rect(rect.x + 12f, bar.yMax + 6f + i / 2 * row, (rect.width - 30f) / 2f, row);
                line.x += i % 2 * (line.width + 6f);
                GUI.color = colors[i];
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                string label = SimTranslation.T("RSMF.Business.Finance.Chart." + keys[i]) + " " + (values[i] / total * 100f).ToString("F0") + "%";
                Widgets.Label(line, label.Truncate(line.width));
                TooltipHandler.TipRegion(line, label + " · " + FormatMoney(values[i]));
            }
        }

        //绘制图表边框和标题。
        private static void DrawChartFrame(Rect rect, string title)
        {
            Widgets.DrawBoxSolid(rect, new Color(1f, 1f, 1f, 0.03f));
            DrawBorder(rect, CGrid);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 4f, rect.width - 20f, Mathf.Max(28f, Text.LineHeightOf(GameFont.Tiny) + 4f)), title);
        }

        //在没有可绘制记录时显示占位。
        private static void DrawChartNoData(Rect rect)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = CDim;
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 34f, rect.width - 20f, rect.height - 44f), SimTranslation.T("RSMF.Common.NoData"));
            ResetText();
        }

        //统一显示游戏货币金额。
        private static string FormatMoney(float value) => SimTranslation.T("RSMF.Business.Finance.Chart.Value", value.ToString("F0").Named("value"));
    }
}
