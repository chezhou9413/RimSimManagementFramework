using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using SimManagementLib.SimZone;
using Verse;

namespace SimManagementLib.BusinessAdvisor
{
    //布局创建区域；普通模式放施工蓝图，上帝模式经审批后即时建造。
    internal static partial class AdvisorActions
    {
        //准备阶段就拒绝互相占位的蓝图或重叠区域，避免批准后才发生半途冲突。
        internal static void ValidateLayoutPlan(IEnumerable<AdvisorAction> actions)
        {
            var occupied = new HashSet<string>();
            var zones = new HashSet<string>();
            foreach (AdvisorAction a in actions)
            {
                CellRect rect;
                HashSet<string> target;
                string layer = "";
                if (a.kind == "blueprint" || a.kind == "build_rect")
                {
                    BuildableDef def = DefDatabase<ThingDef>.GetNamedSilentFail(a.definition)
                        ?? (BuildableDef)DefDatabase<TerrainDef>.GetNamedSilentFail(a.definition);
                    rect = a.kind == "build_rect" ? ActionRectangle(a, AdvisorQueries.Map(a.mapId.Value))
                        : GenAdj.OccupiedRect(new IntVec3(a.x.Value, 0, a.z.Value), new Rot4(a.rotation.Value), def.Size);
                    layer = def is TerrainDef terrain ? (terrain.isFoundation ? "foundation:" : "terrain:") : "building:";
                    target = occupied;
                }
                else if (a.kind == "create_shop") { rect = new CellRect(a.x.Value, a.z.Value, a.width.Value, a.height.Value); target = zones; }
                else continue;
                foreach (IntVec3 cell in rect)
                    if (!target.Add(a.mapId + ":" + layer + cell)) throw new InvalidOperationException("方案中的同层施工占地或商店区域互相重叠：" + cell);
            }
        }

        //创建不覆盖现有区域的矩形商店，明确提示未自动补齐的营业条件。
        private static AdvisorPreparedStep CreateShop(AdvisorAction a, Map map)
        {
            int w = AdvisorQueries.Required(a.width, "width"), h = AdvisorQueries.Required(a.height, "height");
            if (w < 1 || h < 1 || w > 50 || h > 50 || string.IsNullOrWhiteSpace(a.name) || a.name.Length > 80)
                throw new InvalidOperationException("店铺范围需为 1~50 格且具有不超过 80 字符的名称。");
            var rect = new CellRect(AdvisorQueries.Required(a.x, "x"), AdvisorQueries.Required(a.z, "z"), w, h);
            if (!rect.InBounds(map)) throw new InvalidOperationException("店铺范围超出地图。");
            foreach (IntVec3 cell in rect)
                if (cell.Fogged(map) || map.zoneManager.ZoneAt(cell) != null) throw new InvalidOperationException("店铺范围包含迷雾或已有区域：" + cell);
            return new AdvisorPreparedStep { resource = "zone:" + map.uniqueID + ":" + rect, before = "empty-zone:" + rect,
                preview = "创建店铺 " + AdvisorQueries.Q(a.name) + "，范围 " + rect + "，面积 " + rect.Area
                    + "\n仅划定店铺区域；营业仍需要屋顶、收银台、货柜或服务设施和人员。",
                apply = () => { var zone = new Zone_Shop(map.zoneManager) { label = a.name }; map.zoneManager.RegisterZone(zone);
                    foreach (IntVec3 cell in rect) zone.AddCell(cell); } };
        }

