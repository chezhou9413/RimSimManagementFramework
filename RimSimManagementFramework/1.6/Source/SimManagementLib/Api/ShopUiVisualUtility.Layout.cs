using UnityEngine;
using Verse;

namespace SimManagementLib.Api
{
    //提供经营页面的公共布局控件，职责是让核心和扩展共用标题、指标、提示和搜索的视觉层级。
    public static partial class ShopUiVisualUtility
    {
        //绘制安全单行文本和完整提示，职责是隔离字体状态并在窄列中省略长标签。
        public static void DrawCellLabel(Rect rect, string label, Color? color = null, GameFont font = GameFont.Small)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWrap = Text.WordWrap;
            Color oldColor = GUI.color;
            try
            {
                Text.Font = font;
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.WordWrap = false;
                GUI.color = color ?? Color.white;
                if (rect.width > 0f && rect.height >= LineHeight(font))
                    Widgets.Label(rect, (label ?? "").Truncate(rect.width));
                if (!label.NullOrEmpty()) TooltipHandler.TipRegion(rect, label);
            }
            finally { Restore(oldFont, oldAnchor, oldWrap, oldColor); }
        }

        //绘制窗口标题与简短说明并返回占用高度，职责是为右上关闭按钮保留空间。
        public static float DrawPageHeading(Rect rect, string title, string description, bool reserveCloseButton = false)
        {
            float titleHeight = LineHeight(GameFont.Medium);
            float detailHeight = LineHeight(GameFont.Small);
            DrawCellLabel(new Rect(rect.x, rect.y, rect.width - (reserveCloseButton ? 36f : 0f), titleHeight),
                title, Color.white, GameFont.Medium);
            DrawCellLabel(new Rect(rect.x, rect.y + titleHeight + 4f, rect.width, detailHeight), description, MutedText);
            return titleHeight + detailHeight + 16f;
        }

        //返回指标卡安全高度，职责是让标题和值在中文字体回退时仍拥有独立行。
        public static float MetricHeight => LineHeight(GameFont.Small) + LineHeight(GameFont.Medium) + 22f;

        //绘制经营指标卡，职责是突出数值并保持与框架信息分区相同的底色和边界。
        public static void DrawMetric(Rect rect, string label, string value, Color? valueColor = null)
        {
            DrawSection(rect);
            float title = LineHeight(GameFont.Small);
            DrawCellLabel(new Rect(rect.x + 12f, rect.y + 8f, rect.width - 24f, title), label, MutedText);
            DrawCellLabel(new Rect(rect.x + 12f, rect.y + title + 12f, rect.width - 24f, LineHeight(GameFont.Medium)),
                value, valueColor ?? Color.white, GameFont.Medium);
        }

        //绘制等宽指标组，职责是按可用宽度自动换成两列并返回真实高度。
        public static float DrawMetrics(Rect rect, string[] labels, string[] values, bool compact = false)
        {
            int columns = rect.width < 600f ? Mathf.Min(2, labels.Length) : labels.Length;
            if (columns == 0) return 0f;
            float width = (rect.width - (columns - 1) * 8f) / columns;
            float height = compact ? LineHeight(GameFont.Small) + 16f : MetricHeight;
            for (int i = 0; i < labels.Length; i++)
            {
                var card = new Rect(rect.x + i % columns * (width + 8f), rect.y + i / columns * (height + 8f), width, height);
                if (!compact) DrawMetric(card, labels[i], values[i]);
                else
                {
                    DrawSection(card);
                    DrawCellLabel(card.ContractedBy(8f), labels[i] + "  " + values[i]);
                }
            }
            return Mathf.CeilToInt(labels.Length / (float)columns) * (height + 8f) - 8f;
        }

        //测量提示条高度，职责是为多行中文正文保留实际空间。
        public static float NoticeHeight(string message, float width)
        {
            GameFont oldFont = Text.Font;
            bool oldWrap = Text.WordWrap;
            try
            {
                Text.Font = GameFont.Small;
                Text.WordWrap = true;
                return Mathf.Max(LineHeight(GameFont.Small), Text.CalcHeight(message ?? "", Mathf.Max(1f, width - 28f))) + 16f;
            }
            finally { Text.Font = oldFont; Text.WordWrap = oldWrap; }
        }

        //绘制经营状态提示条，职责是让异常信息拥有可辨认的边线和完整换行正文。
        public static void DrawNotice(Rect rect, string message, bool warning = false)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWrap = Text.WordWrap;
            Color oldColor = GUI.color;
            try
            {
                DrawSection(rect);
                Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 3f, rect.height), warning ? WarningText : Accent);
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.WordWrap = true;
                GUI.color = warning ? WarningText : MutedText;
                Widgets.Label(new Rect(rect.x + 14f, rect.y + 8f, rect.width - 28f, rect.height - 16f), message ?? "");
            }
            finally { Restore(oldFont, oldAnchor, oldWrap, oldColor); }
        }

        //绘制带清空操作的搜索栏，职责是让核心和扩展使用统一字段尺寸与操作样式。
        public static string DrawSearchField(Rect rect, string value, string tip = "搜索名称")
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWrap = Text.WordWrap;
            Color oldColor = GUI.color;
            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.WordWrap = false;
                GUI.color = Color.white;
                DrawCellLabel(new Rect(rect.x, rect.y, 46f, rect.height), "搜索", MutedText);
                Rect field = new Rect(rect.x + 50f, rect.y, Mathf.Max(1f, rect.width - 120f), rect.height);
                string result = Widgets.TextField(field, value ?? "");
                TooltipHandler.TipRegion(field, tip);
                if (DrawSecondaryButton(new Rect(rect.xMax - 62f, rect.y, 62f, rect.height), "清空", !result.NullOrEmpty())) result = "";
                return result;
            }
            finally { Restore(oldFont, oldAnchor, oldWrap, oldColor); }
        }
    }
}
