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
        private Vector2 pageTabScrollPos;
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
            if (!string.IsNullOrEmpty(previousDefName))
            {
                int restoredIndex = pages.FindIndex(page => page.defName == previousDefName);
                if (restoredIndex >= 0)
                    curPageIndex = restoredIndex;
            }
            if (curPageIndex >= pages.Count)
                curPageIndex = Mathf.Max(0, pages.Count - 1);
            if (pages.Count > 0)
            {
                curPageDefName = pages[curPageIndex].defName;
                NotifyPageOpened(pages[curPageIndex]);
            }
        }

        private void DrawPageTabs(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.2f));
            float viewWidth = 8f;
            for (int i = 0; i < pages.Count; i++)
            {
                string measureLabel = pages[i]?.DisplayLabel ?? "";
                viewWidth += Mathf.Max(110f, Text.CalcSize(measureLabel).x + 30f) + 8f;
            }

            Rect outRect = rect.ContractedBy(4f);
            Rect viewRect = new Rect(0f, 0f, Mathf.Max(outRect.width + 1f, viewWidth), outRect.height - 2f);
            Widgets.BeginScrollView(outRect, ref pageTabScrollPos, viewRect, false);

            float x = 4f;
            const float tabH = 30f;
            float y = (viewRect.height - tabH) / 2f;

            for (int i = 0; i < pages.Count; i++)
            {
                ShopUiPageDef page = pages[i];
                string label = page.DisplayLabel;
                float w = Mathf.Max(110f, Text.CalcSize(label).x + 30f);
                Rect tab = new Rect(x, y, w, tabH);

                bool selected = i == curPageIndex;
                if (SimUiStyle.DrawTabButton(tab, label, selected, CDim))
                {
                    curPageIndex = i;
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

                x += w + 8f;
            }

            Widgets.EndScrollView();
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
