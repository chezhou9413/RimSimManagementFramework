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
        public override Vector2 InitialSize => new Vector2(Mathf.Min(1040f, Verse.UI.screenWidth - 48f), Mathf.Min(740f, Verse.UI.screenHeight - 48f));

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
                if (!cabinet.Spawned) { Close(); return; }
                float h = RestaurantUiStyle.ControlHeight(), row = h + 12f;
                float y = ShopUiVisualUtility.DrawPageHeading(rect, cabinet.LabelCap,
                    cabinet.IsRefrigerator ? "后厨库存 · 配置储存目标与补货阈值" : "餐厅商品 · 配置库存、售价与交付方式", true);
                int total = draft.Values.Where(d => d.enabled).Sum(d => d.count);
                y += ShopUiVisualUtility.DrawMetrics(new Rect(0, y, rect.width, 0),
                    new[] { "当前库存", "补货在途", "库存目标" },
                    new[] { cabinet.CountTotalStored() + " / " + cabinet.MaxTotalCapacity,
                        cabinet.CountTotalPendingIn(false) + " 件", total + " 件" }, rect.height < 580f) + 10f;
                string status = cabinet.IsRefrigerator ? cabinet.IsCooling ? "通电保鲜 · 库存供餐厅厨房取用。" : "供电中断 · 食品按环境温度腐坏。" : "上架商品进入餐厅菜单，库存与销售规则独立设置。";
                if (!cabinet.RestockSourceIssue.NullOrEmpty()) status += "\n" + cabinet.RestockSourceIssue;
                float statusHeight = ShopUiVisualUtility.NoticeHeight(status, rect.width);
                ShopUiVisualUtility.DrawNotice(new Rect(0, y, rect.width, statusHeight), status,
                    cabinet.IsRefrigerator && !cabinet.IsCooling || !cabinet.RestockSourceIssue.NullOrEmpty());
                y += statusHeight + 8f;
                string next = ShopUiVisualUtility.DrawSearchField(new Rect(0, y, rect.width, h), search, "搜索库存物品名称或来源模组");
                if (next != search) { search = next; scroll = Vector2.zero; }
                y += h + 10f;
                float footer = rect.height - h - RestaurantUiStyle.LineHeight(GameFont.Small) - 12f;
                float width = Mathf.Max(cabinet.IsRefrigerator ? 620f : 850f, rect.width - 16f);
                var shown = items.Where(t => RestaurantFoodUtility.MatchesSearch(t, search)).ToList();
                Rect outer = new Rect(0, y, rect.width, Mathf.Max(0f, footer - y - 10f));
                ShopUiVisualUtility.DrawSection(outer);
                if (shown.Count == 0) RestaurantBusinessUiUtility.DrawEmpty(outer, "没有匹配物品，请调整搜索条件。");
                else
                {
                    Rect view = new Rect(0, 0, width, Mathf.Max(outer.height - 16f, h + shown.Count * row));
                    Widgets.BeginScrollView(outer, ref scroll, view);
                    try
                    {
                        DrawRow(new Rect(0, 0, width, h), null, true);
                        for (int i = 0; i < shown.Count; i++)
                        {
                            var cell = new Rect(0, h + i * row, width, row);
                            ShopUiVisualUtility.DrawTableRowBackground(cell, i, draft[shown[i].defName].enabled);
                            DrawRow(cell, shown[i], false);
                        }
                    }
                    finally { Widgets.EndScrollView(); }
                }
                bool over = total > cabinet.MaxTotalCapacity;
                ShopUiVisualUtility.DrawCellLabel(new Rect(0, footer, rect.width, RestaurantUiStyle.LineHeight(GameFont.Small)),
                    over ? "目标库存超过货柜容量，请减少目标数量。" : "保存后生效；关闭丢弃本次更改。预留库存已分配给其他任务。",
                    over ? RestaurantUiStyle.Warning : RestaurantUiStyle.MutedText);
                float bottom = rect.height - h;
                if (RestaurantUiStyle.DrawSecondaryButton(new Rect(0, bottom, 170f, h), "按已保存目标补货"))
                    foreach (var item in cabinet.ActiveDefs) cabinet.Map.GetComponent<MapComponent_RestockTaskQueue>().ActivateRestockCycle(cabinet, item);
                if (RestaurantUiStyle.DrawSecondaryButton(new Rect(rect.width - 266f, bottom, 106f, h), "取消")) Close();
                if (RestaurantUiStyle.DrawPrimaryButton(new Rect(rect.width - 150f, bottom, 150f, h), "保存库存设置", !over)) Save();
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
                float control = RestaurantUiStyle.ControlHeight();
                Rect cell = new Rect(x + 3f, rect.center.y - control / 2f, widths[i] - 6f, control);
                x += widths[i];
                if (header) { ShopUiVisualUtility.DrawCellLabel(new Rect(cell.x, rect.y, cell.width, rect.height), labels[i], RestaurantUiStyle.MutedText); continue; }
                if (i == 0)
                {
                    RestaurantUiStyle.DrawThingIconOrMissing(new Rect(cell.x, cell.center.y - 16f, 32f, 32f), item);
                    ShopUiVisualUtility.DrawCellLabel(new Rect(cell.x + 38f, cell.y, cell.width - 38f, cell.height), item.LabelCap);
                }
                else if (i == 1) Widgets.CheckboxLabeled(cell, "", ref data.enabled);
                else if (i == 2) ShopUiVisualUtility.DrawCellLabel(cell, cabinet.CountStored(item).ToString());
                else if (i == 3) ShopUiVisualUtility.DrawCellLabel(cell, cabinet.CountReserved(item).ToString());
                else if (i == 4) ShopUiVisualUtility.DrawCellLabel(cell, cabinet.CountPending(item).ToString());
                else if (i == 5) Widgets.TextFieldNumeric(cell, ref data.count, ref data.countBuffer, 0, cabinet.MaxTotalCapacity);
                else if (i == 6) Widgets.TextFieldNumeric(cell, ref data.restockThreshold, ref data.restockThresholdBuffer, -1, data.count);
                else if (i == 7) Widgets.TextFieldNumeric(cell, ref data.price, ref data.priceBuffer, 1f, 100000f);
                else if (i == 8) Widgets.CheckboxLabeled(cell, "", ref rule.onSale);
                else if (i == 9 && RestaurantUiStyle.DrawSecondaryButton(cell, rule.mode == RestaurantDeliveryMode.Consume ? "现场吃喝" : "带走"))
                    rule.mode = rule.mode == RestaurantDeliveryMode.Consume ? RestaurantDeliveryMode.TakeAway : RestaurantDeliveryMode.Consume;
                else if (i == 10 && RestaurantUiStyle.DrawSecondaryButton(cell, rule.portions.ToString()))
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
