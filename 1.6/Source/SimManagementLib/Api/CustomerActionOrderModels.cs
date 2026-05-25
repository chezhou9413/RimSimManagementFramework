using SimManagementLib.SimDef;
using System.Collections.Generic;
using Verse;

namespace SimManagementLib.Api
{
    /// <summary>
    /// 描述顾客外部动作订单从创建、执行、结账到结束的公开状态。
    /// </summary>
    public enum CustomerActionOrderState
    {
        Draft,
        Active,
        WaitingStaff,
        InProgress,
        AwaitingCheckout,
        Completed,
        Canceled,
        Failed
    }

    /// <summary>
    /// 保存顾客外部动作订单，负责跨 Job、跨存档追踪复杂顾客玩法的业务状态。
    /// </summary>
    public class CustomerActionOrder : IExposable
    {
        public int orderId;
        public int customerThingId = -1;
        public int shopZoneId = -1;
        public string actionDefName = "";
        public int targetThingId = -1;
        public string targetLabel = "";
        public float billAmount;
        public CustomerActionOrderState state = CustomerActionOrderState.Draft;
        public int createdTick;
        public int startedTick;
        public int completedTick;
        public int staffThingId = -1;
        public List<int> staffThingIds = new List<int>();
        public string externalData = "";

        /// <summary>
        /// 返回动作订单引用的动作 Def，缺失时返回 null。
        /// </summary>
        public CustomerActionDef ActionDef => DefDatabase<CustomerActionDef>.GetNamedSilentFail(actionDefName);

        /// <summary>
        /// 返回订单是否处于仍需要外部 Job 或员工推进的运行状态。
        /// </summary>
        public bool IsActiveState
        {
            get
            {
                return state == CustomerActionOrderState.Active
                    || state == CustomerActionOrderState.WaitingStaff
                    || state == CustomerActionOrderState.InProgress
                    || state == CustomerActionOrderState.AwaitingCheckout;
            }
        }

        /// <summary>
        /// 读写顾客动作订单，并在读档后补齐安全默认值。
        /// </summary>
        public void ExposeData()
        {
            Scribe_Values.Look(ref orderId, "orderId", 0);
            Scribe_Values.Look(ref customerThingId, "customerThingId", -1);
            Scribe_Values.Look(ref shopZoneId, "shopZoneId", -1);
            Scribe_Values.Look(ref actionDefName, "actionDefName", "");
            Scribe_Values.Look(ref targetThingId, "targetThingId", -1);
            Scribe_Values.Look(ref targetLabel, "targetLabel", "");
            Scribe_Values.Look(ref billAmount, "billAmount", 0f);
            Scribe_Values.Look(ref state, "state", CustomerActionOrderState.Draft);
            Scribe_Values.Look(ref createdTick, "createdTick", 0);
            Scribe_Values.Look(ref startedTick, "startedTick", 0);
            Scribe_Values.Look(ref completedTick, "completedTick", 0);
            Scribe_Values.Look(ref staffThingId, "staffThingId", -1);
            Scribe_Collections.Look(ref staffThingIds, "staffThingIds", LookMode.Value);
            Scribe_Values.Look(ref externalData, "externalData", "");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (actionDefName == null) actionDefName = "";
                if (targetLabel == null) targetLabel = "";
                if (staffThingIds == null) staffThingIds = new List<int>();
                if (staffThingId >= 0 && !staffThingIds.Contains(staffThingId))
                    staffThingIds.Add(staffThingId);
                if (externalData == null) externalData = "";
            }
        }
    }

    /// <summary>
    /// 描述顾客动作订单查询条件，负责让外部模组用稳定结构筛选动作订单。
    /// </summary>
    public class CustomerActionOrderQuery
    {
        public int shopZoneId = -1;
        public int customerThingId = -1;
        public int staffThingId = -1;
        public string actionDefName = "";
        public List<CustomerActionOrderState> states = new List<CustomerActionOrderState>();
        public bool includeTerminalOrders = true;

        /// <summary>
        /// 判断订单是否符合查询条件。
        /// </summary>
        public bool Matches(CustomerActionOrder order)
        {
            if (order == null) return false;
            if (shopZoneId >= 0 && order.shopZoneId != shopZoneId) return false;
            if (customerThingId >= 0 && order.customerThingId != customerThingId) return false;
            if (staffThingId >= 0 && order.staffThingId != staffThingId) return false;
            if (!string.IsNullOrEmpty(actionDefName) && order.actionDefName != actionDefName) return false;
            if (states != null && states.Count > 0 && !states.Contains(order.state)) return false;
            if (!includeTerminalOrders && IsTerminal(order.state)) return false;
            return true;
        }

        /// <summary>
        /// 判断动作订单状态是否已经结束。
        /// </summary>
        private static bool IsTerminal(CustomerActionOrderState state)
        {
            return state == CustomerActionOrderState.Completed
                || state == CustomerActionOrderState.Canceled
                || state == CustomerActionOrderState.Failed;
        }
    }
}
