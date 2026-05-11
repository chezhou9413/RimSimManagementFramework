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
        private bool reservationCancelled = false;

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

        /// <summary>
        /// 取消大货柜的待入库预约，避免中断工作后长期占用容量。
        /// </summary>
        private void CancelReservation()
        {
            if (reservationCancelled) return;
            reservationCancelled = true;
            Building_SimContainer storage = Storage;
            Thing thing = ToHaul;
            if (storage != null && !storage.Destroyed && thing != null)
                storage.CancelPending(thing.def, ReservedCount);
        }

        /// <summary>
        /// 初始化工作运行状态。
        /// </summary>
        public override void Notify_Starting()
        {
            base.Notify_Starting();
            reservationCancelled = false;
        }
    }
}
