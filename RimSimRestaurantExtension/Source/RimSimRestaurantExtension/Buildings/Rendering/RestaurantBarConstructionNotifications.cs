using HarmonyLib;
using RimWorld;
using Verse;

namespace RimSimRestaurantExtension.Buildings.Rendering
{
    //监听吧台施工对象变化，职责是让相邻成品和蓝图在施工或撤销后重新选择连接图块。
    internal static class RestaurantBarConstructionNotifications
    {
        //安装低频生命周期通知，职责是覆盖蓝图和施工框架而不逐帧刷新地图网格。
        internal static void Install()
        {
            var harmony = new Harmony("RimSimRestaurantExtension.BarConstruction");
            var spawn = new HarmonyMethod(typeof(RestaurantBarConstructionNotifications), nameof(Spawned));
            var before = new HarmonyMethod(typeof(RestaurantBarConstructionNotifications), nameof(BeforeDespawn));
            var after = new HarmonyMethod(typeof(RestaurantBarConstructionNotifications), nameof(Despawned));
            harmony.Patch(AccessTools.Method(typeof(Blueprint), nameof(Blueprint.SpawnSetup)), postfix: spawn);
            harmony.Patch(AccessTools.Method(typeof(Blueprint), nameof(Blueprint.DeSpawn)), prefix: before, postfix: after);
            harmony.Patch(AccessTools.Method(typeof(Building), nameof(Building.SpawnSetup)), postfix: spawn);
            harmony.Patch(AccessTools.Method(typeof(Building), nameof(Building.DeSpawn)), prefix: before, postfix: after);
        }

        //识别吧台施工对象，职责是排除其他建筑及已有专用通知的成品吧台。
        private static bool IsBarConstruction(Thing thing)
        {
            return (thing is Blueprint || thing is Frame) && thing.def.entityDefToBuild?.defName == "RSR_BarCounter";
        }

        //刷新出现的施工节点周围网格，职责是让相邻吧台立即连接新蓝图或框架。
        private static void Spawned(Thing __instance)
        {
            if (IsBarConstruction(__instance) && __instance.Spawned) Refresh(__instance.Map, __instance.Position);
        }

        //保存移除前的地图，职责是让撤销施工后仍能刷新原位置。
        private static void BeforeDespawn(Thing __instance, out Map __state)
        {
            __state = IsBarConstruction(__instance) ? __instance.Map : null;
        }

        //刷新已移除节点周围网格，职责是清除相邻吧台指向空格的连接。
        private static void Despawned(Thing __instance, Map __state)
        {
            if (__state != null) Refresh(__state, __instance.Position);
        }

        //标记当前格和邻格重绘，职责是覆盖跨地图网格区块的连接变化。
        private static void Refresh(Map map, IntVec3 cell)
        {
            map.mapDrawer.MapMeshDirty(cell, MapMeshFlagDefOf.Things, regenAdjacentCells: true, regenAdjacentSections: false);
        }
    }
}
