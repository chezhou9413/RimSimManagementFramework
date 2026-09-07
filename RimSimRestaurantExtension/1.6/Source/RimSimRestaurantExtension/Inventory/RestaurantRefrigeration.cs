using HarmonyLib;
using RimSimRestaurantExtension.Buildings;
using RimWorld;
namespace RimSimRestaurantExtension.Inventory
{
    //冷藏组件桥接，职责是仅暂停通电冰箱内的腐坏更新。
    public static class RestaurantRefrigeration
    {
        //安装原版腐坏入口的前置处理，职责是不影响其他物品组件更新。
        public static void Install()
        {
            var harmony = new Harmony("RimSimRestaurantExtension.Refrigeration");
            var prefix = new HarmonyMethod(typeof(RestaurantRefrigeration), nameof(ShouldRot));
            harmony.Patch(AccessTools.Method(typeof(CompRottable), nameof(CompRottable.CompTickInterval)), prefix: prefix);
            harmony.Patch(AccessTools.Method(typeof(CompRottable), nameof(CompRottable.CompTickRare)), prefix: prefix);
        }

        //检查实物直接持有者，职责是保留已有腐坏进度并在断电后正常推进。
        public static bool ShouldRot(CompRottable __instance)
        {
            return !(__instance.parent.ParentHolder is Building_RestaurantStorage storage && storage.IsCooling);
        }
    }
}
