using HarmonyLib;
using RimWorld;
using System.Reflection;
using Verse;

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
            if (!IsBrokenCollectibleInstallBlueprint(blueprint))
                return true;

            if (!blueprint.Destroyed)
                blueprint.Destroy(DestroyMode.Vanish);
            return false;
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
}
