using HarmonyLib;
using RimWorld;
using SimManagementLib.SimMapComp;
using System.Collections.Generic;
using Verse;

namespace SimManagementLib.Patch
{
    /// <summary>
    /// 负责在建筑施工完成后触发店铺蓝图经营配置写入。
    /// </summary>
    [HarmonyPatch(typeof(Frame), nameof(Frame.CompleteConstruction))]
    public static class Patch_Frame_ShopBlueprintPlacement
    {
        /// <summary>
        /// 负责保存施工框架完工前的位置上下文。
        /// </summary>
        public sealed class FrameCompletionState
        {
            public Map map;
            public IntVec3 position;
        }

        /// <summary>
        /// 在原版销毁施工框架前保存地图和位置，供完工后查找新建筑。
        /// </summary>
        public static void Prefix(Frame __instance, out FrameCompletionState __state)
        {
            __state = new FrameCompletionState
            {
                map = __instance?.Map,
                position = __instance?.Position ?? IntVec3.Invalid
            };
        }

        /// <summary>
        /// 在原版完成施工后扫描同格新建筑，并应用蓝图中保存的货柜、招牌和经营组件配置。
        /// </summary>
        public static void Postfix(Frame __instance, FrameCompletionState __state)
        {
            Map map = __state?.map;
            IntVec3 position = __state?.position ?? IntVec3.Invalid;
            if (map == null || !position.IsValid)
                return;

            MapComponent_ShopBlueprintPlacement component = map.GetComponent<MapComponent_ShopBlueprintPlacement>();
            if (component == null)
                return;

            List<Thing> things = position.GetThingList(map);
            for (int i = things.Count - 1; i >= 0; i--)
            {
                Thing thing = things[i];
                if (!(thing is Building) || thing is Frame)
                    continue;

                if (component.TryApplyPendingConfig(thing))
                    return;
            }
        }
    }
}
