using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace SimManagementLib.BusinessAdvisor
{
    //显式登记原版工作指令，不允许模型填写任意类型名或运行代码。
    internal static class AdvisorOrderCatalog
    {
        private static readonly Dictionary<string, Func<Designator>> factories = new Dictionary<string, Func<Designator>>
        {
            { "cut_plants", () => new Designator_PlantsCut() },
            { "harvest", () => new Designator_PlantsHarvest() },
            { "chop_wood", () => new Designator_PlantsHarvestWood() },
            { "mine", () => new Designator_Mine() },
            { "mine_vein", () => new Designator_MineVein() },
            { "haul", () => new Designator_Haul() },
            { "deconstruct", () => new Designator_Deconstruct() },
            { "uninstall", () => new Designator_Uninstall() },
            { "cancel", () => new Designator_Cancel() },
            { "claim", () => new Designator_Claim() },
            { "allow", () => new Designator_Unforbid() },
            { "forbid", () => new Designator_Forbid() },
            { "hunt", () => new Designator_Hunt() },
            { "tame", () => new Designator_Tame() },
            { "slaughter", () => new Designator_Slaughter() },
            { "release_animal", () => new Designator_ReleaseAnimalToWild() },
            { "adopt", () => new Designator_Adopt() },
            { "strip", () => new Designator_Strip() },
            { "open", () => new Designator_Open() },
            { "extract_tree", () => new Designator_ExtractTree() },
            { "extract_skull", () => new Designator_ExtractSkull() },
            { "eject_fuel", () => new Designator_EjectFuel() },
            { "fill_in", () => new Designator_FillIn() },
            { "smooth", () => new Designator_SmoothSurface() },
            { "smooth_wall", () => new Designator_SmoothWalls() },
            { "smooth_floor", () => new Designator_SmoothFloors() },
            { "remove_floor", () => new Designator_RemoveFloor() },
            { "remove_bridge", () => new Designator_RemoveFoundation() },
            { "remove_paint", () => new Designator_RemovePaint() },
            { "remove_floor_paint", () => new Designator_RemoveFloorPaint() },
            { "paint_building", () => new PaintBuilding(null) },
            { "paint_floor", () => new PaintFloor(null) },
            { "plan_add", () => new PlanAdd(null) },
            { "plan_remove", () => new Designator_Plan_Remove() },
            { "build_roof", () => new Designator_AreaBuildRoof() },
            { "remove_roof", () => new Designator_AreaNoRoof() },
            { "ignore_roof", () => new Designator_AreaIgnoreRoof() },
            { "home_add", () => new Designator_AreaHomeExpand() },
            { "home_remove", () => new Designator_AreaHomeClear() },
            { "snow_clear", () => new Designator_AreaSnowClearExpand() },
            { "snow_clear_remove", () => new Designator_AreaSnowClearClear() },
            { "pollution_clear", () => new Designator_AreaPollutionClearExpand() },
            { "pollution_clear_remove", () => new Designator_AreaPollutionClearClear() }
        };

        //目录同时提供用途、颜色参数和当前规则是否允许该指令。
        internal static IEnumerable<string> Rows()
        {
            foreach (var pair in factories)
            {
                Designator designator = pair.Value();
                yield return "command=" + pair.Key + " label=" + AdvisorQueries.Q(designator.Label)
                    + " available=" + Allowed(pair.Key, designator) + " colorRequired=" + NeedsColor(pair.Key)
                    + " description=" + AdvisorQueries.Q(designator.Desc);
            }
            foreach (ColorDef color in DefDatabase<ColorDef>.AllDefsListForReading.Where(c => c.colorType == ColorType.Structure || c.colorType == ColorType.Planning))
                yield return "color=" + color.defName + " type=" + color.colorType + " label=" + AdvisorQueries.Q(color.label);
        }

        //颜色绑定到方案参数，不读取玩家正在使用的油漆工具或规划颜色。
        internal static Designator Create(string command, string color)
        {
            if (!factories.TryGetValue(command ?? "", out var factory)) throw new InvalidOperationException("未知原版指令：" + command
                + (command == "clean" ? "。没有 clean 区域指令；清理污渍需家园区域（home_add）和合格人员的 Cleaning 工作，搬走石块用 haul。" : "。")
                + "可用 command 键：" + string.Join(",", factories.Keys) + "。详细条件请查询 commands。");
            Designator designator;
            if (NeedsColor(command))
            {
                ColorDef definition = DefDatabase<ColorDef>.GetNamedSilentFail(color ?? "");
                ColorType required = command == "plan_add" ? ColorType.Planning : ColorType.Structure;
                if (definition == null || definition.colorType != required) throw new InvalidOperationException("请从 commands 查询取得兼容的 color 定义。");
                designator = command == "paint_building" ? new PaintBuilding(definition)
                    : command == "paint_floor" ? (Designator)new PaintFloor(definition) : new PlanAdd(definition);
            }
            else designator = factory();
            designator.isOrder = true;
            if (!Allowed(command, designator)) throw new InvalidOperationException("当前游戏规则或 DLC 不允许此指令：" + command);
            return designator;
        }

        //只检查真实游戏规则，助手不能启用未安装的扩展。
        private static bool Allowed(string command, Designator designator) => designator.Visible
            && (Current.Game.Rules == null || Current.Game.Rules.DesignatorAllowed(designator))
            && (command != "extract_skull" || ModsConfig.IdeologyActive)
            && (command != "adopt" || ModsConfig.BiotechActive)
            && (!command.StartsWith("pollution_", StringComparison.Ordinal) || ModsConfig.BiotechActive);

        //需要额外参数的原版工具在目录中明确说明。
        private static bool NeedsColor(string command) => command == "paint_building" || command == "paint_floor" || command == "plan_add";

        //通过受保护字段设置明确的建筑颜色。
        private sealed class PaintBuilding : Designator_PaintBuilding
        {
            internal PaintBuilding(ColorDef color) { if (color != null) colorDef = color; }
        }

        //通过受保护字段设置明确的地面颜色。
        private sealed class PaintFloor : Designator_PaintFloor
        {
            internal PaintFloor(ColorDef color) { if (color != null) colorDef = color; }
        }

        //规划只采用方案指定颜色，不沿用界面选中的其他规划。
        private sealed class PlanAdd : Designator_Plan_Add
        {
            internal PlanAdd(ColorDef color) { if (color != null) colorDef = color; }
            public override void DesignateSingleCell(IntVec3 cell)
            {
                SelectedPlan = GenAdj.CardinalDirections.Select(d => cell + d).Where(c => c.InBounds(Map))
                    .Select(c => Map.planManager.PlanAt(c)).FirstOrDefault(p => p != null && p.Color == colorDef);
                base.DesignateSingleCell(cell);
            }
        }
    }
}
