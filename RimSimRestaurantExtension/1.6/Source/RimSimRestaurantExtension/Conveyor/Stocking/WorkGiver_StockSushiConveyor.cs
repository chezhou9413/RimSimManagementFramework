using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimSimRestaurantExtension.Conveyor.Transport;
using Verse;
using Verse.AI;

namespace RimSimRestaurantExtension.Conveyor.Stocking
{
    //为厨师分配线路补餐和清理工作，职责是只在真正预约时占用库存。
    public sealed class WorkGiver_StockSushiConveyor : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        //扫描本地图传送带，职责是限制岗位候选范围。
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            var manager = pawn.Map.GetComponent<MapComponent_SushiConveyor>();
            manager.Rebuild();
            return manager.lines.SelectMany(l => l.segments);
        }

        //检查候选段是否存在当前厨师能完成的低优先级工作。
        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false) =>
            thing is Building_SushiConveyor belt && ConveyorStockPlanner.Find(pawn, belt, out _) != null;

        //创建持久化任务目标，职责是把最终原子领取留给Job预约阶段。
        public override Job JobOnThing(Pawn pawn, Thing thing, bool forced = false) =>
            HasJobOnThing(pawn, thing, forced) ? JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("RSR_StockSushiConveyor"), thing) : null;
    }
}
