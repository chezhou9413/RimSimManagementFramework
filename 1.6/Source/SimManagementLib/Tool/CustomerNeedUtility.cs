using RimWorld;
using Verse;

namespace SimManagementLib.Tool
{
    //类职责：管理顾客访问期间的基础需求状态，避免需求系统打断商店行为。
    public static class CustomerNeedUtility
    {
        private const float BalancedFoodLevelPercent = 0.65f;

        //函数职责：把顾客饱食度维持在稳定平衡值，并清理饥饿导致的营养不良状态。
        public static void StabilizeCustomerNeeds(Pawn customer)
        {
            if (customer == null || customer.Destroyed || customer.Dead)
                return;

            StabilizeFood(customer);
            RemoveStarvationHediff(customer);
        }

        //函数职责：完整消耗一份已购买的食物，并把顾客饱食度维持在平衡值。
        public static bool ConsumePurchasedFood(Pawn customer, Thing food)
        {
            if (customer == null || food == null || food.Destroyed || food.stackCount <= 0)
                return false;

            if (food.stackCount > 1)
                food.stackCount--;
            else
                food.Destroy(DestroyMode.Vanish);

            StabilizeCustomerNeeds(customer);
            return true;
        }

        //函数职责：设置顾客食物需求到中等水平，避免过饿倒地或过饱影响堂食 Job。
        private static void StabilizeFood(Pawn customer)
        {
            Need_Food food = customer.needs?.food;
            if (food == null) return;

            float target = food.MaxLevel * BalancedFoodLevelPercent;
            food.CurLevel = target;
        }

        //函数职责：移除顾客因饥饿产生的营养不良 Hediff。
        private static void RemoveStarvationHediff(Pawn customer)
        {
            if (customer.health?.hediffSet == null) return;
            Hediff starvation = customer.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.Malnutrition);
            if (starvation != null)
                customer.health.RemoveHediff(starvation);
        }
    }
}
