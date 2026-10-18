using HarmonyLib;
using RimSimRestaurantExtension.Conveyor.Rendering;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimSimRestaurantExtension.Conveyor.Placement
{
    //桥接原版施工流程，职责是为专用建造命令保留文化许可和框架连接预览。
    public static class ConveyorConstructionHooks
    {
        //安装仅作用于寿司传送带的原版钩子。
        public static void Install()
        {
            var harmony = new Harmony("RimSimRestaurantExtension.SushiConstruction");
            ConveyorTopologyNotifications.Install(harmony);
            harmony.Patch(AccessTools.Method(typeof(Ideo), nameof(Ideo.MembersCanBuild)),
                postfix: new HarmonyMethod(typeof(ConveyorConstructionHooks), nameof(AllowConstruction)));
            harmony.Patch(AccessTools.Method(typeof(Frame), "DrawAt"),
                postfix: new HarmonyMethod(typeof(ConveyorConstructionHooks), nameof(DrawFrame)));
        }

        //允许使用专用铺设命令的传送带施工，不改变其他文化建筑许可。
        public static void AllowConstruction(Thing thing, ref bool __result)
        {
            if (ConveyorLinks.IsBelt(thing)) __result = true;
        }

        //在施工框架上显示实际连接轮廓，职责是让框架与蓝图和成品具有一致形状。
        public static void DrawFrame(Frame __instance, Vector3 drawLoc)
        {
            if (!ConveyorLinks.IsBelt(__instance) || !__instance.Spawned) return;
            var def = (ThingDef)__instance.def.entityDefToBuild;
            var graphic = def.graphicData.Graphic.GetColoredVersion(ShaderDatabase.Transparent, new Color(0.55f, 0.8f, 1f, 0.55f), Color.white);
            graphic.DrawWorker(drawLoc + new Vector3(0f, 0.02f, 0f), __instance.Rotation, def, __instance, 0f);
        }
    }
}
