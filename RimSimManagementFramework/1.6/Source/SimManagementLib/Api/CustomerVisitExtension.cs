using SimManagementLib.SimAI.CustomerVisit;
using SimManagementLib.SimZone;
using Verse;

namespace SimManagementLib.Api
{
    /// <summary>
    /// 提供顾客访问流程扩展基类，负责让外部玩法只在明确阶段影响顾客状态机。
    /// </summary>
    public abstract class CustomerVisitExtension
    {
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
