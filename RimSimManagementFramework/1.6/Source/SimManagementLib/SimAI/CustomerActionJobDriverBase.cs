using SimManagementLib.Api;
using SimManagementLib.SimDef;
using SimManagementLib.SimAI.CustomerVisit;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimAI
{
    /// <summary>
    /// 提供顾客动作 JobDriver 基类，负责按订单编号恢复上下文并统一处理开始、完成和失败通知。
    /// </summary>
    public abstract class CustomerActionJobDriverBase : JobDriver
    {
        protected CustomerActionOrder order;
        protected CustomerActionWorker worker;
        protected CustomerActionContext actionContext;

        /// <summary>
        /// 读取并校验顾客动作订单，负责让子类无需重复查找订单和 Worker。
        /// </summary>
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            ResolveActionContext();
            return order != null && worker != null && CanContinueAction();
        }

        /// <summary>
        /// 构建动作 Toil 序列，并在前后自动通知订单状态。
        /// </summary>
        protected override IEnumerable<Toil> MakeNewToils()
        {
            ResolveActionContext();
            AddFinishAction(condition =>
            {
                if (condition != JobCondition.Succeeded && order != null && order.IsActiveState)
                    OnActionFailed(condition.ToString());
            });
            this.FailOn(() => !CanContinueAction());
            yield return MakeStartToil();

            IEnumerable<Toil> toils = MakeActionToils();
            if (toils != null)
            {
                foreach (Toil toil in toils)
                    yield return toil;
            }

            yield return MakeCompleteToil();
        }

        /// <summary>
        /// 构建具体动作流程，由外部继承类实现。
        /// </summary>
        protected abstract IEnumerable<Toil> MakeActionToils();

        /// <summary>
        /// 判断动作是否仍可继续，默认要求订单和上下文有效。
        /// </summary>
        protected virtual bool CanContinueAction()
        {
            return order != null && actionContext != null && worker != null && order.IsActiveState;
        }

        /// <summary>
        /// 在动作开始后接收通知，默认不执行额外逻辑。
        /// </summary>
        protected virtual void OnActionStarted()
        {
        }

        /// <summary>
        /// 在动作完成前接收通知，默认不执行额外逻辑。
        /// </summary>
        protected virtual void OnActionCompleted()
        {
            LordJob_CustomerVisit visit = pawn?.Map?.lordManager?.LordOf(pawn)?.LordJob as LordJob_CustomerVisit;
            visit?.GetOrCreateSession(pawn)?.NotifyConsumptionCompleted(visit, pawn, "外部动作完成");
        }

        /// <summary>
        /// 在动作失败或被中断时接收通知，默认标记订单失败。
        /// </summary>
        protected virtual void OnActionFailed(string reason)
        {
            LordJob_CustomerVisit visit = pawn?.Map?.lordManager?.LordOf(pawn)?.LordJob as LordJob_CustomerVisit;
            visit?.GetOrCreateSession(pawn)?.NotifyNoProgressBrowse(visit, pawn, reason ?? "外部动作失败");
            if (order != null)
                SimShopCustomerApi.CancelActionOrder(order, reason, true);
        }

        /// <summary>
        /// 构建动作开始 Toil，负责推进订单状态和调用 Worker。
        /// </summary>
        private Toil MakeStartToil()
        {
            Toil toil = new Toil();
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            toil.initAction = () =>
            {
                ResolveActionContext();
                if (order == null || worker == null) return;
                SimShopCustomerApi.StartActionOrder(order);
                worker.NotifyOrderStarted(actionContext);
                OnActionStarted();
            };
            return toil;
        }

        /// <summary>
        /// 构建动作完成 Toil，负责调用子类、Worker 和 API 完成逻辑。
        /// </summary>
        private Toil MakeCompleteToil()
        {
            Toil toil = new Toil();
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            toil.initAction = () =>
            {
                ResolveActionContext();
                if (order == null || worker == null) return;
                OnActionCompleted();
                worker.NotifyOrderCompleted(actionContext);
                SimShopCustomerApi.CompleteActionOrder(order);
            };
            return toil;
        }

        /// <summary>
        /// 根据 Job 订单编号恢复上下文和 Worker。
        /// </summary>
        private void ResolveActionContext()
        {
            if (order == null)
                order = SimShopCustomerApi.GetActionOrder(job?.count ?? 0);
            if (order == null) return;

            CustomerActionDef actionDef = order.ActionDef;
            worker = actionDef?.Worker;
            if (actionContext == null)
                actionContext = SimShopCustomerApi.BuildActionContext(pawn, order);
        }
    }
}
