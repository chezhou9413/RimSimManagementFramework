using SimManagementLib.Api;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    //类职责：保留框架内部旧调用入口，并把所有商店控件绘制转交给公开视觉 API。
    internal static class SimUiStyle
    {
        //绘制框架主按钮，职责是兼容现有内部页面调用。
        public static bool DrawPrimaryButton(Rect rect, string label, bool enabled = true, GameFont font = GameFont.Small)
        {
            return ShopUiVisualUtility.DrawPrimaryButton(rect, label, enabled, font);
        }

        //绘制可点击的禁用外观按钮，职责是兼容现有解释型按钮交互。
        public static bool DrawDisabledClickableButton(Rect rect, string label, GameFont font = GameFont.Small)
        {
            return ShopUiVisualUtility.DrawDisabledClickableButton(rect, label, font);
        }

        //绘制框架次按钮，职责是兼容现有内部页面调用。
        public static bool DrawSecondaryButton(Rect rect, string label, bool enabled = true, GameFont font = GameFont.Small)
        {
            return ShopUiVisualUtility.DrawSecondaryButton(rect, label, enabled, font);
        }

        //绘制框架危险按钮，职责是兼容现有内部页面调用。
        public static bool DrawDangerButton(Rect rect, string label, bool enabled = true, GameFont font = GameFont.Small)
        {
            return ShopUiVisualUtility.DrawDangerButton(rect, label, enabled, font);
        }

        //绘制框架页签按钮，职责是兼容现有侧栏与页签调用。
        public static bool DrawTabButton(Rect rect, string label, bool selected, Color normalTextColor)
        {
            return ShopUiVisualUtility.DrawTabButton(rect, label, selected, normalTextColor);
        }

        //绘制矩形边界，职责是兼容现有内部页面的分区绘制。
        public static void DrawBorder(Rect rect, Color color, float thickness = 1f)
        {
            ShopUiVisualUtility.DrawBorder(rect, color, thickness);
        }
    }
}
