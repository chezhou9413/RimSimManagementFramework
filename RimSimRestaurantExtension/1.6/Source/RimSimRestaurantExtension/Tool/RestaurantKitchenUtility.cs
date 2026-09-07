using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.Models;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimSimRestaurantExtension.Tool
{
    //提交厨房实际消耗，职责是在生成成品之前核实整单食材并使用真实原料生成成分。
    internal static class RestaurantKitchenUtility
    {
        //验证并制作一整单餐品，职责是拒绝缺料、重复出餐与不足载荷。
        public static bool Produce(Pawn cook, Thing stove, RestaurantOrder order, Job job)
        {
            if (order == null || order.mealProduced || order.state != RestaurantOrderState.Cooking
                || order.cookThingId != cook.thingIDNumber || cook.carryTracker.CarriedThing != null
                || cook.carryTracker.MaxStackSpaceEver(order.mealDef) < order.mealCount
                || !RestaurantCookingUtility.CanCookOrderAt(stove, order)
                || !RestaurantCookingUtility.CanPawnCookOrderAt(cook, stove, order)
                || !RestaurantOrderUtility.EnsureOrderStillValid(order, cook.Map)) return false;
            if (job.placedThings.NullOrEmpty()) return false;
            var placed = job.placedThings.GroupBy(item => item.thing)
                .Select(group => new ThingCount(group.Key, group.Sum(item => item.Count))).ToList();
            if (placed.Any(item => item.Thing == null || item.Thing.Destroyed || !item.Thing.Spawned
                || item.Thing.Map != cook.Map || item.Thing.stackCount < item.Count
                || item.Count <= 0 || item.Thing.IsBurning()
                || item.Thing.TryGetComp<CompRottable>()?.Stage > RotStage.Fresh
                || (item.Thing.Position - stove.Position).LengthHorizontalSquared > 25)) return false;
            var needs = order.GetTotalIngredientNeeds();
            if (needs.Any(need => placed.Where(item => item.Thing.def == need.ThingDef).Sum(item => item.Count) != need.countPerMeal)
                || placed.Any(item => needs.All(need => need.ThingDef != item.Thing.def))) return false;
            var actual = new List<Thing>();
            foreach (ThingCount item in placed)
            {
                cook.Map.physicalInteractionReservationManager.TryRelease(cook, job, item.Thing);
                Thing ingredient = item.Thing.SplitOff(item.Count);
                if (ingredient.Spawned) ingredient.DeSpawn();
                actual.Add(ingredient);
            }
            job.placedThings = null;
            Thing meal = RestaurantCookingUtility.MakeCookedMeal(cook, stove, order, actual);
            if (meal == null)
            {
                foreach (Thing item in actual) GenSpawn.Spawn(item, cook.Position, cook.Map);
                return false;
            }
            order.ingredientCost = actual.Sum(item => item.MarketValue * item.stackCount);
            foreach (Thing item in actual) item.Destroy(DestroyMode.Vanish);
            RestaurantOrderCoordinator.Produced(order, meal);
            if (!RestaurantMealTransferUtility.Transfer(order, cook.carryTracker.innerContainer))
            {
                GenSpawn.Spawn(meal, cook.Position, cook.Map);
                RestaurantMealTransferUtility.Protect(order);
                return false;
            }
            return true;
        }
    }
}
