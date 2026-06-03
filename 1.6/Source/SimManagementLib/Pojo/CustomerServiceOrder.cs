using SimManagementLib.SimService;
using Verse;

namespace SimManagementLib.Pojo
{
    /// <summary>
    /// 保存顾客一次服务消费的订单数据，负责在存档中追踪服务定义、建筑、费用、计费模式和订单状态。
    /// </summary>
    public class CustomerServiceOrder : IExposable
    {
        public int orderId;
        public string serviceDefName = "";
        public int providerThingId = -1;
        public string providerLabel = "";
        public int count = 1;
        public float unitPrice;
        public float totalPrice;
        public ServiceBillingMode billingMode = ServiceBillingMode.PayBeforeUse;
        public ServiceOrderState state = ServiceOrderState.Draft;
        public int reservedTick;
        public int startedTick;
        public int completedTick;
        public int paidTick;
        public bool reviewEnqueued;

        /// <summary>
        /// 返回该订单是否已经发生实际服务使用，发生后不能像未使用订单一样直接取消。
        /// </summary>
        public bool HasBeenUsed
        {
            get
            {
                return state == ServiceOrderState.InUse
                    || state == ServiceOrderState.UsedAwaitingPayment
                    || state == ServiceOrderState.Completed
                    || state == ServiceOrderState.CheckoutFailed;
            }
        }

        /// <summary>
        /// 将服务订单读写到存档，缺失字段会使用兼容旧存档的安全默认值。
        /// </summary>
        public void ExposeData()
        {
            Scribe_Values.Look(ref orderId, "orderId", 0);
            Scribe_Values.Look(ref serviceDefName, "serviceDefName", "");
            Scribe_Values.Look(ref providerThingId, "providerThingId", -1);
            Scribe_Values.Look(ref providerLabel, "providerLabel", "");
            Scribe_Values.Look(ref count, "count", 1);
            Scribe_Values.Look(ref unitPrice, "unitPrice", 0f);
            Scribe_Values.Look(ref totalPrice, "totalPrice", 0f);
            Scribe_Values.Look(ref billingMode, "billingMode", ServiceBillingMode.PayBeforeUse);
            Scribe_Values.Look(ref state, "state", ServiceOrderState.Draft);
            Scribe_Values.Look(ref reservedTick, "reservedTick", 0);
            Scribe_Values.Look(ref startedTick, "startedTick", 0);
            Scribe_Values.Look(ref completedTick, "completedTick", 0);
            Scribe_Values.Look(ref paidTick, "paidTick", 0);
            Scribe_Values.Look(ref reviewEnqueued, "reviewEnqueued", false);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (count <= 0) count = 1;
                if (unitPrice < 0f) unitPrice = 0f;
                if (totalPrice < 0f) totalPrice = 0f;
                if (string.IsNullOrEmpty(providerLabel)) providerLabel = "服务建筑";
            }
        }
    }
}
