using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.Models;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimSimRestaurantExtension.Tool
{
    //执行厨房实物制作提交，职责是让订单和预制库存共用严格的原料消耗及成本记录。
    internal static class RestaurantProductionUtility
    {
        //核实完整原料后生成餐品，职责是保留真实成分并拒绝部分出餐。
        public static bool TryProduce(Pawn cook, Thing stove, RestaurantProductionRequest request,
            Job job, out Thing meal, out float cost)
        {
            meal = null; cost = 0f;
            var placedThings = job.placedThings;
            if (placedThings.NullOrEmpty() || placedThings.Any(i => i?.thing == null)
                || !RestaurantCookingUtility.CanCookOrderAt(stove, request)
                || !RestaurantCookingUtility.CanPawnCookOrderAt(cook, stove, request)) return false;
            var placed = placedThings.GroupBy(i => i.thing).Select(g => new ThingCount(g.Key, g.Sum(i => i.Count), true)).ToList();
            if (placed.Any(i => i.Thing.Destroyed || !i.Thing.Spawned || i.Thing.Map != cook.Map
                || i.Count <= 0 || i.Thing.stackCount < i.Count || i.Thing.IsBurning()
                || i.Thing.TryGetComp<CompRottable>()?.Stage > RotStage.Fresh
                || (i.Thing.Position - stove.Position).LengthHorizontalSquared > 25)) return false;
            var needs = request.GetTotalIngredientNeeds();
            if (needs.Any(n => placed.Where(i => i.Thing.def == n.ThingDef).Sum(i => i.Count) != n.countPerMeal)
                || placed.Any(i => needs.All(n => n.ThingDef != i.Thing.def))) return false;
            var actual = new List<Thing>();
            foreach (var item in placed)
            {
                cook.Map.physicalInteractionReservationManager.TryRelease(cook, job, item.Thing);
                var part = item.Thing.SplitOff(item.Count);
                if (part.Spawned) part.DeSpawn();
                actual.Add(part);
            }
            meal = RestaurantCookingUtility.MakeCookedMeal(cook, stove, request, actual);
            if (meal == null)
            {
                foreach (var part in actual) GenPlace.TryPlaceThing(part, cook.Position, cook.Map, ThingPlaceMode.Near);
                return false;
            }
            cost = actual.Sum(t => t.MarketValue * t.stackCount);
            foreach (var part in actual) part.Destroy();
            return true;
        }
    }
}
