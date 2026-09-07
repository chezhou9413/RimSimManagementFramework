using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.Models;
using RimSimRestaurantExtension.Tool;
using Verse;
namespace RimSimRestaurantExtension.Dining
{
    //描述整次用餐会话的独立生命周期。
    public enum RestaurantSessionState { Seating, Serving, AwaitingCheckout, Completed, Canceled, Failed }

    //保存顾客的一次用餐会话，职责是独占餐位并聚合多张商品子订单。
    public sealed class RestaurantDiningSession : IExposable
    {
        public int sessionId, actionOrderId = -1, customerId, shopId, mapId;
        public int anchorOrderId, currentOrderId = -1, rounds, lastOrderTick, completedTick;
        public int tableId;
        public IntVec3 seat;
        public bool stopOrdering, billQueued;
        public RestaurantSessionState state;
        public string reason = "";
        public RestaurantTableTray tray;
        public List<int> orderIds = new List<int>();
        public IEnumerable<RestaurantOrder> Orders => orderIds.Select(id => RestaurantOrderUtility.OrderManager.GetOrder(id)).Where(o => o != null);
        public RestaurantOrder Anchor => RestaurantOrderUtility.OrderManager.GetOrder(anchorOrderId);
        public bool IsTerminal => state == RestaurantSessionState.Completed || state == RestaurantSessionState.Canceled || state == RestaurantSessionState.Failed;
        public bool ReadyForCheckout => stopOrdering && Orders.All(o => o.IsTerminal || o.mealConsumed);
        public float Committed => Orders.Where(o => o.menuConfirmed && !o.IsTerminal).Sum(o => o.price);
        public float Amount => Orders.Sum(o => o.unitPrice * o.acceptedCount);
        public float Cost => Orders.Sum(o => o.ingredientCost);
        public float ExpectedNutrition => Orders.Where(o => !o.IsTerminal && o.mode == Inventory.RestaurantDeliveryMode.Consume)
            .Sum(o => System.Math.Max(0, o.mealCount - o.acceptedCount) * (o.mealDef?.ingestible?.CachedNutrition ?? 0f));

        //保存会话归属和追加状态，职责是避免读档重复接单或记账。
        public void ExposeData()
        {
            Scribe_Values.Look(ref sessionId, "sessionId");
            Scribe_Values.Look(ref actionOrderId, "actionOrderId", -1);
            Scribe_Values.Look(ref customerId, "customerId");
            Scribe_Values.Look(ref shopId, "shopId");
            Scribe_Values.Look(ref mapId, "mapId");
            Scribe_Values.Look(ref anchorOrderId, "anchorOrderId");
            Scribe_Values.Look(ref currentOrderId, "currentOrderId", -1);
            Scribe_Values.Look(ref rounds, "rounds");
            Scribe_Values.Look(ref lastOrderTick, "lastOrderTick");
            Scribe_Values.Look(ref completedTick, "completedTick");
            Scribe_Values.Look(ref tableId, "tableId");
            Scribe_Values.Look(ref seat, "seat");
            Scribe_Values.Look(ref stopOrdering, "stopOrdering");
            Scribe_Values.Look(ref billQueued, "billQueued");
            Scribe_Values.Look(ref state, "state");
            Scribe_Values.Look(ref reason, "reason", "");
            Scribe_References.Look(ref tray, "tray");
            Scribe_Collections.Look(ref orderIds, "orderIds", LookMode.Value);
        }
    }
}
