using RimWorld;
using SimManagementLib.SimThingClass;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimAI
{
    public class JobDriver_CollectCashRegisterSilver : JobDriver
    {
        private const TargetIndex RegisterInd = TargetIndex.A;
        private const TargetIndex SilverThingInd = TargetIndex.B;
        private const TargetIndex StoreCellInd = TargetIndex.C;
        private const float CashHandlingWorkRequired = 22f;

        private Building_CashRegister Register => job.GetTarget(RegisterInd).Thing as Building_CashRegister;
        private int ReservedCount => job.count;
        private bool reservationCleared;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Register, job, 1, -1, null, errorOnFailed);
        }

        public override void Notify_Starting()
        {
            base.Notify_Starting();
            reservationCleared = false;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(RegisterInd);
            AddFinishAction(_ =>
            {
                if (reservationCleared) return;
                Register?.CancelWithdrawReservation(ReservedCount);
                reservationCleared = true;
            });

            yield return Toils_Goto.GotoThing(RegisterInd, PathEndMode.Touch)
                .FailOnDestroyedOrNull(RegisterInd);

            yield return MakeWorkToil("PrepareWithdrawCashRegisterSilver", CashHandlingWorkRequired);
            yield return MakeWithdrawToil();

            yield return Toils_Goto.GotoThing(SilverThingInd, PathEndMode.ClosestTouch)
                .FailOnDestroyedOrNull(SilverThingInd);

            yield return Toils_Haul.StartCarryThing(SilverThingInd, false, false, false, false);

            yield return MakeFindStoreCellToil();
            yield return Toils_Goto.GotoCell(StoreCellInd, PathEndMode.ClosestTouch);
            yield return MakeWorkToil("PrepareDropCashRegisterSilver", CashHandlingWorkRequired);
            yield return MakeDropCarriedToil();
        }

        private Toil MakeWorkToil(string debugName, float workRequired)
        {
            float workDone = 0f;
            Toil toil = ToilMaker.MakeToil(debugName);
            toil.defaultCompleteMode = ToilCompleteMode.Never;
            toil.tickAction = delegate
            {
                workDone += pawn.GetStatValue(StatDefOf.GeneralLaborSpeed);
                if (workDone >= workRequired)
                    ReadyForNextToil();
            };
            toil.WithProgressBar(RegisterInd, () => Mathf.Clamp01(workDone / workRequired));
            return toil;
        }

        private Toil MakeWithdrawToil()
        {
            Toil toil = ToilMaker.MakeToil("WithdrawCashRegisterSilver");
            toil.initAction = delegate
            {
                Building_CashRegister register = Register;
                if (register == null || register.Destroyed)
                {
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }

                int silverCount = register.WithdrawReservedSilver(ReservedCount);
                reservationCleared = true;

                if (silverCount <= 0)
                {
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }

                Thing silver = ThingMaker.MakeThing(ThingDefOf.Silver);
                silver.stackCount = silverCount;
                if (!GenPlace.TryPlaceThing(silver, pawn.Position, pawn.Map, ThingPlaceMode.Near, out Thing placedSilver) || placedSilver == null)
                {
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }

                job.SetTarget(SilverThingInd, placedSilver);
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }

        private Toil MakeFindStoreCellToil()
        {
            Toil toil = ToilMaker.MakeToil("FindStoreCellForRegisterSilver");
            toil.initAction = delegate
            {
                IntVec3 targetCell = pawn.Position;
                Thing carried = pawn.carryTracker?.CarriedThing;

                if (carried != null)
                {
                    if (!StoreUtility.TryFindBestBetterStoreCellFor(carried, pawn, pawn.Map, StoragePriority.Unstored, pawn.Faction, out targetCell))
                    {
                        if (!StoreUtility.TryFindStoreCellNearColonyDesperate(carried, pawn, out targetCell))
                            targetCell = pawn.Position;
                    }
                }

                job.SetTarget(StoreCellInd, targetCell);
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }

        private Toil MakeDropCarriedToil()
        {
            Toil toil = ToilMaker.MakeToil("DropCashRegisterSilver");
            toil.initAction = delegate
            {
                if (pawn.carryTracker?.CarriedThing == null)
                {
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }

                IntVec3 storeCell = job.GetTarget(StoreCellInd).Cell;
                if (!storeCell.IsValid)
                    storeCell = pawn.Position;

                if (!pawn.carryTracker.TryDropCarriedThing(storeCell, ThingPlaceMode.Near, out Thing _))
                {
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }

                pawn.jobs.EndCurrentJob(JobCondition.Succeeded);
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }
    }
}
