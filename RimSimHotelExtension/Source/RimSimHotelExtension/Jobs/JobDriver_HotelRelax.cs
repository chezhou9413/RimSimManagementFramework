using System.Collections.Generic;
using RimSimHotelExtension.Tool;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimSimHotelExtension.Jobs
{
    //让住客走到休闲位置后坐下或停留，不要求访客具有殖民者的娱乐需求。
    public sealed class JobDriver_HotelRelax : JobDriver
    {
        //区分散步和桌边休息，任务栏与住客页面保持一致。
        public override string GetReport() => HotelText.T(job.targetB.IsValid
            ? "RSH.Text.RelaxingAtTheHotel" : "RSH.Text.WalkingAroundTheHotel");

        //提前保留目的地，避免多名住客同时选择同一把椅子或空格。
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (!pawn.Reserve(job.targetA, job, errorOnFailed: errorOnFailed)) return false;
            pawn.Map.pawnDestinationReservationManager.Reserve(pawn, job, job.targetA.Cell);
            return true;
        }

        //短暂休息后重新检查吃饭、睡觉、消费和退房。
        protected override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            Toil relax = Toils_General.Wait(Rand.RangeInclusive(300, 600), job.targetB.IsValid ? TargetIndex.B : TargetIndex.None);
            relax.socialMode = RandomSocialMode.SuperActive;
            relax.AddPreTickAction(() =>
            {
                pawn.GainComfortFromCellIfPossible(1);
                pawn.needs.joy?.GainJoy(job.def.joyGainRate * 0.36f / 2500f, job.def.joyKind);
            });
            yield return relax;
        }
    }
}
