using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.Models;
using RimSimRestaurantExtension.Tool;
using RimWorld;
using SimManagementLib.Api;
using SimManagementLib.SimZone;
using Verse;
using Verse.AI;

namespace RimSimRestaurantExtension.WorkGivers
{
    //为餐厅厨师分配订单，职责是扫描兼容工作台并在不提前改状态的前提下构造完整取料 Job。
    public class WorkGiver_CookRestaurantOrder : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.PotentialBillGiver);
        public override PathEndMode PathEndMode => PathEndMode.InteractionCell;

        //返回地图上具有可制作订单的工作台，职责是支持原版及模组兼容灶台。
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            if (pawn?.Map == null) return Enumerable.Empty<Thing>();
            return pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.PotentialBillGiver)
                .OfType<Building_WorkTable>()
                .Where(thing => FindOrderFor(pawn, thing) != null);
        }

        //判断工作台是否存在当前厨师可制作的订单，职责是执行岗位、商店、设施与食材过滤。
        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            return FindOrderFor(pawn, thing) != null;
        }

        //创建烹饪 Job，职责是把全部订单食材及其数量写入队列而不在预约前认领订单。
        public override Job JobOnThing(Pawn pawn, Thing stove, bool forced = false)
        {
            Building_WorkTable table = stove as Building_WorkTable;
            RestaurantOrder order = FindOrderFor(pawn, table);
            Zone_Shop shop = SimShopServiceApi.FindShop(table?.Map, table?.Position ?? IntVec3.Invalid);
            if (order == null || shop == null || DefOfRefs.RSR_CookRestaurantOrder == null) return null;
            if (!RestaurantIngredientUtility.TryFindIngredientThingCounts(pawn, shop, order, out List<ThingCount> ingredients, out _))
                return null;

            Job job = JobMaker.MakeJob(DefOfRefs.RSR_CookRestaurantOrder, table);
            job.workGiverDef = DefOfRefs.RSR_WorkGiver_CookRestaurantOrder;
            RestaurantJobUtility.SetOrderId(job, order.orderId);
            job.countQueue = new List<int>();
            for (int i = 0; i < ingredients.Count; i++)
            {
                ThingCount ingredient = ingredients[i];
                if (ingredient.Thing == null || ingredient.Count <= 0) continue;
                job.AddQueuedTarget(TargetIndex.B, ingredient.Thing);
                job.countQueue.Add(ingredient.Count);
            }
            return job.targetQueueB.NullOrEmpty() ? null : job;
        }

        //查找工作台对应商店的最早订单，职责是通过新版公开 API 检查岗位分配和设施可用性。
        private static RestaurantOrder FindOrderFor(Pawn pawn, Thing stove)
        {
            if (pawn?.Map == null || !(stove is Building_WorkTable table) || table.Destroyed || !table.Spawned)
                return null;
            Zone_Shop shop = SimShopServiceApi.FindShop(table.Map, table.Position);
            if (shop == null || !RestaurantOrderUtility.CanRestaurantStaffWorkAt(shop)) return null;
            if (!RestaurantCookingUtility.IsWorkCellInsideShop(shop, table)) return null;
            if (DefOfRefs.RSR_WorkGiver_CookRestaurantOrder != null
                && !SimShopStaffApi.IsAssignedToWorkGiver(shop, pawn, DefOfRefs.RSR_WorkGiver_CookRestaurantOrder))
            {
                return null;
            }
            if (!pawn.CanReserveAndReach(table, PathEndMode.InteractionCell, Danger.Some)) return null;
            return RestaurantOrderUtility.FindCookOrder(pawn, shop, table);
        }
    }
}
