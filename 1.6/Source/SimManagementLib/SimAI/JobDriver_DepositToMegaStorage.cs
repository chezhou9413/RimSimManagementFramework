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
    /// 执行店员把物品存入商店大货柜的工作。
    /// </summary>
    public class JobDriver_DepositToMegaStorage : JobDriver
    {
        private const TargetIndex ThingInd = TargetIndex.A;
        private const TargetIndex StorageInd = TargetIndex.B;
        private const float DepositWorkRequired = 35f;

        private Thing ToHaul => job.GetTarget(ThingInd).Thing;
        private Building_SimContainer Storage => job.GetTarget(StorageInd).Thing as Building_SimContainer;
        private int ReservedCount => job.count;
        private ThingDef ReservedDef => job.plantDefToSow;
        private bool reservationReleased;
        private bool depositSucceeded;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(ToHaul, job, 1, ReservedCount, null, errorOnFailed);
        }

        /// <summary>
        /// 构建搬运物品并存入大货柜的 Toil 序列。
        /// </summary>
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(ThingInd);
            this.FailOnDestroyedOrNull(StorageInd);
            AddFinishAction(CleanupReservation);

            yield return Toils_Goto.GotoThing(ThingInd, PathEndMode.ClosestTouch)
                .FailOnSomeonePhysicallyInteracting(ThingInd);

            yield return Toils_Haul.StartCarryThing(ThingInd, false, false, false, false);

            yield return Toils_Goto.GotoThing(StorageInd, PathEndMode.Touch)
                .FailOnDestroyedOrNull(StorageInd);

            yield return MakeWorkToil("PrepareDepositToMegaStorage", DepositWorkRequired);
            yield return MakeDepositToil();
        }

        /// <summary>
        /// 创建向大货柜存入物品前的准备工作读条。
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
        /// 把当前携带物品写入大货柜库存并结束工作。
        /// </summary>
        private Toil MakeDepositToil()
        {
            Toil toil = ToilMaker.MakeToil("DepositToMegaStorage");
            toil.initAction = delegate
            {
                Building_SimContainer storage = Storage;
                if (storage == null || storage.Destroyed)
                {
                    ReleaseReservation();
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }

                Thing carried = pawn.carryTracker?.CarriedThing;
                if (carried == null)
                {
                    ReleaseReservation();
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }

                int deposited = storage.Deposit(pawn, carried.def, ReservedCount);
                if (deposited <= 0)
                {
                    ReleaseReservation();
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }

                depositSucceeded = true;
                reservationReleased = true;
                pawn.jobs.EndCurrentJob(JobCondition.Succeeded);
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }

        /// <summary>
        /// 根据任务结束结果清理补货预约，负责在寻路失败、被打断或目标消失时释放“途中”数量。
        /// </summary>
        private void CleanupReservation(JobCondition condition)
        {
            if (depositSucceeded || condition == JobCondition.Succeeded) return;
            ReleaseReservation();
        }

        /// <summary>
        /// 释放大货柜的待入库预约，负责避免中断工作后长期占用容量。
        /// </summary>
        private void ReleaseReservation()
        {
            if (reservationReleased) return;
            reservationReleased = true;
            Building_SimContainer storage = Storage;
            ThingDef thingDef = ReservedDef ?? ToHaul?.def ?? pawn?.carryTracker?.CarriedThing?.def;
            if (storage != null && !storage.Destroyed && thingDef != null)
                storage.CancelPending(thingDef, ReservedCount);
        }

        /// <summary>
        /// 初始化工作运行状态。
        /// </summary>
        public override void Notify_Starting()
        {
            base.Notify_Starting();
            reservationReleased = false;
            depositSucceeded = false;
        }
    }
}
