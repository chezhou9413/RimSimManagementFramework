using System.Linq;
using HarmonyLib;
using RimSimRestaurantExtension.Models;
using RimSimRestaurantExtension.Tool;
using RimWorld;
using SimManagementLib.Api;
using SimManagementLib.Tool;
using Verse;
using Verse.AI;

namespace RimSimRestaurantExtension.WorkGivers
{
    //餐厅有待制作订单时，防止强制原版账单误用订单灶台。
    internal static class RestaurantForcedCooking
    {
        //仅拦截玩家对餐厅厨师下达的原版制作命令。
        public static void Install()
        {
            new Harmony("RimSimRestaurantExtension.ForcedCooking").Patch(
                AccessTools.Method(typeof(WorkGiver_DoBill), nameof(WorkGiver_DoBill.JobOnThing)),
                prefix: new HarmonyMethod(typeof(RestaurantForcedCooking), nameof(BeforeJob)));
        }

        //保留原版账单入口，但有餐厅订单时明确提示使用订单专用工作。
        private static bool BeforeJob(Pawn pawn, Thing thing, bool forced, ref Job __result)
        {
            if (!forced || !(thing is Building_WorkTable table) || pawn?.Map == null) return true;
            var shop = SimShopServiceApi.FindShop(pawn.Map, thing.Position);
            if (shop == null || !SimShopStaffApi.IsAssignedToWorkGiver(shop, pawn, DefOfRefs.RSR_WorkGiver_CookRestaurantOrder)
                || RestaurantOrderUtility.OrderManager.GetActiveOrders(shop.ID).Any(o =>
                    (o.state == RestaurantOrderState.WaitingCook || o.state == RestaurantOrderState.Cooking)
                    && RestaurantCookingUtility.GetRecipeForOrder(o, table) != null) != true)
                return true;
            JobFailReason.Is(SimTranslation.T("RSR.Issue.UseRestaurantCooking"));
            __result = null;
            return false;
        }
    }
}
