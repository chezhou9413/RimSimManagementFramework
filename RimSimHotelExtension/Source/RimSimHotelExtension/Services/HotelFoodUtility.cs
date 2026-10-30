using System.Linq;
using RimWorld;
using RimSimHotelExtension.Settings;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimSimHotelExtension.Services
{
    //按真实食量预留餐费，并优先吃掉已经购买的随身食品。
    public static class HotelFoodUtility
    {
        //以普通餐厅两餐七十白银为基准，按种族与代谢的真实食量缩放。
        public static int DailyFoodReserve(Pawn pawn)
        {
            if (pawn.needs?.food == null) return 0;
            float nutrition = pawn.needs.food.FoodFallPerTickAssumingCategory(HungerCategory.Fed) * 60000f;
            float cost = HotelTuningDef.Current.foodBudgetPerDay * Mathf.Max(0, nutrition / 1.6f);
            return Mathf.Max(0, Mathf.CeilToInt(cost));
        }

        //仅食用自己的库存，不从殖民者仓库无偿取餐。
        public static Job MakeInventoryMealJob(Pawn pawn)
        {
            Thing food = pawn.inventory?.innerContainer.FirstOrDefault(t => CanEat(pawn, t));
            if (food == null) return null;
            Job job = JobMaker.MakeJob(JobDefOf.Ingest, food);
            job.count = FoodUtility.WillIngestStackCountOf(pawn, food.def, FoodUtility.GetNutrition(pawn, food, food.def));
            return job;
        }

        //排除腐坏、非食物及不适合该种族的库存。
        private static bool CanEat(Pawn pawn, Thing food)
        {
            return food != null && !food.Destroyed && food.IngestibleNow && food.def.IsNutritionGivingIngestible
                && !food.def.IsDrug && pawn.RaceProps.CanEverEat(food);
        }
    }
}
