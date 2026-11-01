using RimWorld;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimAI
{
    //执行常驻或临时收银值班，在顾客等待时保持接待并保留员工必要需求。
    public class JobDriver_ManCashRegister : JobDriver
    {
        private const int BaseShiftTicks = 7500;
        private int idleTicks;
        private Building_CashRegister Register => (Building_CashRegister)job.GetTarget(TargetIndex.A).Thing;

        /// <summary>
        /// 预约收银台建筑，负责避免多个店员同时值守同一台收银台。
        /// </summary>
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Register, job, 1, -1, null, errorOnFailed);
        }

        //移动到服务台员工侧值班，临时补位在队伍清空后结束。
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);

            Toil work = new Toil();
            work.tickAction = delegate
            {
                //低频查看业务需求，避免每 tick 扫描旅馆合同和温泉会话。
                if (pawn.IsHashIntervalTick(30))
                {
                    Zone_Shop shop = ShopStaffUtility.FindShopFor(Register);
                    bool waiting = Register.HasWaitingCustomers;
                    if (!waiting && !ShopStaffUtility.CanCashierWorkAt(shop))
                    {
                        EndJobWith(JobCondition.InterruptForced);
                        return;
                    }
                    //临时补位在短暂清空后离岗；正常班次到期时先办完仍在排队的业务。
                    idleTicks = waiting ? 0 : idleTicks + 30;
                    if ((job.count == 1 && idleTicks >= 120) || (ticksLeftThisToil <= 0 && !waiting)
                        || (!job.playerForced && (pawn.needs?.food?.CurCategory >= HungerCategory.UrgentlyHungry
                            || pawn.needs?.rest?.CurCategory >= RestCategory.VeryTired)))
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
            work.defaultDuration = Mathf.Max(900, Mathf.RoundToInt(BaseShiftTicks / Mathf.Max(0.2f, pawn.GetStatValue(StatDefOf.WorkSpeedGlobal))));
            work.FailOnCannotTouch(TargetIndex.A, PathEndMode.InteractionCell);
            work.activeSkill = () => CashierSocialUtility.HasSocialSkillRecord(pawn) ? SkillDefOf.Social : null;
            yield return work;
        }
    }
}
