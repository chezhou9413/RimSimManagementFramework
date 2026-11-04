using System.Collections.Generic;
using RimWorld;
using RimSimHotelExtension.GameComp;
using RimSimHotelExtension.Services;
using Verse;
using Verse.AI;

namespace RimSimHotelExtension.Jobs
{
    //复用原版床位预约和睡姿，按旅馆作息结束整晚睡眠。
    public sealed class JobDriver_HotelSleep : JobDriver_LayDown
    {
        //睡觉时不再接受普通休闲和购物调度。
        public override bool LookForOtherJobs => false;

        //去床途中也响应退房、换床和作息调整。
        protected override IEnumerable<Toil> MakeNewToils()
        {
            var manager = HotelMapComponent.For(pawn.Map);
            var stay = manager.FindStay(pawn);
            if (stay == null) yield break;
            var settings = manager.SettingsFor(stay);
            AddEndCondition(() => {
                if (stay.completed || stay.closing || !stay.active || stay.bed != Bed) return JobCondition.Succeeded;
                if ((Find.TickManager.TicksGame + pawn.thingIDNumber) % 60 == 0
                    && !HotelSleepUtility.IsSleepTime(pawn.Map, settings)) return JobCondition.Succeeded;
                return JobCondition.Ongoing;
            });
            foreach (var toil in base.MakeNewToils()) yield return toil;
        }
    }
}
