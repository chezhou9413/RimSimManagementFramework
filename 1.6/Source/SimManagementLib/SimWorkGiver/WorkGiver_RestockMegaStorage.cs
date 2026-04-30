using RimWorld;
using SimManagementLib.SimAI;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimWorkGiver
{
    /// <summary>
    /// 扫描地图上的 Building_SimContainer，为需要补货的货柜分配
    /// JobDriver_DepositToMegaStorage 任务。
    /// </summary>
    public class WorkGiver_RestockMegaStorage : WorkGiver_Scanner
    {
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            foreach (Thing t in pawn.Map.listerBuildings.allBuildingsColonist.OfType<Building_SimContainer>())
            {
                if (t is Building_SimContainer storage && NeedsRestock(storage, pawn))
                    yield return storage;
            }
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Building_SimContainer storage)) return false;
            if (!NeedsRestock(storage, pawn)) return false;
            return FindBestSupply(pawn, storage) != null;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Building_SimContainer storage)) return null;
            if (!NeedsRestock(storage, pawn)) return null;

            Thing supply = FindBestSupply(pawn, storage);
            if (supply == null) return null;

            ThingDef td = supply.def;
            int needed = storage.CountNeeded(td);
            if (needed <= 0) return null;

            int carryMax = MassUtility.CountToPickUpUntilOverEncumbered(pawn, supply);
            int amount = System.Math.Min(needed, System.Math.Min(carryMax, supply.stackCount));
            if (amount <= 0) return null;

            int reserved = storage.ReservePending(td, amount);
            if (reserved <= 0) return null;

            Job job = JobMaker.MakeJob(
                DefDatabase<JobDef>.GetNamed("DepositToMegaStorage"),
                supply,
                storage);
            job.count = reserved;
            job.haulMode = HaulMode.ToCellStorage;
            return job;
        }

        private static bool NeedsRestock(Building_SimContainer storage, Pawn pawn)
        {
            if (storage.Destroyed || !storage.Spawned) return false;
            WorkGiver_RestockMegaStorage workGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("RestockMegaStorage")?.Worker as WorkGiver_RestockMegaStorage;
            if (workGiver != null && !ShopStaffUtility.AllowsPawnForWorkGiver(ShopStaffUtility.FindShopFor(storage), pawn, workGiver.def))
                return false;
            if (!pawn.CanReach(storage, PathEndMode.Touch, Danger.Deadly)) return false;

            foreach (ThingDef td in storage.ActiveDefs)
            {
                if (storage.CountNeeded(td) > 0) return true;
            }
            return false;
        }

        private static Thing FindBestSupply(Pawn pawn, Building_SimContainer storage)
        {
            Thing best = null;
            float bestDist = float.MaxValue;
            Zone_Shop shop = ShopStaffUtility.FindShopFor(storage);
            WorkGiverDef currentDef = DefDatabase<WorkGiverDef>.GetNamedSilentFail("RestockMegaStorage");
            bool pawnAssigned = ShopStaffUtility.IsAssignedToWorkGiver(shop, pawn, currentDef);

            foreach (ThingDef td in storage.ActiveDefs)
            {
                if (storage.CountNeeded(td) <= 0) continue;

                List<Thing> candidates = pawn.Map.listerThings.ThingsOfDef(td);
                foreach (Thing candidate in candidates)
                {
                    if (!candidate.Spawned || candidate.IsForbidden(pawn)) continue;
                    if (!pawn.CanReserve(candidate)) continue;
                    if (!pawn.CanReach(candidate, PathEndMode.ClosestTouch, Danger.Deadly)) continue;
                    if (IsInsideAnyStorageContainer(candidate)) continue;

                    float dist = (candidate.Position - pawn.Position).LengthHorizontalSquared;
                    if (pawnAssigned)
                        dist *= 0.35f;

                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        best = candidate;
                    }
                }
            }
            return best;
        }

        private static bool IsInsideAnyStorageContainer(Thing t)
        {
            return !t.Spawned;
        }
    }
}
