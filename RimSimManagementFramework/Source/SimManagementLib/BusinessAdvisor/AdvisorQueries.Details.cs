using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using SimManagementLib.GameComp;
using SimManagementLib.SimDef;
using SimManagementLib.SimMapComp;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using Verse;

namespace SimManagementLib.BusinessAdvisor
{
    //提供财务、订单、诊断和布局查询，不调用修复或刷新库存的入口。
    internal static partial class AdvisorQueries
    {
        //诊断只读取条件，不触发营业检查中的区划自动修复。
        private static IEnumerable<string> Diagnose(Zone_Shop shop)
        {
            yield return ShopRow(shop);
            int outdoors = shop.Cells.Count(c => !shop.Map.roofGrid.Roofed(c) && (c.GetRoom(shop.Map)?.PsychologicallyOutdoors ?? true));
            int roofed = shop.Cells.Count(c => c.Roofed(shop.Map));
            int roofWork = shop.Cells.Count(c => shop.Map.areaManager.BuildRoof[c] && !c.Roofed(shop.Map));
            var registers = ShopDataUtility.GetCashRegisterSnapshotInZone(shop);
            var storages = ShopDataUtility.GetStorageSnapshotInZone(shop);
            yield return "indoorInvalidCells=" + outdoors + " cashRegisters=" + registers.Count + " manned=" + registers.Count(r => r.IsManned)
                + " storages=" + storages.Count + " serviceProviders=" + ShopServiceUtility.GetServiceProvidersInZone(shop).Count
                + " scheduleOpen=" + shop.GetSchedule().IsOpenNow(shop.Map);
            yield return "roofedCells=" + roofed + " unroofedCells=" + (shop.Cells.Count - roofed) + " pendingBuildRoofCells=" + roofWork
                + "；indoorInvalidCells 是框架室内判定，不等于未盖屋顶格数。区域标记不代表屋顶已完工。";
            yield return "indoorInvalidPositions=" + string.Join(";", shop.Cells.Where(c => !c.Roofed(shop.Map)
                && (c.GetRoom(shop.Map)?.PsychologicallyOutdoors ?? true)).Take(20)) + "；最多列出20格，按坐标查询，不用猜测缺陷位置。";
            foreach (var r in registers) yield return ThingRow(r) + " cashierId=" + r.CurrentCashier?.thingIDNumber + " silver=" + r.StoredSilver;
            foreach (var s in storages) yield return ThingRow(s) + " stored=" + s.CountTotalStored() + " target=" + s.CountConfiguredTargets()
                + " sourceIssue=" + Q(s.RestockSourceIssue);
            var queue = shop.Map.GetComponent<MapComponent_RestockTaskQueue>().CreateDebugSnapshot();
            yield return "mapRestockQueue=" + ScalarFields(queue);
            yield return "waitingReasons=" + string.Join(";", queue.WaitingReasonCounts.Select(p => p.Key + ":" + p.Value));
            foreach (var r in queue.BlockedTasks.Take(12)) yield return "blockedRestock=" + ScalarFields(r);
            var sb = new StringBuilder();
            Current.Game.GetComponent<GameComponent_ShopAnalyticsManager>().AppendServiceStatistics(sb, shop.ID);
            yield return "historicalService=" + sb;
            foreach (string row in Staff(new AdvisorQuery { mapId = shop.Map.uniqueID, shopId = shop.ID })) yield return row;
        }

        //日账不依赖已裁剪的明细，K 线只返回实际开始记录后的数据。
        private static IEnumerable<string> Finance()
        {
            var f = Current.Game.GetComponent<GameComponent_ShopFinanceManager>();
            yield return "totalReceived=" + f.TotalIncome + " recordedProfit=" + f.RecordedProfit;
            foreach (var p in f.DailyRevenue.OrderByDescending(p => p.Key))
            {
                f.DailyProfit.TryGetValue(p.Key, out float profit);
                f.DailyCandles.TryGetValue(p.Key, out var candle);
                yield return "day=" + p.Key + " received=" + p.Value + " profit=" + profit + " candle=" + ScalarFields(candle);
            }
            var profits = f.ShopProfit;
            foreach (var p in f.ShopRevenue) yield return "shopId=" + p.Key + " lifetimeReceived=" + p.Value
                + " lifetimeProfit=" + (profits.TryGetValue(p.Key, out float profit) ? profit : 0);
        }

