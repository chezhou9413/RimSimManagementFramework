using System.Collections.Generic;
using RimWorld;
using SimManagementLib.SimThingClass;
using SimManagementLib.Tool;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimAI
{
    /// <summary>
    /// 执行店员从商店大货柜取出指定物品的工作。
    /// </summary>
    public class JobDriver_WithdrawFromMegaStorage : JobDriver
    {
        private const TargetIndex StorageInd = TargetIndex.A;
        private const float WithdrawWorkRequired = 28f;

        private Building_SimContainer Storage => job.GetTarget(StorageInd).Thing as Building_SimContainer;
        private ThingDef WithdrawDef => job.plantDefToSow;
        private int ReservedCount => job.count;
        private bool reservationReleased;
        private bool withdrawSucceeded;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        /// <summary>
        /// 构建前往大货柜、准备取货并生成取出物品的 Toil 序列。
        /// </summary>
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(StorageInd);
            AddFinishAction(CleanupReservation);

            yield return Toils_Goto.GotoThing(StorageInd, PathEndMode.Touch)
                .FailOnDestroyedOrNull(StorageInd);

            yield return MakeWorkToil("PrepareWithdrawFromMegaStorage", WithdrawWorkRequired);
            yield return MakeWithdrawToil();
        }

        /// <summary>
        /// 创建从大货柜取出物品前的准备工作读条。
        /// </summary>
        private Toil MakeWorkToil(string debugName, float workRequired)
        {
            float workDone = 0f;
            Toil toil = ToilMaker.MakeToil(debugName);
            toil.defaultCompleteMode = ToilCompleteMode.Never;
            toil.tickAction = delegate
            {
                pawn.rotationTracker.FaceTarget(Storage);
                workDone += pawn.GetStatValue(StatDefOf.GeneralLaborSpeed);
                ShopProgressBarUtility.Report(pawn, Mathf.Clamp01(workDone / workRequired), new Color(0.55f, 0.82f, 1f, 0.95f));
                if (workDone >= workRequired)
                    ReadyForNextToil();
            };
            toil.AddFinishAction(() => ShopProgressBarUtility.Clear(pawn));
            return toil;
        }

        /// <summary>
        /// 从大货柜库存中取出目标物品并在店员附近生成实物。
        /// </summary>
        private Toil MakeWithdrawToil()
        {
            Toil toil = ToilMaker.MakeToil("WithdrawFromMegaStorage");
            toil.initAction = delegate
            {
                Building_SimContainer storage = Storage;
                ThingDef td = WithdrawDef;

                if (storage == null || storage.Destroyed || td == null)
                {
                    ReleaseReservation();
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }

                Thing result = storage.Withdraw(td, ReservedCount, pawn.Position, ReservedCount);
                reservationReleased = true;
                if (result == null)
                {
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }

                withdrawSucceeded = true;
                pawn.jobs.EndCurrentJob(JobCondition.Succeeded);
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }

        /// <summary>
        /// 根据任务结束结果清理出库预约，负责在任务被打断或失败时恢复可搬出库存。
        /// </summary>
        private void CleanupReservation(JobCondition condition)
        {
            if (withdrawSucceeded || condition == JobCondition.Succeeded) return;
            ReleaseReservation();
        }

        /// <summary>
        /// 释放大货柜的待出库预约，负责避免中断工作后长期占用库存。
        /// </summary>
        private void ReleaseReservation()
        {
            if (reservationReleased) return;
            reservationReleased = true;
            Building_SimContainer storage = Storage;
            ThingDef td = WithdrawDef;
            if (storage != null && !storage.Destroyed && td != null)
                storage.CancelPendingOut(td, ReservedCount);
        }

        /// <summary>
        /// 初始化工作运行状态。
        /// </summary>
        public override void Notify_Starting()
        {
            base.Notify_Starting();
            reservationReleased = false;
            withdrawSucceeded = false;
        }
    }
}
