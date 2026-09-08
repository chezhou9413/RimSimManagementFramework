using Verse;
using Verse.AI;

namespace RimSimRestaurantExtension.Inventory
{
    //放置厨房批次实物，职责是保持食材引用并避开原版满格挪动和合堆。
    internal static class RestaurantIngredientPlacement
    {
        //在灶台附近空格放下完整批次，职责是保持预留对象不变并保护制作中的实物。
        public static bool TryPlace(Pawn cook, Job job, Thing ingredient)
        {
            Thing stove = job.GetTarget(TargetIndex.A).Thing;
            if (stove?.Spawned != true || ingredient == null || ingredient.Destroyed) return false;
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(cook.Position, 5f, true))
            {
                if (!cell.InBounds(cook.Map) || (cell - stove.Position).LengthHorizontalSquared > 25
                    || cell.GetItemCount(cook.Map) != 0 || !GenSpawn.CanSpawnAt(ingredient.def, cell, cook.Map)
                    || !cook.CanReach(cell, PathEndMode.Touch, Danger.Some)) continue;
                //整批独立生成到空格，避免挤走任何已被本单或其他订单预留的食材。
                GenSpawn.Spawn(ingredient, cell, cook.Map);
                cook.Map.physicalInteractionReservationManager.Reserve(cook, job, ingredient);
                return true;
            }
            return false;
        }
    }
}