        //展开保留账单的商品、服务和套餐成本。
        private static IEnumerable<string> Bills(AdvisorQuery q) => Current.Game.GetComponent<GameComponent_ShopFinanceManager>().BillRecords
            .Where(b => !q.shopId.HasValue || b.zoneId == q.shopId).Reverse()
            .Select(b => ScalarFields(b) + " lines=[" + string.Join(";", b.lines.Select(ScalarFields)) + "]");

        //动作和备餐订单都携带真实状态及扩展业务数据。
        private static IEnumerable<string> Orders(AdvisorQuery q)
        {
            foreach (var o in Current.Game.GetComponent<GameComponent_CustomerActionOrderManager>().Orders
                .Where(o => (!q.shopId.HasValue || o.shopZoneId == q.shopId) && (!q.targetId.HasValue || o.customerThingId == q.targetId)))
                yield return "action " + ScalarFields(o) + " charges=" + string.Join(";", o.chargeLines.Select(ScalarFields));
            foreach (var o in Current.Game.GetComponent<GameComponent_PreparedShopOrderManager>().Orders
                .Where(o => (!q.shopId.HasValue || o.shopZoneId == q.shopId) && (!q.targetId.HasValue || o.customerThingId == q.targetId)))
                yield return "prepared " + ScalarFields(o);
        }

        //按店铺读取评价正文和经营相关快照。
        private static IEnumerable<string> Reviews(AdvisorQuery q) => Current.Game.GetComponent<GameComponent_CustomerReviewManager>().Records
            .Where(r => !q.shopId.HasValue || r.zoneId == q.shopId).Reverse().Select(ScalarFields);

        //读取已保存套餐，不创建空店铺套餐配置。
        private static IEnumerable<string> Combos(AdvisorQuery q)
        {
            foreach (var pair in Current.Game.GetComponent<GameComponent_ShopComboManager>().shopCombos.Where(p => !q.shopId.HasValue || p.Key == q.shopId))
                foreach (var combo in pair.Value) yield return "shopId=" + pair.Key + " " + ScalarFields(combo)
                    + " items=" + string.Join(";", combo.items.Select(ScalarFields));
        }

