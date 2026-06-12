using SimManagementLib.SimDef;
using SimManagementLib.SimAI.CustomerVisit;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimZone;
using Verse;

namespace SimManagementLib.SimAI
{
    /// <summary>
    /// 管理顾客当前店铺和跨店行程，负责选择下一家店并提供按顾客读取目标店的入口。
    /// </summary>
    public partial class LordJob_CustomerVisit
    {
        /// <summary>
        /// 查找顾客当前目标商店，负责优先使用按顾客保存的跨店状态。
        /// </summary>
        public Zone_Shop GetCurrentShop(Pawn pawn)
        {
            CustomerVisitSession session = GetOrCreateSession(pawn);
            return session?.GetCurrentShop(this, pawn);
        }

        /// <summary>
        /// 返回顾客当前目标格，负责为旅行、浏览和结账职责提供动态焦点。
        /// </summary>
        public IntVec3 GetCurrentShopCell(Pawn pawn)
        {
            CustomerVisitSession session = GetOrCreateSession(pawn);
            if (session != null && session.currentShopCell.IsValid)
                return session.currentShopCell;
            return targetShopCell;
        }

        /// <summary>
        /// 记录一次当前店消费动作，并返回是否达到当前店消费次数上限。
        /// </summary>
        public bool RegisterConsumptionActionForCurrentShop(Pawn pawn)
        {
            CustomerVisitSession session = GetOrCreateSession(pawn);
            if (session == null) return true;
            session.NotifyConsumptionCompleted(this, pawn, "顾客完成消费动作");
            return session.HasReachedConsumptionLimit(this);
        }

        /// <summary>
        /// 判断顾客是否已经达到当前店消费次数上限。
        /// </summary>
        public bool HasReachedCurrentShopConsumptionLimit(Pawn pawn)
        {
            CustomerVisitSession session = GetOrCreateSession(pawn);
            return session == null || session.HasReachedConsumptionLimit(this);
        }

        /// <summary>
        /// 判断顾客是否已经完成当前店铺的最低浏览体验。
        /// </summary>
        public bool HasCompletedCurrentShopMinimumBrowse(Pawn pawn)
        {
            CustomerVisitSession session = GetOrCreateSession(pawn);
            return session != null && session.currentShopMinimumBrowseDone;
        }

        /// <summary>
        /// 标记顾客已经完成当前店铺的最低浏览体验。
        /// </summary>
        public void MarkCurrentShopBrowsed(Pawn pawn)
        {
            CustomerVisitSession session = GetOrCreateSession(pawn);
            if (session != null)
                session.currentShopMinimumBrowseDone = true;
        }

        /// <summary>
        /// 判断顾客当前店是否已经浏览过指定货柜，负责让外部职责不直接读取 Session 内部列表。
        /// </summary>
        public bool HasVisitedCurrentShopStorage(Pawn pawn, Building_SimContainer storage)
        {
            CustomerVisitSession session = GetOrCreateSession(pawn);
            return session != null && session.HasVisitedCurrentShopStorage(storage);
        }

        /// <summary>
        /// 记录顾客当前店浏览过的货柜，负责集中维护货柜访问记忆。
        /// </summary>
        public void RecordCurrentShopStorageVisit(Pawn pawn, Building_SimContainer storage)
        {
            CustomerVisitSession session = GetOrCreateSession(pawn);
            session?.RecordCurrentShopStorageVisit(storage);
        }

        /// <summary>
        /// 记录一次当前店浏览尝试，负责让空逛也受到最多浏览次数限制。
        /// </summary>
        public void RegisterCurrentShopBrowseAttempt(Pawn pawn)
        {
            CustomerVisitSession session = GetOrCreateSession(pawn);
            if (session != null)
                session.NotifyBrowseAttempt(this, pawn, "顾客浏览");
        }

        /// <summary>
        /// 记录一次无消费进展的浏览，负责在连续空逛后推进结账离店。
        /// </summary>
        public void RegisterCurrentShopNoProgressBrowse(Pawn pawn)
        {
            CustomerVisitSession session = GetOrCreateSession(pawn);
            if (session != null)
                session.NotifyNoProgressBrowse(this, pawn, "顾客浏览无进展");
        }

