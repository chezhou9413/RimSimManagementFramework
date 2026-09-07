using RimSimRestaurantExtension.GameComp;
using System.Linq;
using RimWorld;
using SimManagementLib.SimZone;
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
        private readonly Zone_Shop shop;
        public override Vector2 InitialSize => new Vector2(540f, 510f);

        //建立参数副本，职责是让取消子窗口也能丢弃本次参数编辑。
        public Dialog_RestaurantParameters(RestaurantShopSettings target, Zone_Shop shop)
        {
            this.target = target;
            this.shop = shop;
            draft = target.Clone();
            doCloseX = true;
            absorbInputAroundWindow = true;
        }

        //绘制参数表单，职责是保留独立标题、滚动正文和确认区域。
        public override void DoWindowContents(Rect rect)
        {
            using (new RestaurantGuiScope())
            {
                float row = RestaurantUiStyle.ControlHeight() + 8f;
                Widgets.Label(new Rect(0f, 0f, rect.width - 32f, row), "餐厅运行参数");
                Rect outer = new Rect(0f, row, rect.width, Mathf.Max(0f, rect.height - row * 2f - 8f));
                Rect view = new Rect(0f, 0f, rect.width - 16f, Mathf.Max(outer.height, row * (9f + shop.Map.zoneManager.AllZones.OfType<Zone_Stockpile>().Count())));
                Widgets.BeginScrollView(outer, ref scroll, view);
                try
                {
                    float y = 0f;
                    Widgets.CheckboxLabeled(new Rect(0f, y, view.width, row - 8f), "启用餐厅", ref draft.enabled); y += row;
                    Widgets.CheckboxLabeled(new Rect(0f, y, view.width, row - 8f), "使用顾客个体偏好", ref draft.useCustomerPreferences); y += row;
                    draft.priceMultiplier = Widgets.HorizontalSlider(new Rect(0f, y, view.width, row - 8f), draft.priceMultiplier,
                        0.1f, 5f, false, $"价格倍率 {draft.priceMultiplier:F2}", "0.1", "5", 0.05f); y += row;
                    draft.preferenceStrength = Widgets.HorizontalSlider(new Rect(0f, y, view.width, row - 8f), draft.preferenceStrength,
                        0f, 2f, false, $"偏好影响 {draft.preferenceStrength:F1}", "0", "2", 0.1f); y += row;
                    draft.maxServiceWaitTicks = Mathf.RoundToInt(Widgets.HorizontalSlider(new Rect(0f, y, view.width, row - 8f),
                        draft.maxServiceWaitTicks, 6000f, 30000f, false, $"最长等待接单 {draft.maxServiceWaitTicks / 60} 秒", "100", "500", 600f)); y += row;
                    draft.maxWaitTicks = Mathf.RoundToInt(Widgets.HorizontalSlider(new Rect(0f, y, view.width, row - 8f),
                        draft.maxWaitTicks, 6000f, 30000f, false, $"最长等待上菜 {draft.maxWaitTicks / 60} 秒", "100", "500", 600f)); y += row;
                    draft.maxOrderRounds = Mathf.RoundToInt(Widgets.HorizontalSlider(new Rect(0, y, view.width, row - 8f),
                        draft.maxOrderRounds, 1, 5, false, $"最多接单 {draft.maxOrderRounds} 次（含首次）", "1", "5", 1)); y += row;
                    draft.reorderIntervalTicks = Mathf.RoundToInt(Widgets.HorizontalSlider(new Rect(0, y, view.width, row - 8f),
                        draft.reorderIntervalTicks, 120, 12000, false, $"追加间隔 {draft.reorderIntervalTicks} Tick", "120", "12000", 120)); y += row;
                    Widgets.Label(new Rect(0, y, view.width, row - 8f), "绑定后厨储存区（同时供厨房取料和货柜补货）"); y += row;
                    foreach (var zone in shop.Map.zoneManager.AllZones.OfType<Zone_Stockpile>())
                    {
                        bool selected = draft.pantryZoneIds.Contains(zone.ID);
                        Widgets.CheckboxLabeled(new Rect(0, y, view.width, row - 8f), zone.label + " · " + zone.CellCount + " 格", ref selected);
                        if (selected && !draft.pantryZoneIds.Contains(zone.ID)) draft.pantryZoneIds.Add(zone.ID);
                        if (!selected) draft.pantryZoneIds.Remove(zone.ID);
                        y += row;
                    }
                }
                finally { Widgets.EndScrollView(); }
                if (RestaurantUiStyle.DrawPrimaryButton(new Rect(rect.width - 140f, rect.height - row + 8f, 140f, row - 8f), "确认参数草稿"))
                {
                    target.enabled = draft.enabled;
                    target.priceMultiplier = draft.priceMultiplier;
                    target.useCustomerPreferences = draft.useCustomerPreferences;
                    target.preferenceStrength = draft.preferenceStrength;
                    target.maxServiceWaitTicks = draft.maxServiceWaitTicks;
                    target.maxWaitTicks = draft.maxWaitTicks;
                    target.maxOrderRounds = draft.maxOrderRounds;
                    target.reorderIntervalTicks = draft.reorderIntervalTicks;
                    target.pantryZoneIds = draft.pantryZoneIds.ToList();
                    Close();
                }
            }
        }
    }
}
