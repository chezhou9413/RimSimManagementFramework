using SimManagementLib.SimService;
using System.Collections.Generic;
using Verse;

namespace SimManagementLib.Api
{
    /// <summary>
    /// 描述现做订单从创建、付款、制作到交付的公开状态。
    /// </summary>
    public enum PreparedShopOrderState
    {
        Draft,
        AwaitingPayment,
        PaidWaitingPreparation,
        Assigned,
        Preparing,
        ReadyForPickup,
        Delivered,
        Canceled,
        Failed
    }

    /// <summary>
    /// 保存外部模组可读写的现做订单数据，负责跨存档追踪顾客、商店、服务、产物和制作状态。
    /// </summary>
    public class PreparedShopOrder : IExposable
    {
        public int orderId;
        public int customerThingId = -1;
        public int sourceServiceOrderId = -1;
        public int shopZoneId = -1;
        public string serviceDefName = "";
        public int providerThingId = -1;
        public string providerLabel = "";
        public ThingDef resultThingDef;
        public int resultCount = 1;
        public float totalPrice;
        public PreparedShopOrderState state = PreparedShopOrderState.Draft;
        public int createdTick;
        public int paidTick;
        public int assignedTick;
        public int startedTick;
        public int completedTick;
        public int staffThingId = -1;
        public string externalData = "";

        /// <summary>
        /// 返回订单引用的服务定义，缺失时返回 null。
        /// </summary>
        public ShopServiceDef ServiceDef => DefDatabase<ShopServiceDef>.GetNamedSilentFail(serviceDefName);

        /// <summary>
        /// 返回订单是否仍可被员工认领或继续制作。
        /// </summary>
        public bool IsActivePreparationState
        {
            get
            {
                return state == PreparedShopOrderState.PaidWaitingPreparation
                    || state == PreparedShopOrderState.Assigned
                    || state == PreparedShopOrderState.Preparing;
            }
        }

        /// <summary>
        /// 读写现做订单存档数据，并在读档后补齐安全默认值。
        /// </summary>
        public void ExposeData()
        {
            Scribe_Values.Look(ref orderId, "orderId", 0);
            Scribe_Values.Look(ref customerThingId, "customerThingId", -1);
            Scribe_Values.Look(ref sourceServiceOrderId, "sourceServiceOrderId", -1);
            Scribe_Values.Look(ref shopZoneId, "shopZoneId", -1);
            Scribe_Values.Look(ref serviceDefName, "serviceDefName", "");
            Scribe_Values.Look(ref providerThingId, "providerThingId", -1);
            Scribe_Values.Look(ref providerLabel, "providerLabel", "");
            Scribe_Defs.Look(ref resultThingDef, "resultThingDef");
            Scribe_Values.Look(ref resultCount, "resultCount", 1);
            Scribe_Values.Look(ref totalPrice, "totalPrice", 0f);
            Scribe_Values.Look(ref state, "state", PreparedShopOrderState.Draft);
            Scribe_Values.Look(ref createdTick, "createdTick", 0);
            Scribe_Values.Look(ref paidTick, "paidTick", 0);
            Scribe_Values.Look(ref assignedTick, "assignedTick", 0);
            Scribe_Values.Look(ref startedTick, "startedTick", 0);
            Scribe_Values.Look(ref completedTick, "completedTick", 0);
            Scribe_Values.Look(ref staffThingId, "staffThingId", -1);
            Scribe_Values.Look(ref externalData, "externalData", "");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (resultCount <= 0) resultCount = 1;
                if (providerLabel == null) providerLabel = "";
                if (serviceDefName == null) serviceDefName = "";
                if (externalData == null) externalData = "";
            }
        }
    }

    /// <summary>
    /// 描述现做订单查询条件，负责让外部模组用稳定结构筛选订单。
    /// </summary>
    public class PreparedShopOrderQuery
    {
        public int shopZoneId = -1;
        public int customerThingId = -1;
        public int staffThingId = -1;
        public string serviceDefName = "";
        public List<PreparedShopOrderState> states = new List<PreparedShopOrderState>();
        public bool includeTerminalOrders = true;

        /// <summary>
        /// 判断指定订单是否符合查询条件。
        /// </summary>
        public bool Matches(PreparedShopOrder order)
        {
            if (order == null) return false;
            if (shopZoneId >= 0 && order.shopZoneId != shopZoneId) return false;
            if (customerThingId >= 0 && order.customerThingId != customerThingId) return false;
            if (staffThingId >= 0 && order.staffThingId != staffThingId) return false;
            if (!string.IsNullOrEmpty(serviceDefName) && order.serviceDefName != serviceDefName) return false;
            if (states != null && states.Count > 0 && !states.Contains(order.state)) return false;
            if (!includeTerminalOrders && IsTerminal(order.state)) return false;
            return true;
        }

        /// <summary>
        /// 判断状态是否代表订单已经结束。
        /// </summary>
        private static bool IsTerminal(PreparedShopOrderState state)
        {
            return state == PreparedShopOrderState.Delivered
                || state == PreparedShopOrderState.Canceled
                || state == PreparedShopOrderState.Failed;
        }
    }

    /// <summary>
    /// 表示现做订单 API 的统一返回值，负责携带成功标记、订单对象和失败说明。
    /// </summary>
    public class PreparedShopOrderResult
    {
        public bool success;
        public PreparedShopOrder order;
        public string failReason = "";

        /// <summary>
        /// 创建成功结果。
        /// </summary>
        public static PreparedShopOrderResult Success(PreparedShopOrder order)
        {
            return new PreparedShopOrderResult { success = true, order = order, failReason = "" };
        }

        /// <summary>
        /// 创建失败结果。
        /// </summary>
        public static PreparedShopOrderResult Fail(string reason)
        {
            return new PreparedShopOrderResult { success = false, order = null, failReason = reason ?? "" };
        }
    }
}
