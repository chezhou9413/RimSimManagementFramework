using RimWorld;
using SimManagementLib.Api;
using SimManagementLib.GameComp;
using SimManagementLib.Pojo;
using SimManagementLib.SimDef;
using SimManagementLib.SimAI.CustomerVisit;
using SimManagementLib.SimThingClass;
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
    /// 执行顾客排队、等待收银员、付款、财务提交和结账失败回滚的收银台工作。
    /// </summary>
    public class JobDriver_PayAtRegister : JobDriver
    {
        private const int ServiceTicks = 300;
        private const int DefaultMaxQueueWaitTicks = 2500;

        private Building_CashRegister Register => (Building_CashRegister)job.GetTarget(TargetIndex.A).Thing;
        private IntVec3 QueueCell => job.GetTarget(TargetIndex.B).Cell;
        private IntVec3 ServiceCell => job.GetTarget(TargetIndex.C).Cell;

        private bool abortedByTimeout;
        private int totalWaitTicks;
        private int maxQueueWaitTicks = DefaultMaxQueueWaitTicks;
        private int serviceTicksRequired = ServiceTicks;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // 顾客不独占收银台建筑本体，多个顾客通过排队顺序控制结账。
            return true;
        }

        /// <summary>
        /// 构建顾客从排队到付款完成的 Toil 序列。
        /// </summary>
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);

            yield return MakeEnsureQueueAndServiceCellsToil();
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);

            Toil waitInQueue = new Toil();
            waitInQueue.defaultCompleteMode = ToilCompleteMode.Never;
            waitInQueue.initAction = () =>
            {
                abortedByTimeout = false;
                totalWaitTicks = 0;
                LordJob_CustomerVisit lordJob = pawn.Map.lordManager.LordOf(pawn)?.LordJob as LordJob_CustomerVisit;
                maxQueueWaitTicks = lordJob?.GetQueuePatienceForPawn(pawn.thingIDNumber) ?? DefaultMaxQueueWaitTicks;
                if (maxQueueWaitTicks <= 0) maxQueueWaitTicks = DefaultMaxQueueWaitTicks;
                CustomerExpressionUtility.TryShowExpression(pawn, CustomerExpressionEvents.CheckoutQueueStart);
            };
            waitInQueue.tickAction = () =>
            {
                totalWaitTicks++;
                FaceCashierOrRegister();
                ShopProgressBarUtility.Report(pawn, Mathf.Min(1f, totalWaitTicks / (float)Mathf.Max(1, maxQueueWaitTicks)), new Color(0.95f, 0.72f, 0.36f, 0.95f));

                LordJob_CustomerVisit lordJob = pawn.Map.lordManager.LordOf(pawn)?.LordJob as LordJob_CustomerVisit;
                if (lordJob == null)
                {
                    abortedByTimeout = true;
                    ReadyForNextToil();
                    return;
                }

                if (totalWaitTicks >= maxQueueWaitTicks)
                {
                    abortedByTimeout = true;
                    ReadyForNextToil();
                    return;
                }

                if (pawn.IsHashIntervalTick(90))
                {
                    UpdateQueueCell(lordJob);
                    if (QueueCell.IsValid && pawn.Position != QueueCell && CustomerSafetyUtility.CanCustomerReach(pawn, QueueCell, PathEndMode.OnCell, Danger.Deadly))
                    {
                        pawn.pather.StartPath(QueueCell, PathEndMode.OnCell);
                    }
                }

                if (!Register.IsManned) return;
                if (!IsMyTurn(lordJob)) return;
                if (!CheckoutQueueCellUtility.IsServiceCellFreeForPawn(pawn.Map, ServiceCell, pawn)) return;

                ReadyForNextToil();
            };
            waitInQueue.AddFinishAction(() => ShopProgressBarUtility.Clear(pawn));
            yield return waitInQueue;

            yield return Toils_Goto.GotoCell(TargetIndex.C, PathEndMode.OnCell);

            Toil doService = new Toil();
            doService.defaultCompleteMode = ToilCompleteMode.Never;
            doService.initAction = () =>
            {
                float cashierSpeed = GetCashierServiceSpeed();
                serviceTicksRequired = Mathf.Max(60, Mathf.RoundToInt(ServiceTicks / Mathf.Max(0.2f, cashierSpeed)));
                ticksLeftThisToil = serviceTicksRequired;
                CustomerExpressionUtility.TryShowExpression(pawn, CustomerExpressionEvents.CheckoutServiceStart);
            };
            doService.tickAction = () =>
            {
                totalWaitTicks++;
                FaceCashierOrRegister();
                ShopProgressBarUtility.Report(pawn, 1f - ticksLeftThisToil / (float)Mathf.Max(1, serviceTicksRequired));

                LordJob_CustomerVisit lordJob = pawn.Map.lordManager.LordOf(pawn)?.LordJob as LordJob_CustomerVisit;
                if (lordJob == null)
                {
                    abortedByTimeout = true;
                    ReadyForNextToil();
                    return;
                }

                if (totalWaitTicks >= maxQueueWaitTicks)
                {
                    abortedByTimeout = true;
                    ReadyForNextToil();
                    return;
                }

                if (pawn.Position != ServiceCell) return;
                if (!Register.IsManned) return;
                if (!IsMyTurn(lordJob)) return;

                ticksLeftThisToil--;
                if (ticksLeftThisToil <= 0)
                {
                    ReadyForNextToil();
                }
            };
            doService.AddFinishAction(() => ShopProgressBarUtility.Clear(pawn));
            yield return doService;

            Toil finalize = new Toil();
            finalize.defaultCompleteMode = ToilCompleteMode.Instant;
            finalize.initAction = () =>
            {
                LordJob_CustomerVisit lordJob = pawn.Map.lordManager.LordOf(pawn)?.LordJob as LordJob_CustomerVisit;
                if (lordJob == null) return;

                GameComponent_ShopFinanceManager finance = Current.Game?.GetComponent<GameComponent_ShopFinanceManager>();
                GameComponent_ShopAnalyticsManager analytics = Current.Game?.GetComponent<GameComponent_ShopAnalyticsManager>();

                int pawnId = pawn.thingIDNumber;
                int budget = lordJob.GetBudgetForPawn(pawnId);
                Zone_Shop shopZone = lordJob.GetCurrentShop(pawn);
                List<CustomerCartItem> purchasedItems = SnapshotCartItems(lordJob, pawnId);

                if (abortedByTimeout)
                {
                    ShopCheckoutContext timeoutContext = BuildCheckoutContext(lordJob, shopZone, finance, pawnId, purchasedItems, 0f, new List<FinanceLineItem>(), 0, true, false, "等太久没轮到收银，离店前没有付款，商品已放回店里");
                    SimShopCheckoutApi.NotifyCheckoutFailed(timeoutContext);
                    CustomerReviewSnapshotBuilder.TryEnqueueReview(pawn, lordJob, shopZone, timeoutContext.billLines, 0, timeoutContext.failReason);
                    HandleCheckoutTimeout(lordJob, finance, pawnId, shopZone);
                    lordJob.GetOrCreateSession(pawn)?.NotifyCheckoutFailed(lordJob, pawn, timeoutContext.failReason);
                    analytics?.RecordCheckoutResult(shopZone, totalWaitTicks, maxQueueWaitTicks, 0, budget, success: false, timeout: true);
                    lordJob.CheckAllCheckoutsDone();
                    return;
                }

                float amountOwed = lordJob.GetAmountOwedForCheckout(pawnId);
                if (amountOwed > 0f)
                {
                    int silverAmount = Mathf.CeilToInt(amountOwed);
                    List<FinanceLineItem> reviewLines = finance?.GetPendingBillLines(pawn) ?? new List<FinanceLineItem>();
                    ShopCheckoutContext checkoutContext = BuildCheckoutContext(lordJob, shopZone, finance, pawnId, purchasedItems, amountOwed, reviewLines, silverAmount, false, true, "");
                    SimShopCheckoutApi.NotifyBuildCheckoutLines(checkoutContext);
                    SimShopCheckoutApi.NotifyBeforeCheckoutCommit(checkoutContext);
                    silverAmount = SimShopCheckoutApi.ModifyPaidSilver(checkoutContext, checkoutContext.paidSilver);
                    checkoutContext.paidSilver = silverAmount;
                    Register.DepositSilver(silverAmount);
                    finance?.CommitCheckout(pawn, Register, silverAmount);
                    lordJob.RecordShopPayment(pawn, silverAmount);
                    lordJob.ResolveServiceOrdersOnCheckoutPaid(pawn, shopZone);
                    CustomerPurchaseDeliveryUtility.DeliverPurchasedItems(pawn, purchasedItems);
                    lordJob.RecordDeliveredItems(pawnId, purchasedItems);
                    PurchaseOutcomeResolver.TryQueuePostPurchaseJobs(pawn, lordJob, pawnId, shopZone, purchasedItems);
                    if (!checkoutContext.postCheckoutJobs.NullOrEmpty())
                        lordJob.QueuePostCheckoutJobs(pawnId, checkoutContext.postCheckoutJobs);
                    SimShopCheckoutApi.NotifyCheckoutPaid(checkoutContext);
                    CustomerReviewSnapshotBuilder.TryEnqueueReview(pawn, lordJob, shopZone, checkoutContext.billLines, silverAmount, "付款完成");
                    CustomerExpressionUtility.TryShowExpression(pawn, CustomerExpressionEvents.CheckoutPaid);
                    ShopBubbleUtility.ShowSilverPayment(pawn, silverAmount);
                    lordJob.ClearCustomerCart(pawnId);
                    lordJob.GetOrCreateSession(pawn)?.NotifyCheckoutPaid(lordJob, pawn, "付款完成");
                    analytics?.RecordCheckoutResult(shopZone, totalWaitTicks, maxQueueWaitTicks, silverAmount, budget, success: true, timeout: false);
                }
                else
                {
                    List<FinanceLineItem> reviewLines = finance?.GetPendingBillLines(pawn) ?? new List<FinanceLineItem>();
                    ShopCheckoutContext checkoutContext = BuildCheckoutContext(lordJob, shopZone, finance, pawnId, purchasedItems, 0f, reviewLines, 0, false, true, "");
                    SimShopCheckoutApi.NotifyBuildCheckoutLines(checkoutContext);
                    SimShopCheckoutApi.NotifyBeforeCheckoutCommit(checkoutContext);
                    finance?.ClearPendingBill(pawn);
                    lordJob.ResolveServiceOrdersOnCheckoutPaid(pawn, shopZone);
                    CustomerPurchaseDeliveryUtility.DeliverPurchasedItems(pawn, purchasedItems);
                    lordJob.RecordDeliveredItems(pawnId, purchasedItems);
                    PurchaseOutcomeResolver.TryQueuePostPurchaseJobs(pawn, lordJob, pawnId, shopZone, purchasedItems);
                    if (!checkoutContext.postCheckoutJobs.NullOrEmpty())
                        lordJob.QueuePostCheckoutJobs(pawnId, checkoutContext.postCheckoutJobs);
                    SimShopCheckoutApi.NotifyCheckoutPaid(checkoutContext);
                    bool needsPostCheckout = lordJob.NeedsPostCheckoutCompletion(pawnId);
                    bool hasCompletedFreeService = lordJob.HasCompletedFreeServiceOrder(pawnId);
                    string reviewReason = needsPostCheckout ? "免费服务等待使用" : hasCompletedFreeService ? "完成免费服务" : "未消费离店";
                    if (!needsPostCheckout
                        && !lordJob.TryEnqueueFreeCompletedServiceReview(pawn, shopZone, "完成免费服务"))
                    {
                        CustomerReviewSnapshotBuilder.TryEnqueueReview(pawn, lordJob, shopZone, checkoutContext.billLines, 0, reviewReason);
                    }
                    lordJob.ClearCustomerCart(pawnId);
                    lordJob.GetOrCreateSession(pawn)?.NotifyCheckoutPaid(lordJob, pawn, reviewReason);
                    analytics?.RecordCheckoutResult(shopZone, totalWaitTicks, maxQueueWaitTicks, 0, budget, success: true, timeout: false);
                }

                lordJob.CheckAllCheckoutsDone();
            };
            yield return finalize;
        }

        /// <summary>
        /// 确保排队格和服务格在当前地图状态下可用。
        /// </summary>
        private Toil MakeEnsureQueueAndServiceCellsToil()
        {
            Toil toil = new Toil();
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            toil.initAction = () =>
            {
                IntVec3 service = ServiceCell;
                if (!CheckoutQueueCellUtility.IsServiceCellStructurallyUsable(pawn.Map, service, pawn))
                {
                    service = CheckoutQueueCellUtility.FindServiceCell(Register, pawn);
                    job.SetTarget(TargetIndex.C, service);
                }

                IntVec3 queue = QueueCell;
                LordJob_CustomerVisit lordJob = pawn.Map.lordManager.LordOf(pawn)?.LordJob as LordJob_CustomerVisit;
                if (!CheckoutQueueCellUtility.IsWaitingCellUsable(queue, pawn.Map, pawn, service))
                {
                    queue = CheckoutQueueCellUtility.FindQueueCell(Register, service, GetQueueIndex(lordJob), pawn);
                    job.SetTarget(TargetIndex.B, queue);
                }
            };
            return toil;
        }

        /// <summary>
        /// 按当前队列顺序刷新等待格，负责在其他顾客离开或让路后恢复队列站位。
        /// </summary>
        private void UpdateQueueCell(LordJob_CustomerVisit lordJob)
        {
            IntVec3 service = ServiceCell;
            if (!CheckoutQueueCellUtility.IsServiceCellStructurallyUsable(pawn.Map, service, pawn))
            {
                service = CheckoutQueueCellUtility.FindServiceCell(Register, pawn);
                job.SetTarget(TargetIndex.C, service);
            }

            IntVec3 queue = CheckoutQueueCellUtility.FindQueueCell(Register, service, GetQueueIndex(lordJob), pawn);
            if (queue.IsValid && queue != QueueCell)
                job.SetTarget(TargetIndex.B, queue);
        }

        /// <summary>
        /// 计算当前顾客在本收银台前面的待付款顾客数量，负责得到稳定的等待格序号。
        /// </summary>
        private int GetQueueIndex(LordJob_CustomerVisit lordJob)
        {
            if (lordJob == null) return 0;

            int ahead = 0;
            int myId = pawn.thingIDNumber;
            int myOrder = lordJob.EnsureCheckoutOrder(myId);
            IReadOnlyList<Pawn> pawns = pawn.Map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn other = pawns[i];
                if (!IsEarlierPayingPawn(other, lordJob, myId, myOrder)) continue;
                ahead++;
            }

            return ahead;
        }

        /// <summary>
        /// 判断当前顾客是否已经轮到在该收银台结账。
        /// </summary>
        private bool IsMyTurn(LordJob_CustomerVisit lordJob)
        {
            int myId = pawn.thingIDNumber;
            int myOrder = lordJob.EnsureCheckoutOrder(myId);

            IReadOnlyList<Pawn> pawns = pawn.Map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn other = pawns[i];
                if (IsEarlierPayingPawn(other, lordJob, myId, myOrder)) return false;
            }

            return true;
        }

        /// <summary>
        /// 判断另一个顾客是否正在同一收银台前排在当前顾客前方，负责复用结账顺序判断。
        /// </summary>
        private bool IsEarlierPayingPawn(Pawn other, LordJob_CustomerVisit lordJob, int myId, int myOrder)
        {
            if (other == null || other == pawn || other.thingIDNumber == myId) return false;
            if (other.CurJobDef == null || other.CurJobDef.defName != "Customer_PayAtRegister") return false;
            if (other.CurJob?.targetA.Thing != Register) return false;

            int otherId = other.thingIDNumber;
            if (lordJob.GetAmountOwedForCheckout(otherId) <= 0f) return false;

            int otherOrder = lordJob.GetCheckoutOrder(otherId);
            return otherOrder < myOrder;
        }

        /// <summary>
        /// 处理顾客排队超时，负责退回商品、清账并标记服务订单失败或取消。
        /// </summary>
        private void HandleCheckoutTimeout(LordJob_CustomerVisit lordJob, GameComponent_ShopFinanceManager finance, int pawnId, Zone_Shop shopZone)
        {
            if (shopZone != null)
            {
                ShopDataUtility.ReturnCartItemsToShop(shopZone, lordJob.GetCartItems(pawnId));
            }

            finance?.ClearPendingBill(pawn);
            lordJob.ResolveServiceOrdersOnCheckoutFailure(pawnId);
            lordJob.ClearCustomerCart(pawnId);
            lordJob.ClearCustomerServiceOrders(pawnId);
            CustomerExpressionUtility.TryShowExpression(pawn, CustomerExpressionEvents.CheckoutTimeout);
            ShopBubbleUtility.ShowTextBubble(pawn, SimTranslation.T("RSMF.Bubble.CheckoutQueueTimeout"), new Color(1f, 0.72f, 0.4f));
        }

        /// <summary>
        /// 构建结账公开上下文，负责把内部收银状态安全传递给外部 Hook。
        /// </summary>
        private ShopCheckoutContext BuildCheckoutContext(
            LordJob_CustomerVisit lordJob,
            Zone_Shop shopZone,
            GameComponent_ShopFinanceManager finance,
            int pawnId,
            List<CustomerCartItem> purchasedItems,
            float amountOwed,
            List<FinanceLineItem> billLines,
            int paidSilver,
            bool timedOut,
            bool success,
            string failReason)
        {
            return new ShopCheckoutContext
            {
                customer = pawn,
                shop = shopZone,
                register = Register,
                internalVisit = lordJob,
                billLines = billLines ?? new List<FinanceLineItem>(),
                amountOwed = amountOwed,
                paidSilver = paidSilver,
                timedOut = timedOut,
                success = success,
                failReason = failReason ?? ""
            };
        }

        /// <summary>
        /// 复制顾客当前商品购物车，供付款后购后行为规则读取。
        /// </summary>
        private static List<CustomerCartItem> SnapshotCartItems(LordJob_CustomerVisit lordJob, int pawnId)
        {
            List<CustomerCartItem> raw = lordJob?.GetCartItems(pawnId);
            if (raw.NullOrEmpty()) return new List<CustomerCartItem>();

            return raw
                .Where(item => item != null && item.def != null && item.count > 0)
                .Select(item => new CustomerCartItem
                {
                    def = item.def,
                    count = item.count
                })
                .ToList();
        }

        /// <summary>
        /// 让顾客朝向当前收银员，缺少收银员时朝向收银台。
        /// </summary>
        private void FaceCashierOrRegister()
        {
            Pawn cashier = Register.CurrentCashier;
            if (cashier != null)
                pawn.rotationTracker.FaceTarget(cashier);
            else
                pawn.rotationTracker.FaceTarget(Register);
        }

        /// <summary>
        /// 根据收银员全局工作速度和社交影响力计算收银服务速度。
        /// </summary>
        private float GetCashierServiceSpeed()
        {
            Pawn cashier = Register.CurrentCashier;
            if (cashier == null || cashier.Destroyed || cashier.Dead)
                return 1f;

            float workSpeed = cashier.GetStatValue(StatDefOf.WorkSpeedGlobal);
            float socialImpact = CashierSocialUtility.GetServiceSocialImpact(cashier);
            return Mathf.Max(0.2f, workSpeed * Mathf.Max(0.5f, socialImpact));
        }
    }
}
