using System.Collections.Generic;
using RimSimHotelExtension.Buildings;
using RimSimHotelExtension.GameComp;
using RimSimHotelExtension.Settings;
using RimSimHotelExtension.Tool;
using SimManagementLib.Api;
using SimManagementLib.SimAI;
using SimManagementLib.Tool;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimSimHotelExtension.Jobs
{
    //在服务台另一侧排队，与值班接待员办理入住、续住或退房。
    public sealed class JobDriver_HotelReception : JobDriver
    {
        private int servedTicks;
        private int waitStartTick = -1;
        private int serviceTicksAtWaitStart;
        private Building_HotelDesk Desk => job.targetA.Thing as Building_HotelDesk;
        private IntVec3 ServiceCell => job.targetC.Cell;

        //允许多位顾客同时申请，轮到队首时才预约服务格。
        public override bool TryMakePreToilReservations(bool errorOnFailed)
            => Desk?.Spawned == true;

        //保存已经得到的服务时间，读档后不会重置队列等待。
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref servedTicks, "servedTicks");
            Scribe_Values.Look(ref waitStartTick, "waitStartTick", -1);
            Scribe_Values.Look(ref serviceTicksAtWaitStart, "serviceTicksAtWaitStart");
        }

        //先建立合同，再把住宿业务加入普通收银和温泉购票共用的队列。
        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddFinishAction(condition =>
            {
                SimShopCheckoutApi.LeaveQueue(pawn);
                ShopProgressBarUtility.Clear(pawn);
            });
            this.FailOnDespawnedOrNull(TargetIndex.A);
            Toil prepare = ToilMaker.MakeToil();
            prepare.defaultCompleteMode = ToilCompleteMode.Instant;
            prepare.initAction = () =>
            {
                var manager = HotelMapComponent.For(pawn.Map);
                if (manager.FindStay(pawn) != null) return;
                var shop = SimShopServiceApi.FindShop(pawn.Map, Desk.Position);
                if (shop == null || !manager.TryCheckInExisting(pawn, shop)) EndJobWith(JobCondition.Incompletable);
            };
            yield return prepare;
            Toil service = ToilMaker.MakeToil();
            service.defaultCompleteMode = ToilCompleteMode.Never;
            service.initAction = () =>
            {
                if (waitStartTick < 0)
                {
                    waitStartTick = Find.TickManager.TicksGame;
                    serviceTicksAtWaitStart = Desk.CustomerServiceTicks;
                }
                DriveReception(true);
            };
            service.tickAction = () => DriveReception(false);
            yield return service;
        }

        //保留队列位置，只在队首站到前台且员工在岗时累计服务时间。
        private void DriveReception(bool started)
        {
            var manager = HotelMapComponent.For(pawn.Map);
            var stay = manager.FindStay(pawn);
            if (stay == null) { EndJobWith(JobCondition.Succeeded); return; }
            if (!SimShopCheckoutApi.TryJoinQueue(pawn, Desk, out int ahead)
                || !CheckoutQueueCellUtility.IsServiceCellStructurallyUsable(pawn.Map, ServiceCell, pawn))
            {
                EndJobWith(JobCondition.Incompletable);
                return;
            }
            int waited = Desk.GetUnservedWaitTicks(waitStartTick, serviceTicksAtWaitStart);
            if (waited > HotelTuningDef.Current.receptionTimeout)
            {
                if (stay.active && !stay.closing)
                {
                    stay.renewalRequested = false;
                    stay.renewalChecked = true;
                }
                else manager.RequestCheckout(stay, "RSH.Text.ReceptionWaitTimedOut");
                EndJobWith(JobCondition.Incompletable);
                return;
            }

            bool canUseServiceCell = ahead == 0
                && CheckoutQueueCellUtility.TryReserveServiceCell(Desk, ServiceCell, pawn, job);
            if (ahead > 0 && pawn.HasReserved(ServiceCell, job))
                pawn.Map.reservationManager.Release(ServiceCell, pawn, job);
            if (started || pawn.IsHashIntervalTick(30))
            {
                IntVec3 destination = canUseServiceCell ? ServiceCell
                    : CheckoutQueueCellUtility.FindQueueCell(Desk, ServiceCell, ahead, pawn);
                if (!destination.IsValid) pawn.pather.StopDead();
                else if (pawn.Position != destination && (!pawn.pather.Moving || pawn.pather.Destination.Cell != destination))
                {
                    if (!pawn.CanReach(destination, PathEndMode.OnCell, Danger.Some))
                    {
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }
                    pawn.pather.StartPath(destination, PathEndMode.OnCell);
                }
                job.SetTarget(TargetIndex.B, destination);
            }
            if (!canUseServiceCell || pawn.Position != ServiceCell)
            {
                if (pawn.IsHashIntervalTick(5))
                    ShopProgressBarUtility.Report(pawn, waited / (float)Mathf.Max(1, HotelTuningDef.Current.receptionTimeout),
                        new Color(0.95f, 0.72f, 0.36f, 0.95f));
                return;
            }

            pawn.pather.StopDead();
            pawn.rotationTracker.FaceTarget(Desk.CurrentCashier ?? (Thing)Desk);
            int needed = stay.active || stay.closing ? HotelTuningDef.Current.returnReceptionTicks : HotelTuningDef.Current.receptionTicks;
            if (started || pawn.IsHashIntervalTick(5))
                ShopProgressBarUtility.Report(pawn, servedTicks / (float)Mathf.Max(1, needed));
            if (started || !Desk.IsManned) return;
            Desk.NotifyCustomerService();
            servedTicks++;
            if (servedTicks < needed) return;
            manager.CompleteReception(pawn);
            ReadyForNextToil();
        }
    }
}
