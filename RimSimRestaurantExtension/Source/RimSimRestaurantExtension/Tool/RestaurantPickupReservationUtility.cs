using RimSimRestaurantExtension.Buildings;
using Verse;
using Verse.AI;

namespace RimSimRestaurantExtension.Tool
{
    //统一取餐目标的预约规则，职责是共享出餐设施并独占地面餐品。
    internal static class RestaurantPickupReservationUtility
    {
        private const int SharedFacilityUsers = 10;

        //判断目标是否为多人取餐设施，职责是区分设施使用权与物品堆叠数量。
        private static bool IsSharedFacility(Thing target)
        {
            return target is Building_RestaurantPass || target is Building_RestaurantStorage;
        }

        //检查取餐预约，职责是让派工筛选与任务实际预约采用相同人数和数量。
        public static bool CanReserve(Pawn actor, Thing target)
        {
            bool shared = IsSharedFacility(target);
            return actor.CanReserve(target, shared ? SharedFacilityUsers : 1, shared ? 0 : -1);
        }

        //预约取餐目标，职责是只预约共享设施使用人数，餐品归属由订单和实物预留管理。
        public static bool Reserve(Pawn actor, Thing target, Job job, bool errorOnFailed)
        {
            bool shared = IsSharedFacility(target);
            return actor.Reserve(target, job, shared ? SharedFacilityUsers : 1, shared ? 0 : -1,
                null, errorOnFailed);
        }
    }
}
