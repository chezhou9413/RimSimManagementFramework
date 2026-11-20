using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.Models;
using RimSimRestaurantExtension.Tool;
using SimManagementLib.Api;
using SimManagementLib.Tool;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimSimRestaurantExtension.WorkGivers
{
    //派发已出餐运输恢复，遵守作息与 24 小时排班，并在自动接单时优先清理中断成品。
    public class WorkGiver_BringRestaurantMealToPass : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForDef(DefOfRefs.RSR_RestaurantOrderCounter);
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        //非上班作息时段或低电机械体跳过自动派单。
        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            if (pawn.Drafted || pawn.Downed || pawn.InMentalState) return true;
            return !forced && ShopStaffUtility.IsPawnOffDutyForAutomaticWork(pawn);
        }

        //枚举出餐台，仅扫描餐厅设施。
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            return pawn.Map.listerThings.ThingsOfDef(DefOfRefs.RSR_RestaurantOrderCounter);
        }

        //检查恢复工作，复用完整起终点可达性筛选。
        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            return FindOrder(pawn, thing, forced) != null;
        }

        //创建运输任务，把实物取餐位置和订单编号持久化。
        public override Job JobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            var order = FindOrder(pawn, thing, forced);
            if (order == null) return null;
            var job = JobMaker.MakeJob(DefOfRefs.RSR_BringRestaurantMealToPass,
                RestaurantMealTransferUtility.PickupTarget(order, pawn));
            RestaurantJobUtility.SetOrderId(job, order.orderId);
            job.workGiverDef = def;
            return job;
        }

        //筛选最早可恢复成品，自动派工遵守排班，手动强制放宽为岗位名单即可。
        private RestaurantOrder FindOrder(Pawn pawn, Thing pass, bool forced)
        {
            var shop = SimShopServiceApi.FindShop(pass.Map, pass.Position);
            if (shop == null) return null;
            bool allowed = forced
                ? SimShopStaffApi.IsAssignedToWorkGiver(shop, pawn, def)
                : SimShopStaffApi.IsOnShiftForWorkGiver(shop, pawn, def);
            if (!allowed || !pawn.CanReach(pass, PathEndMode.Touch, Danger.Some)) return null;
            return RestaurantOrderUtility.OrderManager.GetActiveOrders(shop.ID)
                .Where(order => order.providerThingId == pass.thingIDNumber
                    && order.state == RestaurantOrderState.ChefBringingToPass && order.cookThingId < 0)
                .Where(order => RestaurantMealTransferUtility.CanCollect(pawn, order))
                .OrderBy(order => order.cookedTick).FirstOrDefault();
        }
    }
}
