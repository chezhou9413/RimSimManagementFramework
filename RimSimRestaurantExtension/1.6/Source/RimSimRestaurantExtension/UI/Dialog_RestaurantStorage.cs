using RimWorld;
using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.Buildings;
using RimSimRestaurantExtension.Inventory;
using RimSimRestaurantExtension.Tool;
using SimManagementLib.Api;
using SimManagementLib.SimMapComp;
using SimManagementLib.SimThingComp;
using UnityEngine;
using Verse;
namespace RimSimRestaurantExtension.UI
{
    //编辑餐厅货柜库存草稿，职责是统一筛选、补货目标、上架与交付方式并在保存后生效。
    public sealed class Dialog_RestaurantStorage : Window
    {
        private readonly Building_RestaurantStorage cabinet;
        private readonly Dictionary<string, GoodsItemData> draft;
        private readonly Dictionary<ThingDef, RestaurantStockRule> rules;
        private readonly List<ThingDef> items;
        private Vector2 scroll;
        private string search = "";
        public override Vector2 InitialSize => new Vector2(880f, 660f);

        //复制库存与上架规则，职责是让关闭窗口丢弃全部未提交内容。
        public Dialog_RestaurantStorage(Building_RestaurantStorage cabinet)
        {
            this.cabinet = cabinet;
            draft = cabinet.Goods.CloneItemData();
            items = cabinet.ActiveDefs.OrderBy(t => t.label).ToList();
            rules = cabinet.saleRules.ToDictionary(r => r.item, r => r.Clone());
            foreach (var item in items)
            {
                var product = cabinet.Product(item);
                if (!draft.ContainsKey(item.defName)) draft[item.defName] = new GoodsItemData
                { count = 1, price = product.defaultPrice > 0 ? product.defaultPrice : Mathf.Max(1f, item.BaseMarketValue * 1.5f) };
                if (!rules.ContainsKey(item)) rules[item] = new RestaurantStockRule
                { item = item, mode = product.deliveryMode, portions = product.defaultCount };
            }
            doCloseX = true;
            closeOnAccept = false;
            absorbInputAroundWindow = true;
        }

        //绘制统一表格，职责是为标题、搜索、正文和保存栏分别保留空间。
        public override void DoWindowContents(Rect rect)
        {
            using (new RestaurantGuiScope())
            {
                float row = RestaurantUiStyle.ControlHeight() + 6f;
                Widgets.Label(new Rect(0, 0, rect.width - 32f, row), cabinet.LabelCap);
                string status = $"现存 {cabinet.CountTotalStored()} / {cabinet.MaxTotalCapacity} 件 · 在途 {cabinet.CountTotalPendingIn(false)}"
                    + (cabinet.IsRefrigerator ? cabinet.IsCooling ? " · 通电保鲜" : " · 断电，按环境温度腐坏" : " · 餐厅配送商品")
                    + (cabinet.RestockSourceIssue.NullOrEmpty() ? "" : "\n" + cabinet.RestockSourceIssue);
                float statusHeight = Text.CalcHeight(status, rect.width);
                Widgets.Label(new Rect(0, row, rect.width, statusHeight), status);
                float y = row + statusHeight + 6f;
                Widgets.Label(new Rect(0, y, 54f, row), "搜索");
                search = Widgets.TextField(new Rect(58f, y, rect.width - 58f, row - 6f), search);
                y += row;
                float width = rect.width - 16f;
                DrawRow(new Rect(0, y, width, row), null, true);
                y += row;
                var shown = items.Where(t => RestaurantFoodUtility.MatchesSearch(t, search)).ToList();
                Rect outer = new Rect(0, y, rect.width, Mathf.Max(0, rect.height - y - row - 8f));
                Rect view = new Rect(0, 0, width, Mathf.Max(outer.height, shown.Count * row));
                Widgets.BeginScrollView(outer, ref scroll, view);
                try
                {
                    for (int i = 0; i < shown.Count; i++)
                    {
                        var line = new Rect(0, i * row, width, row);
                        ShopUiVisualUtility.DrawTableRowBackground(line, i, false);
                        DrawRow(line, shown[i], false);
                    }
                }
                finally { Widgets.EndScrollView(); }
                float bottom = rect.height - row + 3f;
                if (RestaurantUiStyle.DrawSecondaryButton(new Rect(0, bottom, 160f, row - 6f), "按已保存目标补货"))
                    foreach (var item in cabinet.ActiveDefs) cabinet.Map.GetComponent<MapComponent_RestockTaskQueue>().ActivateRestockCycle(cabinet, item);
                Widgets.Label(new Rect(170f, bottom, rect.width - 360f, row), "关闭丢弃草稿；总目标不得超过容量");
                if (RestaurantUiStyle.DrawPrimaryButton(new Rect(rect.width - 150f, bottom, 150f, row - 6f), "保存库存设置")) Save();
            }
        }

