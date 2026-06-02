using RimWorld;
using SimManagementLib.Api;
using SimManagementLib.GameComp;
using SimManagementLib.Pojo;
using SimManagementLib.SimDef;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimAI.CustomerVisit
{
    /// <summary>
    /// 管理单个顾客的完整访问状态，负责统一阶段推进、超时兜底、账单状态和调试诊断。
    /// </summary>
    public class CustomerVisitSession : IExposable
    {
        internal int pawnId = -1;
        internal CustomerVisitStage stage = CustomerVisitStage.Arriving;
        internal int currentShopZoneId = -1;
        internal IntVec3 currentShopCell = IntVec3.Invalid;
        internal int currentShopVisitStartTick = -1;
        internal int totalVisitStartTick = -1;
        internal float totalSpentAcrossShops;
        internal float desiredSpendRatio = -1f;
        internal int currentShopConsumptionActions;
        internal int currentShopBrowseAttempts;
        internal int currentShopNoProgressBrowseAttempts;
        internal bool currentShopMinimumBrowseDone;
        internal List<int> currentShopVisitedStorageThingIds = new List<int>();
        internal int currentShopLastStorageThingId = -1;
        internal List<int> visitedShopZoneIds = new List<int>();
        internal string lastReason = "";
        internal string lastFailureReason = "";
        internal int lastStageChangeTick = -1;
        internal int lastDecisionTick = -1;

        public int PawnId => pawnId;
        public CustomerVisitStage Stage => stage;
        public int CurrentShopZoneId => currentShopZoneId;
        public IntVec3 CurrentShopCell => currentShopCell;
        public int CurrentShopVisitStartTick => currentShopVisitStartTick;
        public int TotalVisitStartTick => totalVisitStartTick;
        public float TotalSpentAcrossShops => totalSpentAcrossShops;
        public float DesiredSpendRatio => desiredSpendRatio;
        public int CurrentShopConsumptionActions => currentShopConsumptionActions;
        public int CurrentShopBrowseAttempts => currentShopBrowseAttempts;
        public int CurrentShopNoProgressBrowseAttempts => currentShopNoProgressBrowseAttempts;
        public bool CurrentShopMinimumBrowseDone => currentShopMinimumBrowseDone;
        public IReadOnlyList<int> CurrentShopVisitedStorageThingIds => currentShopVisitedStorageThingIds;
        public int CurrentShopLastStorageThingId => currentShopLastStorageThingId;
        public IReadOnlyList<int> VisitedShopZoneIds => visitedShopZoneIds;
        public string LastReason => lastReason;
        public string LastFailureReason => lastFailureReason;
        public int LastStageChangeTick => lastStageChangeTick;
        public int LastDecisionTick => lastDecisionTick;

        /// <summary>
        /// 读写顾客访问 Session，负责保存新版顾客运行态。
        /// </summary>
        public void ExposeData()
        {
            Scribe_Values.Look(ref pawnId, "pawnId", -1);
            Scribe_Values.Look(ref stage, "stage", CustomerVisitStage.Arriving);
            Scribe_Values.Look(ref currentShopZoneId, "currentShopZoneId", -1);
            Scribe_Values.Look(ref currentShopCell, "currentShopCell", IntVec3.Invalid);
            Scribe_Values.Look(ref currentShopVisitStartTick, "currentShopVisitStartTick", -1);
            Scribe_Values.Look(ref totalVisitStartTick, "totalVisitStartTick", -1);
            Scribe_Values.Look(ref totalSpentAcrossShops, "totalSpentAcrossShops", 0f);
            Scribe_Values.Look(ref desiredSpendRatio, "desiredSpendRatio", -1f);
            Scribe_Values.Look(ref currentShopConsumptionActions, "currentShopConsumptionActions", 0);
            Scribe_Values.Look(ref currentShopBrowseAttempts, "currentShopBrowseAttempts", 0);
            Scribe_Values.Look(ref currentShopNoProgressBrowseAttempts, "currentShopNoProgressBrowseAttempts", 0);
            Scribe_Values.Look(ref currentShopMinimumBrowseDone, "currentShopMinimumBrowseDone", false);
            Scribe_Collections.Look(ref currentShopVisitedStorageThingIds, "currentShopVisitedStorageThingIds", LookMode.Value);
            Scribe_Values.Look(ref currentShopLastStorageThingId, "currentShopLastStorageThingId", -1);
            Scribe_Collections.Look(ref visitedShopZoneIds, "visitedShopZoneIds", LookMode.Value);
            Scribe_Values.Look(ref lastReason, "lastReason", "");
            Scribe_Values.Look(ref lastFailureReason, "lastFailureReason", "");
            Scribe_Values.Look(ref lastStageChangeTick, "lastStageChangeTick", -1);
            Scribe_Values.Look(ref lastDecisionTick, "lastDecisionTick", -1);
            if (visitedShopZoneIds == null)
                visitedShopZoneIds = new List<int>();
            if (currentShopVisitedStorageThingIds == null)
                currentShopVisitedStorageThingIds = new List<int>();
        }

        /// <summary>
        /// 初始化顾客访问 Session，负责从 LordJob 默认目标建立首个商店状态。
        /// </summary>
        internal void Initialize(LordJob_CustomerVisit visit, Pawn pawn)
        {
            pawnId = pawn?.thingIDNumber ?? pawnId;
            if (currentShopZoneId < 0)
                currentShopZoneId = visit?.targetShopZoneId ?? -1;
            if (!currentShopCell.IsValid && visit != null)
                currentShopCell = visit.targetShopCell;
            int now = Find.TickManager?.TicksGame ?? 0;
            if (currentShopVisitStartTick < 0)
                currentShopVisitStartTick = now;
            if (totalVisitStartTick < 0)
                totalVisitStartTick = now;
            if (lastStageChangeTick < 0)
                lastStageChangeTick = now;
            if (currentShopZoneId >= 0 && !visitedShopZoneIds.Contains(currentShopZoneId))
                visitedShopZoneIds.Add(currentShopZoneId);
            EnsureDesiredSpendRatio(visit);
        }

        /// <summary>
        /// 周期推进顾客 Session，负责处理安全兜底、普通结账、普通离店和长期扩展 Tick。
        /// </summary>
        internal CustomerVisitTickResult Tick(LordJob_CustomerVisit visit, Pawn pawn)
        {
            Initialize(visit, pawn);
            lastDecisionTick = Find.TickManager?.TicksGame ?? 0;

            if (ShouldForceEndForSafety(pawn, out string safetyReason))
            {
                SetStage(visit, pawn, CustomerVisitStage.Leaving, safetyReason, notifyExtensions: true);
                visit.CleanupUnpaidCustomerStateForSession(pawn, safetyReason);
                return CustomerVisitTickResult.Leave(safetyReason);
            }

            if (SimShopCustomerApi.HasCustomerVisitExtensions)
                SimShopCustomerApi.NotifyCustomerVisitExtensionTick(BuildExtensionContext(visit, pawn, "Session Tick"));

            Zone_Shop shop = GetCurrentShop(visit, pawn);
            int owed = Mathf.CeilToInt(visit.GetAmountOwedForCheckout(pawnId));
            if (stage == CustomerVisitStage.WaitingCheckout && ShouldEnterCheckoutPhase(visit, pawn))
                return CustomerVisitTickResult.Checkout("顾客等待结账，重新推动结账阶段");

            if (owed > 0 && ShouldCheckoutWithBill(visit, pawn, shop, out string checkoutReason))
            {
                MarkReadyForCheckout(visit, pawn, checkoutReason);
                return CustomerVisitTickResult.Checkout(checkoutReason);
            }

            if (owed <= 0 && ShouldLeaveWithoutBill(visit, pawn, shop, out string leaveReason))
            {
                SetStage(visit, pawn, CustomerVisitStage.Leaving, leaveReason, notifyExtensions: true);
                visit.CleanupUnpaidCustomerStateForSession(pawn, leaveReason);
                return CustomerVisitTickResult.Leave(leaveReason);
            }

            return default(CustomerVisitTickResult);
        }

        /// <summary>
        /// 判断指定阶段是否允许由对应 JobGiver 分配 Job。
        /// </summary>
        internal bool AllowsJobGiver(CustomerVisitStage requestedStage)
        {
            if (stage == CustomerVisitStage.Arriving)
                stage = CustomerVisitStage.Browsing;
            if (stage == CustomerVisitStage.WaitingCheckout && requestedStage == CustomerVisitStage.Checkout)
                return true;
            return stage == requestedStage;
        }

        /// <summary>
        /// 判断顾客是否已经访问过指定商店，负责让跨店选择不直接读取访问列表。
        /// </summary>
        internal bool HasVisitedShop(int shopZoneId)
        {
            return shopZoneId >= 0 && visitedShopZoneIds != null && visitedShopZoneIds.Contains(shopZoneId);
        }

        /// <summary>
        /// 判断顾客在当前店是否已经看过指定货柜，负责让浏览 JobGiver 优先选择新货柜。
        /// </summary>
        internal bool HasVisitedCurrentShopStorage(Building_SimContainer storage)
        {
            if (storage == null) return false;
            return currentShopVisitedStorageThingIds != null
                && currentShopVisitedStorageThingIds.Contains(storage.thingIDNumber);
        }

        /// <summary>
        /// 记录顾客当前店的货柜浏览目标，负责避免同一顾客连续反复看同一个货柜。
        /// </summary>
        internal void RecordCurrentShopStorageVisit(Building_SimContainer storage)
        {
            if (storage == null) return;
            if (currentShopVisitedStorageThingIds == null)
                currentShopVisitedStorageThingIds = new List<int>();

            int storageId = storage.thingIDNumber;
            currentShopLastStorageThingId = storageId;
            if (!currentShopVisitedStorageThingIds.Contains(storageId))
                currentShopVisitedStorageThingIds.Add(storageId);
        }

        /// <summary>
        /// 记录顾客到达店铺，负责从旅行阶段切到浏览阶段。
        /// </summary>
        internal void NotifyArrived(LordJob_CustomerVisit visit, Pawn pawn)
        {
            SetStage(visit, pawn, CustomerVisitStage.Browsing, "顾客抵达商店", notifyExtensions: true);
        }

        /// <summary>
        /// 记录浏览开始，负责统一统计浏览次数。
        /// </summary>
        internal void NotifyBrowseStarted(LordJob_CustomerVisit visit, Pawn pawn)
        {
            SetStage(visit, pawn, CustomerVisitStage.Browsing, "顾客开始浏览", notifyExtensions: false);
            currentShopMinimumBrowseDone = true;
            currentShopBrowseAttempts++;
        }

        /// <summary>
        /// 记录一次没有消费进展的浏览。
        /// </summary>
        internal void NotifyNoProgressBrowse(LordJob_CustomerVisit visit, Pawn pawn, string reason)
        {
            currentShopNoProgressBrowseAttempts++;
            lastReason = reason ?? "顾客浏览无进展";
            if (ShouldCheckoutAfterNoProgress(visit, pawn))
                MarkReadyForCheckout(visit, pawn, lastReason);
        }

        /// <summary>
        /// 记录一次浏览尝试，负责让空逛也纳入浏览次数限制。
        /// </summary>
        internal void NotifyBrowseAttempt(LordJob_CustomerVisit visit, Pawn pawn, string reason)
        {
            currentShopBrowseAttempts++;
            lastReason = reason ?? "顾客浏览";
        }

        /// <summary>
        /// 记录一次成功消费，负责清除无进展计数并判断是否应结账。
        /// </summary>
        internal void NotifyConsumptionCompleted(LordJob_CustomerVisit visit, Pawn pawn, string reason)
        {
            currentShopNoProgressBrowseAttempts = 0;
            currentShopConsumptionActions++;
            lastReason = reason ?? "顾客完成消费";
            Zone_Shop shop = GetCurrentShop(visit, pawn);
            if (ShouldCheckoutFromCurrentShop(visit, pawn, shop, lastReason))
                MarkReadyForCheckout(visit, pawn, lastReason);
        }

        /// <summary>
        /// 标记顾客准备结账，负责把阶段切到等待结账并同步旧结账队列。
        /// </summary>
        internal void MarkReadyForCheckout(LordJob_CustomerVisit visit, Pawn pawn, string reason)
        {
            lastReason = reason ?? "顾客准备结账";
            SetStage(visit, pawn, CustomerVisitStage.WaitingCheckout, lastReason, notifyExtensions: true);
            visit.MarkPawnReadyForCheckoutFromSession(pawnId);
            if (visit.ShouldEnterCheckoutPhaseForSession())
                visit.lord?.ReceiveMemo("Customer_ReadyToCheckout");
        }

        /// <summary>
        /// 记录顾客进入结账阶段。
        /// </summary>
        internal void NotifyCheckoutStarted(LordJob_CustomerVisit visit, Pawn pawn)
        {
            SetStage(visit, pawn, CustomerVisitStage.Checkout, "顾客进入结账阶段", notifyExtensions: true);
        }

        /// <summary>
        /// 记录结账成功，负责进入购后或离店收尾阶段。
        /// </summary>
        internal void NotifyCheckoutPaid(LordJob_CustomerVisit visit, Pawn pawn, string reason)
        {
            CustomerVisitStage next = visit.NeedsPostCheckoutCompletion(pawnId) ? CustomerVisitStage.PostCheckout : CustomerVisitStage.Leaving;
            SetStage(visit, pawn, next, reason ?? "顾客结账完成", notifyExtensions: true);
        }

        /// <summary>
        /// 记录购后阶段完成，负责把无待付款顾客推进到离店收尾。
        /// </summary>
        internal void NotifyPostCheckoutCompleted(LordJob_CustomerVisit visit, Pawn pawn, string reason)
        {
            SetStage(visit, pawn, CustomerVisitStage.Leaving, reason ?? "购后行为完成", notifyExtensions: true);
        }

        /// <summary>
        /// 记录结账失败，负责进入离店阶段。
        /// </summary>
        internal void NotifyCheckoutFailed(LordJob_CustomerVisit visit, Pawn pawn, string reason)
        {
            lastFailureReason = reason ?? "顾客结账失败";
            SetStage(visit, pawn, CustomerVisitStage.Leaving, lastFailureReason, notifyExtensions: true);
        }

        /// <summary>
        /// 切换下一家商店，负责重置单店计数。
        /// </summary>
        internal void MoveToShop(LordJob_CustomerVisit visit, Pawn pawn, Zone_Shop next)
        {
            if (next == null) return;
            currentShopZoneId = next.ID;
            currentShopCell = next.Cells.Count > 0 ? next.Cells[0] : IntVec3.Invalid;
            currentShopVisitStartTick = Find.TickManager?.TicksGame ?? 0;
            currentShopConsumptionActions = 0;
            currentShopBrowseAttempts = 0;
            currentShopNoProgressBrowseAttempts = 0;
            currentShopMinimumBrowseDone = false;
            currentShopVisitedStorageThingIds?.Clear();
            currentShopLastStorageThingId = -1;
            if (!visitedShopZoneIds.Contains(next.ID))
                visitedShopZoneIds.Add(next.ID);
            visit.targetShopZoneId = currentShopZoneId;
            visit.targetShopCell = currentShopCell;
            SetStage(visit, pawn, CustomerVisitStage.Arriving, "顾客前往下一家店", notifyExtensions: true);
        }

        /// <summary>
        /// 尝试切换到下一家商店，负责跨店访问选择、状态重置和上一店运行态清理。
        /// </summary>
        internal bool TryMoveToNextShop(LordJob_CustomerVisit visit, Pawn pawn)
        {
            if (visit == null || pawn?.Map == null) return false;
            ShoppingBehaviorProps behavior = visit.GetShoppingBehavior();
            Zone_Shop currentShop = GetCurrentShop(visit, pawn);
            if (currentShop == null) return false;
            if (behavior.maxShopsToVisit <= 1) return false;
            if (visitedShopZoneIds.Count >= behavior.maxShopsToVisit) return false;
            if (HasReachedDesiredSpend(visit)) return false;
            if (Rand.Value > behavior.continueToNextShopChance) return false;
            if (GetRemainingTripBudgetRatio(visit, pawn) < behavior.nextShopMinRemainingBudgetRatio) return false;

            Zone_Shop next = CustomerNextShopSelector.FindNextShop(visit, pawn, currentShop, this);
            if (next == null) return false;

            visit.ClearCustomerCart(pawnId);
            visit.ClearPawnReadyForCheckout(pawnId);
            MoveToShop(visit, pawn, next);
            return true;
        }

        /// <summary>
        /// 返回当前商店。
        /// </summary>
        internal Zone_Shop GetCurrentShop(LordJob_CustomerVisit visit, Pawn pawn)
        {
            if (visit == null || pawn?.Map == null) return null;
            return ShopDataUtility.FindAssignedShopZone(pawn.Map, currentShopZoneId, currentShopCell);
        }

        /// <summary>
        /// 返回顾客本次访问剩余预算。
        /// </summary>
        internal float GetRemainingTripBudget(LordJob_CustomerVisit visit, Pawn pawn)
        {
            int budget = visit.GetBudgetForPawn(pawnId);
            return Mathf.Max(0f, budget - totalSpentAcrossShops);
        }

        /// <summary>
        /// 返回当前店最多浏览次数，负责为无效配置提供保守默认值。
        /// </summary>
        internal int GetBrowseLimitForVisit(LordJob_CustomerVisit visit)
        {
            return GetBrowseLimit(visit);
        }

        /// <summary>
        /// 返回连续无进展浏览退出阈值。
        /// </summary>
        internal int GetNoProgressLimitForVisit(LordJob_CustomerVisit visit)
        {
            return GetNoProgressLimit(visit);
        }

        /// <summary>
        /// 判断当前店消费动作是否达到上限。
        /// </summary>
        internal bool HasReachedConsumptionLimit(LordJob_CustomerVisit visit)
        {
            return currentShopConsumptionActions >= visit.GetShoppingBehavior().maxConsumptionActionsPerShop;
        }

        /// <summary>
        /// 判断当前店浏览次数是否达到上限。
        /// </summary>
        internal bool HasReachedBrowseLimitForVisit(LordJob_CustomerVisit visit)
        {
            return currentShopBrowseAttempts >= GetBrowseLimit(visit);
        }

        /// <summary>
        /// 判断当前店连续无进展浏览是否达到上限。
        /// </summary>
        internal bool HasReachedNoProgressLimitForVisit(LordJob_CustomerVisit visit)
        {
            return currentShopNoProgressBrowseAttempts >= GetNoProgressLimit(visit);
        }

        /// <summary>
        /// 判断当前商店是否已经满足结账条件，负责给外部动作做纯判断。
        /// </summary>
        internal bool ShouldCheckoutNow(LordJob_CustomerVisit visit, Pawn pawn, Zone_Shop shop, string reason)
        {
            return ShouldCheckoutFromCurrentShop(visit, pawn, shop, reason);
        }

        /// <summary>
        /// 记录已支付金额。
        /// </summary>
        internal void RecordPayment(int paidSilver)
        {
            if (paidSilver > 0)
                totalSpentAcrossShops += paidSilver;
        }

        /// <summary>
        /// 构建 Inspect 和 Debug 使用的一行状态摘要。
        /// </summary>
        internal string BuildShortStatus(LordJob_CustomerVisit visit, Pawn pawn)
        {
            Zone_Shop shop = GetCurrentShop(visit, pawn);
            int now = Find.TickManager?.TicksGame ?? 0;
            return $"{stage} | 店铺={(shop?.label ?? "无")} | 浏览={currentShopBrowseAttempts} | 无进展={currentShopNoProgressBrowseAttempts} | 停留={now - currentShopVisitStartTick} | 原因={lastReason}";
        }

        /// <summary>
        /// 构建完整诊断文本，负责解释顾客为什么没有发 Job、没有结账或没有离店。
        /// </summary>
        internal string BuildDebugReport(LordJob_CustomerVisit visit, Pawn pawn)
        {
            Zone_Shop shop = GetCurrentShop(visit, pawn);
            int now = Find.TickManager?.TicksGame ?? 0;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("顾客 Session 诊断");
            sb.AppendLine("Pawn: " + (pawn?.LabelShortCap ?? "无"));
            sb.AppendLine("阶段: " + stage);
            sb.AppendLine("当前 Job: " + (pawn?.CurJobDef?.defName ?? "无"));
            sb.AppendLine("当前商店: " + (shop?.label ?? "无"));
            sb.AppendLine("待付款: " + visit.GetAmountOwedForCheckout(pawnId).ToString("F0"));
            sb.AppendLine("剩余预算: " + GetRemainingTripBudget(visit, pawn).ToString("F0"));
            sb.AppendLine("浏览次数: " + currentShopBrowseAttempts);
            sb.AppendLine("无进展次数: " + currentShopNoProgressBrowseAttempts);
            sb.AppendLine("已浏览货柜数: " + (currentShopVisitedStorageThingIds?.Count ?? 0));
            sb.AppendLine("最近货柜ID: " + (currentShopLastStorageThingId >= 0 ? currentShopLastStorageThingId.ToString() : "无"));
            sb.AppendLine("当前店停留 Tick: " + (now - currentShopVisitStartTick));
            sb.AppendLine("总停留 Tick: " + (now - totalVisitStartTick));
            sb.AppendLine("最后原因: " + lastReason);
            sb.AppendLine("最后失败: " + lastFailureReason);
            sb.AppendLine("下一步判断: " + ExplainNextDecision(visit, pawn, shop));
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// 强制推进到下一步，负责 DebugAction 调试卡住顾客。
        /// </summary>
        internal CustomerVisitTickResult ForceAdvance(LordJob_CustomerVisit visit, Pawn pawn)
        {
            if (stage == CustomerVisitStage.Browsing || stage == CustomerVisitStage.SelectingService || stage == CustomerVisitStage.RunningExternalAction)
            {
                MarkReadyForCheckout(visit, pawn, "Debug 强制推进到结账");
                return CustomerVisitTickResult.Checkout(lastReason);
            }

            if (stage == CustomerVisitStage.WaitingCheckout)
                return CustomerVisitTickResult.Checkout("Debug 重发结账 Memo");

            SetStage(visit, pawn, CustomerVisitStage.Leaving, "Debug 强制结束访问", notifyExtensions: true);
            return CustomerVisitTickResult.Leave(lastReason);
        }

        /// <summary>
        /// 切换阶段并通知扩展。
        /// </summary>
        private void SetStage(LordJob_CustomerVisit visit, Pawn pawn, CustomerVisitStage next, string reason, bool notifyExtensions)
        {
            if (stage == next && string.Equals(lastReason, reason ?? "", System.StringComparison.Ordinal))
                return;

            stage = next;
            lastReason = reason ?? "";
            lastStageChangeTick = Find.TickManager?.TicksGame ?? 0;
            SimDebugLogger.Journey("RSMF.CustomerSession", $"阶段={stage} 原因={lastReason}", pawn, GetCurrentShop(visit, pawn), -1);
            if (notifyExtensions && SimShopCustomerApi.HasCustomerVisitExtensions)
                SimShopCustomerApi.NotifyCustomerVisitStageChanged(BuildExtensionContext(visit, pawn, lastReason));
        }

        /// <summary>
        /// 判断是否因为安全条件强制结束访问。
        /// </summary>
        private static bool ShouldForceEndForSafety(Pawn pawn, out string reason)
        {
            reason = "";
            if (pawn == null) return false;
            if (pawn.Downed)
            {
                reason = "顾客倒地，强制结束访问";
                return true;
            }
            if (pawn.InMentalState)
            {
                reason = "顾客进入精神状态，强制结束访问";
                return true;
            }
            if (pawn.health?.capacities != null && !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Moving))
            {
                reason = "顾客无法移动，强制结束访问";
                return true;
            }
            if (pawn.needs?.food != null && pawn.needs.food.CurCategory >= HungerCategory.UrgentlyHungry)
            {
                reason = "顾客饥饿过重，强制结束访问";
                return true;
            }
            return false;
        }

        /// <summary>
        /// 判断有账单顾客是否应进入结账。
        /// </summary>
        private bool ShouldCheckoutWithBill(LordJob_CustomerVisit visit, Pawn pawn, Zone_Shop shop, out string reason)
        {
            reason = "";
            if (visit.IsPawnReadyForCheckout(pawnId))
            {
                reason = "顾客已有待结账标记";
                return true;
            }
            if (HasReachedBrowseLimit(visit))
            {
                reason = "顾客达到浏览次数上限";
                return !ShouldDelayCheckout(visit, pawn, shop, reason);
            }
            ShoppingBehaviorProps behavior = visit.GetShoppingBehavior();
            int now = Find.TickManager.TicksGame;
            if (behavior.maxShopVisitTicks > 0 && now - currentShopVisitStartTick >= behavior.maxShopVisitTicks)
            {
                reason = "顾客当前商店停留超时";
                return !ShouldDelayCheckout(visit, pawn, shop, reason);
            }
            if (shop == null || !shop.IsOpenNow())
            {
                reason = "目标商店不可用";
                return !ShouldDelayCheckout(visit, pawn, shop, reason);
            }
            return false;
        }

        /// <summary>
        /// 判断无账单顾客是否应离店。
        /// </summary>
        private bool ShouldLeaveWithoutBill(LordJob_CustomerVisit visit, Pawn pawn, Zone_Shop shop, out string reason)
        {
            reason = "";
            if (shop == null)
            {
                reason = "目标商店不存在";
                return !ShouldDelayLeave(visit, pawn, null, reason);
            }
            if (!shop.IsOpenNow())
            {
                reason = "商店已经停止营业";
                return !ShouldDelayLeave(visit, pawn, shop, reason);
            }
            if (currentShopMinimumBrowseDone && HasReachedBrowseLimit(visit))
            {
                reason = "顾客浏览多次仍没有合适商品";
                return !ShouldDelayLeave(visit, pawn, shop, reason);
            }
            float remaining = GetRemainingTripBudget(visit, pawn);
            if (currentShopMinimumBrowseDone && !CustomerShoppingMatchUtility.ShopHasMatchingAffordableGoodsOrServices(pawn, shop, visit, remaining))
            {
                reason = "商店没有顾客当前想买的商品或服务";
                return !ShouldDelayLeave(visit, pawn, shop, reason);
            }
            return false;
        }

        /// <summary>
        /// 判断一次无进展浏览后是否应结账或离店。
        /// </summary>
        private bool ShouldCheckoutAfterNoProgress(LordJob_CustomerVisit visit, Pawn pawn)
        {
            if (!currentShopMinimumBrowseDone) return false;
            if (!HasReachedBrowseLimit(visit)) return false;
            return true;
        }

        /// <summary>
        /// 判断顾客是否应结束当前店浏览并进入结账。
        /// </summary>
        private bool ShouldCheckoutFromCurrentShop(LordJob_CustomerVisit visit, Pawn pawn, Zone_Shop shop, string reason)
        {
            if (visit == null || pawn == null || shop == null) return true;
            ShoppingBehaviorProps behavior = visit.GetShoppingBehavior();
            int now = Find.TickManager?.TicksGame ?? 0;
            if (!shop.IsOpenNow()) return !ShouldDelayCheckout(visit, pawn, shop, "商店已关门");
            if (GetRemainingTripBudget(visit, pawn) <= 0f) return !ShouldDelayCheckout(visit, pawn, shop, "顾客剩余预算不足");
            if (HasReachedDesiredSpend(visit)) return !ShouldDelayCheckout(visit, pawn, shop, "顾客已达到目标消费比例");
            if (currentShopConsumptionActions >= behavior.maxConsumptionActionsPerShop) return !ShouldDelayCheckout(visit, pawn, shop, "顾客达到消费次数上限");
            if (currentShopBrowseAttempts >= GetBrowseLimit(visit)) return !ShouldDelayCheckout(visit, pawn, shop, "顾客达到浏览次数上限");
            if (behavior.maxShopVisitTicks > 0 && now - currentShopVisitStartTick >= behavior.maxShopVisitTicks) return !ShouldDelayCheckout(visit, pawn, shop, "顾客当前商店停留超时");
            if (behavior.maxTotalVisitTicks > 0 && now - totalVisitStartTick >= behavior.maxTotalVisitTicks) return !ShouldDelayCheckout(visit, pawn, shop, "顾客总行程停留超时");
            if (!string.IsNullOrEmpty(reason)
                && currentShopConsumptionActions > 0
                && currentShopBrowseAttempts >= Mathf.Max(2, GetBrowseLimit(visit) - 1)
                && Rand.Value > behavior.continueShopChance)
                return !ShouldDelayCheckout(visit, pawn, shop, reason);
            return false;
        }

        /// <summary>
        /// 判断是否需要推动 Lord 状态机进入结账阶段。
        /// </summary>
        private bool ShouldEnterCheckoutPhase(LordJob_CustomerVisit visit, Pawn pawn)
        {
            return visit.ShouldEnterCheckoutPhaseForSession();
        }

        /// <summary>
        /// 判断是否达到浏览上限。
        /// </summary>
        private bool HasReachedBrowseLimit(LordJob_CustomerVisit visit)
        {
            return currentShopBrowseAttempts >= GetBrowseLimit(visit)
                || currentShopNoProgressBrowseAttempts >= GetNoProgressLimit(visit);
        }

        /// <summary>
        /// 返回浏览上限。
        /// </summary>
        private static int GetBrowseLimit(LordJob_CustomerVisit visit)
        {
            return Mathf.Max(1, visit.GetShoppingBehavior().maxShelvesToVisit);
        }

        /// <summary>
        /// 返回无进展浏览上限。
        /// </summary>
        private static int GetNoProgressLimit(LordJob_CustomerVisit visit)
        {
            return Mathf.Min(3, Mathf.Max(2, GetBrowseLimit(visit)));
        }

        /// <summary>
        /// 确保目标消费比例存在。
        /// </summary>
        private void EnsureDesiredSpendRatio(LordJob_CustomerVisit visit)
        {
            if (desiredSpendRatio > 0f) return;
            ShoppingBehaviorProps behavior = visit?.GetShoppingBehavior() ?? new ShoppingBehaviorProps();
            float min = Mathf.Clamp01(behavior.desiredSpendRatioRange.min);
            float max = Mathf.Clamp01(behavior.desiredSpendRatioRange.max);
            if (max < min)
            {
                float tmp = min;
                min = max;
                max = tmp;
            }
            desiredSpendRatio = Mathf.Clamp(Rand.Range(min, max), 0.05f, 1f);
        }

        /// <summary>
        /// 判断是否达到目标消费比例。
        /// </summary>
        private bool HasReachedDesiredSpend(LordJob_CustomerVisit visit)
        {
            EnsureDesiredSpendRatio(visit);
            int budget = visit.GetBudgetForPawn(pawnId);
            if (budget <= 0) return true;
            float currentBill = visit.GetCartValue(pawnId);
            return totalSpentAcrossShops + currentBill >= budget * desiredSpendRatio;
        }

        /// <summary>
        /// 返回顾客剩余预算比例，负责判断是否值得继续跨店。
        /// </summary>
        private float GetRemainingTripBudgetRatio(LordJob_CustomerVisit visit, Pawn pawn)
        {
            int budget = visit.GetBudgetForPawn(pawnId);
            if (budget <= 0) return 0f;
            return Mathf.Clamp01(GetRemainingTripBudget(visit, pawn) / budget);
        }

        /// <summary>
        /// 询问扩展是否延迟普通结账。
        /// </summary>
        private bool ShouldDelayCheckout(LordJob_CustomerVisit visit, Pawn pawn, Zone_Shop shop, string reason)
        {
            if (!SimShopCustomerApi.HasCustomerVisitExtensions) return false;
            return SimShopCustomerApi.ShouldDelayCustomerVisitCheckout(BuildExtensionContext(visit, pawn, reason));
        }

        /// <summary>
        /// 询问扩展是否延迟普通离店。
        /// </summary>
        private bool ShouldDelayLeave(LordJob_CustomerVisit visit, Pawn pawn, Zone_Shop shop, string reason)
        {
            if (!SimShopCustomerApi.HasCustomerVisitExtensions) return false;
            return SimShopCustomerApi.ShouldDelayCustomerVisitLeave(BuildExtensionContext(visit, pawn, reason));
        }

        /// <summary>
        /// 构建扩展调用上下文。
        /// </summary>
        private CustomerVisitExtensionContext BuildExtensionContext(LordJob_CustomerVisit visit, Pawn pawn, string reason)
        {
            return new CustomerVisitExtensionContext
            {
                customer = pawn,
                session = this,
                shop = GetCurrentShop(visit, pawn),
                pawnId = pawnId,
                stage = stage,
                remainingBudget = GetRemainingTripBudget(visit, pawn),
                reason = reason ?? "",
                currentTick = Find.TickManager?.TicksGame ?? 0
            };
        }

        /// <summary>
        /// 解释下一步决策，负责 DebugAction 输出卡住原因。
        /// </summary>
        private string ExplainNextDecision(LordJob_CustomerVisit visit, Pawn pawn, Zone_Shop shop)
        {
            if (ShouldForceEndForSafety(pawn, out string safety)) return safety;
            if (stage == CustomerVisitStage.WaitingCheckout) return "等待 Lord 切入结账阶段";
            if (stage == CustomerVisitStage.PostCheckout && visit.NeedsPostCheckoutCompletion(pawnId)) return "等待购后行为完成";
            if (stage == CustomerVisitStage.Browsing && shop == null) return "没有当前商店，下一次 Tick 会离店";
            if (stage == CustomerVisitStage.Browsing && currentShopMinimumBrowseDone && HasReachedBrowseLimit(visit)) return "浏览上限已到，下一次 Tick 会结账或离店";
            if (stage == CustomerVisitStage.Checkout && visit.GetAmountOwedForCheckout(pawnId) <= 0f) return "无待付款，等待结账完成检查";
            return "等待当前阶段 JobGiver 分配工作";
        }
    }
}