        /// <summary>
        /// 清除当前店无进展浏览次数，负责让成功消费后的顾客可以继续正常购物。
        /// </summary>
        public void ClearCurrentShopNoProgressBrowse(Pawn pawn)
        {
            CustomerVisitSession session = GetOrCreateSession(pawn);
            if (session != null)
                session.currentShopNoProgressBrowseAttempts = 0;
        }

        /// <summary>
        /// 判断当前店是否已经达到最多浏览次数，负责启用顾客配置中的 maxShelvesToVisit。
        /// </summary>
        public bool HasReachedCurrentShopBrowseLimit(Pawn pawn)
        {
            CustomerVisitSession session = GetOrCreateSession(pawn);
            return session == null || session.HasReachedBrowseLimitForVisit(this);
        }

        /// <summary>
        /// 判断当前店是否连续多次没有消费进展，负责避免顾客长时间重复浏览。
        /// </summary>
        public bool HasReachedCurrentShopNoProgressLimit(Pawn pawn)
        {
            CustomerVisitSession session = GetOrCreateSession(pawn);
            return session == null || session.HasReachedNoProgressLimitForVisit(this);
        }

        /// <summary>
        /// 返回当前店最多浏览次数，负责为无效或缺失配置提供保守默认值。
        /// </summary>
        public int GetCurrentShopBrowseLimit()
        {
            return UnityEngine.Mathf.Max(1, GetShoppingBehavior().maxShelvesToVisit);
        }

        /// <summary>
        /// 返回连续无进展浏览退出阈值，负责按“两三次后走”的体验限制空逛。
        /// </summary>
        public int GetCurrentShopNoProgressBrowseLimit()
        {
            int browseLimit = GetCurrentShopBrowseLimit();
            return UnityEngine.Mathf.Min(3, UnityEngine.Mathf.Max(2, browseLimit));
        }

        /// <summary>
        /// 返回顾客跨店剩余预算，负责让每家店独立结账但共享总预算。
        /// </summary>
        public float GetRemainingTripBudget(Pawn pawn, Zone_Shop shopZone)
        {
            CustomerVisitSession session = GetOrCreateSession(pawn);
            return session?.GetRemainingTripBudget(this, pawn) ?? 0f;
        }

        // 返回顾客本次行程已消耗金额，负责给顾客页面同时统计已付款和当前待付款账单。
        public float GetTotalSpentIncludingCurrentBill(Pawn pawn)
        {
            int pawnId = pawn?.thingIDNumber ?? -1;
            if (pawnId <= 0) return 0f;

            CustomerVisitSession session = GetOrCreateSession(pawn);
            float paid = session?.TotalSpentAcrossShops ?? 0f;
            return UnityEngine.Mathf.Max(0f, paid + GetCartValue(pawnId));
        }

        /// <summary>
        /// 记录当前店已支付金额，负责跨店预算累计。
        /// </summary>
        public void RecordShopPayment(Pawn pawn, int paidSilver)
        {
            CustomerVisitSession session = GetOrCreateSession(pawn);
            if (session != null)
                session.RecordPayment(paidSilver);
        }

        /// <summary>
        /// 判断顾客是否应结束当前店浏览并进入结账。
        /// </summary>
        public bool ShouldCheckoutFromCurrentShop(Pawn pawn, Zone_Shop shopZone, string reason)
        {
            CustomerVisitSession session = GetOrCreateSession(pawn);
            if (session == null) return true;
            return session.ShouldCheckoutNow(this, pawn, shopZone, reason);
        }

        /// <summary>
        /// 尝试为顾客切换下一家商店，负责在当前店结账完成后继续跨店行程。
        /// </summary>
        public bool TryMovePawnToNextShop(Pawn pawn)
        {
            CustomerVisitSession session = GetOrCreateSession(pawn);
            return session != null && session.TryMoveToNextShop(this, pawn);
        }

        /// <summary>
        /// 返回顾客购物行为配置，负责为旧 Def 或空配置提供默认值。
        /// </summary>
        public ShoppingBehaviorProps GetShoppingBehavior()
        {
            return RuntimeCustomerKind?.shoppingBehavior ?? customerKind?.shoppingBehavior ?? new ShoppingBehaviorProps();
        }
    }
}
