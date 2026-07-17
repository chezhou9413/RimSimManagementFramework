using RimWorld;
using SimManagementLib.SimThingClass;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimAI
{
    //专业货柜补货任务，职责是搬运玩家指定的真实物品并安装到对应槽位。
    public sealed class JobDriver_RestockUniqueGoodsContainer : JobDriver
    {
        private Thing Source => job.GetTarget(TargetIndex.A).Thing;
        private Building_UniqueGoodsContainer Container => job.GetTarget(TargetIndex.B).Thing as Building_UniqueGoodsContainer;
        private int sourceThingId;

        //保存补货任务的原始来源编号，职责是让任务驱动重建后仍能定位对应货柜槽位。
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref sourceThingId, "sourceThingId", -1);
        }

        //预约精确来源物品。
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            Thing source = Source;
            sourceThingId = source?.thingIDNumber ?? -1;
            return source != null && pawn.Reserve(source, job, 1, 1, null, errorOnFailed);
        }

        //构建搬运和入柜流程。
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOnDestroyedOrNull(TargetIndex.B);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch);
            yield return Toils_Haul.StartCarryThing(TargetIndex.A, false, false, false, false);
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);

            Toil install = ToilMaker.MakeToil("RestockUniqueGoodsContainer");
            install.initAction = delegate
            {
                if (Container == null || !Container.TryInstallFromCarry(pawn, sourceThingId))
                {
                    Log.Error($"专业货柜补货安装失败：搬运者={pawn?.LabelShort ?? "无"}，来源编号={sourceThingId}，货柜={Container?.LabelCap ?? "无"}。");
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                }
            };
            install.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return install;
        }
    }
}
