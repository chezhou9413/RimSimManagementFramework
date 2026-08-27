using RimWorld;
using SimManagementLib.SimDef;
using Verse;

namespace SimManagementLib.SimAI.CustomerVisit
{
    //类职责：为顾客所有访问阶段提供统一进度、总期限、结账期限和异常状态看门狗。
    public partial class CustomerVisitSession
    {
        private const int NoProgressRecoveryTicks = 600;
        private const int MaxRecoveryCount = 2;
        private const int CheckoutJobRecoveryTicks = 600;
        private const int PostCheckoutTimeoutTicks = 600;
        private const int LeavingNoProgressTicks = 600;
        private const int UnsafeGraceTicks = 300;

        //检查顾客可靠性期限，职责是让任何阶段都无法永久滞留。
        private CustomerVisitTickResult EvaluateReliabilityWatchdog(LordJob_CustomerVisit visit, Pawn pawn)
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            ObserveProgress(pawn, now);
            TrackUnsafeState(pawn, now);
            if (IsMovementUnsafe(pawn, out string unsafeReason))
            {
                if (now - unsafeSinceTick >= UnsafeGraceTicks)
                {
                    visit.CleanupUnpaidCustomerStateForSession(pawn, unsafeReason);
                    SetStage(visit, pawn, CustomerVisitStage.Leaving, unsafeReason, true);
                    return CustomerVisitTickResult.ForceExit(unsafeReason);
                }
            }

            ShoppingBehaviorProps behavior = visit.GetShoppingBehavior();
            if (behavior.maxTotalVisitTicks > 0 && now - totalVisitStartTick >= behavior.maxTotalVisitTicks && stage < CustomerVisitStage.Leaving)
                return RequestReliableLeave(visit, pawn, "顾客总行程达到期限");

            if (pawn?.needs?.food != null && pawn.needs.food.CurCategory >= HungerCategory.UrgentlyHungry && stage < CustomerVisitStage.Leaving)
                return RequestReliableLeave(visit, pawn, "顾客饥饿过重");

            if (stage == CustomerVisitStage.Leaving)
            {
                if (exitRequestedTick < 0) exitRequestedTick = now;
                if (now - lastProgressTick < LeavingNoProgressTicks)
                    return default(CustomerVisitTickResult);
                if (recoveryCount <= 0)
                {
                    recoveryCount++;
                    lastProgressTick = now;
                    lastReason = "顾客离店无进展，重新下发离图职责";
                    return CustomerVisitTickResult.Recover(lastReason);
                }
                return CustomerVisitTickResult.ForceExit("顾客重新取得离图职责后仍无进展");
            }

            //等待结账表示该顾客已准备好，但同一批次的其他顾客可能仍在购物，不能按个人等待时间清账离店。
            if (stage == CustomerVisitStage.WaitingCheckout)
                return default(CustomerVisitTickResult);

            if (stage == CustomerVisitStage.Checkout)
            {
                bool hasCheckoutJob = pawn?.CurJobDef?.defName == "Customer_PayAtRegister";
                int stageTicks = now - lastStageChangeTick;
                if (!hasCheckoutJob)
                {
                    if (now - lastProgressTick < CheckoutJobRecoveryTicks)
                        return default(CustomerVisitTickResult);
                    if (recoveryCount >= MaxRecoveryCount)
                        return RequestReliableLeave(visit, pawn, "结账工作两次恢复后仍无法取得");

                    recoveryCount++;
                    lastProgressTick = now;
                    lastReason = "结账工作中断，重新下发结账职责";
                    return CustomerVisitTickResult.Recover(lastReason);
                }
                int deadline = visit.GetQueuePatienceForPawn(pawnId) + NoProgressRecoveryTicks;
                if (hasCheckoutJob && stageTicks >= deadline)
                    return RequestReliableLeave(visit, pawn, "顾客结账总期限已到");
                return default(CustomerVisitTickResult);
            }

            if (stage == CustomerVisitStage.PostCheckout)
            {
                if (now - lastProgressTick >= PostCheckoutTimeoutTicks)
                {
                    visit.MarkPostCheckoutCompleted(pawnId);
                    SetStage(visit, pawn, CustomerVisitStage.Leaving, "购后行为无进展，结束剩余行为", true);
                    return CustomerVisitTickResult.Leave(lastReason);
                }
                return default(CustomerVisitTickResult);
            }

            if (now - lastProgressTick < NoProgressRecoveryTicks)
                return default(CustomerVisitTickResult);
            if (recoveryCount >= MaxRecoveryCount)
                return RequestReliableLeave(visit, pawn, "顾客两次恢复后仍无进展");

            recoveryCount++;
            lastProgressTick = now;
            lastReason = "顾客无进展，重新下发当前职责";
            return CustomerVisitTickResult.Recover(lastReason);
        }

        //记录异常状态起点，职责是以 60 tick 轻量频率保证 300 tick 宽限不被分片巡检额外拉长。
        internal void TrackUnsafeState(Pawn pawn, int now)
        {
            if (IsMovementUnsafe(pawn, out _))
            {
                if (unsafeSinceTick < 0) unsafeSinceTick = now;
            }
            else unsafeSinceTick = -1;
        }

        //观察位置和 Job 变化，职责是只把实际移动或行为切换视为有效进展。
        private void ObserveProgress(Pawn pawn, int now)
        {
            if (pawn == null) return;
            int jobLoadId = pawn.CurJob?.loadID ?? -1;
            bool moved = !lastProgressCell.IsValid || pawn.Position != lastProgressCell;
            bool jobChanged = jobLoadId != lastObservedJobLoadId;
            if (!moved && !jobChanged) return;
            lastProgressCell = pawn.Position;
            lastObservedJobLoadId = jobLoadId;
            if (!moved && recoveryCount > 0)
                return;
            lastProgressTick = now;
            if (moved)
                recoveryCount = 0;
        }

        //请求可靠离店，职责是幂等退回未付款商品、清账并进入自然离店阶段。
        private CustomerVisitTickResult RequestReliableLeave(LordJob_CustomerVisit visit, Pawn pawn, string reason)
        {
            visit.CleanupUnpaidCustomerStateForSession(pawn, reason);
            SetStage(visit, pawn, CustomerVisitStage.Leaving, reason, true);
            return CustomerVisitTickResult.Leave(reason);
        }

        //判断顾客是否处于无法自行完成流程的状态，职责是给倒地、精神状态和失去移动能力统一宽限。
        private static bool IsMovementUnsafe(Pawn pawn, out string reason)
        {
            reason = "";
            if (pawn == null) return false;
            if (pawn.Downed) reason = "顾客倒地超过宽限时间";
            else if (pawn.InMentalState) reason = "顾客精神状态超过宽限时间";
            else if (pawn.health?.capacities != null && !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Moving))
                reason = "顾客无法移动超过宽限时间";
            return !string.IsNullOrEmpty(reason);
        }
    }
}
