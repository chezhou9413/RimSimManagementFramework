using UnityEngine;
using Verse;

namespace SimManagementLib.Api
{
    //类职责：向框架内置页面和外部经营扩展公开同一套商店界面颜色、分区、列表行、状态标签和按钮。
    public static class ShopUiVisualUtility
    {
        public static readonly Color Accent = new Color(0.25f, 0.65f, 0.85f, 0.95f);
        public static readonly Color MutedText = new Color(0.72f, 0.72f, 0.72f, 1f);
        public static readonly Color WarningText = new Color(1f, 0.68f, 0.54f, 1f);
        public static readonly Color PositiveText = new Color(0.62f, 1f, 0.66f, 1f);
        public static readonly Color Divider = new Color(0.28f, 0.28f, 0.28f, 0.5f);
        public static readonly Color SectionFill = new Color(0f, 0f, 0f, 0.14f);
        public static readonly Color HeaderFill = new Color(0f, 0f, 0f, 0.25f);
        public static readonly Color AlternateRowFill = new Color(1f, 1f, 1f, 0.025f);
        public static readonly Color SelectedRowFill = new Color(0.25f, 0.65f, 0.85f, 0.08f);

        private static readonly Color PrimaryFill = new Color(0.25f, 0.65f, 0.85f, 0.24f);
        private static readonly Color PrimaryHover = new Color(0.25f, 0.65f, 0.85f, 0.34f);
        private static readonly Color SecondaryFill = new Color(1f, 1f, 1f, 0.05f);
        private static readonly Color SecondaryHover = new Color(1f, 1f, 1f, 0.11f);
        private static readonly Color SecondaryBorder = new Color(1f, 1f, 1f, 0.18f);
        private static readonly Color DangerFill = new Color(0.90f, 0.35f, 0.35f, 0.14f);
        private static readonly Color DangerHover = new Color(0.90f, 0.35f, 0.35f, 0.22f);
        private static readonly Color DangerBorder = new Color(0.90f, 0.35f, 0.35f, 0.45f);
        private static readonly Color DisabledFill = new Color(0f, 0f, 0f, 0.20f);
        private static readonly Color DisabledBorder = new Color(1f, 1f, 1f, 0.08f);
        private static readonly Color DisabledText = new Color(0.55f, 0.55f, 0.55f, 1f);

        //返回指定字体的安全单行高度，职责是兼容中文字体和 Tiny 回退到 Small 的情况。
        public static float LineHeight(GameFont font, float verticalPadding = 2f)
        {
            return Mathf.Ceil(Text.LineHeightOf(font)) + verticalPadding;
        }

        //返回按钮、输入框和滑杆的安全高度，职责是给文本保留稳定上下内边距。
        public static float ControlHeight(GameFont font = GameFont.Small)
        {
            return LineHeight(font) + 10f;
        }

        //绘制框架标准信息分区，职责是为表单摘要和列表容器提供统一背景及边界。
        public static void DrawSection(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, SectionFill);
            DrawBorder(rect, Divider);
        }