        //按当前上帝模式检查建造条件，并把实际放置方式绑定到审批状态。
        private static AdvisorPreparedStep Blueprint(AdvisorAction a, Map map)
        {
            BuildableDef def = DefDatabase<ThingDef>.GetNamedSilentFail(a.definition ?? "")
                ?? (BuildableDef)DefDatabase<TerrainDef>.GetNamedSilentFail(a.definition ?? "");
            bool instant = DebugSettings.godMode;
            if (def == null || def.designationCategory == null || def.blueprintDef == null
                || (def is ThingDef thingDef && thingDef.category != ThingCategory.Building))
                throw new InvalidOperationException("该定义不是可建造的建筑或地板。");
            //原版建造可见性依赖当前地图，因此只在同一地图上批准布局。
            if (Find.CurrentMap != map) throw new InvalidOperationException("请切换到目标地图后再生成或批准布局。");
            if (!instant && (!def.IsResearchFinished || !new Designator_Build(def).Visible))
                throw new InvalidOperationException("当前研究、难度或前置设施不允许建造此项目。");
            ThingDef stuff = string.IsNullOrEmpty(a.stuff) ? null : DefDatabase<ThingDef>.GetNamedSilentFail(a.stuff);
            if (def.MadeFromStuff && (stuff?.stuffProps == null || !stuff.stuffProps.CanMake(def)))
                throw new InvalidOperationException("建造 " + def.defName + " 的材料不兼容：stuff=" + (a.stuff ?? "未提供")
                    + "。必须填写材料 ThingDef，不能填写岩石建筑名或显示名称。可用示例："
                    + string.Join(",", DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.IsStuff && d.stuffProps.CanMake(def)).Take(20).Select(d => d.defName))
                    + "。完整材料请查询 definitions。");
            if (!def.MadeFromStuff && !string.IsNullOrEmpty(a.stuff)) throw new InvalidOperationException("此建筑不接受 stuff 材料参数。");
            int rotation = BuildRotation(def, a);
            var pos = new IntVec3(AdvisorQueries.Required(a.x, "x"), 0, AdvisorQueries.Required(a.z, "z"));
            var rot = new Rot4(rotation);
            CellRect cells = GenAdj.OccupiedRect(pos, rot, def.Size);
            if (!cells.InBounds(map)) throw new InvalidOperationException("施工范围超出地图。");
            var state = new List<string>();
            foreach (IntVec3 cell in cells)
            {
                if (cell.Fogged(map)) throw new InvalidOperationException("施工范围尚未探索。");
                List<Thing> things = cell.GetThingList(map);
                if (def is ThingDef && things.Any(t => t is Building || t is Blueprint || t is Frame))
                    throw new InvalidOperationException("施工范围已有占位物，不会自动覆盖：" + cell + "\n"
                        + string.Join(";", things.Where(t => t is Building || t is Blueprint || t is Frame).Select(t => "targetId=" + t.thingIDNumber
                            + " def=" + t.def.defName + " builds=" + t.def.entityDefToBuild?.defName + " stuff=" + t.Stuff?.defName
                            + " kind=" + (t is Blueprint ? "blueprint（取消用 command=cancel）" : t is Frame ? "frame（取消用 command=cancel）" : "building（拆除用 command=deconstruct）")))
                        + "\n先确认是否可保留利用，确需清理时分批执行，再查地图。准备阶段失败时本批未执行，不要推断部分建成。");
                //原版放置检查负责屋顶限制；普通家具上方盖好屋顶无需作废审批。
                state.Add(cell + ":terrain=" + cell.GetTerrain(map).defName + ":foundation=" + map.terrainGrid.FoundationAt(cell)?.defName);
            }
            AcceptanceReport report = GenConstruct.CanPlaceBlueprintAt(def, pos, rot, map, instant, null, null, stuff);
            if (!report.Accepted) throw new InvalidOperationException("坐标 " + pos + "：" + report.Reason);
            string cost = string.Join("，", def.CostListAdjusted(stuff).Select(c => c.thingDef.LabelCap + " × " + c.count));
            return new AdvisorPreparedStep { resource = "build:" + map.uniqueID + ":" + pos,
                before = "godMode=" + instant + ";" + string.Join(";", state),
                preview = (instant ? "直接建造（上帝模式）：" : "放置施工蓝图：") + def.LabelCap + " (" + def.defName + ")\n坐标 " + pos
                    + "，占地 " + cells + "，朝向 " + rotation + "，材质 " + (stuff?.LabelCap.ToString() ?? "无")
                    + (instant ? "\n跳过科技、难度和前置设施；材料消耗 0，无需工人施工，批准后直接生成建筑或铺设地板。"
                        : "\n材料需求：" + cost + "；需要工人施工，可能需要先搬走物品或清理植物。"),
                apply = () =>
                {
                    if (instant) new AdvisorBuildDesignator(def, stuff, rot).DesignateSingleCell(pos);
                    else GenConstruct.PlaceBlueprintForBuild(def, pos, map, rot, Faction.OfPlayer, stuff);
                } };
        }

        //没有朝向选择的墙和地板使用唯一合法方向，可旋转家具仍必须明确填写。
        private static int BuildRotation(BuildableDef def, AdvisorAction action)
        {
            bool rotatable = def is ThingDef thing && thing.rotatable;
            int rotation = action.rotation ?? (rotatable ? AdvisorQueries.Required(action.rotation, "rotation（可旋转建筑必须指定 0~3）") : 0);
            if (rotation < 0 || rotation > 3) throw new InvalidOperationException("rotation 必须为 0~3。");
            if (!rotatable && rotation != 0) throw new InvalidOperationException("该建筑或地板不允许旋转，请填 rotation=0。");
            action.rotation = rotation;
            return rotation;
        }
    }
}
