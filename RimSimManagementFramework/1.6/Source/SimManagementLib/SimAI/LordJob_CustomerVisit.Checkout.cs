using SimManagementLib.Api;
using SimManagementLib.GameComp;
using SimManagementLib.Pojo;
using SimManagementLib.SimAI.CustomerVisit;
using SimManagementLib.SimMapComp;
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
    //顾客结账协调部分，职责是管理准备状态、结账顺序和全体访问收尾。
    public partial class LordJob_CustomerVisit
    {
        //标记顾客准备结账，并在全体活跃顾客准备完毕时推进群体状态机。
        public void MarkPawnReadyForCheckout(int pawnId)
        {
            Pawn pawn = FindOwnedPawnById(pawnId);
            if (CustomerServiceActivityUtility.HasActiveService(pawn)) return;
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
        //从 Session 同步准备结账标记，负责避免 MarkPawnReadyForCheckout 与 Session 互相递归。
        internal void MarkPawnReadyForCheckoutFromSession(int pawnId)
        {
            checkoutState.MarkPawnReadyForCheckout(pawnId);
        }
        //判断顾客是否已被标记为准备结账。
        public bool IsPawnReadyForCheckout(int pawnId)
        {
            return checkoutState.IsPawnReadyForCheckout(pawnId);
        }
        //清除顾客准备结账标记，负责让顾客完成单店结账后可以重新浏览下一家店。
        public void ClearPawnReadyForCheckout(int pawnId)
        {
            checkoutState.ClearPawnReadyForCheckout(pawnId);
        }
        //获取或分配顾客的固定结账顺序。
        public int EnsureCheckoutOrder(int pawnId)
        {
            return checkoutState.EnsureCheckoutOrder(pawnId);
        }
        //返回顾客已分配的结账顺序。
        public int GetCheckoutOrder(int pawnId)
        {
            return checkoutState.GetCheckoutOrder(pawnId);
        }
        //加入付款后需要执行的 Job 队列。
        public void QueuePostCheckoutJobs(int pawnId, IEnumerable<Job> jobs)
        {
            checkoutState.QueuePostCheckoutJobs(pawnId, jobs);
        }
        //取出顾客下一项购后 Job。
        public bool TryTakeNextPostCheckoutJob(int pawnId, out Job job)
        {
            return checkoutState.TryTakeNextPostCheckoutJob(pawnId, out job);
        }
        //判断顾客是否仍需要完成购后阶段。
        public bool NeedsPostCheckoutCompletion(int pawnId)
        {
            return checkoutState.NeedsPostCheckoutCompletion(pawnId);
        }
        //返回指定顾客当前购后 Job 队列的简短说明，负责给顾客评价快照提供售后行为上下文。
        public string DescribePostCheckoutJobs(int pawnId)
        {
            return checkoutState.DescribePostCheckoutJobs(pawnId);
        }
        //标记顾客购后阶段完成，并清除服务订单。
        public void MarkPostCheckoutCompleted(int pawnId)
        {
            Pawn pawn = FindOwnedPawnById(pawnId);
            if (pawn != null)
                TryEnqueueFreeCompletedServiceReview(pawn, GetCurrentShop(pawn), "完成免费服务");
            checkoutState.MarkPostCheckoutCompleted(pawnId);
            ClearCustomerServiceOrders(pawnId);
        }
        //在顾客已完成最低浏览后标记结账，负责避免未完成最低浏览时反复进入无效结账判定。
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
        //放弃无法完成的结账，负责在缺少可达收银台等失败场景中清账并推进离店。
        public void FailCheckoutAndLeave(Pawn pawn, string failReason)
        {
            if (pawn == null) return;

            int pawnId = pawn.thingIDNumber;
            pawn.Map?.GetComponent<CustomerArrivalManager>()?.ReleaseCheckoutTicket(pawn);
            Zone_Shop shopZone = GetCurrentShop(pawn);
            GameComponent_ShopFinanceManager finance = Current.Game?.GetComponent<GameComponent_ShopFinanceManager>();
            GameComponent_ShopAnalyticsManager analytics = Current.Game?.GetComponent<GameComponent_ShopAnalyticsManager>();
            List<CustomerCartItem> purchasedItems = GetCartItems(pawnId);
            List<FinanceLineItem> billLines = finance?.GetPendingBillLines(pawn) ?? new List<FinanceLineItem>();
            float amountOwed = GetAmountOwedForCheckout(pawnId);
            int budget = GetBudgetForPawn(pawnId);

            if (amountOwed <= 0f && purchasedItems.NullOrEmpty())
            {
                GetOrCreateSession(pawn)?.NotifyCheckoutFailed(this, pawn, failReason);
                forceLeaveAfterCheckout.Add(pawnId);
                CheckAllCheckoutsDone();
                return;
            }

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
            GetOrCreateSession(pawn)?.NotifyCheckoutFailed(this, pawn, context.failReason);
            forceLeaveAfterCheckout.Add(pawnId);
            CheckAllCheckoutsDone();
        }
        //让零账单顾客单独结束本次访问并离图，负责避免无匹配商品顾客等待整个顾客团导致长期停留。
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
            ResolveServiceOrdersOnCheckoutPaid(pawn, shopZone);
            if (NeedsPostCheckoutCompletion(pawnId))
            {
                session?.NotifyCheckoutPaid(this, pawn, reason ?? "免费服务等待使用");
                CheckAllCheckoutsDone();
                return;
            }

            TryEnqueueFreeCompletedServiceReview(pawn, shopZone, reason ?? "完成免费服务");
            if (HasCompletedFreeServiceOrder(pawnId))
                session?.NotifyCheckoutPaid(this, pawn, reason ?? "完成免费服务");
            else
                session?.NotifyCheckoutFailed(this, pawn, reason ?? "顾客没有待付款，结束访问");
            ClearCustomerCart(pawnId);
            ClearCustomerServiceOrders(pawnId);
            checkoutState.MarkPostCheckoutCompleted(pawnId);
            checkoutState.ClearPawnReadyForCheckout(pawnId);
            checkoutState.ClearCheckoutOrder(pawnId);
            Tool.SimDebugLogger.Journey("RSMF.Checkout", reason ?? "零账单顾客离店", pawn, shopZone, -1);

            forceLeaveAfterCheckout.Add(pawnId);
            lord.ReceiveMemo("Customer_CheckoutCompleted");
        }
        //判断所有仍在地图上的顾客是否都已准备结账。
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
        //判断是否应进入结账阶段，负责让已有未付账单的顾客优先结账而不是继续浏览。
        internal bool ShouldEnterCheckoutPhaseForSession()
        {
            //同批顾客的先用后付服务完成前不能整体切换职责，否则尚未产生账单的顾客会被当成空逛。
            if (lord?.ownedPawns != null && lord.ownedPawns.Any(CustomerServiceActivityUtility.HasActiveService))
                return false;
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
        //判断是否应进入结账阶段，负责让已有未付账单的顾客优先结账而不是继续浏览。
        private bool ShouldEnterCheckoutPhase()
        {
            return ShouldEnterCheckoutPhaseForSession();
        }
        //检查所有活跃顾客是否都完成结账和购后行为，完成时推进群体状态机离店。
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

                //正在使用的服务和待执行购后服务都必须完成，不能只凭账单为零判定可以离店。
                if (CustomerServiceActivityUtility.HasActiveService(pawn) || checkoutState.NeedsPostCheckoutCompletion(pawnId))
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
