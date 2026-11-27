using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using SimManagementLib.Api;
using SimManagementLib.SimMapComp;
using SimManagementLib.SimThingComp;
using SimManagementLib.Tool;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    //组件货柜的清单编辑视图，负责筛选、排序、批量操作和逐行价格与补货编辑。
    internal sealed partial class Dialog_StorageSales
    {
        private const float ScrollbarW = 18f;
        private const float ColGap = 8f;
        private const float RowPad = 8f;
        private const float CheckColW = 30f;
        private const float IconSize = 32f;
        private const float StockColW = 92f;
        private const float PriceColW = 122f;
        private const float TargetColW = 118f;
        private const float ThresholdColW = 78f;
        private const float ActionColW = 32f;

        private string manageSearch = "";
        private StockFilter stockFilter = StockFilter.All;
        private SortMode sortMode = SortMode.Default;
        private List<ThingDef> manageRows = new();
        private Vector2 manageScroll;

        //列布局结果，确保表头和数据行完全对齐。
        private readonly struct ManageColumns
        {
            public readonly Rect Check;
            public readonly Rect Icon;
            public readonly Rect Name;
            public readonly Rect Stock;
            public readonly Rect Price;
            public readonly Rect Target;
            public readonly Rect Threshold;
            public readonly Rect Action;

            //按左右固定列宽和弹性名称列切分单行区域。
            public ManageColumns(Rect row)
            {
                float lx = row.x + RowPad;
                Check = new Rect(lx, row.y, CheckColW, row.height);
                lx += CheckColW + ColGap;
                Icon = new Rect(lx, row.y, IconSize, row.height);
                lx += IconSize + ColGap;

                float rx = row.xMax - RowPad;
                rx -= ActionColW;
                Action = new Rect(rx, row.y, ActionColW, row.height);
                rx -= ColGap;

                rx -= ThresholdColW;
                Threshold = new Rect(rx, row.y, ThresholdColW, row.height);
                rx -= ColGap;

                rx -= TargetColW;
                Target = new Rect(rx, row.y, TargetColW, row.height);
                rx -= ColGap;

                rx -= PriceColW;
                Price = new Rect(rx, row.y, PriceColW, row.height);
                rx -= ColGap;

                rx -= StockColW;
                Stock = new Rect(rx, row.y, StockColW, row.height);
                rx -= ColGap;

                Name = new Rect(lx, row.y, Mathf.Max(40f, rx - lx), row.height);
            }
        }

        //按当前状态筛选、搜索词和排序模式重建货柜清单行。
        private void RebuildManageRows()
        {
            IEnumerable<ThingDef> query = draft.Keys.Where(sales.SalesProps.AllowsListing);
            query = stockFilter switch
            {
                StockFilter.InStock => query.Where(d => stock.GetValueOrDefault(d) > 0),
                StockFilter.Restocking => query.Where(d => draft[d].count > 0),
                StockFilter.Paused => query.Where(d => !draft[d].enabled),
                _ => query
            };
            if (!manageSearch.NullOrEmpty())
                query = query.Where(d => MatchesSearch(d, manageSearch));

            manageRows = sortMode switch
            {
                SortMode.StockDesc => query.OrderByDescending(d => stock.GetValueOrDefault(d)).ThenBy(d => d.label).ToList(),
                SortMode.NameAsc => query.OrderBy(d => d.label).ToList(),
                SortMode.PriceDesc => query.OrderByDescending(d => EffectiveDisplayPrice(d)).ThenBy(d => d.label).ToList(),
                SortMode.TargetDesc => query.OrderByDescending(d => draft[d].count).ThenBy(d => d.label).ToList(),
                _ => query.OrderByDescending(d => stock.GetValueOrDefault(d) > 0)
                    .ThenByDescending(d => draft[d].enabled)
                    .ThenByDescending(d => draft[d].count > 0)
                    .ThenBy(d => d.label).ToList()
            };
        }

        //用于排序和展示的参考单价，0 价回退到定义基础市价。
        private float EffectiveDisplayPrice(ThingDef def)
        {
            float configured = draft.TryGetValue(def, out GoodsItemData rule) ? rule.price : 0f;
            return configured > 0f ? configured : def.BaseMarketValue;
        }

        //在模式栏右侧绘制一键筛选按钮，窗口过窄时自动跳过避免重叠。
        private void DrawStockFilterChips(Rect rect)
        {
            (StockFilter filter, string key)[] items =
            {
                (StockFilter.All, "RSMF.StorageSales.Filter.All"),
                (StockFilter.InStock, "RSMF.StorageSales.Filter.InStock"),
                (StockFilter.Restocking, "RSMF.StorageSales.Filter.Restocking"),
                (StockFilter.Paused, "RSMF.StorageSales.Filter.Paused")
            };
            float totalW = 0f;
            float[] widths = new float[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                widths[i] = Mathf.Max(56f, Text.CalcSize(SimTranslation.T(items[i].key)).x + 18f);
                totalW += widths[i] + (i > 0 ? 4f : 0f);
            }
            if (totalW > rect.width) return;

            float cx = rect.xMax - totalW;
            for (int i = 0; i < items.Length; i++)
            {
                if (ShopUiVisualUtility.DrawTabButton(new Rect(cx, rect.y, widths[i], rect.height),
                    SimTranslation.T(items[i].key), stockFilter == items[i].filter, ShopUiVisualUtility.MutedText))
                {
                    stockFilter = items[i].filter;
                    manageScroll = Vector2.zero;
                    RebuildManageRows();
                }
                cx += widths[i] + 4f;
            }
        }

        //绘制搜索栏、排序/批量操作菜单以及带表头的虚拟滚动清单。
        private void DrawManageBody(Rect rect)
        {
            float controlH = ShopUiVisualUtility.ControlHeight();
            string sortLabel = SimTranslation.T("RSMF.StorageSales.Sort.Label", SortLabel(sortMode).Named("mode"));
            string batchLabel = SimTranslation.T("RSMF.StorageSales.Batch.Button");
            float sortW = Mathf.Max(118f, Text.CalcSize(sortLabel).x + 22f);
            float batchW = Mathf.Max(92f, Text.CalcSize(batchLabel).x + 22f);

            Rect batchRect = new Rect(rect.xMax - batchW, rect.y, batchW, controlH);
            Rect sortRect = new Rect(batchRect.x - sortW - 6f, rect.y, sortW, controlH);
            Rect searchRect = new Rect(rect.x, rect.y, Mathf.Max(120f, sortRect.x - rect.x - 8f), controlH);

            string nextSearch = ShopUiVisualUtility.DrawSearchField(searchRect, manageSearch);
            if (nextSearch != manageSearch)
            {
                manageSearch = nextSearch;
                manageScroll = Vector2.zero;
                RebuildManageRows();
            }
            if (ShopUiVisualUtility.DrawSecondaryButton(sortRect, sortLabel)) OpenSortMenu();
            if (ShopUiVisualUtility.DrawSecondaryButton(batchRect, batchLabel, manageRows.Count > 0)) OpenBatchMenu();

            Rect tableFrame = new Rect(rect.x, rect.y + controlH + 8f, rect.width, Mathf.Max(0f, rect.height - controlH - 8f));
            ShopUiVisualUtility.DrawSection(tableFrame);
            if (tableFrame.height <= controlH + 4f) return;

            float headerH = ShopUiVisualUtility.LineHeight(GameFont.Small) + 10f;
            Rect headerRect = new Rect(tableFrame.x + 1f, tableFrame.y + 1f, tableFrame.width - 2f, headerH);
            ShopUiVisualUtility.DrawTableHeaderBackground(headerRect);
            DrawManageHeader(new Rect(headerRect.x, headerRect.y, headerRect.width - ScrollbarW, headerRect.height));

            Rect listRect = new Rect(tableFrame.x + 1f, headerRect.yMax, tableFrame.width - 2f, Mathf.Max(0f, tableFrame.yMax - headerRect.yMax - 1f));
            if (manageRows.Count == 0)
            {
                ShopUiLayoutUtility.DrawEmptyState(listRect, SimTranslation.T("RSMF.StorageSales.EmptyManage"));
                return;
            }

            float smallH = ShopUiVisualUtility.LineHeight(GameFont.Small);
            float tinyH = ShopUiVisualUtility.LineHeight(GameFont.Tiny);
            float rowH = Mathf.Max(46f, smallH + tinyH + 10f, controlH + 8f);
            float viewW = Mathf.Max(100f, listRect.width - ScrollbarW);
            Rect viewRect = new Rect(0f, 0f, viewW, Mathf.Max(listRect.height, manageRows.Count * rowH));
            manageScroll.y = Mathf.Clamp(manageScroll.y, 0f, Mathf.Max(0f, viewRect.height - listRect.height));

            MapComponent_RestockTaskQueue queue = sales.Host.Map?.GetComponent<MapComponent_RestockTaskQueue>();
            ThingDef removeTarget = null;
            Widgets.BeginScrollView(listRect, ref manageScroll, viewRect);
            try
            {
                int first = Mathf.Max(0, Mathf.FloorToInt(manageScroll.y / rowH) - 1);
                int end = Mathf.Min(manageRows.Count, Mathf.CeilToInt((manageScroll.y + listRect.height) / rowH) + 1);
                for (int i = first; i < end; i++)
                {
                    Rect rowRect = new Rect(0f, i * rowH, viewW, rowH);
                    if (DrawRuleRow(rowRect, manageRows[i], i, queue)) removeTarget = manageRows[i];
                }
            }
            finally { Widgets.EndScrollView(); }

            if (removeTarget != null)
            {
                GUI.FocusControl(null);
                draft.Remove(removeTarget);
                removedRules.Add(removeTarget);
                dirty = true;
                RebuildManageRows();
            }
        }

        //绘制对齐的清单表头标签。
        private void DrawManageHeader(Rect rect)
        {
            var cols = new ManageColumns(rect);
            DrawCenteredLabel(cols.Check, SimTranslation.T("RSMF.StorageSales.Sell"), ShopUiVisualUtility.MutedText, GameFont.Tiny);
            Rect nameHeader = new Rect(cols.Icon.x, rect.y, cols.Name.xMax - cols.Icon.x, rect.height);
            ShopUiVisualUtility.DrawCellLabel(nameHeader, SimTranslation.T("RSMF.StorageSales.Item"), ShopUiVisualUtility.MutedText, GameFont.Tiny);
            DrawCenteredLabel(cols.Stock, SimTranslation.T(listed != null ? "RSMF.StorageSales.ListedStock" : "RSMF.StorageSales.Stock"), ShopUiVisualUtility.MutedText, GameFont.Tiny);
            DrawCenteredLabel(cols.Price, SimTranslation.T("RSMF.StorageSales.Price"), ShopUiVisualUtility.MutedText, GameFont.Tiny);
            DrawCenteredLabel(cols.Target, SimTranslation.T("RSMF.StorageSales.Target"), ShopUiVisualUtility.MutedText, GameFont.Tiny);
            DrawCenteredLabel(cols.Threshold, SimTranslation.T("RSMF.StorageSales.Threshold"), ShopUiVisualUtility.MutedText, GameFont.Tiny);
            DrawCenteredLabel(cols.Action, SimTranslation.T("RSMF.StorageSales.Action"), ShopUiVisualUtility.MutedText, GameFont.Tiny);
        }

        //绘制单条品种规则行；返回 true 表示玩家请求删除该无库存规则。
        private bool DrawRuleRow(Rect row, ThingDef def, int index, MapComponent_RestockTaskQueue queue)
        {
            GoodsItemData rule = draft[def];
            ShopUiVisualUtility.DrawTableRowBackground(row, index, rule.enabled);
            var cols = new ManageColumns(row);

            int stored = stock.GetValueOrDefault(def);
            int pending = queue?.CountPending(sales, def) ?? 0;
            bool hostAllowed = sales.AllowsDef(def);

            //可拖拽勾选的上架开关。
            bool prevEnabled = rule.enabled;
            Widgets.Checkbox(cols.Check.center.x - 12f, cols.Check.center.y - 12f, ref rule.enabled, 24f, paintable: true);
            if (rule.enabled != prevEnabled) dirty = true;

            //物品图标与双行说明。
            Widgets.ThingIcon(new Rect(cols.Icon.x, cols.Icon.center.y - IconSize * 0.5f, IconSize, IconSize), def);
            DrawRuleNameCell(cols.Name, def, rule, stored, pending, hostAllowed);

            //库存与在途数量显示。
            DrawStockCell(cols.Stock, def, rule, stored, pending);

            float fieldH = Mathf.Clamp(ShopUiVisualUtility.ControlHeight() - 4f, ShopUiVisualUtility.LineHeight(GameFont.Small) + 4f, row.height - 8f);
            float fieldY = row.center.y - fieldH * 0.5f;

            //单价输入框与快速市价/定价按钮。
            DrawPriceCell(new Rect(cols.Price.x, fieldY, cols.Price.width, fieldH), def, rule);

            //补货目标步进器。
            DrawTargetCell(new Rect(cols.Target.x, fieldY, cols.Target.width, fieldH), rule);

            //补货阈值输入框。
            DrawThresholdCell(new Rect(cols.Threshold.x, fieldY, cols.Threshold.width, fieldH), rule);

            //无库存品种支持删除规则，有库存品种支持一键恢复默认。
            return DrawActionCell(new Rect(cols.Action.x, fieldY, cols.Action.width, fieldH), rule, stored);
        }

        //绘制物品名称、副标题及完整悬停详情。
        private void DrawRuleNameCell(Rect rect, ThingDef def, GoodsItemData rule, int stored, int pending, bool hostAllowed)
        {
            float smallH = ShopUiVisualUtility.LineHeight(GameFont.Small);
            float tinyH = ShopUiVisualUtility.LineHeight(GameFont.Tiny);
            float topY = rect.center.y - (smallH + tinyH) * 0.5f;

            Color titleColor = rule.enabled ? Color.white : ShopUiVisualUtility.MutedText;
            ShopUiVisualUtility.DrawCellLabel(new Rect(rect.x, topY, rect.width, smallH), def.LabelCap, titleColor, GameFont.Small);

            string valueText = def.BaseMarketValue.ToString("0.#");
            string subtitle;
            Color subColor;
            if (!hostAllowed)
            {
                subtitle = SimTranslation.T("RSMF.StorageSales.HostDisallowed");
                subColor = ShopUiVisualUtility.WarningText;
            }
            else
            {
                subtitle = rule.price > 0f
                    ? SimTranslation.T("RSMF.StorageSales.MetaFixed", valueText.Named("value"))
                    : SimTranslation.T("RSMF.StorageSales.MetaMarket", valueText.Named("value"));
                subColor = ShopUiVisualUtility.MutedText;
            }
            ShopUiVisualUtility.DrawCellLabel(new Rect(rect.x, topY + smallH, rect.width, tinyH), subtitle, subColor, GameFont.Tiny);

            if (Mouse.IsOver(rect))
            {
                string tip = def.LabelCap;
                if (!def.description.NullOrEmpty()) tip += "\n\n" + def.description;
                if (pending > 0) tip += "\n\n" + SimTranslation.T("RSMF.StorageSales.PendingBadge", pending.Named("count"));
                TooltipHandler.TipRegion(rect, tip);
            }
        }

        //按库存健康度着色展示现存、可售及在途补货数。
        private void DrawStockCell(Rect rect, ThingDef def, GoodsItemData rule, int stored, int pending)
        {
            Color color = !rule.enabled
                ? ShopUiVisualUtility.MutedText
                : stored <= 0
                    ? CStockNo
                    : rule.count > 0 && stored <= rule.EffectiveRestockThreshold && stored < rule.count
                        ? CStockLow
                        : CStockOk;
            string main = listed != null ? $"{listed.GetValueOrDefault(def)}/{stored}" : stored.ToString();
            if (pending > 0)
            {
                float smallH = ShopUiVisualUtility.LineHeight(GameFont.Small);
                float tinyH = ShopUiVisualUtility.LineHeight(GameFont.Tiny);
                float topY = rect.center.y - (smallH + tinyH) * 0.5f;
                DrawCenteredLabel(new Rect(rect.x, topY, rect.width, smallH), main, color, GameFont.Small);
                DrawCenteredLabel(new Rect(rect.x, topY + smallH, rect.width, tinyH),
                    SimTranslation.T("RSMF.StorageSales.PendingBadge", pending.Named("count")), ShopUiVisualUtility.Accent, GameFont.Tiny);
            }
            else
            {
                DrawCenteredLabel(rect, main, color, GameFont.Small);
            }
        }

        //绘制单价输入框及市价/固定价快捷切换按钮。
        private void DrawPriceCell(Rect rect, ThingDef def, GoodsItemData rule)
        {
            const float btnW = 42f;
            Rect inputRect = new Rect(rect.x, rect.y, rect.width - btnW - 4f, rect.height);
            Rect btnRect = new Rect(inputRect.xMax + 4f, rect.y, btnW, rect.height);

            rule.priceBuffer ??= rule.price.ToString("0.##");
            float prevPrice = rule.price;
            Widgets.TextFieldNumeric(inputRect, ref rule.price, ref rule.priceBuffer, 0f, 999999f);
            if (!Mathf.Approximately(rule.price, prevPrice)) dirty = true;
            TooltipHandler.TipRegion(inputRect, SimTranslation.T("RSMF.StorageSales.PriceFieldTip",
                def.BaseMarketValue.ToString("0.#").Named("value")));

            bool hasCustom = rule.price > 0f;
            string btnLabel = SimTranslation.T(hasCustom ? "RSMF.StorageSales.PriceMarketBtn" : "RSMF.StorageSales.PriceCustomBtn");
            if (ShopUiVisualUtility.DrawSecondaryButton(btnRect, btnLabel, true, GameFont.Tiny))
            {
                GUI.FocusControl(null);
                rule.price = hasCustom ? 0f : Mathf.Max(1f, Mathf.Round(def.BaseMarketValue));
                rule.priceBuffer = rule.price.ToString("0.##");
                dirty = true;
            }
            TooltipHandler.TipRegion(btnRect, SimTranslation.T(hasCustom ? "RSMF.StorageSales.PriceMarketTip" : "RSMF.StorageSales.PriceCustomTip"));
        }

        //绘制带加减快捷按钮的补货目标输入区，支持 Shift/Ctrl 倍率步进。
        private void DrawTargetCell(Rect rect, GoodsItemData rule)
        {
            const float stepW = 24f;
            Rect minusRect = new Rect(rect.x, rect.y, stepW, rect.height);
            Rect plusRect = new Rect(rect.xMax - stepW, rect.y, stepW, rect.height);
            Rect inputRect = new Rect(minusRect.xMax + 4f, rect.y, plusRect.x - minusRect.xMax - 8f, rect.height);

            int step = Event.current.shift ? 10 : Event.current.control ? 5 : 1;
            int prevCount = rule.count;
            if (ShopUiVisualUtility.DrawSecondaryButton(minusRect, "-", rule.count > 0, GameFont.Small))
            {
                GUI.FocusControl(null);
                rule.count = Math.Max(0, rule.count - step);
                rule.countBuffer = rule.count.ToString();
            }

            rule.countBuffer ??= rule.count.ToString();
            Widgets.TextFieldNumeric(inputRect, ref rule.count, ref rule.countBuffer, 0, 999999);

            if (ShopUiVisualUtility.DrawSecondaryButton(plusRect, "+", true, GameFont.Small))
            {
                GUI.FocusControl(null);
                rule.count = Math.Min(999999, rule.count + step);
                rule.countBuffer = rule.count.ToString();
            }

            if (rule.count != prevCount)
            {
                SyncThresholdAfterTargetChange(rule, prevCount);
                dirty = true;
            }
            TooltipHandler.TipRegion(rect, SimTranslation.T("RSMF.StorageSales.TargetStepTip"));
        }

        //绘制补货阈值输入区，目标为 0 时以短横线占位提示未开启补货。
        private void DrawThresholdCell(Rect rect, GoodsItemData rule)
        {
            if (rule.count <= 0)
            {
                DrawCenteredLabel(rect, "-", ShopUiVisualUtility.MutedText, GameFont.Small);
                TooltipHandler.TipRegion(rect, SimTranslation.T("RSMF.StorageSales.ThresholdDisabledTip"));
                return;
            }
            if (rule.restockThreshold < 0)
                rule.restockThreshold = rule.count;
            rule.restockThresholdBuffer ??= rule.restockThreshold.ToString();
            int prevThreshold = rule.restockThreshold;
            Widgets.TextFieldNumeric(rect, ref rule.restockThreshold, ref rule.restockThresholdBuffer, 0, rule.count);
            if (rule.restockThreshold != prevThreshold) dirty = true;
            TooltipHandler.TipRegion(rect, SimTranslation.T("RSMF.StorageSales.ThresholdFieldTip"));
        }

        //绘制行末移除或恢复默认操作按钮。
        private bool DrawActionCell(Rect rect, GoodsItemData rule, int stored)
        {
            if (stored <= 0)
            {
                TooltipHandler.TipRegion(rect, SimTranslation.T("RSMF.StorageSales.RemoveRuleTip"));
                return ShopUiVisualUtility.DrawDangerButton(rect, "×", true, GameFont.Small);
            }
            bool modified = !rule.enabled || rule.price > 0f || rule.count > 0;
            TooltipHandler.TipRegion(rect, SimTranslation.T("RSMF.StorageSales.ResetRuleTip"));
            if (ShopUiVisualUtility.DrawSecondaryButton(rect, "↺", modified, GameFont.Small))
            {
                GUI.FocusControl(null);
                rule.enabled = true;
                rule.price = 0f;
                rule.count = 0;
                rule.restockThreshold = -1;
                NormalizeRuleBuffers(rule);
                dirty = true;
            }
            return false;
        }

        //返回排序模式的本地化名称。
        private static string SortLabel(SortMode mode) => SimTranslation.T("RSMF.StorageSales.Sort." + mode);

        //按名称或 defName 做不区分大小写的搜索匹配。
        private static bool MatchesSearch(ThingDef def, string query) =>
            (def.label != null && def.label.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
            || def.defName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

        //绘制居中单行文本并在空间不足时省略，不污染外部字体和对齐状态。
        private static void DrawCenteredLabel(Rect rect, string text, Color color, GameFont font)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWrap = Text.WordWrap;
            Color oldColor = GUI.color;
            try
            {
                Text.Font = font;
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.WordWrap = false;
                GUI.color = color;
                float line = ShopUiVisualUtility.LineHeight(font);
                if (rect.width > 0f && rect.height >= line)
                    Widgets.Label(new Rect(rect.x, rect.center.y - line * 0.5f, rect.width, line), (text ?? "").Truncate(rect.width));
            }
            finally
            {
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
                Text.WordWrap = oldWrap;
                GUI.color = oldColor;
            }
        }
    }
}
