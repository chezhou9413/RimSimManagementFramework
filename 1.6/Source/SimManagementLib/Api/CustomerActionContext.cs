using SimManagementLib.Pojo;
using SimManagementLib.SimAI;
using SimManagementLib.SimDef;
using SimManagementLib.SimZone;
using Verse;

namespace SimManagementLib.Api
{
    /// <summary>
    /// 提供顾客动作 Worker 的运行上下文，负责暴露顾客、商店、预算、顾客类型和本次访问状态。
    /// </summary>
    public class CustomerActionContext
    {
        public Pawn customer;
        public Zone_Shop shop;
        public LordJob_CustomerVisit visit;
        public CustomerActionDef actionDef;
        public RuntimeCustomerKind customerKind;
        public CustomerActionOrder order;
        public int actionOrderId;
        public int pawnId;
        public float remainingBudget;
        public int currentTick;

        /// <summary>
        /// 标记顾客准备结账，负责让外部动作结束顾客浏览阶段。
        /// </summary>
        public void MarkReadyForCheckout()
        {
            visit?.MarkPawnReadyForCheckout(pawnId);
        }

        /// <summary>
        /// 记录一次消费动作，负责复用框架的消费次数上限。
        /// </summary>
        public bool RegisterConsumptionActionAndShouldCheckout()
        {
            return visit?.RegisterConsumptionActionAndShouldCheckout(pawnId) == true;
        }

        /// <summary>
        /// 判断外部动作结束后是否应该进入结账，负责复用自然浏览退出决策。
        /// </summary>
        public bool ShouldCheckoutAfterAction(string reason)
        {
            return visit?.ShouldCheckoutFromCurrentShop(customer, shop, reason) == true;
        }

        /// <summary>
        /// 向顾客本次账单追加金额，负责让外部动作接入现有收银结账流程。
        /// </summary>
        public void AddBill(float amount)
        {
            if (visit == null || amount <= 0f) return;
            if (!visit.cartValues.ContainsKey(pawnId))
                visit.cartValues[pawnId] = 0f;
            visit.cartValues[pawnId] += amount;
        }

        /// <summary>
        /// 计入账单并记录一次消费动作，负责让外部行为快速接入预算与结账推进。
        /// </summary>
        public void AddBillAndRegisterConsumption(float amount)
        {
            AddBill(amount);
            if (RegisterConsumptionActionAndShouldCheckout() || ShouldCheckoutAfterAction("外部动作消费完成"))
                MarkReadyForCheckout();
        }

        /// <summary>
        /// 返回动作订单引用的地图目标，负责让外部 JobDriver 安全恢复目标建筑。
        /// </summary>
        public Thing FindTargetThing()
        {
            if (shop?.Map == null || order == null || order.targetThingId < 0) return null;
            foreach (Thing thing in shop.Map.listerThings.AllThings)
            {
                if (thing != null && thing.thingIDNumber == order.targetThingId)
                    return thing;
            }
            return null;
        }

        /// <summary>
        /// 完成当前动作订单，负责让外部 Worker 不直接操作订单状态。
        /// </summary>
        public SimApiResult CompleteOrder()
        {
            return SimShopCustomerApi.CompleteActionOrder(order);
        }

        /// <summary>
        /// 取消当前动作订单，负责让外部 Worker 用统一入口结束失败流程。
        /// </summary>
        public SimApiResult CancelOrder(string reason, bool failed = false)
        {
            return SimShopCustomerApi.CancelActionOrder(order, reason, failed);
        }
    }
}
