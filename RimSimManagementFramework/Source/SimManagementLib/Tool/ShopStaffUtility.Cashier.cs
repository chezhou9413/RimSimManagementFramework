using RimWorld;
using SimManagementLib.SimZone;
using Verse;

namespace SimManagementLib.Tool
{
    //提供收银岗位权限、日程上班时段与手动补位判断。
    public static partial class ShopStaffUtility
    {
        private static WorkGiverDef cachedManCashRegisterWorkGiverDef;

        //返回收银值班主 WorkGiverDef，供普通值班与优先接待共享同一岗位分配名单。
        public static WorkGiverDef ManCashRegisterWorkGiverDef
            => cachedManCashRegisterWorkGiverDef ??= DefDatabase<WorkGiverDef>.GetNamed("Sim_WorkGiver_ManCashRegister");

        //按店铺收银/前台岗位分配与当前排班时段判断自动派工是否允许该员工值班。
        public static bool AllowsPawnForCashier(Zone_Shop shop, Pawn pawn)
            => AllowsPawnForWorkGiver(shop, pawn, ManCashRegisterWorkGiverDef, 0);

        //判断员工是否可以提前出发参与收银台交接班（当前在班或在提前窗口内即将接班）。
        public static bool AllowsPawnForCashierHandover(Zone_Shop shop, Pawn pawn, int leadTicks)
            => AllowsPawnForWorkGiver(shop, pawn, ManCashRegisterWorkGiverDef, leadTicks);

        //判断员工是否显式分配到了当前店铺的收银或前台岗位。
        public static bool IsAssignedCashier(Zone_Shop shop, Pawn pawn)
            => IsAssignedToWorkGiver(shop, pawn, ManCashRegisterWorkGiverDef);

        //判断员工是否仍在店铺收银岗位名单内（忽略排班小时，用于检测在岗期间是否被撤岗）。
        public static bool AllowsPawnForCashierRoleIgnoringShift(Zone_Shop shop, Pawn pawn)
            => AllowsPawnForWorkGiver(shop, pawn, ManCashRegisterWorkGiverDef, 0, ignoreShift: true);

        //判断在岗收银员是否临近下班（自定义排班临近结束，或全天模式下原版作息下一小时转为休息/娱乐/冥想）。
        public static bool IsCashierShiftEndingSoon(Zone_Shop shop, Pawn pawn, int leadTicks)
        {
            if (pawn == null || leadTicks <= 0) return false;
            bool usesCustomShift = false;
            if (shop != null)
            {
                WorkGiverDef workGiver = ManCashRegisterWorkGiverDef;
                var visibleRoles = GetCachedVisibleRoles(shop);
                for (int i = 0; i < visibleRoles.Count; i++)
                {
                    var role = visibleRoles[i];
                    if (!RoleContainsWorkGiver(role, workGiver)) continue;
                    if (shop.IsPawnShiftEndingSoon(role.defName, pawn, leadTicks, out bool custom))
                        return true;
                    if (custom) usesCustomShift = true;
                }
            }
            if (usesCustomShift) return false;
            return IsTimetableTurningOffDutySoon(pawn, leadTicks);
        }

        //判断殖民者原版作息表是否在指定 tick 内即将进入睡眠、娱乐或冥想时段。
        public static bool IsTimetableTurningOffDutySoon(Pawn pawn, int leadTicks)
        {
            if (pawn?.timetable == null || pawn.Map == null || !pawn.IsColonist || pawn.IsPrisonerOfColony || leadTicks <= 0)
                return false;
            int dayTick = GenLocalDate.DayTick(pawn.Map);
            int ticksToNextHour = GenDate.TicksPerHour - (dayTick % GenDate.TicksPerHour);
            if (ticksToNextHour > leadTicks) return false;
            int nextHour = ((dayTick / GenDate.TicksPerHour) + 1) % 24;
            TimeAssignmentDef next = pawn.timetable.GetAssignment(nextHour);
            return next == TimeAssignmentDefOf.Sleep
                || next == TimeAssignmentDefOf.Joy
                || next == TimeAssignmentDefOf.Meditate;
        }

        //判断殖民者当前日程是否处于睡眠、娱乐或冥想等非上班时段。
        public static bool IsPawnOffDutyByTimetable(Pawn pawn)
        {
            if (pawn?.timetable == null || !pawn.IsColonist || pawn.IsPrisonerOfColony)
                return false;
            TimeAssignmentDef assignment = pawn.timetable.CurrentAssignment;
            return assignment == TimeAssignmentDefOf.Sleep
                || assignment == TimeAssignmentDefOf.Joy
                || assignment == TimeAssignmentDefOf.Meditate;
        }

        //判断玩家机械体当前是否处于可自动接单的工作状态，排除非工作模式、充电中或低电量状态。
        public static bool CanMechanicalStaffWorkAutomatically(Pawn pawn)
        {
            if (!IsAssignableMechanicalStaff(pawn))
                return false;
            if (pawn.Drafted || pawn.Downed || pawn.InMentalState)
                return false;
            if (pawn.IsCharging())
                return false;

            Need_MechEnergy energy = pawn.needs?.energy;
            if (energy != null)
            {
                if (energy.IsLowEnergySelfShutdown)
                    return false;
                if (energy.CurLevel + 0.1f < JobGiver_GetEnergy.GetMinAutorechargeThreshold(pawn))
                    return false;
            }

            MechWorkModeDef workMode = pawn.GetMechWorkMode();
            if (workMode != null && workMode != MechWorkModeDefOf.Work)
                return false;

            return true;
        }

        //判断员工（殖民者或机械体）当前是否处于非自动上岗时段或需要去充电/休眠。
        public static bool IsPawnOffDutyForAutomaticWork(Pawn pawn)
        {
            if (pawn == null)
                return true;
            if (pawn.IsColonyMech)
                return !CanMechanicalStaffWorkAutomatically(pawn);
            return IsPawnOffDutyByTimetable(pawn);
        }

        //仅用于玩家手动强制派工，忽略固定人员名单但保留岗位能力与扩展限制。
        public static bool CanRelieveCashier(Zone_Shop shop, Pawn pawn)
        {
            WorkGiverDef workGiver = ManCashRegisterWorkGiverDef;
            bool hasRole = false;
            foreach (var role in GetCachedVisibleRoles(shop))
            {
                if (!RoleContainsWorkGiver(role, workGiver)) continue;
                hasRole = true;
                if (IsPawnEligibleForRole(shop, pawn, role)
                    && role.Worker.AllowsPawnForWorkGiver(shop, pawn, workGiver)) return true;
            }
            return !hasRole;
        }
    }
}
