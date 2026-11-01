using System.Collections.Generic;
using System.Linq;
using RimSimHotelExtension.Settings;
using RimSimHotelExtension.Tool;
using SimManagementLib.SimAI;
using Verse;
using Verse.AI;

namespace RimSimHotelExtension.Spa
{
    //推进持久会话的移动、存衣和泡汤，不把关键状态藏在临时 Toil 中。
    public sealed partial class JobDriver_HotelSpa : CustomerActionJobDriverBase
    {
        private SpaSession Session => SpaActionWorker.SessionFor(actionContext);
        private SpaMapComponent Manager => SpaMapComponent.For(pawn.Map);

        //在原版任务栏中报告当前步骤，方便定位排队和归衣问题。
        public override string GetReport()
        {
            switch (Session?.stage)
            {
                case SpaSessionStage.Queued: return HotelText.T("RSH.Text.WaitingForASpaSlotAndLocker");
                case SpaSessionStage.BuyingTicket: return HotelText.T("RSH.Text.BuyingSpaTicketAtReception");
                case SpaSessionStage.GoingToLocker: return HotelText.T("RSH.Text.GoingToStoreApparelInALocker");
                case SpaSessionStage.GoingToPool: return HotelText.T("RSH.Text.GoingToTheReservedSpaSlot");
                case SpaSessionStage.Soaking: return HotelText.T("RSH.Text.BathingInTheHotSpring");
                case SpaSessionStage.ReturningToLocker: return HotelText.T("RSH.Text.ReturningToCollectApparel");
                default: return base.GetReport();
            }
        }

        //恢复后校验同一会话，不再次扣款或新建预约。
        protected override bool CanContinueAction()
        {
            SpaSession session = Session;
            return base.CanContinueAction() && session != null && !session.IsTerminal
                && session.spa != null && session.spa.Operational && !pawn.Downed && !pawn.Dead;
        }

        //单个持续 Toil 依据已保存阶段执行，读档后不依赖 Toil 局部变量。
        protected override IEnumerable<Toil> MakeActionToils()
        {
            Toil toil = new Toil { defaultCompleteMode = ToilCompleteMode.Never };
            toil.initAction = () => Drive(true);
            toil.tickAction = () => Drive(false);
            toil.AddFinishAction(() => job.swimming = false);
            toil.AddFinishAction(ReleaseTicketQueue);
            yield return toil;
        }

        //根据实际阶段推进，一次 Tick 最多执行一项状态转换。
        private void Drive(bool started)
        {
            SpaSession session = Session;
            if (session == null || session.IsTerminal)
            {
                EndJobWith(JobCondition.Incompletable);
                return;
            }
            int now = Find.TickManager.TicksGame;
            if (session.stage != SpaSessionStage.Soaking && session.stage != SpaSessionStage.Queued
                && session.stage != SpaSessionStage.BuyingTicket
                && now - session.stageTick > HotelTuningDef.Current.receptionTimeout)
            {
                Manager.Abort(session, "RSH.Text.CannotReachTheSpaOrLocker");
                EndJobWith(JobCondition.Incompletable);
                return;
            }
            switch (session.stage)
            {
                case SpaSessionStage.Queued:
                    if (now - session.createdTick > HotelTuningDef.Current.spaQueuePatienceTicks)
                    {
                        Manager.Abort(session, "RSH.Text.TimedOutWaitingForAPoolSlotOr");
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }
                    if (started || now % 60 == 0)
                    {
                        if (Manager.TryAdmit(session, job)) DriveTicketPurchase(session, true);
                        else if (!session.IsTerminal && !pawn.pather.Moving && !pawn.Position.AdjacentTo8WayOrInside(session.spa))
                        {
                            IntVec3 cell = GenAdj.CellsAdjacent8Way(session.spa)
                                .Where(c => c.Standable(pawn.Map) && pawn.CanReach(c, PathEndMode.OnCell, Danger.None))
                                .OrderBy(c => c.DistanceToSquared(pawn.Position)).FirstOrDefault(IntVec3.Invalid);
                            if (cell.IsValid) pawn.pather.StartPath(cell, PathEndMode.OnCell);
                        }
                    }
                    break;
                case SpaSessionStage.BuyingTicket:
                    DriveTicketPurchase(session, started);
                    break;
                case SpaSessionStage.GoingToLocker:
                    if (!ReachLocker(session)) break;
                    if (!session.locker.StoreClothes(pawn, session.id))
                    {
                        Manager.Abort(session, "RSH.Text.TheLockerCouldNotStoreApparel");
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }
                    session.clothesStored = true;
                    Manager.ChangeStage(session, SpaSessionStage.GoingToPool);
                    pawn.pather.StartPath(session.poolCell, PathEndMode.OnCell);
                    break;
                case SpaSessionStage.GoingToPool:
                    if (pawn.Position != session.poolCell)
                    {
                        if (!pawn.pather.Moving) pawn.pather.StartPath(session.poolCell, PathEndMode.OnCell);
                        break;
                    }
                    pawn.pather.StopDead();
                    if (!Manager.StartBathing(session)) EndJobWith(JobCondition.Incompletable);
                    break;
                case SpaSessionStage.Soaking:
                    if (pawn.Position != session.poolCell)
                    {
                        Manager.ChangeStage(session, SpaSessionStage.GoingToPool);
                        break;
                    }
                    if (++session.elapsedTicks >= session.durationTicks)
                    {
                        Manager.ChangeStage(session, SpaSessionStage.ReturningToLocker);
                        pawn.pather.StartPath(session.locker, PathEndMode.Touch);
                    }
                    break;
                case SpaSessionStage.ReturningToLocker:
                    if (!ReachLocker(session)) break;
                    ReadyForNextToil();
                    break;
            }
            UpdateBathingPose(session);
        }

        //使用原版游泳绘制隐藏水下身体并下移头部，离池或中断后立即恢复。
        private void UpdateBathingPose(SpaSession session)
        {
            job.swimming = session != null && session.stage == SpaSessionStage.Soaking
                && pawn.Position == session.poolCell && !pawn.Downed && !pawn.Dead;
            if (job.swimming)
                pawn.rotationTracker.FaceCell(SpaLayout.FacingCell(session.spa.def,
                    session.spa.Position, session.spa.Rotation, session.slot));
        }

        //移动到柜旁后才执行正常脱衣与穿衣。
        private bool ReachLocker(SpaSession session)
        {
            if (session.locker == null || !session.locker.Spawned)
            {
                Manager.Abort(session, "RSH.Text.LockerRemoved");
                EndJobWith(JobCondition.Incompletable);
                return false;
            }
            if (pawn.Position.AdjacentTo8WayOrInside(session.locker))
            {
                pawn.pather.StopDead();
                return true;
            }
            if (!pawn.pather.Moving) pawn.pather.StartPath(session.locker, PathEndMode.Touch);
            return false;
        }

        //在框架完成 Toil 内结清会话，避免提前标为结束触发全局失败检查。
        protected override void OnActionCompleted()
        {
            Manager.Complete(Session);
        }

        //外界中断立即处理托管款和实物，再交还框架取消订单。
        protected override void OnActionFailed(string reason)
        {
            Manager?.Abort(Session, reason);
            base.OnActionFailed(reason);
        }
    }
}
