using SimManagementLib.SimThingClass;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using SimManagementLib.Pojo;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
namespace SimManagementLib.SimAI
{
    /// <summary>
    /// 为顾客分配浏览货柜并挑选商品的工作，负责按预算、库存和货柜可预约状态选择目标货柜。
    /// </summary>
    public class JobGiver_Customer_BrowseAndPick : ThinkNode_JobGiver
    {
        // 货柜全部挤满时，最多等待的 tick 数（按真实游戏 Tick 计算），超过后强制去结账
        private const int MaxWaitTicks = 1200; // 约 20 秒
        private const int MaxShelfReservations = 24;

        /// <summary>
        /// 尝试为顾客分配一次商品浏览或最低橱窗浏览，负责在无目标商品时及时推进结账离店。
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            var lordJob = pawn.Map.lordManager.LordOf(pawn)?.LordJob as LordJob_CustomerVisit;
            if (lordJob == null) return null;

            // 计算该 pawn 在当前商店品质下愿意消费的剩余预算。
            int pId = pawn.thingIDNumber;
            if (lordJob.IsPawnReadyForCheckout(pId))
                return null;

            if (lordJob.HasReachedConsumptionLimit(pId))
            {
                Zone_Shop currentShop = lordJob.GetCurrentShop(pawn);
                if (currentShop == null)
                {
                    lordJob.MarkPawnReadyForCheckout(pId);
                    return null;
                }

                return MakeWindowShopOrCheckout(pawn, lordJob, currentShop, pId);
            }

            Zone_Shop shopZone = lordJob.GetCurrentShop(pawn);
            if (shopZone == null)
            {
                lordJob.MarkPawnReadyForCheckout(pId);
                return null;
            }

            if (lordJob.HasCompletedCurrentShopMinimumBrowse(pawn)
                && (lordJob.HasReachedCurrentShopBrowseLimit(pawn) || lordJob.HasReachedCurrentShopNoProgressLimit(pawn)))
            {
                return MakeWindowShopOrCheckout(pawn, lordJob, shopZone, pId);
            }

            float remainingBudget = lordJob.GetRemainingTripBudget(pawn, shopZone);

            // 愿意消费的预算耗尽时，先保证顾客完成最低浏览体验。
            if (remainingBudget <= 0f)
            {
                return MakeWindowShopOrCheckout(pawn, lordJob, shopZone, pId);
            }

            List<ComboData> affordableCombos = CustomerShoppingMatchUtility
                .GetMatchingAffordableInStockCombos(shopZone, lordJob, remainingBudget)
                .ToList();
            bool hasAffordableCombo = affordableCombos.Any();

            // 找有库存且顾客买得起至少一件的货柜
            var allStockedStorages = ShopDataUtility.GetStoragesInZone(shopZone)
                .Where(s => s.ActiveDefs.Any(def =>
                {
                    if (s.CountStored(def) <= 0) return false;
                    if (!CustomerShoppingMatchUtility.ThingMatchesCustomer(lordJob, def)) return false;
                    // 使用统一价格规则判断预算，避免筛选价格和实际购买价格不一致。
                    float unitPrice = ShopPricingUtility.GetUnitPrice(s, def);
                    return unitPrice <= remainingBudget;
                })
                    || (hasAffordableCombo && CustomerShoppingMatchUtility.StorageHasComboItem(s, affordableCombos)))
                .Where(s => pawn.CanReach(s, PathEndMode.Touch, Danger.Deadly))
                .ToList();

            // 没有任何买得起的货时，先保证顾客完成最低浏览体验。
            if (allStockedStorages.NullOrEmpty())
            {
                if (ShopServiceUtility.TryFindServiceForCustomer(
                        pawn,
                        shopZone,
                        remainingBudget,
                        CustomerShoppingMatchUtility.GetTargetServiceCategoryIds(lordJob.RuntimeCustomerKind, lordJob.customerKind),
                        out _,
                        out _,
                        out _))
                    return null;

                return MakeWindowShopOrCheckout(pawn, lordJob, shopZone, pId);
            }

            // 过滤出当前可以预约的货柜（上限放宽，减少大量顾客时的拥堵）
            var availableStorages = allStockedStorages
                .Where(s => pawn.CanReserve(s, MaxShelfReservations))
                .ToList();

            if (availableStorages.NullOrEmpty())
            {
                // 货柜全挤满时，记录首次等待 Tick，改为真实时间判定，避免“半天都在游荡”
                int now = Find.TickManager.TicksGame;
                int waitStartTick = lordJob.GetOrInitBrowseWaitStartTick(pId, now);
                CustomerExpressionUtility.TryShowExpression(pawn, CustomerExpressionEvents.BrowseWait);

                if (now - waitStartTick >= MaxWaitTicks)
                {
                    // 等太久了，改为先做最低浏览体验，再允许放弃购物去结账。
                    lordJob.ClearBrowseWaitStartTick(pId);
                    return MakeWindowShopOrCheckout(pawn, lordJob, shopZone, pId);
                }
                // 返回 null，触发 XML 兜底游荡，稍后重试
                return null;
            }

            // 找到可用货柜，清除等待计时
            lordJob.ClearBrowseWaitStartTick(pId);

            // 随机挑一个可用货柜
            Building_SimContainer targetShelf = availableStorages.RandomElement();
            Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Customer_BrowseAndPick"), targetShelf);
            return job;
        }

        /// <summary>
        /// 为不适合购买的顾客创建橱窗浏览 Job，完成过最低浏览后才进入结账。
        /// </summary>
        private static Job MakeWindowShopOrCheckout(Pawn pawn, LordJob_CustomerVisit lordJob, Zone_Shop shopZone, int pawnId)
        {
            if (lordJob == null) return null;
            if (lordJob.HasCompletedCurrentShopMinimumBrowse(pawn))
            {
                if (!lordJob.cartValues.ContainsKey(pawnId))
                    lordJob.cartValues[pawnId] = 0f;
                lordJob.MarkPawnReadyForCheckout(pawnId);
                return null;
            }

            if (TryFindWindowShopTarget(pawn, shopZone, out LocalTargetInfo target))
                return JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Customer_WindowShop"), target);

            lordJob.MarkCurrentShopBrowsed(pawn);
            if (!lordJob.cartValues.ContainsKey(pawnId))
                lordJob.cartValues[pawnId] = 0f;
            lordJob.MarkPawnReadyForCheckout(pawnId);
            return null;
        }

        /// <summary>
        /// 查找橱窗浏览目标，负责优先让顾客靠近货架，其次进入商店区内可站立格。
        /// </summary>
        private static bool TryFindWindowShopTarget(Pawn pawn, Zone_Shop shopZone, out LocalTargetInfo target)
        {
            target = LocalTargetInfo.Invalid;
            if (pawn?.Map == null || shopZone == null) return false;

            List<Building_SimContainer> storages = ShopDataUtility.GetStoragesInZone(shopZone)
                .Where(storage => storage != null && !storage.Destroyed && storage.Spawned)
                .Where(storage => pawn.CanReach(storage, PathEndMode.Touch, Danger.Deadly))
                .ToList();
            if (!storages.NullOrEmpty())
            {
                target = storages.RandomElement();
                return true;
            }

            List<IntVec3> cells = shopZone.Cells
                .Where(cell => cell.IsValid && cell.Standable(pawn.Map))
                .Where(cell => pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
                .ToList();
            if (cells.NullOrEmpty()) return false;

            target = cells.RandomElement();
            return true;
        }
    }
}
