using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.Dining;
using RimSimRestaurantExtension.Inventory;
using RimSimRestaurantExtension.Models;
using RimSimRestaurantExtension.Tool;
using RimWorld;
using SimManagementLib.Api;
using Verse;
using Verse.AI;

namespace RimSimRestaurantExtension.Jobs
{
    //执行原座用餐会话，职责是按送达顺序吃喝、接收商品并等待所有追加子订单结束。
    public class JobDriver_WaitRestaurantOrderAtDiningSpot : JobDriver
    {
        private RestaurantDiningSession Session => RestaurantOrderUtility.OrderManager.SessionFor(
            RestaurantActionUtility.ResolveRestaurantOrder(RestaurantActionUtility.ResolveActionOrder(job)));
        private RestaurantOrder CurrentOrder => Session == null ? null : RestaurantOrderUtility.OrderManager.GetOrder(Session.currentOrderId);

        //预约会话独占座位，职责是按格支持双人卡座并保持中断恢复位置。
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            var session = Session;
            if (session == null || session.IsTerminal || session.customerId != pawn.thingIDNumber
                || !RestaurantSessionUtility.Validate(session, pawn.Map, out _)) return false;
            job.SetTarget(TargetIndex.B, session.seat);
            job.SetTarget(TargetIndex.C, RestaurantDiningSpotUtility.FindTableById(pawn.Map, session.tableId));
            return pawn.ReserveSittableOrSpot(session.seat, job, errorOnFailed);
        }

