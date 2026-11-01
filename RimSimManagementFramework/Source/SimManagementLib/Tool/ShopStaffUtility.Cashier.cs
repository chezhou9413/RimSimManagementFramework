using RimWorld;
using SimManagementLib.SimZone;
using Verse;

namespace SimManagementLib.Tool
{
    //排队期间放开固定人员名单，同时保留岗位本身的能力和扩展限制。
    public static partial class ShopStaffUtility
    {
        //有任一收银岗位允许该员工服务，就可以临时接待等待中的顾客。
        public static bool CanRelieveCashier(Zone_Shop shop, Pawn pawn)
        {
            var workGiver = DefDatabase<WorkGiverDef>.GetNamed("Sim_WorkGiver_ManCashRegister");
            bool hasRole = false;
            foreach (var role in GetCachedVisibleRoles(shop))
            {
                if (!RoleContainsWorkGiver(role, workGiver)) continue;
                hasRole = true;
                if (EvaluateEligibility(shop, pawn, role).Eligible
                    && role.Worker.AllowsPawnForWorkGiver(shop, pawn, workGiver)) return true;
            }
            return !hasRole;
        }
    }
}
