using RimWorld;
using SimManagementLib.SimMapComp;
using SimManagementLib.SimThingClass;
using SimManagementLib.Tool;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimAI
{
    //普通货柜补货任务，职责是幂等取得 Job 租约并把地图实物搬入虚拟库存。
    public class JobDriver_DepositToMegaStorage : JobDriver
    {
        private const TargetIndex ThingInd = TargetIndex.A;
        private const TargetIndex StorageInd = TargetIndex.B;
        private const float DepositWorkRequired = 35f;

        private Thing ToHaul => job.GetTarget(ThingInd).Thing;
        private Building_SimContainer Storage => job.GetTarget(StorageInd).Thing as Building_SimContainer;
        private ThingDef ReservedDef => job.plantDefToSow;
        private bool leaseReleased;
        private bool depositSucceeded;
        private bool preReservationsMade;

        //读写普通补货预约状态，职责是让同一 Job 在读档后仍不会重复执行原版预约。
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref preReservationsMade, "preReservationsMade", false);
        }

        //幂等取得地图补货租约并预约真实货源，同一 Job 重复调用不会重复增加在途数量。
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (preReservationsMade)
                return true;
            Building_SimContainer storage = Storage;
            Thing source = ToHaul;
            ThingDef thingDef = ReservedDef ?? source?.def;
            if (storage == null || storage.Destroyed || source == null || thingDef == null)
                return false;

            job.plantDefToSow = thingDef;
            int wanted = job.count > 0 ? job.count : source.stackCount;
            MapComponent_RestockTaskQueue queue = pawn?.Map?.GetComponent<MapComponent_RestockTaskQueue>();
            if (queue == null || !queue.TryAcquireBulkLease(pawn, job, storage, thingDef, wanted, out int reserved))
                return false;

            job.count = reserved;
            if (pawn.Reserve(source, job, 1, reserved, null, errorOnFailed))
            {
                preReservationsMade = true;
                return true;
            }

            queue.ReleaseLease(job, "普通补货原版预约失败");
            leaseReleased = true;
            return false;
        }

        //构建搬运物品、前往货柜和实际入库的工作序列。
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(ThingInd);
            this.FailOnDestroyedOrNull(StorageInd);
            AddFinishAction(CleanupLease);

            yield return Toils_Goto.GotoThing(ThingInd, PathEndMode.ClosestTouch)
                .FailOnSomeonePhysicallyInteracting(ThingInd);
            yield return Toils_Haul.StartCarryThing(ThingInd, false, false, false, false);
            yield return Toils_Goto.GotoThing(StorageInd, PathEndMode.Touch)
                .FailOnDestroyedOrNull(StorageInd);
            yield return MakeWorkToil("PrepareDepositToMegaStorage", DepositWorkRequired);
            yield return MakeDepositToil();
        }

        //创建入库前的短工作读条并按劳动速度推进。
        private Toil MakeWorkToil(string debugName, float workRequired)
        {
            float workDone = 0f;
            Toil toil = ToilMaker.MakeToil(debugName);
            toil.defaultCompleteMode = ToilCompleteMode.Never;
            toil.tickAction = delegate
            {
                pawn.rotationTracker.FaceTarget(Storage);
                workDone += pawn.GetStatValue(StatDefOf.GeneralLaborSpeed);
                ShopProgressBarUtility.Report(
                    pawn,
                    Mathf.Clamp01(workDone / workRequired),
                    new Color(0.55f, 0.82f, 1f, 0.95f));
                if (workDone >= workRequired)
                    ReadyForNextToil();
            };
            toil.AddFinishAction(() => ShopProgressBarUtility.Clear(pawn));
            return toil;
        }

        //把携带物品写入货柜并在库存变化后完成租约。
        private Toil MakeDepositToil()
        {
            Toil toil = ToilMaker.MakeToil("DepositToMegaStorage");
            toil.initAction = delegate
            {
                Building_SimContainer storage = Storage;
                Thing carried = pawn.carryTracker?.CarriedThing;
                if (storage == null || storage.Destroyed || carried == null)
                {
                    ReleaseLease("普通补货入库目标失效");
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }

                int deposited = storage.Deposit(pawn, carried.def, job.count);
                if (deposited <= 0)
                {
                    ReleaseLease("普通补货实际入库为零");
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }

                CompleteLease();
                depositSucceeded = true;
                pawn.jobs.EndCurrentJob(JobCondition.Succeeded);
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }

        //任务未成功时立即释放租约，成功路径已经在库存变更后完成租约。
        private void CleanupLease(JobCondition condition)
        {
            if (!depositSucceeded && condition != JobCondition.Succeeded)
                ReleaseLease("普通补货任务中断");
        }

        //释放当前 Job 的地图补货租约。
        private void ReleaseLease(string reason)
        {
            if (leaseReleased)
                return;
            leaseReleased = true;
            (Storage?.MapHeld ?? pawn?.MapHeld)?.GetComponent<MapComponent_RestockTaskQueue>()?.ReleaseLease(job, reason);
        }

        //完成当前 Job 的地图补货租约。
        private void CompleteLease()
        {
            if (leaseReleased)
                return;
            leaseReleased = true;
            (Storage?.MapHeld ?? pawn?.MapHeld)?.GetComponent<MapComponent_RestockTaskQueue>()?.CompleteLease(job, "普通补货成功入库");
        }
    }
}
