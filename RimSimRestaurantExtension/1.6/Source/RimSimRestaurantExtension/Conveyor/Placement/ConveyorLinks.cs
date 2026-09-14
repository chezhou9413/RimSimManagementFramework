using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.Conveyor.Transport;
using UnityEngine;
using Verse;

namespace RimSimRestaurantExtension.Conveyor.Placement
{
    //统一蓝图、框架、成品和预览的有向连接，职责是拒绝对冲与多入口。
    public static class ConveyorLinks
    {
        //识别传送带定义，包括原版蓝图和施工框架。
        public static bool IsBelt(Thing thing) => thing?.def.entityDefToBuild?.defName == "RSR_SushiConveyor"
            || thing?.def.defName == "RSR_SushiConveyor";

        //读取指定格的唯一传送带节点。
        public static Thing At(Map map, IntVec3 cell) => cell.InBounds(map)
            ? cell.GetThingList(map).FirstOrDefault(IsBelt) : null;

        //取得叠加预览后的节点出口，职责是让预览与实际提交使用相同数据。
        public static bool Output(Map map, IntVec3 cell, IDictionary<IntVec3, Rot4> preview, out Rot4 rotation)
        {
            if (preview != null && preview.TryGetValue(cell, out rotation)) return true;
            var node = At(map, cell);
            rotation = node?.Rotation ?? Rot4.Invalid;
            return node != null;
        }

        //判断是否存在有效的单向相邻连接，不允许接收来自自身出口的输入。
        public static bool Connects(Map map, IntVec3 from, IntVec3 to, IDictionary<IntVec3, Rot4> preview = null) =>
            Output(map, from, preview, out var source) && Output(map, to, preview, out var target)
            && from + source.FacingCell == to && to + target.FacingCell != from;

        //计算四向连接图集位，职责是避免仅仅靠近就产生假连接。
        public static int Mask(Map map, IntVec3 cell, IDictionary<IntVec3, Rot4> preview = null)
        {
            int mask = 0;
            for (int i = 0; i < 4; i++)
            {
                var next = cell + GenAdj.CardinalDirections[i];
                if (Connects(map, cell, next, preview) || Connects(map, next, cell, preview)) mask |= 1 << i;
            }
            return mask;
        }

        //校验受影响格是否出现多个入口，职责是阻止隐式合流。
        public static bool Valid(Map map, IDictionary<IntVec3, Rot4> preview)
        {
            foreach (var cell in preview.Keys.SelectMany(c => GenAdj.CardinalDirections.Select(d => c + d).Concat(new[] { c })).Distinct())
                if (GenAdj.CardinalDirections.Count(d => Connects(map, cell + d, cell, preview)) > 1) return false;
            return true;
        }

        //查找实际运输下游，职责是限定同店、成品和单入口连接。
        public static Building_SushiConveyor Next(Building_SushiConveyor belt)
        {
            if (!belt.Spawned) return null;
            var next = At(belt.Map, belt.Position + belt.Rotation.FacingCell) as Building_SushiConveyor;
            if (next == null || !Connects(belt.Map, belt.Position, next.Position)) return null;
            return GenAdj.CardinalDirections.Count(d => Connects(belt.Map, next.Position + d, next.Position)) == 1 ? next : null;
        }

        //取得线路上游方向，职责是为弯道绘制提供进入边。
        public static Vector3 Incoming(Building_SushiConveyor belt)
        {
            foreach (var d in GenAdj.CardinalDirections)
                if (At(belt.Map, belt.Position + d) is Building_SushiConveyor prev && Next(prev) == belt) return -d.ToVector3();
            return belt.Rotation.FacingCell.ToVector3();
        }
    }
}

