using HarmonyLib;
using RimSimRestaurantExtension.Conveyor.Transport;
using RimWorld;
using Verse;

namespace RimSimRestaurantExtension.Conveyor.Placement
{
    //监听传送带施工对象的生命周期，职责是让连接缓存感知蓝图和施工框架的出现与移除。
    internal static class ConveyorTopologyNotifications
    {
        //安装建筑与蓝图的低频事件钩子，职责是避免逐步扫描未完成的施工节点。
        internal static void Install(Harmony harmony)
        {
            var spawn = new HarmonyMethod(typeof(ConveyorTopologyNotifications), nameof(Spawned));
            var before = new HarmonyMethod(typeof(ConveyorTopologyNotifications), nameof(BeforeDespawn));
            var after = new HarmonyMethod(typeof(ConveyorTopologyNotifications), nameof(Despawned));
            harmony.Patch(AccessTools.Method(typeof(Blueprint), nameof(Blueprint.SpawnSetup)), postfix: spawn);
            harmony.Patch(AccessTools.Method(typeof(Blueprint), nameof(Blueprint.DeSpawn)), prefix: before, postfix: after);
            harmony.Patch(AccessTools.Method(typeof(Building), nameof(Building.SpawnSetup)), postfix: spawn);
            harmony.Patch(AccessTools.Method(typeof(Building), nameof(Building.DeSpawn)), prefix: before, postfix: after);
        }

        //登记施工节点出现，职责是仅让寿司传送带的蓝图和框架触发拓扑更新。
        private static void Spawned(Thing __instance)
        {
            if ((__instance is Blueprint || __instance is Frame) && ConveyorLinks.IsBelt(__instance) && __instance.Spawned)
                __instance.Map.GetComponent<MapComponent_SushiConveyor>().Dirty();
        }

        //记录施工对象原地图，职责是在移除后仍能通知对应地图的线路缓存。
        private static void BeforeDespawn(Thing __instance, out Map __state)
        {
            __state = (__instance is Blueprint || __instance is Frame) && ConveyorLinks.IsBelt(__instance) ? __instance.Map : null;
        }

        //通知施工节点已经移除，职责是让取消蓝图或施工完成后的连接及时重建。
        private static void Despawned(Map __state)
        {
            __state?.GetComponent<MapComponent_SushiConveyor>().Dirty();
        }
    }
}
