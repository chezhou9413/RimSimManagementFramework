using RimWorld;
using SimManagementLib.SimMapComp;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimThingComp;
using SimManagementLib.Tool;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimAI
{
    //为货柜预约货源和目的地，按实际入库量完成补货。
    public class JobDriver_DepositToMegaStorage : JobDriver
    {
        private const TargetIndex ThingInd = TargetIndex.A;
        private const TargetIndex StorageInd = TargetIndex.B;
        private const float DepositWorkRequired = 35f;

        private Thing ToHaul => job.GetTarget(ThingInd).Thing;
        private IShopStorage Storage => ShopStorage.For(job.GetTarget(StorageInd).Thing);
        private PathEndMode DeliveryEndMode => Storage is ThingComp_StorageSales sales
            ? sales.DeliveryEndMode : Storage.InventoryInteractionEndMode;
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
            IShopStorage storage = Storage;
            Thing source = ToHaul;
            ThingDef thingDef = ReservedDef ?? source?.def;
            if (storage == null || storage.Host.Destroyed || source == null || thingDef == null)
                return false;

            if (!storage.AllowsRestockSource(source) || pawn.Map.GetComponent<MapComponent_InventoryReservations>().Available(source) < job.count) return false;
            job.plantDefToSow = thingDef;
            int wanted = job.count > 0 ? job.count : source.stackCount;
            MapComponent_RestockTaskQueue queue = pawn?.Map?.GetComponent<MapComponent_RestockTaskQueue>();
            if (queue == null || !queue.TryAcquireBulkLease(pawn, job, storage, thingDef, wanted, out int reserved))
                return false;

            if (storage is ThingComp_StorageSales sales)
            {
                var destination = job.targetC.IsValid ? job.targetC : sales.FindDeliveryTarget(pawn, source);
                int room = sales.DeliveryRoom(pawn, source, destination);
                if (room < reserved || !pawn.Reserve(sales.ReservationTarget(destination), job, 1, -1, null, errorOnFailed))
                {
                    queue.ReleaseLease(job, "宿主目的地已满或已预约");
                    return false;
                }
                job.targetC = destination;
            }
            job.count = reserved;
            if (pawn.Reserve(source, job, 1, reserved, null, errorOnFailed))
            {
                if (storage is ThingComp_StorageSales && storage.Host is IHaulEnroute route
                    && storage.Host is not ISlotGroupParent)
                    pawn.Map.enrouteManager.AddEnroute(route, pawn, thingDef, reserved);
                preReservationsMade = true;
                return true;
            }

            if (storage is ThingComp_StorageSales destinationSales)
                pawn.Map.reservationManager.Release(destinationSales.ReservationTarget(job.targetC), pawn, job);
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
            Toil checkSource = ToilMaker.MakeToil("CheckStorageSupply");
            checkSource.defaultCompleteMode = ToilCompleteMode.Instant;
            checkSource.initAction = () =>
            {
                int reserved = pawn.Map.GetComponent<MapComponent_InventoryReservations>()?.Reserved(ToHaul) ?? 0;
                if (!Storage.AllowsRestockSource(ToHaul) || RestockSourceResolver.IsRetailSupply(ToHaul)
                    || Storage is ThingComp_StorageSales sales && sales.DeliveryRoom(pawn, ToHaul, job.targetC) <= 0
                    || ToHaul.stackCount - reserved < job.count)
                    EndJobWith(JobCondition.Incompletable);
            };
            yield return checkSource;
            yield return Toils_Haul.StartCarryThing(ThingInd, false, false, false, false);
            Toil target = ToilMaker.MakeToil("ResolveStorageAccess");
            target.initAction = () =>
            {
                if (Storage is not ThingComp_StorageSales) job.SetTarget(TargetIndex.C, Storage.InventoryInteractionTarget);
            };
            target.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return target;
            yield return Toils_Goto.GotoThing(TargetIndex.C, DeliveryEndMode)
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
                pawn.rotationTracker.FaceTarget(Storage.Host);
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
                IShopStorage storage = Storage;
                Thing carried = pawn.carryTracker?.CarriedThing;
                if (storage == null || storage.Host.Destroyed || carried == null
                    || !pawn.CanReachImmediate(job.targetC, DeliveryEndMode))
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
            if (job.targetB.Thing is IHaulEnroute route && job.targetB.Thing is not ISlotGroupParent && Storage is ThingComp_StorageSales)
                pawn.MapHeld?.enrouteManager.ReleaseFor(route, pawn);
            if (!depositSucceeded && condition != JobCondition.Succeeded)
                ReleaseLease("普通补货任务中断");
        }

        //释放当前 Job 的地图补货租约。
        private void ReleaseLease(string reason)
        {
            if (leaseReleased)
                return;
            leaseReleased = true;
            (Storage?.Host.MapHeld ?? pawn?.MapHeld)?.GetComponent<MapComponent_RestockTaskQueue>()?.ReleaseLease(job, reason);
        }

        //完成当前 Job 的地图补货租约。
        private void CompleteLease()
        {
            if (leaseReleased)
                return;
            leaseReleased = true;
            (Storage?.Host.MapHeld ?? pawn?.MapHeld)?.GetComponent<MapComponent_RestockTaskQueue>()?.CompleteLease(job, "普通补货成功入库");
        }
    }
}
