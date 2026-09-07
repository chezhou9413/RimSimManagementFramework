using SimManagementLib.Api;
using UnityEngine;
using Verse;

namespace RimSimRestaurantExtension.UI
{
    //类职责：保留餐厅页面的语义化控件入口，并把视觉绘制统一转交给框架公开 API。
    internal static class RestaurantUiStyle
    {
        public static readonly Color CardBg = ShopUiVisualUtility.AlternateRowFill;
        public static readonly Color Border = ShopUiVisualUtility.Divider;
        public static readonly Color MutedText = ShopUiVisualUtility.MutedText;
        public static readonly Color Accent = ShopUiVisualUtility.Accent;
        public static readonly Color Warning = ShopUiVisualUtility.WarningText;
        public static readonly Color Good = ShopUiVisualUtility.PositiveText;

        //返回字体的安全单行高度，职责是复用框架对中文字体和 Tiny 回退的测量规则。
        public static float LineHeight(GameFont font, float verticalPadding = 2f)
        {
            return ShopUiVisualUtility.LineHeight(font, verticalPadding);
        }

        //返回按钮和输入框的安全高度，职责是复用框架统一控件尺寸。
        public static float ControlHeight(GameFont font = GameFont.Small)
        {
            return ShopUiVisualUtility.ControlHeight(font);
        }

        //保留页面容器入口，职责是避免扩展在框架已经绘制的页面区域上重复覆盖整页背景。
        public static void DrawPanel(Rect rect)
        {
        }

        //绘制标准信息分区，职责是让餐厅摘要和列表容器使用框架内置页面层级。
        public static void DrawCard(Rect rect)
        {
            ShopUiVisualUtility.DrawSection(rect);
        }

        //绘制物品图标或缺失占位，职责是让失效餐品 Def 显示明确异常而不崩溃。
        public static void DrawThingIconOrMissing(Rect rect, ThingDef def)
        {
            if (def != null)
            {
                Widgets.ThingIcon(rect, def);
                return;
            }
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;
            try
            {
                Widgets.DrawBoxSolid(rect, new Color(0.8f, 0.25f, 0.25f, 0.12f));
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = Warning;
                float line = LineHeight(GameFont.Medium);
                if (rect.height >= line)
                    Widgets.Label(new Rect(rect.x, rect.center.y - line / 2f, rect.width, line), "!");
            }
            finally
            {
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
                GUI.color = oldColor;
            }
        }

        //绘制框架主按钮，职责是统一餐厅创建、选择和确认操作。
        public static bool DrawPrimaryButton(Rect rect, string label, bool enabled = true, GameFont font = GameFont.Small)
        {
            return ShopUiVisualUtility.DrawPrimaryButton(rect, label, enabled, font);
        }

        //绘制框架次按钮，职责是统一餐厅筛选、复制和普通设置操作。
        public static bool DrawSecondaryButton(Rect rect, string label, bool enabled = true, GameFont font = GameFont.Small)
        {
            return ShopUiVisualUtility.DrawSecondaryButton(rect, label, enabled, font);
        }

        //绘制框架危险按钮，职责是统一餐厅删除和重置操作。
        public static bool DrawDangerButton(Rect rect, string label, bool enabled = true, GameFont font = GameFont.Small)
        {
            return ShopUiVisualUtility.DrawDangerButton(rect, label, enabled, font);
        }

        //绘制框架状态标签，职责是统一订单阶段的颜色、边界和字体高度。
        public static void DrawBadge(Rect rect, string label, Color fill)
        {
            ShopUiVisualUtility.DrawStatusBadge(rect, label, fill);
        }

        //绘制框架矩形边界，职责是兼容餐厅既有图标和详情分区调用。
        public static void DrawBorder(Rect rect, Color color, float thickness = 1f)
        {
            ShopUiVisualUtility.DrawBorder(rect, color, thickness);
        }
    }
}
