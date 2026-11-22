using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimSimRestaurantExtension.Conveyor.Transport;
using SimManagementLib.Tool;
using Verse;
using Verse.AI;

namespace RimSimRestaurantExtension.Conveyor.Stocking
{
    //为厨师分配线路补餐和清理工作，遵守作息与 24 小时排班并只在真正预约时占用库存。
    public sealed class WorkGiver_StockSushiConveyor : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        //非上班作息时段或低电机械体跳过自动补餐扫描。
        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            if (pawn.Drafted || pawn.Downed || pawn.InMentalState) return true;
            return !forced && ShopStaffUtility.IsPawnOffDutyForAutomaticWork(pawn);
        }

        //扫描本地图传送带，职责是限制岗位候选范围。
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            var manager = pawn.Map.GetComponent<MapComponent_SushiConveyor>();
            manager.Rebuild();
            return manager.lines.SelectMany(l => l.segments);
        }

        //检查候选段是否存在当前厨师能完成的传送带工作。
        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false) =>
            thing is Building_SushiConveyor belt && ConveyorStockPlanner.Find(pawn, belt, out _, forced) != null;

        //创建持久化任务目标，职责是把最终原子领取留给Job预约阶段。
        public override Job JobOnThing(Pawn pawn, Thing thing, bool forced = false) =>
            HasJobOnThing(pawn, thing, forced) ? JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("RSR_StockSushiConveyor"), thing) : null;
    }
}
