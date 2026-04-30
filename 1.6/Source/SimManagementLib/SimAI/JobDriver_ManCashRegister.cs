using RimWorld;
using SimManagementLib.SimThingClass;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimAI
{
    public class JobDriver_ManCashRegister : JobDriver
    {
        private const int BaseShiftTicks = 7500;
        private Building_CashRegister Register => (Building_CashRegister)job.GetTarget(TargetIndex.A).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Register, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);

            Toil work = new Toil();
            work.tickAction = delegate
            {
                pawn.rotationTracker.FaceTarget(Register);
                pawn.skills.Learn(SkillDefOf.Social, 0.05f);
                pawn.GainComfortFromCellIfPossible(1);
            };

            work.defaultCompleteMode = ToilCompleteMode.Delay;
            work.defaultDuration = Mathf.Max(900, Mathf.RoundToInt(BaseShiftTicks / Mathf.Max(0.2f, pawn.GetStatValue(StatDefOf.WorkSpeedGlobal))));
            work.FailOnCannotTouch(TargetIndex.A, PathEndMode.InteractionCell);
            work.activeSkill = () => SkillDefOf.Social;
            yield return work;
        }
    }
}
