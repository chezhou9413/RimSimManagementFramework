using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;
using SimManagementLib.Tool;

namespace SimManagementLib.SimAI
{
    /// <summary>
    /// 执行顾客付款后在店内就餐停留的行为，负责寻找可用座位、面向桌子并显示进度。
    /// </summary>
    public class JobDriver_PostPurchaseDineInShop : JobDriver
    {
        private const int DefaultDineTicks = 700;

        private int DurationTicks => job != null && job.expiryInterval > 0 ? job.expiryInterval : DefaultDineTicks;
        private Thing Food => job.GetTarget(TargetIndex.C).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        /// <summary>
        /// 构建店内就餐行为的 Toil 序列。
        /// </summary>
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
                ShopProgressBarUtility.Report(pawn, 1f - ticksLeftThisToil / (float)Mathf.Max(1, DurationTicks));
                LocalTargetInfo table = job.GetTarget(TargetIndex.B);
                if (table.IsValid)
                    pawn.rotationTracker.FaceTarget(table);
            };
            dine.AddFinishAction(() =>
            {
                CustomerNeedUtility.ConsumePurchasedFood(pawn, Food);
                CustomerExpressionUtility.TryShowExpression(pawn, CustomerExpressionEvents.DineFinish);
                ShopProgressBarUtility.Clear(pawn);
            });
            yield return dine;
        }

        /// <summary>
        /// 确保顾客拥有可站立的店内就餐格。
        /// </summary>
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

        /// <summary>
        /// 判断指定格子是否能作为顾客店内就餐位置。
        /// </summary>
        private bool IsUsableSeatCell(IntVec3 cell)
        {
            if (!cell.IsValid || pawn.Map == null) return false;
            if (!cell.InBounds(pawn.Map)) return false;
            if (!cell.Standable(pawn.Map)) return false;
            if (!CustomerSafetyUtility.CanCustomerReach(pawn, cell, PathEndMode.OnCell, Danger.Deadly)) return false;
            return true;
        }
    }
}
