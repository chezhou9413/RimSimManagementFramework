using SimManagementLib.Api;
using SimManagementLib.GameComp;
using SimManagementLib.Pojo;
using SimManagementLib.SimAI.CustomerVisit;
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
    public partial class LordJob_CustomerVisit
    {
        /// <summary>
        /// 标记顾客准备结账，并在全体活跃顾客准备完毕时推进群体状态机。
        /// </summary>
        public void MarkPawnReadyForCheckout(int pawnId)
        {
            Pawn pawn = FindOwnedPawnById(pawnId);
            CustomerVisitSession session = pawn != null ? GetOrCreateSession(pawn) : null;
            ShopCheckoutReadinessContext context = new ShopCheckoutReadinessContext
            {
                customer = pawn,
                shop = pawn != null ? GetCurrentShop(pawn) : null,
                internalVisit = this,
                pawnId = pawnId
            };
            if (!SimShopCheckoutApi.CanPawnEnterCheckout(context))
            {
                Tool.SimDebugLogger.Journey("RSMF.Checkout", "顾客准备结账被扩展暂缓", pawn, context.shop, -1);
                checkoutState.ClearPawnReadyForCheckout(pawnId);
                return;
            }

            session?.MarkReadyForCheckout(this, pawn, "顾客准备结账");
            MarkPawnReadyForCheckoutFromSession(pawnId);
            Tool.SimDebugLogger.Journey("RSMF.Checkout", "顾客已标记准备结账", pawn, context.shop, -1);

            if (ShouldEnterCheckoutPhase())
            {
                Tool.SimDebugLogger.Journey("RSMF.Checkout", "顾客结账条件满足，发送 Customer_ReadyToCheckout", pawn, context.shop, -1);
                lord?.ReceiveMemo("Customer_ReadyToCheckout");
            }
        }

        /// <summary>
        /// 从 Session 同步准备结账标记，负责避免 MarkPawnReadyForCheckout 与 Session 互相递归。
        /// </summary>
        internal void MarkPawnReadyForCheckoutFromSession(int pawnId)
        {
            checkoutState.MarkPawnReadyForCheckout(pawnId);
        }

        /// <summary>
        /// 判断顾客是否已被标记为准备结账。
        /// </summary>
        public bool IsPawnReadyForCheckout(int pawnId)
        {
            return checkoutState.IsPawnReadyForCheckout(pawnId);
        }

        /// <summary>
        /// 清除顾客准备结账标记，负责让顾客完成单店结账后可以重新浏览下一家店。
        /// </summary>
        public void ClearPawnReadyForCheckout(int pawnId)
        {
            checkoutState.ClearPawnReadyForCheckout(pawnId);
        }

        /// <summary>
        /// 获取或分配顾客的固定结账顺序。
        /// </summary>
        public int EnsureCheckoutOrder(int pawnId)
        {
            return checkoutState.EnsureCheckoutOrder(pawnId);
        }

        /// <summary>
        /// 返回顾客已分配的结账顺序。
        /// </summary>
        public int GetCheckoutOrder(int pawnId)
        {
            return checkoutState.GetCheckoutOrder(pawnId);
        }

        /// <summary>
        /// 加入付款后需要执行的 Job 队列。
        /// </summary>
        public void QueuePostCheckoutJobs(int pawnId, IEnumerable<Job> jobs)
        {
            checkoutState.QueuePostCheckoutJobs(pawnId, jobs);
        }

        /// <summary>
        /// 取出顾客下一项购后 Job。
        /// </summary>
        public bool TryTakeNextPostCheckoutJob(int pawnId, out Job job)
        {
            return checkoutState.TryTakeNextPostCheckoutJob(pawnId, out job);
        }

        /// <summary>
        /// 判断顾客是否仍需要完成购后阶段。
        /// </summary>
        public bool NeedsPostCheckoutCompletion(int pawnId)
        {
            return checkoutState.NeedsPostCheckoutCompletion(pawnId);
        }

        /// <summary>
        /// 返回指定顾客当前购后 Job 队列的简短说明，负责给顾客评价快照提供售后行为上下文。
        /// </summary>
        public string DescribePostCheckoutJobs(int pawnId)
        {
            return checkoutState.DescribePostCheckoutJobs(pawnId);
        }

        /// <summary>
        /// 标记顾客购后阶段完成，并清除服务订单。
        /// </summary>
        public void MarkPostCheckoutCompleted(int pawnId)
        {
            checkoutState.MarkPostCheckoutCompleted(pawnId);
            ClearCustomerServiceOrders(pawnId);
        }

        /// <summary>
        /// 在顾客已完成最低浏览后标记结账，负责避免未完成最低浏览时反复进入无效结账判定。
        /// </summary>
        public bool TryMarkReadyForCheckoutAfterMinimumBrowse(Pawn pawn)
        {
            if (pawn == null) return false;
            if (!HasCompletedCurrentShopMinimumBrowse(pawn))
                return false;

            int pawnId = pawn.thingIDNumber;
            EnsureCustomerBill(pawnId);
            MarkPawnReadyForCheckout(pawnId);
            return true;
        }

        /// <summary>
        /// 放弃无法完成的结账，负责在缺少可达收银台等失败场景中清账并推进离店。
        /// </summary>
        public void FailCheckoutAndLeave(Pawn pawn, string failReason)
        {
            if (pawn == null) return;

            int pawnId = pawn.thingIDNumber;
            Zone_Shop shopZone = GetCurrentShop(pawn);
            GameComponent_ShopFinanceManager finance = Current.Game?.GetComponent<GameComponent_ShopFinanceManager>();
            GameComponent_ShopAnalyticsManager analytics = Current.Game?.GetComponent<GameComponent_ShopAnalyticsManager>();
            List<CustomerCartItem> purchasedItems = GetCartItems(pawnId);
            List<FinanceLineItem> billLines = finance?.GetPendingBillLines(pawn) ?? new List<FinanceLineItem>();
            float amountOwed = GetAmountOwedForCheckout(pawnId);
            int budget = GetBudgetForPawn(pawnId);

            if (shopZone != null)
                ShopDataUtility.ReturnCartItemsToShop(shopZone, purchasedItems);

            ShopCheckoutContext context = new ShopCheckoutContext
            {
                customer = pawn,
                shop = shopZone,
                register = null,
                internalVisit = this,
                billLines = billLines,
                amountOwed = amountOwed,
                paidSilver = 0,
                timedOut = false,
                success = false,
                failReason = failReason ?? ""
            };
            SimShopCheckoutApi.NotifyCheckoutFailed(context);
            CustomerReviewSnapshotBuilder.TryEnqueueReview(pawn, this, shopZone, billLines, 0, context.failReason);

            finance?.ClearPendingBill(pawn);
            ResolveServiceOrdersOnCheckoutFailure(pawnId);
            ClearCustomerCart(pawnId);
            ClearCustomerServiceOrders(pawnId);
            CustomerExpressionUtility.TryShowExpression(pawn, CustomerExpressionEvents.CheckoutTimeout);
            ShopBubbleUtility.ShowTextBubble(pawn, context.failReason, new Color(1f, 0.72f, 0.4f));
            analytics?.RecordCheckoutResult(shopZone, 0, GetQueuePatienceForPawn(pawnId), 0, budget, success: false, timeout: true);
            CheckAllCheckoutsDone();
        }

        /// <summary>
        /// 让零账单顾客单独结束本次访问并离图，负责避免无匹配商品顾客等待整个顾客团导致长期停留。
        /// </summary>
        public void FinishZeroBillCustomerAndLeave(Pawn pawn, string reason)
        {
            if (pawn == null || lord == null) return;

            int pawnId = pawn.thingIDNumber;
            if (GetAmountOwedForCheckout(pawnId) > 0f)
            {
                MarkPawnReadyForCheckout(pawnId);
                return;
            }

            Zone_Shop shopZone = GetCurrentShop(pawn);
            CustomerVisitSession session = GetOrCreateSession(pawn);
            session?.NotifyCheckoutFailed(this, pawn, reason ?? "顾客没有待付款，结束访问");
            ClearCustomerCart(pawnId);
            ClearCustomerServiceOrders(pawnId);
            checkoutState.MarkPostCheckoutCompleted(pawnId);
            checkoutState.ClearPawnReadyForCheckout(pawnId);
            checkoutState.ClearCheckoutOrder(pawnId);
            Tool.SimDebugLogger.Journey("RSMF.Checkout", reason ?? "零账单顾客离店", pawn, shopZone, -1);

            Lord oldLord = lord;
            oldLord.Notify_PawnLost(pawn, PawnLostCondition.LeftVoluntarily);
            if (pawn.Spawned && !pawn.Dead && !pawn.Destroyed && pawn.Map != null)
                LordMaker.MakeNewLord(pawn.Faction, new LordJob_ExitMapBest(LocomotionUrgency.Walk, canDig: false, canDefendSelf: false), pawn.Map, new[] { pawn });

            if (!oldLord.ownedPawns.NullOrEmpty())
                CheckAllCheckoutsDone();
        }

        /// <summary>
        /// 判断所有仍在地图上的顾客是否都已准备结账。
        /// </summary>
        private bool AreAllActivePawnsReadyForCheckout()
        {
            if (lord?.ownedPawns == null || lord.ownedPawns.Count == 0) return true;

            for (int i = 0; i < lord.ownedPawns.Count; i++)
            {
                Pawn pawn = lord.ownedPawns[i];
                if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned) continue;
                if (!checkoutState.IsPawnReadyForCheckout(pawn.thingIDNumber))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 判断是否应进入结账阶段，负责让已有未付账单的顾客优先结账而不是继续浏览。
        /// </summary>
        internal bool ShouldEnterCheckoutPhaseForSession()
        {
            if (AreAllActivePawnsReadyForCheckout())
                return true;

            if (lord?.ownedPawns == null) return false;
            for (int i = 0; i < lord.ownedPawns.Count; i++)
            {
                Pawn pawn = lord.ownedPawns[i];
                if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned) continue;
                int pawnId = pawn.thingIDNumber;
                if (checkoutState.IsPawnReadyForCheckout(pawnId) && GetAmountOwedForCheckout(pawnId) > 0f)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 判断是否应进入结账阶段，负责让已有未付账单的顾客优先结账而不是继续浏览。
        /// </summary>
        private bool ShouldEnterCheckoutPhase()
        {
            return ShouldEnterCheckoutPhaseForSession();
        }

        /// <summary>
        /// 检查所有活跃顾客是否都完成结账和购后行为，完成时推进群体状态机离店。
        /// </summary>
        public void CheckAllCheckoutsDone()
        {
            bool allDone = true;
            foreach (Pawn pawn in lord.ownedPawns)
            {
                if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned) continue;

                int pawnId = pawn.thingIDNumber;
                float owed = GetAmountOwedForCheckout(pawnId);
                Tool.SimDebugLogger.Journey("RSMF.Checkout", $"检查结账完成 pawnId={pawnId} owed={owed}", pawn, GetCurrentShop(pawn), -1);
                if (owed > 0f)
                {
                    allDone = false;
                    break;
                }

                // 顾客必须消费完所有购后服务 Job 后才算完成本次访问。
                if (checkoutState.NeedsPostCheckoutCompletion(pawnId))
                {
                    allDone = false;
                    break;
                }
            }

            if (allDone)
            {
                Pawn pawn = FirstActivePawn();
                if (pawn != null && !ShouldForceLeaveGroupAfterCheckout() && TryMovePawnToNextShop(pawn))
                {
                    Tool.SimDebugLogger.Journey("RSMF.Checkout", "结账完成，顾客前往下一家店", pawn, GetCurrentShop(pawn), -1);
                    lord.ReceiveMemo("Customer_GoToNextShop");
                }
                else
                {
                    Tool.SimDebugLogger.Journey("RSMF.Checkout", "结账完成，顾客离店", pawn, pawn != null ? GetCurrentShop(pawn) : null, -1);
                    lord.ReceiveMemo("Customer_CheckoutCompleted");
                }
            }
        }
    }
}
