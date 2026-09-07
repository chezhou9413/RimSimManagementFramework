using System.Linq;
using RimSimRestaurantExtension.Models;
using RimSimRestaurantExtension.Tool;
using SimManagementLib.Api;
using UnityEngine;
using Verse;

namespace RimSimRestaurantExtension.UI
{
    //绘制框架内单店菜单表格，职责是复用窗口搜索和统一保存并把编辑状态保存在上下文。
    public class ShopPageWorker_RestaurantSettings : ShopManagerPageWorker
    {
        private const string StateKey = "RimSimRestaurant.Settings";

        //判断餐厅页面是否属于当前店铺，职责是按接待设施挂载页面。
        public override bool CanShow(ShopUiContext context)
        {
            return RestaurantOrderCreationUtility.FindOrderCounter((context as ShopManagerUiContext)?.Shop) != null;
        }

        //获取当前窗口草稿，职责是切页时保留编辑内容。
        private static RestaurantPageState State(ShopManagerUiContext context)
        {
            return context.GetOrCreatePageState(StateKey, () => new RestaurantPageState
            {
                draft = RestaurantOrderUtility.Settings.GetOrCreate(context.Shop.ID).Clone()
            });
        }

        //打开页面时建立一次草稿，职责是不覆盖未保存编辑。
        public override void OnOpen(ShopUiContext context)
        {
            if (context is ShopManagerUiContext shopContext && shopContext.Shop != null) State(shopContext);
        }

        //绘制参数入口与菜单表格，职责是保留单一列表滚动并使用框架样式。
        public override void DrawShopPage(Rect rect, ShopManagerUiContext context)
        {
            var state = State(context);
            using (new RestaurantGuiScope())
            {
                float row = RestaurantUiStyle.ControlHeight();
                Rect toolbar = new Rect(rect.x, rect.y, rect.width, row);
                Widgets.CheckboxLabeled(new Rect(toolbar.x, toolbar.y, 130f, row), "启用餐厅", ref state.draft.enabled);
                if (RestaurantUiStyle.DrawSecondaryButton(new Rect(toolbar.x + 142f, toolbar.y, 104f, row), "运行参数"))
                    Find.WindowStack.Add(new Dialog_RestaurantParameters(state.draft, context.Shop));
                if (RestaurantUiStyle.DrawPrimaryButton(new Rect(toolbar.xMax - 110f, toolbar.y, 110f, row), "添加菜品"))
                    OpenEditor(context, state, new RestaurantMenuItem { id = GameComp.RestaurantShopSettings.MakeMenuId() }, true);
                string issue = RestaurantBusinessAvailability.Snapshot(context.Shop);
                string status = issue.NullOrEmpty() ? "设施与人员检查通过；顾客在桌边点菜，吃完后结账。" : "经营提示：" + issue;
                float h = Text.CalcHeight(status, rect.width);
                Widgets.Label(new Rect(rect.x, toolbar.yMax + 6f, rect.width, h), status);
                var table = new Rect(rect.x, toolbar.yMax + h + 12f, rect.width, rect.height - row - h - 12f);
                DrawTable(table, context, state);
            }
        }

        //绘制紧凑表格，职责是将菜名、价格、份数、启用和制作状态放在同一行。
        private static void DrawTable(Rect rect, ShopManagerUiContext context, RestaurantPageState state)
        {
            float row = RestaurantUiStyle.ControlHeight() + 6f;
            float width = rect.width - 16f;
            DrawColumns(new Rect(rect.x, rect.y, width, row), null, null, true, state.draft.priceMultiplier);
            var items = state.draft.menuItems.Concat(Inventory.RestaurantProductMenuUtility.StockMenus(context.Shop)).Where(item => context.SearchText.NullOrEmpty()
                || (item.DisplayLabel + " " + item.mealDefName).IndexOf(context.SearchText, System.StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            var outer = new Rect(rect.x, rect.y + row, rect.width, Mathf.Max(0f, rect.height - row));
            var view = new Rect(0f, 0f, width, Mathf.Max(outer.height, items.Count * row));
            Widgets.BeginScrollView(outer, ref state.scroll, view);
            try
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    var line = new Rect(0f, i * row, width, row);
                    ShopUiVisualUtility.DrawTableRowBackground(line, i, state.selectedId == item.id);
                    string issue = MenuIssue(context, state, item);
                    DrawColumns(line, item, issue, false, state.draft.priceMultiplier);
                    if (RestaurantUiStyle.DrawSecondaryButton(new Rect(width - 92f, line.y + 3f, 54f, row - 6f), "编辑"))
                    {
                        state.selectedId = item.id;
                        if (item.IsStockProduct) Find.WindowStack.Add(new Dialog_RestaurantStorage(item.sourceCabinet));
                        else OpenEditor(context, state, item, false);
                    }
                    if (!item.IsStockProduct && RestaurantUiStyle.DrawSecondaryButton(new Rect(width - 32f, line.y + 3f, 32f, row - 6f), "…"))
                        Find.WindowStack.Add(new FloatMenu(new System.Collections.Generic.List<FloatMenuOption>
                        {
                            new FloatMenuOption("复制", () =>
                            {
                                var clone = item.Clone();
                                clone.id = GameComp.RestaurantShopSettings.MakeMenuId();
                                clone.label = item.DisplayLabel + " 副本";
                                state.draft.menuItems.Add(clone);
                            }),
                            new FloatMenuOption("删除", () => state.draft.menuItems.Remove(item))
                        }));
                }
            }
            finally { Widgets.EndScrollView(); }
        }

