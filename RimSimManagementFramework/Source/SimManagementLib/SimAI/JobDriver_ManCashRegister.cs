using RimWorld;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimWorkGiver;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimAI
{
    //执行常驻或交接收银值班，按 24 小时排班表、原版作息与无缝接力换班稳定守岗，不再因固定短时长走开。
    public class JobDriver_ManCashRegister : JobDriver
    {
        private const int OffDutyGraceTicks = 600;
        private const int HandoverWaitGraceTicks = 1500;
        private const int RelieverWaitMaxTicks = 180;
        private int idleTicks;
        private int offDutyWaitTicks;
        private int relieverWaitTicks;
        private Building_CashRegister Register => (Building_CashRegister)job.GetTarget(TargetIndex.A).Thing;

        //使用 2 槽共享预约协调无缝交接，平时由收银台交接状态阻止多余员工同时占用。
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            Building_CashRegister register = Register;
            if (register == null || register.Destroyed || !register.Spawned)
                return false;
            if (!register.CanAcceptStaffReservation(pawn, out bool isHandover, job.playerForced))
                return false;
            if (!pawn.Reserve(register, job, Building_CashRegister.MaxStaffReservations, 0, null, errorOnFailed))
                return false;
            bool asHandover = isHandover || job.targetB.HasThing
                || (register.GetActiveShiftCashier() != null && register.GetActiveShiftCashier() != pawn);
            register.RegisterCashierClaim(pawn, asHandover, job.playerForced);
            return true;
        }

        //交接员先走到柜台旁通知前任让位再站上交互格，随后稳守岗位直到本班结束、交接完成或紧急需求触发。
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            AddFinishAction(_ => Register?.ClearCashierClaim(pawn));

            Toil gotoInteraction = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);

            //若柜台当前空闲或自己已是主班，直接前往交互格；否则先到柜台旁完成无缝交接。
            yield return Toils_Jump.JumpIf(gotoInteraction, () =>
            {
                Pawn active = Register?.GetActiveShiftCashier();
                return active == null || active == pawn;
            });

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            Toil waitHandover = ToilMaker.MakeToil("WaitCashierHandover");
            waitHandover.defaultCompleteMode = ToilCompleteMode.Never;
            waitHandover.initAction = delegate
            {
                relieverWaitTicks = 0;
                Register?.NotifyRelieverArrived(pawn);
            };
            waitHandover.tickAction = delegate
            {
                pawn.rotationTracker.FaceTarget(Register);
                Register?.NotifyRelieverArrived(pawn);
                Pawn active = Register?.GetActiveShiftCashier();
                if (active == null || active == pawn)
                {
                    ReadyForNextToil();
                    return;
                }
                if (!job.playerForced && Register?.IsHandoverStillWanted(pawn) != true)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                relieverWaitTicks++;
                if (relieverWaitTicks >= RelieverWaitMaxTicks)
                    EndJobWith(JobCondition.Incompletable);
            };
            yield return waitHandover;

            yield return gotoInteraction;

            Toil work = ToilMaker.MakeToil("ManCashRegisterDuty");
            work.initAction = delegate
            {
                idleTicks = 0;
                offDutyWaitTicks = 0;
                if (Register == null || !Register.PromoteRelieverToActive(pawn))
                    EndJobWith(JobCondition.InterruptForced);
            };
            work.tickAction = delegate
            {
                //高频守卫单一主班与即时交接，确保同一台收银台不会出现两人同时值班。
                if (pawn.IsHashIntervalTick(5))
                {
                    if (Register.GetActiveShiftCashier() != pawn)
                    {
                        EndJobWith(JobCondition.InterruptForced);
                        return;
                    }
                    if (Register.ShouldYieldToReliever(pawn))
                    {
                        ReadyForNextToil();
                        return;
                    }
                }

                //低频核对业务、岗位、排班与交接状态，避免每 tick 扫描旅馆合同和温泉会话。
                if (pawn.IsHashIntervalTick(30))
                {
                    Zone_Shop shop = ShopStaffUtility.FindShopFor(Register);
                    bool waiting = Register.HasWaitingCustomers;

                    //被移出本店岗位名单时立即离岗（保留下班交接宽限给仍在名单内但到达下班点的员工）。
                    if (!job.playerForced && !ShopStaffUtility.AllowsPawnForCashierRoleIgnoringShift(shop, pawn))
                    {
                        EndJobWith(JobCondition.InterruptForced);
                        return;
                    }
                    if (!job.playerForced && !waiting && !ShopStaffUtility.CanCashierWorkAt(shop))
                    {
                        EndJobWith(JobCondition.InterruptForced);
                        return;
                    }

                    bool urgentNeed = !job.playerForced && (pawn.needs?.food?.CurCategory >= HungerCategory.UrgentlyHungry
                        || pawn.needs?.rest?.CurCategory >= RestCategory.VeryTired);
                    if (urgentNeed)
                    {
                        ReadyForNextToil();
                        return;
                    }

                    bool onDutyByShift = job.playerForced
                        || ShopStaffUtility.AllowsPawnForCashierHandover(shop, pawn, WorkGiver_ManCashRegister.HandoverLeadTicks);
                    bool offDuty = !job.playerForced && (!onDutyByShift || ShopStaffUtility.IsPawnOffDutyForAutomaticWork(pawn));
                    bool endingSoon = !job.playerForced
                        && ShopStaffUtility.IsCashierShiftEndingSoon(shop, pawn, WorkGiver_ManCashRegister.HandoverLeadTicks);
                    bool needReliefSoon = !job.playerForced
                        && (pawn.needs?.food?.CurCategory >= HungerCategory.Hungry
                            || pawn.needs?.rest?.CurCategory >= RestCategory.Tired);

                    bool wantHandover = offDuty || endingSoon || needReliefSoon;
                    Register.SetHandoverRequested(pawn, wantHandover);

                    //接班人已走到柜台旁时立即让出交互格，实现零空窗换班。
                    if (Register.ShouldYieldToReliever(pawn))
                    {
                        ReadyForNextToil();
                        return;
                    }

                    if (offDuty)
                    {
                        offDutyWaitTicks += 30;
                        bool relieverOnTheWay = Register.GetIncomingReliever() != null;
                        if (relieverOnTheWay)
                        {
                            if (offDutyWaitTicks >= HandoverWaitGraceTicks)
                            {
                                ReadyForNextToil();
                                return;
                            }
                        }
                        else if (!waiting || offDutyWaitTicks >= OffDutyGraceTicks)
                        {
                            ReadyForNextToil();
                            return;
                        }
                    }
                    else
                    {
                        offDutyWaitTicks = 0;
                    }

                    //临时收尾或手动跨岗补位在短暂清空队列后离开；常驻值班不再因固定短时间强退。
                    idleTicks = waiting ? 0 : idleTicks + 30;
                    if (job.count == 1 && idleTicks >= 120)
                    {
                        ReadyForNextToil();
                        return;
                    }
                }
                pawn.rotationTracker.FaceTarget(Register);
                CashierSocialUtility.TryLearnSocial(pawn, 0.05f);
                pawn.GainComfortFromCellIfPossible(1);
            };

            work.defaultCompleteMode = ToilCompleteMode.Never;
            work.FailOnCannotTouch(TargetIndex.A, PathEndMode.InteractionCell);
            work.activeSkill = () => CashierSocialUtility.HasSocialSkillRecord(pawn) ? SkillDefOf.Social : null;
            yield return work;
        }
    }
}