        //绘制框架标准表头背景，职责是让外部列表与内置列表使用相同层级和分隔线。
        public static void DrawTableHeaderBackground(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, HeaderFill);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), Divider);
        }

        //绘制框架标准列表行背景，职责是统一交替底色、选中状态和鼠标悬停反馈。
        public static void DrawTableRowBackground(Rect rect, int index, bool selected)
        {
            if (selected)
                Widgets.DrawBoxSolid(rect, SelectedRowFill);
            else if (index % 2 == 1)
                Widgets.DrawBoxSolid(rect, AlternateRowFill);
            Widgets.DrawHighlightIfMouseover(rect);
        }

        //绘制框架标准状态标签，职责是在真实字体高度足够时显示居中的短状态文本。
        public static void DrawStatusBadge(Rect rect, string label, Color fill)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWrap = Text.WordWrap;
            Color oldColor = GUI.color;
            try
            {
                Widgets.DrawBoxSolid(rect, fill);
                DrawBorder(rect, new Color(1f, 1f, 1f, 0.16f));
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.WordWrap = false;
                GUI.color = Color.white;
                float line = LineHeight(GameFont.Tiny);
                if (rect.height >= line)
                    Widgets.Label(new Rect(rect.x + 2f, rect.center.y - line / 2f, rect.width - 4f, line), label ?? "");
            }
            finally
            {
                Restore(oldFont, oldAnchor, oldWrap, oldColor);
            }
        }

        //绘制框架标准主按钮，职责是突出创建、选择和保存操作。
        public static bool DrawPrimaryButton(Rect rect, string label, bool enabled = true, GameFont font = GameFont.Small)
        {
            return DrawButton(rect, label, PrimaryFill, PrimaryHover, Accent, Color.white, enabled, font);
        }

        //绘制框架标准次按钮，职责是承载筛选、复制、取消和普通设置操作。
        public static bool DrawSecondaryButton(Rect rect, string label, bool enabled = true, GameFont font = GameFont.Small)
        {
            return DrawButton(rect, label, SecondaryFill, SecondaryHover, SecondaryBorder, Color.white, enabled, font);
        }

        //绘制框架标准危险按钮，职责是清晰标识删除和重置操作。
        public static bool DrawDangerButton(Rect rect, string label, bool enabled = true, GameFont font = GameFont.Small)
        {
            return DrawButton(rect, label, DangerFill, DangerHover, DangerBorder,
                new Color(1f, 0.80f, 0.80f, 1f), enabled, font);
        }

        //绘制外观禁用但仍可点击的按钮，职责是保留框架现有的解释型禁用交互。
        public static bool DrawDisabledClickableButton(Rect rect, string label, GameFont font = GameFont.Small)
        {
            return DrawButton(rect, label, DisabledFill, SecondaryHover, DisabledBorder, DisabledText, true, font);
        }

        //绘制框架标准页签按钮，职责是统一选中、普通和悬停状态。
        public static bool DrawTabButton(Rect rect, string label, bool selected, Color normalTextColor)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWrap = Text.WordWrap;
            Color oldColor = GUI.color;
            try
            {
                Color fill = selected ? new Color(0.25f, 0.65f, 0.85f, 0.20f)
                    : Mouse.IsOver(rect) ? new Color(1f, 1f, 1f, 0.10f) : new Color(1f, 1f, 1f, 0.04f);
                Color border = selected ? new Color(0.25f, 0.65f, 0.85f, 1f) : new Color(1f, 1f, 1f, 0.14f);
                Widgets.DrawBoxSolid(rect, fill);
                DrawBorder(rect, border);
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.WordWrap = false;
                GUI.color = selected ? Color.white : normalTextColor;
                float line = LineHeight(GameFont.Small);
                if (rect.height >= line)
                    Widgets.Label(new Rect(rect.x + 2f, rect.center.y - line / 2f, rect.width - 4f, line), label ?? "");
                return !selected && Widgets.ButtonInvisible(rect, false);
            }
            finally
            {
                Restore(oldFont, oldAnchor, oldWrap, oldColor);
            }
        }

        //绘制矩形边界，职责是供框架及外部页面建立一致的一像素层级线。
        public static void DrawBorder(Rect rect, Color color, float thickness = 1f)
        {
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, rect.width, thickness), color);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, thickness, rect.height), color);
            Widgets.DrawBoxSolid(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        //绘制带完整状态恢复的文本按钮，职责是确保外部页面不会污染同帧后续控件。
        private static bool DrawButton(Rect rect, string label, Color fill, Color hover, Color border,
            Color text, bool enabled, GameFont font)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWrap = Text.WordWrap;
            Color oldColor = GUI.color;
            try
            {
                Widgets.DrawBoxSolid(rect, !enabled ? DisabledFill : Mouse.IsOver(rect) ? hover : fill);
                DrawBorder(rect, enabled ? border : DisabledBorder);
                Text.Font = font;
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.WordWrap = false;
                GUI.color = enabled ? text : DisabledText;
                float line = LineHeight(font);
                if (rect.height >= line)
                    Widgets.Label(new Rect(rect.x + 2f, rect.center.y - line / 2f, rect.width - 4f, line), label ?? "");
                if (!label.NullOrEmpty()) TooltipHandler.TipRegion(rect, label);
                return enabled && Widgets.ButtonInvisible(rect, false);
            }
            finally
            {
                Restore(oldFont, oldAnchor, oldWrap, oldColor);
            }
        }

        //恢复进入控件前的 IMGUI 状态，职责是隔离不同页面和控件的字体、锚点、换行及颜色。
        private static void Restore(GameFont font, TextAnchor anchor, bool wrap, Color color)
        {
            Text.Font = font;
            Text.Anchor = anchor;
            Text.WordWrap = wrap;
            GUI.color = color;
        }
    }
}
