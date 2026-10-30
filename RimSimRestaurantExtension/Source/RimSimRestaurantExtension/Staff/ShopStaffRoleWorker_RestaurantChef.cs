using RimSimRestaurantExtension.Tool;
using SimManagementLib.SimDef;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using Verse;

namespace RimSimRestaurantExtension.Staff
{
    //让厨师岗位沿用原版烹饪能力限制，并在候选列表显示不可分配原因。
    public sealed class ShopStaffRoleWorker_RestaurantChef : ShopStaffRoleWorker
    {
        //商店服务工作不能绕过背景、健康或其他因素造成的烹饪禁用。
        public override bool CanAssignPawn(Zone_Shop zone, Pawn pawn, out string reason)
        {
            bool eligible = RestaurantCookingUtility.CanPawnCook(pawn);
            reason = eligible ? "" : SimTranslation.T("RSR.Issue.CookingUnavailable");
            return eligible;
        }
    }
}
