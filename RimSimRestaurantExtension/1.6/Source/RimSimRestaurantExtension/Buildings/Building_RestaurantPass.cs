using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.Tool;
using Verse;

namespace RimSimRestaurantExtension.Buildings
{
    //保存各订单的独立餐品，职责是提供可存档出餐容器并交由原版持有者机制推进物品生命周期。
    public class Building_RestaurantPass : Building, IThingHolder
    {
        private ThingOwner<Thing> meals;

        //构造出餐容器，职责是不合并不同订单的堆叠。
        public Building_RestaurantPass()
        {
            meals = new ThingOwner<Thing>(this, false);
        }

        //返回直接持有餐品，职责是接入存档、搬运和原版容器物品更新。
        public ThingOwner GetDirectlyHeldThings()
        {
            return meals;
        }

        //枚举子持有者，职责是让原版正确遍历容器内部对象。
        public void GetChildHolders(List<IThingHolder> children)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(children, meals);
        }

        //读写实物容器，职责是保留订单引用对应的同一件餐品。
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref meals, "restaurantMeals", this);
        }

        //处理拆除与卸载，职责是终止依赖此台的订单并释放所有实物。
        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            foreach (var order in RestaurantOrderUtility.OrderManager?.Orders
                .Where(item => !item.IsTerminal && item.state != Models.RestaurantOrderState.AwaitingCheckout
                    && item.providerThingId == thingIDNumber).ToList()
                ?? new List<Models.RestaurantOrder>())
                RestaurantOrderUtility.FailOrder(order, "接待与出餐台已移除");
            if (Spawned) meals.TryDropAll(Position, Map, ThingPlaceMode.Near);
            base.DeSpawn(mode);
        }

        //显示待出餐数量，职责是帮助玩家确认厨师已经交入实物。
        public override string GetInspectString()
        {
            return base.GetInspectString() + "\n待取餐品：" + meals.Count;
        }
    }
}
