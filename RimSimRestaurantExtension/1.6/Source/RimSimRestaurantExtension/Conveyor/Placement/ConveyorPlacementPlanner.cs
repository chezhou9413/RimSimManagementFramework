using System.Collections.Generic;
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
            FillPlan(map, cells, fallback, result);
            return result;
        }

        //复用调用方方向表，职责是让连续预览保持实时规划而不反复分配路径字典。
        public static void FillPlan(Map map, List<IntVec3> cells, Rot4 fallback, Dictionary<IntVec3, Rot4> result)
        {
            result.Clear();
            if (cells.Count == 0) return;
            for (int i = 0; i < cells.Count; i++)
                result[cells[i]] = i + 1 < cells.Count ? Rot4.FromIntVec3(cells[i + 1] - cells[i])
                    : i > 0 ? Rot4.FromIntVec3(cells[i] - cells[i - 1]) : fallback;
            var first = cells[0];
            var last = cells[cells.Count - 1];
            int sourceCount = 0;
            Thing source = null;
            for (int i = 0; i < 4; i++)
            {
                var candidate = ConveyorLinks.At(map, first + GenAdj.CardinalDirections[i]);
                if (candidate == null || result.ContainsKey(candidate.Position)) continue;
                var output = candidate.Position + candidate.Rotation.FacingCell;
                if (output != first && (ConveyorLinks.At(map, output) != null || Incoming(map, candidate.Position) != 1)) continue;
                source = candidate;
                sourceCount++;
            }
            if (sourceCount == 1) result[source.Position] = Rot4.FromIntVec3(first - source.Position);
            var forward = last + result[last].FacingCell;
            if (ConveyorLinks.Output(map, forward, result, out var direction) && forward + direction.FacingCell != last)
                return;
            int targetCount = 0;
            IntVec3 target = IntVec3.Invalid;
            for (int i = 0; i < 4; i++)
            {
                var candidate = last + GenAdj.CardinalDirections[i];
                bool closesLoop = candidate == first && cells.Count > 2;
                if (!closesLoop && (result.ContainsKey(candidate) || ConveyorLinks.At(map, candidate) == null || Incoming(map, candidate) != 0)) continue;
                if (!ConveyorLinks.Output(map, candidate, result, out var rot) || candidate + rot.FacingCell == last) continue;
                target = candidate;
                targetCount++;
            }
            if (targetCount == 1) result[last] = Rot4.FromIntVec3(target - last);
        }

        //统计旧线路真实入口，职责是仅吸附没有上游的入口端点。
        private static int Incoming(Map map, IntVec3 cell)
        {
            int count = 0;
            for (int i = 0; i < 4; i++)
                if (ConveyorLinks.Connects(map, cell + GenAdj.CardinalDirections[i], cell)) count++;
            return count;
        }
    }
}
