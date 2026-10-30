using System;
using System.Linq;
using RimSimHotelExtension.Tool;
using SimManagementLib.Api;
using UnityEngine;
using Verse;

namespace RimSimHotelExtension.UI
{
    //主页面只保留摘要和列表，表单及长明细放到独立窗口。
    internal sealed partial class HotelUi
    {
        //摘要使用一至两行文字，避免统计卡片占满首屏。
        public static void Summary(float width, ref float y, string text)
        {
            float h = Mathf.Max(Line, Text.CalcHeight(text, Mathf.Max(1f, width - 16f)) + 12f);
            ShopUiVisualUtility.DrawSection(new Rect(0, y, width, h));
            Widgets.Label(new Rect(8f, y + 6f, width - 16f, h - 12f), text);
            y += h + 8f;
        }

        //排序菜单保留当前选择，点击后回到列表顶部。
        public static void SortMenu(string[] labels, int selected, Action<int> select)
        {
            Find.WindowStack.Add(new FloatMenu(labels.Select((label, index) =>
                new FloatMenuOption((index == selected ? "✓ " : "") + label, () => select(index))).ToList()));
        }

        //表头和数据行使用相同列宽；窄窗口保留操作列并将数据改为两行。
        public static void TableHeader(float width, ref float y, string[] labels, bool locate = true)
        {
            ShopUiVisualUtility.DrawTableHeaderBackground(new Rect(0, y, width, Line));
            if (width < 500f)
                ShopUiVisualUtility.DrawCellLabel(new Rect(8f, y, width - 16f, Line), string.Join(" / ", labels));
            else DrawCells(width, y, Line, labels, locate);
            y += Line + 2f;
        }

        //每行提供明确的小按钮，完整名称和被省略的内容可悬停查看。
        public static void TableRow(float width, ref float y, int index, string[] values,
            Thing thing = null, Action details = null, string action = null, bool locate = true)
        {
            bool narrow = width < 500f;
            float h = narrow ? Line * 2f + 8f : Line + 8f;
            ShopUiVisualUtility.DrawTableRowBackground(new Rect(0, y, width, h), index, false);
            float buttonWidth = ActionWidth(locate);
            float start = width - buttonWidth - 6f;
            if (narrow)
            {
                ShopUiVisualUtility.DrawCellLabel(new Rect(8f, y + 4f, Mathf.Max(1f, start - 16f), Line), values[0]);
                ShopUiVisualUtility.DrawCellLabel(new Rect(8f, y + Line + 4f, width - 16f, Line), string.Join(" · ", values.Skip(1)), ShopUiVisualUtility.MutedText);
            }
            else DrawCells(width, y + 4f, Line, values, locate);
            float x = start;
            if (details != null && Button(new Rect(x, y + 4f, buttonWidth - (locate ? LocateWidth + 6f : 0f), Line),
                    action ?? HotelText.T("RSH.Ui.Details"))) details();
            if (locate && Button(new Rect(width - LocateWidth - 6f, y + 4f, LocateWidth, Line), HotelText.T("RSH.Ui.Locate"),
                    enabled: thing?.Spawned == true)) CameraJumper.TryJumpAndSelect(thing);
            y += h + 2f;
        }

        private static float LocateWidth => Mathf.Max(52f, Text.CalcSize(HotelText.T("RSH.Ui.Locate")).x + 16f);
        private static float ActionWidth(bool locate) => Mathf.Max(60f, Text.CalcSize(HotelText.T("RSH.Ui.Details")).x + 16f)
            + (locate ? LocateWidth + 6f : 0f);

        //名称列略宽，其余列等宽，统一保留右侧操作空间。
        private static void DrawCells(float width, float y, float height, string[] values, bool locate)
        {
            float available = Mathf.Max(1f, width - ActionWidth(locate) - 28f);
            float unit = available / (values.Length + 0.6f);
            float x = 8f;
            for (int i = 0; i < values.Length; i++)
            {
                float w = unit * (i == 0 ? 1.6f : 1f);
                ShopUiVisualUtility.DrawCellLabel(new Rect(x, y, Mathf.Max(1f, w - 8f), height), values[i]);
                x += w;
            }
        }
    }
}
