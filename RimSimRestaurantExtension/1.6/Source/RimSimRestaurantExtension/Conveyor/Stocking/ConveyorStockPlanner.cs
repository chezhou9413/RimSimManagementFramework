using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.Conveyor.Transport;
using RimSimRestaurantExtension.Inventory;
using RimSimRestaurantExtension.Models;
using RimSimRestaurantExtension.Tool;
using RimWorld;
using SimManagementLib.Api;
using SimManagementLib.SimMapComp;
using SimManagementLib.SimZone;
using Verse;
using Verse.AI;

namespace RimSimRestaurantExtension.Conveyor.Stocking
{
    //查找厨师空闲补餐任务，职责是统一订单优先级、库存来源和线路容量约束。
    public static class ConveyorStockPlanner
    {
        //统计已被厨师领取的盘数，职责是避免多个厨师重复补同一目标。
        public static int Pending(ConveyorLine line, string rule) =>
            line.segments.Count(b => b.reservedBy != null && b.reservedRuleId == rule);

        //识别清理中的实物占位，职责是仅固定待清理餐盘而不让补餐容量预约挡住运输。
        public static bool HoldsPlate(Building_SushiConveyor belt) =>
            belt.Food != null && belt.reservedBy?.jobs?.curDriver is JobDriver_StockSushiConveyor driver
                && driver.task?.cleaning == true && driver.task.destination == belt && driver.task.cleaningFood == belt.Food;

        //统计整条线路的在途补餐，职责是让流动空位仍只被一个任务计入容量。
        public static int PendingTotal(ConveyorLine line) =>
            line.segments.Count(b => b.reservedBy != null && !b.reservedRuleId.NullOrEmpty());

        //判断厨师是否可以领取低优先级工作，职责是避让同店顾客厨房订单。
        public static bool Idle(Pawn pawn, ConveyorLine line)
        {
            var shop = line?.Shop;
            return shop != null && line.SameShop && shop.IsOpenNow()
                && RestaurantOrderUtility.Settings.GetOrCreate(shop.ID).enabled
                && !RestaurantOrderUtility.HasBlockingPriorityJobOrNeed(pawn)
                && SimShopStaffApi.IsAssignedToWorkGiver(shop, pawn, DefOfRefs.RSR_WorkGiver_CookRestaurantOrder)
                && !RestaurantOrderUtility.OrderManager.GetActiveOrders(shop.ID).Any(o =>
                    o.state == RestaurantOrderState.WaitingOrder || o.state == RestaurantOrderState.TakingOrder
                    || o.state == RestaurantOrderState.WaitingCook || o.state == RestaurantOrderState.Cooking
                    || o.state == RestaurantOrderState.ChefBringingToPass);
        }

        //判断餐盘是否需要清理，职责是拒售腐坏食品和已经下架的旧餐盘。
        public static bool NeedsCleaning(Building_SushiConveyor belt) => belt.Food != null
            && (!belt.Food.IngestibleNow || belt.Food.TryGetComp<CompRottable>()?.Stage > RotStage.Fresh
                || belt.Line?.rules.Any(r => r.id == belt.plate?.ruleId && r.enabled) != true);

