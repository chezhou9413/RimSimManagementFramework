using SimManagementLib.Pojo;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimService
{
    /// <summary>
    /// 收费厕所服务执行器，负责让顾客站到马桶建筑自身格子上读条使用服务。
    /// </summary>
    public class ShopServiceWorker_Toilet : ShopServiceWorker
    {
        /// <summary>
        /// 判断顾客是否能站到马桶建筑格上使用服务，避免默认交互格逻辑把顾客带到马桶旁边。
        /// </summary>
        public override bool CanUse(Pawn customer, Thing provider, SimZone.Zone_Shop shop, out string failReason)
        {
            failReason = "";
            if (customer == null || provider == null || provider.Destroyed || provider.Map == null)
            {
                failReason = "马桶不可用";
                return false;
            }
            if (!provider.Spawned || customer.Map != provider.Map)
            {
                failReason = "马桶不在当前地图";
                return false;
            }

            IntVec3 cell = GetUseCell(provider);
            if (!cell.IsValid || !cell.InBounds(provider.Map))
            {
                failReason = "马桶位置无效";
                return false;
            }
            if (!cell.Standable(provider.Map))
            {
                failReason = "马桶格不可站立";
                return false;
            }
            if (!customer.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
            {
                failReason = "顾客无法到达马桶";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 生成如厕服务 Job，目标格固定为马桶建筑占用格，保证顾客显示在马桶上方读条。
        /// </summary>
        public override Job MakeUseJob(Pawn customer, Thing provider, CustomerServiceOrder order)
        {
            if (provider == null || order == null) return null;
            JobDef jobDef = def?.useJobDef ?? DefDatabase<JobDef>.GetNamedSilentFail("Customer_UsePaidService");
            if (jobDef == null) return null;

            Job job = JobMaker.MakeJob(jobDef, provider, GetUseCell(provider));
            job.count = order.orderId;
            return job;
        }

        /// <summary>
        /// 返回马桶建筑自身格子作为使用位置，而不是默认交互格。
        /// </summary>
        protected override IntVec3 GetUseCell(Thing provider)
        {
            return provider?.Position ?? IntVec3.Invalid;
        }

        /// <summary>
        /// 在如厕读条期间让顾客固定朝向屏幕下方，使玩家能正面看到 Pawn。
        /// </summary>
        public override void TickServiceUse(Pawn customer, Thing provider, CustomerServiceOrder order)
        {
            if (customer == null) return;
            customer.Rotation = Rot4.South;
        }
    }
}
