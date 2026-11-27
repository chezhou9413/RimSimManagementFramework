using RimSimHotelExtension.Settings;
using SimManagementLib.Api;
using SimManagementLib.SimAI;
using SimManagementLib.Tool;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimSimHotelExtension.Spa
{
    //预留池位后到服务台排队购票，与普通收银共用队列和服务格。
    public sealed partial class JobDriver_HotelSpa
    {
        private const int TicketQueueJoinRadiusSquared = 64;
        private const int TicketQueueDropRadiusSquared = 196;
        private bool ticketQueueJoined;
        private bool ticketApproachedFromFar;

        //先走到服务台附近再加入排队队列，避免远程占住空台队首；排队与无人值守期间统一显示耐心条。
        private void DriveTicketPurchase(SpaSession session, bool started)
        {
            var desk = session.ticketDesk;
            int now = Find.TickManager.TicksGame;
            if (desk == null || !desk.Spawned || desk.Map != pawn.Map
                || session.Shop(pawn.Map)?.ContainsCell(desk.Position) != true)
            {
                FailTicketPurchase(session, "RSH.Text.SpaNeedsReceptionDesk");
                return;
            }
            if (!CheckoutQueueCellUtility.IsServiceCellStructurallyUsable(pawn.Map, session.ticketServiceCell, pawn))
            {
                FailTicketPurchase(session, "RSH.Text.CannotReachSpaTicketDesk");
                return;
            }

            int patience = Mathf.Max(1, HotelTuningDef.Current.spaQueuePatienceTicks);
            int distSq = pawn.Position.DistanceToSquared(session.ticketServiceCell);
            if (ticketQueueJoined && distSq > TicketQueueDropRadiusSquared)
            {
                ticketQueueJoined = false;
                SimShopCheckoutApi.LeaveQueue(pawn);
                ShopProgressBarUtility.Clear(pawn);
            }
            if (!ticketQueueJoined && distSq > TicketQueueJoinRadiusSquared)
            {
                ticketApproachedFromFar = true;
                if (now - session.stageTick >= HotelTuningDef.Current.receptionTimeout * 2)
                {
                    FailTicketPurchase(session, "RSH.Text.SpaTicketWaitTimedOut");
                    return;
                }
                if (started || !pawn.pather.Moving || pawn.IsHashIntervalTick(60))
                {
                    if (!pawn.CanReach(session.ticketServiceCell, PathEndMode.OnCell, Danger.None))
                    {
                        FailTicketPurchase(session, "RSH.Text.CannotReachSpaTicketDesk");
                        return;
                    }
                    if (!pawn.pather.Moving || pawn.pather.Destination.Cell != session.ticketServiceCell)
                        pawn.pather.StartPath(session.ticketServiceCell, PathEndMode.OnCell);
                }
                return;
            }

            if (!ticketQueueJoined)
            {
                ticketQueueJoined = true;
                if (ticketApproachedFromFar)
                {
                    session.stageTick = now;
                    session.ticketServiceTicksAtWaitStart = desk.CustomerServiceTicks;
                    ticketApproachedFromFar = false;
                }
                started = true;
            }

            if (!SimShopCheckoutApi.TryJoinQueue(pawn, desk, out int ahead))
            {
                FailTicketPurchase(session, "RSH.Text.CannotReachSpaTicketDesk");
                return;
            }

            bool canUseServiceCell = ahead == 0
                && CheckoutQueueCellUtility.TryReserveServiceCell(desk, session.ticketServiceCell, pawn, job);
            if (ahead > 0 && pawn.HasReserved(session.ticketServiceCell, job))
                pawn.Map.reservationManager.Release(session.ticketServiceCell, pawn, job);

            int waited = desk.GetUnservedWaitTicks(session.stageTick, session.ticketServiceTicksAtWaitStart);
            bool atServiceCell = canUseServiceCell && pawn.Position == session.ticketServiceCell;
            bool activelyServed = atServiceCell && desk.IsManned;

            if (!activelyServed && waited >= patience)
            {
                FailTicketPurchase(session, "RSH.Text.SpaTicketWaitTimedOut");
                return;
            }

            if (started || pawn.IsHashIntervalTick(15) || (canUseServiceCell && job.targetB.Cell != session.ticketServiceCell))
            {
                IntVec3 destination = canUseServiceCell ? session.ticketServiceCell
                    : CheckoutQueueCellUtility.FindQueueCell(desk, session.ticketServiceCell, ahead, pawn);
                if (!destination.IsValid)
                    pawn.pather.StopDead();
                else if (pawn.Position != destination && (!pawn.pather.Moving || pawn.pather.Destination.Cell != destination))
                {
                    if (!pawn.CanReach(destination, PathEndMode.OnCell, Danger.None))
                    {
                        FailTicketPurchase(session, "RSH.Text.CannotReachSpaTicketDesk");
                        return;
                    }
                    pawn.pather.StartPath(destination, PathEndMode.OnCell);
                }
                job.SetTarget(TargetIndex.B, destination);
            }

            if (atServiceCell)
            {
                pawn.pather.StopDead();
                pawn.rotationTracker.FaceTarget(desk.CurrentCashier ?? (Thing)desk);
            }

            if (!activelyServed)
            {
                if (started || pawn.IsHashIntervalTick(5))
                    ShopProgressBarUtility.Report(pawn, Mathf.Clamp01(waited / (float)patience),
                        new Color(0.95f, 0.72f, 0.36f, 0.95f));
                return;
            }

            desk.NotifyCustomerService();
            session.ticketServiceTicks++;
            int needed = HotelTuningDef.Current.returnReceptionTicks;
            if (started || now % 5 == 0)
                ShopProgressBarUtility.Report(pawn, session.ticketServiceTicks / (float)Mathf.Max(1, needed));
            if (session.ticketServiceTicks < needed) return;
            if (!Manager.BuyTicket(session))
            {
                if (session.IsTerminal) EndJobWith(JobCondition.Incompletable);
                return;
            }
            ReleaseTicketQueue();
            Manager.ChangeStage(session, SpaSessionStage.GoingToLocker);
            pawn.pather.StartPath(session.locker, PathEndMode.Touch);
        }

        //完成或中断时立即释放收银队列和顾客侧预约，不影响池位与衣柜。
        private void ReleaseTicketQueue()
        {
            ticketQueueJoined = false;
            ticketApproachedFromFar = false;
            SimShopCheckoutApi.LeaveQueue(pawn);
            ShopProgressBarUtility.Clear(pawn);
            SpaSession session = Session;
            if (session == null) return;
            IntVec3 serviceCell = session.ticketServiceCell;
            //购票结束后不再持有服务格，先清空会话记录，避免后续收尾或存档继续引用旧预约。
            session.ticketServiceCell = IntVec3.Invalid;
            if (serviceCell.IsValid && pawn.HasReserved(serviceCell, job))
                pawn.Map.reservationManager.Release(serviceCell, pawn, job);
        }

        //无法完成购票时统一取消会话，交还已预留的设施。
        private void FailTicketPurchase(SpaSession session, string reason)
        {
            Manager.Abort(session, reason);
            EndJobWith(JobCondition.Incompletable);
        }
    }
}
