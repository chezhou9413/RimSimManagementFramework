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
            if (!RestaurantProductionUtility.TryProduce(cook, stove, order, job, out Thing meal, out float cost))
                return false;
            job.placedThings = null;
            order.ingredientCost = cost;
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
