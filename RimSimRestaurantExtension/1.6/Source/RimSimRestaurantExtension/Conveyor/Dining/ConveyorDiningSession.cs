using System.Linq;
using RimSimRestaurantExtension.Conveyor.Transport;
using RimSimRestaurantExtension.Dining;
using RimSimRestaurantExtension.Inventory;
using RimSimRestaurantExtension.Models;
using RimSimRestaurantExtension.Tool;
using RimWorld;
using Verse;

namespace RimSimRestaurantExtension.Conveyor.Dining
{
    //推进传送带自助会话，职责是选择经过面前的食品并复用普通餐厅账单。
    public static class ConveyorDiningSession
    {
        //初始化自助锚点，职责是保留框架动作标识但不创建服务员待接单任务。
        public static void Initialize(RestaurantDiningSession session, Building_SushiConveyor belt)
        {
            session.selfService = true;
            session.conveyorLineId = belt.Line.id;
            session.Anchor.mealConsumed = true;
            session.Anchor.mealDelivered = true;
        }

        //推进需求和等待上限，职责是只有本人实际取餐才能重置等待时钟。
        public static void Tick(RestaurantDiningSession session)
        {
            if (session.IsTerminal || session.state == RestaurantSessionState.AwaitingCheckout) return;
            var map = Find.Maps.FirstOrDefault(m => m.uniqueID == session.mapId);
            var customer = RestaurantOrderUtility.FindThingById(map, session.customerId) as Pawn;
            if (customer?.Spawned != true || customer.Dead || map == null)
            { RestaurantSessionUtility.Abort(session, "自助用餐顾客已不可用"); return; }
            if (RestaurantOrderUtility.FindShopById(map, session.shopId) == null)
            { RestaurantSessionUtility.Abort(session, "自助用餐所属商店已不存在"); return; }
            if (!RestaurantDiningSpotUtility.IsDiningSpotValid(customer, session.Anchor))
            { EndAtLostSeat(customer, session); return; }
            if (session.state == RestaurantSessionState.Serving && session.tray?.Spawned != true)
            { EndAtLostSeat(customer, session); return; }
            var belt = RestaurantDiningSpotUtility.FindTableById(map, session.tableId) as Building_SushiConveyor;
            if (belt?.Line == null || !belt.Line.SameShop || belt.Line.Shop?.ID != session.shopId)
            { EndAtLostSeat(customer, session); return; }
            session.conveyorLineId = belt.Line.id;
            if (session.state != RestaurantSessionState.Serving || session.stopOrdering) return;
            var settings = RestaurantOrderUtility.Settings.GetOrCreate(session.shopId);
            bool eating = session.Orders.Any(o => !o.IsTerminal && o.mealDelivered && !o.mealConsumed);
            if (!eating && (session.rounds >= settings.maxOrderRounds || (customer.needs?.food?.NutritionWanted ?? 0f) <= 0.05f))
            { session.stopOrdering = true; return; }
            float budget = RestaurantProductMenuUtility.RemainingBudget(customer, belt.Line.Shop);
            if (!eating && !belt.Line.rules.Any(r => r.enabled && r.price * r.portions <= budget))
            { session.stopOrdering = true; session.reason = "没有剩余预算内的上架食品"; return; }
            if (!eating && Find.TickManager.TicksGame - session.lastOrderTick >= settings.maxWaitTicks)
            { session.stopOrdering = true; session.reason = "等待传送带餐品超时"; return; }
            if (eating || session.rounds > 0 && Find.TickManager.TicksGame - session.lastOrderTick < settings.reorderIntervalTicks) return;
            if (!session.wantedConveyorRule.NullOrEmpty()
                && belt.Line.rules.Any(r => r.id == session.wantedConveyorRule && r.enabled)) return;
            var offers = ConveyorDiningAvailability.ReachablePlates(belt).Where(p => ConveyorDiningAvailability.Accepts(customer, p)).ToList();
            if (offers.Count == 0) return;
            var preference = settings.useCustomerPreferences ? RestaurantOrderUtility.Preferences.GetOrCreate(customer) : null;
            var selected = offers.RandomElementByWeight(source =>
            {
                var rule = ConveyorDiningAvailability.Rule(source);
                var menu = new RestaurantMenuItem { id = rule.id, label = rule.Label, mealDefName = source.Food.def.defName,
                    ingredients = source.Food.TryGetComp<CompIngredients>()?.ingredients.Select(d =>
                        new RestaurantIngredientRequirement { thingDefName = d.defName, countPerMeal = 1 }).ToList()
                        ?? new System.Collections.Generic.List<RestaurantIngredientRequirement>() };
                return RestaurantMenuUtility.ScoreMenuItem(menu, source.Food.stackCount, rule.price,
                    RestaurantProductMenuUtility.RemainingBudget(customer, belt.Line.Shop), preference, settings.preferenceStrength);
            });
            session.wantedConveyorRule = selected.plate.ruleId;
        }

