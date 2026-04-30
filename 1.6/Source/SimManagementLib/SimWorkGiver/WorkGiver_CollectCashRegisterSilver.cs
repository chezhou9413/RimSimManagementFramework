using RimWorld;
using SimManagementLib.SimThingClass;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimWorkGiver
{
    public class WorkGiver_CollectCashRegisterSilver : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial);
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            if (pawn?.Map == null) yield break;

            foreach (Building_CashRegister register in pawn.Map.listerBuildings.allBuildingsColonist.OfType<Building_CashRegister>())
            {
                if (register != null)
                    yield return register;
            }
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Building_CashRegister register = t as Building_CashRegister;
            if (register == null || register.Destroyed || !register.Spawned) return false;
            if (register.IsManned) return false; // 下班后再统一取钱
            if (register.AvailableForWithdraw <= 0) return false;
            if (!pawn.CanReserve(register, 1, -1, null, forced)) return false;
            if (!pawn.CanReach(register, PathEndMode.Touch, Danger.Deadly)) return false;
            return true;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Building_CashRegister register = t as Building_CashRegister;
            if (register == null) return null;

            int batchSize = Mathf.Max(1, ThingDefOf.Silver.stackLimit);
            int desired = Mathf.Min(register.AvailableForWithdraw, batchSize);
            int reserved = register.ReserveWithdrawSilver(desired);
            if (reserved <= 0) return null;

            Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Sim_CollectCashRegisterSilver"), register);
            job.count = reserved;
            return job;
        }
    }
}
