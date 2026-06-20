using HarmonyLib;
using RimWorld;
using System.Reflection;
using Verse;
using Verse.AI;

namespace SimManagementLib.Patch
{
    //收藏品安装蓝图清理补丁，职责是移除已经失去待安装物的收藏品蓝图。
    [HarmonyPatch]
    public static class Patch_BlueprintInstall_Collectible
    {
        private static readonly FieldInfo MiniToInstallField = AccessTools.Field(typeof(Blueprint_Install), "miniToInstall");
        private static readonly FieldInfo BuildingToReinstallField = AccessTools.Field(typeof(Blueprint_Install), "buildingToReinstall");

        //定位真实绘制入口，职责是兼容 Blueprint_Install 继承 Thing.Print 但自身不声明 Print 的情况。
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Thing), nameof(Thing.Print), new[] { typeof(SectionLayer) });
        }

        //绘制安装蓝图前检查目标，职责是阻止空收藏品蓝图持续抛出渲染异常。
        private static bool Prefix(Thing __instance)
        {
            Blueprint_Install blueprint = __instance as Blueprint_Install;
            return !TryDestroyBrokenCollectibleInstallBlueprint(blueprint);
        }

        //清理失效收藏品安装蓝图，职责是给其他补丁入口复用同一套判定。
        public static bool TryDestroyBrokenCollectibleInstallBlueprint(Blueprint_Install blueprint)
        {
            if (!IsBrokenCollectibleInstallBlueprint(blueprint))
                return false;

            if (!blueprint.Destroyed)
                blueprint.Destroy(DestroyMode.Vanish);
            return true;
        }

        //判断蓝图是否属于失效收藏品安装蓝图，职责是只处理本模组收藏品而不影响普通安装蓝图。
        private static bool IsBrokenCollectibleInstallBlueprint(Blueprint_Install blueprint)
        {
            if (blueprint?.def?.entityDefToBuild == null)
                return false;

            if (!blueprint.def.entityDefToBuild.defName.StartsWith("RSMF_Collections_"))
                return false;

            return MiniToInstallField?.GetValue(blueprint) == null
                && BuildingToReinstallField?.GetValue(blueprint) == null;
        }
    }

    //施工搬运扫描清理补丁，职责是在原版 WorkGiver 读取安装目标前移除空收藏品蓝图。
    [HarmonyPatch(typeof(WorkGiver_ConstructDeliverResourcesToBlueprints))]
    public static class Patch_ConstructDeliverResourcesToBlueprints_CollectibleInstall
    {
        //扫描是否有工作前清理失效蓝图，职责是避免 DeliverResourcesToBlueprints 触发 Nothing to install。
        [HarmonyPatch(nameof(WorkGiver_ConstructDeliverResourcesToBlueprints.HasJobOnThing))]
        [HarmonyPrefix]
        public static bool HasJobOnThingPrefix(Thing t, ref bool __result)
        {
            if (Patch_BlueprintInstall_Collectible.TryDestroyBrokenCollectibleInstallBlueprint(t as Blueprint_Install))
            {
                __result = false;
                return false;
            }
            return true;
        }

        //创建工作前清理失效蓝图，职责是覆盖扫描和实际取 Job 之间蓝图失效的情况。
        [HarmonyPatch(nameof(WorkGiver_ConstructDeliverResourcesToBlueprints.JobOnThing))]
        [HarmonyPrefix]
        public static bool JobOnThingPrefix(Thing t, ref Job __result)
        {
            if (Patch_BlueprintInstall_Collectible.TryDestroyBrokenCollectibleInstallBlueprint(t as Blueprint_Install))
            {
                __result = null;
                return false;
            }
            return true;
        }
    }
}