        //在本人面前取走一盘，职责是把物权转移和消费明细登记作为同一主线程提交。
        public static void TryTake(Pawn customer, RestaurantDiningSession session)
        {
            if (session.stopOrdering || session.state != RestaurantSessionState.Serving || session.tray?.Spawned != true
                || customer.Position != session.seat || session.wantedConveyorRule.NullOrEmpty()) return;
            var belt = RestaurantDiningSpotUtility.FindTableById(customer.Map, session.tableId) as Building_SushiConveyor;
            if (belt?.Food == null || Stocking.ConveyorStockPlanner.HoldsPlate(belt) || belt.plate?.ruleId != session.wantedConveyorRule
                || !ConveyorDiningAvailability.Accepts(customer, belt)) return;
            if (session.Orders.Any(o => !o.IsTerminal && !o.mealConsumed)) return;
            var food = belt.Food;
            var plate = belt.plate;
            var rule = ConveyorDiningAvailability.Rule(belt);
            if (!belt.GetDirectlyHeldThings().TryTransferToContainer(food, session.tray.GetDirectlyHeldThings(), false))
                throw new System.InvalidOperationException("自助餐盘无法转入顾客托盘");
            belt.plate = null;
            int now = Find.TickManager.TicksGame;
            var order = RestaurantOrderUtility.OrderManager.AddOrder(new RestaurantOrder
            {
                sessionId = session.sessionId, actionOrderId = session.actionOrderId, mapId = session.mapId,
                customerThingId = session.customerId, shopZoneId = session.shopId, providerThingId = session.Anchor.providerThingId,
                tableThingId = session.tableId, seatCell = session.seat, state = RestaurantOrderState.Dining,
                stockProduct = true, mode = RestaurantDeliveryMode.Consume, menuConfirmed = true, mealProduced = true, mealDelivered = true,
                meal = food, mealThingId = food.thingIDNumber, mealDef = food.def, mealCount = food.stackCount,
                menuItemId = rule.id, menuItemLabel = rule.Label, unitPrice = rule.price, price = rule.price * food.stackCount,
                ingredientCost = plate.cost, createdTick = now, orderedTick = now, deliveredTick = now, lastProgressTick = now,
                round = session.rounds + 1, selectionReason = "从面前传送带自行取餐"
            });
            order.goods.Add(food);
            session.orderIds.Add(order.orderId);
            session.rounds++;
            session.lastOrderTick = now;
            session.wantedConveyorRule = "";
        }

        //在餐位失效时保留已消费账单，职责是不让拆除桌椅取消已经吃掉的餐品费用。
        private static void EndAtLostSeat(Pawn customer, RestaurantDiningSession session)
        {
            session.stopOrdering = true;
            session.reason = "传送带餐位已失效";
            foreach (var order in session.Orders.Where(o => !o.IsTerminal && !o.mealConsumed).ToList())
            {
                if (order.meal != null && customer.carryTracker.CarriedThing == order.meal && session.tray?.Spawned == true)
                    RestaurantMealTransferUtility.Transfer(order, session.tray.GetDirectlyHeldThings());
                RestaurantOrderUtility.FailOrder(order, session.reason);
            }
            RestaurantSessionSettlement.Finish(customer, session);
        }
    }
}
