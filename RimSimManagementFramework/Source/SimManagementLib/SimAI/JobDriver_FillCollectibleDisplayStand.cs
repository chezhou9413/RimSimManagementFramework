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
    /// 执行把地图收藏品搬运并安装到展台槽位的工作。
    /// </summary>
    public class JobDriver_FillCollectibleDisplayStand : JobDriver
    {
        private const TargetIndex SourceInd = TargetIndex.A;
        private const TargetIndex StandInd = TargetIndex.B;
        private const float InstallWorkRequired = 45f;

        private Thing Source => job.GetTarget(SourceInd).Thing;
        private Building_CollectibleDisplayStand Stand => job.GetTarget(StandInd).Thing as Building_CollectibleDisplayStand;
        private int SlotIndex => job.count;

        /// <summary>
        /// 预定来源收藏品和目标展台，避免多个小人同时处理同一槽位来源。
        /// </summary>
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Source, job, 1, 1, null, errorOnFailed)
                && pawn.Reserve(Stand, job, 1, 1, null, errorOnFailed);
        }

        /// <summary>
        /// 构建搬运收藏品并写入展台槽位的 Toil 序列。
        /// </summary>
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(SourceInd);
            this.FailOnDestroyedOrNull(StandInd);
            this.FailOn(() => Stand?.GetSlot(SlotIndex)?.HasStoredThing != false);

            yield return Toils_Goto.GotoThing(SourceInd, PathEndMode.ClosestTouch)
                .FailOnSomeonePhysicallyInteracting(SourceInd);

            yield return MakeStartCarryToil();

            yield return Toils_Goto.GotoThing(StandInd, PathEndMode.Touch)
                .FailOnDestroyedOrNull(StandInd);

            yield return MakeInstallWorkToil();
            yield return MakeInstallToil();
        }

        /// <summary>
        /// 把来源收藏品转成缩小物并放入小人携带容器。
        /// </summary>
        private Toil MakeStartCarryToil()
        {
            Toil toil = ToilMaker.MakeToil("CarryCollectibleToDisplayStand");
            toil.initAction = delegate
            {
                if (!CollectibleDisplayStandUtility.TryStartCarryAsMinified(pawn, Source, out _))
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }

        /// <summary>
        /// 创建展台安装前的工作读条。
        /// </summary>
        private Toil MakeInstallWorkToil()
        {
            float workDone = 0f;
            Toil toil = ToilMaker.MakeToil("PrepareInstallCollectibleDisplayStand");
            toil.defaultCompleteMode = ToilCompleteMode.Never;
            toil.tickAction = delegate
            {
                pawn.rotationTracker.FaceTarget(Stand);
                workDone += pawn.GetStatValue(StatDefOf.GeneralLaborSpeed);
                ShopProgressBarUtility.Report(pawn, Mathf.Clamp01(workDone / InstallWorkRequired), new Color(0.55f, 0.82f, 1f, 0.95f));
                if (workDone >= InstallWorkRequired)
                    ReadyForNextToil();
            };
            toil.AddFinishAction(() => ShopProgressBarUtility.Clear(pawn));
            return toil;
        }

        /// <summary>
        /// 把携带的收藏品写入目标槽位并结束工作。
        /// </summary>
        private Toil MakeInstallToil()
        {
            Toil toil = ToilMaker.MakeToil("InstallCollectibleDisplayStand");
            toil.initAction = delegate
            {
                Building_CollectibleDisplayStand stand = Stand;
                if (stand == null || stand.Destroyed || !stand.TryInstallFromPawnCarry(pawn, SlotIndex))
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
