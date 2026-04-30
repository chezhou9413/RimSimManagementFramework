using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;
using SimManagementLib.Tool;

namespace SimManagementLib.SimAI
{
    public class JobDriver_PostPurchaseDineInShop : JobDriver
    {
        private const int DefaultDineTicks = 700;

        private int DurationTicks => job != null && job.expiryInterval > 0 ? job.expiryInterval : DefaultDineTicks;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            yield return EnsureSeatCellToil();
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);

            Toil dine = Toils_General.Wait(DurationTicks, TargetIndex.None);
            dine.initAction = () =>
            {
                CustomerExpressionUtility.TryShowExpression(pawn, CustomerExpressionEvents.DineStart);
            };
            dine.tickAction = () =>
            {
                LocalTargetInfo table = job.GetTarget(TargetIndex.B);
                if (table.IsValid)
                    pawn.rotationTracker.FaceTarget(table);
            };
            dine.AddFinishAction(() =>
            {
                CustomerExpressionUtility.TryShowExpression(pawn, CustomerExpressionEvents.DineFinish);
            });
            dine.WithProgressBar(TargetIndex.A, () => 1f - (ticksLeftThisToil / (float)Mathf.Max(1, DurationTicks)));
            yield return dine;
        }

        private Toil EnsureSeatCellToil()
        {
            Toil toil = new Toil();
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            toil.initAction = () =>
            {
                LocalTargetInfo seat = job.GetTarget(TargetIndex.A);
                if (seat.IsValid && seat.Cell.IsValid && IsUsableSeatCell(seat.Cell))
                    return;

                if (TryFindNearbyCell(out IntVec3 found))
                    job.SetTarget(TargetIndex.A, found);
                else
                    job.SetTarget(TargetIndex.A, pawn.Position);
            };
            return toil;
        }

        private bool TryFindNearbyCell(out IntVec3 found)
        {
            return CellFinder.TryFindRandomCellNear(
                pawn.Position,
                pawn.Map,
                8,
                c => IsUsableSeatCell(c),
                out found);
        }

        private bool IsUsableSeatCell(IntVec3 cell)
        {
            if (!cell.IsValid || pawn.Map == null) return false;
            if (!cell.InBounds(pawn.Map)) return false;
            if (!cell.Standable(pawn.Map)) return false;
            if (!pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly)) return false;
            return true;
        }
    }
}
