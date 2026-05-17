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
    /// 提供玩家自定义商品类型和商品关联的游戏内编辑窗口。
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
        private List<ThingCategoryDef> candidateThingCategories = new List<ThingCategoryDef>();
        private List<ThingDef> browserFilteredCache = new List<ThingDef>();

        private string selectedCategoryId = string.Empty;
        private string newCategoryLabelBuffer = string.Empty;
        private string selectedCategoryLabelBuffer = string.Empty;
        private string browserSearch = string.Empty;
        private string browserFilterCacheSearch = null;
        private ThingCategoryDef selectedThingCategory;
        private ThingCategoryDef browserFilterCacheCategory;
        private bool browserFilterDirty = true;

        private Vector2 categoryScroll;
        private Vector2 currentItemsScroll;
        private Vector2 browserScroll;
        private int browserPageIndex;
        private bool dirty;

        public override Vector2 InitialSize => new Vector2(1380f, 860f);

        /// <summary>
        /// 初始化注册窗口并加载当前自定义商品草稿。
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
        /// 绘制完整注册窗口，并在绘制结束后恢复全局 IMGUI 状态。
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
        /// 绘制标题、说明文本以及导入、导出、保存操作。
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
            Widgets.Label(titleRect, SimTranslation.T("RSMF.CustomGoods.Title"));

            Text.Font = GameFont.Tiny;
            GUI.color = MutedText;
            Rect descRect = new Rect(rect.x + 20f, titleRect.yMax + 4f, textWidth, Text.LineHeightOf(GameFont.Tiny) * 2f + 4f);
            Widgets.Label(
                descRect,
                SimTranslation.T("RSMF.CustomGoods.Description")
            );

            float buttonY = rect.y + 18f;
            float right = rect.xMax - 16f - CloseXReservedWidth;

            right -= 116f;
            if (SimUiStyle.DrawPrimaryButton(new Rect(right, buttonY, 116f, 34f), dirty ? SimTranslation.T("RSMF.CustomGoods.SaveDirty") : SimTranslation.T("RSMF.CustomGoods.Save")))
                SaveDraft();

            right -= 124f;
            if (SimUiStyle.DrawSecondaryButton(new Rect(right, buttonY, 112f, 34f), SimTranslation.T("RSMF.CustomGoods.Reload")))
                ConfirmReload();

            right -= 124f;
            if (SimUiStyle.DrawSecondaryButton(new Rect(right, buttonY, 112f, 34f), SimTranslation.T("RSMF.CustomGoods.ExportBase64")))
                Find.WindowStack.Add(new Dialog_CustomGoodsTransfer(CustomGoodsDatabase.ExportBase64(draftData)));

            right -= 124f;
            if (SimUiStyle.DrawSecondaryButton(new Rect(right, buttonY, 112f, 34f), SimTranslation.T("RSMF.CustomGoods.ImportBase64")))
                Find.WindowStack.Add(new Dialog_CustomGoodsImport(HandleImport));

            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }

        /// <summary>
        /// 绘制商品类型列表和新建类型输入区。
        /// </summary>
        private void DrawSidebar(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, PanelBg);
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.08f));

            float titleHeight = Text.LineHeightOf(GameFont.Small) + 6f;
            Rect titleRect = new Rect(rect.x + 16f, rect.y + 12f, rect.width - 32f, titleHeight);
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            Widgets.Label(titleRect, SimTranslation.T("RSMF.CustomGoods.CategoryListTitle"));

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
            Widgets.Label(new Rect(footerRect.x + 10f, footerRect.y + 8f, footerRect.width - 20f, Text.LineHeightOf(GameFont.Tiny) + 2f), SimTranslation.T("RSMF.CustomGoods.NewCustomCategory"));
            newCategoryLabelBuffer = Widgets.TextField(new Rect(footerRect.x + 10f, footerRect.y + 30f, footerRect.width - 108f, 28f), newCategoryLabelBuffer);
            if (SimUiStyle.DrawPrimaryButton(new Rect(footerRect.xMax - 90f, footerRect.y + 29f, 80f, 30f), SimTranslation.T("RSMF.CustomGoods.Create"), !string.IsNullOrWhiteSpace(newCategoryLabelBuffer), GameFont.Tiny))
                CreateCategory();

            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        /// <summary>
        /// 绘制一个可选择的商品类型行，并显示来源标记和商品数量。
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
            Widgets.Label(new Rect(rect.xMax - 110f, rect.y + 44f, 98f, Text.LineHeightOf(GameFont.Tiny) + 2f), SimTranslation.T("RSMF.CustomGoods.ItemCount", category.Items.Count.Named("count")));
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
        /// 绘制当前商品类型详情、已包含商品列表和候选商品浏览器。
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
                Widgets.Label(new Rect(rect.x, rect.y, rect.width, Mathf.Max(rect.height, Text.LineHeightOf(GameFont.Medium) + 4f)), SimTranslation.T("RSMF.CustomGoods.NoCategories"));
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
        /// 绘制当前商品类型的元数据和编辑控件。
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
                    ? SimTranslation.T("RSMF.CustomGoods.SourceDef")
                    : SimTranslation.T("RSMF.CustomGoods.SourceCustom")
            );

            float right = rect.xMax - 16f;
            if (category.IsBuiltInCategory)
            {
                DrawBadge(new Rect(right - 96f, rect.y + 12f, 96f, 20f), SimTranslation.T("RSMF.CustomGoods.DefCategory"), BuiltInBadge);
            }
            else
            {
                selectedCategoryLabelBuffer = Widgets.TextField(new Rect(right - 226f, rect.y + 12f, 140f, 28f), selectedCategoryLabelBuffer ?? category.label);
                if (SimUiStyle.DrawSecondaryButton(new Rect(right - 78f, rect.y + 11f, 68f, 30f), SimTranslation.T("RSMF.CustomGoods.Rename"), true, GameFont.Tiny))
                    RenameSelectedCategory();
                if (SimUiStyle.DrawDangerButton(new Rect(right - 118f, rect.y + 46f, 108f, 28f), SimTranslation.T("RSMF.CustomGoods.DeleteCategory"), true, GameFont.Tiny))
                    ConfirmDeleteCategory();
            }

            GUI.color = Color.white;
        }

        /// <summary>
        /// 绘制当前商品类型中已经包含的商品。
        /// </summary>
        private void DrawSelectedItems(Rect rect, RuntimeGoodsCategory category)
        {
            Widgets.DrawBoxSolid(rect, PanelBg);
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.08f));

            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x + 16f, rect.y + 10f, 220f, Text.LineHeightOf(GameFont.Small) + 4f), SimTranslation.T("RSMF.CustomGoods.CurrentItems"));

            Text.Font = GameFont.Tiny;
            GUI.color = MutedText;
            Widgets.Label(new Rect(rect.x + 16f, rect.y + 36f, rect.width - 32f, Text.LineHeightOf(GameFont.Tiny) + 2f), SimTranslation.T("RSMF.CustomGoods.LockedTip"));

            Rect listRect = new Rect(rect.x + 12f, rect.y + 62f, rect.width - 24f, Mathf.Max(0f, rect.height - 74f));
            int columns = Mathf.Max(1, Mathf.FloorToInt((listRect.width - ScrollbarWidth) / (ItemCardMinWidth + 10f)));
            float cardWidth = Mathf.Max(120f, (listRect.width - (columns - 1) * 10f - ScrollbarWidth) / columns);
            int rows = Mathf.CeilToInt(category.Items.Count / (float)columns);
            Rect viewRect = new Rect(0f, 0f, Mathf.Max(0f, listRect.width - ScrollbarWidth), Mathf.Max(listRect.height, rows * (ItemCardHeight + 10f)));

            Widgets.BeginScrollView(listRect, ref currentItemsScroll, viewRect);
            float rowStride = ItemCardHeight + 10f;
            int firstVisibleRow = Mathf.Max(0, Mathf.FloorToInt(currentItemsScroll.y / rowStride));
            int lastVisibleRow = Mathf.Min(rows - 1, Mathf.CeilToInt((currentItemsScroll.y + listRect.height) / rowStride));
            for (int row = firstVisibleRow; row <= lastVisibleRow; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    int itemIndex = row * columns + column;
                    if (itemIndex < 0 || itemIndex >= category.Items.Count)
                        continue;

                    RuntimeGoodsItem item = category.Items[itemIndex];
                    Rect cardRect = new Rect(column * (cardWidth + 10f), row * rowStride, cardWidth, ItemCardHeight);
                    DrawSelectedItemCard(cardRect, category, item);
                }
            }
            Widgets.EndScrollView();
        }

        /// <summary>
        /// 绘制当前类型商品网格中的单个商品卡片。
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
            Widgets.Label(new Rect(iconRect.xMax + 10f, rect.y + 56f, textWidth, Text.LineHeightOf(GameFont.Tiny) + 2f), SimTranslation.T("RSMF.CustomGoods.MarketValue", item.baseMarketValue.ToString("F1").Named("value")));

            if (playerDefinedItem)
            {
                DrawBadge(new Rect(rect.xMax - 80f, rect.y + 10f, 68f, 18f), "Custom", CustomBadge);
                if (SimUiStyle.DrawDangerButton(new Rect(rect.xMax - 80f, rect.y + 64f, 68f, 24f), SimTranslation.T("RSMF.CustomGoods.Remove"), true, GameFont.Tiny))
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
        /// 绘制可搜索、可按物品分类筛选的候选商品浏览器。
        /// </summary>
        private void DrawBrowser(Rect rect, RuntimeGoodsCategory category)
        {
            Widgets.DrawBoxSolid(rect, PanelBg);
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.08f));

            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x + 16f, rect.y + 10f, 220f, Text.LineHeightOf(GameFont.Small) + 4f), SimTranslation.T("RSMF.CustomGoods.AvailableItems"));

            Rect categoryButtonRect = new Rect(rect.x + 144f, rect.y + 8f, 190f, 28f);
            if (SimUiStyle.DrawSecondaryButton(categoryButtonRect, GetSelectedThingCategoryLabel(), true, GameFont.Tiny))
                OpenThingCategoryFilterMenu();

            string newSearch = Widgets.TextField(new Rect(rect.xMax - 260f, rect.y + 8f, 244f, 28f), browserSearch);
            if (newSearch != browserSearch)
            {
                browserSearch = newSearch;
                browserPageIndex = 0;
                browserScroll = Vector2.zero;
                MarkBrowserFilterDirty();
            }

            if (string.IsNullOrEmpty(browserSearch))
            {
                Text.Font = GameFont.Tiny;
                GUI.color = MutedText;
                Widgets.Label(new Rect(rect.xMax - 252f, rect.y + 14f, 220f, Text.LineHeightOf(GameFont.Tiny) + 2f), SimTranslation.T("RSMF.CustomGoods.SearchPlaceholder"));
            }

            List<ThingDef> filtered = GetFilteredBrowserItems();

            int pageCount = Mathf.Max(1, Mathf.CeilToInt(filtered.Count / (float)BrowserPageSize));
            browserPageIndex = Mathf.Clamp(browserPageIndex, 0, pageCount - 1);
            int pageStartIndex = browserPageIndex * BrowserPageSize;
            int pageItemCount = Mathf.Min(BrowserPageSize, Mathf.Max(0, filtered.Count - pageStartIndex));

            Rect pagerRect = new Rect(rect.x + 12f, rect.y + 44f, rect.width - 24f, 30f);
            DrawBrowserPager(pagerRect, filtered.Count, pageCount);

            Rect listRect = new Rect(rect.x + 12f, pagerRect.yMax + 4f, rect.width - 24f, Mathf.Max(0f, rect.height - 90f));
            Rect viewRect = new Rect(0f, 0f, Mathf.Max(0f, listRect.width - ScrollbarWidth), Mathf.Max(listRect.height, pageItemCount * BrowserRowHeight));

            Widgets.BeginScrollView(listRect, ref browserScroll, viewRect);
            float y = 0f;
            for (int i = 0; i < pageItemCount; i++)
            {
                ThingDef thingDef = filtered[pageStartIndex + i];
                Rect rowRect = new Rect(0f, y, viewRect.width, BrowserRowHeight);
                DrawBrowserRow(rowRect, category, thingDef);
                y += BrowserRowHeight;
            }
            Widgets.EndScrollView();
        }

        /// <summary>
        /// 绘制候选商品浏览器的分页控件。
        /// </summary>
        private void DrawBrowserPager(Rect rect, int totalItemCount, int pageCount)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = MutedText;
            Widgets.Label(new Rect(rect.x, rect.y + 6f, 220f, Text.LineHeightOf(GameFont.Tiny) + 2f), SimTranslation.T("RSMF.CustomGoods.TotalPaged", totalItemCount.Named("count")));

            float centerX = rect.center.x;
            if (SimUiStyle.DrawSecondaryButton(new Rect(centerX - 124f, rect.y, 84f, 28f), SimTranslation.T("RSMF.CustomGoods.PrevPage"), browserPageIndex > 0, GameFont.Tiny))
            {
                browserPageIndex--;
                browserScroll = Vector2.zero;
            }

            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(centerX - 34f, rect.y + 4f, 68f, 20f), $"{browserPageIndex + 1}/{pageCount}");
            Text.Anchor = TextAnchor.UpperLeft;

            if (SimUiStyle.DrawSecondaryButton(new Rect(centerX + 40f, rect.y, 84f, 28f), SimTranslation.T("RSMF.CustomGoods.NextPage"), browserPageIndex < pageCount - 1, GameFont.Tiny))
            {
                browserPageIndex++;
                browserScroll = Vector2.zero;
            }

            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        /// <summary>
        /// 绘制一个候选商品行及其追加或已存在状态。
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
            Widgets.Label(new Rect(iconRect.xMax + 10f, rect.y + 28f, textWidth, Text.LineHeightOf(GameFont.Tiny) + 2f), SimTranslation.T("RSMF.CustomGoods.DefMarketValue", thingDef.defName.Named("defName"), thingDef.BaseMarketValue.ToString("F1").Named("value")).Truncate(textWidth));

            if (exists)
            {
                DrawBadge(new Rect(rect.xMax - 88f, rect.y + 15f, 76f, 18f), SimTranslation.T("RSMF.CustomGoods.Exists"), BuiltInBadge);
            }
            else if (SimUiStyle.DrawPrimaryButton(new Rect(rect.xMax - 88f, rect.y + 10f, 76f, 28f), SimTranslation.T("RSMF.CustomGoods.Append"), true, GameFont.Tiny))
            {
                AddItemToSelectedCategory(thingDef);
            }

            GUI.color = Color.white;
        }

        /// <summary>
        /// 判断候选商品是否符合当前搜索文本和物品分类筛选。
        /// </summary>
        private bool IsThingVisibleInBrowser(ThingDef thing)
        {
            if (thing == null) return false;

            if (selectedThingCategory != null && !selectedThingCategory.ContainedInThisOrDescendant(thing))
                return false;

            return string.IsNullOrEmpty(browserSearch)
                || (thing.label != null && thing.label.IndexOf(browserSearch, StringComparison.OrdinalIgnoreCase) >= 0)
                || thing.defName.IndexOf(browserSearch, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// 返回候选商品过滤结果，负责避免 IMGUI 每帧重复遍历全部 ThingDef。
        /// </summary>
        private List<ThingDef> GetFilteredBrowserItems()
        {
            if (!browserFilterDirty
                && browserFilteredCache != null
                && browserFilterCacheSearch == browserSearch
                && browserFilterCacheCategory == selectedThingCategory)
            {
                return browserFilteredCache;
            }

            browserFilteredCache = allCandidateThings.Where(IsThingVisibleInBrowser).ToList();
            browserFilterCacheSearch = browserSearch;
            browserFilterCacheCategory = selectedThingCategory;
            browserFilterDirty = false;
            return browserFilteredCache;
        }

        /// <summary>
        /// 标记候选商品过滤缓存失效，负责在搜索词、分类或候选数据变化后延迟重算。
        /// </summary>
        private void MarkBrowserFilterDirty()
        {
            browserFilterDirty = true;
        }

        /// <summary>
        /// 返回当前候选商品分类筛选按钮显示文本。
        /// </summary>
        private string GetSelectedThingCategoryLabel()
        {
            return selectedThingCategory == null
                ? SimTranslation.T("RSMF.CustomGoods.ThingCategoryAll")
                : SimTranslation.T("RSMF.CustomGoods.ThingCategory", selectedThingCategory.LabelCap.RawText.Named("label")).Truncate(180f);
        }

        /// <summary>
        /// 打开候选商品物品分类筛选菜单。
        /// </summary>
        private void OpenThingCategoryFilterMenu()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                new FloatMenuOption(SimTranslation.T("RSMF.CustomGoods.AllThingCategories"), delegate
                {
                    selectedThingCategory = null;
                    browserPageIndex = 0;
                    browserScroll = Vector2.zero;
                    MarkBrowserFilterDirty();
                })
            };

            for (int i = 0; i < candidateThingCategories.Count; i++)
            {
                ThingCategoryDef category = candidateThingCategories[i];
                string label = category.LabelCap.RawText + " / " + category.defName;
                options.Add(new FloatMenuOption(label, delegate
                {
                    selectedThingCategory = category;
                    browserPageIndex = 0;
                    browserScroll = Vector2.zero;
                    MarkBrowserFilterDirty();
                }));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        /// <summary>
        /// 重建可用于候选商品筛选的物品分类列表。
        /// </summary>
        private void RebuildCandidateThingCategories()
        {
            candidateThingCategories = DefDatabase<ThingCategoryDef>.AllDefsListForReading
                .Where(category => category != null && allCandidateThings.Any(category.ContainedInThisOrDescendant))
                .OrderBy(category => category.LabelCap.RawText)
                .ThenBy(category => category.defName)
                .ToList();

            if (selectedThingCategory != null && !candidateThingCategories.Contains(selectedThingCategory))
                selectedThingCategory = null;
            MarkBrowserFilterDirty();
        }

        /// <summary>
        /// 加载自定义商品数据并重建运行时预览列表。
        /// </summary>
        private void LoadDraft()
        {
            draftData = CustomGoodsDatabase.Load();
            allCandidateThings = CustomGoodsDatabase.GetAllCandidateThings();
            RebuildCandidateThingCategories();
            RebuildPreviewFromDraft();
            EnsureValidSelection();
            browserPageIndex = 0;
            MarkBrowserFilterDirty();
            dirty = false;
        }

        /// <summary>
        /// 将 Def 商品类型和玩家记录合并为可编辑预览模型。
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
        /// 从预览列表中返回当前选中的商品类型。
        /// </summary>
        private RuntimeGoodsCategory GetSelectedCategory()
        {
            return previewCategories.FirstOrDefault(category => category.categoryId == selectedCategoryId);
        }

        /// <summary>
        /// 在预览数据变化后保持选择指向仍然存在的商品类型。
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
        /// 根据侧栏输入创建新的玩家自定义商品类型。
        /// </summary>
        private void CreateCategory()
        {
            string label = CustomGoodsDatabase.NormalizeLabel(newCategoryLabelBuffer);
            if (string.IsNullOrEmpty(label))
                return;

            if (previewCategories.Any(category => string.Equals(category.label, label, StringComparison.OrdinalIgnoreCase)))
            {
                Messages.Message(SimTranslation.T("RSMF.CustomGoods.DuplicateCategory"), MessageTypeDefOf.RejectInput, false);
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
        /// 重命名当前选中的玩家自定义商品类型。
        /// </summary>
        private void RenameSelectedCategory()
        {
            RuntimeGoodsCategory selected = GetSelectedCategory();
            if (selected == null || selected.IsBuiltInCategory)
                return;

            string newLabel = CustomGoodsDatabase.NormalizeLabel(selectedCategoryLabelBuffer);
            if (string.IsNullOrEmpty(newLabel))
            {
                Messages.Message(SimTranslation.T("RSMF.CustomGoods.EmptyCategoryName"), MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (previewCategories.Any(category => category.categoryId != selected.categoryId && string.Equals(category.label, newLabel, StringComparison.OrdinalIgnoreCase)))
            {
                Messages.Message(SimTranslation.T("RSMF.CustomGoods.DuplicateCategory"), MessageTypeDefOf.RejectInput, false);
                return;
            }

            CustomGoodsCategoryRecord record = GetOrCreateDraftRecord(selected.categoryId, false, selected.label);
            record.label = newLabel;
            dirty = true;
            RebuildPreviewFromDraft();
            selectedCategoryLabelBuffer = newLabel;
        }

        /// <summary>
        /// 删除玩家自定义类型前打开确认对话框。
        /// </summary>
        private void ConfirmDeleteCategory()
        {
            RuntimeGoodsCategory selected = GetSelectedCategory();
            if (selected == null || selected.IsBuiltInCategory)
                return;

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                SimTranslation.T("RSMF.CustomGoods.DeleteConfirm"),
                DeleteSelectedCategory));
        }

        /// <summary>
        /// 从草稿数据中移除当前选中的玩家自定义商品类型。
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
        /// 将 ThingDef 作为玩家自定义关联追加到当前商品类型。
        /// </summary>
        private void AddItemToSelectedCategory(ThingDef thingDef)
        {
            RuntimeGoodsCategory selected = GetSelectedCategory();
            if (selected == null || thingDef == null)
                return;

            if (selected.Contains(thingDef))
            {
                Messages.Message(SimTranslation.T("RSMF.CustomGoods.ItemAlreadyExists"), MessageTypeDefOf.RejectInput, false);
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
        /// 从当前商品类型移除一个玩家自定义 ThingDef 关联。
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
        /// 保存草稿数据并通知运行时商品目录重建。
        /// </summary>
        private void SaveDraft()
        {
            CustomGoodsDatabase.Save(draftData);
            CustomGoodsDatabase.NotifyRuntimeChanged();
            dirty = false;
            Messages.Message(SimTranslation.T("RSMF.CustomGoods.SaveSuccess"), MessageTypeDefOf.PositiveEvent, false);
        }

        /// <summary>
        /// 重新加载自定义商品数据，存在未保存改动时先请求确认。
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
                SimTranslation.T("RSMF.CustomGoods.ReloadConfirm"),
                reloadAction));
        }

        /// <summary>
        /// 按导入模式处理外部商品数据并保存。
        /// </summary>
        private void HandleImport(CustomGoodsDatabaseData importedData, bool replaceExisting)
        {
            draftData = replaceExisting
                ? (importedData ?? new CustomGoodsDatabaseData())
                : MergeImportedData(draftData, importedData);
            RebuildPreviewFromDraft();
            EnsureValidSelection();
            browserPageIndex = 0;
            SaveDraft();
        }

        /// <summary>
        /// 将导入数据增量合并到当前草稿，按商品类型 ID 合并并对商品 DefName 去重。
        /// </summary>
        private static CustomGoodsDatabaseData MergeImportedData(CustomGoodsDatabaseData currentData, CustomGoodsDatabaseData importedData)
        {
            CustomGoodsDatabaseData merged = currentData ?? new CustomGoodsDatabaseData();
            if (merged.categories == null)
                merged.categories = new List<CustomGoodsCategoryRecord>();
            if (importedData?.categories == null)
                return merged;

            for (int i = 0; i < importedData.categories.Count; i++)
            {
                CustomGoodsCategoryRecord source = importedData.categories[i];
                if (source == null || string.IsNullOrEmpty(source.categoryId))
                    continue;

                CustomGoodsCategoryRecord target = merged.categories.FirstOrDefault(record => string.Equals(record.categoryId, source.categoryId, StringComparison.OrdinalIgnoreCase));
                if (target == null)
                {
                    target = new CustomGoodsCategoryRecord
                    {
                        categoryId = source.categoryId,
                        label = source.label,
                        builtInCategory = source.builtInCategory,
                        itemDefNames = new List<string>()
                    };
                    merged.categories.Add(target);
                }
                else
                {
                    target.builtInCategory |= source.builtInCategory;
                    if (!target.builtInCategory && string.IsNullOrEmpty(target.label) && !string.IsNullOrEmpty(source.label))
                        target.label = source.label;
                }

                if (target.itemDefNames == null)
                    target.itemDefNames = new List<string>();

                List<string> sourceItems = source.itemDefNames ?? new List<string>();
                for (int itemIndex = 0; itemIndex < sourceItems.Count; itemIndex++)
                {
                    string defName = sourceItems[itemIndex];
                    if (!string.IsNullOrEmpty(defName) && !target.itemDefNames.Any(existing => string.Equals(existing, defName, StringComparison.OrdinalIgnoreCase)))
                        target.itemDefNames.Add(defName);
                }
            }

            return merged;
        }

        /// <summary>
        /// 查找或创建支撑商品类型覆盖配置的草稿记录。
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
        /// 查找或创建预览商品类型，并在存在 Def 元数据时保留其来源信息。
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
        /// 为有效 ThingDef 查找或创建预览商品记录。
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
        /// 绘制紧凑来源或状态标记，并在结束后恢复常规文本状态。
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
