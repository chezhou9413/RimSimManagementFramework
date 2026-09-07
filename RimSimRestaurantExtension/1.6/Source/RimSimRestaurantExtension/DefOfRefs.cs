using RimWorld;
using Verse;
using Verse.AI;

namespace RimSimRestaurantExtension
{
    //缓存餐厅扩展常用 Def 引用，负责让代码安全读取 XML 中声明的 Job 和建筑。
    [DefOf]
    public static class DefOfRefs
    {
        public static ThingDef RSR_RestaurantOrderCounter;
        public static JobDef RSR_TakeRestaurantOrder;
        public static JobDef RSR_BringRestaurantMealToPass;
        public static WorkGiverDef RSR_WorkGiver_TakeRestaurantOrder;
        public static WorkGiverDef RSR_WorkGiver_BringRestaurantMealToPass;
        public static JobDef RSR_CookRestaurantOrder;
        public static JobDef RSR_DeliverRestaurantOrder;
        public static JobDef RSR_WaitRestaurantOrderAtDiningSpot;
        public static JobDef RSR_RestaurantStandby;
        public static WorkGiverDef RSR_WorkGiver_CookRestaurantOrder;
        public static WorkGiverDef RSR_WorkGiver_DeliverRestaurantOrder;
        public static WorkGiverDef RSR_WorkGiver_RestaurantChefStandby;
        public static WorkGiverDef RSR_WorkGiver_RestaurantWaiterStandby;

        //初始化 DefOf 缓存，职责是让 RimWorld 在程序集加载时绑定 XML 定义。
        static DefOfRefs()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(DefOfRefs));
        }
    }
}