        //构造无副作用的单盘任务预览，职责是在预约前验证食材、技能和可达性。
        public static ConveyorRestockTask Find(Pawn pawn, Building_SushiConveyor belt, out List<ThingCount> stock)
        {
            stock = new List<ThingCount>();
            var line = belt.Line;
            if (pawn.carryTracker.CarriedThing != null || belt.reservedBy != null || !Idle(pawn, line)
                || !pawn.CanReserveAndReach(belt, PathEndMode.Touch, Danger.Some))
                return null;
            if (NeedsCleaning(belt))
            {
                if (belt.Food.stackCount > pawn.carryTracker.MaxStackSpaceEver(belt.Food.def)) return null;
                return new ConveyorRestockTask { cleaning = true, destination = belt, shopId = line.Shop.ID,
                    cleaningFood = belt.Food, mealDef = belt.Food.def, mealCount = belt.Food.stackCount, cost = belt.plate?.cost ?? 0f };
            }
            if (!line.CanStock || belt.Food != null || pawn.carryTracker.CarriedThing != null
                || line.Occupied + PendingTotal(line) >= line.segments.Count) return null;
            foreach (var rule in line.rules.Where(r => r.enabled && r.Food != null
                && line.Count(r.id) + Pending(line, r.id) < r.target))
            {
                if (rule.portions > pawn.carryTracker.MaxStackSpaceEver(rule.Food)) continue;
                var task = new ConveyorRestockTask { destination = belt, ruleId = rule.id, shopId = line.Shop.ID,
                    mealDef = rule.Food, mealCount = rule.portions };
                if (rule.menu == null)
                {
                    stock = SelectStock(line.Shop, rule, pawn);
                    if (stock.Sum(t => t.Count) == rule.portions) return task;
                }
                else
                {
                    task.ingredients = RestaurantIngredientUtility.BuildNeeds(rule.menu, rule.portions);
                    foreach (var stove in RestaurantCookingUtility.FindUsableStoves(line.Shop, task))
                    {
                        if (!RestaurantCookingUtility.CanPawnCookOrderAt(pawn, stove, task)
                            || !pawn.CanReserveAndReach(stove, PathEndMode.InteractionCell, Danger.Some)) continue;
                        if (!RestaurantStockUtility.Select(line.Shop, task, pawn, out stock)) continue;
                        task.stove = stove;
                        task.recipe = RestaurantCookingUtility.GetRecipeForOrder(task, stove);
                        return task;
                    }
                }
            }
            return null;
        }

        //重新核对取料来源，职责是感知取料途中发生的商店和后厨区域调整。
        public static bool IsSourceAllowed(Pawn pawn, ConveyorRestockTask task, Thing source)
        {
            var shop = RestaurantOrderUtility.FindShopById(pawn.Map, task.shopId);
            if (shop == null || source == null || task.destination?.Line?.Shop != shop) return false;
            if (task.stove != null)
            {
                if (source.ParentHolder is Buildings.Building_RestaurantStorage fridge)
                    return fridge.Spawned && fridge.IsRefrigerator && fridge.Shop == shop && fridge.AllowsInventoryItem(source.def);
                return RestaurantKitchenStorage.Contains(shop, source);
            }
            var rule = task.destination.Line.rules.FirstOrDefault(r => r.id == task.ruleId && r.enabled);
            if (rule == null) return false;
            return rule.cabinet != null
                ? rule.cabinet.Shop == shop && source.ParentHolder == rule.cabinet && rule.cabinet.AllowsInventoryItem(source.def)
                : RestaurantKitchenStorage.Contains(shop, source);
        }

        //筛选食品柜或店内储存架现货，职责是排除其他预留及传送带回流。
        private static List<ThingCount> SelectStock(Zone_Shop shop, ConveyorStockRule rule, Pawn pawn)
        {
            var candidates = rule.cabinet != null
                ? RestaurantStockUtility.Cabinets(shop).Where(c => c == rule.cabinet && !c.IsRefrigerator)
                    .SelectMany(c => c.GetDirectlyHeldThings().Cast<Thing>())
                : RestaurantKitchenStorage.Items(shop);
            var ledger = shop.Map.GetComponent<MapComponent_InventoryReservations>();
            var result = new List<ThingCount>();
            int left = rule.portions;
            foreach (var thing in candidates.Where(t => t.def == rule.Food && t.IngestibleNow && RestaurantStockUtility.Reachable(pawn, t))
                .OrderBy(t => t.PositionHeld.DistanceToSquared(pawn.Position)))
            {
                int take = left;
                if (ledger.Available(thing) < take) continue;
                result.Add(new ThingCount(thing, take)); left -= take;
                if (left == 0) break;
            }
            return result;
        }
    }
}
