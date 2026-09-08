using System;
using System.Collections.Generic;
using RimSimRestaurantExtension.Models;
using RimSimRestaurantExtension.Tool;
using Verse;
using Verse.AI;
namespace RimSimRestaurantExtension.Inventory
{
    //执行冰箱和地面取料，职责是随真实搬运移动预留并保留可存档 Job 队列。
    internal static class RestaurantIngredientToils
    {
        //构造逐批取料流程，职责是让超出单次携带能力的食材分批搬到灶台。
        public static IEnumerable<Toil> Collect(JobDriver driver, Func<RestaurantOrder> resolve)
        {
            Pawn pawn = driver.pawn;
            Job job = driver.job;
            Toil choose = ToilMaker.MakeToil("RestaurantChooseIngredient");
            choose.defaultCompleteMode = ToilCompleteMode.Instant;
            Toil finished = ToilMaker.MakeToil("RestaurantIngredientsCollected");
            finished.defaultCompleteMode = ToilCompleteMode.Instant;
            Toil toStove = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);
            choose.initAction = () =>
            {
                if (job.targetQueueB.NullOrEmpty()) return;
                if (!RestaurantIngredientTransfer.Prepare(pawn, job, resolve(), out string reason))
                    Stop(driver, resolve(), reason);
            };
            yield return choose;
            yield return Toils_Jump.JumpIf(finished, () => job.targetQueueB.NullOrEmpty());
            yield return Toils_Jump.JumpIf(toStove, () => RestaurantIngredientTransfer.IsCarryingBatch(pawn, job, resolve()));
            Toil walk = ToilMaker.MakeToil("RestaurantWalkToIngredient");
            walk.defaultCompleteMode = ToilCompleteMode.PatherArrival;
            walk.initAction = () =>
            {
                if (!RestaurantIngredientTransfer.TryGetPickup(job.targetQueueB[0].Thing, out LocalTargetInfo target, out PathEndMode mode))
                { Stop(driver, resolve(), "出发时取料设施或地面物资已失效"); return; }
                job.SetTarget(TargetIndex.C, target);
                pawn.pather.StartPath(target, mode);
            };
            yield return walk;
            Toil extract = ToilMaker.MakeToil("RestaurantExtractIngredient");
            extract.defaultCompleteMode = ToilCompleteMode.Instant;
            extract.initAction = () =>
            {
                if (!RestaurantIngredientTransfer.Extract(pawn, job, resolve(), out string reason))
                    Stop(driver, resolve(), reason);
            };
            yield return extract;
            yield return toStove;
            Toil place = ToilMaker.MakeToil("RestaurantPlaceIngredient");
            place.defaultCompleteMode = ToilCompleteMode.Instant;
            place.initAction = () =>
            {
                Thing ingredient = pawn.carryTracker.CarriedThing;
                if (!RestaurantIngredientTransfer.IsCarryingBatch(pawn, job, resolve()))
                { Stop(driver, resolve(), "到达灶台时携带食材与当前批次预留不符"); return; }
                int count = ingredient.stackCount;
                if (!RestaurantIngredientPlacement.TryPlace(pawn, job, ingredient))
                { Stop(driver, resolve(), "灶台附近没有可独立放置食材的空格"); return; }
                job.placedThings.Add(new ThingCountClass(ingredient, count));
                job.countQueue[0] -= count;
                if (job.countQueue[0] <= 0) { job.countQueue.RemoveAt(0); job.targetQueueB.RemoveAt(0); }
                job.SetTarget(TargetIndex.B, LocalTargetInfo.Invalid);
                job.count = 0;
            };
            yield return place;
            yield return Toils_Jump.Jump(choose);
            yield return finished;
        }

        //记录取料中断原因，职责是让结束回调在释放认领前保留可以诊断的上下文。
        private static void Stop(JobDriver driver, RestaurantOrder order, string reason)
        {
            string detail = $"取料失败：{reason}；工作={driver.job.loadID}，位置={driver.pawn.Position}，批次数量={driver.job.count}";
            if (order != null) order.blockReason = detail;
            else RestaurantFlowLog.Failure(order, "厨师取料", detail, driver.pawn);
            driver.EndJobWith(JobCondition.Incompletable);
        }
    }
}
