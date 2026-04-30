using RimWorld;
using SimManagementLib.SimDef;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimZone;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;

namespace SimManagementLib.Tool
{
    public static class ShopStaffUtility
    {
        public struct StaffEligibility
        {
            public bool Eligible;
            public string Reason;
        }

        public static IReadOnlyList<ShopStaffRoleDef> Roles => DefDatabase<ShopStaffRoleDef>.AllDefsListForReading;

        public static Zone_Shop FindShopFor(Building_CashRegister register)
        {
            if (register?.Map == null || !register.Spawned) return null;
            return ShopDataUtility.FindShopZone(register.Map, register.Position);
        }

        public static Zone_Shop FindShopFor(Building_SimContainer storage)
        {
            if (storage?.Map == null || !storage.Spawned) return null;
            return ShopDataUtility.FindShopZone(storage.Map, storage.Position);
        }

        public static List<ShopStaffRoleDef> GetVisibleRoles(Zone_Shop zone)
        {
            if (zone?.Map == null) return new List<ShopStaffRoleDef>();
            return Roles.Where(r => RoleMatchesShop(zone, r)).OrderBy(r => r.index).ThenBy(r => r.defName).ToList();
        }

        public static bool RoleMatchesShop(Zone_Shop zone, ShopStaffRoleDef role)
        {
            if (zone?.Map == null || role == null) return false;
            if (role.requiredThingDefs.NullOrEmpty() && role.requiredThingClasses.NullOrEmpty()) return true;

            foreach (IntVec3 cell in zone.Cells)
            {
                List<Thing> things = zone.Map.thingGrid.ThingsListAt(cell);
                for (int i = 0; i < things.Count; i++)
                {
                    Thing thing = things[i];
                    if (thing == null || thing.Destroyed) continue;

                    if (!role.requiredThingDefs.NullOrEmpty() && role.requiredThingDefs.Contains(thing.def))
                        return true;

                    if (!role.requiredThingClasses.NullOrEmpty())
                    {
                        for (int c = 0; c < role.requiredThingClasses.Count; c++)
                        {
                            if (role.requiredThingClasses[c] != null && role.requiredThingClasses[c].IsInstanceOfType(thing))
                                return true;
                        }
                    }
                }
            }

            return false;
        }

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
                if (assigned.Contains(pawn)) return true;
            }

            return !hasAnyAssignment;
        }

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

        public static string GetAssignmentLabel(Zone_Shop zone, ShopStaffRoleDef role)
        {
            if (zone == null || role == null) return "未指定";

            List<Pawn> pawns = zone.GetAssignedPawns(role.defName);
            if (pawns.Count <= 0) return "未指定";

            string joined = string.Join("、", pawns.Take(3).Select(p => p.LabelShortCap));
            if (pawns.Count > 3) joined += "…";
            return $"{joined} ({pawns.Count}/{role.MaxAssignedPawns})";
        }

        public static IEnumerable<Pawn> GetAssignablePawns(Map map)
        {
            if (map?.mapPawns == null) return Enumerable.Empty<Pawn>();
            return map.mapPawns.FreeColonists
                .Where(p => p != null && !p.Destroyed && !p.Dead)
                .OrderBy(p => p.LabelShortCap);
        }

        public static StaffEligibility EvaluateEligibility(Pawn pawn, ShopStaffRoleDef role)
        {
            if (pawn == null) return new StaffEligibility { Eligible = false, Reason = "无效 pawn" };
            if (role == null) return new StaffEligibility { Eligible = false, Reason = "无效岗位" };
            if (pawn.Destroyed || pawn.Dead) return new StaffEligibility { Eligible = false, Reason = "已死亡或不可用" };
            if (pawn.workSettings == null || !pawn.workSettings.EverWork) return new StaffEligibility { Eligible = false, Reason = "没有工作设置" };

            if (role.workGivers.NullOrEmpty())
                return new StaffEligibility { Eligible = true, Reason = "可上岗" };

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
                        localReasons.Add($"禁用工作类型: {workTypeLabel}");
                    }
                    else if (!pawn.workSettings.WorkIsActive(wg.workType))
                    {
                        usable = false;
                        localReasons.Add($"未开启工作类型: {workTypeLabel}");
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
                            localReasons.Add($"缺少能力: {capacity.LabelCap.RawText}");
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
                    reasons.Add(prefix + " - " + string.Join("，", localReasons.Distinct()));
                }
            }

            if (anyWorkGiverUsable)
                return new StaffEligibility { Eligible = true, Reason = "可上岗" };

            if (reasons.Count <= 0)
                return new StaffEligibility { Eligible = false, Reason = "没有可用岗位工作" };

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < reasons.Count; i++)
            {
                if (i > 0) sb.Append("；");
                sb.Append(reasons[i]);
            }

            return new StaffEligibility { Eligible = false, Reason = sb.ToString() };
        }
    }
}
