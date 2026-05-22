using RimWorld;
using SimManagementLib.SimAI;
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
        private sealed class PageDef
        {
            public string Label;
            public Action<Rect> DrawAction;
        }

        private sealed class ShopViewData
        {
            public Map Map;
            public Zone_Shop Zone;
        }

        private sealed class CustomerViewData
        {
            public Map Map;
            public Pawn Pawn;
            public Zone_Shop ShopZone;
            public LordJob_CustomerVisit Visit;
        }

        private readonly List<PageDef> pages = new List<PageDef>();
        private bool pagesBuiltWithReviews;
        private int curPageIndex;
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
            EnsurePages();
        }

        /// <summary>
        /// 关闭经营管理窗口前清理网络蓝图异步任务，负责避免后台请求继续占用资源。
        /// </summary>
        public override void PreClose()
        {
            base.PreClose();
            CancelBlueprintNetworkRequests();
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

                Rect tabRect = new Rect(inRect.x, inRect.y, inRect.width, 42f);
                DrawPageTabs(tabRect);

                Rect bodyRect = new Rect(inRect.x, tabRect.yMax + 6f, inRect.width, inRect.height - tabRect.height - 6f);
                Widgets.DrawBoxSolid(bodyRect, CPanel);
                if (curPageIndex >= 0 && curPageIndex < pages.Count)
                {
                    pages[curPageIndex].DrawAction?.Invoke(bodyRect.ContractedBy(10f));
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
            bool shouldShowReviews = SimManagementLibMod.Settings?.HasValidReviewAiConfig() == true;
            if (!pages.NullOrEmpty() && pagesBuiltWithReviews == shouldShowReviews)
            {
                if (curPageIndex >= pages.Count)
                    curPageIndex = Mathf.Max(0, pages.Count - 1);
                return;
            }

            pages.Clear();
            pagesBuiltWithReviews = shouldShowReviews;
            pages.Add(new PageDef { Label = SimTranslation.T("RSMF.Business.Page.ShopManagement"), DrawAction = DrawShopManagementPage });
            pages.Add(new PageDef { Label = SimTranslation.T("RSMF.Business.Page.Vending"), DrawAction = DrawVendingMachinePage });
            pages.Add(new PageDef { Label = SimTranslation.T("RSMF.Business.Page.Finance"), DrawAction = DrawFinancePage });
            if (shouldShowReviews)
                pages.Add(new PageDef { Label = SimTranslation.T("RSMF.Business.Page.Reviews"), DrawAction = DrawCustomerReviewsPage });
            pages.Add(new PageDef { Label = SimTranslation.T("RSMF.Business.Page.CollectibleExchange"), DrawAction = DrawCollectibleExchangePage });
            pages.Add(new PageDef { Label = SimTranslation.T("RSMF.Business.Page.Blueprints"), DrawAction = DrawBlueprintPage });
            pages.Add(new PageDef { Label = SimTranslation.T("RSMF.Business.Page.Customers"), DrawAction = DrawCustomerPage });
            pages.Add(new PageDef { Label = SimTranslation.T("RSMF.Business.Page.Staff"), DrawAction = DrawStaffPage });
            if (curPageIndex >= pages.Count)
                curPageIndex = Mathf.Max(0, pages.Count - 1);
        }

        private void DrawPageTabs(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.2f));
            float x = rect.x + 8f;
            const float tabH = 30f;
            float y = rect.y + (rect.height - tabH) / 2f;

            for (int i = 0; i < pages.Count; i++)
            {
                PageDef page = pages[i];
                float w = Mathf.Max(110f, Text.CalcSize(page.Label).x + 30f);
                Rect tab = new Rect(x, y, w, tabH);

                bool selected = i == curPageIndex;
                if (SimUiStyle.DrawTabButton(tab, page.Label, selected, CDim))
                {
                    curPageIndex = i;
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
                }

                x += w + 8f;
            }
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
