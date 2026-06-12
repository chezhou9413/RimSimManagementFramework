using RimWorld;
using SimManagementLib.GameComp;
using SimManagementLib.Pojo;
using SimManagementLib.SimAI;
using SimManagementLib.SimAI.CustomerVisit;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SimManagementLib.Debug
{
    /// <summary>
    /// 构建单个顾客的完整调试报告，负责把状态机、当前职责、商品匹配和最近行程日志汇总为可复制文本。
    /// </summary>
    public static class CustomerVisitDebugReportBuilder
    {
        private const int MaxRecentLogLines = 80;

        /// <summary>
        /// 构建指定顾客的诊断文本，负责给卡住顾客定位使用。
        /// </summary>
        public static string Build(Pawn pawn)
        {
            StringBuilder sb = new StringBuilder();
            AppendHeader(sb, pawn);

            Lord lord = pawn?.Map?.lordManager?.LordOf(pawn);
            LordJob_CustomerVisit visit = lord?.LordJob as LordJob_CustomerVisit;
            if (pawn == null || visit == null)
            {
                sb.AppendLine("不是 RimSim 顾客或顾客 Lord 已不存在。");
                return sb.ToString();
            }

            CustomerVisitSession session = visit.GetOrCreateSession(pawn);
            Zone_Shop shop = visit.GetCurrentShop(pawn);
            int pawnId = pawn.thingIDNumber;
            float owed = visit.GetAmountOwedForCheckout(pawnId);
            float remaining = shop != null ? visit.GetRemainingTripBudget(pawn, shop) : 0f;

            sb.AppendLine("[Lord]");
            sb.AppendLine("LordJob: " + lord.LordJob.GetType().FullName);
            sb.AppendLine("Toil: " + (lord.CurLordToil?.GetType().FullName ?? "无"));
            sb.AppendLine("ownedPawns: " + (lord.ownedPawns?.Count ?? 0));
            sb.AppendLine("readyForCheckout: " + visit.IsPawnReadyForCheckout(pawnId));
            sb.AppendLine("owed: " + owed.ToString("F2"));
            sb.AppendLine("cartValue: " + visit.GetCartValue(pawnId).ToString("F2"));
            sb.AppendLine("totalSpentWithCurrentBill: " + visit.GetTotalSpentIncludingCurrentBill(pawn).ToString("F2"));
            sb.AppendLine("remainingBudget: " + remaining.ToString("F2"));
            sb.AppendLine("needsPostCheckout: " + visit.NeedsPostCheckoutCompletion(pawnId));
            sb.AppendLine();

            sb.AppendLine("[Pawn]");
            sb.AppendLine("Name: " + pawn.LabelShortCap);
            sb.AppendLine("ThingID: " + pawnId);
            sb.AppendLine("Position: " + pawn.Position);
            sb.AppendLine("Faction: " + (pawn.Faction?.Name ?? "无"));
            sb.AppendLine("CurJob: " + (pawn.CurJobDef?.defName ?? "无"));
            sb.AppendLine("CurJobTargets: " + DescribeJobTargets(pawn.CurJob));
            sb.AppendLine("Duty: " + (pawn.mindState?.duty?.def?.defName ?? "无"));
            sb.AppendLine("DutyFocus: " + (pawn.mindState?.duty?.focus.Cell.ToString() ?? "无"));
            sb.AppendLine("Downed: " + pawn.Downed);
            sb.AppendLine("Mental: " + pawn.InMentalState);
            sb.AppendLine("CanMove: " + (pawn.health?.capacities?.CapableOf(PawnCapacityDefOf.Moving) ?? false));
            sb.AppendLine("Food: " + (pawn.needs?.food?.CurCategory.ToString() ?? "无"));
            sb.AppendLine();

            sb.AppendLine("[Session]");
            sb.AppendLine(session?.BuildDebugReport(visit, pawn) ?? "无 Session");
            sb.AppendLine();

            AppendShopDiagnostics(sb, pawn, visit, shop, remaining);
            AppendCartAndBills(sb, pawn, visit);
            AppendRecentLogLines(sb, pawn);
            return sb.ToString();
        }

        /// <summary>
        /// 写入报告头部，负责记录生成时间和游戏 Tick。
        /// </summary>
        private static void AppendHeader(StringBuilder sb, Pawn pawn)
        {
            sb.AppendLine("RimSim 顾客行为诊断");
            sb.AppendLine("生成时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("Tick: " + (Find.TickManager?.TicksGame ?? 0));
            sb.AppendLine("Map: " + (pawn?.Map?.uniqueID.ToString() ?? "无"));
            sb.AppendLine();
        }

        /// <summary>
        /// 写入商店和商品匹配诊断，负责解释顾客为什么能或不能拿到浏览 Job。
        /// </summary>
        private static void AppendShopDiagnostics(StringBuilder sb, Pawn pawn, LordJob_CustomerVisit visit, Zone_Shop shop, float remaining)
        {
            sb.AppendLine("[ShopMatch]");
            sb.AppendLine("Shop: " + (shop?.label ?? "无"));
            sb.AppendLine("ShopId: " + (shop?.ID.ToString() ?? "无"));
            sb.AppendLine("ShopOpen: " + (shop?.IsOpenNow().ToString() ?? "无"));
            if (shop == null)
            {
                sb.AppendLine();
                return;
            }

            bool hasGoods = CustomerShoppingMatchUtility.ShopHasMatchingAffordableGoods(pawn, shop, visit, remaining);
            bool hasGoodsOrServices = CustomerShoppingMatchUtility.ShopHasMatchingAffordableGoodsOrServices(pawn, shop, visit, remaining);
            sb.AppendLine("HasMatchingAffordableGoods: " + hasGoods);
            sb.AppendLine("HasMatchingAffordableGoodsOrServices: " + hasGoodsOrServices);

            List<ComboData> combos = CustomerShoppingMatchUtility.GetMatchingAffordableInStockCombos(shop, visit, remaining);
            sb.AppendLine("MatchingCombos: " + (combos?.Count ?? 0));

            List<Building_SimContainer> storages = ShopDataUtility.GetStoragesInZone(shop).ToList();
            sb.AppendLine("Storages: " + storages.Count);
            for (int i = 0; i < Math.Min(storages.Count, 12); i++)
            {
                Building_SimContainer storage = storages[i];
                sb.AppendLine("  Storage " + i + ": " + DescribeStorage(pawn, visit, storage, remaining));
            }
            sb.AppendLine();
        }

        /// <summary>
        /// 写入购物车和账单诊断，负责检查零账单或待付款状态。
        /// </summary>
        private static void AppendCartAndBills(StringBuilder sb, Pawn pawn, LordJob_CustomerVisit visit)
        {
            int pawnId = pawn.thingIDNumber;
            sb.AppendLine("[CartAndBill]");
            List<CustomerCartItem> items = visit.GetCartItems(pawnId);
            sb.AppendLine("CartItems: " + (items?.Count ?? 0));
            if (items != null)
            {
                for (int i = 0; i < Math.Min(items.Count, 20); i++)
                    sb.AppendLine("  " + items[i]?.def?.defName + " x" + items[i]?.count);
            }

            GameComponent_ShopFinanceManager finance = Current.Game?.GetComponent<GameComponent_ShopFinanceManager>();
            List<FinanceLineItem> lines = finance != null ? finance.GetPendingBillLines(pawn) : new List<FinanceLineItem>();
            sb.AppendLine("PendingBillLines: " + lines.Count);
            for (int i = 0; i < Math.Min(lines.Count, 20); i++)
                sb.AppendLine("  " + lines[i].label + " amount=" + lines[i].amount.ToString("F2"));
            sb.AppendLine();
        }

        /// <summary>
        /// 写入最近行程日志，负责提供当前顾客相关的历史事件。
        /// </summary>
        private static void AppendRecentLogLines(StringBuilder sb, Pawn pawn)
        {
            sb.AppendLine("[RecentJourneyLog]");
            try
            {
                string path = SimDebugLogger.JourneyLogPath;
                sb.AppendLine("Path: " + path);
                if (!File.Exists(path))
                {
                    sb.AppendLine("日志文件不存在。");
                    return;
                }

                string pawnKey = "pawn=" + pawn.LabelShortCap + "/" + pawn.thingIDNumber;
                List<string> lines = File.ReadLines(path, Encoding.UTF8)
                    .Where(line => line.Contains(pawnKey))
                    .Reverse()
                    .Take(MaxRecentLogLines)
                    .Reverse()
                    .ToList();
                if (lines.Count == 0)
                    sb.AppendLine("没有找到该顾客的日志行。");
                for (int i = 0; i < lines.Count; i++)
                    sb.AppendLine(lines[i]);
            }
            catch (Exception ex)
            {
                sb.AppendLine("读取日志失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 描述当前 Job 目标，负责判断顾客正在操作哪个对象。
        /// </summary>
        private static string DescribeJobTargets(Job job)
        {
            if (job == null) return "无";
            return "A=" + DescribeTarget(job.targetA)
                + " B=" + DescribeTarget(job.targetB)
                + " C=" + DescribeTarget(job.targetC)
                + " count=" + job.count;
        }

        /// <summary>
        /// 描述单个 Job 目标。
        /// </summary>
        private static string DescribeTarget(LocalTargetInfo target)
        {
            if (!target.IsValid) return "无";
            if (target.HasThing) return target.Thing.LabelShortCap + "/" + target.Thing.ThingID;
            return target.Cell.ToString();
        }

        /// <summary>
        /// 描述货柜库存匹配状态，负责检查目标商品、价格和可达性。
        /// </summary>
        private static string DescribeStorage(Pawn pawn, LordJob_CustomerVisit visit, Building_SimContainer storage, float remaining)
        {
            if (storage == null) return "无";
            bool reachable = pawn.CanReach(storage, PathEndMode.Touch, Danger.Deadly);
            bool hasAffordable = CustomerShoppingMatchUtility.StorageHasMatchingAffordableStock(storage, pawn, visit, remaining);
            string priceRejection = visit.GetPriceRejectionReason(pawn.thingIDNumber);
            List<string> items = new List<string>();
            foreach (ThingDef def in storage.ActiveDefs.Take(12))
            {
                int count = storage.CountStored(def);
                bool match = CustomerShoppingMatchUtility.ThingMatchesCustomer(visit, def);
                float price = ShopPricingUtility.GetUnitPrice(storage, def);
                CustomerPriceEvaluation evaluation = CustomerPriceUtility.Evaluate(def, price, visit.GetPriceSensitivity(pawn.thingIDNumber));
                items.Add(def.defName + " count=" + count + " match=" + match + " price=" + price.ToString("F1") + " ratio=" + evaluation.ratio.ToString("F2") + " rejected=" + evaluation.rejected);
            }

            return storage.LabelShortCap
                + " id=" + storage.thingIDNumber
                + " reachable=" + reachable
                + " hasAffordable=" + hasAffordable
                + " priceRejection=" + priceRejection
                + " items=[" + string.Join("; ", items) + "]";
        }
    }
}