        //绘制列内容，职责是让变长中文标签按列宽截断并提供完整提示。
        private static void DrawColumns(Rect rect, RestaurantMenuItem item, string issue, bool header, float multiplier)
        {
            if (header) ShopUiVisualUtility.DrawTableHeaderBackground(rect);
            float nameWidth = Mathf.Max(80f, rect.width - 352f);
            string[] values = header ? new[] { "菜品与来源", "售价/份", "份数", "启用", "供货情况", "操作" }
                : new[] { (item.IsStockProduct ? "[现货] " + item.DisplayLabel + " · " + item.sourceCabinet.LabelCap : "[制作] " + item.DisplayLabel), (item.unitPrice * multiplier).ToString("F0"), item.minCount + "–" + item.maxCount, "", issue.NullOrEmpty() ? "可制作" : issue, "" };
            float[] widths = { nameWidth, 65f, 52f, 48f, 95f, 92f };
            float x = rect.x;
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.WordWrap = false;
            for (int i = 0; i < widths.Length; i++)
            {
                Rect cell = new Rect(x + 4f, rect.y, widths[i] - 8f, rect.height);
                if (!header && i == 3)
                {
                    if (item.IsStockProduct) Widgets.Label(cell, "在柜中");
                    else Widgets.CheckboxLabeled(cell, "", ref item.enabled);
                }
                else Widgets.Label(cell, values[i].Truncate(cell.width));
                if (!header) TooltipHandler.TipRegion(cell, values[i]);
                x += widths[i];
            }
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = true;
        }

        //缓存菜单状态，职责是每 120 Tick 检查一次库存与厨房而不逐帧寻路。
        private static string MenuIssue(ShopManagerUiContext context, RestaurantPageState state, RestaurantMenuItem item)
        {
            int now = Find.TickManager.TicksGame;
            if (state.menuStatus.TryGetValue(item.id, out var cached) && now - cached.tick < 120) return cached.issue;
            var preview = new RestaurantOrder { mealDef = item.MealDef, mealCount = item.minCount,
                ingredients = RestaurantIngredientUtility.BuildNeeds(item, item.minCount) };
            string issue = item.IsStockProduct
                ? Inventory.RestaurantProductMenuUtility.Available(context.Shop, item, item.minCount) ? "柜中有货" : "现货或路线不足"
                : !RestaurantIngredientUtility.HasIngredients(null, context.Shop, item, item.minCount)
                    ? "食材不足" : RestaurantBusinessAvailability.CheckOrder(context.Shop, preview);
            state.menuStatus[item.id] = (now, issue);
            return issue;
        }

        //打开独立菜单编辑窗口，职责是子窗口确认仅修改当前父窗口草稿。
        private static void OpenEditor(ShopManagerUiContext context, RestaurantPageState state, RestaurantMenuItem item, bool adding)
        {
            Find.WindowStack.Add(new Dialog_RestaurantMenuEditor(context.Shop, item, edited =>
            {
                if (context.GetPageState<RestaurantPageState>(StateKey) != state) return;
                if (adding) state.draft.menuItems.Add(edited);
                else
                {
                    int index = state.draft.menuItems.FindIndex(menu => menu.id == item.id);
                    if (index >= 0) state.draft.menuItems[index] = edited;
                }
                state.menuStatus.Clear();
            }));
        }

        //提交当前窗口草稿，职责是只有统一保存才改变游戏中的菜单和参数。
        public override void OnSave(ShopUiContext context)
        {
            var state = context.GetPageState<RestaurantPageState>(StateKey);
            var shop = (context as ShopManagerUiContext)?.Shop;
            if (state == null || shop == null) return;
            RestaurantOrderUtility.Settings.GetOrCreate(shop.ID).CopyFrom(state.draft);
            foreach (var cabinet in Inventory.RestaurantStockUtility.Cabinets(shop)) cabinet.MarkRestockQueueDirty(null, "后厨绑定变化");
            Inventory.RestaurantPantryConfiguration.Apply(shop);
            RestaurantBusinessAvailability.Reset();
            RestaurantMenuUtility.ResetSelections();
            shop.InvalidateShopRuntimeCache();
        }

        //说明保存语义，职责是帮助用户区分子窗口确认和实际提交。
        public override string GetSaveTip(ShopUiContext context)
        {
            return "菜单与参数在统一保存后生效；关闭窗口会丢弃未保存内容。";
        }
    }
}
