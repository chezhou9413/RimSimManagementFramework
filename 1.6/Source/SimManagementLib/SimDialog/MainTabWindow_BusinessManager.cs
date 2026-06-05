using RimWorld;
using SimManagementLib.Api;
using SimManagementLib.SimAI;
using SimManagementLib.SimDef;
using SimManagementLib.SimZone;
using SimManagementLib.Pojo;
using SimManagementLib.Tool;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    public partial class MainTabWindow_BusinessManager : MainTabWindow
    {
        /// <summary>
        /// 保存经商管理商店行需要的地图与区域，负责避免绘制循环反复解析地图。
        /// </summary>
        private sealed class ShopViewData
        {
            public Map Map;
            public Zone_Shop Zone;
        }

        /// <summary>
        /// 保存经商管理顾客行需要的顾客、地图、商店和 Lord 状态。
        /// </summary>
        private sealed class CustomerViewData
        {
            public Map Map;
            public Pawn Pawn;
            public Zone_Shop ShopZone;
            public LordJob_CustomerVisit Visit;
        }

        private readonly List<ShopUiPageDef> pages = new List<ShopUiPageDef>();
        private readonly BusinessManagerUiContext uiContext = new BusinessManagerUiContext();
        private string curPageDefName;
        private int curPageIndex;
        private int pageTabPageIndex;
        private Vector2 shopScrollPos;
        private Vector2 financeScrollPos;
        private Vector2 financeLogScrollPos;
        private Vector2 customerScrollPos;
        private Vector2 reviewScrollPos;
        private Vector2 collectibleExchangeScrollPos;
        private int reviewPageIndex;
        private Vector2 staffScrollPos;
        private Vector2 vendingScrollPos;
        private Vector2 blueprintScrollPos;
        private Vector2 blueprintNetworkScrollPos;
        private Vector2 blueprintNetworkDetailScrollPos;
        private int financeSubPageIndex;
        private int financeLogPageIndex;
        private int reviewSortMode;
        private int reviewFilterZoneId = int.MinValue;
        private const int BlueprintMaxSize = 50;
        private bool blueprintSelectionActive;
        private List<ShopBlueprintLocalRecord> blueprintRecords;
        private bool blueprintShowNetworkTab;
        private BlueprintNetworkSortMode blueprintNetworkSortMode;
        private int blueprintNetworkPage = 1;
        private const int BlueprintNetworkPageSize = 12;
        private SteamSessionInfo blueprintSteamSession;
        private float blueprintNextSteamSessionResolveTime;
        private BlueprintNetworkStatusData blueprintNetworkStatus;
        private BlueprintNetworkPagedListData blueprintNetworkPagedList;
        private BlueprintNetworkDetailData blueprintNetworkDetail;
        private Task<BlueprintNetworkStatusData> blueprintNetworkStatusTask;
        private Task<BlueprintNetworkPagedListData> blueprintNetworkListTask;
        private Task<BlueprintNetworkDetailData> blueprintNetworkDetailTask;
        private Dictionary<string, Texture2D> blueprintRemotePreviewCache = new Dictionary<string, Texture2D>();
        private Dictionary<string, Task<byte[]>> blueprintRemotePreviewTasks = new Dictionary<string, Task<byte[]>>();
        private Vector2 blueprintNetworkModScrollPos;
        private string blueprintNetworkCodeBuffer = "";
        private string blueprintNetworkMessage = "";
        private string blueprintNetworkError = "";
        private CancellationTokenSource blueprintNetworkCts;
        private readonly HashSet<string> expandedReviewReplyIds = new HashSet<string>();
        private readonly HashSet<string> expandedReviewNegotiationHistoryIds = new HashSet<string>();

        private static readonly Color CAccent = new Color(0.25f, 0.65f, 0.85f, 1f);
        private static readonly Color CPanel = new Color(0f, 0f, 0f, 0.18f);
        private static readonly Color CPanelAlt = new Color(1f, 1f, 1f, 0.03f);
        private static readonly Color CDim = new Color(0.72f, 0.72f, 0.72f, 1f);
        private static readonly Color COk = new Color(0.35f, 0.80f, 0.45f, 1f);
        private static readonly Color CWarn = new Color(0.95f, 0.72f, 0.25f, 1f);

        public override Vector2 RequestedTabSize => new Vector2(1220f, 720f);

        public override void PreOpen()
        {
            base.PreOpen();
            uiContext.Window = this;
            EnsurePages();
        }

        /// <summary>
        /// 关闭经营管理窗口前清理网络蓝图异步任务，负责避免后台请求继续占用资源。
        /// </summary>
        public override void PreClose()
        {
            base.PreClose();
            SimShopUiApi.ClearContext(uiContext);
            CancelBlueprintNetworkRequests();
            BlueprintPreviewTextureCache.Clear();
            BusinessPageWorker_Extensions.ClearPreviewCache();
        }

        public override void DoWindowContents(Rect inRect)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWordWrap = Text.WordWrap;
            Color oldColor = GUI.color;

            try
            {
                if (blueprintSelectionActive)
                    return;

                EnsurePages();
                uiContext.Window = this;
                uiContext.WindowRect = inRect;

                Rect tabRect = new Rect(inRect.x, inRect.y, inRect.width, 42f);
                DrawPageTabs(tabRect);

                Rect bodyRect = new Rect(inRect.x, tabRect.yMax + 6f, inRect.width, inRect.height - tabRect.height - 6f);
                Widgets.DrawBoxSolid(bodyRect, CPanel);
                if (curPageIndex >= 0 && curPageIndex < pages.Count)
                {
                    ShopUiPageDef page = pages[curPageIndex];
                    SimShopUiApi.SafeInvoke(page, uiContext, "DrawPage", worker =>
                    {
                        worker?.DrawPage(bodyRect.ContractedBy(10f), uiContext);
                    });

                    if (uiContext.LastException != null)
                    {
                        ShopUiLayoutUtility.DrawErrorState(bodyRect.ContractedBy(10f), SimTranslation.TOrFallback("RSMF.ShopUi.Error.PageDrawFailed", "Page drawing failed."), uiContext.LastException.Message);
                    }
                }
            }
            finally
            {
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
                Text.WordWrap = oldWordWrap;
                GUI.color = oldColor;
            }
        }

        private void EnsurePages()
        {
            if (!pages.NullOrEmpty() && !SimShopUiApi.ConsumeRefreshRequest())
            {
                if (curPageIndex >= pages.Count)
                    curPageIndex = Mathf.Max(0, pages.Count - 1);
                return;
            }

            string previousDefName = curPageDefName;
            pages.Clear();
            uiContext.Window = this;
            pages.AddRange(SimShopUiApi.GetPages(ShopUiPageScope.BusinessManager, uiContext));
            ApplyBusinessPageOrder();
            PruneBusinessPageSettings();
            if (!string.IsNullOrEmpty(previousDefName))
            {
                int restoredIndex = pages.FindIndex(page => page.defName == previousDefName);
                if (restoredIndex >= 0 && IsBusinessPageVisible(pages[restoredIndex]))
                    curPageIndex = restoredIndex;
            }
            if (pages.Count > 0 && !IsBusinessPageVisible(CurrentBusinessPageOrNull()))
                curPageIndex = FindFirstVisibleBusinessPageIndex();
            if (curPageIndex >= pages.Count)
                curPageIndex = Mathf.Max(0, pages.Count - 1);
            if (pages.Count > 0)
            {
                curPageDefName = pages[curPageIndex].defName;
                EnsureSelectedTabPageVisible(new Rect(0f, 0f, RequestedTabSize.x, 42f));
                NotifyPageOpened(pages[curPageIndex]);
            }
        }

        private void DrawPageTabs(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.2f));
            Rect outRect = rect.ContractedBy(4f);
            List<int> visiblePageIndices = BuildVisibleBusinessPageIndices();
            if (visiblePageIndices.Count == 0)
                return;

            bool hasPager;
            List<TabPageRange> ranges = BuildTabPageRanges(outRect.width, visiblePageIndices, out hasPager);
            pageTabPageIndex = Mathf.Clamp(pageTabPageIndex, 0, Mathf.Max(0, ranges.Count - 1));
            if (ranges.Count == 0)
                return;

            TabPageRange range = ranges[pageTabPageIndex];

            Rect tabArea = outRect;
            tabArea.width = Mathf.Max(1f, outRect.width - TabSelectorWidth - TabControlGap - (hasPager ? TabPagerWidth + TabControlGap : 0f));
            Rect selectorRect = new Rect(outRect.xMax - TabSelectorWidth, outRect.y + (outRect.height - TabControlHeight) / 2f, TabSelectorWidth, TabControlHeight);

            float x = 4f;
            const float tabH = 30f;
            float y = outRect.y + (outRect.height - tabH) / 2f;

            for (int i = range.StartIndex; i <= range.EndIndex && i < visiblePageIndices.Count; i++)
            {
                int pageIndex = visiblePageIndices[i];
                ShopUiPageDef page = pages[pageIndex];
                string label = page.DisplayLabel;
                float w = GetPageTabWidth(label);
                Rect tab = new Rect(tabArea.x + x, y, w, tabH);

                bool selected = pageIndex == curPageIndex;
                if (SimUiStyle.DrawTabButton(tab, label, selected, CDim))
                {
                    SelectBusinessPage(pageIndex);
                }

                x += w + 8f;
            }

            if (hasPager)
                DrawTabPager(new Rect(selectorRect.x - TabControlGap - TabPagerWidth, outRect.y, TabPagerWidth, outRect.height), ranges.Count);

            if (SimUiStyle.DrawSecondaryButton(selectorRect, SimTranslation.TOrFallback("RSMF.Business.Pages.Select", "页面"), true, GameFont.Tiny))
                ShowBusinessPageManager();
        }

        private const float TabPagerWidth = 176f;
        private const float TabSelectorWidth = 96f;
        private const float TabControlGap = 8f;
        private const float TabControlHeight = 28f;
        private const float TabMinWidth = 110f;
        private const float TabMaxWidth = 188f;

        /// <summary>
        /// 切换经商管理页面，负责同步当前页 Def、重置页面滚动状态并触发页面打开生命周期。
        /// </summary>
        private void SelectBusinessPage(int pageIndex)
        {
            if (pageIndex < 0 || pageIndex >= pages.Count)
                return;

            curPageIndex = pageIndex;
            ShopUiPageDef page = pages[curPageIndex];
            curPageDefName = page.defName;
            shopScrollPos = Vector2.zero;
            financeScrollPos = Vector2.zero;
            financeLogScrollPos = Vector2.zero;
            customerScrollPos = Vector2.zero;
            reviewScrollPos = Vector2.zero;
            collectibleExchangeScrollPos = Vector2.zero;
            reviewPageIndex = 0;
            staffScrollPos = Vector2.zero;
            vendingScrollPos = Vector2.zero;
            blueprintScrollPos = Vector2.zero;
            NotifyPageOpened(page);
        }

        /// <summary>
        /// 绘制页签分页按钮，负责在页面数量过多时提供稳定的上一页和下一页切换入口。
        /// </summary>
        private void DrawTabPager(Rect rect, int pageCount)
        {
            float buttonH = Mathf.Max(Text.LineHeightOf(GameFont.Tiny) + 8f, 28f);
            float y = rect.y + (rect.height - buttonH) / 2f;
            Rect prevRect = new Rect(rect.x, y, 42f, buttonH);
            Rect labelRect = new Rect(prevRect.xMax + 6f, y, 72f, buttonH);
            Rect nextRect = new Rect(labelRect.xMax + 6f, y, 42f, buttonH);

            if (SimUiStyle.DrawSecondaryButton(prevRect, "<", pageTabPageIndex > 0, GameFont.Tiny))
                pageTabPageIndex--;

            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWordWrap = Text.WordWrap;
            Color oldColor = GUI.color;
            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.WordWrap = false;
                GUI.color = CDim;
                Widgets.Label(labelRect, (pageTabPageIndex + 1) + "/" + pageCount);
            }
            finally
            {
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
                Text.WordWrap = oldWordWrap;
                GUI.color = oldColor;
            }

            if (SimUiStyle.DrawSecondaryButton(nextRect, ">", pageTabPageIndex < pageCount - 1, GameFont.Tiny))
                pageTabPageIndex++;
        }

        /// <summary>
        /// 确保当前选中页签所在分页可见，负责处理外部刷新或恢复页面后页签分页不同步的问题。
        /// </summary>
        private void EnsureSelectedTabPageVisible(Rect outRect)
        {
            List<int> visiblePageIndices = BuildVisibleBusinessPageIndices();
            bool hasPager;
            List<TabPageRange> ranges = BuildTabPageRanges(outRect.width, visiblePageIndices, out hasPager);
            if (ranges.Count == 0)
            {
                pageTabPageIndex = 0;
                return;
            }

            for (int i = 0; i < ranges.Count; i++)
            {
                if (ranges[i].ContainsPageIndex(visiblePageIndices, curPageIndex))
                {
                    pageTabPageIndex = i;
                    return;
                }
            }

            pageTabPageIndex = Mathf.Clamp(pageTabPageIndex, 0, ranges.Count - 1);
        }

        /// <summary>
        /// 根据当前窗口宽度切分页签范围，负责让每一页页签都能完整放入可用宽度。
        /// </summary>
        private List<TabPageRange> BuildTabPageRanges(float availableWidth, List<int> visiblePageIndices, out bool hasPager)
        {
            hasPager = false;
            if (visiblePageIndices.NullOrEmpty())
                return new List<TabPageRange>();

            float tabWidthWithoutPager = Mathf.Max(TabMinWidth, availableWidth - TabSelectorWidth - TabControlGap);
            List<TabPageRange> fullWidthRanges = BuildTabPageRangesForWidth(tabWidthWithoutPager, visiblePageIndices);
            if (fullWidthRanges.Count <= 1)
                return fullWidthRanges;

            hasPager = true;
            float tabWidthWithPager = Mathf.Max(TabMinWidth, availableWidth - TabSelectorWidth - TabPagerWidth - TabControlGap * 2f);
            return BuildTabPageRangesForWidth(tabWidthWithPager, visiblePageIndices);
        }

        /// <summary>
        /// 按指定页签区域宽度切分范围，负责给带分页器和不带分页器的布局复用同一套测量逻辑。
        /// </summary>
        private List<TabPageRange> BuildTabPageRangesForWidth(float tabAreaWidth, List<int> visiblePageIndices)
        {
            List<TabPageRange> ranges = new List<TabPageRange>();
            int start = 0;
            float used = 4f;
            for (int i = 0; i < visiblePageIndices.Count; i++)
            {
                int pageIndex = visiblePageIndices[i];
                float width = GetPageTabWidth(pages[pageIndex]?.DisplayLabel ?? "");
                float nextUsed = used + width + 8f;
                if (i > start && nextUsed > tabAreaWidth)
                {
                    ranges.Add(new TabPageRange(start, i - 1));
                    start = i;
                    used = 4f + width + 8f;
                    continue;
                }

                used = nextUsed;
            }

            ranges.Add(new TabPageRange(start, visiblePageIndices.Count - 1));
            return ranges;
        }

        /// <summary>
        /// 测量页签宽度，负责限制过长翻译文本占满整行。
        /// </summary>
        private static float GetPageTabWidth(string label)
        {
            GameFont oldFont = Text.Font;
            try
            {
                Text.Font = GameFont.Small;
                return Mathf.Clamp(Text.CalcSize(label ?? "").x + 30f, TabMinWidth, TabMaxWidth);
            }
            finally
            {
                Text.Font = oldFont;
            }
        }

        /// <summary>
        /// 返回当前仍显示在顶部页签栏中的页面下标，负责把页面过滤和分页测量分离。
        /// </summary>
        private List<int> BuildVisibleBusinessPageIndices()
        {
            List<int> result = new List<int>();
            for (int i = 0; i < pages.Count; i++)
            {
                if (IsBusinessPageVisible(pages[i]))
                    result.Add(i);
            }
            return result;
        }

        /// <summary>
        /// 打开经商管理页面管理器，负责让玩家在独立窗口中调整页面显示和顺序。
        /// </summary>
        private void ShowBusinessPageManager()
        {
            Find.WindowStack.Add(new Dialog_BusinessPageManager(pages, ApplyBusinessPageManagement));
        }

        /// <summary>
        /// 判断页面是否显示在顶部页签中，负责默认让新增或首次出现的页面保持可见。
        /// </summary>
        private bool IsBusinessPageVisible(ShopUiPageDef page)
        {
            SimManagementLibSettings settings = SimManagementLibMod.Settings;
            return page != null
                && !string.IsNullOrEmpty(page.defName)
                && (settings?.businessManagerHiddenPages == null || !settings.businessManagerHiddenPages.Contains(page.defName));
        }

        /// <summary>
        /// 按玩家设置重排经商管理页面，负责让顶部页签和页面管理器使用同一顺序。
        /// </summary>
        private void ApplyBusinessPageOrder()
        {
            List<string> order = SimManagementLibMod.Settings?.businessManagerPageOrder;
            if (order.NullOrEmpty() || pages.Count <= 1)
                return;

            List<ShopUiPageDef> orderedPages = new List<ShopUiPageDef>();
            for (int i = 0; i < order.Count; i++)
            {
                string defName = order[i];
                int pageIndex = pages.FindIndex(page => page?.defName == defName);
                if (pageIndex >= 0 && !orderedPages.Contains(pages[pageIndex]))
                    orderedPages.Add(pages[pageIndex]);
            }

            for (int i = 0; i < pages.Count; i++)
            {
                if (!orderedPages.Contains(pages[i]))
                    orderedPages.Add(pages[i]);
            }

            pages.Clear();
            pages.AddRange(orderedPages);
        }

        /// <summary>
        /// 清理已不存在页面的排序和隐藏记录，负责避免外部页面卸载后留下无效配置。
        /// </summary>
        private void PruneBusinessPageSettings()
        {
            SimManagementLibSettings settings = SimManagementLibMod.Settings;
            if (settings == null)
                return;

            PrunePageDefNameList(settings.businessManagerPageOrder);
            PrunePageDefNameList(settings.businessManagerHiddenPages);
            if (BuildVisibleBusinessPageIndices().Count == 0 && pages.Count > 0)
                settings.businessManagerHiddenPages.Clear();
        }

        /// <summary>
        /// 清理指定页面标识列表，负责保留仍存在且不重复的页面 DefName。
        /// </summary>
        private void PrunePageDefNameList(List<string> defNames)
        {
            if (defNames == null)
                return;

            HashSet<string> seen = new HashSet<string>();
            for (int i = defNames.Count - 1; i >= 0; i--)
            {
                string defName = defNames[i];
                if (string.IsNullOrWhiteSpace(defName) || !seen.Add(defName) || pages.FindIndex(page => page?.defName == defName) < 0)
                    defNames.RemoveAt(i);
            }
        }

        /// <summary>
        /// 应用页面管理器提交的配置，负责持久化排序、隐藏状态并恢复当前选中页面。
        /// </summary>
        internal void ApplyBusinessPageManagement(List<string> pageOrder, HashSet<string> hiddenPages)
        {
            SimManagementLibSettings settings = SimManagementLibMod.Settings;
            if (settings == null)
                return;

            settings.businessManagerPageOrder = NormalizeSubmittedBusinessPageOrder(pageOrder);
            settings.businessManagerHiddenPages = NormalizeSubmittedHiddenBusinessPages(hiddenPages);
            settings.Write();

            string previousDefName = curPageDefName;
            ApplyBusinessPageOrder();
            PruneBusinessPageSettings();

            int restoredIndex = !string.IsNullOrEmpty(previousDefName)
                ? pages.FindIndex(page => page?.defName == previousDefName)
                : -1;
            if (restoredIndex >= 0 && IsBusinessPageVisible(pages[restoredIndex]))
                SelectBusinessPage(restoredIndex);
            else
                SelectBusinessPage(FindFirstVisibleBusinessPageIndex());

            EnsureSelectedTabPageVisible(new Rect(0f, 0f, RequestedTabSize.x, 42f));
        }

        /// <summary>
        /// 规范化管理器提交的页面顺序，负责补齐新增页面并丢弃无效页面。
        /// </summary>
        private List<string> NormalizeSubmittedBusinessPageOrder(List<string> pageOrder)
        {
            List<string> result = new List<string>();
            HashSet<string> seen = new HashSet<string>();
            if (pageOrder != null)
            {
                for (int i = 0; i < pageOrder.Count; i++)
                    AddValidPageDefName(result, seen, pageOrder[i]);
            }

            for (int i = 0; i < pages.Count; i++)
                AddValidPageDefName(result, seen, pages[i]?.defName);

            return result;
        }

        /// <summary>
        /// 规范化管理器提交的隐藏页面，负责至少保留一个可见页。
        /// </summary>
        private List<string> NormalizeSubmittedHiddenBusinessPages(HashSet<string> hiddenPages)
        {
            List<string> result = new List<string>();
            if (hiddenPages != null)
            {
                for (int i = 0; i < pages.Count; i++)
                {
                    string defName = pages[i]?.defName;
                    if (!string.IsNullOrEmpty(defName) && hiddenPages.Contains(defName))
                        result.Add(defName);
                }
            }

            if (result.Count >= pages.Count && result.Count > 0)
                result.RemoveAt(0);

            return result;
        }

        /// <summary>
        /// 添加有效页面标识到结果列表，负责过滤空值、重复值和不存在的页面。
        /// </summary>
        private void AddValidPageDefName(List<string> result, HashSet<string> seen, string defName)
        {
            if (string.IsNullOrWhiteSpace(defName) || seen.Contains(defName) || pages.FindIndex(page => page?.defName == defName) < 0)
                return;

            seen.Add(defName);
            result.Add(defName);
        }

        /// <summary>
        /// 返回当前选中页面对象，负责让可见性恢复逻辑避免重复边界判断。
        /// </summary>
        private ShopUiPageDef CurrentBusinessPageOrNull()
        {
            if (curPageIndex < 0 || curPageIndex >= pages.Count)
                return null;
            return pages[curPageIndex];
        }

        /// <summary>
        /// 查找第一个可见页面下标，负责在当前页被隐藏或页面刷新后恢复到可绘制页面。
        /// </summary>
        private int FindFirstVisibleBusinessPageIndex()
        {
            for (int i = 0; i < pages.Count; i++)
            {
                if (IsBusinessPageVisible(pages[i]))
                    return i;
            }
            return pages.Count > 0 ? 0 : -1;
        }

        /// <summary>
        /// 保存页签分页的起止下标，负责避免在绘制循环中重复计算范围。
        /// </summary>
        private struct TabPageRange
        {
            public readonly int StartIndex;
            public readonly int EndIndex;

            public TabPageRange(int startIndex, int endIndex)
            {
                StartIndex = startIndex;
                EndIndex = endIndex;
            }

            /// <summary>
            /// 判断指定页面下标是否落在当前页签分页中。
            /// </summary>
            public bool ContainsPageIndex(List<int> visiblePageIndices, int pageIndex)
            {
                if (visiblePageIndices == null)
                    return false;
                for (int i = StartIndex; i <= EndIndex && i < visiblePageIndices.Count; i++)
                {
                    if (visiblePageIndices[i] == pageIndex)
                        return true;
                }
                return false;
            }
        }

        /// <summary>
        /// 通知当前页面已打开，负责触发 Def Worker 的打开生命周期。
        /// </summary>
        private void NotifyPageOpened(ShopUiPageDef page)
        {
            if (page == null) return;
            SimShopUiApi.SafeInvoke(page, uiContext, "OnOpen", worker => worker?.OnOpen(uiContext));
        }

        private static void DrawBorder(Rect rect, Color color)
        {
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, rect.width, 1f), color);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 1f, rect.height), color);
            Widgets.DrawBoxSolid(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
        }

        private static void ResetText()
        {
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            Text.WordWrap = true;
            GUI.color = Color.white;
        }
    }
}
