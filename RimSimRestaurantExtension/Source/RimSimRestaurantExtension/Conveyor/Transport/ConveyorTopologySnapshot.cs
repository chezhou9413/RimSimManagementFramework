using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.Conveyor.Placement;

namespace RimSimRestaurantExtension.Conveyor.Transport
{
    //保存一次重建的有向连接快照，职责是只排序一次并避免连通分量遍历反复查询地图邻格。
    internal sealed class ConveyorTopologySnapshot
    {
        internal readonly List<Building_SushiConveyor> Belts;
        internal readonly HashSet<int> ThingIds = new HashSet<int>();
        private readonly Dictionary<Building_SushiConveyor, Building_SushiConveyor> next = new Dictionary<Building_SushiConveyor, Building_SushiConveyor>();
        private readonly Dictionary<Building_SushiConveyor, Building_SushiConveyor> previous = new Dictionary<Building_SushiConveyor, Building_SushiConveyor>();

        //采集当前成品及有效输入输出，职责是保持原有禁止合流规则和按建筑编号选择线路锚点的顺序。
        internal ConveyorTopologySnapshot(IEnumerable<Building_SushiConveyor> belts)
        {
            Belts = belts.Where(belt => belt.Spawned).OrderBy(belt => belt.thingIDNumber).ToList();
            for (int i = 0; i < Belts.Count; i++)
            {
                var belt = Belts[i];
                ThingIds.Add(belt.thingIDNumber);
                var downstream = ConveyorLinks.Next(belt);
                if (downstream == null) continue;
                next.Add(belt, downstream);
                previous.Add(downstream, belt);
            }
        }

        //取得快照中的下游，职责是让分量搜索和线路排序共用同一次连接计算。
        internal Building_SushiConveyor Next(Building_SushiConveyor belt)
        {
            return next.TryGetValue(belt, out var result) ? result : null;
        }

        //取得快照中的唯一上游，职责是让无向分量遍历只访问真实相连节点。
        internal Building_SushiConveyor Previous(Building_SushiConveyor belt)
        {
            return previous.TryGetValue(belt, out var result) ? result : null;
        }
    }
}
