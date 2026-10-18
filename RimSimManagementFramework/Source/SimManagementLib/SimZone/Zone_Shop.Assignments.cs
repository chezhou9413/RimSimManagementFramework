using SimManagementLib.Pojo;
using Verse;

namespace SimManagementLib.SimZone
{
    /// <summary>
    /// 提供商店岗位分配的快速查询入口，负责让 WorkGiver 权限判断避免反复构造员工列表。
    /// </summary>
    public partial class Zone_Shop
    {
        /// <summary>
        /// 判断指定岗位是否有任何可用员工，负责支持未分配时允许所有员工兜底工作的规则。
        /// </summary>
        public bool HasAssignedPawnsForRole(string roleDefName)
        {
            if (roleAssignments.NullOrEmpty() || string.IsNullOrEmpty(roleDefName))
                return false;

            NormalizeRoleAssignments();
            for (int i = 0; i < roleAssignments.Count; i++)
            {
                ShopRoleAssignment assignment = roleAssignments[i];
                if (assignment == null || assignment.roleDefName != roleDefName) continue;
                if (assignment.HasUsablePawnOn(Map))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 判断指定员工是否分配到岗位，负责避免高频工作扫描创建临时 Pawn 列表。
        /// </summary>
        public bool IsPawnAssignedToRole(string roleDefName, Pawn pawn)
        {
            if (roleAssignments.NullOrEmpty() || string.IsNullOrEmpty(roleDefName) || pawn == null)
                return false;

            NormalizeRoleAssignments();
            for (int i = 0; i < roleAssignments.Count; i++)
            {
                ShopRoleAssignment assignment = roleAssignments[i];
                if (assignment == null || assignment.roleDefName != roleDefName) continue;
                if (!assignment.HasUsablePawnOn(Map)) continue;
                if (assignment.MatchesPawn(pawn))
                    return true;
            }

            return false;
        }
    }
}
