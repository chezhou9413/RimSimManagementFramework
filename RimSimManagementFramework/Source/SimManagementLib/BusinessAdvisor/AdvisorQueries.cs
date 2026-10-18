using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using SimManagementLib.Api;
using SimManagementLib.GameComp;
using SimManagementLib.SimMapComp;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimThingComp;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using Verse;

namespace SimManagementLib.BusinessAdvisor
{
    //所有游戏查询在主线程执行，网络请求只接收分页后的文本。
    internal static partial class AdvisorQueries
    {
        //从明确的查询空间分派，未知名称不会退化为任意反射访问。
        public static string Run(AdvisorQuery q)
        {
            if (q == null || string.IsNullOrWhiteSpace(q.topic)) throw new InvalidOperationException("查询缺少 topic。");
            if (q.offset < 0) throw new InvalidOperationException("offset 不能小于 0。");
            if (q.topic.StartsWith("source_", StringComparison.Ordinal)) return AdvisorSourceQuery.Query(q);
            IEnumerable<string> rows;
            switch (q.topic)
            {
                case "overview": rows = Overview(); break;
                case "shops": rows = Shops(q).Select(ShopRow); break;
                case "diagnostics": rows = Diagnose(Shop(Required(q.mapId, "mapId"), Required(q.shopId, "shopId"))); break;
                case "inventory": rows = Inventory(q); break;
                case "unique_sources": rows = UniqueSources(q); break;
                case "shelf_goods": rows = ShelfGoods(q); break;
                case "stock": rows = Maps(q).SelectMany(m => m.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver))
                    .Where(t => !q.targetId.HasValue || t.thingIDNumber == q.targetId).Select(ThingRow); break;
                case "staff": rows = Staff(q); break;
                case "commands": rows = AdvisorOrderCatalog.Rows(); break;
                case "colonists": rows = ColonistWork(q); break;
                case "work_types": rows = DefDatabase<WorkTypeDef>.AllDefsListForReading.Select(w => "role=" + w.defName
                    + " label=" + Q(w.labelShort) + " description=" + Q(w.description)); break;
                case "customers": rows = Customers(q); break;
                case "finance": rows = Finance(); break;
                case "bills": rows = Bills(q); break;
                case "orders": rows = Orders(q); break;
                case "reviews": rows = Reviews(q); break;
                case "combos": rows = Combos(q); break;
                case "services": rows = Shops(q).SelectMany(s => ShopServiceUtility.GetServiceProvidersInZone(s)
                    .Select(p => "shopId=" + s.ID + " " + ThingRow(p) + " " + p.GetInspectString())); break;
                case "catalog": rows = GoodsCatalog.Categories.OrderBy(c => c.categoryId).SelectMany(c => c.Items
                    .Select(i => "category=" + c.categoryId + " " + c.label + " definition=" + i.thingDefName + " " + i.label + " market=" + i.baseMarketValue)); break;
                case "definitions": rows = Definitions(); break;
                case "rules": rows = Rules(); break;
                case "collectibles": rows = Collectibles(); break;
                case "transport": rows = Current.Game.GetComponent<GameComponent_ShopZoneTransportManager>().AdvisorTransportRows(); break;
                case "map": rows = MapCells(q); break;
                case "coordinates": rows = Coordinates(); break;
                default: throw new InvalidOperationException("未知查询空间：" + q.topic);
            }
            return "snapshotTick=" + Find.TickManager.TicksGame + " godMode=" + DebugSettings.godMode
                + " buildMode=" + (DebugSettings.godMode ? "instant" : "blueprint") + "\n" + Page(rows.Where(r => Match(r, q.filter)), q.offset);
        }

        //提供地图编号和日期，避免每轮把完整经营快照塞入提示前缀。
        private static IEnumerable<string> Overview()
        {
            yield return "day=" + GenDate.DaysPassed + " currentMapId=" + Find.CurrentMap?.uniqueID;
            foreach (Map map in Find.Maps) yield return "mapId=" + map.uniqueID + " name=" + Q(map.Parent.Label)
                + " size=" + map.Size + " playerHome=" + map.IsPlayerHome + " localHour=" + GenLocalDate.HourInteger(map);
            var f = Current.Game.GetComponent<GameComponent_ShopFinanceManager>();
            yield return "totalReceived=" + f.TotalIncome + " recordedProfit=" + f.RecordedProfit
                + " retainedBills=" + f.BillRecords.Count + "；账面利润只扣已登记账单成本。";
        }

        //给出营业开关及缓存指标，实际原因由专用诊断查询提供。
        private static string ShopRow(Zone_Shop shop)
        {
            var schedule = shop.GetSchedule();
            var analytics = Current.Game.GetComponent<GameComponent_ShopAnalyticsManager>();
            var m = analytics.GetOrEvaluateShopMetrics(shop);
            var index = shop.Map.GetComponent<CustomerArrivalManager>().RuntimeIndex;
            return "mapId=" + shop.Map.uniqueID + " shopId=" + shop.ID + " name=" + Q(shop.label) + " cells=" + shop.Cells.Count
                + " manualOpen=" + schedule.manualOpen + " scheduleEnabled=" + schedule.useSchedule + " hours=" + Hours(shop)
                + " scheduleOpenNow=" + schedule.IsOpenNow(shop.Map) + " activeCustomers=" + index.CountActiveForShop(shop.ID)
                + " checkoutQueue=" + index.CountPendingCheckoutForShop(shop.ID) + " cachedMetrics=" + ScalarFields(m);
        }

