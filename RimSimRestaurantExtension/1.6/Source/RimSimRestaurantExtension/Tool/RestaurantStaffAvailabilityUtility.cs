using System.Linq;
using RimSimRestaurantExtension.Models;
using RimWorld;
using SimManagementLib.Api;
using SimManagementLib.SimZone;
using Verse;
using Verse.AI;

namespace RimSimRestaurantExtension.Tool
{
    //提供餐厅岗位可用性查询，职责是统一点菜前的厨师技能、服务员路径和岗位分配检查。
    public static class RestaurantStaffAvailabilityUtility
    {
        //判断商店是否有能制作订单的厨师，职责是复用厨房对灶台、配方和技能的完整验证。
        public static bool HasCook(Zone_Shop shop, RestaurantOrder order)
        {
            return RestaurantCookingUtility.HasAvailableCook(shop, order);
        }

        //判断店内是否有岗位允许的员工，职责是避免无人承担服务员岗位时仍接受订单。
        public static bool HasStaffForWorkGiver(Zone_Shop shop, WorkGiverDef workGiverDef)
        {
            if (shop?.Map?.mapPawns == null || workGiverDef == null) return false;
            return shop.Map.mapPawns.AllPawnsSpawned.Any(pawn => IsAvailableNow(pawn)
                && SimShopStaffApi.IsAssignedToWorkGiver(shop, pawn, workGiverDef));
        }

        //判断是否存在能走到交付格的服务员，职责是避免餐桌周围封死后生成无法送达的订单。
        public static bool HasWaiterForOrder(Zone_Shop shop, RestaurantOrder order)
        {
            if (shop?.Map?.mapPawns == null || order == null || DefOfRefs.RSR_WorkGiver_DeliverRestaurantOrder == null)
                return false;
            return shop.Map.mapPawns.AllPawnsSpawned.Any(pawn => IsAvailableNow(pawn)
                && SimShopStaffApi.IsAssignedToWorkGiver(shop, pawn, DefOfRefs.RSR_WorkGiver_DeliverRestaurantOrder)
                && RestaurantOrderCreationUtility.FindOrderCounter(shop) is Thing pass
                && pawn.CanReach(pass, Verse.AI.PathEndMode.Touch, Danger.Some)
                && RestaurantDiningSpotUtility.TryFindDeliveryCell(pawn, order, out _));
        }

        //判断员工当前是否能承担新订单，职责是排除死亡、倒地、征召和精神状态中的人员。
        public static bool IsAvailableNow(Pawn pawn)
        {
            return pawn != null && pawn.Faction == Faction.OfPlayer
                && (pawn.RaceProps.Humanlike || pawn.IsColonyMech)
                && !pawn.Dead && !pawn.Downed && !pawn.Drafted && !pawn.InMentalState
                && pawn.health?.capacities?.CapableOf(PawnCapacityDefOf.Manipulation) == true;
        }
    }
}
