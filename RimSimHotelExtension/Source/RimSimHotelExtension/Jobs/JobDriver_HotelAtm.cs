using System.Collections.Generic;
using RimSimHotelExtension.Buildings;
using RimSimHotelExtension.Services;
using RimSimHotelExtension.Settings;
using Verse;
using Verse.AI;

namespace RimSimHotelExtension.Jobs
{
    //在通电取款机完成一次有限旅行账户转账。
    public sealed class JobDriver_HotelAtm : JobDriver
    {
        private bool completed;
        private Building_HotelAtm Atm => job.targetA.Thing as Building_HotelAtm;

        //单台机器一次只服务一名顾客。
        public override bool TryMakePreToilReservations(bool errorOnFailed)
            => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);

        //保存事务是否提交，读档不会重复扣取手续费。
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref completed, "completed");
        }

        //断电时直接中止操作，只有成功到账后才收手续费。
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => Atm?.Available != true);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);
            yield return Toils_General.Wait(HotelTuningDef.Current.atmUseTicks, TargetIndex.A);
            Toil transfer = ToilMaker.MakeToil();
            transfer.initAction = () =>
            {
                if (completed) return;
                completed = HotelAtmUtility.Withdraw(pawn, Atm, job.loadID, out string reason);
                if (!completed) EndJobWith(JobCondition.Incompletable);
            };
            yield return transfer;
        }
    }
}
