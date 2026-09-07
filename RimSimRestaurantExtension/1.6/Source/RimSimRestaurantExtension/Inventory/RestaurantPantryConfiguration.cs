using RimWorld;
using System.Linq;
using RimSimRestaurantExtension.Tool;
using SimManagementLib.SimZone;
namespace RimSimRestaurantExtension.Inventory
{
    //响应后厨区绑定变化，职责是撤销不再属于本店货源的未取食材承诺。
    internal static class RestaurantPantryConfiguration
    {
        //检查绑定范围，职责是保留已取出的食材并取消尚在失效货源上的订单。
        public static void Apply(Zone_Shop shop)
        {
            foreach (var order in RestaurantOrderUtility.OrderManager.GetActiveOrders(shop.ID).Where(o => !o.mealProduced && o.menuConfirmed))
            {
                if (RestaurantOrderStock.Reserved(order, shop.Map).Any(t => t.Thing?.Spawned == true
                    && !RestaurantOrderStock.IsWithdrawn(order, t.Thing, shop.Map)
                    && !RestaurantStockUtility.Pantries(shop).Any(z => z.ContainsCell(t.Thing.Position))))
                    RestaurantOrderUtility.CancelOrder(order, "订单预留货源不再属于本店后厨绑定");
            }
        }
    }
}
