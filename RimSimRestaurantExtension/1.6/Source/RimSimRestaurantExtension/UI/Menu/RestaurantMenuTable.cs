using System;
using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.Models;
using RimSimRestaurantExtension.Tool;
using SimManagementLib.Api;
using UnityEngine;
using Verse;

namespace RimSimRestaurantExtension.UI
{
    //绘制餐厅菜单列表，职责是把食品身份、售价、供货状态和编辑操作分列展示。
    internal static class RestaurantMenuTable
    {
        //绘制可滚动的菜单表格，职责是复用框架搜索并保持窄窗口的最小可读列宽。
        internal static void Draw(Rect rect, ShopManagerUiContext context, RestaurantPageState state,
            Action<RestaurantMenuItem, bool> edit)
        {
            var items = state.draft.menuItems.Concat(Inventory.RestaurantProductMenuUtility.StockMenus(context.Shop))
                .Where(item => context.SearchText.NullOrEmpty() || (item.DisplayLabel + " " + item.mealDefName)
                    .IndexOf(context.SearchText, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            ShopUiVisualUtility.DrawSection(rect);
            if (items.Count == 0)
            {
                RestaurantBusinessUiUtility.DrawEmpty(rect, context.SearchText.NullOrEmpty()
                    ? "菜单还是空的\n添加菜品后，可设置售价、食材与每次点餐份数。" : "没有匹配菜品\n请调整商店管理窗口的搜索条件。");
                return;
            }
            float width = Mathf.Max(620f, rect.width - 16f);
            float header = RestaurantUiStyle.ControlHeight();
            float row = RestaurantUiStyle.LineHeight(GameFont.Small) * 2f + 16f;
            var view = new Rect(0, 0, width, Mathf.Max(rect.height - 16f, header + items.Count * row));
            Widgets.BeginScrollView(rect, ref state.scroll, view);
            try
            {
                DrawRow(new Rect(0, 0, width, header), null, "", state, edit, 0);
                for (int i = 0; i < items.Count; i++)
                    DrawRow(new Rect(0, header + i * row, width, row), items[i], Issue(context, state, items[i]), state, edit, i);
            }
            finally { Widgets.EndScrollView(); }
        }

        //绘制食品行及其操作，职责是保留长名称提示并用次级文字说明真实货源。
        private static void DrawRow(Rect rect, RestaurantMenuItem item, string issue, RestaurantPageState state,
            Action<RestaurantMenuItem, bool> edit, int index)
        {
            bool header = item == null;
            if (header) ShopUiVisualUtility.DrawTableHeaderBackground(rect);
            else ShopUiVisualUtility.DrawTableRowBackground(rect, index, state.selectedId == item.id);
            float[] widths = { rect.width - 384f, 70f, 54f, 50f, 106f, 104f };
            string[] labels = { "菜品与货源", "售价/份", "份数", "启用", "供货状态", "操作" };
            float x = rect.x, control = RestaurantUiStyle.ControlHeight(), h = RestaurantUiStyle.LineHeight(GameFont.Small);
            for (int i = 0; i < widths.Length; i++)
            {
                var cell = new Rect(x + 5f, rect.y, widths[i] - 10f, rect.height);
                x += widths[i];
                if (header) { ShopUiVisualUtility.DrawCellLabel(cell, labels[i], RestaurantUiStyle.MutedText); continue; }
                var field = new Rect(cell.x, cell.center.y - control / 2f, cell.width, control);
                if (i == 0)
                {
                    RestaurantUiStyle.DrawThingIconOrMissing(new Rect(cell.x, cell.center.y - 18f, 36f, 36f), item.MealDef);
                    ShopUiVisualUtility.DrawCellLabel(new Rect(cell.x + 44f, cell.y + 6f, cell.width - 44f, h), item.DisplayLabel);
                    ShopUiVisualUtility.DrawCellLabel(new Rect(cell.x + 44f, cell.y + h + 8f, cell.width - 44f, h),
                        item.IsStockProduct ? "现货 · " + item.sourceCabinet.LabelCap : "菜谱 · 厨房制作", RestaurantUiStyle.MutedText);
                }
                else if (i == 1) ShopUiVisualUtility.DrawCellLabel(cell, (item.unitPrice * state.draft.priceMultiplier).ToString("F1"));
                else if (i == 2) ShopUiVisualUtility.DrawCellLabel(cell, item.minCount + "–" + item.maxCount);
                else if (i == 3)
                {
                    if (item.IsStockProduct) ShopUiVisualUtility.DrawCellLabel(cell, "柜中", RestaurantUiStyle.MutedText);
                    else Widgets.CheckboxLabeled(field, "", ref item.enabled);
                }
                else if (i == 4)
                {
                    string status = !item.enabled && !item.IsStockProduct ? "已停用" : issue.NullOrEmpty() ? "可制作" : issue;
                    bool available = issue.NullOrEmpty() || issue == "柜中有货";
                    ShopUiVisualUtility.DrawCellLabel(cell, status, available ? RestaurantUiStyle.Good : RestaurantUiStyle.Warning);
                }
                else
                {
                    if (RestaurantUiStyle.DrawSecondaryButton(new Rect(field.x, field.y, 58f, field.height), "编辑"))
                    {
                        state.selectedId = item.id;
                        if (item.IsStockProduct) Find.WindowStack.Add(new Dialog_RestaurantStorage(item.sourceCabinet));
                        else edit(item, false);
                    }
                    if (!item.IsStockProduct && RestaurantUiStyle.DrawSecondaryButton(new Rect(field.xMax - 30f, field.y, 30f, field.height), "…"))
                        Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                        {
                            new FloatMenuOption("复制菜品", () =>
                            {
                                var clone = item.Clone();
                                clone.id = GameComp.RestaurantShopSettings.MakeMenuId();
                                clone.label = item.DisplayLabel + " 副本";
                                state.draft.menuItems.Add(clone);
                            }),
                            new FloatMenuOption("删除菜品", () => state.draft.menuItems.Remove(item))
                        }));
                }
            }
        }

        //缓存菜单供货提示，职责是避免每帧执行库存核对与厨房检查。
        private static string Issue(ShopManagerUiContext context, RestaurantPageState state, RestaurantMenuItem item)
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
    }
}
