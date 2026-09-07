using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.Models;
using RimSimRestaurantExtension.Tool;
using SimManagementLib.Api;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimSimRestaurantExtension.WorkGivers
{
    //派发已出餐运输恢复，职责是优先清理中断成品再让厨师制作新单。
    public class WorkGiver_BringRestaurantMealToPass : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForDef(DefOfRefs.RSR_RestaurantOrderCounter);
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        //枚举出餐台，职责是仅扫描餐厅设施。
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            return pawn.Map.listerThings.ThingsOfDef(DefOfRefs.RSR_RestaurantOrderCounter);
        }

        //检查恢复工作，职责是复用完整起终点可达性筛选。
        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            return FindOrder(pawn, thing) != null;
        }

        //创建运输任务，职责是把实物取餐位置和订单编号持久化。
        public override Job JobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            var order = FindOrder(pawn, thing);
            if (order == null) return null;
            var job = JobMaker.MakeJob(DefOfRefs.RSR_BringRestaurantMealToPass,
                RestaurantMealTransferUtility.PickupTarget(order, pawn));
            RestaurantJobUtility.SetOrderId(job, order.orderId);
            job.workGiverDef = def;
            return job;
        }

        //筛选最早可恢复成品，职责是让不可达首单不阻塞其他餐品。
        private RestaurantOrder FindOrder(Pawn pawn, Thing pass)
        {
            var shop = SimShopServiceApi.FindShop(pass.Map, pass.Position);
            if (shop == null || !SimShopStaffApi.IsAssignedToWorkGiver(shop, pawn, def)
                || !pawn.CanReach(pass, PathEndMode.Touch, Danger.Some)) return null;
            return RestaurantOrderUtility.OrderManager.GetActiveOrders(shop.ID)
                .Where(order => order.providerThingId == pass.thingIDNumber
                    && order.state == RestaurantOrderState.ChefBringingToPass && order.cookThingId < 0)
                .Where(order => RestaurantMealTransferUtility.CanCollect(pawn, order))
                .OrderBy(order => order.cookedTick).FirstOrDefault();
        }
    }
}
