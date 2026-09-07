using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.GameComp;
using RimSimRestaurantExtension.Models;
using RimWorld;
using SimManagementLib.Api;
using SimManagementLib.SimZone;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RimSimRestaurantExtension.Tool
{
    //提供餐厅订单的公共查询和状态推进，职责是让动作 Worker、岗位与 JobDriver 使用同一套业务约束。
    public static class RestaurantOrderUtility
    {
        public static GameComponent_RestaurantOrderManager OrderManager => Current.Game?.GetComponent<GameComponent_RestaurantOrderManager>();
        public static GameComponent_RestaurantSettings Settings => Current.Game?.GetComponent<GameComponent_RestaurantSettings>();
        public static GameComponent_RestaurantPreferences Preferences => Current.Game?.GetComponent<GameComponent_RestaurantPreferences>();

        //按订单查找地图上的顾客，职责是从持久化编号恢复运行对象。
        public static Pawn FindCustomer(Map map, RestaurantOrder order)
        {
            return order == null ? null : FindThingById(map, order.customerThingId) as Pawn;
        }

        //按订单查找接待与出餐台，职责是确认餐厅动作仍属于可用商店设施。
        public static Thing FindProvider(Map map, RestaurantOrder order)
        {
            return order == null ? null : FindThingById(map, order.providerThingId);
        }

        //按编号查找地图 Thing，职责是给跨存档订单恢复灶台、餐品和员工引用。
        public static Thing FindThingById(Map map, int thingId)
        {
            return RestaurantThingQuery.Find(map, thingId);
        }

        //按订单查找餐品，职责是让配送和进食阶段始终操作订单自己的成品。
        public static Thing FindMeal(Map map, RestaurantOrder order)
        {
            return order?.meal == null || order.meal.Destroyed ? null : order.meal;
        }

        //按编号查找商店区域，职责是避免扩展直接依赖框架访问对象的内部字段。
        public static Zone_Shop FindShopById(Map map, int shopZoneId)
        {
            if (map?.zoneManager?.AllZones == null || shopZoneId < 0) return null;
            return map.zoneManager.AllZones.OfType<Zone_Shop>().FirstOrDefault(shop => shop.ID == shopZoneId);
        }

        //按框架动作订单查找餐厅订单，职责是让顾客 Job 在读档后恢复领域状态。
        public static RestaurantOrder FindByActionOrderId(int actionOrderId)
        {
            return OrderManager?.Orders?.FirstOrDefault(order => order != null && order.actionOrderId == actionOrderId);
        }

        //标记订单失败，职责是只允许活跃订单进入失败终态并释放餐品锁定。
        public static void FailOrder(RestaurantOrder order, string reason)
        {
            if (order == null || order.IsTerminal) return;
            RestaurantFlowLog.Failure(order, "订单失败", reason ?? "未提供原因");
            order.state = RestaurantOrderState.Failed;
            order.failReason = reason ?? "";
            order.completedTick = Find.TickManager?.TicksGame ?? 0;
            order.TouchProgress();
            ReleaseMealLock(order);
        }

        //标记订单取消，职责是处理动作未开始、顾客主动中断或商店失效的无收费收尾。
        public static void CancelOrder(RestaurantOrder order, string reason)
        {
            if (order == null || order.IsTerminal) return;
            RestaurantFlowLog.Failure(order, "订单取消", reason ?? "未提供原因");
            order.state = RestaurantOrderState.Canceled;
            order.failReason = reason ?? "";
            order.completedTick = Find.TickManager?.TicksGame ?? 0;
            order.TouchProgress();
            ReleaseMealLock(order);
        }

        //标记订单完成，职责是固定成功时间并释放餐品锁定。
        public static void CompleteOrder(RestaurantOrder order)
        {
            if (order == null || order.IsTerminal) return;
            order.state = RestaurantOrderState.Completed;
            order.completedTick = Find.TickManager?.TicksGame ?? 0;
            order.TouchProgress();
            ReleaseMealLock(order);
            RestaurantFlowLog.Stage(order, "订单完成");
        }

        //判断订单是否超过上菜等待上限，职责是只限制员工尚未交付的阶段而不打断顾客进食。
        public static bool IsOrderTimedOut(RestaurantOrder order)
        {
            if (order == null || order.mealDelivered || order.IsTerminal
                || order.state == RestaurantOrderState.AwaitingCheckout) return false;
            int started = order.menuConfirmed ? order.orderedTick : (order.seatedTick >= 0 ? order.seatedTick : order.createdTick);
            return (Find.TickManager?.TicksGame ?? 0) - started > GetMaxWaitTicks(order);
        }

        //返回订单等待上限，职责是统一顾客进度和管理器超时判定。
        public static int GetMaxWaitTicks(RestaurantOrder order)
        {
            if (order == null || order.shopZoneId < 0) return 12000;
            var settings = Settings?.GetOrCreate(order.shopZoneId);
            return System.Math.Max(6000, order.menuConfirmed
                ? settings?.maxWaitTicks ?? 12000 : settings?.maxServiceWaitTicks ?? 12000);
        }

        //校验订单继续运行所需对象，职责是及时终止离图、设施丢失或超时订单。
        public static bool EnsureOrderStillValid(RestaurantOrder order, Map map)
        {
            if (order == null || order.IsTerminal) return false;
            Pawn customer = FindCustomer(map, order);
            if (customer == null || customer.Destroyed || customer.Dead || !customer.Spawned)
            {
                FailOrder(order, "顾客已离开地图");
                return false;
            }
            Zone_Shop shop = FindShopById(map, order.shopZoneId);
            if (shop == null)
            {
                FailOrder(order, "餐厅商店区域已不存在");
                return false;
            }
            if (order.state == RestaurantOrderState.AwaitingCheckout) return true;
            Thing provider = FindProvider(map, order);
            if (provider == null || !shop.Cells.Contains(provider.Position))
            {
                FailOrder(order, "点餐台已不可用或被移出餐厅区域");
                return false;
            }
            if (order.menuConfirmed && !order.stockProduct && !order.mealProduced && RestaurantCookingUtility.FindUsableStoves(shop, order).Count == 0)
            {
                FailOrder(order, "本单需要的兼容灶台已不可用");
                return false;
            }
            if (!RestaurantDiningSpotUtility.IsDiningSpotValid(customer, order))
            {
                FailOrder(order, "顾客餐位或餐桌已不可用");
                return false;
            }
            if (IsOrderTimedOut(order))
            {
                Dining.RestaurantSessionUtility.StopUndelivered(OrderManager.SessionFor(order), order.menuConfirmed ? "等待上菜超时" : "等待服务员接单超时");
                return false;
            }
            return true;
        }

        //判断顾客在指定商店是否已有活跃订单，职责是阻止重复动作订单占用同一顾客和餐位。
        public static bool HasActiveCustomerOrder(Pawn customer, Zone_Shop shop = null)
        {
            if (customer == null) return false;
            int shopId = shop?.ID ?? -1;
            return OrderManager?.Sessions.Any(session => !session.IsTerminal && session.customerId == customer.thingIDNumber
                && (shopId < 0 || session.shopId == shopId)) == true;
        }

        //创建顾客等待用餐 Job，职责是把框架动作订单编号写入可持久化 Job 字段。
        public static Job MakeCustomerDiningJob(Pawn customer, RestaurantOrder order)
        {
            if (customer?.Map == null || order == null || order.actionOrderId <= 0 || DefOfRefs.RSR_WaitRestaurantOrderAtDiningSpot == null)
                return null;
            Job job = JobMaker.MakeJob(DefOfRefs.RSR_WaitRestaurantOrderAtDiningSpot);
            job.SetTarget(TargetIndex.B, order.seatCell);
            Thing table = RestaurantDiningSpotUtility.FindTableById(customer.Map, order.tableThingId);
            if (table != null)
                job.SetTarget(TargetIndex.C, table);
            job.count = order.actionOrderId;
            RestaurantJobUtility.SetOrderId(job, order.actionOrderId);
            return job;
        }

        //查找厨师可认领的最早订单，职责是过滤状态、设施、食材和商店边界。
        public static RestaurantOrder FindCookOrder(Pawn cook, Zone_Shop shop, Thing stove)
        {
            if (cook?.Map == null || shop == null || stove == null || OrderManager?.CanDispatchCooking(cook) != true) return null;
            return OrderManager?.GetActiveOrders(shop.ID)
                .Where(order => order.state == RestaurantOrderState.WaitingCook && Find.TickManager.TicksGame >= order.nextCookingAttemptTick)
                .Where(order => EnsureOrderStillValid(order, cook.Map))
                .Where(order => !order.mealProduced && RestaurantCookingUtility.CanPawnCookOrderAt(cook, stove, order))
                .Where(order => cook.carryTracker.MaxStackSpaceEver(order.mealDef) >= order.mealCount)
                .Where(order => FindProvider(cook.Map, order) is Thing pass && cook.CanReach(pass, PathEndMode.Touch, Danger.Some))
                .Where(order => RestaurantIngredientUtility.TryFindIngredientThingCounts(cook, shop, order, out _, out _))
                .OrderBy(order => order.createdTick)
                .FirstOrDefault();
        }

        //释放餐品的临时禁用标记，职责是保证失败、中断和成功路径都不会遗留永久禁止物品。
        public static void ReleaseMealLock(RestaurantOrder order)
        {
            RestaurantMealTransferUtility.Release(order);
            Inventory.RestaurantOrderStock.Release(order);
        }

        //判断关店后是否仍允许餐厅员工收尾，职责是避免活跃订单因营业时间结束永久卡住。
        public static bool CanRestaurantStaffWorkAt(Zone_Shop shop)
        {
            return shop == null || shop.IsOpenNow() || OrderManager?.GetActiveOrders(shop.ID).Any(order => order.NeedsStaffWork) == true;
        }

        //返回员工可以值班的餐厅，职责是按新版公开岗位 API 过滤店铺分配。
        public static List<Zone_Shop> GetRestaurantShopsForPawn(Pawn pawn, WorkGiverDef workGiverDef)
        {
            if (pawn?.Map?.zoneManager?.AllZones == null) return new List<Zone_Shop>();
            return pawn.Map.zoneManager.AllZones
                .OfType<Zone_Shop>()
                .Where(shop => shop.IsOpenNow() || OrderManager?.GetActiveOrders(shop.ID).Any(order => !order.IsTerminal) == true)
                .Where(shop => workGiverDef == null || SimShopStaffApi.IsAssignedToWorkGiver(shop, pawn, workGiverDef))
                .ToList();
        }

        //判断员工当前是否有不可打断状态，职责是让低优先级值班工作避让玩家命令和基础生存需求。
        public static bool HasBlockingPriorityJobOrNeed(Pawn pawn)
        {
            if (pawn == null || pawn.Drafted || pawn.Downed || pawn.InMentalState) return true;
            if (pawn.CurJob?.playerForced == true) return true;
            if (pawn.needs?.food?.CurCategory >= HungerCategory.Hungry) return true;
            return pawn.needs?.rest != null && pawn.needs.rest.CurLevelPercentage < 0.18f;
        }
    }
}
