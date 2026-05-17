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

        /// <summary>
        /// 判断指定商店当前是否允许店员执行商店工作，找不到商店时保持兼容允许工作。
        /// </summary>
        public static bool IsShopOpenForWork(Zone_Shop zone)
        {
            return zone == null || zone.IsOpenNow();
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
            if (zone == null || role == null) return SimTranslation.T("RSMF.Common.Unspecified");

            List<Pawn> pawns = zone.GetAssignedPawns(role.defName);
            if (pawns.Count <= 0) return SimTranslation.T("RSMF.Common.Unspecified");

            string joined = string.Join(SimTranslation.T("RSMF.Common.ListSeparator"), pawns.Take(3).Select(p => p.LabelShortCap));
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
            if (pawn == null) return new StaffEligibility { Eligible = false, Reason = SimTranslation.T("RSMF.StaffManager.InvalidPawn") };
            if (role == null) return new StaffEligibility { Eligible = false, Reason = SimTranslation.T("RSMF.StaffManager.InvalidRole") };
            if (pawn.Destroyed || pawn.Dead) return new StaffEligibility { Eligible = false, Reason = SimTranslation.T("RSMF.StaffManager.DeadOrUnavailable") };
            if (pawn.workSettings == null || !pawn.workSettings.EverWork) return new StaffEligibility { Eligible = false, Reason = SimTranslation.T("RSMF.StaffManager.NoWorkSettings") };

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
    }
}
