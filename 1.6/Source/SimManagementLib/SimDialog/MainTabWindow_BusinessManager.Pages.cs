using RimWorld;
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
        private void DrawShopManagementPage(Rect rect)
        {
            List<ShopViewData> shops = CollectAllShops();
            if (shops.NullOrEmpty())
            {
                Widgets.NoneLabel(rect.center.y, rect.width, "(当前没有可管理的商店区域)");
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
                bool valid = zone.IsValidShop();

                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = Color.white;
                Widgets.Label(new Rect(row.x + 10f, row.y + 6f, 360f, 26f), zone.label + $"  (ID:{zone.ID})");

                Text.Font = GameFont.Tiny;
                GUI.color = CDim;
                Widgets.Label(new Rect(row.x + 10f, row.y + 30f, 500f, 20f), $"地图: {entry.Map.info?.parent?.LabelCap ?? entry.Map.ToString()}");

                GUI.color = valid ? COk : CWarn;
                Widgets.Label(new Rect(row.x + 10f, row.y + 50f, 220f, 20f), valid ? "状态: 可营业" : "状态: 条件未满足");

                GUI.color = Color.white;
                Widgets.Label(new Rect(row.x + 240f, row.y + 50f, 420f, 20f), $"货柜: {storages.Count}   在售商品: {goodsKinds}   有库存: {inStockKinds}   套餐: {comboCount}");

                GUI.color = COk;
                if (metrics != null)
                {
                    Widgets.Label(
                        new Rect(row.x + 10f, row.y + 72f, row.width - 220f, 18f),
                        $"评分 {metrics.score:F1}   口碑 {metrics.reputation:F1}   满意度 {metrics.satisfaction:F1}   美观 {metrics.beautyAverage:F1}   容量 {metrics.dynamicCapacity}");
                }

                string comboPreview = BuildComboPreview(comboManager?.GetCombosForZone(zone));
                GUI.color = CDim;
                Widgets.Label(new Rect(row.x + 10f, row.y + 92f, row.width - 220f, 32f), "套餐预览: " + comboPreview);
                ResetText();

                float btnW = 92f;
                float btnH = 28f;
                float bx = row.xMax - btnW - 10f;
                float by = row.y + 10f;

                if (SimUiStyle.DrawPrimaryButton(new Rect(bx, by, btnW, btnH), "商店管理", true, GameFont.Tiny))
                {
                    Find.WindowStack.Add(new Dialog_ShopManager(zone));
                }

                by += btnH + 6f;
                if (SimUiStyle.DrawSecondaryButton(new Rect(bx, by, btnW, btnH), "货柜管理", true, GameFont.Tiny))
                {
                    OpenStorageManagerMenu(storages);
                }

                by += btnH + 6f;
                if (SimUiStyle.DrawSecondaryButton(new Rect(bx, by, btnW, btnH), "定位", true, GameFont.Tiny))
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
                Widgets.NoneLabel(rect.center.y, rect.width, "(财务组件未初始化)");
                return;
            }

            int today = GenDate.DaysPassed;
            float todayIncome = finance.DailyRevenue.TryGetValue(today, out float income) ? income : 0f;

            Rect summaryRect = new Rect(rect.x, rect.y, rect.width, 84f);
            DrawFinanceSummary(summaryRect, finance.TotalIncome, finance.BillRecords.Count, todayIncome);

            Rect subTabsRect = new Rect(rect.x, summaryRect.yMax + 6f, rect.width, 34f);
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

            if (SimUiStyle.DrawTabButton(left, "统计总览", financeSubPageIndex == 0, CDim))
            {
                financeSubPageIndex = 0;
                financeScrollPos = Vector2.zero;
            }

            if (SimUiStyle.DrawTabButton(right, "账单日志", financeSubPageIndex == 1, CDim))
            {
                financeSubPageIndex = 1;
                financeLogScrollPos = Vector2.zero;
            }
        }

        private void DrawFinanceOverviewPage(Rect rect, GameComponent_ShopFinanceManager finance)
        {
            float viewWidth = rect.width - 18f;
            float viewHeight = EstimateFinanceViewHeight(finance);
            Rect viewRect = new Rect(0f, 0f, viewWidth, viewHeight);
            Widgets.BeginScrollView(rect, ref financeScrollPos, viewRect);

            float y = 0f;
            y = DrawFinanceSection(viewRect.width, y, "单品销量 / 收入", BuildProductRows(finance));
            y = DrawFinanceSection(viewRect.width, y, "套餐销量 / 收入", BuildComboRows(finance));
            y = DrawFinanceSection(viewRect.width, y, "商店盈利", BuildShopRows(finance));
            DrawFinanceSection(viewRect.width, y, "每日盈利", BuildDailyRows(finance));

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
            if (SimUiStyle.DrawSecondaryButton(prevRect, "上一页", financeLogPageIndex > 0, GameFont.Tiny))
            {
                financeLogPageIndex--;
                financeLogScrollPos = Vector2.zero;
            }
            if (SimUiStyle.DrawSecondaryButton(nextRect, "下一页", financeLogPageIndex < totalPages - 1, GameFont.Tiny))
            {
                financeLogPageIndex++;
                financeLogScrollPos = Vector2.zero;
            }

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = CDim;
            Widgets.Label(new Rect(navRect.x + 170f, navRect.y, navRect.width - 180f, navRect.height), $"第 {financeLogPageIndex + 1}/{totalPages} 页  每页 {pageSize} 条  共 {total} 条");
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
                Widgets.NoneLabel(viewRect.center.y, viewRect.width, "(暂无账单日志)");
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
                Widgets.Label(new Rect(row.x + 6f, row.y + 2f, row.width - 12f, 20f), $"第 {record.gameDay} 天  {record.zoneLabel}  顾客:{record.customerName}  支付:{record.paidSilver} 银");

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
                Widgets.NoneLabel(rect.center.y, rect.width, "(当前没有活跃顾客)");
                return;
            }

            float avgBudgetUse = 0f;
            for (int i = 0; i < customers.Count; i++)
            {
                CustomerViewData c = customers[i];
                int pawnId = c.Pawn.thingIDNumber;
                float spent = c.Visit.cartValues.TryGetValue(pawnId, out float val) ? val : 0f;
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
            Widgets.Label(new Rect(summaryRect.x + 10f, summaryRect.y, 320f, summaryRect.height), $"活跃顾客: {customers.Count}");
            GUI.color = CAccent;
            Widgets.Label(new Rect(summaryRect.x + 180f, summaryRect.y, 320f, summaryRect.height), $"平均预算使用率: {(avgBudgetUse * 100f):F0}%");
            ResetText();

            Rect listRect = new Rect(rect.x, summaryRect.yMax + 8f, rect.width, rect.height - summaryRect.height - 8f);
            float viewWidth = listRect.width - 18f;
            float rowH = 98f;
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
                float spent = visit.cartValues.TryGetValue(pawnId, out float val) ? val : 0f;
                float budgetUse = Mathf.Clamp01(spent / Mathf.Max(1f, budget));
                int patience = visit.GetQueuePatienceForPawn(pawnId);
                string shopLabel = item.ShopZone?.label ?? ("商店#" + visit.targetShopZoneId);
                string curJob = pawn.CurJobDef?.LabelCap ?? "(无)";

                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = Color.white;
                Widgets.Label(new Rect(row.x + 10f, row.y + 4f, 300f, 24f), pawn.LabelShortCap + $"  ({settings?.profileLabel ?? "默认档案"})");

                Text.Font = GameFont.Tiny;
                GUI.color = CDim;
                Widgets.Label(new Rect(row.x + 10f, row.y + 24f, 440f, 18f), $"商店: {shopLabel}   地图: {item.Map.info?.parent?.LabelCap ?? item.Map.ToString()}");
                Widgets.Label(new Rect(row.x + 10f, row.y + 40f, 440f, 18f), $"当前行为: {curJob}   结账耐心: {patience} ticks");

                Rect budgetBarRect = new Rect(row.x + 10f, row.y + 62f, 360f, 18f);
                Widgets.FillableBar(budgetBarRect, budgetUse, SolidColorMaterials.NewSolidColorTexture(CAccent), BaseContent.BlackTex, doBorder: false);
                DrawBorder(budgetBarRect, new Color(1f, 1f, 1f, 0.18f));
                GUI.color = Color.white;
                Widgets.Label(new Rect(budgetBarRect.x + 4f, budgetBarRect.y - 1f, budgetBarRect.width - 8f, budgetBarRect.height), $"预算 {budget}  已花 {spent:F0}  剩余 {(budget - spent):F0}");

                GUI.color = CDim;
                Widgets.Label(new Rect(row.x + 380f, row.y + 62f, row.width - 500f, 18f), "偏好: " + BuildPreferenceText(settings));

                float btnW = 84f;
                float btnH = 28f;
                float bx = row.xMax - btnW - 10f;
                float by = row.y + 10f;
                if (SimUiStyle.DrawSecondaryButton(new Rect(bx, by, btnW, btnH), "定位", true, GameFont.Tiny))
                {
                    CameraJumper.TryJump(pawn);
                }

                by += btnH + 8f;
                if (SimUiStyle.DrawSecondaryButton(new Rect(bx, by, btnW, btnH), "选中", true, GameFont.Tiny))
                {
                    Find.Selector.Select(pawn, playSound: false, forceDesignatorDeselect: false);
                }

                ResetText();
            }

            Widgets.EndScrollView();
        }

        private void DrawStaffPage(Rect rect)
        {
            List<ShopViewData> shops = CollectAllShops();
            if (shops.NullOrEmpty())
            {
                Widgets.NoneLabel(rect.center.y, rect.width, "(当前没有可配置店员的商店)");
                return;
            }

            float rowHeight = 92f;
            float viewWidth = rect.width - 18f;
            Rect viewRect = new Rect(0f, 0f, viewWidth, shops.Count * rowHeight);
            Widgets.BeginScrollView(rect, ref staffScrollPos, viewRect);

            for (int i = 0; i < shops.Count; i++)
            {
                ShopViewData entry = shops[i];
                Zone_Shop zone = entry.Zone;
                Rect row = new Rect(0f, i * rowHeight, viewWidth, rowHeight - 6f);
                Widgets.DrawBoxSolid(row, i % 2 == 0 ? CPanelAlt : new Color(0f, 0f, 0f, 0.08f));
                DrawBorder(row, new Color(1f, 1f, 1f, 0.12f));

                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = Color.white;
                Widgets.Label(new Rect(row.x + 10f, row.y + 8f, 420f, 24f), $"{zone.label.CapitalizeFirst()}  (ID:{zone.ID})");

                Text.Font = GameFont.Tiny;
                GUI.color = CDim;
                List<ShopStaffRoleDef> roles = ShopStaffUtility.GetVisibleRoles(zone);
                int assignedCount = roles.Sum(r => zone.GetAssignedPawns(r.defName).Count);
                Widgets.Label(new Rect(row.x + 10f, row.y + 32f, 520f, 18f), $"地图: {entry.Map.info?.parent?.LabelCap ?? entry.Map.ToString()}");
                Widgets.Label(new Rect(row.x + 10f, row.y + 50f, 600f, 18f), $"岗位数: {roles.Count}   已分配店员: {assignedCount}   状态: {(zone.IsValidShop() ? "有效" : zone.GetValidationMessage())}");

                Rect openRect = new Rect(row.xMax - 150f, row.y + 26f, 128f, 34f);
                if (SimUiStyle.DrawPrimaryButton(openRect, "打开店员配置", true, GameFont.Tiny))
                {
                    Find.WindowStack.Add(new Dialog_ShopStaffManager(zone));
                }

                ResetText();
            }

            Widgets.EndScrollView();
        }
    }
}
