using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.Conveyor.Stocking;
using RimSimRestaurantExtension.Conveyor.Transport;
using RimSimRestaurantExtension.Inventory;
using RimSimRestaurantExtension.Tool;
using RimSimRestaurantExtension.UI;
using SimManagementLib.Api;
using UnityEngine;
using Verse;

namespace RimSimRestaurantExtension.Conveyor.UI
{
    //编辑整条线路上架草稿，职责是一次提交规则并显示现存、在途和损耗。
    public sealed class Dialog_ConveyorStock : Window
    {
        private readonly Building_SushiConveyor belt;
        private readonly ConveyorLine original;
        private readonly List<int> members;
        private readonly List<ConveyorStockRule> draft;
        private readonly ConveyorStockTable table = new ConveyorStockTable();
        private Vector2 scroll;
        private string search = "";
        private bool paused;
        public override Vector2 InitialSize => new Vector2(Mathf.Min(1040f, Verse.UI.screenWidth - 48f), Mathf.Min(760f, Verse.UI.screenHeight - 48f));

        //复制当前线路规则，职责是让关闭窗口保留原运行配置。
        public Dialog_ConveyorStock(Building_SushiConveyor belt)
        {
            this.belt = belt; original = belt.Line;
            members = original.segments.Select(b => b.thingIDNumber).OrderBy(i => i).ToList();
            draft = original.rules.Select(r => r.Clone()).ToList();
            paused = original.paused;
            doCloseX = true; absorbInputAroundWindow = true; closeOnAccept = false;
            draggable = true;
        }

        //绘制线路工作台，职责是分开展示运行摘要、上架表格和草稿提交操作。
        public override void DoWindowContents(Rect rect)
        {
            using (new RestaurantGuiScope())
            {
                if (!belt.Spawned) { Close(); return; }
                var line = belt.Line;
                float h = RestaurantUiStyle.ControlHeight();
                float y = ShopUiVisualUtility.DrawPageHeading(rect, SimTranslation.T("RSR.UI.ConveyorStockHeading"),
                    SimTranslation.T("RSR.UI.LineHeading", (line.Shop?.label ?? SimTranslation.T("RSR.UI.NoRestaurant")).Named("shop"), (line.id).Named("id")), true);
                int target = draft.Where(r => r.enabled).Sum(r => r.target);
                y += ShopUiVisualUtility.DrawMetrics(new Rect(0, y, rect.width, 0),
                    new[] { SimTranslation.T("RSR.UI.PlateStock"), SimTranslation.T("RSR.UI.PendingPlates"), SimTranslation.T("RSR.UI.PlateTarget"), SimTranslation.T("RSR.UI.TotalWaste") },
                    new[] { line.Occupied + " / " + line.segments.Count, SimTranslation.T("RSR.UI.PlateCount", (ConveyorStockPlanner.PendingTotal(line)).Named("count")),
                        SimTranslation.T("RSR.UI.PlateCount", (target).Named("count")), SimTranslation.T("RSR.UI.PlateCount", (line.lostPlates).Named("count")) }, rect.height < 580f) + 10f;
                string status = !line.SameShop ? SimTranslation.T("RSR.UI.ConveyorAreaHint")
                    : !line.Powered ? SimTranslation.T("RSR.UI.ConveyorPowerHint")
                    : line.paused ? SimTranslation.T("RSR.UI.ConveyorPausedHint")
                    : SimTranslation.T("RSR.UI.ConveyorActiveHint");
                if (!line.notice.NullOrEmpty()) status += "\n" + line.notice;
                float notice = ShopUiVisualUtility.NoticeHeight(status, rect.width);
                ShopUiVisualUtility.DrawNotice(new Rect(0, y, rect.width, notice), status,
                    !line.SameShop || !line.Powered || !line.notice.NullOrEmpty());
                TooltipHandler.TipRegion(new Rect(0, y, rect.width, notice), SimTranslation.T("RSR.UI.WasteCost", (line.wasteCost.ToString("F1")).Named("cost")));
                y += notice + 10f;
                bool narrow = rect.width < 650f;
                float buttonWidth = narrow ? (rect.width - 8f) / 2f : 156f;
                if (RestaurantUiStyle.DrawPrimaryButton(new Rect(0, y, buttonWidth, h), SimTranslation.T("RSR.UI.AddRecipe"))) AddMenu();
                if (RestaurantUiStyle.DrawSecondaryButton(new Rect(buttonWidth + 8f, y, buttonWidth, h), SimTranslation.T("RSR.UI.AddStockFood"))) AddStock();
                if (narrow) y += h + 8f;
                RestaurantUiStyle.DrawCheckbox(new Rect(narrow ? 0f : rect.width - 180f, y, 180f, h), SimTranslation.T("RSR.UI.PauseRestock"), ref paused);
                y += h + 8f;
                string nextSearch = ShopUiVisualUtility.DrawSearchField(new Rect(0, y, rect.width, h), search, SimTranslation.T("RSR.UI.SearchConveyorFood"));
                if (nextSearch != search) { search = nextSearch; scroll = Vector2.zero; }
                y += h + 8f;
                float footerY = rect.height - h - RestaurantUiStyle.LineHeight(GameFont.Small) - 16f;
                table.Draw(new Rect(0, y, rect.width, Mathf.Max(0f, footerY - y - 10f)), line, draft, search, ref scroll);
                bool over = target > line.segments.Count;
                ShopUiVisualUtility.DrawCellLabel(new Rect(0, footerY, rect.width, RestaurantUiStyle.LineHeight(GameFont.Small)),
                    over ? SimTranslation.T("RSR.UI.PlateOverCapacity") : SimTranslation.T("RSR.UI.ConveyorSaveHint"),
                    over ? RestaurantUiStyle.Warning : RestaurantUiStyle.MutedText);
                float bottom = rect.height - h;
                ShopUiVisualUtility.DrawCellLabel(new Rect(0, bottom, Mathf.Max(0f, rect.width - 280f), h),
                    SimTranslation.T("RSR.UI.PlateTargetSummary", (draft.Count(r => r.enabled)).Named("count"), (target).Named("target"), (line.segments.Count).Named("capacity")));
                if (RestaurantUiStyle.DrawSecondaryButton(new Rect(rect.width - 266f, bottom, 116f, h), SimTranslation.T("RSR.UI.Cancel"))) Close();
                if (RestaurantUiStyle.DrawPrimaryButton(new Rect(rect.width - 140f, bottom, 140f, h), SimTranslation.T("RSR.UI.SaveConveyor"), !over)) Save();
            }
        }

