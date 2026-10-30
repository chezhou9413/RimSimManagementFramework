using System;
using System.Linq;
using RimSimHotelExtension.Buildings;
using RimSimHotelExtension.GameComp;
using RimSimHotelExtension.Tool;
using SimManagementLib.Api;
using UnityEngine;
using Verse;

namespace RimSimHotelExtension.UI
{
    //沿用经营框架控件，隔离页面的字体、颜色和交互状态。
    internal sealed partial class HotelUi : IDisposable
    {
        private readonly GameFont font = Text.Font;
        private readonly TextAnchor anchor = Text.Anchor;
        private readonly bool wrap = Text.WordWrap;
        private readonly Color color = GUI.color;
        private readonly bool enabled = GUI.enabled;

        //使用实际小号字体测量中英文布局。
        public HotelUi() { Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; Text.WordWrap = true; GUI.color = Color.white; }

        //异常退出时也不影响其他页面及底部保存按钮。
        public void Dispose() { Text.Font = font; Text.Anchor = anchor; Text.WordWrap = wrap; GUI.color = color; GUI.enabled = enabled; }

        public static float Line => ShopUiVisualUtility.ControlHeight();

        //服务台和旅馆床构成住宿配置入口；未结合同在设施失效后仍可查看。
        public static bool HasHotel(ShopUiContext context)
        {
            var shop = (context as ShopManagerUiContext)?.Shop;
            if (shop?.Map == null) return false;
            HotelMapComponent manager = HotelMapComponent.For(shop.Map);
            return manager.Stays.Any(stay => !stay.completed && stay.shopZoneId == shop.ID)
                || (manager.DeskFor(shop) != null && shop.Map.listerBuildings.allBuildingsColonist
                    .OfType<Building_HotelBed>().Any(bed => shop.Cells.Contains(bed.Position)));
        }

        //说明按可用宽度完整换行，标题沿用框架字体。
        public static void Heading(float width, ref float y, string title, string description)
        {
            float height = ShopUiVisualUtility.LineHeight(GameFont.Medium);
            ShopUiVisualUtility.DrawCellLabel(new Rect(0, y, width, height), title, font: GameFont.Medium);
            y += height + 6f;
            Label(width, ref y, description, true);
            y += 6f;
        }

        //分区标题保留完整文字和统一表头底色。
        public static void Section(float width, ref float y, string title)
        {
            y += 8f;
            float height = Mathf.Max(Line, Text.CalcHeight(title, Mathf.Max(1f, width - 20f)) + 12f);
            ShopUiVisualUtility.DrawTableHeaderBackground(new Rect(0, y, width, height));
            Widgets.Label(new Rect(10f, y + 6f, width - 20f, height - 12f), title);
            y += height + 10f;
        }

        //指标按窗口宽度排列，极窄页面仍保留单列。
        public static void Metrics(float width, ref float y, string[] labels, params string[] values)
        {
            int columns = Mathf.Min(labels.Length, Mathf.Max(1, Mathf.FloorToInt((width + 8f) / 175f)));
            if (labels.Length == 4 && columns == 3) columns = 2;
            float cellWidth = (width - (columns - 1) * 8f) / columns;
            float height = ShopUiVisualUtility.MetricHeight;
            for (int i = 0; i < labels.Length; i++)
                ShopUiVisualUtility.DrawMetric(new Rect(i % columns * (cellWidth + 8f), y + i / columns * (height + 8f), cellWidth, height), labels[i], values[i]);
            y += Mathf.CeilToInt(labels.Length / (float)columns) * (height + 8f) + 4f;
        }

        //说明按实际字体测量高度，并恢复绘制颜色。
        public static void Label(float width, ref float y, string text, bool muted = false)
        {
            float height = Mathf.Max(Text.LineHeight, Text.CalcHeight(text, Mathf.Max(1f, width)));
            Color oldColor = GUI.color;
            try
            {
                if (muted) GUI.color = ShopUiVisualUtility.MutedText;
                Widgets.Label(new Rect(0, y, width, height), text);
            }
            finally { GUI.color = oldColor; }
            y += height + 8f;
        }

        //关键数值独立成行，窄窗口将数值放到标签下方。
        public static void Value(float width, ref float y, string label, string value)
        {
            if (width < 360f) { Label(width, ref y, label, true); Label(width, ref y, value); return; }
            float left = width * 0.43f;
            float height = Mathf.Max(Line, Mathf.Max(Text.CalcHeight(label, left - 12f), Text.CalcHeight(value, width - left)) + 8f);
            Color oldColor = GUI.color;
            try
            {
                GUI.color = ShopUiVisualUtility.MutedText;
                Widgets.Label(new Rect(0, y + 4f, left - 12f, height - 8f), label);
                GUI.color = oldColor;
                Widgets.Label(new Rect(left, y + 4f, width - left, height - 8f), value);
            }
            finally { GUI.color = oldColor; }
            y += height;
        }

        //资金条目分开显示名称、金额和完整说明。
        public static void MoneyRow(float width, ref float y, string title, string amount, string detail, int index)
        {
            float detailHeight = Text.CalcHeight(detail, Mathf.Max(1f, width - 16f));
            float height = Line + detailHeight + 18f;
            ShopUiVisualUtility.DrawTableRowBackground(new Rect(0, y, width, height), index, false);
            float amountWidth = Mathf.Min(width * 0.4f, Mathf.Max(100f, Text.CalcSize(amount).x + 12f));
            ShopUiVisualUtility.DrawCellLabel(new Rect(8f, y + 4f, width - amountWidth - 24f, Line), title);
            ShopUiVisualUtility.DrawCellLabel(new Rect(width - amountWidth - 8f, y + 4f, amountWidth, Line), amount, ShopUiVisualUtility.PositiveText);
            Color oldColor = GUI.color;
            try
            {
                GUI.color = ShopUiVisualUtility.MutedText;
                Widgets.Label(new Rect(8f, y + Line + 8f, width - 16f, detailHeight), detail);
            }
            finally { GUI.color = oldColor; }
            y += height + 4f;
        }

