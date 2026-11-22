using RimWorld;
using RimSimRestaurantExtension.Tool;
using SimManagementLib.Api;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace RimSimRestaurantExtension.WorkGivers
{
    //为餐厅员工分配店内值班工作，让空闲厨师和服务员停留在店铺工作区域。
    public class WorkGiver_RestaurantStandby : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForDef(DefOfRefs.RSR_RestaurantOrderCounter);
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        //自动派工遵守作息与机械体状态，手动右键强制不拦截。
        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            if (pawn.Drafted || pawn.Downed || pawn.InMentalState) return true;
            return !forced && ShopStaffUtility.IsPawnOffDutyForAutomaticWork(pawn);
        }

        //返回地图上的餐厅点餐台作为值班工作扫描入口。
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            if (pawn?.Map == null || DefOfRefs.RSR_RestaurantOrderCounter == null)
                return Enumerable.Empty<Thing>();
            return pawn.Map.listerThings.ThingsOfDef(DefOfRefs.RSR_RestaurantOrderCounter);
        }

        //判断指定点餐台所在商店是否需要当前员工值班。
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return TryMakeStandbyJob(pawn, t, forced, out _);
        }

        //创建餐厅员工值班 Job。
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return TryMakeStandbyJob(pawn, t, forced, out Job job) ? job : null;
        }

        //按当前 WorkGiver 判断岗位类型并构造值班 Job，手动强制允许忽略排班小时。
        private bool TryMakeStandbyJob(Pawn pawn, Thing provider, bool forced, out Job job)
        {
            job = null;
            if (pawn?.Map == null || provider == null || provider.Destroyed) return false;
            if (!forced && RestaurantOrderUtility.HasBlockingPriorityJobOrNeed(pawn)) return false;

            Zone_Shop shop = SimShopServiceApi.FindShop(provider.Map, provider.Position);
            if (shop == null || (!forced && !RestaurantOrderUtility.CanRestaurantStaffWorkAt(shop))
                || !RestaurantOrderUtility.Settings.GetOrCreate(shop.ID).enabled) return false;
            bool allowed = forced
                ? SimShopStaffApi.IsAssignedToWorkGiver(shop, pawn, def)
                : SimShopStaffApi.IsOnShiftForWorkGiver(shop, pawn, def);
            if (!allowed) return false;

            bool waiter = def == DefOfRefs.RSR_WorkGiver_RestaurantWaiterStandby;
            if (!RestaurantStandbyUtility.TryFindStandbyCell(pawn, shop, waiter, out IntVec3 cell, out Thing focus))
                return false;
            //已到岗仍需生成值班工作，否则工作树会继续选择店外任务。
            job = JobMaker.MakeJob(DefOfRefs.RSR_RestaurantStandby, cell);
            if (focus != null) job.SetTarget(TargetIndex.B, focus);
            job.SetTarget(TargetIndex.C, provider);
            job.workGiverDef = def;
            return true;
        }
    }
}
