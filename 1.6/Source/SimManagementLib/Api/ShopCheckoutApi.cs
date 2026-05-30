using SimManagementLib.Pojo;
using SimManagementLib.SimAI;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimZone;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace SimManagementLib.Api
{
    /// <summary>
    /// 保存一次收银结账的公开上下文，负责让外部 Worker 安全读取和调整结账数据。
    /// </summary>
    public class ShopCheckoutContext
    {
        public Pawn customer;
        public Zone_Shop shop;
        public Building_CashRegister register;
        public LordJob_CustomerVisit visit;
        public List<FinanceLineItem> billLines = new List<FinanceLineItem>();
        public List<Job> postCheckoutJobs = new List<Job>();
        public float amountOwed;
        public int paidSilver;
        public bool timedOut;
        public bool success;
        public string failReason = "";

        /// <summary>
        /// 追加付款后执行的 Job，负责让外部玩法接入结账后的顾客动作。
        /// </summary>
        public void AddPostCheckoutJob(Job job)
        {
            if (job != null) postCheckoutJobs.Add(job);
        }
    }

    /// <summary>
    /// 保存顾客准备进入结账阶段的公开上下文，负责让外部扩展判断是否需要暂缓结账。
    /// </summary>
    public class ShopCheckoutReadinessContext
    {
        public Pawn customer;
        public Zone_Shop shop;
        public LordJob_CustomerVisit visit;
        public int pawnId;
        public bool allowed = true;
        public string reason = "";

        /// <summary>
        /// 暂缓顾客进入结账阶段。
        /// </summary>
        public void Defer(string deferReason)
        {
            allowed = false;
            reason = deferReason ?? "";
        }
    }

    /// <summary>
    /// 提供结账管线的可继承 Hook，负责让外部模组调整账单、金额和结账后行为。
    /// </summary>
    public class ShopCheckoutWorker
    {
        /// <summary>
        /// 在结账提交前接收通知，默认不调整上下文。
        /// </summary>
        public virtual void BeforeCheckoutCommit(ShopCheckoutContext context)
        {
        }

        /// <summary>
        /// 在账单明细构建后接收通知，默认不追加明细。
        /// </summary>
        public virtual void BuildCheckoutLines(ShopCheckoutContext context)
        {
        }

        /// <summary>
        /// 返回调整后的付款金额，默认保持原金额。
        /// </summary>
        public virtual int ModifyPaidSilver(ShopCheckoutContext context, int paidSilver)
        {
            return paidSilver;
        }

        /// <summary>
        /// 在付款完成后接收通知，默认不执行额外逻辑。
        /// </summary>
        public virtual void AfterCheckoutPaid(ShopCheckoutContext context)
        {
        }

        /// <summary>
        /// 在结账失败或超时时接收通知，默认不执行额外逻辑。
        /// </summary>
        public virtual void OnCheckoutFailed(ShopCheckoutContext context)
        {
        }

        /// <summary>
        /// 判断顾客是否可以进入结账阶段，默认允许。
        /// </summary>
        public virtual bool CanPawnEnterCheckout(ShopCheckoutReadinessContext context)
        {
            return true;
        }
    }

    /// <summary>
    /// 提供收银结账管线的公开注册入口，负责安全调用外部结账 Worker。
    /// </summary>
    public static class SimShopCheckoutApi
    {
        private static readonly List<ShopCheckoutWorker> Workers = new List<ShopCheckoutWorker>();

        /// <summary>
        /// 注册一个结账 Worker，负责让外部模组接入收银流程。
        /// </summary>
        public static bool RegisterCheckoutWorker(ShopCheckoutWorker worker)
        {
            if (worker == null || Workers.Contains(worker)) return false;
            Workers.Add(worker);
            return true;
        }

        /// <summary>
        /// 注销一个结账 Worker。
        /// </summary>
        public static bool UnregisterCheckoutWorker(ShopCheckoutWorker worker)
        {
            return worker != null && Workers.Remove(worker);
        }

        /// <summary>
        /// 判断顾客是否允许进入结账阶段，负责让餐厅、定制服务等扩展能暂缓结账。
        /// </summary>
        public static bool CanPawnEnterCheckout(ShopCheckoutReadinessContext context)
        {
            if (context == null) return true;

            for (int i = 0; i < Workers.Count; i++)
            {
                try
                {
                    if (!Workers[i].CanPawnEnterCheckout(context))
                    {
                        if (context.allowed)
                            context.Defer("");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[SimShop.Checkout] 外部结账 Worker 在准备结账判断阶段执行失败: {ex}");
                }
            }

            return context.allowed;
        }

        /// <summary>
        /// 安全触发提交前 Hook。
        /// </summary>
        public static void NotifyBeforeCheckoutCommit(ShopCheckoutContext context)
        {
            InvokeWorker("结账提交前", worker => worker.BeforeCheckoutCommit(context));
            SimShopEvents.NotifyCheckoutBeforeCommit(context);
        }

        /// <summary>
        /// 安全触发账单明细 Hook。
        /// </summary>
        public static void NotifyBuildCheckoutLines(ShopCheckoutContext context)
        {
            InvokeWorker("结账明细构建", worker => worker.BuildCheckoutLines(context));
        }

        /// <summary>
        /// 安全执行付款金额调整 Hook。
        /// </summary>
        public static int ModifyPaidSilver(ShopCheckoutContext context, int paidSilver)
        {
            int result = paidSilver;
            InvokeWorker("结账金额调整", worker => result = Math.Max(0, worker.ModifyPaidSilver(context, result)));
            return result;
        }

        /// <summary>
        /// 安全触发付款完成 Hook。
        /// </summary>
        public static void NotifyCheckoutPaid(ShopCheckoutContext context)
        {
            InvokeWorker("结账付款完成", worker => worker.AfterCheckoutPaid(context));
            SimShopEvents.NotifyCheckoutPaid(context);
        }

        /// <summary>
        /// 安全触发结账失败 Hook。
        /// </summary>
        public static void NotifyCheckoutFailed(ShopCheckoutContext context)
        {
            InvokeWorker("结账失败", worker => worker.OnCheckoutFailed(context));
            SimShopEvents.NotifyCheckoutFailed(context);
        }

        /// <summary>
        /// 逐个调用外部 Worker，并隔离外部异常。
        /// </summary>
        private static void InvokeWorker(string stage, Action<ShopCheckoutWorker> action)
        {
            for (int i = 0; i < Workers.Count; i++)
            {
                try
                {
                    action?.Invoke(Workers[i]);
                }
                catch (Exception ex)
                {
                    Log.Error($"[SimShop.Checkout] 外部结账 Worker 在 {stage} 阶段执行失败: {ex}");
                }
            }
        }
    }
}
