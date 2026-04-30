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

        private static string BuildPreferenceText(CustomerRuntimeSettings settings)
        {
            if (settings == null) return "无";

            List<string> parts = new List<string>();
            if (!settings.preferredGoodsCategoryIds.NullOrEmpty())
                parts.AddRange(settings.preferredGoodsCategoryIds
                    .Select(id => GoodsCatalog.GetCategory(id)?.label)
                    .Where(label => !string.IsNullOrEmpty(label))
                    .Take(2));
            if (!settings.preferredThings.NullOrEmpty())
                parts.AddRange(settings.preferredThings.Where(t => t != null).Select(t => t.LabelCap.RawText).Take(2));

            if (parts.Count <= 0) return "无";
            return string.Join("、", parts.Distinct().Take(3));
        }

        private static string BuildComboPreview(List<ComboData> combos)
        {
            if (combos.NullOrEmpty()) return "无";

            List<string> preview = combos
                .Where(c => c != null)
                .Take(3)
                .Select(c =>
                {
                    string name = string.IsNullOrEmpty(c.comboName) ? "未命名套餐" : c.comboName;
                    return $"{name}(¥{c.totalPrice:F0})";
                })
                .ToList();

            if (combos.Count > 3)
                preview.Add("...");

            return string.Join("、", preview);
        }

        private void DrawFinanceSummary(Rect rect, float totalIncome, int billCount, float todayIncome)
        {
            const float gap = 8f;
            float cardWidth = (rect.width - gap * 2f) / 3f;

            DrawSummaryCard(new Rect(rect.x, rect.y, cardWidth, rect.height), "总收入", $"¥{totalIncome:F0}");
            DrawSummaryCard(new Rect(rect.x + cardWidth + gap, rect.y, cardWidth, rect.height), "总账单数", billCount.ToString());
            DrawSummaryCard(new Rect(rect.x + (cardWidth + gap) * 2f, rect.y, cardWidth, rect.height), "今日收入", $"¥{todayIncome:F0}");
        }

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
                Widgets.Label(new Rect(secRect.x + 8f, secRect.y + headerH, secRect.width - 16f, rowH), "暂无数据");
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

        private float EstimateFinanceViewHeight(GameComponent_ShopFinanceManager finance)
        {
            int lineCount = 0;
            lineCount += Mathf.Max(1, finance.ProductSoldCounts.Count);
            lineCount += Mathf.Max(1, finance.ComboSoldCounts.Count);
            lineCount += Mathf.Max(1, finance.ShopRevenue.Count);
            lineCount += Mathf.Max(1, finance.DailyRevenue.Count);
            lineCount += Mathf.Max(1, finance.BillRecords.Count * 3);
            return 460f + lineCount * 22f;
        }

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
                    return $"{label}: 售出 {kv.Value}，收入 ¥{revenue:F0}";
                })
                .ToList();
        }

        private List<string> BuildComboRows(GameComponent_ShopFinanceManager finance)
        {
            return finance.ComboSoldCounts
                .OrderByDescending(kv => kv.Value)
                .Take(30)
                .Select(kv =>
                {
                    float revenue = finance.ComboRevenues.TryGetValue(kv.Key, out float r) ? r : 0f;
                    return $"{kv.Key}: 售出 {kv.Value}，收入 ¥{revenue:F0}";
                })
                .ToList();
        }

        private List<string> BuildShopRows(GameComponent_ShopFinanceManager finance)
        {
            return finance.ShopRevenue
                .OrderByDescending(kv => kv.Value)
                .Take(30)
                .Select(kv =>
                {
                    float profit = finance.ShopProfit.TryGetValue(kv.Key, out float p) ? p : 0f;
                    string label = finance.GetShopLabel(kv.Key);
                    return $"{label}: 收入 ¥{kv.Value:F0}，利润 ¥{profit:F0}";
                })
                .ToList();
        }

        private List<string> BuildDailyRows(GameComponent_ShopFinanceManager finance)
        {
            return finance.DailyRevenue
                .OrderByDescending(kv => kv.Key)
                .Take(60)
                .Select(kv =>
                {
                    float profit = finance.DailyProfit.TryGetValue(kv.Key, out float p) ? p : 0f;
                    return $"第 {kv.Key} 天: 收入 ¥{kv.Value:F0}，利润 ¥{profit:F0}";
                })
                .ToList();
        }

        private static string BuildBillLineSummary(List<FinanceLineItem> lines)
        {
            if (lines.NullOrEmpty()) return "明细: (无)";

            List<string> parts = new List<string>();
            for (int i = 0; i < lines.Count; i++)
            {
                FinanceLineItem line = lines[i];
                if (line == null) continue;
                string name = string.IsNullOrEmpty(line.label) ? (line.isCombo ? "未命名套餐" : line.defName) : line.label;
                parts.Add($"{name} x{line.count} (¥{line.amount:F0})");
                if (parts.Count >= 4)
                {
                    parts.Add("...");
                    break;
                }
            }

            return "明细: " + string.Join("、", parts);
        }

        private static void OpenStorageManagerMenu(HashSet<Building_SimContainer> storages)
        {
            if (storages.NullOrEmpty())
            {
                Messages.Message("该商店未找到可配置货柜。", MessageTypeDefOf.RejectInput, false);
                return;
            }

            List<Building_SimContainer> ordered = storages
                .Where(s => s != null && !s.Destroyed)
                .OrderBy(s => s.thingIDNumber)
                .ToList();
            if (ordered.NullOrEmpty())
            {
                Messages.Message("该商店未找到可配置货柜。", MessageTypeDefOf.RejectInput, false);
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
                string label = $"货柜 #{i + 1} - {local.StorageDisplayLabel}";
                options.Add(new FloatMenuOption(label, () => OpenStorageManager(local)));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void OpenStorageManager(Building_SimContainer storage)
        {
            ThingComp_GoodsData comp = storage?.GetComp<ThingComp_GoodsData>();
            if (comp == null)
            {
                Messages.Message("该货柜无法打开管理面板。", MessageTypeDefOf.RejectInput, false);
                return;
            }

            Find.WindowStack.Add(new Dialog_GoodsManager(comp));
        }
    }
}