        //显示所有储物类型的实际库存和配置，厨房库存不再被当成零售缺货。
        private static IEnumerable<string> Inventory(AdvisorQuery q)
        {
            foreach (var storage in Maps(q).SelectMany(m => m.listerThings.AllThings.OfType<Building_SimContainer>())
                .Where(s => (!q.targetId.HasValue || q.targetId == s.thingIDNumber)
                    && (!q.shopId.HasValue || ShopStaffUtility.FindShopFor(s)?.ID == q.shopId)))
            {
                var comp = storage.GetComp<ThingComp_GoodsData>();
                yield return ThingRow(storage) + " category=" + comp?.ActiveGoodsDefName + " capacity=" + storage.MaxTotalCapacity
                    + " containerKind=" + (storage is Building_UniqueGoodsContainer ? "unique" : "bulk")
                    + " operations=" + (storage is Building_UniqueGoodsContainer ? "unique_stock,unique_price" : "shelf_category,shelf")
                    + " allowedCategoryIds=" + (storage is Building_UniqueGoodsContainer ? "not_applicable" : AllowedCategoryIds(comp))
                    + " configuredTotal=" + (comp?.itemData.Where(p => p.Value.enabled).Sum(p => p.Value.count) ?? 0)
                    + " allowsStuffOrQuality=" + ((comp?.props as ThingCompProperties_GoodsData)?.allowStuffOrQualityGoods != false)
                    + " goodsQuery=" + (storage is Building_UniqueGoodsContainer ? "unique_sources" : "shelf_goods")
                    + " stored=" + storage.CountTotalStored() + " selfPurchase=" + storage.AllowsCustomerSelfPurchase
                    + " sourceIssue=" + Q(storage.RestockSourceIssue) + " allowedCategories=" + comp?.GetAllowedGoodsCategoryLabelSummary();
                if (storage is Building_UniqueGoodsContainer unique)
                    foreach (var slot in unique.AdvisorSlots) yield return "containerId=" + storage.thingIDNumber + " slot=" + ScalarFields(slot);
                else if (comp != null)
                    foreach (var pair in comp.itemData.OrderBy(p => p.Key))
                    {
                        var def = DefDatabase<ThingDef>.GetNamedSilentFail(pair.Key);
                        yield return "containerId=" + storage.thingIDNumber + " definition=" + pair.Key + " config=" + ScalarFields(pair.Value)
                            + " stored=" + (def == null ? 0 : storage.CountStored(def));
                    }
                foreach (Thing thing in storage.GetDirectlyHeldThings()) yield return "containerId=" + storage.thingIDNumber + " held " + ThingRow(thing);
            }
        }

        //给出人员、岗位资格以及会影响补货和服务的原版工作优先级。
        private static IEnumerable<string> Staff(AdvisorQuery q)
        {
            foreach (Map map in Maps(q))
                foreach (Pawn pawn in ShopStaffUtility.GetAssignablePawns(map).Where(p => !q.targetId.HasValue || p.thingIDNumber == q.targetId))
                    yield return ThingRow(pawn) + " drafted=" + pawn.Drafted + " downed=" + pawn.Downed + " job=" + pawn.CurJobDef?.defName
                        + " work=" + WorkPriorities(pawn);
            foreach (Zone_Shop shop in Shops(q))
                foreach (var role in ShopStaffUtility.GetVisibleRoles(shop))
                    yield return "shopId=" + shop.ID + " role=" + role.defName + " " + Q(role.label)
                        + " max=" + ShopStaffUtility.GetMaxAssignedPawns(shop, role)
                        + " assigned=" + string.Join(",", shop.GetAssignedPawns(role.defName).Select(p => p.thingIDNumber))
                        + " eligible=" + string.Join(";", ShopStaffUtility.GetAssignablePawns(shop.Map).Select(p => p.thingIDNumber
                            + ":" + ShopStaffUtility.EvaluateEligibility(shop, p, role).Reason
                            + ":" + ShopStaffUtility.EvaluateEligibility(shop, p, role).Eligible));
        }

