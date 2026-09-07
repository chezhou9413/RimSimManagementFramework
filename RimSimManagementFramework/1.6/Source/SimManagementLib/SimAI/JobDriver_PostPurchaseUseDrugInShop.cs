using SimManagementLib.Tool;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimAI
{
    /// <summary>
    /// 执行顾客付款后在店内使用药物的行为，负责寻找可用格、面向关注目标并显示进度。
    /// </summary>
    public class JobDriver_PostPurchaseUseDrugInShop : JobDriver
    {
        private const int DefaultUseTicks = 600;

        private int DurationTicks => job != null && job.expiryInterval > 0 ? job.expiryInterval : DefaultUseTicks;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        /// <summary>
        /// 构建店内使用药物行为的 Toil 序列。
        /// </summary>
        protected override IEnumerable<Toil> MakeNewToils()
        {
            yield return EnsureUseCellToil();
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);

            Toil useDrug = Toils_General.Wait(DurationTicks, TargetIndex.None);
            useDrug.initAction = () =>
            {
                CustomerExpressionUtility.TryShowExpression(pawn, CustomerExpressionEvents.DrugUseStart);
                ShopBubbleUtility.ShowTextBubble(pawn, SimTranslation.T("RSMF.Bubble.DrugUseInShop"), new Color(0.85f, 1f, 0.85f), 1.1f, false);
            };
            useDrug.tickAction = () =>
            {
                ShopProgressBarUtility.Report(pawn, 1f - ticksLeftThisToil / (float)Mathf.Max(1, DurationTicks), new Color(0.7f, 1f, 0.7f, 0.95f));
                LocalTargetInfo focus = job.GetTarget(TargetIndex.B);
                if (focus.IsValid)
                    pawn.rotationTracker.FaceTarget(focus);
            };
            useDrug.AddFinishAction(() =>
            {
                CustomerExpressionUtility.TryShowExpression(pawn, CustomerExpressionEvents.DrugUseFinish);
                ShopProgressBarUtility.Clear(pawn);
            });
            yield return useDrug;
        }

        /// <summary>
        /// 确保顾客拥有可站立的店内使用格。
        /// </summary>
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

        /// <summary>
        /// 判断指定格子是否能作为顾客店内使用药物位置。
        /// </summary>
        private bool IsUsableCell(IntVec3 cell)
        {
            if (!cell.IsValid || pawn.Map == null) return false;
            if (!cell.InBounds(pawn.Map)) return false;
            if (!cell.Standable(pawn.Map)) return false;
            if (!CustomerSafetyUtility.CanCustomerReach(pawn, cell, PathEndMode.OnCell, Danger.Deadly)) return false;
            return true;
        }
    }
}
