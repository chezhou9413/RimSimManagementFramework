using System.Collections.Generic;
using RimWorld;
using SimManagementLib.SimThingClass;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimAI
{
    public class JobDriver_DepositToMegaStorage : JobDriver
    {
        private const TargetIndex ThingInd = TargetIndex.A;
        private const TargetIndex StorageInd = TargetIndex.B;
        private const float DepositWorkRequired = 35f;

        private Thing ToHaul => job.GetTarget(ThingInd).Thing;
        private Building_SimContainer Storage => job.GetTarget(StorageInd).Thing as Building_SimContainer;
        private int ReservedCount => job.count;
        private bool reservationCancelled = false;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(ToHaul, job, 1, ReservedCount, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(ThingInd);
            this.FailOnDestroyedOrNull(StorageInd);

            yield return Toils_Goto.GotoThing(ThingInd, PathEndMode.ClosestTouch)
                .FailOnSomeonePhysicallyInteracting(ThingInd);

            yield return Toils_Haul.StartCarryThing(ThingInd, false, false, false, false);

            yield return Toils_Goto.GotoThing(StorageInd, PathEndMode.Touch)
                .FailOnDestroyedOrNull(StorageInd);

            yield return MakeWorkToil("PrepareDepositToMegaStorage", DepositWorkRequired);
            yield return MakeDepositToil();
        }

        private Toil MakeWorkToil(string debugName, float workRequired)
        {
            float workDone = 0f;
            Toil toil = ToilMaker.MakeToil(debugName);
            toil.defaultCompleteMode = ToilCompleteMode.Never;
            toil.tickAction = delegate
            {
                pawn.rotationTracker.FaceTarget(Storage);
                workDone += pawn.GetStatValue(StatDefOf.GeneralLaborSpeed);
                if (workDone >= workRequired)
                    ReadyForNextToil();
            };
            toil.WithProgressBar(StorageInd, () => Mathf.Clamp01(workDone / workRequired));
            return toil;
        }

        private Toil MakeDepositToil()
        {
            Toil toil = ToilMaker.MakeToil("DepositToMegaStorage");
            toil.initAction = delegate
            {
                Building_SimContainer storage = Storage;
                if (storage == null || storage.Destroyed)
                {
                    CancelReservation();
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }

                Thing carried = pawn.carryTracker?.CarriedThing;
                if (carried == null)
                {
                    CancelReservation();
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }

                int deposited = storage.Deposit(pawn, carried.def, ReservedCount);
                if (deposited <= 0)
                {
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }

                pawn.jobs.EndCurrentJob(JobCondition.Succeeded);
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }

        private void CancelReservation()
        {
            if (reservationCancelled) return;
            reservationCancelled = true;
            Building_SimContainer storage = Storage;
            Thing thing = ToHaul;
            if (storage != null && !storage.Destroyed && thing != null)
                storage.CancelPending(thing.def, ReservedCount);
        }

        public override void Notify_Starting()
        {
            base.Notify_Starting();
            reservationCancelled = false;
        }
    }
}
