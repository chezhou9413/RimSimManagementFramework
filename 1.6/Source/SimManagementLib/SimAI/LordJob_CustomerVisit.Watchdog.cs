using RimWorld;
using SimManagementLib.Api;
using SimManagementLib.GameComp;
using SimManagementLib.Pojo;
using SimManagementLib.SimDef;
using SimManagementLib.SimAI.CustomerVisit;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
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

            for (int i = lord.ownedPawns.Count - 1; i >= 0; i--)
            {
                Pawn pawn = lord.ownedPawns[i];
                if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned)
                    continue;

                CustomerVisitSession session = GetOrCreateSession(pawn);
                CustomerVisitTickResult result = session != null ? session.Tick(this, pawn) : default(CustomerVisitTickResult);
                ApplySessionTickResult(pawn, result);
            }
        }

        /// <summary>
        /// 应用 Session Tick 的状态机请求，负责让 Lord 只响应统一结果。
        /// </summary>
        private void ApplySessionTickResult(Pawn pawn, CustomerVisitTickResult result)
        {
            if (result.removeFromLord)
            {
                lord?.Notify_PawnLost(pawn, PawnLostCondition.Incapped);
                return;
            }

            if (result.requestCheckoutMemo)
                lord?.ReceiveMemo("Customer_ReadyToCheckout");
            if (result.requestNextShopMemo)
                lord?.ReceiveMemo("Customer_GoToNextShop");
            if (result.requestCheckoutCompletedMemo)
            {
                forceLeaveAfterCheckout.Add(pawn.thingIDNumber);
                checkoutState.MarkPawnReadyForCheckout(pawn.thingIDNumber);
                CheckAllCheckoutsDone();
            }
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

        /// <summary>
        /// 供 Session 安全兜底调用的未付款清理入口，负责集中复用原有退货和清账逻辑。
        /// </summary>
        internal void CleanupUnpaidCustomerStateForSession(Pawn pawn, string reason)
        {
            CleanupUnpaidCustomerState(pawn, reason);
        }
    }
}
