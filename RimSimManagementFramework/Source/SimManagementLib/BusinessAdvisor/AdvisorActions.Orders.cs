using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace SimManagementLib.BusinessAdvisor
{
    //把已批准的工作请求交给原版 Designator，让正常工作系统安排殖民者。
    internal static partial class AdvisorActions
    {
        //矩形内只收集原版允许的实际目标；多格建筑去重，不波及圈外其他对象。
        private static AdvisorPreparedStep NativeOrder(AdvisorAction a, Map map)
        {
            EnsureOrderMap(map);
            if (a.targetId.HasValue) return NativeOrderUnit(a, map);
            if (a.command == "mine_vein") throw new InvalidOperationException("全矿脉开采需要指定一个矿石 targetId；精确矩形开采请使用 mine。");
            CellRect rect = ActionRectangle(a, map);
            Designator command = AdvisorOrderCatalog.Create(a.command, a.color);
            var children = new List<AdvisorPreparedStep>();
            var seen = new HashSet<int>();
            int skipped = 0, satisfied = 0;
            var reasons = new HashSet<string>();
            foreach (IntVec3 cell in rect)
            {
                if (cell.Fogged(map)) throw new InvalidOperationException("指令范围尚未探索：" + cell);
                if (RoofOrderSatisfied(a.command, map, cell)) { satisfied++; continue; }
                var targets = cell.GetThingList(map).Where(t => a.command != "cancel" && a.command != "remove_paint" && command.CanDesignateThing(t).Accepted).ToList();
                if (targets.Count > 0)
                {
                    foreach (Thing thing in targets)
                        if (seen.Add(thing.thingIDNumber)) children.Add(Prepare(new AdvisorAction { kind = "order_target", mapId = a.mapId,
                            command = a.command, color = a.color, targetId = thing.thingIDNumber, x = thing.Position.x, z = thing.Position.z }));
                }
                else
                {
                    AcceptanceReport report = command.CanDesignateCell(cell);
                    if (report.Accepted) children.Add(Prepare(new AdvisorAction { kind = "order_cell", mapId = a.mapId,
                        command = a.command, color = a.color, x = cell.x, z = cell.z }));
                    else { skipped++; if (!string.IsNullOrEmpty(report.Reason)) reasons.Add(report.Reason); }
                }
                if (children.Count > 2500) throw new InvalidOperationException("实际目标超过 2500 个，请缩小范围分批下达。");
            }
            return Batch("order:" + a.command + ":" + map.uniqueID + ":" + rect, children,
                command.Label + " · " + rect + " · 待下达目标 " + children.Count + "；已有相同区域设置 " + satisfied + "；不适用格 " + skipped
                + (a.color == null ? "" : "；颜色 " + a.color)
                + "\n" + command.Desc + "\n" + OrderMode() + "\n不适用原因：" + string.Join("；", reasons.Take(4))
                + (IsRoofOrder(a.command) ? "\n区域标记和实际屋顶是不同状态；已有标记不会重复下达，施工进度请查 map 的 roof/buildRoofArea/noRoofArea 或使用 wait_work。" : "")
                + "\n目标：" + string.Join("；", children.Take(12).Select(c => c.resource)) + (children.Count > 12 ? "…（执行记录逐项可查）" : ""));
        }

        //每次执行重新解析固定编号或格子，状态变化会反馈给顾问重新规划。
        private static AdvisorPreparedStep NativeOrderUnit(AdvisorAction a, Map map)
        {
            EnsureOrderMap(map);
            Designator command = AdvisorOrderCatalog.Create(a.command, a.color);
            Thing thing = null;
            if (a.targetId.HasValue)
            {
                //批量固定物体优先从已知格子取，避免每个目标都扫描整张地图。
                if (a.x.HasValue && a.z.HasValue)
                {
                    var known = new IntVec3(a.x.Value, 0, a.z.Value);
                    if (known.InBounds(map)) thing = known.GetThingList(map).FirstOrDefault(t => t.thingIDNumber == a.targetId);
                }
                if (thing == null) thing = Target(map, a.targetId, false);
            }
            IntVec3 cell = thing?.Position ?? new IntVec3(AdvisorQueries.Required(a.x, "x"), 0, AdvisorQueries.Required(a.z, "z"));
            if (!cell.InBounds(map) || cell.Fogged(map)) throw new InvalidOperationException("指令目标超出地图或位于迷雾：" + cell);
            bool toggleForbidden = thing != null && thing.def.category == ThingCategory.Item
                && (a.command == "allow" || a.command == "forbid") && thing.TryGetComp<CompForbiddable>() != null;
            bool satisfied = thing == null ? RoofOrderSatisfied(a.command, map, cell)
                : toggleForbidden && thing.TryGetComp<CompForbiddable>().Forbidden == (a.command == "forbid");
            AcceptanceReport report = thing == null ? command.CanDesignateCell(cell) : command.CanDesignateThing(thing);
            if (!report.Accepted && !satisfied) throw new InvalidOperationException(command.Label + " " + cell + "：" + (report.Reason.NullOrEmpty() ? "目标已不适用或已有该指令，请重新查询。" : report.Reason));
            string target = thing == null ? cell.ToString() : (toggleForbidden ? thing.def.LabelCap : thing.LabelCap) + " (#" + thing.thingIDNumber + ")";
            //屋顶区域指令只改变区域开关；施工进展和路过物体不影响固定格子的授权。
            string state = thing == null ? (IsRoofOrder(a.command) ? "roof-area:" + a.command : CellOrderState(map, cell))
                : thing.def.defName + ":" + thing.Stuff?.defName + ":" + thing.Faction?.loadID
                + (toggleForbidden ? ":forbidden-target=" + (a.command == "forbid")
                    : ":forbidden=" + thing.IsForbidden(Faction.OfPlayer) + ":paint=" + (thing as Building)?.PaintColorDef?.defName
                        + ":" + string.Join(",", map.designationManager.AllDesignationsOn(thing).Select(d => d.def.defName).OrderBy(s => s)));
            return new AdvisorPreparedStep { resource = "order:" + a.command + ":" + map.uniqueID + ":" + (thing == null ? cell.ToString() : "thing#" + thing.thingIDNumber),
                before = "godMode=" + DebugSettings.godMode + ";" + state, alreadySatisfied = satisfied,
                preview = command.Label + " → " + target + (a.color == null ? "" : "；颜色 " + a.color)
                    + (a.command == "mine_vein" ? "\n作用于整条已探索的相连矿脉，范围可能超出标记矩形。" : "") + "\n" + OrderMode(),
                apply = () => { if (satisfied) return;
                    if (thing == null) command.DesignateSingleCell(cell); else command.DesignateThing(thing);
                    //拆屋顶在 Finalize 内清除相反区域，不能只调用 DesignateSingleCell。
                    if (a.command == "plan_add" || a.command == "plan_remove" || a.command == "remove_roof") command.Finalize(true); } };
        }

        //原版屋顶指令修改的目标是区域开关，而非瞬间生成或移除屋顶。
        private static bool IsRoofOrder(string command) => command == "build_roof" || command == "remove_roof" || command == "ignore_roof";

        //只跳过已经达到请求状态的区域；不把其他不适用原因当成成功。
        private static bool RoofOrderSatisfied(string command, Map map, IntVec3 cell)
        {
            if (command == "build_roof") return map.areaManager.BuildRoof[cell] && !map.areaManager.NoRoof[cell];
            if (command == "remove_roof") return map.areaManager.NoRoof[cell] && !map.areaManager.BuildRoof[cell];
            if (command == "ignore_roof") return !map.areaManager.BuildRoof[cell] && !map.areaManager.NoRoof[cell];
            return false;
        }

        //原版指令从当前地图取数据，不能通过临时切换地图绕过检查。
        private static void EnsureOrderMap(Map map)
        {
            if (Find.CurrentMap != map) throw new InvalidOperationException("请切换到目标地图后再下达原版指令。");
            if (TutorSystem.TutorialMode) throw new InvalidOperationException("教学模式期间请先完成原版引导，再下达顾问指令。");
        }

        //格子指令的审批记录包含现有地形、建筑和任务，不绑定路过的小人位置。
        private static string CellOrderState(Map map, IntVec3 cell) => cell.GetTerrain(map).defName + ":" + map.terrainGrid.FoundationAt(cell)?.defName
            + ":paint=" + map.terrainGrid.ColorAt(cell)?.defName + ":plan=" + map.planManager.PlanAt(cell)?.GetUniqueLoadID()
            + ":roof=" + map.roofGrid.RoofAt(cell)?.defName + ":" + string.Join(",", cell.GetThingList(map).Where(t => !(t is Pawn)).Select(t => t.thingIDNumber).OrderBy(i => i))
            + ":" + string.Join(",", map.designationManager.AllDesignationsAt(cell).Select(d => d.def.defName).OrderBy(s => s));

        //区分下达工作与完成工作，注明上帝模式沿用原版即时处理行为。
        private static string OrderMode() => DebugSettings.godMode
            ? "上帝模式：遵循原版指令行为，采矿、拆除、地面处理等可能即时完成。"
            : "按原版规则下达指令；需施工的工作由殖民者执行，受工作优先级、能力、材料和可达性影响；下达不代表完工。";
    }
}