        //选择已有菜谱的独立快照，职责是保持线路售价和目标独立。
        private void AddMenu()
        {
            var shop = belt.Line.Shop;
            if (shop == null) return;
            var settings = RestaurantOrderUtility.Settings.GetOrCreate(shop.ID);
            var menus = RestaurantMenuUtility.GetEnabledMenuItems(settings);
            var options = menus.Select(m => new FloatMenuOption(m.DisplayLabel, () =>
                draft.Add(new ConveyorStockRule { menu = m.Clone(), price = Mathf.Max(1f, m.unitPrice * settings.priceMultiplier) }))).ToList();
            if (options.Count == 0) { Messages.Message(SimTranslation.T("RSR.UI.ConfigureMenuFirst"), RimWorld.MessageTypeDefOf.RejectInput, false); return; }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        //选择现成食品来源，职责是限定本店食品柜或店内储存架。
        private void AddStock()
        {
            var shop = belt.Line.Shop;
            if (shop == null) return;
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption(SimTranslation.T("RSR.UI.ShopShelves"), () => Find.WindowStack.Add(new Dialog_ConveyorFoodPicker(
                    DefDatabase<ThingDef>.AllDefsListForReading, food => draft.Add(new ConveyorStockRule
                    { food = food, price = Mathf.Max(1f, food.BaseMarketValue * 1.5f) }))))
            };
            foreach (var cabinet in RestaurantStockUtility.Cabinets(shop).Where(c => !c.IsRefrigerator))
            {
                var captured = cabinet;
                options.Add(new FloatMenuOption(captured.LabelCap + "（" + captured.Position + "）", () =>
                    Find.WindowStack.Add(new Dialog_ConveyorFoodPicker(captured.ActiveDefs, food => draft.Add(new ConveyorStockRule
                    { food = food, cabinet = captured, price = Mathf.Max(1f, captured.Goods.FindItemData(food)?.price ?? food.BaseMarketValue) })))));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        //核验拓扑与容量后提交，职责是避免窗口打开期间的拆分合并覆盖另一条线路。
        private void Save()
        {
            var line = belt.Line;
            if (line != original || !line.segments.Select(b => b.thingIDNumber).OrderBy(i => i).SequenceEqual(members))
            { Messages.Message(SimTranslation.T("RSR.UI.LineChanged"), RimWorld.MessageTypeDefOf.RejectInput, false); return; }
            if (draft.Any(r => r.Food == null || r.portions < 1 || r.portions > r.Food.stackLimit
                || r.target < 0 || r.price < 1f || float.IsNaN(r.price) || float.IsInfinity(r.price))
                || draft.Where(r => r.enabled).Sum(r => r.target) > line.segments.Count)
            { Messages.Message(SimTranslation.T("RSR.UI.InvalidConveyorStock"), RimWorld.MessageTypeDefOf.RejectInput, false); return; }
            line.rules = draft.Select(r => r.Clone()).ToList();
            line.paused = paused;
            line.notice = "";
            RestaurantBusinessAvailability.Reset();
            Close();
        }
    }
}
