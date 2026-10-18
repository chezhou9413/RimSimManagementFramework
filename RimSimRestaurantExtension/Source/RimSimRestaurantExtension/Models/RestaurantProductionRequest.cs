using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RimSimRestaurantExtension.Models
{
    //描述厨房实际制作需求，职责是让顾客订单与备餐任务共用配方和食材计算。
    public class RestaurantProductionRequest
    {
        public ThingDef mealDef;
        public int mealCount = 1;
        public RecipeDef recipe;
        public List<RestaurantIngredientRequirement> ingredients = new List<RestaurantIngredientRequirement>();

        //合并同类食材需求，返回独立清单供配方核验与实物预留使用。
        public List<RestaurantIngredientRequirement> GetTotalIngredientNeeds() => ingredients
            .GroupBy(item => item.thingDefName)
            .Select(group => new RestaurantIngredientRequirement
            { thingDefName = group.Key, countPerMeal = group.Sum(item => item.countPerMeal) }).ToList();
    }
}

