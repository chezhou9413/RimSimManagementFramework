using SimManagementLib.Pojo;
using SimManagementLib.SimAI;
using SimManagementLib.SimZone;
using System;
using System.Collections.Generic;
using Verse;

namespace SimManagementLib.Api
{
    /// <summary>
    /// 提供顾客评价快照的扩展上下文，负责让外部模组读取结账现场并补充评价资料。
    /// </summary>
    public class CustomerReviewSnapshotContext
    {
        public Pawn customer;
        public LordJob_CustomerVisit visit;
        public Zone_Shop shop;
        public List<FinanceLineItem> billLines;
        public int paidSilver;
        public string checkoutResult = "";
        public CustomerReviewSnapshot snapshot;
    }

    /// <summary>
    /// 提供顾客评价快照的扩展策略，负责让外部模组追加服务体验、售后体验和评分倾向。
    /// </summary>
    public class CustomerReviewSnapshotWorker
    {
        /// <summary>
        /// 在评价快照入队前接收通知，默认不调整快照。
        /// </summary>
        public virtual void BeforeEnqueue(CustomerReviewSnapshotContext context)
        {
        }
    }

    /// <summary>
    /// 提供评价快照扩展的公开注册入口，负责安全调用外部模组的评价补充逻辑。
    /// </summary>
    public static class SimShopReviewApi
    {
        private static readonly List<CustomerReviewSnapshotWorker> Workers = new List<CustomerReviewSnapshotWorker>();

        /// <summary>
        /// 注册评价快照扩展策略。
        /// </summary>
        public static bool RegisterSnapshotWorker(CustomerReviewSnapshotWorker worker)
        {
            if (worker == null || Workers.Contains(worker)) return false;
            Workers.Add(worker);
            return true;
        }

        /// <summary>
        /// 注销评价快照扩展策略。
        /// </summary>
        public static bool UnregisterSnapshotWorker(CustomerReviewSnapshotWorker worker)
        {
            return worker != null && Workers.Remove(worker);
        }

        /// <summary>
        /// 通知外部模组补充评价快照，负责隔离外部异常。
        /// </summary>
        public static void NotifyBeforeEnqueue(CustomerReviewSnapshotContext context)
        {
            if (context == null || context.snapshot == null) return;
            for (int i = 0; i < Workers.Count; i++)
            {
                try
                {
                    Workers[i].BeforeEnqueue(context);
                }
                catch (Exception ex)
                {
                    Log.Error($"[SimShop.Review] 外部评价快照 Worker 执行失败: {ex}");
                }
            }
        }
    }
}
