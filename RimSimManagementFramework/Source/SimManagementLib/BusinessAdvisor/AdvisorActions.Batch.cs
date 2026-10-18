using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace SimManagementLib.BusinessAdvisor
{
    //矩形操作只压缩模型参数和审批展示，执行仍按具体格子逐项检查。
    internal static partial class AdvisorActions
    {
        //支持一行、一列或矩形；单批上限用于限制主线程准备开销。
        internal static CellRect ActionRectangle(AdvisorAction a, Map map)
        {
            int w = AdvisorQueries.Required(a.width, "width"), h = AdvisorQueries.Required(a.height, "height");
            if (w < 1 || h < 1 || w > 50 || h > 50) throw new InvalidOperationException("操作范围每边需为 1~50 格，更大范围请分批。");
            var rect = new CellRect(AdvisorQueries.Required(a.x, "x"), AdvisorQueries.Required(a.z, "z"), w, h);
            if (!rect.InBounds(map)) throw new InvalidOperationException("操作范围超出地图。");
            return rect;
        }

        //批量建造仅接受单格定义，避免尺寸重叠或自动推算门和家具的布局。
        private static AdvisorPreparedStep BuildRectangle(AdvisorAction a, Map map)
        {
            CellRect rect = ActionRectangle(a, map);
            BuildableDef def = DefDatabase<ThingDef>.GetNamedSilentFail(a.definition ?? "")
                ?? (BuildableDef)DefDatabase<TerrainDef>.GetNamedSilentFail(a.definition ?? "");
            if (def == null || def.Size.x != 1 || def.Size.z != 1) throw new InvalidOperationException("build_rect 只支持单格建筑、墙、地板或桥梁；多格家具使用 blueprint。");
            BuildRotation(def, a);
            var children = new List<AdvisorPreparedStep>();
            int existing = 0;
            foreach (IntVec3 cell in rect)
            {
                if (cell.Fogged(map)) throw new InvalidOperationException("操作范围尚未探索：" + cell);
                if (AlreadyBuilt(map, cell, def, a)) { existing++; continue; }
                children.Add(Prepare(new AdvisorAction { kind = "blueprint", mapId = a.mapId, definition = a.definition,
                    stuff = a.stuff, rotation = a.rotation, x = cell.x, z = cell.z }));
            }
            string unitCost = string.Join("，", def.CostListAdjusted(DefDatabase<ThingDef>.GetNamedSilentFail(a.stuff ?? ""))
                .Select(c => c.thingDef.LabelCap + " × " + (c.count * children.Count)));
            return Batch("build-rect:" + map.uniqueID + ":" + rect, children,
                (DebugSettings.godMode ? "直接批量建造（上帝模式）：" : "批量放置施工蓝图：") + def.LabelCap + " (" + def.defName + ")"
                + "\n范围 " + rect + "；朝向 " + a.rotation + "；材质 " + (a.stuff ?? "无")
                + "\n待放置 " + children.Count + " 格；相同成品或施工中项目跳过 " + existing + " 格"
                + (DebugSettings.godMode ? "；不消耗材料。" : "；预计材料：" + unitCost + "。指令下达后由殖民者施工。"));
        }

        //重试时识别已有同款成品、蓝图或框架，不重复下达已经成功的施工。
        private static bool AlreadyBuilt(Map map, IntVec3 cell, BuildableDef def, AdvisorAction a)
        {
            if (def is TerrainDef terrain && (terrain.isFoundation ? map.terrainGrid.FoundationAt(cell) : cell.GetTerrain(map)) == terrain) return true;
            return cell.GetThingList(map).Any(t => (t.def == def || t.def.entityDefToBuild == def)
                && (string.IsNullOrEmpty(a.stuff) ? t.Stuff == null : t.Stuff?.defName == a.stuff)
                && (def is TerrainDef || t.Rotation.AsInt == a.rotation));
        }

        //聚合完整的固定子步骤，审批时核对目标集合，执行时再逐项核对。
        private static AdvisorPreparedStep Batch(string resource, List<AdvisorPreparedStep> children, string preview)
            => new AdvisorPreparedStep { resource = resource, children = children,
                before = "godMode=" + DebugSettings.godMode + ";" + string.Join("\n", children.Select(s => s.resource + ":" + s.before)),
                preview = preview, apply = () => { } };
    }
}
