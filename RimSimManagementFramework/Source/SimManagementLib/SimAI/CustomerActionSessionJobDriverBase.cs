using SimManagementLib.Api;
using SimManagementLib.SimDef;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimAI
{
    /// <summary>
    /// 提供员工参与顾客动作会话的 JobDriver 基类，负责恢复订单、顾客、会话 Worker 和员工加入状态。
    /// </summary>
    public abstract class CustomerActionSessionJobDriverBase : JobDriver
    {
        protected CustomerActionOrder order;
        protected CustomerActionSessionWorker sessionWorker;
        protected CustomerActionContext actionContext;
        protected Pawn customer;

        /// <summary>
        /// 读取并校验会话订单，负责让子类专注具体服务流程。
        /// </summary>
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            ResolveSessionContext();
            return order != null && sessionWorker != null && customer != null && CanContinueSession();
        }

        /// <summary>
        /// 构建会话服务 Toil 序列，并在前后统一处理员工加入、离开和完成。
        /// </summary>
        protected override IEnumerable<Toil> MakeNewToils()
        {
            ResolveSessionContext();
            AddFinishAction(condition =>
            {
                if (order != null && sessionWorker != null)
                    sessionWorker.NotifyStaffLeft(actionContext, pawn, condition.ToString());
            });
            this.FailOn(() => !CanContinueSession());
            yield return MakeJoinToil();

            IEnumerable<Toil> toils = MakeSessionToils();
            if (toils != null)
            {
                foreach (Toil toil in toils)
                    yield return toil;
            }

            yield return MakeCompleteToil();
        }

        /// <summary>
        /// 构建会话服务的具体 Toil，由外部继承类实现。
        /// </summary>
        protected abstract IEnumerable<Toil> MakeSessionToils();

        /// <summary>
        /// 判断会话是否仍可继续，默认要求订单未结束且上下文有效。
        /// </summary>
        protected virtual bool CanContinueSession()
        {
            return order != null && order.IsActiveState && actionContext != null && sessionWorker != null;
        }

        /// <summary>
        /// 在会话完成前接收通知，默认不执行额外逻辑。
        /// </summary>
        protected virtual void OnSessionCompleted()
        {
        }

        /// <summary>
        /// 构建员工加入 Toil，负责把员工登记进订单并通知 Worker。
        /// </summary>
        private Toil MakeJoinToil()
        {
            Toil toil = new Toil();
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            toil.initAction = () =>
            {
                ResolveSessionContext();
                if (order == null || sessionWorker == null) return;
                SimShopCustomerApi.TryAssignActionOrderStaff(pawn, order);
                sessionWorker.NotifyStaffJoined(actionContext, pawn);
            };
            return toil;
        }

        /// <summary>
        /// 构建会话完成 Toil，负责调用完成回调并推进订单状态。
        /// </summary>
        private Toil MakeCompleteToil()
        {
            Toil toil = new Toil();
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            toil.initAction = () =>
            {
                ResolveSessionContext();
                if (order == null || sessionWorker == null) return;
                OnSessionCompleted();
                sessionWorker.NotifySessionCompleted(actionContext);
                SimShopCustomerApi.CompleteActionOrder(order);
            };
            return toil;
        }

        /// <summary>
        /// 根据 Job 订单编号恢复会话上下文。
        /// </summary>
        private void ResolveSessionContext()
        {
            if (order == null)
                order = SimShopCustomerApi.GetActionOrder(job?.count ?? 0);
            if (order == null) return;

            CustomerActionDef actionDef = order.ActionDef;
            sessionWorker = actionDef?.Worker as CustomerActionSessionWorker;
            customer = SimShopCustomerApi.FindActionOrderCustomer(pawn?.Map, order);
            if (actionContext == null && customer != null)
                actionContext = SimShopCustomerApi.BuildActionContext(customer, order);
        }
    }
}
