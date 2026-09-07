using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.Models;
using RimSimRestaurantExtension.Tool;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimSimRestaurantExtension.Jobs
{
    //执行餐厅烹饪订单，职责是预约整单食材、搬运、制作、生成成品并在中断时安全释放认领。
    public class JobDriver_CookRestaurantOrder : JobDriver
    {
        //预约工作台与全部食材并原子认领订单，职责是防止多个厨师同时制作同一张订单。
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            RestaurantOrder order = ResolveOrder();
            Thing stove = job.GetTarget(TargetIndex.A).Thing;
            if (order == null || order.state != RestaurantOrderState.WaitingCook || !RestaurantCookingUtility.CanCookOrderAt(stove, order))
                return RejectReservation(order, "订单状态或灶台配方已失效");
            if (!pawn.Reserve(stove, job, 1, -1, null, errorOnFailed))
                return RejectReservation(order, "无法预约灶台");
            if (stove.def.hasInteractionCell && !pawn.ReserveSittableOrSpot(stove.InteractionCell, job, errorOnFailed))
                return RejectReservation(order, "无法预约灶台交互格");

            List<LocalTargetInfo> ingredients = job.GetTargetQueue(TargetIndex.B);
            if (ingredients.NullOrEmpty() || job.countQueue == null || job.countQueue.Count != ingredients.Count)
                return RejectReservation(order, "Job 食材目标与数量队列不完整");
            for (int i = 0; i < ingredients.Count; i++)
            {
                int count = job.countQueue[i];
                Thing source = ingredients[i].Thing;
                if (source == null || source.Destroyed || count <= 0 || count > source.stackCount
                    || pawn.carryTracker.MaxStackSpaceEver(source.def) <= 0)
                    return RejectReservation(order, $"第 {i + 1} 组食材无效或无法携带，数量={count}");
                if (!source.Spawned || pawn.Reserve(ingredients[i], job, 1, count, null, errorOnFailed)) continue;
                return RejectReservation(order, $"无法预约第 {i + 1} 组食材，数量={count}");
            }

            return RestaurantOrderCoordinator.ClaimCooking(order, pawn, stove)
                || RejectReservation(order, "订单暂不可认领，等待重新检查");
        }

        //处理尚未建立 Toil 的预约失败，职责是记录原因并阻止原版立即再次派发相同厨房工作。
        private bool RejectReservation(RestaurantOrder order, string reason)
        {
            RestaurantOrderUtility.OrderManager.DeferCooking(pawn, order, reason);
            RestaurantFlowLog.Failure(order, "厨师预约", reason, pawn);
            return false;
        }

        //构建取料、烹饪和出餐流程，职责是让所有临时对象都可从 Job 与订单编号恢复。
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => ResolveOrder()?.IsTerminal != false);
            AddFinishAction(HandleJobFinished);
            this.FailOnDespawnedOrNull(TargetIndex.A);

            Toil init = ToilMaker.MakeToil("RestaurantCookInit");
            init.defaultCompleteMode = ToilCompleteMode.Instant;
            init.initAction = () =>
            {
                RestaurantOrder order = ResolveOrder();
                Thing stove = job.GetTarget(TargetIndex.A).Thing;
                if (order == null || stove == null || order.state != RestaurantOrderState.Cooking || order.cookThingId != pawn.thingIDNumber)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                job.placedThings = job.placedThings ?? new List<ThingCountClass>();
                order.TouchProgress();
            };
            yield return init;

            Toil gotoStove = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);
            foreach (Toil collect in Inventory.RestaurantIngredientToils.Collect(this, ResolveOrder))
            {
                yield return collect;
            }
            yield return gotoStove;

            Toil cook = ToilMaker.MakeToil("RestaurantCookWork");
            cook.defaultCompleteMode = ToilCompleteMode.Delay;
            cook.defaultDuration = 1;
            cook.initAction = () =>
            {
                RestaurantOrder order = ResolveOrder();
                ticksLeftThisToil = RestaurantCookingUtility.GetCookTicks(pawn, job.GetTarget(TargetIndex.A).Thing, order);
            };
            cook.tickAction = () =>
            {
                RestaurantOrder order = ResolveOrder();
                Thing stove = job.GetTarget(TargetIndex.A).Thing;
                if (order == null || order.state != RestaurantOrderState.Cooking || !RestaurantCookingUtility.CanCookOrderAt(stove, order))
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                RecipeDef recipe = RestaurantCookingUtility.GetRecipeForOrder(order, stove as Building_WorkTable);
                if (recipe?.workSkill != null)
                    pawn.skills?.Learn(recipe.workSkill, 0.1f * recipe.workSkillLearnFactor);
                RestaurantCookingUtility.NotifyUsedThisTick(stove);
                if (Find.TickManager.TicksGame % 120 == 0)
                    order.TouchProgress();
            };
            cook.WithEffect(() =>
            {
                RestaurantOrder order = ResolveOrder();
                return RestaurantCookingUtility.GetRecipeForOrder(order, job.GetTarget(TargetIndex.A).Thing as Building_WorkTable)?.effectWorking;
            }, TargetIndex.A);
            cook.PlaySustainerOrSound(() =>
            {
                RestaurantOrder order = ResolveOrder();
                return RestaurantCookingUtility.GetRecipeForOrder(order, job.GetTarget(TargetIndex.A).Thing as Building_WorkTable)?.soundWorking;
            });
            cook.WithProgressBar(TargetIndex.A, () =>
            {
                RestaurantOrder order = ResolveOrder();
                int totalTicks = RestaurantCookingUtility.GetCookTicks(pawn, job.GetTarget(TargetIndex.A).Thing, order);
                return Mathf.Clamp01(1f - ticksLeftThisToil / (float)Mathf.Max(1, totalTicks));
            });
            cook.activeSkill = () => RestaurantCookingUtility
                .GetRecipeForOrder(ResolveOrder(), job.GetTarget(TargetIndex.A).Thing as Building_WorkTable)?.workSkill;
            yield return cook;

            Toil finish = ToilMaker.MakeToil("RestaurantCookFinish");
            finish.defaultCompleteMode = ToilCompleteMode.Instant;
            finish.initAction = FinishCooking;
            yield return finish;
            foreach (Toil transfer in RestaurantPassToils.BringToPass(this, ResolveOrder))
                yield return transfer;
        }

        //完成烹饪并生成成品，职责是验证订单认领、放置成品、消耗整单食材和推进待配送状态。
        private void FinishCooking()
        {
            RestaurantOrder order = ResolveOrder();
            if (RestaurantKitchenUtility.Produce(pawn, job.GetTarget(TargetIndex.A).Thing, order, job)) return;
            if (order?.mealProduced != true) RestaurantOrderUtility.FailOrder(order, "出餐前核实失败：实际食材不足或产物不可生成");
            EndJobWith(JobCondition.Incompletable);
        }

        //释放搬到灶台旁的食材，职责是在制作中断或出餐失败时撤销物理交互预约。
        private void ReleasePlacedIngredients()
        {
            if (job.placedThings == null) return;
            for (int i = 0; i < job.placedThings.Count; i++)
            {
                Thing thing = job.placedThings[i]?.thing;
                if (thing != null && !thing.Destroyed)
                {
                    pawn.Map.physicalInteractionReservationManager.TryRelease(pawn, job, thing);
                }
            }
            job.placedThings = null;
        }

        //处理 Job 结束，职责是在非成功条件下把仍由本厨师认领的订单退回待制作。
        private void HandleJobFinished(JobCondition condition)
        {
            if (condition == JobCondition.Succeeded) return;
            RestaurantOrder order = ResolveOrder();
            string reason = order?.blockReason.NullOrEmpty() != false ? condition.ToString() : order.blockReason;
            bool failed = condition == JobCondition.Incompletable || condition == JobCondition.Errored;
            //先登记重试边界，避免释放认领后被当前结束回调再次派回同一任务。
            if (failed) RestaurantOrderUtility.OrderManager.DeferCooking(pawn, order, reason);
            RestaurantFlowLog.Failure(order, "烹饪 Job 中断", reason, pawn);
            if (order?.mealProduced == true) RestaurantMealTransferUtility.DropCarried(pawn, order);
            else ReleaseCarriedIngredient();
            ReleasePlacedIngredients();
            RestaurantOrderCoordinator.ReleaseClaim(order, pawn);
            if (failed && order?.IsTerminal == false) order.blockReason = reason;
            if (order?.IsTerminal == true) RestaurantMealTransferUtility.Release(order);
        }

        //放回厨师手中尚未落地的食材，职责是避免中断后的 Pawn 长期携带原料并绕开店内库存统计。
        private void ReleaseCarriedIngredient()
        {
            Thing carried = pawn?.carryTracker?.CarriedThing;
            if (carried == null || carried != job.GetTarget(TargetIndex.B).Thing) return;
            pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _);
        }

        //按 Job 标签恢复餐厅订单，职责是避免依赖未被 Scribe 保存的 JobDriver 字段。
        private RestaurantOrder ResolveOrder()
        {
            return RestaurantOrderUtility.OrderManager?.GetOrder(RestaurantJobUtility.GetOrderId(job));
        }
    }
}
