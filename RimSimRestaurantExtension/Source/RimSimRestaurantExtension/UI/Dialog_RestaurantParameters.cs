using SimManagementLib.Tool;
using RimSimRestaurantExtension.GameComp;
using SimManagementLib.Api;
using UnityEngine;
using Verse;

namespace RimSimRestaurantExtension.UI
{
    //编辑餐厅运行参数的独立草稿，职责是确认后回写父窗口草稿并保留统一保存语义。
    internal sealed class Dialog_RestaurantParameters : Window
    {
        private readonly RestaurantShopSettings target;
        private readonly RestaurantShopSettings draft;
        private Vector2 scroll;
        public override Vector2 InitialSize => new Vector2(Mathf.Min(620f, Verse.UI.screenWidth - 48f), Mathf.Min(740f, Verse.UI.screenHeight - 48f));

        //建立参数副本，职责是让取消子窗口丢弃本次参数编辑。
        public Dialog_RestaurantParameters(RestaurantShopSettings target)
        {
            this.target = target;
            draft = target.Clone();
            doCloseX = true;
            closeOnAccept = false;
            absorbInputAroundWindow = true;
            draggable = true;
        }

        //绘制分组参数表单，职责是分别保留标题、滚动正文和草稿确认栏。
        public override void DoWindowContents(Rect rect)
        {
            using (new RestaurantGuiScope())
            {
                float h = RestaurantUiStyle.ControlHeight();
                float top = ShopUiVisualUtility.DrawPageHeading(rect, SimTranslation.T("RSR.UI.ParameterHeading"), SimTranslation.T("RSR.UI.ParameterHint"), true);
                float footer = rect.height - h;
                float line = RestaurantUiStyle.LineHeight(GameFont.Small);
                string kitchenHint = SimTranslation.T("RSR.UI.KitchenHint");
                Text.Font = GameFont.Small;
                Text.WordWrap = true;
                float hintHeight = Text.CalcHeight(kitchenHint, rect.width - 32f);
                float height = 3f * (h + 8f) + 2f * (h + 8f) + 6f * (line + h + 12f)
                    + hintHeight + 12f;
                Rect outer = new Rect(0, top, rect.width, Mathf.Max(0f, footer - top - 12f));
                Rect view = new Rect(0, 0, rect.width - 16f, Mathf.Max(outer.height, height));
                Widgets.BeginScrollView(outer, ref scroll, view);
                try
                {
                    float y = Section(view.width, 0f, SimTranslation.T("RSR.UI.BusinessPricing"));
                    RestaurantUiStyle.DrawCheckbox(new Rect(8f, y, view.width - 16f, h), SimTranslation.T("RSR.UI.EnableService"), ref draft.enabled); y += h + 8f;
                    RestaurantUiStyle.DrawCheckbox(new Rect(8f, y, view.width - 16f, h), SimTranslation.T("RSR.UI.UsePreferences"), ref draft.useCustomerPreferences); y += h + 8f;
                    draft.priceMultiplier = Slider(view.width, ref y, SimTranslation.T("RSR.UI.PriceMultiplier"), SimTranslation.T("RSR.UI.MultiplierValue", (draft.priceMultiplier.ToString("F2")).Named("value")), draft.priceMultiplier, 0.1f, 5f, 0.05f);
                    draft.preferenceStrength = Slider(view.width, ref y, SimTranslation.T("RSR.UI.PreferenceStrength"), draft.preferenceStrength.ToString("F1"), draft.preferenceStrength, 0f, 2f, 0.1f);
                    y = Section(view.width, y, SimTranslation.T("RSR.UI.DiningWaiting"));
                    draft.maxServiceWaitTicks = Mathf.RoundToInt(Slider(view.width, ref y, SimTranslation.T("RSR.UI.MaxOrderWait"), SimTranslation.T("RSR.UI.SecondsValue", (draft.maxServiceWaitTicks / 60).Named("seconds")),
                        draft.maxServiceWaitTicks / 60f, 100f, 500f, 10f) * 60f);
                    draft.maxWaitTicks = Mathf.RoundToInt(Slider(view.width, ref y, SimTranslation.T("RSR.UI.MaxMealWait"), SimTranslation.T("RSR.UI.SecondsValue", (draft.maxWaitTicks / 60).Named("seconds")),
                        draft.maxWaitTicks / 60f, 100f, 500f, 10f) * 60f);
                    draft.maxOrderRounds = Mathf.RoundToInt(Slider(view.width, ref y, SimTranslation.T("RSR.UI.MaxRounds"), SimTranslation.T("RSR.UI.RoundsValue", (draft.maxOrderRounds).Named("rounds")),
                        draft.maxOrderRounds, 1f, 5f, 1f));
                    draft.reorderIntervalTicks = Mathf.RoundToInt(Slider(view.width, ref y, SimTranslation.T("RSR.UI.ReorderInterval"), SimTranslation.T("RSR.UI.SecondsValue", (draft.reorderIntervalTicks / 60).Named("seconds")),
                        draft.reorderIntervalTicks / 60f, 2f, 200f, 2f) * 60f);
                    y = Section(view.width, y, SimTranslation.T("RSR.UI.KitchenSupply"));
                    GUI.color = RestaurantUiStyle.MutedText;
                    Text.Anchor = TextAnchor.UpperLeft;
                    Text.WordWrap = true;
                    Widgets.Label(new Rect(8f, y, view.width - 16f, hintHeight), kitchenHint);
                    GUI.color = Color.white;
                }
                finally { Widgets.EndScrollView(); }
                if (RestaurantUiStyle.DrawSecondaryButton(new Rect(0, footer, 110f, h), SimTranslation.T("RSR.UI.Cancel"))) Close();
                if (RestaurantUiStyle.DrawPrimaryButton(new Rect(rect.width - 150f, footer, 150f, h), SimTranslation.T("RSR.UI.ConfirmParameters"))) Confirm();
            }
        }

