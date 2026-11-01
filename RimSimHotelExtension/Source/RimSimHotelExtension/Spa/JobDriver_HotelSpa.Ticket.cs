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
        //只有队首顾客站在服务格且接待员在岗时才累计购票时间。
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
            if (desk.GetUnservedWaitTicks(session.stageTick, session.ticketServiceTicksAtWaitStart)
                >= HotelTuningDef.Current.spaQueuePatienceTicks)
            {
                FailTicketPurchase(session, "RSH.Text.SpaTicketWaitTimedOut");
                return;
            }
            if (!SimShopCheckoutApi.TryJoinQueue(pawn, desk, out int ahead)
                || !CheckoutQueueCellUtility.IsServiceCellStructurallyUsable(pawn.Map, session.ticketServiceCell, pawn))
            {
                FailTicketPurchase(session, "RSH.Text.CannotReachSpaTicketDesk");
                return;
            }

            bool canUseServiceCell = ahead == 0
                && CheckoutQueueCellUtility.IsServiceCellFreeForPawn(pawn.Map, session.ticketServiceCell, pawn)
                && pawn.CanReserve(session.ticketServiceCell);
            if (ahead > 0 && pawn.HasReserved(session.ticketServiceCell, job))
                pawn.Map.reservationManager.Release(session.ticketServiceCell, pawn, job);
            if (started || now % 30 == 0 || !job.targetB.IsValid)
            {
                IntVec3 destination = canUseServiceCell ? session.ticketServiceCell
                    : CheckoutQueueCellUtility.FindQueueCell(desk, session.ticketServiceCell, ahead, pawn);
                if (canUseServiceCell && !pawn.Reserve(destination, job, errorOnFailed: false)) return;
                if (pawn.Position != destination && (!pawn.pather.Moving || job.targetB.Cell != destination))
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

            if (!canUseServiceCell || pawn.Position != session.ticketServiceCell || !desk.IsManned)
            {
                ShopProgressBarUtility.Clear(pawn);
                return;
            }
            pawn.pather.StopDead();
            pawn.rotationTracker.FaceTarget(desk.CurrentCashier);
            desk.NotifyCustomerService();
            session.ticketServiceTicks++;
            int needed = HotelTuningDef.Current.returnReceptionTicks;
            if (now % 5 == 0)
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
            SimShopCheckoutApi.LeaveQueue(pawn);
            ShopProgressBarUtility.Clear(pawn);
            //购票成功已经释放过服务格；泡汤结束和中断清理只释放当前任务仍持有的预约。
            if (Session?.ticketServiceCell.IsValid == true && pawn.HasReserved(Session.ticketServiceCell, job))
                pawn.Map.reservationManager.Release(Session.ticketServiceCell, pawn, job);
        }

        //无法完成购票时统一取消会话，交还已预留的设施。
        private void FailTicketPurchase(SpaSession session, string reason)
        {
            Manager.Abort(session, reason);
            EndJobWith(JobCondition.Incompletable);
        }
    }
}
