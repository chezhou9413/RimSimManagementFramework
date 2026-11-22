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
        private const int StandbyTicks = 120;

        //预约值班格，负责避免多个员工堆在同一格。
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA.Cell, job, 1, -1, null, errorOnFailed);
        }

        //构建前往店内值班点并短暂停留的流程，玩家强制值班不被自动需求或排班中断。
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => !job.playerForced && RestaurantOrderUtility.HasBlockingPriorityJobOrNeed(pawn));
            this.FailOnDespawnedOrNull(TargetIndex.C);
            this.FailOn(() => pawn.IsHashIntervalTick(60) && !CanContinueStandby());
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);

            //每两秒回到工作树重查订单，无订单时在原格续班，有订单时按岗位优先级接手。
            Toil wait = Toils_General.Wait(StandbyTicks, TargetIndex.B);
            wait.AddFinishAction(() => SimShopUiApi.ClearPawnProgress(pawn));
            yield return wait;
        }

        //核对岗位、区域和营业状态，让撤岗、关店或缩区及时结束自动值班。
        private bool CanContinueStandby()
        {
            Thing provider = job.GetTarget(TargetIndex.C).Thing;
            if (provider?.Spawned != true) return false;
            var shop = SimShopServiceApi.FindShop(pawn.Map, provider.Position);
            if (shop == null || !RestaurantStandbyUtility.CanContinueAt(pawn, shop, job.targetA.Cell)
                || !RestaurantOrderUtility.Settings.GetOrCreate(shop.ID).enabled)
                return false;
            if (job.playerForced)
                return SimShopStaffApi.IsAssignedToWorkGiver(shop, pawn, job.workGiverDef);
            return RestaurantOrderUtility.CanRestaurantStaffWorkAt(shop)
                && SimShopStaffApi.IsOnShiftForWorkGiver(shop, pawn, job.workGiverDef);
        }
    }
}
