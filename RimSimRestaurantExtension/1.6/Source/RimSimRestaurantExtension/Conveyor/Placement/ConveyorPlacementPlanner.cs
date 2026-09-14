using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RimSimRestaurantExtension.Conveyor.Placement
{
    //规划拖拽节点方向与端点吸附，职责是保持预览无副作用并在提交前检查多入口。
    public static class ConveyorPlacementPlanner
    {
        //构造完整方向表，包含需要随新线转向的旧上游端点。
        public static Dictionary<IntVec3, Rot4> Plan(Map map, List<IntVec3> cells, Rot4 fallback)
        {
            var result = new Dictionary<IntVec3, Rot4>();
            if (cells.Count == 0) return result;
            for (int i = 0; i < cells.Count; i++)
                result[cells[i]] = i + 1 < cells.Count ? Rot4.FromIntVec3(cells[i + 1] - cells[i])
                    : i > 0 ? Rot4.FromIntVec3(cells[i] - cells[i - 1]) : fallback;
            var first = cells[0];
            var last = cells[cells.Count - 1];
            var sources = GenAdj.CardinalDirections.Select(d => ConveyorLinks.At(map, first + d))
                .Where(t => t != null && !result.ContainsKey(t.Position))
                .Where(t => t.Position + t.Rotation.FacingCell == first
                    || ConveyorLinks.At(map, t.Position + t.Rotation.FacingCell) == null && Incoming(map, t.Position) == 1).ToList();
            if (sources.Count == 1) result[sources[0].Position] = Rot4.FromIntVec3(first - sources[0].Position);
            var forward = last + result[last].FacingCell;
            if (ConveyorLinks.Output(map, forward, result, out var direction) && forward + direction.FacingCell != last)
                return result;
            var targets = GenAdj.CardinalDirections.Select(d => last + d)
                .Where(c => c != last && (c == first && cells.Count > 2
                    || !result.ContainsKey(c) && ConveyorLinks.At(map, c) != null && Incoming(map, c) == 0))
                .Where(c => ConveyorLinks.Output(map, c, result, out var rot) && c + rot.FacingCell != last).ToList();
            if (targets.Count == 1) result[last] = Rot4.FromIntVec3(targets[0] - last);
            return result;
        }

        //统计旧线路真实入口，职责是仅吸附没有上游的入口端点。
        private static int Incoming(Map map, IntVec3 cell) =>
            GenAdj.CardinalDirections.Count(d => ConveyorLinks.Connects(map, cell + d, cell));
    }
}