        //列出建造所需的尺寸、研究、成本和可用材料定义。
        private static IEnumerable<string> Definitions()
        {
            var materials = DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.IsStuff).ToList();
            foreach (var def in materials)
                yield return "stuff=" + def.defName + " " + Q(def.label) + " categories=" + string.Join(",", def.stuffProps.categories.Select(c => c.defName));
            foreach (BuildableDef def in DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.category == ThingCategory.Building && d.designationCategory != null)
                .Cast<BuildableDef>().Concat(DefDatabase<TerrainDef>.AllDefsListForReading.Where(d => d.designationCategory != null)))
                yield return "definition=" + def.defName + " " + Q(def.label) + " size=" + (def is ThingDef t ? t.size.ToString() : "1,1")
                    + " stuffRequired=" + def.MadeFromStuff + " costStuffCount=" + def.costStuffCount
                    + " rotationRequired=" + (def is ThingDef rotating && rotating.rotatable)
                    + " footprintOffsets=" + string.Join(";", Enumerable.Range(0, def is ThingDef facing && facing.rotatable ? 4 : 1)
                        .Select(r => r + ":" + GenAdj.OccupiedRect(IntVec3.Zero, new Rot4(r), def.Size)))
                    + " allowedStuffExamples=" + (def.MadeFromStuff ? string.Join(",", materials
                        .Where(s => s.stuffProps.CanMake(def)).Take(12).Select(s => s.defName)) : "none")
                    + " shopRole=" + ShopBuildingRole(def)
                    + " costs=" + string.Join(",", (def.costList ?? new List<ThingDefCountClass>()).Select(c => c.thingDef.defName + ":" + c.count))
                    + " research=" + string.Join(",", (def.researchPrerequisites ?? new List<ResearchProjectDef>()).Select(r => r.defName + ":" + r.IsFinished));
        }

        //把框架货柜与普通储物架明确区分，避免用原版 Shelf 冒充可售货设施。
        private static string ShopBuildingRole(BuildableDef def)
        {
            if (def is not ThingDef thing || thing.thingClass == null) return "none";
            if (typeof(Building_UniqueGoodsContainer).IsAssignableFrom(thing.thingClass)) return "unique_goods_container";
            if (typeof(Building_SimContainer).IsAssignableFrom(thing.thingClass)) return "goods_container";
            if (typeof(Building_CashRegister).IsAssignableFrom(thing.thingClass)) return "cash_register";
            return "none";
        }

        //返回调参和顾客规则，复杂规则可继续通过源码查询核对。
        private static IEnumerable<string> Rules()
        {
            foreach (var tuning in DefDatabase<ShopTuningDef>.AllDefsListForReading) yield return "tuning=" + tuning.defName + " " + ScalarFields(tuning);
            foreach (var kind in CustomerCatalog.Kinds) yield return "customerKind=" + ScalarFields(kind);
        }

        //读取收藏品实际剩余数量和刷新时间。
        private static IEnumerable<string> Collectibles()
        {
            var manager = Current.Game.GetComponent<GameComponent_CollectibleExchangeManager>();
            foreach (var def in DefDatabase<CollectibleExchangeListDef>.AllDefsListForReading)
                yield return "exchange=" + def.defName + " " + Q(def.label) + " " + ScalarFields(manager.GetProgress(def))
                    + " refreshTicks=" + manager.AdvisorTicksUntilRefresh(def);
        }

        //读取明确的有限矩形，规划不会扫描整张地图的所有格子。
        private static IEnumerable<string> MapCells(AdvisorQuery q)
        {
            Map map = Map(Required(q.mapId, "mapId"));
            int x = Required(q.x, "x"), z = Required(q.z, "z"), w = Required(q.width, "width"), h = Required(q.height, "height");
            if (w < 1 || h < 1 || w > 16 || h > 16) throw new InvalidOperationException("地图查询范围必须在 1~16 格之间。");
            foreach (IntVec3 cell in new CellRect(x, z, w, h))
            {
                if (!cell.InBounds(map)) { yield return cell + " outOfBounds"; continue; }
                if (cell.Fogged(map)) { yield return cell + " fogged"; continue; }
                yield return cell + " terrain=" + cell.GetTerrain(map).defName + " roof=" + map.roofGrid.RoofAt(cell)?.defName
                    + " buildRoofArea=" + map.areaManager.BuildRoof[cell] + " noRoofArea=" + map.areaManager.NoRoof[cell]
                    + " foundation=" + map.terrainGrid.FoundationAt(cell)?.defName
                    + " standable=" + cell.Standable(map) + " zoneId=" + map.zoneManager.ZoneAt(cell)?.ID
                    + " designations=" + string.Join(",", map.designationManager.AllDesignationsAt(cell).Select(d => d.def.defName))
                    + " things=" + string.Join(";", cell.GetThingList(map).Where(t => t.def.category != ThingCategory.Mote)
                        .Select(t => t.thingIDNumber + ":" + t.def.defName + ":rot" + t.Rotation.AsInt
                        + ":kind=" + (t is Blueprint ? "blueprint" : t is Frame ? "frame" : t.def.category.ToString())
                        + ":builds=" + t.def.entityDefToBuild?.defName + ":stuff=" + t.Stuff?.defName
                        + ":orders=" + string.Join(",", map.designationManager.AllDesignationsOn(t).Select(d => d.def.defName))));
            }
        }

        //用当前选择定位玩家所指的货架或店铺，不把猜测坐标用于施工。
        private static IEnumerable<string> Coordinates()
        {
            yield return "currentMapId=" + Find.CurrentMap?.uniqueID;
            foreach (Thing thing in Find.Selector.SelectedObjects.OfType<Thing>()) yield return ThingRow(thing);
            foreach (Zone_Shop shop in Find.Selector.SelectedObjects.OfType<Zone_Shop>()) yield return ShopRow(shop);
            yield return "布局请使用 map 查询确认坐标，玩家可在问题里指定地图与矩形起点。";
        }
    }
}
