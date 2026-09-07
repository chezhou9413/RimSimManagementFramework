using RimWorld;
using SimManagementLib.GameComp;
using SimManagementLib.Pojo;
using SimManagementLib.SimAI;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimThingComp;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace SimManagementLib.SimDialog
{
    public partial class MainTabWindow_BusinessManager
    {
        //汇集地图店铺数据，职责是为经营总览提供统一列表。
        private static List<ShopViewData> CollectAllShops()
        {
            List<ShopViewData> result = new List<ShopViewData>();
            if (Find.Maps == null) return result;

            foreach (Map map in Find.Maps)
            {
                if (map == null || !map.IsPlayerHome) continue;

                foreach (Zone_Shop zone in map.zoneManager.AllZones.OfType<Zone_Shop>())
                {
                    result.Add(new ShopViewData
                    {
                        Map = map,
                        Zone = zone
                    });
                }
            }

            return result
                .OrderBy(r => r.Map.Index)
                .ThenBy(r => r.Zone.ID)
                .ToList();
        }

        //汇集活跃顾客数据，职责是展示当前访问和消费状态。
        private static List<CustomerViewData> CollectActiveCustomers()
        {
            List<CustomerViewData> result = new List<CustomerViewData>();
            if (Find.Maps == null) return result;

            foreach (Map map in Find.Maps)
            {
                if (map == null || map.lordManager == null) continue;

                for (int i = 0; i < map.lordManager.lords.Count; i++)
                {
                    Lord lord = map.lordManager.lords[i];
                    if (!(lord?.LordJob is LordJob_CustomerVisit visit)) continue;

                    Zone_Shop zone = map.zoneManager.AllZones
                        .OfType<Zone_Shop>()
                        .FirstOrDefault(z => z.ID == visit.targetShopZoneId);

                    for (int p = 0; p < lord.ownedPawns.Count; p++)
                    {
                        Pawn pawn = lord.ownedPawns[p];
                        if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned) continue;

                        result.Add(new CustomerViewData
                        {
                            Map = map,
                            Pawn = pawn,
                            ShopZone = zone,
                            Visit = visit
                        });
                    }
                }
            }

            return result
                .OrderBy(c => c.Map.Index)
                .ThenBy(c => c.ShopZone?.ID ?? c.Visit.targetShopZoneId)
                .ThenBy(c => c.Pawn.thingIDNumber)
                .ToList();
        }

        //格式化顾客偏好，职责是生成适合列表显示的文本。
        private static string BuildPreferenceText(CustomerRuntimeSettings settings)
        {
            if (settings == null) return SimTranslation.T("RSMF.Common.None");

            List<string> parts = new List<string>();
            if (!settings.preferredGoodsCategoryIds.NullOrEmpty())
                parts.AddRange(settings.preferredGoodsCategoryIds
                    .Select(id => GoodsCatalog.GetCategory(id)?.label)
                    .Where(label => !string.IsNullOrEmpty(label))
                    .Take(2));
            if (!settings.preferredThings.NullOrEmpty())
                parts.AddRange(settings.preferredThings.Where(t => t != null).Select(t => t.LabelCap.RawText).Take(2));

            if (parts.Count <= 0) return SimTranslation.T("RSMF.Common.None");
            return string.Join("、", parts.Distinct().Take(3));
        }

        //汇总套餐预览，职责是展示店铺可售组合。
        private static string BuildComboPreview(List<ComboData> combos)
        {
            if (combos.NullOrEmpty()) return SimTranslation.T("RSMF.Common.None");

            List<string> preview = combos
                .Where(c => c != null)
                .Take(3)
                .Select(c =>
                {
                    string name = string.IsNullOrEmpty(c.comboName) ? SimTranslation.T("RSMF.Common.UnnamedCombo") : c.comboName;
                    return $"{name}(¥{c.totalPrice:F0})";
                })
                .ToList();

            if (combos.Count > 3)
                preview.Add("...");

            return string.Join("、", preview);
        }

        //绘制财务摘要，职责是呈现总收入、账单量和当日收入。
        private void DrawFinanceSummary(Rect rect, float totalIncome, int billCount, float todayIncome)
        {
            const float gap = 8f;
            float cardWidth = (rect.width - gap * 2f) / 3f;

            DrawSummaryCard(new Rect(rect.x, rect.y, cardWidth, rect.height), SimTranslation.T("RSMF.Business.Finance.TotalIncome"), $"¥{totalIncome:F0}");
            DrawSummaryCard(new Rect(rect.x + cardWidth + gap, rect.y, cardWidth, rect.height), SimTranslation.T("RSMF.Business.Finance.TotalBills"), billCount.ToString());
            DrawSummaryCard(new Rect(rect.x + (cardWidth + gap) * 2f, rect.y, cardWidth, rect.height), SimTranslation.T("RSMF.Business.Finance.TodayIncome"), $"¥{todayIncome:F0}");
        }

        //绘制单项统计卡片，职责是按统一样式展示标题和值。
        private void DrawSummaryCard(Rect rect, string title, string value)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.25f));
            DrawBorder(rect, new Color(1f, 1f, 1f, 0.12f));

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = CDim;
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, 20f), title);

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = CAccent;
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 22f, rect.width - 20f, rect.height - 30f), value);
            ResetText();
        }

        //绘制财务明细分区，职责是根据内容返回实际占用高度。
        private float DrawFinanceSection(float width, float startY, string title, List<string> rows)
        {
            float headerH = 28f;
            float rowH = 22f;
            float secH = headerH + Mathf.Max(rowH, rows.Count * rowH) + 8f;
            Rect secRect = new Rect(0f, startY, width, secH);

            Widgets.DrawBoxSolid(secRect, new Color(0f, 0f, 0f, 0.16f));
            DrawBorder(secRect, new Color(1f, 1f, 1f, 0.08f));

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(new Rect(secRect.x + 8f, secRect.y + 2f, secRect.width - 16f, headerH), title);

            if (rows.NullOrEmpty())
            {
                Text.Font = GameFont.Tiny;
                GUI.color = CDim;
                Widgets.Label(new Rect(secRect.x + 8f, secRect.y + headerH, secRect.width - 16f, rowH), SimTranslation.T("RSMF.Common.NoData"));
            }
            else
            {
                Text.Font = GameFont.Tiny;
                for (int i = 0; i < rows.Count; i++)
                {
                    GUI.color = i % 2 == 0 ? Color.white : CDim;
                    Widgets.Label(new Rect(secRect.x + 8f, secRect.y + headerH + i * rowH, secRect.width - 16f, rowH), rows[i]);
                }
            }

            ResetText();
            return secRect.yMax + 8f;
        }

        //估算财务滚动高度，职责是为各项统计预留显示区域。
        private float EstimateFinanceViewHeight(GameComponent_ShopFinanceManager finance)
        {
            int lineCount = 0;
            lineCount += Mathf.Max(1, finance.ProductSoldCounts.Count);
            lineCount += Mathf.Max(1, finance.ComboSoldCounts.Count);
            lineCount += Mathf.Max(1, finance.ShopRevenue.Count);
            lineCount += Mathf.Max(1, finance.DailyRevenue.Count);
            lineCount += Mathf.Max(1, finance.BillRecords.Count * 3);
            return 820f + lineCount * 22f;
        }

        //生成商品统计行，职责是汇总销量和收入。
        private List<string> BuildProductRows(GameComponent_ShopFinanceManager finance)
        {
            return finance.ProductSoldCounts
                .OrderByDescending(kv => kv.Value)
                .Take(30)
                .Select(kv =>
                {
                    ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(kv.Key);
                    string label = def != null ? def.LabelCap.RawText : kv.Key;
                    float revenue = finance.ProductRevenues.TryGetValue(kv.Key, out float r) ? r : 0f;
                    return SimTranslation.T("RSMF.Business.Finance.ProductRow",
                        label.Named("label"),
                        kv.Value.Named("count"),
                        revenue.ToString("F0").Named("revenue"));
                })
                .ToList();
        }

        //生成套餐统计行，职责是汇总组合成交结果。
        private List<string> BuildComboRows(GameComponent_ShopFinanceManager finance)
        {
            return finance.ComboSoldCounts
                .OrderByDescending(kv => kv.Value)
                .Take(30)
                .Select(kv =>
                {
                    float revenue = finance.ComboRevenues.TryGetValue(kv.Key, out float r) ? r : 0f;
                    return SimTranslation.T("RSMF.Business.Finance.ProductRow",
                        kv.Key.Named("label"),
                        kv.Value.Named("count"),
                        revenue.ToString("F0").Named("revenue"));
                })
                .ToList();
        }

        //生成店铺统计行，职责是展示各店收入和利润。
        private List<string> BuildShopRows(GameComponent_ShopFinanceManager finance)
        {
            return finance.ShopRevenue
                .OrderByDescending(kv => kv.Value)
                .Take(30)
                .Select(kv =>
                {
                    float profit = finance.ShopProfit.TryGetValue(kv.Key, out float p) ? p : 0f;
                    string label = finance.GetShopLabel(kv.Key);
                    return SimTranslation.T("RSMF.Business.Finance.ShopRow",
                        label.Named("label"),
                        kv.Value.ToString("F0").Named("revenue"),
                        profit.ToString("F0").Named("profit"));
                })
                .ToList();
        }

        //生成每日统计行，职责是展示按日期归集的经营结果。
        private List<string> BuildDailyRows(GameComponent_ShopFinanceManager finance)
        {
            return finance.DailyRevenue
                .OrderByDescending(kv => kv.Key)
                .Take(60)
                .Select(kv =>
                {
                    float profit = finance.DailyProfit.TryGetValue(kv.Key, out float p) ? p : 0f;
                    return SimTranslation.T("RSMF.Business.Finance.DailyRow",
                        kv.Key.Named("day"),
                        kv.Value.ToString("F0").Named("revenue"),
                        profit.ToString("F0").Named("profit"));
                })
                .ToList();
        }

        //生成账单行摘要，职责是描述本次交易包含的商品和服务。
        private static string BuildBillLineSummary(List<FinanceLineItem> lines)
        {
            if (lines.NullOrEmpty()) return SimTranslation.T("RSMF.Business.Finance.DetailsEmpty");

            List<string> parts = new List<string>();
            for (int i = 0; i < lines.Count; i++)
            {
                FinanceLineItem line = lines[i];
                if (line == null) continue;
                string name = string.IsNullOrEmpty(line.label) ? (line.isCombo ? SimTranslation.T("RSMF.Common.UnnamedCombo") : line.defName) : line.label;
                parts.Add($"{name} x{line.count} (¥{line.amount:F0})");
                if (parts.Count >= 4)
                {
                    parts.Add("...");
                    break;
                }
            }

            return SimTranslation.T("RSMF.Business.Finance.Details", string.Join(SimTranslation.T("RSMF.Common.ListSeparator"), parts).Named("details"));
        }

        //打开货柜选择菜单，职责是让用户选择要管理的具体设施。
        private static void OpenStorageManagerMenu(HashSet<Building_SimContainer> storages)
        {
            if (storages.NullOrEmpty())
            {
                Messages.Message(SimTranslation.T("RSMF.Business.Storage.NotFoundMessage"), MessageTypeDefOf.RejectInput, false);
                return;
            }

            List<Building_SimContainer> ordered = storages
                .Where(s => s != null && !s.Destroyed)
                .OrderBy(s => s.thingIDNumber)
                .ToList();
            if (ordered.NullOrEmpty())
            {
                Messages.Message(SimTranslation.T("RSMF.Business.Storage.NotFoundMessage"), MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (ordered.Count == 1)
            {
                OpenStorageManager(ordered[0]);
                return;
            }

            List<FloatMenuOption> options = new List<FloatMenuOption>();
            for (int i = 0; i < ordered.Count; i++)
            {
                Building_SimContainer storage = ordered[i];
                Building_SimContainer local = storage;
                string label = SimTranslation.T("RSMF.Business.Storage.OptionLabel",
                    (i + 1).Named("index"),
                    local.StorageDisplayLabel.Named("label"));
                options.Add(new FloatMenuOption(label, () => OpenStorageManager(local)));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        //打开指定货柜的管理窗口，职责是尊重扩展提供的窗口策略。
        private static void OpenStorageManager(Building_SimContainer storage)
        {
            ThingComp_GoodsData comp = storage?.GetComp<ThingComp_GoodsData>();
            if (comp == null)
            {
                Messages.Message(SimTranslation.T("RSMF.Business.Storage.CannotOpenMessage"), MessageTypeDefOf.RejectInput, false);
                return;
            }

            storage.OpenInventoryManagement();
        }
    }
}