        //构造会话循环，职责是所有取物发生于私人桌面容器而不离开餐位。
        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddFinishAction(HandleFinished);
            Toil start = ToilMaker.MakeToil("RestaurantStartDining");
            start.defaultCompleteMode = ToilCompleteMode.Instant;
            start.initAction = () =>
            {
                var action = RestaurantActionUtility.ResolveActionOrder(job);
                if (action == null || Session?.IsTerminal != false) EndJobWith(JobCondition.Incompletable);
                else if (action.state == CustomerActionOrderState.Active) SimShopCustomerApi.StartActionOrder(action);
            };
            yield return start;
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);
            Toil wait = ToilMaker.MakeToil("RestaurantWaitAtSeat");
            wait.defaultCompleteMode = ToilCompleteMode.Never;
            wait.initAction = () => RestaurantSessionUtility.Seated(Session, pawn);
            wait.tickAction = WaitAtSeat;
            yield return wait;
            Toil finish = ToilMaker.MakeToil("RestaurantFinishDining");
            finish.defaultCompleteMode = ToilCompleteMode.Instant;
            finish.initAction = () =>
            {
                if (!RestaurantSessionSettlement.Finish(pawn, Session))
                { RestaurantSessionUtility.Abort(Session, "用餐记账或收银交接失败"); EndJobWith(JobCondition.Incompletable); }
            };
            yield return Toils_Jump.JumpIf(finish, () => Session.ReadyForCheckout);
            Toil prepare = ToilMaker.MakeToil("RestaurantPrepareTableProduct");
            prepare.defaultCompleteMode = ToilCompleteMode.Instant;
            prepare.initAction = PrepareMeal;
            yield return prepare;
            yield return Toils_Jump.JumpIf(wait, () => CurrentOrder == null || CurrentOrder.IsTerminal || CurrentOrder.mealConsumed);
            Toil accept = ToilMaker.MakeToil("RestaurantAcceptTakeAway");
            accept.defaultCompleteMode = ToilCompleteMode.Instant;
            accept.initAction = AcceptTakeAway;
            yield return Toils_Jump.JumpIf(accept, () => CurrentOrder.mode == RestaurantDeliveryMode.TakeAway);
            Toil chew = Toils_Ingest.ChewIngestible(pawn, 1f, TargetIndex.A, TargetIndex.C);
            var vanillaTick = chew.tickAction;
            chew.tickAction = () =>
            {
                if (Session.IsTerminal) { EndJobWith(JobCondition.Incompletable); return; }
                if (CurrentOrder?.IsTerminal != false) { Session.currentOrderId = -1; JumpToToil(wait); return; }
                vanillaTick?.Invoke();
            };
            yield return chew;
            Toil ingest = Toils_Ingest.FinalizeIngest(pawn, TargetIndex.A);
            var original = ingest.initAction;
            ingest.initAction = () =>
            {
                var order = CurrentOrder;
                Thing meal = order.meal;
                int before = meal.stackCount;
                original();
                int remaining = meal.Destroyed ? 0 : meal.stackCount;
                order.acceptedCount += before - remaining;
                if (meal.Destroyed)
                {
                    order.goods.Remove(meal);
                    order.meal = null;
                    order.mealThingId = -1;
                    job.SetTarget(TargetIndex.A, LocalTargetInfo.Invalid);
                }
                order.mealConsumed = order.acceptedCount >= order.mealCount;
                order.TouchProgress();
            };
            yield return ingest;
            yield return Toils_Jump.Jump(prepare);
            yield return accept;
            yield return Toils_Jump.Jump(wait);
            yield return finish;
        }

        //推进原座等待，职责是允许经理同时安排追加交谈和其他商品配送。
        private void WaitAtSeat()
        {
            var session = Session;
            if (session == null || session.IsTerminal) { EndJobWith(JobCondition.Incompletable); return; }
            if (Find.TickManager.TicksGame % 120 == 0 && !RestaurantSessionUtility.Validate(session, pawn.Map, out string reason))
            { RestaurantSessionUtility.Abort(session, reason); EndJobWith(JobCondition.Incompletable); return; }
            pawn.rotationTracker.FaceTarget(job.GetTarget(TargetIndex.C));
            if (session.ReadyForCheckout) { ReadyForNextToil(); return; }
            var next = session.Orders.Where(o => o.mealDelivered && !o.mealConsumed && !o.IsTerminal)
                .OrderBy(o => o.deliveredTick).ThenBy(o => o.orderId).FirstOrDefault();
            if (next == null) return;
            session.currentOrderId = next.orderId;
            ReadyForNextToil();
        }

        //从本人托盘或中断暂存中恢复商品，职责是不允许顾客前往地面取餐。
        private void PrepareMeal()
        {
            var order = CurrentOrder;
            if (order == null || order.mealConsumed || order.IsTerminal) return;
            Thing meal = order.goods.FirstOrDefault(t => t != null && !t.Destroyed);
            if (meal == null || !RestaurantOrderStock.Usable(order, meal))
            { RestaurantOrderUtility.FailOrder(order, "桌面商品已丢失或不能继续食用"); return; }
            order.meal = meal;
            order.mealThingId = meal.thingIDNumber;
            if (order.mode == RestaurantDeliveryMode.TakeAway) return;
            bool owned = meal.holdingOwner == Session.tray?.GetDirectlyHeldThings()
                || meal.holdingOwner == pawn.inventory.innerContainer || meal.holdingOwner == pawn.carryTracker.innerContainer;
            if (!owned || pawn.carryTracker.CarriedThing != null && pawn.carryTracker.CarriedThing != meal
                || !RestaurantMealTransferUtility.Transfer(order, pawn.carryTracker.innerContainer))
            { RestaurantOrderUtility.FailOrder(order, "本人餐品无法从托盘转到手中"); return; }
            meal.SetForbidden(false, false);
            job.SetTarget(TargetIndex.A, meal);
            job.count = meal.stackCount;
            job.ingestTotalCount = true;
        }

        //接受带走商品，职责是转移真实实物并登记付款失败回收引用。
        private void AcceptTakeAway()
        {
            var order = CurrentOrder;
            var action = RestaurantActionUtility.ResolveActionOrder(job);
            foreach (Thing thing in order.goods)
            {
                if (thing.holdingOwner != Session.tray.GetDirectlyHeldThings()) continue;
                if (!RestaurantMealTransferUtility.TransferThing(thing, pawn.inventory.innerContainer))
                    throw new System.InvalidOperationException("带走商品无法转入顾客库存");
                var result = SimShopDeliveredGoodsApi.Register(pawn, action, order.orderId.ToString(), thing, order.sourceCabinet);
                if (!result.success) throw new System.InvalidOperationException(result.failReason);
                order.acceptedCount += thing.stackCount;
                thing.SetForbidden(false, false);
            }
            order.mealConsumed = order.acceptedCount == order.mealCount;
            order.TouchProgress();
        }

        //处理任务中断，职责是把顾客手中未吃完的商品暂存到本人托盘。
        private void HandleFinished(JobCondition condition)
        {
            SimShopUiApi.ClearPawnProgress(pawn);
            var session = Session;
            if (session == null || session.IsTerminal || session.state == RestaurantSessionState.AwaitingCheckout) return;
            var order = CurrentOrder;
            if (order?.meal != null && pawn.carryTracker.CarriedThing == order.meal && session.tray?.Spawned == true)
                RestaurantMealTransferUtility.Transfer(order, session.tray.GetDirectlyHeldThings());
            if (condition == JobCondition.InterruptForced) return;
            if (condition != JobCondition.Succeeded) RestaurantSessionUtility.Abort(session, "顾客用餐中断：" + condition);
        }
    }
}
