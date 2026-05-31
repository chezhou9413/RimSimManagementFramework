using RimWorld;
using SimManagementLib.SimThingClass;
using SimManagementLib.Tool;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimAI
{
    /// <summary>
    /// 执行收银台值班工作，负责让店员站在交互格并持续提供结账服务。
    /// </summary>
    public class JobDriver_ManCashRegister : JobDriver
    {
        private const int BaseShiftTicks = 7500;
        private Building_CashRegister Register => (Building_CashRegister)job.GetTarget(TargetIndex.A).Thing;

        /// <summary>
        /// 预约收银台建筑，负责避免多个店员同时值守同一台收银台。
        /// </summary>
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Register, job, 1, -1, null, errorOnFailed);
        }

        /// <summary>
        /// 构建值班流程，负责移动到交互格、面向收银台并训练社交技能。
        /// </summary>
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);

            Toil work = new Toil();
            work.tickAction = delegate
            {
                pawn.rotationTracker.FaceTarget(Register);
                CashierSocialUtility.TryLearnSocial(pawn, 0.05f);
                pawn.GainComfortFromCellIfPossible(1);
            };

            work.defaultCompleteMode = ToilCompleteMode.Delay;
            work.defaultDuration = Mathf.Max(900, Mathf.RoundToInt(BaseShiftTicks / Mathf.Max(0.2f, pawn.GetStatValue(StatDefOf.WorkSpeedGlobal))));
            work.FailOnCannotTouch(TargetIndex.A, PathEndMode.InteractionCell);
            work.activeSkill = () => CashierSocialUtility.HasSocialSkillRecord(pawn) ? SkillDefOf.Social : null;
            yield return work;
        }
    }
}
