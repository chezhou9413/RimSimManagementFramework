using RimWorld;
using System;
using System.Collections.Generic;
using Verse;

namespace SimManagementLib.SimDef
{
    public class ShopStaffRoleDef : Def
    {
        public int maxAssignedPawns = 1;
        public List<ThingDef> requiredThingDefs = new List<ThingDef>();
        public List<WorkGiverDef> workGivers = new List<WorkGiverDef>();
        public List<JobDef> jobDefs = new List<JobDef>();
        public List<Type> requiredThingClasses = new List<Type>();

        public int MaxAssignedPawns => Math.Max(0, maxAssignedPawns);
    }
}
