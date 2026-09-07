using RimSimRestaurantExtension.Buildings;
using RimSimRestaurantExtension.Models;
using RimWorld;
using SimManagementLib.Api;
using Verse;

namespace RimSimRestaurantExtension.Tool
{
    //协调服务阶段的原子交接，职责是让 Job 只负责动作且每个业务节点最多提交一次。
    public static class RestaurantOrderCoordinator
    {
        //推进领域阶段，职责是统一时间记录和阶段日志。
        public static void MoveTo(RestaurantOrder order, RestaurantOrderState state)
        {
            if (order == null || order.IsTerminal) return;
            order.state = state;
            order.failReason = "";
            order.blockReason = "";
            order.TouchProgress();
            RestaurantFlowLog.Stage(order, state.ToString());
        }

        //登记顾客入座，职责是启动接待期限且重试不重置计时。
        public static void Seated(RestaurantOrder order)
        {
            if (order.state != RestaurantOrderState.GoingToSeat) return;
            if (order.seatedTick < 0) order.seatedTick = Find.TickManager.TicksGame;
            MoveTo(order, RestaurantOrderState.WaitingOrder);
        }

        //认领接待任务，职责是拒绝重复服务员并要求顾客正在原座等待。
        public static bool ClaimReception(RestaurantOrder order, Pawn waiter)
        {
            Pawn customer = RestaurantOrderUtility.FindCustomer(waiter.Map, order);
            if (order?.state != RestaurantOrderState.WaitingOrder || customer?.Position != order.seatCell
                || customer.CurJobDef != DefOfRefs.RSR_WaitRestaurantOrderAtDiningSpot) return false;
            order.waiterThingId = waiter.thingIDNumber;
            MoveTo(order, RestaurantOrderState.TakingOrder);
            return true;
        }

        //完成交谈并固定菜单快照，职责是关键时点重查预算、配方、库存、路线并只弹出一次图标。
        public static bool ConfirmMenu(RestaurantOrder order, Pawn waiter)
        {
            if (order == null || order.menuConfirmed || order.state != RestaurantOrderState.TakingOrder
                || order.waiterThingId != waiter.thingIDNumber
                || !RestaurantOrderUtility.EnsureOrderStillValid(order, waiter.Map)) return false;
            Pawn customer = RestaurantOrderUtility.FindCustomer(waiter.Map, order);
            var shop = RestaurantOrderUtility.FindShopById(waiter.Map, order.shopZoneId);
            Thing provider = RestaurantOrderUtility.FindProvider(waiter.Map, order);
            RestaurantMenuUtility.ClearSelection(customer, provider, shop);
            var selected = RestaurantMenuUtility.GetOrCreateSelection(customer, provider, shop);
            var settings = RestaurantOrderUtility.Settings.GetOrCreate(order.shopZoneId);
            if (selected?.IsValid != true || !RestaurantOrderCreationUtility.ValidateSelectionAtOrderTime(customer, shop, settings, selected))
            {
                RestaurantOrderUtility.FailOrder(order, "交谈后没有符合预算、饮食与库存条件的菜品");
                return false;
            }
            order.stockProduct = selected.menuItem.IsStockProduct;
            order.sourceCabinet = selected.menuItem.sourceCabinet;
            order.mode = selected.menuItem.deliveryMode;
            order.mealDef = selected.menuItem.MealDef;
            order.mealCount = selected.count;
            if (customer.carryTracker.MaxStackSpaceEver(order.mealDef) < order.mealCount)
            {
                RestaurantOrderUtility.FailOrder(order, "点菜份数超过顾客携带能力");
                return false;
            }
            order.ingredients = RestaurantIngredientUtility.BuildNeeds(selected.menuItem, selected.count);
            order.recipe = order.stockProduct ? null : RestaurantCookingUtility.GetRecipeForOrder(order);
            string issue = RestaurantBusinessAvailability.CheckOrder(shop, order);
            if (!issue.NullOrEmpty())
            {
                RestaurantOrderUtility.FailOrder(order, issue);
                return false;
            }
            order.menuItemId = selected.menuItem.id;
            order.menuItemLabel = selected.menuItem.DisplayLabel;
            order.price = selected.totalPrice;
            order.unitPrice = selected.totalPrice / selected.count;
            order.ingredientCost = 0f;
            order.preferenceSummary = selected.preference?.BuildSummary() ?? "未启用个体偏好";
            order.preferenceScore = selected.preferenceScore;
            order.selectionReason = selected.selectionReason;
            if (!Inventory.RestaurantOrderStock.Reserve(order, shop))
            {
                RestaurantOrderUtility.FailOrder(order, "确认时真实库存已不足或被其他订单预留");
                return false;
            }
            order.menuConfirmed = true;
            order.orderedTick = Find.TickManager.TicksGame;
            order.waiterThingId = -1;
            MoveTo(order, order.stockProduct ? RestaurantOrderState.ReadyToDeliver : RestaurantOrderState.WaitingCook);
            var session = RestaurantOrderUtility.OrderManager.SessionFor(order);
            session.rounds++;
            session.lastOrderTick = order.orderedTick;
            if (session.rounds >= settings.maxOrderRounds) session.stopOrdering = true;
            if (!order.dishBubbleShown)
            {
                order.dishBubbleShown = true;
                SimShopUiApi.ShowCustomerThingBubble(customer, order.mealDef);
            }
            RestaurantMenuUtility.ClearSelection(customer, provider, shop);
            return true;
        }

