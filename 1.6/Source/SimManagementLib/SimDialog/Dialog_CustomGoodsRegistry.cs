using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.SimDef;
using SimManagementLib.Tool;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    /// <summary>
    /// Provides the in-game editor for player-defined goods categories and item links.
    /// </summary>
    public partial class Dialog_CustomGoodsRegistry : Window
    {
        private const float HeaderHeight = 76f;
        private const float SidebarWidth = 290f;
        private const float SidebarFooterHeight = 88f;
        private const float SectionGap = 14f;
        private const float CategoryRowHeight = 70f;
        private const float ItemCardHeight = 102f;
        private const float ItemCardMinWidth = 190f;
        private const float BrowserRowHeight = 48f;
        private const int BrowserPageSize = 14;
        private const float ScrollbarWidth = 16f;
        private const float CloseXReservedWidth = Widgets.CloseButtonSize + Widgets.CloseButtonMargin * 2f + 18f;

        private static readonly Color WindowBg = new Color(0.10f, 0.11f, 0.13f, 1f);
        private static readonly Color PanelBg = new Color(0.15f, 0.17f, 0.20f, 0.95f);
        private static readonly Color Accent = new Color(0.18f, 0.69f, 0.87f, 1f);
        private static readonly Color SoftAccent = new Color(0.18f, 0.69f, 0.87f, 0.12f);
        private static readonly Color MutedText = new Color(0.73f, 0.77f, 0.82f, 1f);
        private static readonly Color BuiltInBadge = new Color(0.21f, 0.49f, 0.78f, 0.22f);
        private static readonly Color CustomBadge = new Color(0.17f, 0.72f, 0.48f, 0.20f);
        private static readonly Color LockedBadge = new Color(0.90f, 0.66f, 0.24f, 0.20f);

        private CustomGoodsDatabaseData draftData;
        private List<RuntimeGoodsCategory> previewCategories = new List<RuntimeGoodsCategory>();
        private List<ThingDef> allCandidateThings = new List<ThingDef>();

        private string selectedCategoryId = string.Empty;
        private string newCategoryLabelBuffer = string.Empty;
        private string selectedCategoryLabelBuffer = string.Empty;
        private string browserSearch = string.Empty;

        private Vector2 categoryScroll;
        private Vector2 currentItemsScroll;
        private Vector2 browserScroll;
        private int browserPageIndex;
        private bool dirty;

        public override Vector2 InitialSize => new Vector2(1380f, 860f);

        /// <summary>
        /// Initializes the registry window and loads the current custom goods draft.
        /// </summary>
        public Dialog_CustomGoodsRegistry()
        {
            forcePause = true;
            absorbInputAroundWindow = true;
            doCloseX = true;
            draggable = true;
            resizeable = true;
            closeOnClickedOutside = false;

            LoadDraft();
        }

        /// <summary>
        /// Draws the full registry window while restoring global IMGUI state after custom painting.
        /// </summary>
        public override void DoWindowContents(Rect inRect)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWordWrap = Text.WordWrap;
            Color oldColor = GUI.color;

            try
            {
                Text.WordWrap = true;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;

                Widgets.DrawBoxSolid(inRect, WindowBg);

                float headerHeight = Mathf.Max(HeaderHeight, Text.LineHeightOf(GameFont.Medium) + Text.LineHeightOf(GameFont.Tiny) + 30f);
                Rect headerRect = new Rect(inRect.x, inRect.y, inRect.width, headerHeight);
                Rect bodyRect = new Rect(inRect.x, headerRect.yMax + 8f, inRect.width, Mathf.Max(0f, inRect.height - headerHeight - 8f));
                float sidebarWidth = Mathf.Min(SidebarWidth, Mathf.Max(220f, bodyRect.width * 0.34f));
                Rect sidebarRect = new Rect(bodyRect.x, bodyRect.y, sidebarWidth, bodyRect.height);
                Rect contentRect = new Rect(sidebarRect.xMax + SectionGap, bodyRect.y, Mathf.Max(0f, bodyRect.width - sidebarWidth - SectionGap), bodyRect.height);

                DrawHeader(headerRect);
                DrawSidebar(sidebarRect);
                DrawContent(contentRect);
            }
            finally
            {
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
                Text.WordWrap = oldWordWrap;
                GUI.color = oldColor;
            }
        }

        /// <summary>
        /// Draws the title, explanatory text, and import/export/save actions.
        /// </summary>
        private void DrawHeader(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, PanelBg);
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.08f));

            float actionWidth = 500f;
            float textWidth = Mathf.Max(240f, rect.width - actionWidth - CloseXReservedWidth - 44f);
            Rect titleRect = new Rect(rect.x + 20f, rect.y + 8f, textWidth, Text.LineHeightOf(GameFont.Medium) + 4f);

            Text.Font = GameFont.Medium;
            GUI.color = Color.white;
            Widgets.Label(titleRect, "自定义商品注册");

            Text.Font = GameFont.Tiny;
            GUI.color = MutedText;
            Rect descRect = new Rect(rect.x + 20f, titleRect.yMax + 4f, textWidth, Text.LineHeightOf(GameFont.Tiny) * 2f + 4f);
            Widgets.Label(
                descRect,
                "原版 Def 商品只读。玩家本地 JSON 会在 Def 之后加载，并合并进运行时商品目录。"
            );

            float buttonY = rect.y + 18f;
            float right = rect.xMax - 16f - CloseXReservedWidth;

            right -= 116f;
            if (SimUiStyle.DrawPrimaryButton(new Rect(right, buttonY, 116f, 34f), dirty ? "保存*" : "保存"))
                SaveDraft();

            right -= 124f;
            if (SimUiStyle.DrawSecondaryButton(new Rect(right, buttonY, 112f, 34f), "重载"))
                ConfirmReload();

            right -= 124f;
            if (SimUiStyle.DrawSecondaryButton(new Rect(right, buttonY, 112f, 34f), "导出 B64"))
                Find.WindowStack.Add(new Dialog_CustomGoodsTransfer(CustomGoodsDatabase.ExportBase64(draftData)));

            right -= 124f;
            if (SimUiStyle.DrawSecondaryButton(new Rect(right, buttonY, 112f, 34f), "导入 B64"))
                Find.WindowStack.Add(new Dialog_CustomGoodsImport(HandleImportReplace));

            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }

        /// <summary>
        /// Draws the goods category list and the new-category input footer.
        /// </summary>
        private void DrawSidebar(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, PanelBg);
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.08f));

            float titleHeight = Text.LineHeightOf(GameFont.Small) + 6f;
            Rect titleRect = new Rect(rect.x + 16f, rect.y + 12f, rect.width - 32f, titleHeight);
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            Widgets.Label(titleRect, "商品类型");

            Rect listRect = new Rect(rect.x + 12f, titleRect.yMax + 8f, rect.width - 24f, Mathf.Max(0f, rect.height - SidebarFooterHeight - titleRect.height - 26f));
            Rect footerRect = new Rect(rect.x + 12f, rect.yMax - SidebarFooterHeight, rect.width - 24f, SidebarFooterHeight - 12f);

            float viewHeight = Mathf.Max(listRect.height, previewCategories.Count * (CategoryRowHeight + 6f));
            Rect viewRect = new Rect(0f, 0f, Mathf.Max(0f, listRect.width - ScrollbarWidth), viewHeight);

            Widgets.BeginScrollView(listRect, ref categoryScroll, viewRect);
            float y = 0f;
            for (int i = 0; i < previewCategories.Count; i++)
            {
                RuntimeGoodsCategory category = previewCategories[i];
                Rect rowRect = new Rect(0f, y, viewRect.width, CategoryRowHeight);
                DrawCategoryRow(rowRect, category);
                y += CategoryRowHeight + 6f;
            }
            Widgets.EndScrollView();

            Widgets.DrawBoxSolid(footerRect, new Color(1f, 1f, 1f, 0.03f));
            SimUiStyle.DrawBorder(footerRect, new Color(1f, 1f, 1f, 0.06f));
            Text.Font = GameFont.Tiny;
            GUI.color = MutedText;
            Widgets.Label(new Rect(footerRect.x + 10f, footerRect.y + 8f, footerRect.width - 20f, Text.LineHeightOf(GameFont.Tiny) + 2f), "新建自定义类型");
            newCategoryLabelBuffer = Widgets.TextField(new Rect(footerRect.x + 10f, footerRect.y + 30f, footerRect.width - 108f, 28f), newCategoryLabelBuffer);
            if (SimUiStyle.DrawPrimaryButton(new Rect(footerRect.xMax - 90f, footerRect.y + 29f, 80f, 30f), "创建", !string.IsNullOrWhiteSpace(newCategoryLabelBuffer), GameFont.Tiny))
                CreateCategory();

            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        /// <summary>
        /// Draws one selectable category row with source badge and item count.
        /// </summary>
        private void DrawCategoryRow(Rect rect, RuntimeGoodsCategory category)
        {
            bool selected = selectedCategoryId == category.categoryId;
            Widgets.DrawBoxSolid(rect, selected ? SoftAccent : new Color(1f, 1f, 1f, Mouse.IsOver(rect) ? 0.05f : 0.02f));
            SimUiStyle.DrawBorder(rect, selected ? Accent : new Color(1f, 1f, 1f, 0.04f));

            if (selected)
                Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 4f, rect.height), Accent);

            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            float titleWidth = Mathf.Max(40f, rect.width - 110f);
            Widgets.Label(new Rect(rect.x + 12f, rect.y + 8f, titleWidth, Text.LineHeightOf(GameFont.Small) + 2f), category.label.Truncate(titleWidth));

            Text.Font = GameFont.Tiny;
            GUI.color = MutedText;
            Widgets.Label(new Rect(rect.x + 12f, rect.y + 34f, titleWidth, Text.LineHeightOf(GameFont.Tiny) + 2f), category.categoryId.Truncate(titleWidth));

            DrawBadge(new Rect(rect.xMax - 82f, rect.y + 9f, 70f, 18f), category.IsBuiltInCategory ? "Def" : "Custom", category.IsBuiltInCategory ? BuiltInBadge : CustomBadge);

            GUI.color = MutedText;
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(new Rect(rect.xMax - 110f, rect.y + 44f, 98f, Text.LineHeightOf(GameFont.Tiny) + 2f), category.Items.Count + " 项");
            Text.Anchor = TextAnchor.UpperLeft;

            if (Widgets.ButtonInvisible(rect))
            {
                selectedCategoryId = category.categoryId;
                selectedCategoryLabelBuffer = category.label;
                currentItemsScroll = Vector2.zero;
                browserScroll = Vector2.zero;
                browserPageIndex = 0;
            }

            GUI.color = Color.white;
        }

        /// <summary>
        /// Draws the selected category details, current item grid, and candidate browser.
        /// </summary>
        private void DrawContent(Rect rect)
        {
            RuntimeGoodsCategory selectedCategory = GetSelectedCategory();
            if (selectedCategory == null)
            {
                Widgets.DrawBoxSolid(rect, PanelBg);
                SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.08f));
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = MutedText;
                Widgets.Label(new Rect(rect.x, rect.y, rect.width, Mathf.Max(rect.height, Text.LineHeightOf(GameFont.Medium) + 4f)), "没有可用的商品类型");
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                return;
            }

            float infoHeight = Mathf.Max(92f, Text.LineHeightOf(GameFont.Medium) + Text.LineHeightOf(GameFont.Tiny) * 2f + 36f);
            float remainingHeight = Mathf.Max(0f, rect.height - infoHeight - 10f - SectionGap);
            float selectedHeight = Mathf.Clamp(remainingHeight * 0.56f, 150f, Mathf.Max(150f, remainingHeight - 150f));
            float browserHeight = Mathf.Max(120f, remainingHeight - selectedHeight);
            Rect infoRect = new Rect(rect.x, rect.y, rect.width, infoHeight);
            Rect selectedItemsRect = new Rect(rect.x, infoRect.yMax + 10f, rect.width, selectedHeight);
            Rect browserRect = new Rect(rect.x, selectedItemsRect.yMax + SectionGap, rect.width, browserHeight);

            DrawCategoryInfo(infoRect, selectedCategory);
            DrawSelectedItems(selectedItemsRect, selectedCategory);
            DrawBrowser(browserRect, selectedCategory);
        }

        /// <summary>
        /// Draws metadata and edit controls for the currently selected category.
        /// </summary>
        private void DrawCategoryInfo(Rect rect, RuntimeGoodsCategory category)
        {
            Widgets.DrawBoxSolid(rect, PanelBg);
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.08f));

            float rightControlWidth = category.IsBuiltInCategory ? 120f : 260f;
            float textWidth = Mathf.Max(120f, rect.width - rightControlWidth - 48f);

            Text.Font = GameFont.Medium;
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x + 16f, rect.y + 10f, textWidth, Text.LineHeightOf(GameFont.Medium) + 4f), category.label.Truncate(textWidth));

            Text.Font = GameFont.Tiny;
            GUI.color = MutedText;
            Widgets.Label(new Rect(rect.x + 16f, rect.y + 40f, textWidth, Text.LineHeightOf(GameFont.Tiny) + 2f), ("ID: " + category.categoryId).Truncate(textWidth));
            Widgets.Label(
                new Rect(rect.x + 16f, rect.y + 60f, textWidth, Text.LineHeightOf(GameFont.Tiny) * 2f + 2f),
                category.IsBuiltInCategory
                    ? "来源：GoodsDef。你可以追加自定义商品，但原始内容保持锁定。"
                    : "来源：玩家自定义数据。名称、商品列表和删除都可编辑。"
            );

            float right = rect.xMax - 16f;
            if (category.IsBuiltInCategory)
            {
                DrawBadge(new Rect(right - 96f, rect.y + 12f, 96f, 20f), "Def 类型", BuiltInBadge);
            }
            else
            {
                selectedCategoryLabelBuffer = Widgets.TextField(new Rect(right - 226f, rect.y + 12f, 140f, 28f), selectedCategoryLabelBuffer ?? category.label);
                if (SimUiStyle.DrawSecondaryButton(new Rect(right - 78f, rect.y + 11f, 68f, 30f), "改名", true, GameFont.Tiny))
                    RenameSelectedCategory();
                if (SimUiStyle.DrawDangerButton(new Rect(right - 118f, rect.y + 46f, 108f, 28f), "删除类型", true, GameFont.Tiny))
                    ConfirmDeleteCategory();
            }

            GUI.color = Color.white;
        }

        /// <summary>
        /// Draws the items already present in the selected category.
        /// </summary>
        private void DrawSelectedItems(Rect rect, RuntimeGoodsCategory category)
        {
            Widgets.DrawBoxSolid(rect, PanelBg);
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.08f));

            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x + 16f, rect.y + 10f, 220f, Text.LineHeightOf(GameFont.Small) + 4f), "当前类型中的商品");

            Text.Font = GameFont.Tiny;
            GUI.color = MutedText;
            Widgets.Label(new Rect(rect.x + 16f, rect.y + 36f, rect.width - 32f, Text.LineHeightOf(GameFont.Tiny) + 2f), "Locked 表示原始 Def 商品，Custom 表示玩家追加并可移除。");

            Rect listRect = new Rect(rect.x + 12f, rect.y + 62f, rect.width - 24f, Mathf.Max(0f, rect.height - 74f));
            int columns = Mathf.Max(1, Mathf.FloorToInt((listRect.width - ScrollbarWidth) / (ItemCardMinWidth + 10f)));
            float cardWidth = Mathf.Max(120f, (listRect.width - (columns - 1) * 10f - ScrollbarWidth) / columns);
            int rows = Mathf.CeilToInt(category.Items.Count / (float)columns);
            Rect viewRect = new Rect(0f, 0f, Mathf.Max(0f, listRect.width - ScrollbarWidth), Mathf.Max(listRect.height, rows * (ItemCardHeight + 10f)));

            Widgets.BeginScrollView(listRect, ref currentItemsScroll, viewRect);
            for (int i = 0; i < category.Items.Count; i++)
            {
                RuntimeGoodsItem item = category.Items[i];
                int row = i / columns;
                int column = i % columns;
                Rect cardRect = new Rect(column * (cardWidth + 10f), row * (ItemCardHeight + 10f), cardWidth, ItemCardHeight);
                DrawSelectedItemCard(cardRect, category, item);
            }
            Widgets.EndScrollView();
        }

        /// <summary>
        /// Draws one item card in the current-category grid.
        /// </summary>
        private void DrawSelectedItemCard(Rect rect, RuntimeGoodsCategory category, RuntimeGoodsItem item)
        {
            bool playerDefinedItem = category.IsPlayerDefinedItem(item.thingDef);
            Widgets.DrawBoxSolid(rect, new Color(1f, 1f, 1f, 0.03f));
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.05f));

            float textWidth = Mathf.Max(56f, rect.width - 156f);
            Rect iconRect = new Rect(rect.x + 10f, rect.y + 20f, 44f, 44f);
            Widgets.DrawBoxSolid(iconRect, new Color(0f, 0f, 0f, 0.16f));
            Widgets.ThingIcon(iconRect.ContractedBy(4f), item.thingDef);

            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            Widgets.Label(new Rect(iconRect.xMax + 10f, rect.y + 10f, textWidth, Text.LineHeightOf(GameFont.Small) + 2f), item.label.Truncate(textWidth));

            Text.Font = GameFont.Tiny;
            GUI.color = MutedText;
            Widgets.Label(new Rect(iconRect.xMax + 10f, rect.y + 36f, textWidth, Text.LineHeightOf(GameFont.Tiny) + 2f), item.thingDefName.Truncate(textWidth));
            Widgets.Label(new Rect(iconRect.xMax + 10f, rect.y + 56f, textWidth, Text.LineHeightOf(GameFont.Tiny) + 2f), "市价 " + item.baseMarketValue.ToString("F1"));

            if (playerDefinedItem)
            {
                DrawBadge(new Rect(rect.xMax - 80f, rect.y + 10f, 68f, 18f), "Custom", CustomBadge);
                if (SimUiStyle.DrawDangerButton(new Rect(rect.xMax - 80f, rect.y + 64f, 68f, 24f), "移除", true, GameFont.Tiny))
                    RemoveItemFromSelectedCategory(item.thingDef);
            }
            else
            {
                DrawBadge(new Rect(rect.xMax - 80f, rect.y + 10f, 68f, 18f), "Locked", LockedBadge);
            }

            TooltipHandler.TipRegion(rect, item.label + "\n" + item.thingDefName);
            GUI.color = Color.white;
        }

        /// <summary>
        /// Draws the searchable candidate item browser for appending goods.
        /// </summary>
        private void DrawBrowser(Rect rect, RuntimeGoodsCategory category)
        {
            Widgets.DrawBoxSolid(rect, PanelBg);
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.08f));

            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x + 16f, rect.y + 10f, 220f, Text.LineHeightOf(GameFont.Small) + 4f), "可追加商品");

            string newSearch = Widgets.TextField(new Rect(rect.xMax - 260f, rect.y + 8f, 244f, 28f), browserSearch);
            if (newSearch != browserSearch)
            {
                browserSearch = newSearch;
                browserPageIndex = 0;
                browserScroll = Vector2.zero;
            }

            if (string.IsNullOrEmpty(browserSearch))
            {
                Text.Font = GameFont.Tiny;
                GUI.color = MutedText;
                Widgets.Label(new Rect(rect.xMax - 252f, rect.y + 14f, 220f, Text.LineHeightOf(GameFont.Tiny) + 2f), "按名称或 DefName 搜索");
            }

            List<ThingDef> filtered = allCandidateThings.Where(thing =>
                string.IsNullOrEmpty(browserSearch)
                || (thing.label != null && thing.label.IndexOf(browserSearch, StringComparison.OrdinalIgnoreCase) >= 0)
                || thing.defName.IndexOf(browserSearch, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            int pageCount = Mathf.Max(1, Mathf.CeilToInt(filtered.Count / (float)BrowserPageSize));
            browserPageIndex = Mathf.Clamp(browserPageIndex, 0, pageCount - 1);
            List<ThingDef> pageItems = filtered.Skip(browserPageIndex * BrowserPageSize).Take(BrowserPageSize).ToList();

            Rect pagerRect = new Rect(rect.x + 12f, rect.y + 44f, rect.width - 24f, 30f);
            DrawBrowserPager(pagerRect, filtered.Count, pageCount);

            Rect listRect = new Rect(rect.x + 12f, pagerRect.yMax + 4f, rect.width - 24f, Mathf.Max(0f, rect.height - 90f));
            Rect viewRect = new Rect(0f, 0f, Mathf.Max(0f, listRect.width - ScrollbarWidth), Mathf.Max(listRect.height, pageItems.Count * BrowserRowHeight));

            Widgets.BeginScrollView(listRect, ref browserScroll, viewRect);
            float y = 0f;
            for (int i = 0; i < pageItems.Count; i++)
            {
                ThingDef thingDef = pageItems[i];
                Rect rowRect = new Rect(0f, y, viewRect.width, BrowserRowHeight);
                DrawBrowserRow(rowRect, category, thingDef);
                y += BrowserRowHeight;
            }
            Widgets.EndScrollView();
        }

        /// <summary>
        /// Draws paging controls for the candidate browser.
        /// </summary>
        private void DrawBrowserPager(Rect rect, int totalItemCount, int pageCount)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = MutedText;
            Widgets.Label(new Rect(rect.x, rect.y + 6f, 220f, Text.LineHeightOf(GameFont.Tiny) + 2f), $"共 {totalItemCount} 项，分页显示");

            float centerX = rect.center.x;
            if (SimUiStyle.DrawSecondaryButton(new Rect(centerX - 124f, rect.y, 84f, 28f), "上一页", browserPageIndex > 0, GameFont.Tiny))
            {
                browserPageIndex--;
                browserScroll = Vector2.zero;
            }

            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(centerX - 34f, rect.y + 4f, 68f, 20f), $"{browserPageIndex + 1}/{pageCount}");
            Text.Anchor = TextAnchor.UpperLeft;

            if (SimUiStyle.DrawSecondaryButton(new Rect(centerX + 40f, rect.y, 84f, 28f), "下一页", browserPageIndex < pageCount - 1, GameFont.Tiny))
            {
                browserPageIndex++;
                browserScroll = Vector2.zero;
            }

            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        /// <summary>
        /// Draws one searchable candidate item row and its append/existing action.
        /// </summary>
        private void DrawBrowserRow(Rect rect, RuntimeGoodsCategory category, ThingDef thingDef)
        {
            bool exists = category.Contains(thingDef);
            Widgets.DrawBoxSolid(rect, new Color(1f, 1f, 1f, Mouse.IsOver(rect) ? 0.04f : 0.015f));

            Rect iconRect = new Rect(rect.x + 8f, rect.y + 6f, 36f, 36f);
            Widgets.DrawBoxSolid(iconRect, new Color(0f, 0f, 0f, 0.14f));
            Widgets.ThingIcon(iconRect.ContractedBy(3f), thingDef);

            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            float textWidth = Mathf.Max(40f, rect.width - 180f);
            Widgets.Label(new Rect(iconRect.xMax + 10f, rect.y + 6f, textWidth, Text.LineHeightOf(GameFont.Small) + 2f), thingDef.LabelCap.ToString().Truncate(textWidth));

            Text.Font = GameFont.Tiny;
            GUI.color = MutedText;
            Widgets.Label(new Rect(iconRect.xMax + 10f, rect.y + 28f, textWidth, Text.LineHeightOf(GameFont.Tiny) + 2f), (thingDef.defName + "   市价 " + thingDef.BaseMarketValue.ToString("F1")).Truncate(textWidth));

            if (exists)
            {
                DrawBadge(new Rect(rect.xMax - 88f, rect.y + 15f, 76f, 18f), "已存在", BuiltInBadge);
            }
            else if (SimUiStyle.DrawPrimaryButton(new Rect(rect.xMax - 88f, rect.y + 10f, 76f, 28f), "追加", true, GameFont.Tiny))
            {
                AddItemToSelectedCategory(thingDef);
            }

            GUI.color = Color.white;
        }

        /// <summary>
        /// Loads custom goods data and rebuilds runtime preview lists.
        /// </summary>
        private void LoadDraft()
        {
            draftData = CustomGoodsDatabase.Load();
            allCandidateThings = CustomGoodsDatabase.GetAllCandidateThings();
            RebuildPreviewFromDraft();
            EnsureValidSelection();
            browserPageIndex = 0;
            dirty = false;
        }

        /// <summary>
        /// Combines Def-based categories with player records for the editable preview model.
        /// </summary>
        private void RebuildPreviewFromDraft()
        {
            Dictionary<string, RuntimeGoodsCategory> categoriesById = new Dictionary<string, RuntimeGoodsCategory>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, RuntimeGoodsItem> itemsById = new Dictionary<string, RuntimeGoodsItem>(StringComparer.OrdinalIgnoreCase);

            foreach (GoodsDef goodsDef in DefDatabase<GoodsDef>.AllDefsListForReading.Where(def => def != null))
            {
                RuntimeGoodsCategory category = GetOrCreatePreviewCategory(categoriesById, goodsDef.defName, goodsDef.label, goodsDef);
                category.Clear();
                if (goodsDef.GoodsList.NullOrEmpty())
                    continue;

                for (int i = 0; i < goodsDef.GoodsList.Count; i++)
                {
                    ThingDef thingDef = goodsDef.GoodsList[i];
                    if (thingDef == null)
                        continue;

                    category.TryAdd(GetOrCreatePreviewItem(itemsById, thingDef), false);
                }
            }

            if (draftData?.categories != null)
            {
                for (int i = 0; i < draftData.categories.Count; i++)
                {
                    CustomGoodsCategoryRecord record = draftData.categories[i];
                    if (record == null || string.IsNullOrEmpty(record.categoryId))
                        continue;

                    GoodsDef sourceDef = DefDatabase<GoodsDef>.GetNamedSilentFail(record.categoryId);
                    RuntimeGoodsCategory category = GetOrCreatePreviewCategory(categoriesById, record.categoryId, sourceDef != null ? sourceDef.label : record.label, sourceDef);
                    category.hasPlayerDefinedConfig = true;
                    if (sourceDef == null && !string.IsNullOrEmpty(record.label))
                        category.label = record.label;

                    for (int itemIndex = 0; itemIndex < (record.itemDefNames?.Count ?? 0); itemIndex++)
                    {
                        ThingDef thingDef = DefDatabase<ThingDef>.GetNamedSilentFail(record.itemDefNames[itemIndex]);
                        if (!CustomGoodsDatabase.IsValidCandidateThing(thingDef))
                            continue;

                        category.TryAdd(GetOrCreatePreviewItem(itemsById, thingDef), true);
                    }
                }
            }

            previewCategories = categoriesById.Values
                .OrderBy(category => category.IsBuiltInCategory ? 0 : 1)
                .ThenBy(category => category.label)
                .ToList();
        }

        /// <summary>
        /// Returns the currently selected category from the preview list.
        /// </summary>
        private RuntimeGoodsCategory GetSelectedCategory()
        {
            return previewCategories.FirstOrDefault(category => category.categoryId == selectedCategoryId);
        }

        /// <summary>
        /// Keeps selection pointed at an existing category after preview data changes.
        /// </summary>
        private void EnsureValidSelection()
        {
            if (previewCategories.Count == 0)
            {
                selectedCategoryId = string.Empty;
                selectedCategoryLabelBuffer = string.Empty;
                return;
            }

            RuntimeGoodsCategory selected = GetSelectedCategory() ?? previewCategories[0];
            selectedCategoryId = selected.categoryId;
            selectedCategoryLabelBuffer = selected.label;
        }

        /// <summary>
        /// Creates a new player-defined goods category from the sidebar input.
        /// </summary>
        private void CreateCategory()
        {
            string label = CustomGoodsDatabase.NormalizeLabel(newCategoryLabelBuffer);
            if (string.IsNullOrEmpty(label))
                return;

            if (previewCategories.Any(category => string.Equals(category.label, label, StringComparison.OrdinalIgnoreCase)))
            {
                Messages.Message("已存在同名商品类型。", MessageTypeDefOf.RejectInput, false);
                return;
            }

            string categoryId = CustomGoodsDatabase.GenerateUniqueCategoryId(label, previewCategories.Select(category => category.categoryId));
            draftData.categories.Add(new CustomGoodsCategoryRecord
            {
                categoryId = categoryId,
                label = label,
                builtInCategory = false,
                itemDefNames = new List<string>()
            });

            newCategoryLabelBuffer = string.Empty;
            dirty = true;
            RebuildPreviewFromDraft();
            selectedCategoryId = categoryId;
            selectedCategoryLabelBuffer = label;
            browserPageIndex = 0;
            browserScroll = Vector2.zero;
        }

        /// <summary>
        /// Renames the currently selected player-defined goods category.
        /// </summary>
        private void RenameSelectedCategory()
        {
            RuntimeGoodsCategory selected = GetSelectedCategory();
            if (selected == null || selected.IsBuiltInCategory)
                return;

            string newLabel = CustomGoodsDatabase.NormalizeLabel(selectedCategoryLabelBuffer);
            if (string.IsNullOrEmpty(newLabel))
            {
                Messages.Message("商品类型名称不能为空。", MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (previewCategories.Any(category => category.categoryId != selected.categoryId && string.Equals(category.label, newLabel, StringComparison.OrdinalIgnoreCase)))
            {
                Messages.Message("已存在同名商品类型。", MessageTypeDefOf.RejectInput, false);
                return;
            }

            CustomGoodsCategoryRecord record = GetOrCreateDraftRecord(selected.categoryId, false, selected.label);
            record.label = newLabel;
            dirty = true;
            RebuildPreviewFromDraft();
            selectedCategoryLabelBuffer = newLabel;
        }

        /// <summary>
        /// Opens a confirmation dialog before deleting a player-defined category.
        /// </summary>
        private void ConfirmDeleteCategory()
        {
            RuntimeGoodsCategory selected = GetSelectedCategory();
            if (selected == null || selected.IsBuiltInCategory)
                return;

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "删除这个自定义类型，以及其中全部自定义商品关联？",
                DeleteSelectedCategory));
        }

        /// <summary>
        /// Removes the selected player-defined category from the draft data.
        /// </summary>
        private void DeleteSelectedCategory()
        {
            draftData.categories.RemoveAll(record => string.Equals(record.categoryId, selectedCategoryId, StringComparison.OrdinalIgnoreCase));
            dirty = true;
            RebuildPreviewFromDraft();
            EnsureValidSelection();
            browserPageIndex = 0;
            browserScroll = Vector2.zero;
        }

        /// <summary>
        /// Adds a ThingDef to the selected category as a player-defined association.
        /// </summary>
        private void AddItemToSelectedCategory(ThingDef thingDef)
        {
            RuntimeGoodsCategory selected = GetSelectedCategory();
            if (selected == null || thingDef == null)
                return;

            if (selected.Contains(thingDef))
            {
                Messages.Message("该商品已经在当前类型中。", MessageTypeDefOf.RejectInput, false);
                return;
            }

            bool builtInCategory = selected.IsBuiltInCategory;
            CustomGoodsCategoryRecord record = GetOrCreateDraftRecord(selected.categoryId, builtInCategory, selected.label);
            if (!record.itemDefNames.Contains(thingDef.defName))
                record.itemDefNames.Add(thingDef.defName);

            dirty = true;
            RebuildPreviewFromDraft();
            browserScroll = Vector2.zero;
        }

        /// <summary>
        /// Removes a player-defined ThingDef association from the selected category.
        /// </summary>
        private void RemoveItemFromSelectedCategory(ThingDef thingDef)
        {
            RuntimeGoodsCategory selected = GetSelectedCategory();
            if (selected == null || thingDef == null || !selected.IsPlayerDefinedItem(thingDef))
                return;

            CustomGoodsCategoryRecord record = draftData.categories.FirstOrDefault(entry => string.Equals(entry.categoryId, selected.categoryId, StringComparison.OrdinalIgnoreCase));
            if (record == null)
                return;

            record.itemDefNames.RemoveAll(defName => string.Equals(defName, thingDef.defName, StringComparison.OrdinalIgnoreCase));
            if (record.builtInCategory && record.itemDefNames.Count == 0)
                draftData.categories.Remove(record);

            dirty = true;
            RebuildPreviewFromDraft();
        }

        /// <summary>
        /// Persists the draft data and asks the runtime goods catalog to rebuild.
        /// </summary>
        private void SaveDraft()
        {
            CustomGoodsDatabase.Save(draftData);
            CustomGoodsDatabase.NotifyRuntimeChanged();
            dirty = false;
            Messages.Message("自定义商品注册已保存，运行时商品目录已重建。", MessageTypeDefOf.PositiveEvent, false);
        }

        /// <summary>
        /// Reloads custom goods data, asking for confirmation when unsaved edits exist.
        /// </summary>
        private void ConfirmReload()
        {
            Action reloadAction = LoadDraft;
            if (!dirty)
            {
                reloadAction();
                return;
            }

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "重新加载会丢弃当前未保存的自定义商品改动，是否继续？",
                reloadAction));
        }

        /// <summary>
        /// Replaces the current draft with imported data and persists it.
        /// </summary>
        private void HandleImportReplace(CustomGoodsDatabaseData importedData)
        {
            draftData = importedData ?? new CustomGoodsDatabaseData();
            RebuildPreviewFromDraft();
            EnsureValidSelection();
            browserPageIndex = 0;
            SaveDraft();
        }

        /// <summary>
        /// Finds or creates the draft record backing a category override.
        /// </summary>
        private CustomGoodsCategoryRecord GetOrCreateDraftRecord(string categoryId, bool builtInCategory, string label)
        {
            CustomGoodsCategoryRecord record = draftData.categories.FirstOrDefault(entry => string.Equals(entry.categoryId, categoryId, StringComparison.OrdinalIgnoreCase));
            if (record != null)
                return record;

            record = new CustomGoodsCategoryRecord
            {
                categoryId = categoryId,
                label = builtInCategory ? string.Empty : label,
                builtInCategory = builtInCategory,
                itemDefNames = new List<string>()
            };
            draftData.categories.Add(record);
            return record;
        }

        /// <summary>
        /// Finds or creates a preview category while preserving Def metadata when present.
        /// </summary>
        private static RuntimeGoodsCategory GetOrCreatePreviewCategory(
            IDictionary<string, RuntimeGoodsCategory> categoriesById,
            string categoryId,
            string label,
            GoodsDef sourceDef)
        {
            if (!categoriesById.TryGetValue(categoryId, out RuntimeGoodsCategory category))
            {
                category = new RuntimeGoodsCategory
                {
                    categoryId = categoryId,
                    label = string.IsNullOrEmpty(label) ? categoryId : label,
                    sourceDef = sourceDef
                };
                categoriesById[categoryId] = category;
            }
            else
            {
                if (!string.IsNullOrEmpty(label))
                    category.label = label;
                if (sourceDef != null)
                    category.sourceDef = sourceDef;
            }

            return category;
        }

        /// <summary>
        /// Finds or creates a preview item record for a valid ThingDef.
        /// </summary>
        private static RuntimeGoodsItem GetOrCreatePreviewItem(IDictionary<string, RuntimeGoodsItem> itemsById, ThingDef thingDef)
        {
            if (!itemsById.TryGetValue(thingDef.defName, out RuntimeGoodsItem item))
            {
                item = new RuntimeGoodsItem
                {
                    thingDefName = thingDef.defName,
                    thingDef = thingDef,
                    label = thingDef.LabelCap.RawText,
                    baseMarketValue = thingDef.BaseMarketValue
                };
                itemsById[thingDef.defName] = item;
            }

            return item;
        }

        /// <summary>
        /// Draws a compact source/status badge and restores standard text state afterwards.
        /// </summary>
        private static void DrawBadge(Rect rect, string label, Color fill)
        {
            Widgets.DrawBoxSolid(rect, fill);
            SimUiStyle.DrawBorder(rect, new Color(fill.r, fill.g, fill.b, 0.55f));
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = Color.white;
            Widgets.Label(rect, label);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }
    }
}
