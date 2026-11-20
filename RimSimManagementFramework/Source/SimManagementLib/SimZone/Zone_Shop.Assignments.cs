using SimManagementLib.Pojo;
using Verse;

namespace SimManagementLib.SimZone
{
    //提供商店岗位分配的快速只读查询入口，避免高频工作扫描修改快照或分配字符串。
    public partial class Zone_Shop
    {
        //判断指定岗位是否有任何当前地图可用员工，供未分配时允许兜底工作的规则使用。
        public bool HasAssignedPawnsForRole(string roleDefName)
        {
            if (roleAssignments.NullOrEmpty() || string.IsNullOrEmpty(roleDefName))
                return false;

            for (int i = 0; i < roleAssignments.Count; i++)
            {
                ShopRoleAssignment assignment = roleAssignments[i];
                if (assignment == null || assignment.roleDefName != roleDefName) continue;
                if (assignment.HasUsablePawnOn(Map))
                    return true;
            }

            return false;
        }

        //判断指定员工是否分配到岗位，保持热路径零分配。
        public bool IsPawnAssignedToRole(string roleDefName, Pawn pawn)
        {
            if (roleAssignments.NullOrEmpty() || string.IsNullOrEmpty(roleDefName) || pawn == null)
                return false;

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

        //判断指定员工是否分配到岗位且当前（或提前交接窗口内）处于排班值班时段。
        public bool IsPawnOnShiftForRole(string roleDefName, Pawn pawn, int leadTicks = 0)
        {
            if (roleAssignments.NullOrEmpty() || string.IsNullOrEmpty(roleDefName) || pawn == null)
                return false;

            for (int i = 0; i < roleAssignments.Count; i++)
            {
                ShopRoleAssignment assignment = roleAssignments[i];
                if (assignment == null || assignment.roleDefName != roleDefName) continue;
                if (!assignment.HasUsablePawnOn(Map) || !assignment.MatchesPawn(pawn)) continue;
                if (assignment.IsOnShiftNow(Map, leadTicks))
                    return true;
            }

            return false;
        }

        //判断指定员工在本岗位的自定义排班是否即将到达下班整点，同时输出该员工是否启用了自定义排班。
        public bool IsPawnShiftEndingSoon(string roleDefName, Pawn pawn, int leadTicks, out bool usesCustomShift)
        {
            usesCustomShift = false;
            if (roleAssignments.NullOrEmpty() || string.IsNullOrEmpty(roleDefName) || pawn == null)
                return false;

            for (int i = 0; i < roleAssignments.Count; i++)
            {
                ShopRoleAssignment assignment = roleAssignments[i];
                if (assignment == null || assignment.roleDefName != roleDefName) continue;
                if (!assignment.HasUsablePawnOn(Map) || !assignment.MatchesPawn(pawn)) continue;
                if (assignment.useShiftSchedule)
                {
                    usesCustomShift = true;
                    if (assignment.IsShiftEndingSoon(Map, leadTicks))
                        return true;
                }
            }

            return false;
        }

        //将指定员工在本店所有启用了自定义排班的岗位值班小时汇总到 24 小时数组，供作息表同步使用。
        public void CollectScheduledShiftHoursForPawn(Pawn pawn, bool[] dutyHours, ref bool hasAnyScheduledShift)
        {
            if (roleAssignments.NullOrEmpty() || pawn == null || dutyHours == null || dutyHours.Length < 24)
                return;

            for (int i = 0; i < roleAssignments.Count; i++)
            {
                ShopRoleAssignment assignment = roleAssignments[i];
                if (assignment == null || !assignment.useShiftSchedule) continue;
                if (!assignment.HasUsablePawnOn(Map) || !assignment.MatchesPawn(pawn)) continue;
                hasAnyScheduledShift = true;
                for (int h = 0; h < 24; h++)
                {
                    if (assignment.IsOnShiftAtHour(h))
                        dutyHours[h] = true;
                }
            }
        }
    }
}
