using RimWorld;
using SimManagementLib.SimAI;
using SimManagementLib.SimDef;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimZone;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using Verse.AI.Group;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 提供商店员工岗位、资格和工作许可判断，负责把店铺配置转换为原版 WorkGiver 可用规则。
    /// </summary>
    public static class ShopStaffUtility
    {
        /// <summary>
        /// 保存员工岗位资格结果，负责同时返回是否可分配和不可分配原因。
        /// </summary>
        public struct StaffEligibility
        {
            public bool Eligible;
            public string Reason;
        }

        public static IReadOnlyList<ShopStaffRoleDef> Roles => DefDatabase<ShopStaffRoleDef>.AllDefsListForReading;

        /// <summary>
        /// 查找收银台所在的商店区域，负责为收银工作提供店铺上下文。
        /// </summary>
        public static Zone_Shop FindShopFor(Building_CashRegister register)
        {
            if (register?.Map == null || !register.Spawned) return null;
            return ShopDataUtility.FindShopZone(register.Map, register.Position);
        }

        /// <summary>
        /// 查找货柜所在的商店区域，负责为补货和搬运工作提供店铺上下文。
        /// </summary>
        public static Zone_Shop FindShopFor(Building_SimContainer storage)
        {
            if (storage?.Map == null || !storage.Spawned) return null;
            return ShopDataUtility.FindShopZone(storage.Map, storage.Position);
        }

        /// <summary>
        /// 判断指定商店当前是否允许店员执行商店工作，找不到商店时保持兼容允许工作。
        /// </summary>
        public static bool IsShopOpenForWork(Zone_Shop zone)
        {
            return zone == null || zone.IsOpenNow();
        }

        /// <summary>
        /// 判断收银员是否可以在指定商店工作，负责让关店后的待付款顾客仍能完成结账。
        /// </summary>
        public static bool CanCashierWorkAt(Zone_Shop zone)
        {
            return IsShopOpenForWork(zone) || HasPendingCheckoutCustomers(zone);
        }

        /// <summary>
        /// 判断商店是否还有活跃顾客待付款，负责限制关店后只保留清空收银队列的工作。
        /// </summary>
        public static bool HasPendingCheckoutCustomers(Zone_Shop zone)
        {
            if (zone?.Map?.lordManager?.lords == null) return false;

            List<Lord> lords = zone.Map.lordManager.lords;
            for (int i = 0; i < lords.Count; i++)
            {
                LordJob_CustomerVisit visit = lords[i]?.LordJob as LordJob_CustomerVisit;
                if (visit == null) continue;

                if (HasActivePawnWithPendingBill(lords[i], visit, zone)) return true;
            }

            return false;
        }

        /// <summary>
        /// 判断顾客队伍中是否存在仍在地图上的待付款顾客。
        /// </summary>
        private static bool HasActivePawnWithPendingBill(Lord lord, LordJob_CustomerVisit visit, Zone_Shop zone)
        {
            if (lord?.ownedPawns == null || visit == null) return false;

            for (int i = 0; i < lord.ownedPawns.Count; i++)
            {
                Pawn pawn = lord.ownedPawns[i];
                if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned) continue;
                if (visit.GetCurrentShop(pawn) != zone) continue;
                if (visit.HasAnyBill(pawn.thingIDNumber))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 返回指定商店可显示的岗位列表，负责按岗位 Worker 和排序配置过滤。
        /// </summary>
        public static List<ShopStaffRoleDef> GetVisibleRoles(Zone_Shop zone)
        {
            if (zone?.Map == null) return new List<ShopStaffRoleDef>();
            return Roles.Where(r => RoleMatchesShop(zone, r)).OrderBy(r => r.index).ThenBy(r => r.defName).ToList();
        }

        /// <summary>
        /// 判断岗位是否应在商店中显示，负责委托岗位 Worker 执行可见性策略。
        /// </summary>
        public static bool RoleMatchesShop(Zone_Shop zone, ShopStaffRoleDef role)
        {
            if (zone?.Map == null || role == null) return false;
            return role.Worker?.CanShow(zone) == true;
        }

        /// <summary>
        /// 返回指定商店中岗位允许分配的人数上限，负责支持 Worker 按店铺状态动态控制人数。
        /// </summary>
        public static int GetMaxAssignedPawns(Zone_Shop zone, ShopStaffRoleDef role)
        {
            if (role == null) return 0;
            return System.Math.Max(0, role.Worker?.GetMaxAssignedPawns(zone) ?? role.MaxAssignedPawns);
        }

        /// <summary>
        /// 判断员工是否允许执行指定 WorkGiver，负责落实店铺岗位分配限制。
        /// </summary>
        public static bool AllowsPawnForWorkGiver(Zone_Shop zone, Pawn pawn, WorkGiverDef workGiverDef)
        {
            if (pawn == null) return false;
            if (zone == null || workGiverDef == null) return true;

            List<ShopStaffRoleDef> matchingRoles = GetVisibleRoles(zone)
                .Where(r => !r.workGivers.NullOrEmpty() && r.workGivers.Contains(workGiverDef))
                .ToList();
            if (matchingRoles.Count <= 0) return true;

            bool hasAnyAssignment = false;
            for (int i = 0; i < matchingRoles.Count; i++)
            {
                List<Pawn> assigned = zone.GetAssignedPawns(matchingRoles[i].defName);
                if (assigned.Count <= 0) continue;
                hasAnyAssignment = true;
                if (assigned.Contains(pawn) && matchingRoles[i].Worker.AllowsPawnForWorkGiver(zone, pawn, workGiverDef)) return true;
            }

            return !hasAnyAssignment;
        }

        /// <summary>
        /// 判断员工是否已被分配到指定 WorkGiver 对应岗位。
        /// </summary>
        public static bool IsAssignedToWorkGiver(Zone_Shop zone, Pawn pawn, WorkGiverDef workGiverDef)
        {
            if (zone == null || pawn == null || workGiverDef == null) return false;

            List<ShopStaffRoleDef> matchingRoles = GetVisibleRoles(zone)
                .Where(r => !r.workGivers.NullOrEmpty() && r.workGivers.Contains(workGiverDef))
                .ToList();

            for (int i = 0; i < matchingRoles.Count; i++)
            {
                if (zone.GetAssignedPawns(matchingRoles[i].defName).Contains(pawn))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 构建岗位分配显示文本，负责展示当前员工和岗位人数上限。
        /// </summary>
        public static string GetAssignmentLabel(Zone_Shop zone, ShopStaffRoleDef role)
        {
            if (zone == null || role == null) return SimTranslation.T("RSMF.Common.Unspecified");

            List<Pawn> pawns = zone.GetAssignedPawns(role.defName);
            if (pawns.Count <= 0) return SimTranslation.T("RSMF.Common.Unspecified");

            string joined = string.Join(SimTranslation.T("RSMF.Common.ListSeparator"), pawns.Take(3).Select(p => p.LabelShortCap));
            if (pawns.Count > 3) joined += "…";
            int max = GetMaxAssignedPawns(zone, role);
            return $"{joined} ({pawns.Count}/{(max <= 0 ? SimTranslation.T("RSMF.Common.Unlimited") : max.ToString())})";
        }

        /// <summary>
        /// 枚举地图上可分配为店员的殖民者。
        /// </summary>
        public static IEnumerable<Pawn> GetAssignablePawns(Map map)
        {
            if (map?.mapPawns == null) return Enumerable.Empty<Pawn>();
            return map.mapPawns.FreeColonists
                .Where(p => p != null && !p.Destroyed && !p.Dead)
                .OrderBy(p => p.LabelShortCap);
        }

        /// <summary>
        /// 评估员工是否满足岗位基础与 Worker 自定义资格要求。
        /// </summary>
        public static StaffEligibility EvaluateEligibility(Zone_Shop zone, Pawn pawn, ShopStaffRoleDef role)
        {
            if (pawn == null) return new StaffEligibility { Eligible = false, Reason = SimTranslation.T("RSMF.StaffManager.InvalidPawn") };
            if (role == null) return new StaffEligibility { Eligible = false, Reason = SimTranslation.T("RSMF.StaffManager.InvalidRole") };
            if (pawn.Destroyed || pawn.Dead) return new StaffEligibility { Eligible = false, Reason = SimTranslation.T("RSMF.StaffManager.DeadOrUnavailable") };
            if (pawn.workSettings == null || !pawn.workSettings.EverWork) return new StaffEligibility { Eligible = false, Reason = SimTranslation.T("RSMF.StaffManager.NoWorkSettings") };
            if (role.Worker != null && !role.Worker.CanAssignPawn(zone, pawn, out string workerReason))
                return new StaffEligibility { Eligible = false, Reason = workerReason ?? "" };

            if (role.workGivers.NullOrEmpty())
                return new StaffEligibility { Eligible = true, Reason = SimTranslation.T("RSMF.StaffManager.Eligible") };

            List<string> reasons = new List<string>();
            bool anyWorkGiverUsable = false;

            for (int i = 0; i < role.workGivers.Count; i++)
            {
                WorkGiverDef wg = role.workGivers[i];
                if (wg == null) continue;

                bool usable = true;
                List<string> localReasons = new List<string>();

                if (wg.workType != null)
                {
                    string workTypeLabel = wg.workType.LabelCap.RawText;
                    if (pawn.WorkTypeIsDisabled(wg.workType))
                    {
                        usable = false;
                        localReasons.Add(SimTranslation.T("RSMF.StaffManager.DisabledWorkType", workTypeLabel.Named("workType")));
                    }
                    else if (!pawn.workSettings.WorkIsActive(wg.workType))
                    {
                        usable = false;
                        localReasons.Add(SimTranslation.T("RSMF.StaffManager.InactiveWorkType", workTypeLabel.Named("workType")));
                    }
                }

                if (wg.requiredCapacities != null)
                {
                    for (int c = 0; c < wg.requiredCapacities.Count; c++)
                    {
                        PawnCapacityDef capacity = wg.requiredCapacities[c];
                        if (capacity != null && !pawn.health.capacities.CapableOf(capacity))
                        {
                            usable = false;
                            localReasons.Add(SimTranslation.T("RSMF.StaffManager.MissingCapacity", capacity.LabelCap.RawText.Named("capacity")));
                        }
                    }
                }

                if (usable)
                {
                    anyWorkGiverUsable = true;
                }
                else if (localReasons.Count > 0)
                {
                    string prefix = wg.label.NullOrEmpty() ? wg.defName : wg.LabelCap.RawText;
                    reasons.Add(prefix + " - " + string.Join(SimTranslation.T("RSMF.Common.ListSeparator"), localReasons.Distinct()));
                }
            }

            if (anyWorkGiverUsable)
                return new StaffEligibility { Eligible = true, Reason = SimTranslation.T("RSMF.StaffManager.Eligible") };

            if (reasons.Count <= 0)
                return new StaffEligibility { Eligible = false, Reason = SimTranslation.T("RSMF.StaffManager.NoUsableRoleWork") };

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < reasons.Count; i++)
            {
                if (i > 0) sb.Append(SimTranslation.T("RSMF.Common.SemicolonSeparator"));
                sb.Append(reasons[i]);
            }

            return new StaffEligibility { Eligible = false, Reason = sb.ToString() };
        }

        /// <summary>
        /// 评估员工是否满足岗位要求，负责兼容缺少商店上下文的旧调用。
        /// </summary>
        public static StaffEligibility EvaluateEligibility(Pawn pawn, ShopStaffRoleDef role)
        {
            return EvaluateEligibility(null, pawn, role);
        }
    }
}