        //提示和空列表均使用框架通知样式。
        public static void Notice(float width, ref float y, string text, bool warning = false)
        {
            float height = ShopUiVisualUtility.NoticeHeight(text, width);
            ShopUiVisualUtility.DrawNotice(new Rect(0, y, width, height), text, warning);
            y += height + 8f;
        }

        //宽窗口将输入框右对齐，窄窗口改为上下布局。
        public static void Number(float width, ref float y, string label, ref int value, ref string buffer, int max)
        {
            float inputWidth = Mathf.Min(140f, width);
            float labelWidth = width - inputWidth - 16f;
            if (labelWidth < 220f)
            {
                Label(width, ref y, label);
                Widgets.TextFieldNumeric(new Rect(0, y, inputWidth, Line), ref value, ref buffer, 0, max);
                y += Line + 10f;
                return;
            }
            float height = Mathf.Max(Line, Text.CalcHeight(label, labelWidth) + 8f);
            Widgets.Label(new Rect(0, y + 4f, labelWidth, height - 4f), label);
            Widgets.TextFieldNumeric(new Rect(width - inputWidth, y, inputWidth, Line), ref value, ref buffer, 0, max);
            y += height + 8f;
        }

        //使用原版禁用参数，避免全局 GUI 状态泄漏。
        public static void Check(float width, ref float y, string label, ref bool value, bool disabled = false)
        {
            float height = Mathf.Max(Line, Text.CalcHeight(label, Mathf.Max(1f, width - 32f)) + 8f);
            Widgets.CheckboxLabeled(new Rect(0, y, width, height), label, ref value, disabled);
            y += height + 6f;
        }

        //列表标题与定位按钮同排，长名称可悬停查看全文。
        public static void Item(float width, ref float y, string title, Thing thing, int index)
        {
            float buttonWidth = Mathf.Min(width * 0.35f, Text.CalcSize(HotelText.T("RSH.Ui.Locate")).x + 28f);
            ShopUiVisualUtility.DrawTableRowBackground(new Rect(0, y, width, Line + 12f), index, false);
            ShopUiVisualUtility.DrawCellLabel(new Rect(8f, y + 6f, width - buttonWidth - 24f, Line), title);
            if (Button(new Rect(width - buttonWidth - 6f, y + 6f, buttonWidth, Line),
                HotelText.T("RSH.Ui.Locate"), enabled: thing != null && thing.Spawned)) CameraJumper.TryJumpAndSelect(thing);
            y += Line + 18f;
        }

        //状态标签单独测量宽度，不挤占名称和操作按钮。
        public static void Status(float width, ref float y, string text, bool positive)
        {
            float badgeWidth = Mathf.Min(width, Text.CalcSize(text).x + 24f);
            ShopUiVisualUtility.DrawStatusBadge(new Rect(0, y, badgeWidth, Line), text,
                positive ? ShopUiVisualUtility.SelectedRowFill : new Color(0.7f, 0.3f, 0.15f, 0.18f));
            y += Line + 8f;
        }

        //按钮使用框架样式，极窄宽度下省略文字并保留完整提示。
        public static bool Button(Rect rect, string label, bool primary = false, bool enabled = true)
        {
            string visible = label.Truncate(Mathf.Max(1f, rect.width - 8f));
            bool clicked = primary ? ShopUiVisualUtility.DrawPrimaryButton(rect, visible, enabled)
                : ShopUiVisualUtility.DrawSecondaryButton(rect, visible, enabled);
            TooltipHandler.TipRegion(rect, label);
            return clicked;
        }

        //并列操作按翻译后的宽度决定是否换行。
        public static void Actions(float width, ref float y, string first, Action firstAction, string second, Action secondAction)
        {
            float a = Mathf.Min(width, Mathf.Max(110f, Text.CalcSize(first).x + 28f));
            float b = Mathf.Min(width, Mathf.Max(110f, Text.CalcSize(second).x + 28f));
            if (Button(new Rect(0, y, a, Line), first, true)) firstAction();
            bool stack = a + b + 8f > width;
            if (Button(new Rect(stack ? 0 : a + 8f, y + (stack ? Line + 6f : 0), b, Line), second)) secondAction();
            y += (stack ? Line * 2f + 6f : Line) + 12f;
        }

        //框架已预留页脚，滚动内容不会绘制到底部按钮上。
        public static void Scroll(Rect rect, PageScroll state, Action<float> draw)
        {
            float width = Mathf.Max(1f, rect.width - 20f);
            state.position.y = Mathf.Clamp(state.position.y, 0f, Mathf.Max(0f, state.height - rect.height));
            Widgets.BeginScrollView(rect, ref state.position, new Rect(0, 0, width, Mathf.Max(rect.height, state.height)));
            try { draw(width); }
            finally { Widgets.EndScrollView(); }
        }

        //沿用框架搜索内容，匹配名称、状态或房间等文字。
        public static bool Matches(string search, string value) => search.NullOrEmpty() || (value ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    //保存单个窗口内页面的滚动位置和实际内容高度。
    internal class PageScroll
    {
        public Vector2 position;
        public float height;
    }
}
