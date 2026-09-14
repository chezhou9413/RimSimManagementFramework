using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.Conveyor.Placement;
using RimSimRestaurantExtension.Conveyor.Stocking;
using Verse;

namespace RimSimRestaurantExtension.Conveyor.Transport
{
    //管理地图线路拓扑与运输，职责是按主线程两阶段提交保证满环不死锁、不复制实物。
    public sealed class MapComponent_SushiConveyor : MapComponent
    {
        public List<ConveyorLine> lines = new List<ConveyorLine>();
        private readonly HashSet<Building_SushiConveyor> belts = new HashSet<Building_SushiConveyor>();
        private readonly Dictionary<Building_SushiConveyor, ConveyorLine> index = new Dictionary<Building_SushiConveyor, ConveyorLine>();
        private bool dirty = true;
        private int nextId = 1;

        //绑定地图，职责是隔离不同地图的线路与库存。
        public MapComponent_SushiConveyor(Map map) : base(map) { }

        //登记成品节点并触发拓扑更新。
        public void Register(Building_SushiConveyor belt) { belts.Add(belt); dirty = true; }

        //注销节点并触发拆分检测。
        public void Unregister(Building_SushiConveyor belt) { belts.Remove(belt); dirty = true; }

        //通知改向，职责是让铺设提交后重建连接。
        public void Dirty() { dirty = true; }

        //按建筑取得线路，职责是统一所有业务入口的拓扑版本。
        public ConveyorLine LineFor(Building_SushiConveyor belt)
        {
            Rebuild();
            return index.TryGetValue(belt, out var line) ? line : null;
        }

        //按拓扑更新连通分量，职责是让拆分继承配置、合并暂停补货。
        public void Rebuild()
        {
            if (!dirty) return;
            dirty = false;
            index.Clear();
            var old = lines.ToDictionary(l => l.id);
            var claimed = new HashSet<int>();
            var remaining = new HashSet<Building_SushiConveyor>(belts.Where(b => b.Spawned));
            var result = new List<ConveyorLine>();
            while (remaining.Count > 0)
            {
                var component = new List<Building_SushiConveyor>();
                var queue = new Queue<Building_SushiConveyor>();
                queue.Enqueue(remaining.OrderBy(b => b.thingIDNumber).First());
                while (queue.Count > 0)
                {
                    var belt = queue.Dequeue();
                    if (!remaining.Remove(belt)) continue;
                    component.Add(belt);
                    foreach (var d in GenAdj.CardinalDirections)
                        if (ConveyorLinks.At(map, belt.Position + d) is Building_SushiConveyor neighbor
                            && (ConveyorLinks.Next(belt) == neighbor || ConveyorLinks.Next(neighbor) == belt))
                            queue.Enqueue(neighbor);
                }
                var previous = component.Select(b => b.lineId).Distinct().Where(old.ContainsKey).Select(id => old[id]).OrderBy(l => l.id).ToList();
                var source = previous.FirstOrDefault();
                bool ownsAnchor = source != null && (component.Any(b => b.thingIDNumber == source.anchorId)
                    || !belts.Any(b => b.Spawned && b.thingIDNumber == source.anchorId));
                bool reuse = ownsAnchor && claimed.Add(source.id);
                var line = reuse ? source : new ConveyorLine { id = nextId++, anchorId = component.Min(b => b.thingIDNumber) };
                if (source != null && !reuse)
                {
                    line.rules = source.rules.Select(rule => rule.Clone()).ToList();
                    line.paused = true;
                    line.notice = "线路拆分，请确认上架目标后恢复补货";
                }
                if (previous.Count(l => l.rules.Count > 0) > 1)
                { line.paused = true; line.notice = "线路合并，保留较早线路配置，请确认上架目标"; }
                line.segments = OrderSegments(component);
                line.transport.Rebuild(line.segments);
                line.RefreshShop();
                if (!component.Any(b => b.thingIDNumber == line.anchorId)) line.anchorId = component.Min(b => b.thingIDNumber);
                if (line.rules.Where(x => x.enabled).Sum(x => x.target) > component.Count)
                { line.paused = true; line.notice = "目标盘数超过线路容量，请调整上架配置"; }
                foreach (var belt in component) { belt.lineId = line.id; index[belt] = line; }
                result.Add(line);
            }
            lines = result;
        }

        //按运输方向排列线路成员，职责是让阻塞限制能够从下游向上游线性传播。
        private static List<Building_SushiConveyor> OrderSegments(List<Building_SushiConveyor> component)
        {
            var downstream = new HashSet<Building_SushiConveyor>(component.Select(ConveyorLinks.Next).Where(b => b != null));
            var start = component.FirstOrDefault(b => !downstream.Contains(b)) ?? component.OrderBy(b => b.thingIDNumber).First();
            var result = new List<Building_SushiConveyor>();
            var visited = new HashSet<Building_SushiConveyor>();
            for (var current = start; current != null && visited.Add(current); current = ConveyorLinks.Next(current))
                result.Add(current);
            return result;
        }

        //更新线路动画与餐盘，职责是原版负责食品组件，本组件只负责运输。
        public override void MapComponentTick()
        {
            Rebuild();
            int now = Find.TickManager.TicksGame;
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (now % 120 == line.id % 120) MaintainLine(line);
                bool advancing = line.SameShop && line.Powered;
                ConveyorMovement.Tick(line, advancing, now);
                if (!advancing) continue;
                line.clock++;
                line.renderClock.NotifyTick();
            }
        }

        //错开执行低频线路维护，职责是刷新商店归属并释放已经失效的补餐预约。
        private void MaintainLine(ConveyorLine line)
        {
            line.RefreshShop();
            for (int i = 0; i < line.segments.Count; i++)
            {
                var belt = line.segments[i];
                if (belt.reservationKey == null) continue;
                if (belt.reservedBy?.jobs?.curDriver is JobDriver_StockSushiConveyor driver
                    && driver.task?.key == belt.reservationKey && driver.task.destination == belt) continue;
                map.GetComponent<SimManagementLib.SimMapComp.MapComponent_InventoryReservations>().Release(belt.reservationKey);
                belt.reservedBy = null; belt.reservedRuleId = null; belt.reservationKey = null;
            }
        }

        //保存线路菜单与时钟，职责是载入后从建筑重建运行期索引。
        public override void ExposeData()
        {
            Scribe_Collections.Look(ref lines, "sushiLines", LookMode.Deep);
            Scribe_Values.Look(ref nextId, "sushiNextLineId", 1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) dirty = true;
        }
    }
}
