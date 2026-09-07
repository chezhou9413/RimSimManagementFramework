using System.Collections.Generic;
using RimSimRestaurantExtension.Models;
using RimSimRestaurantExtension.Tool;
using SimManagementLib.Api;
using Verse;
using Verse.AI;

namespace RimSimRestaurantExtension.Jobs
{
    //执行桌边点菜交谈，职责是坐定后交谈 120 Tick 并提交厨房订单。
    public class JobDriver_TakeRestaurantOrder : JobDriver
    {
        //预约交谈站位并认领顾客，职责是避免多服务员同时接待同一顾客。
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            var order = Resolve();
            return order != null
                && pawn.ReserveSittableOrSpot(job.GetTarget(TargetIndex.B).Cell, job, errorOnFailed)
                && RestaurantOrderCoordinator.ClaimReception(order, pawn);
        }

        //构造走向顾客和交谈，职责是只结束接待阶段而不结束顾客用餐会话。
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => Resolve()?.IsTerminal != false);
            AddFinishAction(condition =>
            {
                SimShopUiApi.ClearPawnProgress(pawn);
                RestaurantOrderCoordinator.ReleaseClaim(Resolve(), pawn);
            });
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);
            Toil talk = ToilMaker.MakeToil("RestaurantTakeOrder");
            talk.defaultCompleteMode = ToilCompleteMode.Delay;
            talk.defaultDuration = 120;
            talk.tickAction = () =>
            {
                var order = Resolve();
                Pawn customer = RestaurantOrderUtility.FindCustomer(pawn.Map, order);
                if (order?.state != RestaurantOrderState.TakingOrder || order.waiterThingId != pawn.thingIDNumber
                    || customer?.Position != order.seatCell || customer.CurJobDef != DefOfRefs.RSR_WaitRestaurantOrderAtDiningSpot
                    || !pawn.CanReachImmediate(customer, PathEndMode.Touch))
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                pawn.rotationTracker.FaceTarget(customer);
                customer.rotationTracker.FaceTarget(pawn);
                SimShopUiApi.ReportPawnProgress(pawn, 1f - ticksLeftThisToil / 120f);
            };
            yield return talk;
            Toil confirm = ToilMaker.MakeToil("RestaurantConfirmMenu");
            confirm.defaultCompleteMode = ToilCompleteMode.Instant;
            confirm.initAction = () =>
            {
                if (!RestaurantOrderCoordinator.ConfirmMenu(Resolve(), pawn)) EndJobWith(JobCondition.Incompletable);
            };
            yield return confirm;
        }

        //解析订单编号，职责是支持接待中存读档。
        private RestaurantOrder Resolve()
        {
            return RestaurantOrderUtility.OrderManager.GetOrder(RestaurantJobUtility.GetOrderId(job));
        }
    }
}
