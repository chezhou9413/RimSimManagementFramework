using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.SimThingClass;
using SimManagementLib.Tool;
using UnityEngine;
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
                failReason = SimTranslation.T("RSMF.ShopService.Fail.ToiletUnavailable");
                return false;
            }
            if (!provider.Spawned || customer.Map != provider.Map)
            {
                failReason = SimTranslation.T("RSMF.ShopService.Fail.ToiletWrongMap");
                return false;
            }

            IntVec3 cell = GetUseCell(provider);
            if (!cell.IsValid || !cell.InBounds(provider.Map))
            {
                failReason = SimTranslation.T("RSMF.ShopService.Fail.ToiletCellInvalid");
                return false;
            }
            if (!cell.Standable(provider.Map))
            {
                failReason = SimTranslation.T("RSMF.ShopService.Fail.ToiletCellNotStandable");
                return false;
            }
            if (!CustomerSafetyUtility.CanCustomerReach(customer, cell, PathEndMode.OnCell, Danger.Deadly))
            {
                failReason = SimTranslation.T("RSMF.ShopService.Fail.CustomerCannotReachToilet");
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

    /// <summary>
    /// 收藏品展台参观服务执行器，职责是按展品数量计算参观价格并让顾客面向展台停留。
    /// </summary>
    public class ShopServiceWorker_CollectibleDisplayStandVisit : ShopServiceWorker
    {
        private const float SilverPerDisplayedCollectible = 20f;

        /// <summary>
        /// 判断顾客是否能参观展台，职责是绕开展台固定交互格并改用周围任意可站立格。
        /// </summary>
        public override bool CanUse(Pawn customer, Thing provider, SimZone.Zone_Shop shop, out string failReason)
        {
            failReason = "";
            if (customer == null || provider == null || provider.Destroyed || provider.Map == null)
            {
                failReason = SimTranslation.T("RSMF.ShopService.Fail.ProviderUnavailable");
                return false;
            }
            if (!provider.Spawned || customer.Map != provider.Map)
            {
                failReason = SimTranslation.T("RSMF.ShopService.Fail.ProviderWrongMap");
                return false;
            }
            Building_CollectibleDisplayStand stand = provider as Building_CollectibleDisplayStand;
            if (stand == null || stand.DisplayedCollectibleCount <= 0)
            {
                failReason = SimTranslation.T("RSMF.ShopService.Fail.DisplayStandEmpty");
                return false;
            }
            if (!TryFindUseCell(customer, stand, out _))
            {
                failReason = SimTranslation.T("RSMF.ShopService.Fail.CustomerCannotReach");
                return false;
            }

            return true;
        }

        /// <summary>
        /// 生成展台参观 Job，职责是把目标格设置为展台周围当前可达的站立格。
        /// </summary>
        public override Job MakeUseJob(Pawn customer, Thing provider, CustomerServiceOrder order)
        {
            if (provider == null || order == null) return null;
            JobDef jobDef = def?.useJobDef ?? DefDatabase<JobDef>.GetNamedSilentFail("Customer_UsePaidService");
            if (jobDef == null) return null;

            IntVec3 useCell = TryFindUseCell(customer, provider, out IntVec3 foundCell)
                ? foundCell
                : GetUseCell(provider);
            Job job = JobMaker.MakeJob(jobDef, provider, useCell);
            job.count = order.orderId;
            return job;
        }

        /// <summary>
        /// 返回参观价格，职责是按当前展品数量每件 20 白银动态计费。
        /// </summary>
        public override float GetPrice(Pawn customer, Thing provider, SimZone.Zone_Shop shop)
        {
            if (ShopServiceUtility.TryGetExplicitServicePrice(provider, def, out float overridePrice))
                return overridePrice;

            Building_CollectibleDisplayStand stand = provider as Building_CollectibleDisplayStand;
            int count = Mathf.Max(0, stand?.DisplayedCollectibleCount ?? 0);
            return Mathf.Max(0f, count * SilverPerDisplayedCollectible);
        }

        /// <summary>
        /// 在参观开始时把展品数量写入订单展示名，职责是让 AI 点评快照能明确描述本次参观内容。
        /// </summary>
        public override void NotifyServiceStarted(Pawn customer, Thing provider, CustomerServiceOrder order)
        {
            if (order == null)
                return;

            Building_CollectibleDisplayStand stand = provider as Building_CollectibleDisplayStand;
            int count = Mathf.Max(0, stand?.DisplayedCollectibleCount ?? 0);
            string label = provider?.LabelCap ?? def?.DisplayLabel ?? "收藏品展台";
            order.providerLabel = label + "（" + count + "件展品）";
        }

        /// <summary>
        /// 在参观期间让顾客面向展台中心。
        /// </summary>
        public override void TickServiceUse(Pawn customer, Thing provider, CustomerServiceOrder order)
        {
            if (customer == null || provider == null)
                return;

            customer.rotationTracker.FaceTarget(provider);
        }

        /// <summary>
        /// 查找顾客可达的参观站位，职责是优先使用交互格，失败时遍历展台外圈格子。
        /// </summary>
        private static bool TryFindUseCell(Pawn customer, Thing provider, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            if (customer == null || provider == null || provider.Map == null)
                return false;

            IntVec3 interactionCell = provider is Building building ? building.InteractionCell : IntVec3.Invalid;
            if (IsUsableCell(customer, provider.Map, interactionCell))
            {
                cell = interactionCell;
                return true;
            }

            foreach (IntVec3 adjacent in GenAdj.CellsAdjacent8Way(provider))
            {
                if (!IsUsableCell(customer, provider.Map, adjacent))
                    continue;

                cell = adjacent;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 判断格子是否能作为参观站位，职责是统一边界、站立和寻路检查。
        /// </summary>
        private static bool IsUsableCell(Pawn customer, Map map, IntVec3 cell)
        {
            return cell.IsValid
                && cell.InBounds(map)
                && cell.Standable(map)
                && !cell.IsForbidden(customer)
                && CustomerSafetyUtility.CanCustomerReach(customer, cell, PathEndMode.OnCell, Danger.Deadly);
        }
    }
}
