using System.Collections.Generic;
using RimSimRestaurantExtension.Models;
using RimSimRestaurantExtension.Tool;
using SimManagementLib.Api;
using Verse;
using Verse.AI;

namespace RimSimRestaurantExtension.Jobs
{
    //执行整单配送，职责是从出餐台或商品柜取货并交入顾客餐位的桌面容器。
    public class JobDriver_DeliverRestaurantOrder : JobDriver
    {
        //预约起点与顾客旁站位，职责是原子认领配送且避免重复领取实物。
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            var order = Resolve();
            if (order?.state != RestaurantOrderState.ReadyToDeliver
                || !RestaurantMealTransferUtility.CanCollect(pawn, order)
                || !RestaurantPickupReservationUtility.Reserve(pawn, job.GetTarget(TargetIndex.A).Thing, job, errorOnFailed)
                || !pawn.ReserveSittableOrSpot(job.GetTarget(TargetIndex.B).Cell, job, errorOnFailed)) return false;
            order.waiterThingId = pawn.thingIDNumber;
            RestaurantOrderCoordinator.MoveTo(order, RestaurantOrderState.Delivering);
            return true;
        }

        //构造取餐与交付动作，职责是全程持有订单餐品且不把菜放在地面让顾客取。
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => Resolve()?.IsTerminal != false);
            AddFinishAction(HandleFinished);
            Toil access = ToilMaker.MakeToil("RestaurantResolvePickupAccess");
            access.defaultCompleteMode = ToilCompleteMode.Instant;
            access.initAction = () => job.SetTarget(TargetIndex.C, job.GetTarget(TargetIndex.A).Thing is Buildings.Building_RestaurantStorage cabinet
                ? cabinet.InventoryInteractionTarget : job.GetTarget(TargetIndex.A));
            yield return access;
            Toil walk = ToilMaker.MakeToil("RestaurantWalkToPickup");
            walk.defaultCompleteMode = ToilCompleteMode.PatherArrival;
            walk.initAction = () => pawn.pather.StartPath(job.GetTarget(TargetIndex.C),
                job.GetTarget(TargetIndex.C).HasThing ? PathEndMode.Touch : PathEndMode.OnCell);
            yield return walk;
            Toil collect = ToilMaker.MakeToil("RestaurantCollectFromPass");
            collect.defaultCompleteMode = ToilCompleteMode.Instant;
            collect.initAction = () =>
            {
                if (!RestaurantMealTransferUtility.Collect(pawn, Resolve())) EndJobWith(JobCondition.Incompletable);
            };
            yield return collect;
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);
            Toil present = ToilMaker.MakeToil("RestaurantPresentMeal");
            present.defaultCompleteMode = ToilCompleteMode.Delay;
            present.defaultDuration = 90;
            present.tickAction = () =>
            {
                var order = Resolve();
                Pawn customer = RestaurantOrderUtility.FindCustomer(pawn.Map, order);
                if (order?.state != RestaurantOrderState.Delivering || customer?.Position != order.seatCell)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                pawn.rotationTracker.FaceTarget(customer);
                SimShopUiApi.ReportPawnProgress(pawn, 1f - ticksLeftThisToil / 90f);
            };
            yield return present;
            Toil transfer = ToilMaker.MakeToil("RestaurantGiveMealToCustomer");
            transfer.defaultCompleteMode = ToilCompleteMode.Instant;
            transfer.initAction = () =>
            {
                if (!RestaurantOrderCoordinator.Deliver(Resolve(), pawn)) EndJobWith(JobCondition.Incompletable);
            };
            yield return transfer;
        }

        //清理中断配送，职责是将成品独立落地并释放认领以供其他服务员恢复。
        private void HandleFinished(JobCondition condition)
        {
            SimShopUiApi.ClearPawnProgress(pawn);
            if (condition == JobCondition.Succeeded) return;
            var order = Resolve();
            RestaurantMealTransferUtility.DropCarried(pawn, order);
            RestaurantOrderCoordinator.ReleaseClaim(order, pawn);
            if (order?.IsTerminal == true) RestaurantMealTransferUtility.Release(order);
        }

        //解析领域订单，职责是支持携带过程中读档。
        private RestaurantOrder Resolve()
        {
            return RestaurantOrderUtility.OrderManager.GetOrder(RestaurantJobUtility.GetOrderId(job));
        }
    }
}