        //认领厨房任务，职责是防止同一订单重复投料。
        public static bool ClaimCooking(RestaurantOrder order, Pawn cook, Thing stove)
        {
            if (order?.state != RestaurantOrderState.WaitingCook || order.mealProduced || order.stockProduct
                || Find.TickManager.TicksGame < order.nextCookingAttemptTick) return false;
            order.cookThingId = cook.thingIDNumber;
            order.stoveThingId = stove.thingIDNumber;
            order.cookingStartedTick = Find.TickManager.TicksGame;
            MoveTo(order, RestaurantOrderState.Cooking);
            return true;
        }

        //登记实际产出，职责是使中断恢复从运输阶段继续且不再投料。
        public static void Produced(RestaurantOrder order, Thing meal)
        {
            order.meal = meal;
            order.goods.Add(meal);
            Inventory.RestaurantOrderStock.Release(order);
            order.mealThingId = meal.thingIDNumber;
            order.mealProduced = true;
            order.cookedTick = Find.TickManager.TicksGame;
            MoveTo(order, RestaurantOrderState.ChefBringingToPass);
            RestaurantMealTransferUtility.Protect(order);
        }

        //将厨师携带成品交入出餐台，职责是核实设施和整单数量。
        public static bool StoreAtPass(RestaurantOrder order, Pawn cook)
        {
            var pass = RestaurantOrderUtility.FindProvider(cook.Map, order) as Building_RestaurantPass;
            if (order?.state != RestaurantOrderState.ChefBringingToPass || order.cookThingId != cook.thingIDNumber
                || pass == null || cook.carryTracker.CarriedThing != order.meal
                || order.meal.stackCount != order.mealCount
                || !cook.CanReachImmediate(pass, Verse.AI.PathEndMode.Touch)
                || !RestaurantOrderUtility.EnsureOrderStillValid(order, cook.Map)) return false;
            if (!RestaurantMealTransferUtility.Transfer(order, pass.GetDirectlyHeldThings())) return false;
            order.cookThingId = -1;
            MoveTo(order, RestaurantOrderState.ReadyToDeliver);
            return true;
        }

        //完成服务员交付，职责是把整单实物直接交给原座顾客。
        public static bool Deliver(RestaurantOrder order, Pawn waiter)
        {
            return Dining.RestaurantTableDelivery.Deliver(order, waiter);
        }

        //退回中断认领，职责是保留已产出实物且不重置接待和上菜期限。
        public static void ReleaseClaim(RestaurantOrder order, Pawn employee)
        {
            if (order == null || order.IsTerminal) return;
            if (order.state == RestaurantOrderState.TakingOrder && order.waiterThingId == employee.thingIDNumber)
            {
                order.waiterThingId = -1;
                MoveTo(order, RestaurantOrderState.WaitingOrder);
            }
            else if (order.state == RestaurantOrderState.Delivering && order.waiterThingId == employee.thingIDNumber)
            {
                RestaurantMealTransferUtility.DropCarried(employee, order);
                order.waiterThingId = -1;
                MoveTo(order, RestaurantOrderState.ReadyToDeliver);
            }
            else if (order.cookThingId == employee.thingIDNumber)
            {
                RestaurantMealTransferUtility.DropCarried(employee, order);
                order.cookThingId = -1;
                if (order.state == RestaurantOrderState.Cooking)
                {
                    Inventory.RestaurantOrderStock.Release(order);
                    MoveTo(order, RestaurantOrderState.WaitingCook);
                }
            }
        }
    }
}
