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
        private const int QueueJoinRadiusSquared = 64;
        private const int QueueDropRadiusSquared = 196;
        private int servedTicks;
        private int waitStartTick = -1;
        private int serviceTicksAtWaitStart;
        private bool joinedQueue;
        private Building_HotelDesk Desk => job.targetA.Thing as Building_HotelDesk;
        private IntVec3 ServiceCell => job.targetC.Cell;

        //允许多位顾客同时申请，走到前台附近后再加入队列，轮到队首时才预约服务格。
        public override bool TryMakePreToilReservations(bool errorOnFailed)
            => Desk?.Spawned == true;

        //保存已经得到的服务时间，读档后不会重置队列等待。
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref servedTicks, "servedTicks");
            Scribe_Values.Look(ref waitStartTick, "waitStartTick", -1);
            Scribe_Values.Look(ref serviceTicksAtWaitStart, "serviceTicksAtWaitStart");
            Scribe_Values.Look(ref joinedQueue, "joinedQueue");
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
            service.initAction = () => DriveReception(true);
            service.tickAction = () => DriveReception(false);
            yield return service;
        }

        //先走到前台附近再加入队列，避免地图边缘刚进图的住客远程占住队首和服务格；排队或无人值守时统一显示耐心条。
        private void DriveReception(bool started)
        {
            var manager = HotelMapComponent.For(pawn.Map);
            var stay = manager.FindStay(pawn);
            if (stay == null) { EndJobWith(JobCondition.Succeeded); return; }
            if (!CheckoutQueueCellUtility.IsServiceCellStructurallyUsable(pawn.Map, ServiceCell, pawn))
            {
                EndJobWith(JobCondition.Incompletable);
                return;
            }

            int now = Find.TickManager.TicksGame;
            int timeout = Mathf.Max(1, HotelTuningDef.Current.receptionTimeout);
            int distSq = pawn.Position.DistanceToSquared(ServiceCell);
            if (joinedQueue && distSq > QueueDropRadiusSquared)
            {
                joinedQueue = false;
                SimShopCheckoutApi.LeaveQueue(pawn);
                ShopProgressBarUtility.Clear(pawn);
            }
            if (!joinedQueue && distSq > QueueJoinRadiusSquared)
            {
                if (!stay.active && !stay.closing && now - stay.startTick > timeout * 3)
                {
                    manager.RequestCheckout(stay, "RSH.Text.ReceptionWaitTimedOut");
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                if (started || !pawn.pather.Moving || pawn.IsHashIntervalTick(60))
                {
                    if (!pawn.CanReach(ServiceCell, PathEndMode.OnCell, Danger.Some))
                    {
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }
                    if (!pawn.pather.Moving || pawn.pather.Destination.Cell != ServiceCell)
                        pawn.pather.StartPath(ServiceCell, PathEndMode.OnCell);
                }
                return;
            }

            if (!joinedQueue)
            {
                joinedQueue = true;
                if (waitStartTick < 0)
                {
                    waitStartTick = now;
                    serviceTicksAtWaitStart = Desk.CustomerServiceTicks;
                }
                if (!stay.active && !stay.closing && stay.receptionArrivedTick < 0)
                {
                    stay.receptionDesk = Desk;
                    stay.receptionArrivedTick = now;
                    stay.receptionServiceTicksAtStart = Desk.CustomerServiceTicks;
                }
                else if (stay.closing && stay.receptionDesk != Desk)
                {
                    stay.receptionDesk = Desk;
                    stay.checkoutServiceTicksAtStart = Desk.CustomerServiceTicks;
                }
                started = true;
            }

            if (!SimShopCheckoutApi.TryJoinQueue(pawn, Desk, out int ahead))
            {
                EndJobWith(JobCondition.Incompletable);
                return;
            }

            bool canUseServiceCell = ahead == 0
                && CheckoutQueueCellUtility.TryReserveServiceCell(Desk, ServiceCell, pawn, job);
            if (ahead > 0 && pawn.HasReserved(ServiceCell, job))
                pawn.Map.reservationManager.Release(ServiceCell, pawn, job);

            int effectiveWaitStart = !stay.active && !stay.closing && stay.receptionArrivedTick >= 0
                ? stay.receptionArrivedTick : waitStartTick;
            int effectiveServiceStart = !stay.active && !stay.closing && stay.receptionArrivedTick >= 0
                ? stay.receptionServiceTicksAtStart : serviceTicksAtWaitStart;
            int waited = Desk.GetUnservedWaitTicks(effectiveWaitStart, effectiveServiceStart);
            bool atServiceCell = canUseServiceCell && pawn.Position == ServiceCell;
            bool activelyServed = atServiceCell && Desk.IsManned;

            if (!activelyServed && waited > timeout)
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

            if (atServiceCell)
            {
                pawn.pather.StopDead();
                pawn.rotationTracker.FaceTarget(Desk.CurrentCashier ?? (Thing)Desk);
            }

            if (!activelyServed)
            {
                if (started || pawn.IsHashIntervalTick(5))
                    ShopProgressBarUtility.Report(pawn, Mathf.Clamp01(waited / (float)timeout),
                        new Color(0.95f, 0.72f, 0.36f, 0.95f));
                return;
            }

            int needed = stay.active || stay.closing ? HotelTuningDef.Current.returnReceptionTicks : HotelTuningDef.Current.receptionTicks;
            if (started || pawn.IsHashIntervalTick(5))
                ShopProgressBarUtility.Report(pawn, servedTicks / (float)Mathf.Max(1, needed));
            if (started) return;
            Desk.NotifyCustomerService();
            servedTicks++;
            if (servedTicks < needed) return;
            manager.CompleteReception(pawn);
            ReadyForNextToil();
        }
    }
}
