using RimWorld;
using SimManagementLib.SimDef;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
using Verse;

namespace SimManagementLib.Api
{
    /// <summary>
    /// 提供店员岗位和工作权限的稳定查询入口，负责包装内部岗位工具。
    /// </summary>
    public static class SimShopStaffApi
    {
        /// <summary>
        /// 返回商店当前可见的岗位列表。
        /// </summary>
        public static IReadOnlyList<ShopStaffRoleDef> GetVisibleRoles(Zone_Shop shop)
        {
            return ShopStaffUtility.GetVisibleRoles(shop);
        }

        /// <summary>
        /// 判断指定员工是否允许执行某个工作分配器。
        /// </summary>
        public static bool AllowsPawnForWorkGiver(Zone_Shop shop, Pawn pawn, WorkGiverDef workGiverDef)
        {
            return ShopStaffUtility.AllowsPawnForWorkGiver(shop, pawn, workGiverDef);
        }

        /// <summary>
        /// 判断指定员工是否已经分配给某个工作分配器对应岗位。
        /// </summary>
        public static bool IsAssignedToWorkGiver(Zone_Shop shop, Pawn pawn, WorkGiverDef workGiverDef)
        {
            return ShopStaffUtility.IsAssignedToWorkGiver(shop, pawn, workGiverDef);
        }

        /// <summary>
        /// 评估员工是否满足岗位要求。
        /// </summary>
        public static ShopStaffUtility.StaffEligibility EvaluateEligibility(Pawn pawn, ShopStaffRoleDef role)
        {
            return ShopStaffUtility.EvaluateEligibility(pawn, role);
        }
    }
}
