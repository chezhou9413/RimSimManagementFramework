using System.Collections.Generic;
using RimWorld;
using SimManagementLib.SimThingClass;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimAI
{
    public class JobDriver_WithdrawFromMegaStorage : JobDriver
    {
        private const TargetIndex StorageInd = TargetIndex.A;
        private const float WithdrawWorkRequired = 28f;

        private Building_SimContainer Storage => job.GetTarget(StorageInd).Thing as Building_SimContainer;
        private ThingDef WithdrawDef => job.plantDefToSow;
        private int ReservedCount => job.count;
        private bool reservationCancelled = false;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(StorageInd);

            yield return Toils_Goto.GotoThing(StorageInd, PathEndMode.Touch)
                .FailOnDestroyedOrNull(StorageInd);

            yield return MakeWorkToil("PrepareWithdrawFromMegaStorage", WithdrawWorkRequired);
            yield return MakeWithdrawToil();
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

        private Toil MakeWithdrawToil()
        {
            Toil toil = ToilMaker.MakeToil("WithdrawFromMegaStorage");
            toil.initAction = delegate
            {
                Building_SimContainer storage = Storage;
                ThingDef td = WithdrawDef;

                if (storage == null || storage.Destroyed || td == null)
                {
                    CancelReservation();
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }

                Thing result = storage.Withdraw(td, ReservedCount, pawn.Position, ReservedCount);
                if (result == null)
                {
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }

                reservationCancelled = true;
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
            ThingDef td = WithdrawDef;
            if (storage != null && !storage.Destroyed && td != null)
                storage.CancelPendingOut(td, ReservedCount);
        }

        public override void Notify_Starting()
        {
            base.Notify_Starting();
            reservationCancelled = false;
        }
    }
}