        //绘制物品与库存列，职责是按实际字体高度布局数量和销售控件。
        private void DrawRow(Rect rect, ThingDef item, bool header)
        {
            float extra = cabinet.IsRefrigerator ? 0f : 231f;
            float[] widths = { rect.width - 304f - extra, 44f, 48f, 48f, 48f, 58f, 58f, 62f, 42f, 85f, 42f };
            string[] labels = { "物品", "允许", "现存", "预留", "途中", "目标", "阈值", "单价", "上架", "交付", "份数" };
            if (header) ShopUiVisualUtility.DrawTableHeaderBackground(rect);
            GoodsItemData data = item == null ? null : draft[item.defName];
            RestaurantStockRule rule = item == null ? null : rules[item];
            float x = rect.x;
            for (int i = 0; i < (cabinet.IsRefrigerator ? 7 : 11); i++)
            {
                Rect cell = new Rect(x + 3f, rect.y + 3f, widths[i] - 6f, rect.height - 6f);
                x += widths[i];
                if (header) { Widgets.Label(cell, labels[i]); continue; }
                if (i == 0) { Widgets.Label(cell, item.LabelCap.ToString().Truncate(cell.width)); TooltipHandler.TipRegion(cell, item.LabelCap + "\n" + item.defName); }
                else if (i == 1) Widgets.CheckboxLabeled(cell, "", ref data.enabled);
                else if (i == 2) Widgets.Label(cell, cabinet.CountStored(item).ToString());
                else if (i == 3) Widgets.Label(cell, cabinet.CountReserved(item).ToString());
                else if (i == 4) Widgets.Label(cell, cabinet.CountPending(item).ToString());
                else if (i == 5) Widgets.TextFieldNumeric(cell, ref data.count, ref data.countBuffer, 0, cabinet.MaxTotalCapacity);
                else if (i == 6) Widgets.TextFieldNumeric(cell, ref data.restockThreshold, ref data.restockThresholdBuffer, -1, data.count);
                else if (i == 7) Widgets.TextFieldNumeric(cell, ref data.price, ref data.priceBuffer, 1f, 100000f);
                else if (i == 8) Widgets.CheckboxLabeled(cell, "", ref rule.onSale);
                else if (i == 9 && Widgets.ButtonText(cell, rule.mode == RestaurantDeliveryMode.Consume ? "现场吃喝" : "带走"))
                    rule.mode = rule.mode == RestaurantDeliveryMode.Consume ? RestaurantDeliveryMode.TakeAway : RestaurantDeliveryMode.Consume;
                else if (i == 10 && Widgets.ButtonText(cell, rule.portions.ToString()))
                    Find.WindowStack.Add(new FloatMenu(Enumerable.Range(1, Mathf.Min(10, item.stackLimit))
                        .Select(n => new FloatMenuOption(n.ToString(), () => rule.portions = n)).ToList()));
            }
        }

        //验证并提交库存草稿，职责是清理配置失效的预留并通知补货和菜单快照。
        private void Save()
        {
            if (!cabinet.Spawned) { Close(); return; }
            if (draft.Values.Where(d => d.enabled).Sum(d => d.count) > cabinet.MaxTotalCapacity)
            { Messages.Message("目标库存总数超过货柜共享容量", MessageTypeDefOf.RejectInput, false); return; }
            var invalid = rules.Values.FirstOrDefault(r => r.onSale && r.mode == RestaurantDeliveryMode.Consume && r.item.ingestible == null);
            if (invalid != null) { Messages.Message(invalid.item.defName + " 无法现场吃喝", MessageTypeDefOf.RejectInput, false); return; }
            cabinet.Goods.ApplySettings(cabinet.CatalogId, draft);
            cabinet.saleRules = rules.Values.Select(r => r.Clone()).ToList();
            RestaurantStorageConfiguration.Apply(cabinet);
            Close();
        }
    }
}
