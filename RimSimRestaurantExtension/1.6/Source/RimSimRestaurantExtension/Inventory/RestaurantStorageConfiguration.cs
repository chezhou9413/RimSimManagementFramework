using System.Linq;
using RimSimRestaurantExtension.Buildings;
using RimSimRestaurantExtension.Tool;
namespace RimSimRestaurantExtension.Inventory
{
    //响应货柜配置变化，职责是释放无法继续取用的订单预留并刷新经营查询。
    public static class RestaurantStorageConfiguration
    {
        //核查失效预留，职责是不允许已经排除的物品仍被后台取走。
        public static void Apply(Building_RestaurantStorage cabinet)
        {
            foreach (var order in RestaurantOrderUtility.OrderManager.Orders.Where(o => !o.IsTerminal && !o.mealProduced))
            {
                if (order.mapId != cabinet.Map.uniqueID) continue;
                var reserved = RestaurantOrderStock.Reserved(order, cabinet.Map);
                if (reserved.Any(t => t.Thing?.ParentHolder == cabinet && !cabinet.AllowsInventoryItem(t.Thing.def)))
                    RestaurantOrderUtility.CancelOrder(order, "预留物品已被货柜筛选排除");
                else if (order.stockProduct && order.sourceCabinet == cabinet && !cabinet.Rule(order.mealDef).onSale)
                    RestaurantOrderUtility.CancelOrder(order, "已预留商品停止上架");
            }
            RestaurantBusinessAvailability.Reset();
            RestaurantMenuUtility.ResetSelections();
            cabinet.NotifyInventoryChanged();
        }
    }
}
