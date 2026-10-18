using System.Collections.Generic;
using RimSimRestaurantExtension.Models;
using RimSimRestaurantExtension.Tool;
using Verse;
using Verse.AI;

namespace RimSimRestaurantExtension.Jobs
{
    //恢复厨师出餐运输，职责是搬运已有成品且不重新取料制作。
    public class JobDriver_BringRestaurantMealToPass : JobDriver
    {
        //预约可回收餐品并认领运输，职责是阻止多名厨师重复搬运。
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            var order = Resolve();
            Thing target = RestaurantMealTransferUtility.PickupTarget(order, pawn);
            if (order?.state != RestaurantOrderState.ChefBringingToPass || order.cookThingId >= 0
                || !RestaurantMealTransferUtility.CanCollect(pawn, order)
                || !RestaurantPickupReservationUtility.Reserve(pawn, target, job, errorOnFailed)) return false;
            order.cookThingId = pawn.thingIDNumber;
            return true;
        }

        //执行恢复取餐和送往出餐台，职责是使用订单实物引用而不是重新生成餐品。
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => Resolve()?.IsTerminal != false);
            AddFinishAction(condition =>
            {
                var order = Resolve();
                RestaurantOrderCoordinator.ReleaseClaim(order, pawn);
                if (order?.IsTerminal == true) RestaurantMealTransferUtility.Release(order);
            });
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            var collect = ToilMaker.MakeToil("RestaurantRecoverCookedMeal");
            collect.defaultCompleteMode = ToilCompleteMode.Instant;
            collect.initAction = () =>
            {
                if (!RestaurantMealTransferUtility.Collect(pawn, Resolve())) EndJobWith(JobCondition.Incompletable);
            };
            yield return collect;
            foreach (Toil toil in RestaurantPassToils.BringToPass(this, Resolve)) yield return toil;
        }

        //恢复领域订单，职责是不依赖临时驱动字段。
        private RestaurantOrder Resolve()
        {
            return RestaurantOrderUtility.OrderManager.GetOrder(RestaurantJobUtility.GetOrderId(job));
        }
    }
}
