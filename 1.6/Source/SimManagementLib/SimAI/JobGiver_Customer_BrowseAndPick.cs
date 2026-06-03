using SimManagementLib.SimThingClass;
using SimManagementLib.SimZone;
using SimManagementLib.SimAI.CustomerVisit;
using SimManagementLib.Tool;
using SimManagementLib.Pojo;
using System.Collections.Generic;
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
        /// <summary>
        /// 尝试为顾客分配一次商品浏览或最低橱窗浏览，负责在无目标商品时及时推进结账离店。
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            var lordJob = pawn.Map.lordManager.LordOf(pawn)?.LordJob as LordJob_CustomerVisit;
            if (lordJob == null) return null;
            CustomerVisitSession session = lordJob.GetOrCreateSession(pawn);
            if (session == null)
                return null;

            // 计算该 pawn 在当前商店品质下愿意消费的剩余预算。
            int pId = pawn.thingIDNumber;
            Zone_Shop currentShop = lordJob.GetCurrentShop(pawn);
            if (!session.AllowsJobGiver(CustomerVisitStage.Browsing))
            {
                return null;
            }

            if (lordJob.IsPawnReadyForCheckout(pId))
            {
                return null;
            }

            if (lordJob.HasReachedConsumptionLimit(pId))
            {
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
                MarkReadyForNextStage(lordJob, pId);
                return null;
            }

            float remainingBudget = lordJob.GetRemainingTripBudget(pawn, shopZone);

            // 愿意消费的预算耗尽时，先保证顾客完成最低浏览体验。
            if (remainingBudget <= 0f)
            {
                return MakeWindowShopOrCheckout(pawn, lordJob, shopZone, pId);
            }

            List<ComboData> affordableCombos = CustomerShoppingMatchUtility.GetMatchingAffordableInStockCombos(shopZone, lordJob, pawn, remainingBudget);
            Building_SimContainer targetShelf = FindRandomReachableStockedStorage(pawn, lordJob, shopZone, remainingBudget, affordableCombos);

            // 没有任何买得起的货时，先保证顾客完成最低浏览体验。
            if (targetShelf == null)
            {
                return MakeWindowShopOrCheckout(pawn, lordJob, shopZone, pId);
            }

            // 顾客浏览货柜不占用原版预约，允许多人同时访问同一货柜，避免预约拥堵导致职责树落到游荡。
            lordJob.ClearBrowseWaitStartTick(pId);
            lordJob.RecordCurrentShopStorageVisit(pawn, targetShelf);

            Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Customer_BrowseAndPick"), targetShelf);
            return job;
        }

        /// <summary>
        /// 单次扫描选择一个可达且有可买内容的货柜，负责优先让顾客访问当前店还没看过的货柜。
        /// </summary>
        private static Building_SimContainer FindRandomReachableStockedStorage(Pawn pawn, LordJob_CustomerVisit lordJob, Zone_Shop shopZone, float remainingBudget, List<ComboData> affordableCombos)
        {
            Building_SimContainer unvisitedSelected = null;
            Building_SimContainer fallbackSelected = null;
            int unvisitedSeen = 0;
            int fallbackSeen = 0;
            foreach (Building_SimContainer storage in ShopDataUtility.GetStoragesInZone(shopZone))
            {
                if (!StorageHasAffordableContent(storage, pawn, lordJob, remainingBudget, affordableCombos)) continue;
                if (!pawn.CanReach(storage, PathEndMode.Touch, Danger.Deadly)) continue;

                fallbackSeen++;
                if (Rand.RangeInclusive(1, fallbackSeen) == 1)
                    fallbackSelected = storage;

                if (lordJob.HasVisitedCurrentShopStorage(pawn, storage)) continue;

                unvisitedSeen++;
                if (Rand.RangeInclusive(1, unvisitedSeen) == 1)
                    unvisitedSelected = storage;
            }
            return unvisitedSelected ?? fallbackSelected;
        }

        /// <summary>
        /// 判断货柜是否存在当前顾客可购买的商品或套餐项。
        /// </summary>
        private static bool StorageHasAffordableContent(Building_SimContainer storage, Pawn pawn, LordJob_CustomerVisit lordJob, float remainingBudget, List<ComboData> affordableCombos)
        {
            if (storage == null || storage.Destroyed || !storage.Spawned) return false;
            foreach (ThingDef def in storage.ActiveDefs)
            {
                if (storage.CountStored(def) <= 0) continue;
                if (!CustomerShoppingMatchUtility.ThingMatchesCustomer(lordJob, def)) continue;
                float unitPrice = ShopPricingUtility.GetUnitPrice(storage, def);
                CustomerPriceEvaluation price = CustomerPriceUtility.Evaluate(def, unitPrice, lordJob.GetPriceSensitivity(pawn.thingIDNumber));
                if (unitPrice <= remainingBudget && !price.rejected)
                    return true;
            }
            return !affordableCombos.NullOrEmpty() && CustomerShoppingMatchUtility.StorageHasComboItem(storage, affordableCombos);
        }

        /// <summary>
        /// 为不适合购买的顾客创建橱窗浏览 Job，完成过最低浏览后才进入结账。
        /// </summary>
        private static Job MakeWindowShopOrCheckout(Pawn pawn, LordJob_CustomerVisit lordJob, Zone_Shop shopZone, int pawnId)
        {
            if (lordJob == null) return null;
            if (lordJob.HasCompletedCurrentShopMinimumBrowse(pawn))
            {
                MarkReadyForNextStage(lordJob, pawnId);
                return null;
            }

            Job windowShopJob = MakeWindowShopJob(pawn, lordJob, shopZone);
            if (windowShopJob != null)
                return windowShopJob;

            lordJob.MarkCurrentShopBrowsed(pawn);
            MarkReadyForNextStage(lordJob, pawnId);
            return null;
        }

        /// <summary>
        /// 标记顾客进入后续阶段，负责在无匹配商品时避免重复派发橱窗浏览。
        /// </summary>
        private static void MarkReadyForNextStage(LordJob_CustomerVisit lordJob, int pawnId)
        {
            if (lordJob == null) return;
            lordJob.EnsureCustomerBill(pawnId);
            Pawn pawn = lordJob.FindOwnedPawnById(pawnId);
            if (pawn != null && !lordJob.HasAnyBill(pawnId))
            {
                lordJob.FinishZeroBillCustomerAndLeave(pawn, "顾客没有找到合适商品，离店");
                return;
            }
            lordJob.MarkPawnReadyForCheckout(pawnId);
        }

        /// <summary>
        /// 创建橱窗浏览 Job，负责在没有可购买商品或等待结账时阻止职责树落到原版游荡。
        /// </summary>
        private static Job MakeWindowShopJob(Pawn pawn, LordJob_CustomerVisit lordJob, Zone_Shop shopZone)
        {
            if (TryFindWindowShopTarget(pawn, lordJob, shopZone, out LocalTargetInfo target))
            {
                Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Customer_WindowShop"), target);
                job.count = Rand.RangeInclusive(300, 600);
                return job;
            }
            return null;
        }

        /// <summary>
        /// 查找橱窗浏览目标，负责优先让顾客靠近货架，其次进入商店区内可站立格。
        /// </summary>
        private static bool TryFindWindowShopTarget(Pawn pawn, LordJob_CustomerVisit lordJob, Zone_Shop shopZone, out LocalTargetInfo target)
        {
            target = LocalTargetInfo.Invalid;
            if (pawn?.Map == null || shopZone == null) return false;

            Building_SimContainer storageTarget = FindRandomReachableWindowStorage(pawn, lordJob, shopZone);
            if (storageTarget != null)
            {
                lordJob?.RecordCurrentShopStorageVisit(pawn, storageTarget);
                target = storageTarget;
                return true;
            }

            return TryFindRandomReachableShopCell(pawn, shopZone, out target);
        }

        /// <summary>
        /// 单次扫描选择橱窗浏览货柜，负责避免无购买目标时反复分配列表。
        /// </summary>
        private static Building_SimContainer FindRandomReachableWindowStorage(Pawn pawn, LordJob_CustomerVisit lordJob, Zone_Shop shopZone)
        {
            Building_SimContainer unvisitedSelected = null;
            Building_SimContainer fallbackSelected = null;
            int unvisitedSeen = 0;
            int fallbackSeen = 0;
            foreach (Building_SimContainer storage in ShopDataUtility.GetStoragesInZone(shopZone))
            {
                if (storage == null || storage.Destroyed || !storage.Spawned) continue;
                if (!pawn.CanReach(storage, PathEndMode.Touch, Danger.Deadly)) continue;

                fallbackSeen++;
                if (Rand.RangeInclusive(1, fallbackSeen) == 1)
                    fallbackSelected = storage;

                if (lordJob != null && lordJob.HasVisitedCurrentShopStorage(pawn, storage)) continue;

                unvisitedSeen++;
                if (Rand.RangeInclusive(1, unvisitedSeen) == 1)
                    unvisitedSelected = storage;
            }
            return unvisitedSelected ?? fallbackSelected;
        }

        /// <summary>
        /// 单次扫描选择商店内可站立格，负责给没有货柜的商店提供最低浏览目标。
        /// </summary>
        private static bool TryFindRandomReachableShopCell(Pawn pawn, Zone_Shop shopZone, out LocalTargetInfo target)
        {
            target = LocalTargetInfo.Invalid;
            IntVec3 selected = IntVec3.Invalid;
            int seen = 0;
            foreach (IntVec3 cell in shopZone.Cells)
            {
                if (!cell.IsValid || !cell.Standable(pawn.Map)) continue;
                if (!pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly)) continue;

                seen++;
                if (Rand.RangeInclusive(1, seen) == 1)
                    selected = cell;
            }

            if (!selected.IsValid) return false;
            target = selected;
            return true;
        }
    }
}
