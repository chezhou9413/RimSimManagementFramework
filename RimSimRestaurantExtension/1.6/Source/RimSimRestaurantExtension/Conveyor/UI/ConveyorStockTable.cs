using System;
using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.Conveyor.Stocking;
using RimSimRestaurantExtension.Conveyor.Transport;
using RimSimRestaurantExtension.Tool;
using RimSimRestaurantExtension.UI;
using SimManagementLib.Api;
using UnityEngine;
using Verse;

namespace RimSimRestaurantExtension.Conveyor.UI
{
    //绘制线路上架表格，职责是隔离数字输入缓冲并为长菜名和窄窗口保留安全列宽。
    internal sealed class ConveyorStockTable
    {
        private readonly Dictionary<string, string> buffers = new Dictionary<string, string>();

        //绘制带表头和空状态的滚动列表，职责是让窄窗口横向滚动而不挤压编辑控件。
        internal void Draw(Rect rect, ConveyorLine line, List<ConveyorStockRule> draft, string search, ref Vector2 scroll)
        {
            var shown = draft.Where(r => search.NullOrEmpty() || RestaurantFoodUtility.MatchesSearch(r.Food, search)
                || (r.Label + " " + Source(r)).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            ShopUiVisualUtility.DrawSection(rect);
            if (shown.Count == 0)
            {
                RestaurantBusinessUiUtility.DrawEmpty(rect, draft.Count == 0
                    ? "尚未配置上架食品\n添加餐厅菜谱或现成食品，厨师就会按目标补餐。"
                    : "没有匹配的上架食品\n请调整或清空搜索条件。");
                return;
            }
            float width = Mathf.Max(850f, rect.width - 16f);
            float row = RestaurantUiStyle.LineHeight(GameFont.Small) * 2f + 16f;
            float header = RestaurantUiStyle.ControlHeight();
            var view = new Rect(0, 0, width, Mathf.Max(rect.height - 16f, header + shown.Count * row));
            Widgets.BeginScrollView(rect, ref scroll, view);
            try
            {
                DrawRow(new Rect(0, 0, width, header), null, line, draft, 0);
                for (int i = 0; i < shown.Count; i++)
                    DrawRow(new Rect(0, header + i * row, width, row), shown[i], line, draft, i);
            }
            finally { Widgets.EndScrollView(); }
        }

        //绘制统一列宽的表头或食品行，职责是区分上架开关、每盘规格、目标和真实库存。
        private void DrawRow(Rect rect, ConveyorStockRule rule, ConveyorLine line, List<ConveyorStockRule> draft, int index)
        {
            float[] widths = { 44f, rect.width - 542f, 88f, 88f, 96f, 142f, 84f };
            string[] labels = { "启用", "食品与货源", "每盘件数", "目标盘数", "单件售价", "现存 / 补餐在途", "操作" };
            bool header = rule == null;
            if (header) ShopUiVisualUtility.DrawTableHeaderBackground(rect);
            else ShopUiVisualUtility.DrawTableRowBackground(rect, index, rule.enabled);
            float x = rect.x;
            float control = RestaurantUiStyle.ControlHeight();
            for (int i = 0; i < widths.Length; i++)
            {
                var cell = new Rect(x + 6f, rect.y, widths[i] - 12f, rect.height);
                x += widths[i];
                if (header) { ShopUiVisualUtility.DrawCellLabel(cell, labels[i], RestaurantUiStyle.MutedText); continue; }
                var field = new Rect(cell.x, rect.center.y - control / 2f, cell.width, control);
                if (i == 0) Widgets.CheckboxLabeled(field, "", ref rule.enabled);
                else if (i == 1)
                {
                    RestaurantUiStyle.DrawThingIconOrMissing(new Rect(cell.x, rect.center.y - 18f, 36f, 36f), rule.Food);
                    float textX = cell.x + 44f, textWidth = cell.width - 44f;
                    float h = RestaurantUiStyle.LineHeight(GameFont.Small);
                    ShopUiVisualUtility.DrawCellLabel(new Rect(textX, cell.y + 6f, textWidth, h), rule.Label,
                        rule.enabled ? Color.white : RestaurantUiStyle.MutedText);
                    ShopUiVisualUtility.DrawCellLabel(new Rect(textX, cell.y + h + 8f, textWidth, h), Source(rule), RestaurantUiStyle.MutedText);
                    TooltipHandler.TipRegion(cell, rule.Label + "\n" + Source(rule) + "\n整盘售价：" + (rule.price * rule.portions).ToString("F1"));
                }
                else if (i == 2) Number(field, rule.id + "/portions", ref rule.portions, 1, rule.Food?.stackLimit ?? 1);
                else if (i == 3) Number(field, rule.id + "/target", ref rule.target, 0, line.segments.Count);
                else if (i == 4)
                {
                    string key = rule.id + "/price";
                    buffers.TryGetValue(key, out var buffer);
                    Widgets.TextFieldNumeric(field, ref rule.price, ref buffer, 1f, 100000f);
                    buffers[key] = buffer;
                }
                else if (i == 5) ShopUiVisualUtility.DrawCellLabel(field,
                    line.Count(rule.id) + " / " + ConveyorStockPlanner.Pending(line, rule.id));
                else if (RestaurantUiStyle.DrawDangerButton(field, "移除")) draft.Remove(rule);
            }
        }

        //显示食品来源，职责是让制作与搬运分支拥有清晰可读的名称。
        private static string Source(ConveyorStockRule rule) =>
            rule.menu != null ? "菜谱 · 厨房制作" : rule.cabinet != null ? "现货 · " + rule.cabinet.LabelCap : "现货 · 绑定后厨储存区";

        //保留数字输入缓冲，职责是避免重绘覆盖正在编辑的数量。
        private void Number(Rect rect, string key, ref int value, int minimum, int maximum)
        {
            buffers.TryGetValue(key, out var buffer);
            Widgets.TextFieldNumeric(rect, ref value, ref buffer, minimum, maximum);
            buffers[key] = buffer;
        }
    }
}
