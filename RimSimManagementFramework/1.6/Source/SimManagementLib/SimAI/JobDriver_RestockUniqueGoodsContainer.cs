using RimWorld;
using SimManagementLib.SimMapComp;
using SimManagementLib.SimThingClass;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimAI
{
    //专业货柜补货任务，职责是用幂等租约搬运玩家指定的真实物品并安装到精确槽位。
    public sealed class JobDriver_RestockUniqueGoodsContainer : JobDriver
    {
        private Thing Source => job.GetTarget(TargetIndex.A).Thing;
        private Building_UniqueGoodsContainer Container => job.GetTarget(TargetIndex.B).Thing as Building_UniqueGoodsContainer;
        private int sourceThingId = -1;
        private bool leaseReleased;
        private bool installSucceeded;
        private bool preReservationsMade;

        //保存原始来源编号，职责是让读档后的运行 Job 仍能定位对应专业槽位。
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref sourceThingId, "sourceThingId", -1);
            Scribe_Values.Look(ref preReservationsMade, "preReservationsMade", false);
        }

        //幂等取得精确槽位租约并预约真实来源物品。
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (preReservationsMade)
                return true;
            Thing source = Source;
            Building_UniqueGoodsContainer container = Container;
            if (sourceThingId < 0)
                sourceThingId = source?.thingIDNumber ?? -1;
            MapComponent_RestockTaskQueue queue = pawn?.Map?.GetComponent<MapComponent_RestockTaskQueue>();
            if (source == null || container == null || queue == null
                || !queue.TryAcquireUniqueLease(pawn, job, container, sourceThingId))
                return false;
            if (pawn.Reserve(source, job, 1, 1, null, errorOnFailed))
            {
                preReservationsMade = true;
                return true;
            }

            queue.ReleaseLease(job, "专业补货原版预约失败");
            leaseReleased = true;
            return false;
        }

        //构建精确来源搬运和槽位安装流程。
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOnDestroyedOrNull(TargetIndex.B);
            AddFinishAction(CleanupLease);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch);
            yield return Toils_Haul.StartCarryThing(TargetIndex.A, false, false, false, false);
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);

            Toil install = ToilMaker.MakeToil("RestockUniqueGoodsContainer");
            install.initAction = delegate
            {
                if (Container == null || !Container.TryInstallFromCarry(pawn, sourceThingId))
                {
                    Log.Error($"专业货柜补货安装失败：搬运者={pawn?.LabelShort ?? "无"}，来源编号={sourceThingId}，货柜={Container?.LabelCap ?? "无"}。");
                    ReleaseLease("专业补货安装失败");
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }

                installSucceeded = true;
                CompleteLease();
            };
            install.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return install;
        }

        //任务未成功时立即释放专业槽位租约。
        private void CleanupLease(JobCondition condition)
        {
            if (!installSucceeded && condition != JobCondition.Succeeded)
                ReleaseLease("专业补货任务中断");
        }

        //释放当前 Job 的专业补货租约。
        private void ReleaseLease(string reason)
        {
            if (leaseReleased)
                return;
            leaseReleased = true;
            (Container?.MapHeld ?? pawn?.MapHeld)?.GetComponent<MapComponent_RestockTaskQueue>()?.ReleaseLease(job, reason);
        }

        //完成当前 Job 的专业补货租约。
        private void CompleteLease()
        {
            if (leaseReleased)
                return;
            leaseReleased = true;
            (Container?.MapHeld ?? pawn?.MapHeld)?.GetComponent<MapComponent_RestockTaskQueue>()?.CompleteLease(job, "专业补货成功安装");
        }
    }
}
