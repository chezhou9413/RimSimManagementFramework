using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.Inventory;
using SimManagementLib.SimMapComp;
using Verse;
using Verse.AI;

namespace RimSimRestaurantExtension.Conveyor.Stocking
{
    //构造补餐分批取料流程，职责是复用餐厅的货源交互与实物预约转移。
    internal static class ConveyorIngredientToils
    {
        //依次搬运所有预留原料，现货食品直接送往上架段。
        public static IEnumerable<Toil> Collect(JobDriver_StockSushiConveyor driver, Toil upload)
        {
            Pawn pawn = driver.pawn;
            Job job = driver.job;
            var ledger = pawn.Map.GetComponent<MapComponent_InventoryReservations>();
            Toil done = ToilMaker.MakeToil("SushiIngredientsDone");
            done.defaultCompleteMode = ToilCompleteMode.Instant;
            Toil choose = ToilMaker.MakeToil("SushiChooseIngredient");
            choose.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return choose;
            yield return Toils_Jump.JumpIf(done, () => job.targetQueueB.NullOrEmpty());
            Toil walk = ToilMaker.MakeToil("SushiWalkToIngredient");
            walk.defaultCompleteMode = ToilCompleteMode.PatherArrival;
            walk.initAction = () =>
            {
                var source = job.targetQueueB[0].Thing;
                if (!RestaurantIngredientTransfer.TryGetPickup(source, out var target, out var mode))
                { driver.Fail("补餐取料来源已失效"); return; }
                job.SetTarget(TargetIndex.C, target);
                pawn.pather.StartPath(target, mode);
            };
            yield return walk;
            Toil take = ToilMaker.MakeToil("SushiTakeIngredient");
            take.defaultCompleteMode = ToilCompleteMode.Instant;
            take.initAction = () =>
            {
                Thing source = job.targetQueueB[0].Thing;
                if (!RestaurantIngredientTransfer.TryGetPickup(source, out var target, out var mode)
                    || !pawn.CanReachImmediate(target, mode) || !RestaurantStockUtility.Reachable(pawn, source, driver.task.key)
                    || !ConveyorStockPlanner.IsSourceAllowed(pawn, driver.task, source))
                { driver.Fail("补餐取料时未抵达有效货源"); return; }
                int amount = System.Math.Min(job.countQueue[0], pawn.carryTracker.MaxStackSpaceEver(source.def));
                float cost = source.MarketValue * amount;
                Thing taken = ledger.Extract(driver.task.key, source, amount, pawn.carryTracker.innerContainer, out string reason);
                if (taken == null) { driver.Fail(reason); return; }
                if (driver.task.stove == null) { driver.task.cost = cost; driver.task.produced = true; }
                job.count = amount;
            };
            yield return take;
            yield return Toils_Jump.JumpIf(upload, () => driver.task.stove == null);
            Toil stove = ToilMaker.MakeToil("SushiBringIngredient");
            stove.defaultCompleteMode = ToilCompleteMode.PatherArrival;
            stove.initAction = () =>
            {
                job.SetTarget(TargetIndex.C, driver.task.stove);
                pawn.pather.StartPath(driver.task.stove, PathEndMode.InteractionCell);
            };
            yield return stove;
            Toil place = ToilMaker.MakeToil("SushiPlaceIngredient");
            place.defaultCompleteMode = ToilCompleteMode.Instant;
            place.initAction = () =>
            {
                Thing ingredient = pawn.carryTracker.CarriedThing;
                if (ingredient == null || !RestaurantIngredientPlacement.TryPlaceAt(pawn, job, ingredient, driver.task.stove))
                { driver.Fail("补餐灶台附近没有可放置食材的空格"); return; }
                job.placedThings.Add(new ThingCountClass(ingredient, job.count));
                job.countQueue[0] -= job.count;
                if (job.countQueue[0] <= 0) { job.countQueue.RemoveAt(0); job.targetQueueB.RemoveAt(0); }
            };
            yield return place;
            yield return Toils_Jump.Jump(choose);
            yield return done;
        }
    }
}
