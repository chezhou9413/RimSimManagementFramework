using System;
using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.Conveyor.Placement;
using RimSimRestaurantExtension.Conveyor.Stocking;
using RimSimRestaurantExtension.Conveyor.Transport;
using RimSimRestaurantExtension.Models;
using RimSimRestaurantExtension.Tool;
using RimWorld;
using SimManagementLib.SimZone;
using Verse;

namespace RimSimRestaurantExtension.Debug
{
    //初始化样板闭环库存，职责是同时准备菜谱制作、后厨现货和可直接取餐的真实餐盘。
    internal static class ConveyorRestaurantStock
    {
        //校验闭环并配置目标，职责是让少量预置餐盘与剩余补餐需求进入正式线路系统。
        internal static bool TryConfigure(Map map, IntVec3 center, Zone_Shop shop,
            List<RestaurantMenuItem> menuItems, IReadOnlyList<IntVec3> kitchenCells,
            out string description, out string reason)
        {
            description = "";
            reason = "";
            var cells = ConveyorRestaurantLayout.Path(center);
            var first = ConveyorLinks.At(map, cells[0]) as Building_SushiConveyor;
            var line = first?.Line;
            line?.RefreshShop();
            if (line == null || line.Shop != shop || !line.SameShop || line.segments.Count != cells.Count
                || line.segments.Any(b => ConveyorLinks.Next(b) == null))
            {
                reason = "样板传送带未形成归属于本店的完整闭环";
                return false;
            }
            var sourceCell = kitchenCells.Where(cell =>
                !cell.GetThingList(map).Any(t => t.def.category == ThingCategory.Item)).DefaultIfEmpty(IntVec3.Invalid).First();
            if (!sourceCell.IsValid)
            {
                reason = "后厨没有空格放置传送带现货";
                return false;
            }

            line.rules = menuItems.Take(2).Select(menu => new ConveyorStockRule
            { menu = menu.Clone(), portions = 1, target = 4, price = menu.unitPrice }).ToList();
            var readyRule = new ConveyorStockRule
            { food = ThingDefOf.MealSurvivalPack, portions = 1, target = 4, price = ThingDefOf.MealSurvivalPack.BaseMarketValue };
            line.rules.Add(readyRule);
            line.paused = false;
            line.notice = "";

            //现货不进入冰箱补货目标，保留在绑定后厨，供厨师走现货搬运分支。
            Thing reserve = ThingMaker.MakeThing(readyRule.Food);
            reserve.stackCount = Math.Min(30, readyRule.Food.stackLimit);
            GenSpawn.Spawn(reserve, sourceCell, map);
            int plateCount = line.rules.Count * 2;
            for (int i = 0; i < plateCount; i++)
                SeedPlate(line.segments[i * line.segments.Count / plateCount], line.rules[i % line.rules.Count]);
            description = $"闭环 {line.segments.Count} 段，预置 {plateCount} 盘，上架目标 {line.rules.Sum(r => r.target)} 盘，";
            return true;
        }

        //创建调试餐盘及食材记录，职责是让顾客偏好、腐坏、成本与取餐都处理真实物品。
        private static void SeedPlate(Building_SushiConveyor belt, ConveyorStockRule rule)
        {
            Thing meal = ThingMaker.MakeThing(rule.Food);
            meal.stackCount = rule.portions;
            float cost = meal.MarketValue * meal.stackCount;
            if (rule.menu != null)
            {
                var needs = RestaurantIngredientUtility.BuildNeeds(rule.menu, rule.portions);
                var ingredients = meal.TryGetComp<CompIngredients>();
                foreach (var need in needs) ingredients?.RegisterIngredient(need.ThingDef);
                cost = RestaurantIngredientUtility.CalculateIngredientCost(needs);
            }
            if (!belt.GetDirectlyHeldThings().TryAdd(meal, false))
                throw new InvalidOperationException("无法将样板餐盘放入传送带：" + belt.Position);
            belt.plate = new ConveyorPlate { ruleId = rule.id, cost = cost };
            belt.progress = 0.5f;
        }
    }
}
