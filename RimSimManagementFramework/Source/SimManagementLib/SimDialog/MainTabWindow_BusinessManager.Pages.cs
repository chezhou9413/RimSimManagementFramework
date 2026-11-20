using RimWorld;
using SimManagementLib.Api;
using SimManagementLib.GameComp;
using SimManagementLib.Pojo;
using SimManagementLib.SimDef;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimAI;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    public partial class MainTabWindow_BusinessManager
    {
        /// <summary>
        /// 绘制商店管理页，展示商店状态、经营指标、客流倍率和快捷管理入口。
        /// </summary>
        private void DrawShopManagementPage(Rect rect)
        {
            string search = uiContext.SearchText ?? "";
            List<ShopViewData> shops = CollectAllShops().Where(entry => search.Length == 0
                || entry.Zone.label.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) >= 0
                || (entry.Map.info?.parent?.LabelCap.ToString() ?? "").IndexOf(search, System.StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (shops.NullOrEmpty())
            {
                string message = SimTranslation.T(search.Length == 0
                    ? "RSMF.Business.Empty.NoManageableShops" : "RSMF.Business.Operations.NoMatches");
                ShopUiVisualUtility.DrawNotice(new Rect(rect.x, rect.y, rect.width,
                    ShopUiVisualUtility.NoticeHeight(message, rect.width)), message);
                return;
            }

            float viewWidth = rect.width - 18f;
            float rowHeight = 150f;
            Rect viewRect = new Rect(0f, 0f, viewWidth, shops.Count * rowHeight);
            Widgets.BeginScrollView(rect, ref shopScrollPos, viewRect);

            GameComponent_ShopComboManager comboManager = Current.Game?.GetComponent<GameComponent_ShopComboManager>();
            GameComponent_ShopAnalyticsManager analytics = Current.Game?.GetComponent<GameComponent_ShopAnalyticsManager>();

            for (int i = 0; i < shops.Count; i++)
            {
                ShopViewData entry = shops[i];
                Rect row = new Rect(0f, i * rowHeight, viewWidth, rowHeight - 6f);
                Widgets.DrawBoxSolid(row, i % 2 == 0 ? CPanelAlt : new Color(0f, 0f, 0f, 0.08f));
                DrawBorder(row, new Color(1f, 1f, 1f, 0.12f));

                Zone_Shop zone = entry.Zone;
                HashSet<Building_SimContainer> storages = ShopDataUtility.GetStoragesInZone(zone);
                int goodsKinds = ShopDataUtility.GetAllSellableGoods(zone).Count;
                int inStockKinds = ShopDataUtility.GetInStockGoods(zone).Count;
                int comboCount = comboManager?.GetCombosForZone(zone)?.Count ?? 0;
                ShopMetricsSnapshot metrics = analytics?.GetOrEvaluateShopMetrics(zone);
                GameComponent_CustomerReviewManager reviewManager = Current.Game?.GetComponent<GameComponent_CustomerReviewManager>();
                bool valid = zone.IsValidShop();
                bool open = zone.IsOpenNow();
                float avgStars = 0f;
                int reviewCount = 0;
                reviewManager?.GetShopReviewStats(zone.ID, out avgStars, out reviewCount);

                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = Color.white;
                Widgets.Label(new Rect(row.x + 10f, row.y + 6f, 360f, 26f), zone.label.CapitalizeFirst());

                Text.Font = GameFont.Tiny;
                GUI.color = CDim;
                Widgets.Label(new Rect(row.x + 10f, row.y + 30f, 500f, 20f), SimTranslation.T("RSMF.Business.MapLine",
                    (entry.Map.info?.parent?.LabelCap ?? entry.Map.ToString()).Named("map")));

                GUI.color = open ? COk : CWarn;
                Widgets.Label(new Rect(row.x + 10f, row.y + 50f, 220f, 20f), open ? SimTranslation.T("RSMF.Business.Status.Open") : SimTranslation.T("RSMF.Business.Status.Custom", zone.GetOpenStatusMessage().Truncate(90f).Named("status")));

                GUI.color = Color.white;
                Widgets.Label(new Rect(row.x + 240f, row.y + 50f, 420f, 20f), SimTranslation.T("RSMF.Business.ShopInventoryLine",
                    storages.Count.Named("storages"),
                    goodsKinds.Named("goodsKinds"),
                    inStockKinds.Named("inStockKinds"),
                    comboCount.Named("comboCount")));

                GUI.color = COk;
                if (metrics != null)
                {
                    Widgets.Label(
                        new Rect(row.x + 10f, row.y + 72f, row.width - 220f, 18f),
                        SimTranslation.T("RSMF.Business.ShopMetricsLine",
                            metrics.score.ToString("F1").Named("score"),
                            metrics.reputation.ToString("F1").Named("reputation"),
                            metrics.satisfaction.ToString("F1").Named("satisfaction"),
                            metrics.beautyAverage.ToString("F1").Named("beauty"),
                            metrics.dynamicCapacity.Named("capacity"),
                            avgStars.ToString("F1").Named("stars"),
                            reviewCount.Named("reviewCount")));
                    Widgets.Label(
                        new Rect(row.x + 10f, row.y + 90f, row.width - 220f, 18f),
                        SimTranslation.T("RSMF.Business.ShopDemandLine",
                            metrics.spawnDemandFactor.ToString("F2").Named("spawn"),
                            metrics.beautyDemandMultiplier.ToString("F2").Named("beauty"),
                            metrics.scaleDemandMultiplier.ToString("F2").Named("scale"),
                            metrics.scaleCapacityMultiplier.ToString("F2").Named("capacityScale"),
                            metrics.effectiveScale.ToString("F1").Named("effectiveScale")));
                }

                string comboPreview = BuildComboPreview(comboManager?.GetCombosForZone(zone));
                GUI.color = CDim;
                Widgets.Label(new Rect(row.x + 10f, row.y + 112f, row.width - 220f, 26f), SimTranslation.T("RSMF.Business.ComboPreview", comboPreview.Named("preview")));
                ResetText();

                float btnW = 92f;
                float btnH = 28f;
                float bx = row.xMax - btnW - 10f;
                float by = row.y + 10f;

                if (SimUiStyle.DrawPrimaryButton(new Rect(bx, by, btnW, btnH), SimTranslation.T("RSMF.Gizmo.ShopManagement.Label"), true, GameFont.Tiny))
                {
                    Find.WindowStack.Add(new Dialog_ShopManager(zone));
                }

                by += btnH + 6f;
                if (SimUiStyle.DrawSecondaryButton(new Rect(bx, by, btnW, btnH), SimTranslation.T("RSMF.Gizmo.ContainerManagement.Label"), true, GameFont.Tiny))
                {
                    OpenStorageManagerMenu(storages);
                }

                by += btnH + 6f;
                if (SimUiStyle.DrawSecondaryButton(new Rect(bx, by, btnW, btnH), SimTranslation.T("RSMF.Common.Locate"), true, GameFont.Tiny))
                {
                    IntVec3 focusCell = zone.Cells.Count > 0 ? zone.Cells.First() : zone.Map.Center;
                    CameraJumper.TryJump(focusCell, zone.Map);
                }
            }

            Widgets.EndScrollView();
        }

        private void DrawFinancePage(Rect rect)
        {
            GameComponent_ShopFinanceManager finance = Current.Game?.GetComponent<GameComponent_ShopFinanceManager>();
            if (finance == null)
            {
                Widgets.NoneLabel(rect.center.y, rect.width, SimTranslation.T("RSMF.Business.Empty.FinanceMissing"));
                return;
            }

            int today = GenDate.DaysPassed;
            float todayIncome = finance.DailyRevenue.TryGetValue(today, out float income) ? income : 0f;

            float summaryHeight = Text.LineHeightOf(GameFont.Tiny) + Text.LineHeightOf(GameFont.Medium) + 26f;
            Rect summaryRect = new Rect(rect.x, rect.y, rect.width, summaryHeight);
            DrawFinanceSummary(summaryRect, finance, todayIncome);

            Rect subTabsRect = new Rect(rect.x, summaryRect.yMax + 6f, rect.width, Mathf.Max(36f, Text.LineHeightOf(GameFont.Small) + 12f));
            DrawFinanceSubTabs(subTabsRect);

            Rect contentRect = new Rect(rect.x, subTabsRect.yMax + 6f, rect.width, rect.height - summaryRect.height - subTabsRect.height - 12f);
            if (financeSubPageIndex == 0)
                DrawFinanceOverviewPage(contentRect, finance);
            else
                DrawFinanceLogsPage(contentRect, finance);
        }

        private void DrawFinanceSubTabs(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.16f));
            DrawBorder(rect, new Color(1f, 1f, 1f, 0.1f));

            Rect left = new Rect(rect.x + 8f, rect.y + 4f, 110f, rect.height - 8f);
            Rect right = new Rect(left.xMax + 8f, left.y, 110f, left.height);

            if (SimUiStyle.DrawTabButton(left, SimTranslation.T("RSMF.Business.Finance.Tab.Overview"), financeSubPageIndex == 0, CDim))
            {
                financeSubPageIndex = 0;
                financeScrollPos = Vector2.zero;
            }

            if (SimUiStyle.DrawTabButton(right, SimTranslation.T("RSMF.Business.Finance.Tab.Bills"), financeSubPageIndex == 1, CDim))
            {
                financeSubPageIndex = 1;
                financeLogScrollPos = Vector2.zero;
            }
        }

        private void DrawFinanceOverviewPage(Rect rect, GameComponent_ShopFinanceManager finance)
        {
            float viewWidth = rect.width - 18f;
            var products = BuildProductRows(finance);
            var combos = BuildComboRows(finance);
            var shops = BuildShopRows(finance);
            var days = BuildDailyRows(finance);
            float viewHeight = FinanceChartsHeight + 8f + FinanceSectionHeight(products.Count)
                + FinanceSectionHeight(combos.Count) + FinanceSectionHeight(shops.Count) + FinanceSectionHeight(days.Count);
            Rect viewRect = new Rect(0f, 0f, viewWidth, viewHeight);
            Widgets.BeginScrollView(rect, ref financeScrollPos, viewRect);

            float y = 0f;
            y = DrawFinanceCharts(viewRect.width, y, finance);
            y = DrawFinanceSection(viewRect.width, y, SimTranslation.T("RSMF.Business.Finance.ProductSales"), products);
            y = DrawFinanceSection(viewRect.width, y, SimTranslation.T("RSMF.Business.Finance.ComboSales"), combos);
            y = DrawFinanceSection(viewRect.width, y, SimTranslation.T("RSMF.Business.Finance.ShopProfit"), shops);
            DrawFinanceSection(viewRect.width, y, SimTranslation.T("RSMF.Business.Finance.DailyProfit"), days);

            Widgets.EndScrollView();
        }

        private void DrawFinanceLogsPage(Rect rect, GameComponent_ShopFinanceManager finance)
        {
            IReadOnlyList<FinanceBillRecord> records = finance.BillRecords;
            int total = records?.Count ?? 0;
            int pageSize = Mathf.Clamp(SimManagementLibMod.Settings?.financeLogPageSize ?? 30, 10, 200);
            int totalPages = Mathf.Max(1, Mathf.CeilToInt(total / (float)pageSize));
            financeLogPageIndex = Mathf.Clamp(financeLogPageIndex, 0, totalPages - 1);

            Rect navRect = new Rect(rect.x, rect.y, rect.width, 34f);
            Widgets.DrawBoxSolid(navRect, new Color(0f, 0f, 0f, 0.2f));
            DrawBorder(navRect, new Color(1f, 1f, 1f, 0.1f));

            Rect prevRect = new Rect(navRect.x + 8f, navRect.y + 4f, 72f, navRect.height - 8f);
            Rect nextRect = new Rect(prevRect.xMax + 8f, prevRect.y, 72f, prevRect.height);
            if (SimUiStyle.DrawSecondaryButton(prevRect, SimTranslation.T("RSMF.Common.PreviousPage"), financeLogPageIndex > 0, GameFont.Tiny))
            {
                financeLogPageIndex--;
                financeLogScrollPos = Vector2.zero;
            }
            if (SimUiStyle.DrawSecondaryButton(nextRect, SimTranslation.T("RSMF.Common.NextPage"), financeLogPageIndex < totalPages - 1, GameFont.Tiny))
            {
                financeLogPageIndex++;
                financeLogScrollPos = Vector2.zero;
            }

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = CDim;
            Widgets.Label(new Rect(navRect.x + 170f, navRect.y, navRect.width - 180f, navRect.height), SimTranslation.T("RSMF.Business.Finance.PageInfo",
                (financeLogPageIndex + 1).Named("page"),
                totalPages.Named("pages"),
                pageSize.Named("pageSize"),
                total.Named("total")));
            ResetText();

            Rect listRect = new Rect(rect.x, navRect.yMax + 6f, rect.width, rect.height - navRect.height - 6f);
            int start = total - 1 - financeLogPageIndex * pageSize;
            int end = Mathf.Max(0, start - pageSize + 1);
            int count = total > 0 ? (start - end + 1) : 0;

            float viewWidth = listRect.width - 18f;
            float rowH = 58f;
            float viewHeight = Mathf.Max(listRect.height + 1f, count * rowH + 8f);
            Rect viewRect = new Rect(0f, 0f, viewWidth, viewHeight);
            Widgets.BeginScrollView(listRect, ref financeLogScrollPos, viewRect);

            if (total <= 0)
            {
                Widgets.NoneLabel(viewRect.center.y, viewRect.width, SimTranslation.T("RSMF.Business.Empty.NoBills"));
                Widgets.EndScrollView();
                return;
            }

            float y = 0f;
            int shown = 0;
            for (int i = start; i >= end; i--)
            {
                FinanceBillRecord record = records[i];
                if (record == null) continue;

                Rect row = new Rect(0f, y, viewRect.width, rowH - 4f);
                Widgets.DrawBoxSolid(row, shown % 2 == 0 ? CPanelAlt : new Color(1f, 1f, 1f, 0.01f));
                DrawBorder(row, new Color(1f, 1f, 1f, 0.08f));

                GUI.color = Color.white;
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(row.x + 6f, row.y + 2f, row.width - 12f, 20f), SimTranslation.T("RSMF.Business.Finance.BillLine",
                    record.gameDay.Named("day"),
                    record.zoneLabel.Named("shop"),
                    record.customerName.Named("customer"),
                    record.paidSilver.Named("paid")));

                GUI.color = CDim;
                Widgets.Label(new Rect(row.x + 6f, row.y + 22f, row.width - 12f, 30f), BuildBillLineSummary(record.lines));
                ResetText();

                y += rowH;
                shown++;
            }

            Widgets.EndScrollView();
        }

        private void DrawCustomerPage(Rect rect)
        {
            List<CustomerViewData> customers = CollectActiveCustomers();
            if (customers.NullOrEmpty())
            {
                Widgets.NoneLabel(rect.center.y, rect.width, SimTranslation.T("RSMF.Business.Empty.NoActiveCustomers"));
                return;
            }

            float avgBudgetUse = 0f;
            for (int i = 0; i < customers.Count; i++)
            {
                CustomerViewData c = customers[i];
                int pawnId = c.Pawn.thingIDNumber;
                float spent = c.Visit.GetTotalSpentIncludingCurrentBill(c.Pawn);
                float budget = Mathf.Max(1f, c.Visit.GetBudgetForPawn(pawnId));
                avgBudgetUse += Mathf.Clamp01(spent / budget);
            }
            avgBudgetUse /= Mathf.Max(1, customers.Count);

            Rect summaryRect = new Rect(rect.x, rect.y, rect.width, 64f);
            Widgets.DrawBoxSolid(summaryRect, new Color(0f, 0f, 0f, 0.22f));
            DrawBorder(summaryRect, new Color(1f, 1f, 1f, 0.12f));
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(new Rect(summaryRect.x + 10f, summaryRect.y, 320f, summaryRect.height), SimTranslation.T("RSMF.Business.Customer.ActiveCount", customers.Count.Named("count")));
            GUI.color = CAccent;
            Widgets.Label(new Rect(summaryRect.x + 180f, summaryRect.y, 320f, summaryRect.height), SimTranslation.T("RSMF.Business.Customer.AverageBudgetUse", (avgBudgetUse * 100f).ToString("F0").Named("percent")));
            ResetText();

            Rect listRect = new Rect(rect.x, summaryRect.yMax + 8f, rect.width, rect.height - summaryRect.height - 8f);
            float viewWidth = listRect.width - 18f;
            float actionButtonHeight = Mathf.Max(28f, Text.LineHeightOf(GameFont.Tiny) + 8f);
            const float actionButtonGap = 4f;
            float rowH = Mathf.Max(106f, actionButtonHeight * 3f + actionButtonGap * 2f + 16f);
            Rect viewRect = new Rect(0f, 0f, viewWidth, rowH * customers.Count);
            Widgets.BeginScrollView(listRect, ref customerScrollPos, viewRect);

            for (int i = 0; i < customers.Count; i++)
            {
                CustomerViewData item = customers[i];
                Rect row = new Rect(0f, i * rowH, viewWidth, rowH - 6f);
                Widgets.DrawBoxSolid(row, i % 2 == 0 ? CPanelAlt : new Color(0f, 0f, 0f, 0.08f));
                DrawBorder(row, new Color(1f, 1f, 1f, 0.10f));

                Pawn pawn = item.Pawn;
                LordJob_CustomerVisit visit = item.Visit;
                int pawnId = pawn.thingIDNumber;
                CustomerRuntimeSettings settings = visit.GetPawnSettings(pawnId);
                int budget = visit.GetBudgetForPawn(pawnId);
                float spent = visit.GetTotalSpentIncludingCurrentBill(pawn);
                float budgetUse = Mathf.Clamp01(spent / Mathf.Max(1f, budget));
                int patience = visit.GetQueuePatienceForPawn(pawnId);
                string shopLabel = item.ShopZone?.label ?? SimTranslation.T("RSMF.Business.Customer.ShopFallback", visit.targetShopZoneId.Named("id"));
                string curJob = pawn.CurJobDef?.LabelCap ?? SimTranslation.T("RSMF.Business.Customer.NoCurrentJob");

                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = Color.white;
                Widgets.Label(new Rect(row.x + 10f, row.y + 4f, 300f, 24f), pawn.LabelShortCap + $"  ({settings?.profileLabel ?? SimTranslation.T("RSMF.Common.Default")})");

                Text.Font = GameFont.Tiny;
                GUI.color = CDim;
                Widgets.Label(new Rect(row.x + 10f, row.y + 24f, 440f, 18f), SimTranslation.T("RSMF.Business.Customer.ShopMapLine",
                    shopLabel.Named("shop"),
                    (item.Map.info?.parent?.LabelCap ?? item.Map.ToString()).Named("map")));
                Widgets.Label(new Rect(row.x + 10f, row.y + 40f, 440f, 18f), SimTranslation.T("RSMF.Business.Customer.JobPatienceLine",
                    curJob.Named("job"),
                    Mathf.RoundToInt(patience / 60f).Named("patience")));

                Rect budgetBarRect = new Rect(row.x + 10f, row.y + 62f, 360f, 18f);
                Widgets.FillableBar(budgetBarRect, budgetUse, SolidColorMaterials.NewSolidColorTexture(CAccent), BaseContent.BlackTex, doBorder: false);
                DrawBorder(budgetBarRect, new Color(1f, 1f, 1f, 0.18f));
                GUI.color = Color.white;
                Widgets.Label(new Rect(budgetBarRect.x + 4f, budgetBarRect.y - 1f, budgetBarRect.width - 8f, budgetBarRect.height), SimTranslation.T("RSMF.Business.Customer.BudgetLine",
                    budget.Named("budget"),
                    spent.ToString("F0").Named("spent"),
                    (budget - spent).ToString("F0").Named("remaining")));

                GUI.color = CDim;
                Widgets.Label(new Rect(row.x + 380f, row.y + 62f, row.width - 500f, 18f), SimTranslation.T("RSMF.Business.Customer.PreferenceLine", BuildPreferenceText(settings).Named("preferences")));

                float btnW = 84f;
                float bx = row.xMax - btnW - 10f;
                float by = row.y + 8f;
                if (SimUiStyle.DrawSecondaryButton(new Rect(bx, by, btnW, actionButtonHeight), SimTranslation.T("RSMF.Common.Locate"), true, GameFont.Tiny))
                {
                    CameraJumper.TryJump(pawn);
                }

                by += actionButtonHeight + actionButtonGap;
                if (SimUiStyle.DrawSecondaryButton(new Rect(bx, by, btnW, actionButtonHeight), SimTranslation.T("RSMF.Common.Select"), true, GameFont.Tiny))
                {
                    Find.Selector.Select(pawn, playSound: false, forceDesignatorDeselect: false);
                }

                by += actionButtonHeight + actionButtonGap;
                if (SimUiStyle.DrawSecondaryButton(new Rect(bx, by, btnW, actionButtonHeight), SimTranslation.T("RSMF.CustomerDismiss.Label"), true, GameFont.Tiny))
                {
                    CustomerDismissalUtility.RequestDismiss(pawn);
                }

                ResetText();
            }

            Widgets.EndScrollView();
        }

        //绘制全图店铺店员与 24 小时排班覆盖总览页，每家店直接展示排班覆盖迷你条和空窗预警。
        private void DrawStaffPage(Rect rect)
        {
            List<ShopViewData> shops = CollectAllShops();
            if (shops.NullOrEmpty())
            {
                Widgets.NoneLabel(rect.center.y, rect.width, SimTranslation.T("RSMF.Business.Empty.NoStaffShops"));
                return;
            }

            int[] combinedCoverage = new int[24];
            int[] roleCoverage = new int[24];
            float rowHeight = 112f;
            float viewWidth = rect.width - 18f;
            Rect viewRect = new Rect(0f, 0f, viewWidth, shops.Count * rowHeight);
            Widgets.BeginScrollView(rect, ref staffScrollPos, viewRect);

            for (int i = 0; i < shops.Count; i++)
            {
                ShopViewData entry = shops[i];
                Zone_Shop zone = entry.Zone;
                Rect row = new Rect(0f, i * rowHeight, viewWidth, rowHeight - 8f);
                Widgets.DrawBoxSolid(row, i % 2 == 0 ? CPanelAlt : new Color(0f, 0f, 0f, 0.08f));
                DrawBorder(row, new Color(1f, 1f, 1f, 0.12f));

                List<ShopStaffRoleDef> roles = ShopStaffUtility.GetVisibleRoles(zone);
                int assignedCount = 0;
                int maxGapHours = 0;
                bool anyRoleAssigned = false;
                for (int h = 0; h < 24; h++) combinedCoverage[h] = int.MaxValue;

                for (int r = 0; r < roles.Count; r++)
                {
                    int roleCount = zone.GetAssignedPawns(roles[r].defName).Count;
                    assignedCount += roleCount;
                    if (roleCount <= 0) continue;
                    anyRoleAssigned = true;
                    int gap = ShopStaffShiftUtility.CountRoleOpenGapHours(zone, roles[r].defName, roleCoverage, out _, out _);
                    if (gap > maxGapHours) maxGapHours = gap;
                    for (int h = 0; h < 24; h++)
                    {
                        if (roleCoverage[h] < combinedCoverage[h])
                            combinedCoverage[h] = roleCoverage[h];
                    }
                }
                if (!anyRoleAssigned)
                {
                    for (int h = 0; h < 24; h++) combinedCoverage[h] = 0;
                }

                string openLabel = SimTranslation.T("RSMF.Business.Staff.OpenConfig");
                string locateLabel = SimTranslation.T("RSMF.Common.Locate");
                Text.Font = GameFont.Tiny;
                float openW = Mathf.Max(118f, Text.CalcSize(openLabel).x + 20f);
                float locateW = Mathf.Max(58f, Text.CalcSize(locateLabel).x + 16f);
                float rightButtonsW = openW + locateW + 24f;
                float leftW = Mathf.Max(180f, row.width - rightButtonsW - 16f);

                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = Color.white;
                Widgets.Label(new Rect(row.x + 12f, row.y + 8f, leftW, 24f), zone.label.CapitalizeFirst().Truncate(leftW));

                Text.Font = GameFont.Tiny;
                GUI.color = CDim;
                string metaLine = $"{SimTranslation.T("RSMF.Business.MapLine", (entry.Map.info?.parent?.LabelCap ?? entry.Map.ToString()).Named("map"))} · "
                    + SimTranslation.T("RSMF.Business.Staff.Line", roles.Count.Named("roles"), assignedCount.Named("assigned"), zone.GetOpenStatusMessage().Named("status"));
                Widgets.Label(new Rect(row.x + 12f, row.y + 34f, leftW, 18f), metaLine.Truncate(leftW));

                string shiftStatus = !anyRoleAssigned
                    ? SimTranslation.T("RSMF.Business.Staff.NoRoleAssigned")
                    : (maxGapHours > 0
                        ? SimTranslation.T("RSMF.Business.Staff.GapWarning", maxGapHours.Named("gap"))
                        : SimTranslation.T("RSMF.Business.Staff.AllCovered"));
                GUI.color = !anyRoleAssigned ? CDim : (maxGapHours > 0 ? new Color(0.95f, 0.72f, 0.25f) : new Color(0.35f, 0.80f, 0.45f));
                Widgets.Label(new Rect(row.x + 12f, row.y + 54f, leftW, 18f), shiftStatus.Truncate(leftW));

                //底部展示店铺已分配岗位的最小覆盖 24h 迷你条，空窗时段标红警示。
                int curHour = entry.Map != null ? GenLocalDate.HourInteger(entry.Map) : 0;
                Dialog_ShopStaffManager.DrawMiniCoverageStrip(
                    new Rect(row.x + 12f, row.y + 76f, leftW, 16f),
                    combinedCoverage,
                    zone.GetSchedule(),
                    curHour,
                    anyRoleAssigned);

                float btnY = row.y + (row.height - 32f) * 0.5f;
                Rect openRect = new Rect(row.xMax - openW - 12f, btnY, openW, 32f);
                Rect locateRect = new Rect(openRect.x - locateW - 8f, btnY, locateW, 32f);
                if (SimUiStyle.DrawSecondaryButton(locateRect, locateLabel, zone.Cells.Count > 0, GameFont.Tiny))
                    CameraJumper.TryJump(zone.Cells[0], entry.Map);
                if (SimUiStyle.DrawPrimaryButton(openRect, openLabel, true, GameFont.Tiny))
                    Find.WindowStack.Add(new Dialog_ShopStaffManager(zone));

                ResetText();
            }

            Widgets.EndScrollView();
        }
    }
}
