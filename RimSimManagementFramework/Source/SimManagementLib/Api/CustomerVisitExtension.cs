using SimManagementLib.SimAI.CustomerVisit;
using SimManagementLib.SimZone;
using Verse;
using Verse.AI;
using SimManagementLib.Pojo;
using System.Collections.Generic;

namespace SimManagementLib.Api
{
    /// <summary>
    /// 提供顾客访问流程扩展基类，负责让外部玩法只在明确阶段影响顾客状态机。
    /// </summary>
    public abstract class CustomerVisitExtension
    {
        //判断该顾客是否由扩展保留长期入住。
        public virtual bool HasLongStay(Pawn customer) => false;

        //取得住客当前需要执行的生活任务。
        public virtual Job MakeLongStayJob(Pawn customer) => null;

        //在顾客紧急离开经营流程前释放住宿和寄存物。
        public virtual void OnLongStayInterrupted(Pawn customer, string reason) { }

        //读取实际钱包总额，待付款由框架统一扣除。
        public virtual int GetAvailableFunds(Pawn customer) => 0;

        //原子扣除住客钱包，失败时返回明确原因。
        public virtual bool TrySpendFunds(Pawn customer, int amount, out string reason)
        {
            reason = "长期顾客扩展未提供付款实现";
            return false;
        }

        //把退回的预付款返还实际钱包。
        public virtual void RefundFunds(Pawn customer, int amount) { }

        //在生成顾客前预留同行人数和设施；返回 null 表示不接管此次到客。
        public virtual CustomerArrivalReservation PrepareArrival(Zone_Shop shop, RuntimeCustomerKind kind, int availablePlaces) => null;

        //所有同行顾客生成成功后交付同一预订。
        public virtual void CompleteArrival(Zone_Shop shop, RuntimeCustomerKind kind, CustomerArrivalReservation reservation, List<Pawn> customers) { }

        //生成失败时释放尚未交付的预订。
        public virtual void CancelArrival(Zone_Shop shop, CustomerArrivalReservation reservation, string reason) { }
        /// <summary>
        /// 顾客 Session 切换阶段后调用，负责让扩展记录长期会话或同步外部状态。
        /// </summary>
        public virtual void OnStageChanged(CustomerVisitExtensionContext context)
        {
        }

        /// <summary>
        /// 顾客 Session 周期 Tick 时调用，负责让扩展追加周期费用等长期行为。
        /// </summary>
        public virtual void TickLongStay(CustomerVisitExtensionContext context)
        {
        }

        /// <summary>
        /// 判断是否暂缓普通自动结账，不能阻止饥饿、倒地等生存安全兜底。
        /// </summary>
        public virtual bool ShouldDelayCheckout(CustomerVisitExtensionContext context)
        {
            return false;
        }

        /// <summary>
        /// 判断是否暂缓普通自动离店，不能阻止饥饿、倒地等生存安全兜底。
        /// </summary>
        public virtual bool ShouldDelayLeave(CustomerVisitExtensionContext context)
        {
            return false;
        }
    }

    //保存一次同步到客的容量和扩展预订标识，不承担长期业务状态。
    public sealed class CustomerArrivalReservation
    {
        public int count = 1;
        public string token = "";
        internal CustomerVisitExtension owner;
    }

    /// <summary>
    /// 保存顾客访问扩展调用上下文，负责向外部玩法公开稳定的只读状态和必要操作入口。
    /// </summary>
    public class CustomerVisitExtensionContext
    {
        public Pawn customer;
        public CustomerVisitSession session;
        public Zone_Shop shop;
        public int pawnId;
        public CustomerVisitStage stage;
        public float remainingBudget;
        public string reason = "";
        public int currentTick;

        /// <summary>
        /// 向顾客当前账单追加金额，负责让扩展通过受控入口计入周期费用。
        /// </summary>
        public SimApiResult AddBill(float amount)
        {
            return SimShopCustomerApi.AddCustomerBill(customer, amount);
        }

        /// <summary>
        /// 标记顾客进入结账流程，负责让扩展在到期时交回默认收银逻辑。
        /// </summary>
        public SimApiResult MarkReadyForCheckout()
        {
            return SimShopCustomerApi.MarkCustomerReadyForCheckout(customer);
        }
    }
}
