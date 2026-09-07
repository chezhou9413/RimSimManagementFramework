using RimWorld;
using RimSimRestaurantExtension.Tool;
using SimManagementLib.Api;
using SimManagementLib.SimZone;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace RimSimRestaurantExtension.WorkGivers
{
    //为餐厅员工分配店内值班工作，负责让空闲厨师和服务员停留在店铺工作区域。
    public class WorkGiver_RestaurantStandby : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForDef(DefOfRefs.RSR_RestaurantOrderCounter);
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        //返回地图上的餐厅点餐台，负责作为值班工作扫描入口。
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            if (pawn?.Map == null || DefOfRefs.RSR_RestaurantOrderCounter == null)
                return Enumerable.Empty<Thing>();
            return pawn.Map.listerThings.ThingsOfDef(DefOfRefs.RSR_RestaurantOrderCounter);
        }

        //判断指定点餐台所在商店是否需要当前员工值班。
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return TryMakeStandbyJob(pawn, t, out _);
        }

        //创建餐厅员工值班 Job。
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return TryMakeStandbyJob(pawn, t, out Job job) ? job : null;
        }

        //按当前 WorkGiver 判断岗位类型并构造值班 Job。
        private bool TryMakeStandbyJob(Pawn pawn, Thing provider, out Job job)
        {
            job = null;
            if (pawn?.Map == null || provider == null || provider.Destroyed) return false;
            if (RestaurantOrderUtility.HasBlockingPriorityJobOrNeed(pawn)) return false;

            Zone_Shop shop = SimShopServiceApi.FindShop(provider.Map, provider.Position);
            if (shop == null || !RestaurantOrderUtility.CanRestaurantStaffWorkAt(shop)) return false;
            if (!SimShopStaffApi.IsAssignedToWorkGiver(shop, pawn, def)) return false;

            bool waiter = def == DefOfRefs.RSR_WorkGiver_RestaurantWaiterStandby;
            if (!RestaurantStandbyUtility.TryFindStandbyCell(pawn, shop, waiter, out IntVec3 cell, out Thing focus))
                return false;
            if (pawn.Position == cell) return false;

            JobDef jobDef = DefOfRefs.RSR_RestaurantStandby ?? DefDatabase<JobDef>.GetNamedSilentFail("RSR_RestaurantStandby");
            if (jobDef == null) return false;
            job = JobMaker.MakeJob(jobDef, cell);
            if (focus != null) job.SetTarget(TargetIndex.B, focus);
            job.expiryInterval = 420;
            job.checkOverrideOnExpire = true;
            return true;
        }
    }
}
