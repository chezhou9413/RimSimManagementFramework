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
                float y = ShopUiVisualUtility.DrawPageHeading(rect, "传送带上架管理",
                    (line.Shop?.label ?? "未归属餐厅") + " · 线路 " + line.id + " · 配置整条相连线路", true);
                int target = draft.Where(r => r.enabled).Sum(r => r.target);
                y += ShopUiVisualUtility.DrawMetrics(new Rect(0, y, rect.width, 0),
                    new[] { "餐盘库存", "补餐在途", "上架目标", "累计损耗" },
                    new[] { line.Occupied + " / " + line.segments.Count, ConveyorStockPlanner.PendingTotal(line) + " 盘",
                        target + " 盘", line.lostPlates + " 盘" }, rect.height < 580f) + 10f;
                string status = !line.SameShop ? "线路需要完整位于同一家餐厅，请调整经营区域。"
                    : !line.Powered ? "供电中断：运输与补餐已停止，顾客仍可取走面前的食品。"
                    : line.paused ? "运输正常 · 自动补餐已暂停。"
                    : "运输正常 · 厨师优先处理顾客订单，空闲时补餐。";
                if (!line.notice.NullOrEmpty()) status += "\n" + line.notice;
                float notice = ShopUiVisualUtility.NoticeHeight(status, rect.width);
                ShopUiVisualUtility.DrawNotice(new Rect(0, y, rect.width, notice), status,
                    !line.SameShop || !line.Powered || !line.notice.NullOrEmpty());
                TooltipHandler.TipRegion(new Rect(0, y, rect.width, notice), "累计损耗成本：" + line.wasteCost.ToString("F1"));
                y += notice + 10f;
                bool narrow = rect.width < 650f;
                float buttonWidth = narrow ? (rect.width - 8f) / 2f : 156f;
                if (RestaurantUiStyle.DrawPrimaryButton(new Rect(0, y, buttonWidth, h), "添加餐厅菜谱")) AddMenu();
                if (RestaurantUiStyle.DrawSecondaryButton(new Rect(buttonWidth + 8f, y, buttonWidth, h), "添加现成食品")) AddStock();
                if (narrow) y += h + 8f;
                Widgets.CheckboxLabeled(new Rect(narrow ? 0f : rect.width - 180f, y, 180f, h), "暂停自动补餐", ref paused);
                y += h + 8f;
                string nextSearch = ShopUiVisualUtility.DrawSearchField(new Rect(0, y, rect.width, h), search, "搜索菜品名称、食品名称或货源");
                if (nextSearch != search) { search = nextSearch; scroll = Vector2.zero; }
                y += h + 8f;
                float footerY = rect.height - h - RestaurantUiStyle.LineHeight(GameFont.Small) - 16f;
                table.Draw(new Rect(0, y, rect.width, Mathf.Max(0f, footerY - y - 10f)), line, draft, search, ref scroll);
                bool over = target > line.segments.Count;
                ShopUiVisualUtility.DrawCellLabel(new Rect(0, footerY, rect.width, RestaurantUiStyle.LineHeight(GameFont.Small)),
                    over ? "目标超过线路容量，请减少目标盘数后保存。" : "保存后生效；取消或关闭会丢弃本次更改。",
                    over ? RestaurantUiStyle.Warning : RestaurantUiStyle.MutedText);
                float bottom = rect.height - h;
                ShopUiVisualUtility.DrawCellLabel(new Rect(0, bottom, Mathf.Max(0f, rect.width - 280f), h),
                    "已启用 " + draft.Count(r => r.enabled) + " 项 · 目标 " + target + " / " + line.segments.Count + " 盘");
                if (RestaurantUiStyle.DrawSecondaryButton(new Rect(rect.width - 266f, bottom, 116f, h), "取消")) Close();
                if (RestaurantUiStyle.DrawPrimaryButton(new Rect(rect.width - 140f, bottom, 140f, h), "保存上架配置", !over)) Save();
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
            if (options.Count == 0) { Messages.Message("请先在餐厅菜单中配置菜谱。", RimWorld.MessageTypeDefOf.RejectInput, false); return; }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        //选择现成食品来源，职责是限定本店食品柜或绑定后厨。
        private void AddStock()
        {
            var shop = belt.Line.Shop;
            if (shop == null) return;
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("绑定后厨储存区", () => Find.WindowStack.Add(new Dialog_ConveyorFoodPicker(
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
            { Messages.Message("线路结构已变化，请重新打开配置。", RimWorld.MessageTypeDefOf.RejectInput, false); return; }
            if (draft.Any(r => r.Food == null || r.portions < 1 || r.portions > r.Food.stackLimit
                || r.target < 0 || r.price < 1f || float.IsNaN(r.price) || float.IsInfinity(r.price))
                || draft.Where(r => r.enabled).Sum(r => r.target) > line.segments.Count)
            { Messages.Message("食品、数量或售价无效，或目标盘数超过线路容量。", RimWorld.MessageTypeDefOf.RejectInput, false); return; }
            line.rules = draft.Select(r => r.Clone()).ToList();
            line.paused = paused;
            line.notice = "";
            RestaurantBusinessAvailability.Reset();
            Close();
        }
    }
}
