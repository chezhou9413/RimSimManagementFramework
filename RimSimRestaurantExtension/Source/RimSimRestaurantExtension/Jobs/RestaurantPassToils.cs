using System;
using System.Collections.Generic;
using RimSimRestaurantExtension.Models;
using RimSimRestaurantExtension.Tool;
using Verse;
using Verse.AI;

namespace RimSimRestaurantExtension.Jobs
{
    //构造厨师送往出餐台的共同动作，职责是让首次出餐和运输恢复使用相同交接。
    internal static class RestaurantPassToils
    {
        //执行持物前往和入台，职责是关键交接失败时保留餐品供运输恢复。
        public static IEnumerable<Toil> BringToPass(JobDriver driver, Func<RestaurantOrder> resolve)
        {
            Toil target = ToilMaker.MakeToil("RestaurantPassTarget");
            target.defaultCompleteMode = ToilCompleteMode.Instant;
            target.initAction = () =>
            {
                Thing pass = RestaurantOrderUtility.FindProvider(driver.pawn.Map, resolve());
                if (pass == null) driver.EndJobWith(JobCondition.Incompletable);
                else driver.job.SetTarget(TargetIndex.C, pass);
            };
            yield return target;
            yield return Toils_Goto.GotoThing(TargetIndex.C, PathEndMode.Touch).FailOnDespawnedOrNull(TargetIndex.C);
            Toil store = ToilMaker.MakeToil("RestaurantStoreAtPass");
            store.defaultCompleteMode = ToilCompleteMode.Instant;
            store.initAction = () =>
            {
                if (!RestaurantOrderCoordinator.StoreAtPass(resolve(), driver.pawn))
                    driver.EndJobWith(JobCondition.Incompletable);
            };
            yield return store;
        }
    }
}
