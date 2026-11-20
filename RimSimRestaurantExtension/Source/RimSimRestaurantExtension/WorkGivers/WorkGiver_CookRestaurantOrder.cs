using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.Models;
using RimSimRestaurantExtension.Tool;
using RimWorld;
using SimManagementLib.Api;
using SimManagementLib.SimZone;
using Verse;
using Verse.AI;
using SimManagementLib.Tool;

namespace RimSimRestaurantExtension.WorkGivers
{
    //为餐厅厨师分配订单，轻量筛选灶台并用单 tick 缓存复用 HasJobOnThing 与 JobOnThing 的食材查找结果。
    public class WorkGiver_CookRestaurantOrder : WorkGiver_Scanner
    {
        private int cachedTick = -1;
        private int cachedPawnId = -1;
        private int cachedStoveId = -1;
        private bool cachedForced;
        private RestaurantOrder cachedOrder;
        private List<ThingCount> cachedIngredients;

        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.PotentialBillGiver);
        public override PathEndMode PathEndMode => PathEndMode.InteractionCell;

        //跳过烹饪不可用、非上班时段或尚未到重试时点的厨师。
        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            if (!forced && ShopStaffUtility.IsPawnOffDutyForAutomaticWork(pawn))
                return true;
            return !RestaurantCookingUtility.CanPawnCook(pawn)
                || RestaurantOrderUtility.OrderManager?.CanDispatchCooking(pawn) != true;
        }

        //仅做廉价的店铺与岗位过滤，避免枚举阶段对全图每个工作台都执行可达性与食材搜索。
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            if (pawn?.Map == null) yield break;
            List<Thing> billGivers = pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.PotentialBillGiver);
            for (int i = 0; i < billGivers.Count; i++)
            {
                Building_WorkTable table = billGivers[i] as Building_WorkTable;
                if (table == null || table.Destroyed || !table.Spawned) continue;
                Zone_Shop shop = SimShopServiceApi.FindShop(table.Map, table.Position);
                if (shop == null || !RestaurantOrderUtility.CanRestaurantStaffWorkAt(shop)) continue;
                if (!RestaurantCookingUtility.IsWorkCellInsideShop(shop, table)) continue;
                if (DefOfRefs.RSR_WorkGiver_CookRestaurantOrder != null
                    && !SimShopStaffApi.IsOnShiftForWorkGiver(shop, pawn, DefOfRefs.RSR_WorkGiver_CookRestaurantOrder))
                    continue;
                yield return table;
            }
        }

        //判断工作台是否存在当前厨师可制作的订单，并缓存同 tick 的订单与食材结果。
        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            return ResolveOrderCached(pawn, thing, forced, out _, out _);
        }

        //创建烹饪 Job，直接复用同 tick 缓存的食材清单，避免重复执行全图食材搜索。
        public override Job JobOnThing(Pawn pawn, Thing stove, bool forced = false)
        {
            Building_WorkTable table = stove as Building_WorkTable;
            if (!ResolveOrderCached(pawn, table, forced, out RestaurantOrder order, out List<ThingCount> ingredients)
                || order == null || DefOfRefs.RSR_CookRestaurantOrder == null || ingredients.NullOrEmpty())
                return null;
            cachedOrder = null;
            cachedIngredients = null;

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

        //按同 tick 缓存单次灶台订单与食材解析结果，消除 HasJobOnThing 与 JobOnThing 的重复搜索。
        private bool ResolveOrderCached(Pawn pawn, Thing stove, bool forced, out RestaurantOrder order, out List<ThingCount> ingredients)
        {
            int now = Find.TickManager?.TicksGame ?? -1;
            int pawnId = pawn?.thingIDNumber ?? -1;
            int stoveId = stove?.thingIDNumber ?? -1;
            if (cachedTick == now && cachedPawnId == pawnId && cachedStoveId == stoveId && cachedForced == forced)
            {
                order = cachedOrder;
                ingredients = cachedIngredients;
                return order != null && !ingredients.NullOrEmpty();
            }

            cachedTick = now;
            cachedPawnId = pawnId;
            cachedStoveId = stoveId;
            cachedForced = forced;
            cachedOrder = FindOrderFor(pawn, stove, forced, out cachedIngredients);
            order = cachedOrder;
            ingredients = cachedIngredients;
            return order != null && !ingredients.NullOrEmpty();
        }

        //查找工作台对应商店的最早可制作订单及食材列表，手动强制允许忽略排班小时。
        private static RestaurantOrder FindOrderFor(Pawn pawn, Thing stove, bool forced, out List<ThingCount> ingredients)
        {
            ingredients = null;
            if (pawn?.Map == null || !(stove is Building_WorkTable table) || table.Destroyed || !table.Spawned)
                return null;
            Zone_Shop shop = SimShopServiceApi.FindShop(table.Map, table.Position);
            if (shop == null || !RestaurantOrderUtility.CanRestaurantStaffWorkAt(shop)) return null;
            if (!RestaurantCookingUtility.IsWorkCellInsideShop(shop, table)) return null;
            if (DefOfRefs.RSR_WorkGiver_CookRestaurantOrder != null)
            {
                bool allowed = forced
                    ? SimShopStaffApi.IsAssignedToWorkGiver(shop, pawn, DefOfRefs.RSR_WorkGiver_CookRestaurantOrder)
                    : SimShopStaffApi.IsOnShiftForWorkGiver(shop, pawn, DefOfRefs.RSR_WorkGiver_CookRestaurantOrder);
                if (!allowed) return null;
            }
            if (!pawn.CanReserveAndReach(table, PathEndMode.InteractionCell, Danger.Some))
            {
                JobFailReason.Is(SimTranslation.T("RSR.Issue.StoveReservation"));
                return null;
            }
            if (table.def.hasInteractionCell && !pawn.CanReserveSittableOrSpot(table.InteractionCell, table))
            {
                JobFailReason.Is(SimTranslation.T("RSR.Issue.StoveCellReservation"));
                return null;
            }
            RestaurantOrder order = RestaurantOrderUtility.FindCookOrder(pawn, shop, table, out ingredients);
            if (order == null) JobFailReason.Is(SimTranslation.T("RSR.Issue.NoCookOrIngredients"));
            return order;
        }
    }
}
