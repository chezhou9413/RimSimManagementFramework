using RimWorld;
using System;
using System.Collections.Generic;
using RimSimRestaurantExtension.Buildings;
using RimSimRestaurantExtension.Models;
using SimManagementLib.SimMapComp;
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
            choose.initAction = () =>
            {
                if (job.targetQueueB.NullOrEmpty()) return;
                Thing source = job.targetQueueB[0].Thing;
                if (source == null || source.Destroyed) { driver.EndJobWith(JobCondition.Incompletable); return; }
                job.count = Math.Min(job.countQueue[0], pawn.carryTracker.MaxStackSpaceEver(source.def));
                job.SetTarget(TargetIndex.C, source.ParentHolder is Building_RestaurantStorage cabinet ? cabinet.InventoryInteractionTarget : source);
            };
            yield return choose;
            yield return Toils_Jump.JumpIf(finished, () => job.targetQueueB.NullOrEmpty());
            Toil walk = ToilMaker.MakeToil("RestaurantWalkToIngredient");
            walk.defaultCompleteMode = ToilCompleteMode.PatherArrival;
            walk.initAction = () => pawn.pather.StartPath(job.GetTarget(TargetIndex.C),
                job.GetTarget(TargetIndex.C).HasThing ? PathEndMode.Touch : PathEndMode.OnCell);
            yield return walk;
            Toil extract = ToilMaker.MakeToil("RestaurantExtractIngredient");
            extract.defaultCompleteMode = ToilCompleteMode.Instant;
            extract.initAction = () =>
            {
                RestaurantOrder order = resolve();
                RestaurantOrderStock.Validate(order, pawn.Map);
                if (order.IsTerminal) { driver.EndJobWith(JobCondition.Incompletable); return; }
                Thing source = job.targetQueueB[0].Thing;
                LocalTargetInfo location = source?.ParentHolder is Building_RestaurantStorage cabinet ? cabinet.InventoryInteractionTarget : source;
                if (source?.Destroyed != false || pawn.carryTracker.CarriedThing != null
                    || !pawn.CanReachImmediate(location, location.HasThing ? PathEndMode.Touch : PathEndMode.OnCell))
                { driver.EndJobWith(JobCondition.Incompletable); return; }
                Thing taken = pawn.Map.GetComponent<MapComponent_InventoryReservations>().Extract(
                    RestaurantStockUtility.Key(resolve()), source, job.count, pawn.carryTracker.innerContainer);
                if (taken == null) { driver.EndJobWith(JobCondition.Incompletable); return; }
                job.SetTarget(TargetIndex.B, taken);
                taken.SetForbidden(true, false);
            };
            yield return extract;
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);
            Toil place = ToilMaker.MakeToil("RestaurantPlaceIngredient");
            place.defaultCompleteMode = ToilCompleteMode.Instant;
            place.initAction = () =>
            {
                Thing ingredient = pawn.carryTracker.CarriedThing;
                if (ingredient != job.GetTarget(TargetIndex.B).Thing) { driver.EndJobWith(JobCondition.Incompletable); return; }
                int count = ingredient.stackCount;
                pawn.carryTracker.innerContainer.Remove(ingredient);
                GenSpawn.Spawn(ingredient, pawn.Position, pawn.Map);
                job.placedThings.Add(new ThingCountClass(ingredient, count));
                job.countQueue[0] -= count;
                if (job.countQueue[0] <= 0) { job.countQueue.RemoveAt(0); job.targetQueueB.RemoveAt(0); }
            };
            yield return place;
            yield return Toils_Jump.Jump(choose);
            yield return finished;
        }
    }
}
