using SimManagementLib.Tool;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimAI
{
    public class JobDriver_PostPurchaseUseDrugInShop : JobDriver
    {
        private const int DefaultUseTicks = 600;

        private int DurationTicks => job != null && job.expiryInterval > 0 ? job.expiryInterval : DefaultUseTicks;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            yield return EnsureUseCellToil();
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);

            Toil useDrug = Toils_General.Wait(DurationTicks, TargetIndex.None);
            useDrug.initAction = () =>
            {
                CustomerExpressionUtility.TryShowExpression(pawn, CustomerExpressionEvents.DrugUseStart);
                ShopBubbleUtility.ShowTextBubble(pawn, "店内吸食", new Color(0.85f, 1f, 0.85f), 1.1f, false);
            };
            useDrug.tickAction = () =>
            {
                LocalTargetInfo focus = job.GetTarget(TargetIndex.B);
                if (focus.IsValid)
                    pawn.rotationTracker.FaceTarget(focus);
            };
            useDrug.AddFinishAction(() =>
            {
                CustomerExpressionUtility.TryShowExpression(pawn, CustomerExpressionEvents.DrugUseFinish);
            });
            useDrug.WithProgressBar(TargetIndex.A, () => 1f - (ticksLeftThisToil / (float)Mathf.Max(1, DurationTicks)));
            yield return useDrug;
        }

        private Toil EnsureUseCellToil()
        {
            Toil toil = new Toil();
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            toil.initAction = () =>
            {
                LocalTargetInfo spot = job.GetTarget(TargetIndex.A);
                if (spot.IsValid && spot.Cell.IsValid && IsUsableCell(spot.Cell))
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
                c => IsUsableCell(c),
                out found);
        }

        private bool IsUsableCell(IntVec3 cell)
        {
            if (!cell.IsValid || pawn.Map == null) return false;
            if (!cell.InBounds(pawn.Map)) return false;
            if (!cell.Standable(pawn.Map)) return false;
            if (!pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly)) return false;
            return true;
        }
    }
}
