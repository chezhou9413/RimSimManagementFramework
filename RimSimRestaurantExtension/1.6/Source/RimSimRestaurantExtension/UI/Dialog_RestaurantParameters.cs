using System.Linq;
using RimSimRestaurantExtension.GameComp;
using RimWorld;
using SimManagementLib.Api;
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
        private readonly Zone_Shop shop;
        private Vector2 scroll;
        public override Vector2 InitialSize => new Vector2(Mathf.Min(620f, Verse.UI.screenWidth - 48f), Mathf.Min(740f, Verse.UI.screenHeight - 48f));

        //建立参数副本，职责是让取消子窗口丢弃本次参数编辑。
        public Dialog_RestaurantParameters(RestaurantShopSettings target, Zone_Shop shop)
        {
            this.target = target;
            this.shop = shop;
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
                float top = ShopUiVisualUtility.DrawPageHeading(rect, "餐厅运行参数", "确认返回商店管理后，点击统一保存才会生效。", true);
                float footer = rect.height - h;
                var zones = shop.Map.zoneManager.AllZones.OfType<Zone_Stockpile>().ToList();
                float line = RestaurantUiStyle.LineHeight(GameFont.Small);
                float height = 3f * (h + 8f) + 2f * (h + 8f) + 6f * (line + h + 12f)
                    + Mathf.Max(1, zones.Count) * (h + 6f) + line + 12f;
                Rect outer = new Rect(0, top, rect.width, Mathf.Max(0f, footer - top - 12f));
                Rect view = new Rect(0, 0, rect.width - 16f, Mathf.Max(outer.height, height));
                Widgets.BeginScrollView(outer, ref scroll, view);
                try
                {
                    float y = Section(view.width, 0f, "经营与定价");
                    Widgets.CheckboxLabeled(new Rect(8f, y, view.width - 16f, h), "启用餐厅服务", ref draft.enabled); y += h + 8f;
                    Widgets.CheckboxLabeled(new Rect(8f, y, view.width - 16f, h), "按顾客偏好选择菜品", ref draft.useCustomerPreferences); y += h + 8f;
                    draft.priceMultiplier = Slider(view.width, ref y, "菜单价格倍率", draft.priceMultiplier.ToString("F2") + " 倍", draft.priceMultiplier, 0.1f, 5f, 0.05f);
                    draft.preferenceStrength = Slider(view.width, ref y, "顾客偏好影响", draft.preferenceStrength.ToString("F1"), draft.preferenceStrength, 0f, 2f, 0.1f);
                    y = Section(view.width, y, "用餐与等待");
                    draft.maxServiceWaitTicks = Mathf.RoundToInt(Slider(view.width, ref y, "最长等待接单", draft.maxServiceWaitTicks / 60 + " 秒",
                        draft.maxServiceWaitTicks / 60f, 100f, 500f, 10f) * 60f);
                    draft.maxWaitTicks = Mathf.RoundToInt(Slider(view.width, ref y, "最长等待餐品", draft.maxWaitTicks / 60 + " 秒",
                        draft.maxWaitTicks / 60f, 100f, 500f, 10f) * 60f);
                    draft.maxOrderRounds = Mathf.RoundToInt(Slider(view.width, ref y, "每次用餐最多轮数", draft.maxOrderRounds + " 轮（含首次）",
                        draft.maxOrderRounds, 1f, 5f, 1f));
                    draft.reorderIntervalTicks = Mathf.RoundToInt(Slider(view.width, ref y, "追加用餐间隔", draft.reorderIntervalTicks / 60 + " 秒",
                        draft.reorderIntervalTicks / 60f, 2f, 200f, 2f) * 60f);
                    y = Section(view.width, y, "后厨货源");
                    ShopUiVisualUtility.DrawCellLabel(new Rect(8f, y, view.width - 16f, line), "绑定的储存区同时供厨房取料、传送带现货与货柜补货。", RestaurantUiStyle.MutedText);
                    y += line + 12f;
                    if (zones.Count == 0)
                        ShopUiVisualUtility.DrawCellLabel(new Rect(8f, y, view.width - 16f, h), "地图上还没有储存区。", RestaurantUiStyle.MutedText);
                    for (int i = 0; i < zones.Count; i++)
                    {
                        var zone = zones[i];
                        var row = new Rect(0, y, view.width, h);
                        bool selected = draft.pantryZoneIds.Contains(zone.ID);
                        ShopUiVisualUtility.DrawTableRowBackground(row, i, selected);
                        Widgets.CheckboxLabeled(new Rect(8f, y, view.width - 16f, h), (zone.label + " · " + zone.CellCount + " 格").Truncate(view.width - 56f), ref selected);
                        TooltipHandler.TipRegion(row, zone.label);
                        if (selected && !draft.pantryZoneIds.Contains(zone.ID)) draft.pantryZoneIds.Add(zone.ID);
                        if (!selected) draft.pantryZoneIds.Remove(zone.ID);
                        y += h + 6f;
                    }
                }
                finally { Widgets.EndScrollView(); }
                if (RestaurantUiStyle.DrawSecondaryButton(new Rect(0, footer, 110f, h), "取消")) Close();
                if (RestaurantUiStyle.DrawPrimaryButton(new Rect(rect.width - 150f, footer, 150f, h), "确认参数草稿")) Confirm();
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
            target.pantryZoneIds = draft.pantryZoneIds.ToList();
            Close();
        }
    }
}
