using HarmonyLib;
using RimWorld;
using SimManagementLib.SimThingClass;
using System.Collections.Generic;
using Verse;

namespace SimManagementLib.Patch
{
    /// <summary>
    /// 屏蔽虚拟货柜的原版内部物品选择按钮，负责避免检查面板每帧枚举大量虚拟库存。
    /// </summary>
    [HarmonyPatch(typeof(ContainingSelectionUtility), nameof(ContainingSelectionUtility.SelectableContainedThings))]
    public static class Patch_ContainingSelectionUtility_SimContainer
    {
        private static readonly IEnumerable<Thing> EmptyThings = new List<Thing>();

        /// <summary>
        /// 在原版枚举容器内物品前处理模拟经营货柜，负责把具体库存查看入口集中到货柜管理面板。
        /// </summary>
        public static bool Prefix(Thing container, ref IEnumerable<Thing> __result)
        {
            if (container is Building_SimContainer)
            {
                __result = EmptyThings;
                return false;
            }

            return true;
        }
    }
}
