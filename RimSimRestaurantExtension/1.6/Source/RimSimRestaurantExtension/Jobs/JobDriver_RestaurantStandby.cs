using RimSimRestaurantExtension.Tool;
using SimManagementLib.Api;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace RimSimRestaurantExtension.Jobs
{
    //让餐厅员工在店铺内短暂值班，负责限制空闲厨师和服务员离开工作区域。
    public class JobDriver_RestaurantStandby : JobDriver
    {
        private const int StandbyTicks = 360;

        //预约值班格，负责避免多个员工堆在同一格。
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA.Cell, job, 1, -1, null, errorOnFailed);
        }

        //构建前往店内值班点并短暂停留的流程。
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => RestaurantOrderUtility.HasBlockingPriorityJobOrNeed(pawn));
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);

            Toil wait = Toils_General.Wait(StandbyTicks);
            wait.tickAction += () =>
            {
                LocalTargetInfo focus = job.GetTarget(TargetIndex.B);
                if (focus.IsValid)
                    pawn.rotationTracker.FaceTarget(focus);
            };
            wait.AddFinishAction(() => SimShopUiApi.ClearPawnProgress(pawn));
            yield return wait;
        }
    }
}
