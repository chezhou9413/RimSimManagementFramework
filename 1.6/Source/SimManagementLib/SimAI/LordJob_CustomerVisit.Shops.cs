using RimWorld;
using SimManagementLib.GameComp;
using SimManagementLib.Pojo;
using SimManagementLib.SimDef;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimThingComp;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimAI
{
    /// <summary>
    /// 管理顾客当前店铺和跨店行程，负责选择下一家店并提供按顾客读取目标店的入口。
    /// </summary>
    public partial class LordJob_CustomerVisit
    {
        /// <summary>
        /// 获取或创建指定顾客的访问状态，负责旧存档和旧字段回退。
        /// </summary>
        public CustomerVisitState GetOrCreateVisitState(Pawn pawn)
        {
            int pawnId = pawn?.thingIDNumber ?? -1;
            if (pawnId <= 0) return null;
            EnsureStateObjects();
            if (!visitStates.TryGetValue(pawnId, out CustomerVisitState state) || state == null)
            {
                state = new CustomerVisitState
                {
                    pawnId = pawnId,
                    currentShopZoneId = targetShopZoneId,
                    currentShopCell = targetShopCell,
                    currentShopVisitStartTick = Find.TickManager?.TicksGame ?? 0,
                    totalVisitStartTick = Find.TickManager?.TicksGame ?? 0
                };
                if (targetShopZoneId >= 0)
                    state.visitedShopZoneIds.Add(targetShopZoneId);
                visitStates[pawnId] = state;
            }

            EnsureDesiredSpendRatio(state);
            if (state.totalVisitStartTick < 0)
                state.totalVisitStartTick = Find.TickManager?.TicksGame ?? 0;
            if (state.currentShopVisitStartTick < 0)
                state.currentShopVisitStartTick = Find.TickManager?.TicksGame ?? 0;
            return state;
        }

        /// <summary>
        /// 查找顾客当前目标商店，负责优先使用按顾客保存的跨店状态。
        /// </summary>
        public Zone_Shop GetCurrentShop(Pawn pawn)
        {
            if (pawn?.Map == null) return null;
            CustomerVisitState state = GetOrCreateVisitState(pawn);
            if (state == null)
                return ShopDataUtility.FindAssignedShopZone(pawn.Map, targetShopZoneId, targetShopCell);
            return ShopDataUtility.FindAssignedShopZone(pawn.Map, state.currentShopZoneId, state.currentShopCell);
        }

        /// <summary>
        /// 返回顾客当前目标格，负责为旅行、浏览和结账职责提供动态焦点。
        /// </summary>
        public IntVec3 GetCurrentShopCell(Pawn pawn)
        {
            CustomerVisitState state = GetOrCreateVisitState(pawn);
            if (state != null && state.currentShopCell.IsValid)
                return state.currentShopCell;
            return targetShopCell;
        }

        /// <summary>
        /// 记录一次当前店消费动作，并返回是否达到当前店消费次数上限。
        /// </summary>
        public bool RegisterConsumptionActionForCurrentShop(Pawn pawn)
        {
            CustomerVisitState state = GetOrCreateVisitState(pawn);
            if (state == null) return true;
            state.currentShopConsumptionActions++;
            return state.currentShopConsumptionActions >= GetShoppingBehavior().maxConsumptionActionsPerShop;
        }

        /// <summary>
        /// 判断顾客是否已经达到当前店消费次数上限。
        /// </summary>
        public bool HasReachedCurrentShopConsumptionLimit(Pawn pawn)
        {
            CustomerVisitState state = GetOrCreateVisitState(pawn);
            return state != null && state.currentShopConsumptionActions >= GetShoppingBehavior().maxConsumptionActionsPerShop;
        }

        /// <summary>
        /// 判断顾客是否已经完成当前店铺的最低浏览体验。
        /// </summary>
        public bool HasCompletedCurrentShopMinimumBrowse(Pawn pawn)
        {
            CustomerVisitState state = GetOrCreateVisitState(pawn);
            return state != null && state.currentShopMinimumBrowseDone;
        }

        /// <summary>
        /// 标记顾客已经完成当前店铺的最低浏览体验。
        /// </summary>
        public void MarkCurrentShopBrowsed(Pawn pawn)
        {
            CustomerVisitState state = GetOrCreateVisitState(pawn);
            if (state != null)
                state.currentShopMinimumBrowseDone = true;
        }

        /// <summary>
        /// 返回顾客跨店剩余预算，负责让每家店独立结账但共享总预算。
        /// </summary>
        public float GetRemainingTripBudget(Pawn pawn, Zone_Shop shopZone)
        {
            if (pawn == null) return 0f;
            CustomerVisitState state = GetOrCreateVisitState(pawn);
            int rawBudget = GetBudgetForPawn(pawn.thingIDNumber);
            float effective = GetEffectiveBudgetForPawn(pawn, shopZone);
            float spent = state?.totalSpentAcrossShops ?? 0f;
            return Mathf.Max(0f, Mathf.Min(rawBudget, effective) - spent);
        }

        /// <summary>
        /// 记录当前店已支付金额，负责跨店预算累计。
        /// </summary>
        public void RecordShopPayment(Pawn pawn, int paidSilver)
        {
            CustomerVisitState state = GetOrCreateVisitState(pawn);
            if (state != null && paidSilver > 0)
                state.totalSpentAcrossShops += paidSilver;
        }

        /// <summary>
        /// 判断顾客是否应结束当前店浏览并进入结账。
        /// </summary>
        public bool ShouldCheckoutFromCurrentShop(Pawn pawn, Zone_Shop shopZone, string reason)
        {
            if (pawn == null || shopZone == null) return true;
            ShoppingBehaviorProps behavior = GetShoppingBehavior();
            CustomerVisitState state = GetOrCreateVisitState(pawn);
            int now = Find.TickManager?.TicksGame ?? 0;
            if (state == null) return true;
            if (!shopZone.IsOpenNow()) return true;
            if (GetRemainingTripBudget(pawn, shopZone) <= 0f) return true;
            if (HasReachedDesiredSpend(pawn, state)) return true;
            if (state.currentShopConsumptionActions >= behavior.maxConsumptionActionsPerShop) return true;
            if (behavior.maxShopVisitTicks > 0 && now - state.currentShopVisitStartTick >= behavior.maxShopVisitTicks) return true;
            if (behavior.maxTotalVisitTicks > 0 && now - state.totalVisitStartTick >= behavior.maxTotalVisitTicks) return true;
            if (GetCheckoutQueueSize(pawn.Map, shopZone) >= behavior.crowdingQueueSoftLimit && Rand.Value > behavior.continueShopChance) return true;
            if (cartState.satisfactionMap.TryGetValue(pawn.thingIDNumber, out float satisfaction) && satisfaction >= behavior.satisfactionCheckoutThreshold) return true;
            if (!string.IsNullOrEmpty(reason) && Rand.Value > behavior.continueShopChance) return true;
            return false;
        }

        /// <summary>
        /// 尝试为顾客切换下一家商店，负责在当前店结账完成后继续跨店行程。
        /// </summary>
        public bool TryMovePawnToNextShop(Pawn pawn)
        {
            if (pawn?.Map == null) return false;
            ShoppingBehaviorProps behavior = GetShoppingBehavior();
            CustomerVisitState state = GetOrCreateVisitState(pawn);
            Zone_Shop currentShop = GetCurrentShop(pawn);
            if (state == null || currentShop == null) return false;
            if (behavior.maxShopsToVisit <= 1) return false;
            if (state.visitedShopZoneIds.Count >= behavior.maxShopsToVisit) return false;
            if (HasReachedDesiredSpend(pawn, state)) return false;
            if (Rand.Value > behavior.continueToNextShopChance) return false;
            if (GetRemainingTripBudgetRatio(pawn, currentShop) < behavior.nextShopMinRemainingBudgetRatio) return false;

            Zone_Shop next = FindNextShop(pawn, currentShop, state);
            if (next == null) return false;

            state.currentShopZoneId = next.ID;
            state.currentShopCell = next.Cells.FirstOrDefault();
            state.currentShopVisitStartTick = Find.TickManager?.TicksGame ?? 0;
            state.currentShopConsumptionActions = 0;
            state.currentShopMinimumBrowseDone = false;
            if (!state.visitedShopZoneIds.Contains(next.ID))
                state.visitedShopZoneIds.Add(next.ID);
            targetShopZoneId = state.currentShopZoneId;
            targetShopCell = state.currentShopCell;
            ClearCurrentShopRuntimeState(pawn.thingIDNumber);
            return true;
        }

        /// <summary>
        /// 返回顾客购物行为配置，负责为旧 Def 或空配置提供默认值。
        /// </summary>
        public ShoppingBehaviorProps GetShoppingBehavior()
        {
            return RuntimeCustomerKind?.shoppingBehavior ?? customerKind?.shoppingBehavior ?? new ShoppingBehaviorProps();
        }

        /// <summary>
        /// 从当前地图选择下一家适合的商店。
        /// </summary>
        private Zone_Shop FindNextShop(Pawn pawn, Zone_Shop currentShop, CustomerVisitState state)
        {
            List<Zone_Shop> candidates = pawn.Map.zoneManager.AllZones
                .OfType<Zone_Shop>()
                .Where(shop => shop != null && shop != currentShop)
                .Where(shop => shop.IsOpenNow())
                .Where(shop => !state.visitedShopZoneIds.Contains(shop.ID))
                .Where(shop => HasMatchingGoodsOrService(pawn, shop))
                .Where(shop => pawn.CanReach(shop.Cells.FirstOrDefault(), PathEndMode.ClosestTouch, Danger.Deadly))
                .ToList();
            if (candidates.NullOrEmpty()) return null;
            return candidates.RandomElementByWeight(shop => Mathf.Max(0.01f, ScoreNextShop(pawn, shop)));
        }

        /// <summary>
        /// 判断商店是否存在顾客可能消费的商品或服务。
        /// </summary>
        private bool HasMatchingGoodsOrService(Pawn pawn, Zone_Shop shop)
        {
            float remainingBudget = GetRemainingTripBudget(pawn, shop);
            if (remainingBudget <= 0f) return false;
            if (ShopServiceUtility.TryFindServiceForCustomer(pawn, shop, remainingBudget, RuntimeCustomerKind?.GetTargetServiceCategoryIds(), out _, out _, out _))
                return true;
            return ShopDataUtility.GetStoragesInZone(shop).Any(storage => StorageHasAffordableItem(storage, remainingBudget));
        }

        /// <summary>
        /// 判断货柜是否存在顾客买得起的商品。
        /// </summary>
        private static bool StorageHasAffordableItem(Building_SimContainer storage, float remainingBudget)
        {
            if (storage == null) return false;
            foreach (ThingDef def in storage.ActiveDefs)
            {
                if (def == null || storage.CountStored(def) <= 0) continue;
                if (ShopPricingUtility.GetUnitPrice(storage, def) <= remainingBudget)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 计算下一家店的选择权重。
        /// </summary>
        private float ScoreNextShop(Pawn pawn, Zone_Shop shop)
        {
            float score = 1f;
            GameComponent_ShopAnalyticsManager analytics = Current.Game?.GetComponent<GameComponent_ShopAnalyticsManager>();
            ShopMetricsSnapshot metrics = analytics?.GetOrEvaluateShopMetrics(shop);
            if (metrics != null)
                score += Mathf.Clamp(metrics.score, 0f, 100f) / 100f;
            score += Mathf.Clamp01((GetRemainingTripBudget(pawn, shop) / Mathf.Max(1f, GetBudgetForPawn(pawn.thingIDNumber))));
            int queue = GetCheckoutQueueSize(pawn.Map, shop);
            score *= 1f / Mathf.Max(1f, 1f + queue * 0.35f);
            float dist = (shop.Cells.FirstOrDefault() - pawn.Position).LengthHorizontal;
            score *= 1f / Mathf.Max(1f, dist / 20f);
            return score;
        }

        /// <summary>
        /// 返回当前店队列人数，用于判断拥挤度。
        /// </summary>
        private static int GetCheckoutQueueSize(Map map, Zone_Shop shop)
        {
            if (map == null || shop == null) return 0;
            int count = 0;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn?.CurJobDef == null || pawn.CurJobDef.defName != "Customer_PayAtRegister") continue;
                if (pawn.CurJob?.targetA.Thing is Building_CashRegister register && shop.Cells.Contains(register.Position))
                    count++;
            }
            return count;
        }

        /// <summary>
        /// 返回顾客剩余预算比例，负责判断是否值得前往下一家店。
        /// </summary>
        private float GetRemainingTripBudgetRatio(Pawn pawn, Zone_Shop shop)
        {
            int budget = GetBudgetForPawn(pawn.thingIDNumber);
            if (budget <= 0) return 0f;
            return Mathf.Clamp01(GetRemainingTripBudget(pawn, shop) / budget);
        }

        /// <summary>
        /// 确保顾客拥有本次行程的目标消费比例，负责让预算退出决策在读档和新访问中保持稳定。
        /// </summary>
        private void EnsureDesiredSpendRatio(CustomerVisitState state)
        {
            if (state == null || state.desiredSpendRatio > 0f) return;
            ShoppingBehaviorProps behavior = GetShoppingBehavior();
            float min = Mathf.Clamp01(behavior.desiredSpendRatioRange.min);
            float max = Mathf.Clamp01(behavior.desiredSpendRatioRange.max);
            if (max < min)
            {
                float tmp = min;
                min = max;
                max = tmp;
            }
            state.desiredSpendRatio = Mathf.Clamp(Rand.Range(min, max), 0.05f, 1f);
        }

        /// <summary>
        /// 判断顾客是否已经达到本次行程愿意消费的预算比例。
        /// </summary>
        private bool HasReachedDesiredSpend(Pawn pawn, CustomerVisitState state)
        {
            if (pawn == null || state == null) return true;
            EnsureDesiredSpendRatio(state);
            int budget = GetBudgetForPawn(pawn.thingIDNumber);
            if (budget <= 0) return true;
            float currentBill = cartValues.TryGetValue(pawn.thingIDNumber, out float value) ? value : 0f;
            return state.totalSpentAcrossShops + currentBill >= budget * state.desiredSpendRatio;
        }

        /// <summary>
        /// 清理当前店结账运行态，负责让顾客切换下一店时不携带上一店的队列标记。
        /// </summary>
        private void ClearCurrentShopRuntimeState(int pawnId)
        {
            cartState.ClearCustomerCart(pawnId);
            checkoutState.ClearPawnReadyForCheckout(pawnId);
            checkoutState.ClearCheckoutOrder(pawnId);
        }
    }
}