        //绘制参数分组标题，职责是沿用框架表头底色并提供统一的上下间距。
        private static float Section(float width, float y, string title)
        {
            float h = RestaurantUiStyle.ControlHeight();
            var rect = new Rect(0, y, width, h);
            ShopUiVisualUtility.DrawTableHeaderBackground(rect);
            ShopUiVisualUtility.DrawCellLabel(new Rect(10f, y, width - 20f, h), title);
            return y + h + 8f;
        }

        //分开绘制参数名称、当前值和滑杆，职责是避免中文标签与滑杆轨道重叠。
        private static float Slider(float width, ref float y, string label, string value, float current, float minimum, float maximum, float step)
        {
            float line = RestaurantUiStyle.LineHeight(GameFont.Small);
            float h = RestaurantUiStyle.ControlHeight();
            ShopUiVisualUtility.DrawCellLabel(new Rect(8f, y, width - 190f, line), label);
            ShopUiVisualUtility.DrawCellLabel(new Rect(width - 176f, y, 168f, line), value, RestaurantUiStyle.Accent);
            float result = Widgets.HorizontalSlider(new Rect(8f, y + line + 4f, width - 16f, h), current, minimum, maximum, false, null, null, null, step);
            y += line + h + 12f;
            return result;
        }

        //回写参数副本，职责是仅改变父窗口草稿并保留菜单内容。
        private void Confirm()
        {
            target.enabled = draft.enabled;
            target.priceMultiplier = draft.priceMultiplier;
            target.useCustomerPreferences = draft.useCustomerPreferences;
            target.preferenceStrength = draft.preferenceStrength;
            target.maxServiceWaitTicks = draft.maxServiceWaitTicks;
            target.maxWaitTicks = draft.maxWaitTicks;
            target.maxOrderRounds = draft.maxOrderRounds;
            target.reorderIntervalTicks = draft.reorderIntervalTicks;
            Close();
        }
    }
}
