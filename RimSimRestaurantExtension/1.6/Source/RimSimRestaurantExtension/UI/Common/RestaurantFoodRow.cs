using SimManagementLib.Tool;
using RimSimRestaurantExtension.Tool;
using SimManagementLib.Api;
using UnityEngine;
using Verse;

namespace RimSimRestaurantExtension.UI
{
    //绘制食品选择行，职责是让菜谱与传送带选择器共用图标、营养摘要和选择操作。
    internal static class RestaurantFoodRow
    {
        public static float Height => Mathf.Max(48f, RestaurantUiStyle.LineHeight(GameFont.Small) * 2f + 4f) + 16f;

        //绘制可选择的食品条目，职责是保留长名称提示并按实际中文行高定位摘要。
        public static bool Draw(Rect rect, ThingDef food, int index)
        {
            using (new RestaurantGuiScope())
            {
                ShopUiVisualUtility.DrawTableRowBackground(rect, index, false);
                RestaurantUiStyle.DrawThingIconOrMissing(new Rect(rect.x + 8f, rect.center.y - 24f, 48f, 48f), food);
                float control = RestaurantUiStyle.ControlHeight();
                var button = new Rect(rect.xMax - 90f, rect.center.y - control / 2f, 82f, control);
                float x = rect.x + 66f, width = Mathf.Max(1f, button.x - x - 10f);
                float line = RestaurantUiStyle.LineHeight(GameFont.Small);
                ShopUiVisualUtility.DrawCellLabel(new Rect(x, rect.y + 8f, width, line), food.LabelCap);
                ShopUiVisualUtility.DrawCellLabel(new Rect(x, rect.y + line + 12f, width, line),
                    RestaurantFoodUtility.BuildFoodSummary(food), RestaurantUiStyle.MutedText);
                return RestaurantUiStyle.DrawPrimaryButton(button, SimTranslation.T("RSR.UI.Select"))
                    || Widgets.ButtonInvisible(new Rect(rect.x, rect.y, button.x - rect.x - 4f, rect.height), false);
            }
        }
    }
}