        //读取当前访问，不创建顾客 Session 或改变访问阶段。
        private static IEnumerable<string> Customers(AdvisorQuery q)
        {
            foreach (Pawn pawn in Maps(q).SelectMany(m => m.mapPawns.AllPawnsSpawned).Where(p => !q.targetId.HasValue || p.thingIDNumber == q.targetId))
            {
                var visit = SimShopCustomerApi.GetCustomerVisit(pawn);
                if (visit == null) continue;
                visit.visitSessions.TryGetValue(pawn.thingIDNumber, out var session);
                int shopId = session?.CurrentShopZoneId ?? visit.targetShopZoneId;
                if (q.shopId.HasValue && shopId != q.shopId) continue;
                var cart = visit.GetCartItems(pawn.thingIDNumber);
                yield return ThingRow(pawn) + " shopId=" + shopId + " kind=" + visit.RuntimeCustomerKind?.kindId
                    + " originalBudget=" + visit.totalBudget + " spentAcrossShops=" + session?.TotalSpentAcrossShops
                    + " effectiveBudget=" + (visit.effectiveBudgetCaps.TryGetValue(pawn.thingIDNumber, out int budget) ? budget.ToString() : "尚未计算")
                    + " owed=" + SimShopCustomerApi.GetAmountOwed(pawn) + " stage=" + session?.Stage + " lastReason=" + Q(session?.LastReason)
                    + " lastFailure=" + Q(session?.LastFailureReason) + " recoveryCount=" + session?.RecoveryCount
                    + " job=" + pawn.CurJobDef?.defName + " cart=" + (cart == null ? "empty" : string.Join(";", cart.Select(ScalarFields)));
            }
        }

        //只格式化明确传入的业务记录的标量字段，避免沿对象图读取凭据或游戏状态。
        internal static string ScalarFields(object data)
        {
            if (data == null) return "null";
            return string.Join(",", data.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
                .Where(f => f.FieldType.IsPrimitive || f.FieldType.IsEnum || f.FieldType == typeof(string) || typeof(Def).IsAssignableFrom(f.FieldType))
                .Select(f => f.Name + "=" + (f.GetValue(data) is Def def ? def.defName : Q(Convert.ToString(f.GetValue(data), System.Globalization.CultureInfo.InvariantCulture)))));
        }

        //按记录边界限制结果，下一页从实际已返回行数继续，避免无声截断。
        internal static string Page(IEnumerable<string> rows, int offset, int size = 40)
        {
            var sb = new StringBuilder();
            int count = 0;
            bool more = false;
            foreach (string row in rows.Skip(offset))
            {
                if (count >= size || (count > 0 && sb.Length + row.Length > 10000)) { more = true; break; }
                sb.AppendLine(row.Length <= 10000 ? row : row.Substring(0, 10000) + " [单条过长，按更具体对象查询]");
                count++;
            }
            return "offset=" + offset + " returned=" + count + " nextOffset=" + (more ? (offset + count).ToString() : "end") + "\n" + sb;
        }

        //匹配查询子串，不解释正则或可执行表达式。
        internal static bool Match(string text, string filter) => string.IsNullOrEmpty(filter) || (text ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        //把游戏名称作为带引号的数据输出。
        internal static string Q(string value) => CustomerReviewJsonUtility.Quote(value ?? "");
        //必要编号缺失时直接说明，不猜测目标。
        internal static int Required(int? value, string name) => value ?? throw new InvalidOperationException("缺少 " + name);
        //查找已加载地图。
        internal static Map Map(int id) => Find.Maps.FirstOrDefault(m => m.uniqueID == id) ?? throw new InvalidOperationException("地图不存在：" + id);
        //查找特定地图的店铺。
        internal static Zone_Shop Shop(int mapId, int shopId) => Map(mapId).zoneManager.AllZones.OfType<Zone_Shop>()
            .FirstOrDefault(s => s.ID == shopId) ?? throw new InvalidOperationException("店铺不存在：" + shopId);
        //按精确地图编号筛选加载中的地图。
        private static IEnumerable<Map> Maps(AdvisorQuery q) => q.mapId.HasValue ? (IEnumerable<Map>)new[] { Map(q.mapId.Value) } : Find.Maps;
        //跨地图筛选店铺。
        private static IEnumerable<Zone_Shop> Shops(AdvisorQuery q) => Maps(q).SelectMany(m => m.zoneManager.AllZones.OfType<Zone_Shop>())
            .Where(s => !q.shopId.HasValue || s.ID == q.shopId);
        //输出可稳定重新定位的物品编号和位置。
        internal static string ThingRow(Thing t) => "mapId=" + t.MapHeld?.uniqueID + " targetId=" + t.thingIDNumber + " def=" + t.def.defName
            + " name=" + Q(t.LabelCap) + " position=" + t.PositionHeld + " count=" + t.stackCount + " faction=" + t.Faction?.Name
            + " spawned=" + t.Spawned + " forbidden=" + t.IsForbidden(Faction.OfPlayer);

        //提供真实分类编号，空限制由组件自身规则解释，不让模型根据译名猜编号。
        internal static string AllowedCategoryIds(ThingComp_GoodsData comp) => comp == null ? "none"
            : string.Join(",", GoodsCatalog.Categories.Where(c => comp.AllowsGoodsCategory(c.categoryId)).Select(c => c.categoryId));
        //输出日程的固定二十四位位图。
        internal static string Hours(Zone_Shop shop) => string.Concat(shop.GetSchedule().openHours.Select(h => h ? "1" : "0"));
        //只读取已初始化的工作设置。
        internal static string WorkPriorities(Pawn pawn) => pawn.workSettings == null ? "unavailable" : string.Join(",",
            DefDatabase<WorkTypeDef>.AllDefsListForReading.Select(w => w.defName + ":" + pawn.workSettings.GetPriority(w)));
    }
}
