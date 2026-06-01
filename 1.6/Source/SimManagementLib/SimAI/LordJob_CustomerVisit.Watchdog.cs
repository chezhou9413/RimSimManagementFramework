using RimWorld;
using SimManagementLib.GameComp;
using SimManagementLib.Pojo;
using SimManagementLib.SimDef;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SimManagementLib.SimAI
{
    /// <summary>
    /// 监控顾客访问过程中的无效闲逛、访问超时和紧急状态，负责把顾客从卡住状态推进到离店。
    /// </summary>
    public partial class LordJob_CustomerVisit
    {
        private const int VisitWatchdogIntervalTicks = 250;
        private readonly HashSet<int> forceLeaveAfterCheckout = new HashSet<int>();

        /// <summary>
        /// 周期性检查顾客访问状态，负责在无目标商品、访问超时、饥饿或倒地时结束访问。
        /// </summary>
        public override void LordJobTick()
        {
            base.LordJobTick();
            if (Find.TickManager.TicksGame % VisitWatchdogIntervalTicks != 0) return;
            if (lord?.ownedPawns == null || lord.ownedPawns.Count == 0) return;

            List<Pawn> pawns = lord.ownedPawns.ToList();
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned)
                    continue;

                if (ShouldRemoveFromCustomerVisit(pawn, out string removeReason))
                {
                    EndVisitForPawn(pawn, removeReason, removeFromLord: true);
                    continue;
                }

                if (ShouldSendUnpaidCustomerToCheckout(pawn))
                {
                    MarkPawnReadyForCheckout(pawn.thingIDNumber);
                    continue;
                }

                if (ShouldLeaveCustomerVisit(pawn, out string leaveReason))
                    EndVisitForPawn(pawn, leaveReason, removeFromLord: false);
            }
        }

        /// <summary>
        /// 判断顾客是否已经无法继续作为顾客行动。
        /// </summary>
        private static bool ShouldRemoveFromCustomerVisit(Pawn pawn, out string reason)
        {
            reason = "";
            if (pawn == null) return false;
            if (pawn.Downed)
            {
                reason = "顾客倒地，终止本次访问";
                return true;
            }

            if (pawn.InMentalState)
            {
                reason = "顾客进入精神状态，终止本次访问";
                return true;
            }

            if (pawn.health?.capacities != null && !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Moving))
            {
                reason = "顾客无法移动，终止本次访问";
                return true;
            }

            return false;
        }

        /// <summary>
        /// 判断有未付账单的顾客是否需要立刻进入结账阶段，负责避免拿货后继续浏览或被零账单离店兜底清理。
        /// </summary>
        private bool ShouldSendUnpaidCustomerToCheckout(Pawn pawn)
        {
            if (pawn == null) return false;
            int pawnId = pawn.thingIDNumber;
            if (IsPawnReadyForCheckout(pawnId)) return false;
            if (GetAmountOwedForCheckout(pawnId) <= 0f) return false;

            CustomerVisitState state = GetOrCreateVisitState(pawn);
            if (state == null) return true;
            if (HasReachedCurrentShopBrowseLimit(pawn)) return true;
            if (HasReachedCurrentShopNoProgressLimit(pawn)) return true;

            ShoppingBehaviorProps behavior = GetShoppingBehavior();
            int now = Find.TickManager.TicksGame;
            if (behavior.maxShopVisitTicks > 0 && now - state.currentShopVisitStartTick >= behavior.maxShopVisitTicks) return true;
            if (behavior.maxTotalVisitTicks > 0 && now - state.totalVisitStartTick >= behavior.maxTotalVisitTicks) return true;
            if (pawn.needs?.food != null && pawn.needs.food.CurCategory >= HungerCategory.UrgentlyHungry) return true;

            Zone_Shop shop = GetCurrentShop(pawn);
            if (shop == null || !shop.IsOpenNow()) return true;

            float remainingBudget = GetRemainingTripBudget(pawn, shop);
            return remainingBudget <= 0f
                || !CustomerShoppingMatchUtility.ShopHasMatchingAffordableGoodsOrServices(pawn, shop, this, remainingBudget);
        }

        /// <summary>
        /// 判断顾客是否应主动离店。
        /// </summary>
        private bool ShouldLeaveCustomerVisit(Pawn pawn, out string reason)
        {
            reason = "";
            if (pawn == null) return false;

            if (pawn.needs?.food != null && pawn.needs.food.CurCategory >= HungerCategory.UrgentlyHungry)
            {
                reason = "顾客饥饿过重，提前离店";
                return true;
            }

            Zone_Shop shop = GetCurrentShop(pawn);
            if (shop == null)
            {
                reason = "目标商店不存在，提前离店";
                return true;
            }

            CustomerVisitState state = GetOrCreateVisitState(pawn);
            ShoppingBehaviorProps behavior = GetShoppingBehavior();
            int now = Find.TickManager.TicksGame;
            if (state != null && behavior.maxShopVisitTicks > 0 && now - state.currentShopVisitStartTick >= behavior.maxShopVisitTicks)
            {
                reason = "当前商店停留过久，提前离店";
                return true;
            }

            if (state != null && behavior.maxTotalVisitTicks > 0 && now - state.totalVisitStartTick >= behavior.maxTotalVisitTicks)
            {
                reason = "本次购物行程过久，提前离店";
                return true;
            }

            int pawnId = pawn.thingIDNumber;
            if (state != null && state.currentShopMinimumBrowseDone && GetAmountOwedForCheckout(pawnId) <= 0f && !NeedsPostCheckoutCompletion(pawnId))
            {
                if (HasReachedCurrentShopBrowseLimit(pawn) || HasReachedCurrentShopNoProgressLimit(pawn))
                {
                    reason = "顾客浏览多次仍没有合适商品，提前离店";
                    return true;
                }

                float remainingBudget = GetRemainingTripBudget(pawn, shop);
                if (!CustomerShoppingMatchUtility.ShopHasMatchingAffordableGoodsOrServices(pawn, shop, this, remainingBudget))
                {
                    reason = "商店没有顾客当前想买的商品或服务，提前离店";
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 结束单个顾客访问，负责清理未付款状态并让可行动顾客进入结账完成流程。
        /// </summary>
        private void EndVisitForPawn(Pawn pawn, string reason, bool removeFromLord)
        {
            if (pawn == null) return;

            CleanupUnpaidCustomerState(pawn, reason);
            CustomerExpressionUtility.TryShowExpression(pawn, CustomerExpressionEvents.BrowseNoMatch);
            if (!pawn.Downed && pawn.Spawned && !pawn.Dead)
                ShopBubbleUtility.ShowTextBubble(pawn, reason, new Color(1f, 0.72f, 0.4f));

            if (removeFromLord)
            {
                lord?.Notify_PawnLost(pawn, PawnLostCondition.Incapped);
                return;
            }

            forceLeaveAfterCheckout.Add(pawn.thingIDNumber);
            checkoutState.MarkPawnReadyForCheckout(pawn.thingIDNumber);
            lord?.ReceiveMemo("Customer_ReadyToCheckout");
            CheckAllCheckoutsDone();
        }

        /// <summary>
        /// 判断顾客是否因看门狗收尾而必须离图，负责避免紧急离店后继续跨店购物。
        /// </summary>
        private bool ShouldForceLeaveAfterCheckout(Pawn pawn)
        {
            return pawn != null && forceLeaveAfterCheckout.Contains(pawn.thingIDNumber);
        }

        /// <summary>
        /// 判断当前顾客团是否必须结束行程离图，负责让任一紧急收尾顾客阻止后续跨店。
        /// </summary>
        private bool ShouldForceLeaveGroupAfterCheckout()
        {
            if (forceLeaveAfterCheckout.Count <= 0) return false;
            if (lord?.ownedPawns == null) return true;

            for (int i = 0; i < lord.ownedPawns.Count; i++)
            {
                Pawn pawn = lord.ownedPawns[i];
                if (pawn != null && forceLeaveAfterCheckout.Contains(pawn.thingIDNumber))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 清理顾客未付款购物车、财务账单和服务订单，负责防止紧急离店后残留账单或吞物品。
        /// </summary>
        private void CleanupUnpaidCustomerState(Pawn pawn, string reason)
        {
            if (pawn == null) return;

            int pawnId = pawn.thingIDNumber;
            Zone_Shop shopZone = GetCurrentShop(pawn);
            GameComponent_ShopFinanceManager finance = Current.Game?.GetComponent<GameComponent_ShopFinanceManager>();
            List<CustomerCartItem> purchasedItems = GetCartItems(pawnId);

            if (shopZone != null)
                ShopDataUtility.ReturnCartItemsToShop(shopZone, purchasedItems);

            if (GetAmountOwedForCheckout(pawnId) > 0f)
                CustomerReviewSnapshotBuilder.TryEnqueueReview(pawn, this, shopZone, finance?.GetPendingBillLines(pawn), 0, reason);

            finance?.ClearPendingBill(pawn);
            ResolveServiceOrdersOnCheckoutFailure(pawnId);
            ClearCustomerCart(pawnId);
            ClearCustomerServiceOrders(pawnId);
            checkoutState.MarkPostCheckoutCompleted(pawnId);
            checkoutState.ClearPawnReadyForCheckout(pawnId);
            checkoutState.ClearCheckoutOrder(pawnId);
            SimDebugLogger.Journey("RSMF.CustomerWatchdog", reason, pawn, shopZone, -1);
        }
    }
}
