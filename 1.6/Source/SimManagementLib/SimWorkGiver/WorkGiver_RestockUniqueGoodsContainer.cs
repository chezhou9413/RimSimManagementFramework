using RimWorld;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimWorkGiver
{
    //专业货柜补货分配器，职责是为补货员查找精确来源物品。
    public sealed class WorkGiver_RestockUniqueGoodsContainer : WorkGiver_Scanner
    {
        //枚举殖民者拥有的专业货柜。
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            List<Building> buildings = pawn?.Map?.listerBuildings?.allBuildingsColonist;
            if (buildings == null) yield break;
            for (int i = 0; i < buildings.Count; i++)
                if (buildings[i] is Building_UniqueGoodsContainer container)
                    yield return container;
        }

        //判断指定货柜是否存在当前小人可执行的精确补货任务。
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Building_UniqueGoodsContainer container)) return false;
            Zone_Shop shop = ShopStaffUtility.FindShopFor(container);
            if (!ShopStaffUtility.AllowsPawnForWorkGiver(shop, pawn, def)) return false;
            return container.TryFindPendingSource(pawn, out _, out _);
        }

        //为专业货柜创建精确补货任务。
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Building_UniqueGoodsContainer container)
                || !container.TryFindPendingSource(pawn, out _, out Thing source)) return null;
            Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("RestockUniqueGoodsContainer"), source, container);
            job.count = 1;
            job.haulMode = HaulMode.ToCellNonStorage;
            return job;
        }
    }
}
