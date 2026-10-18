using SimManagementLib.Pojo;
using System;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    /// <summary>
    /// 负责为蓝图虚拟空间解析并绘制真实建筑 UI 图标。
    /// </summary>
    public sealed partial class Dialog_EditShopBlueprint
    {
        /// <summary>
        /// 在建筑占位内绘制真实建筑图标，无法解析或绘制失败时保留底层色块。
        /// </summary>
        private void DrawBuildingThingIcon(Rect rect, ShopBlueprintBuildingData building)
        {
            ThingDef thingDef = ResolveThingDef(building?.defName);
            if (thingDef == null)
                return;

            ThingDef stuffDef = ResolveThingDef(building.stuffDefName);
            ThingStyleDef styleDef = ResolveThingStyleDef(building.styleDefName);
            try
            {
                float scale = GetBuildingIconScale(rect);
                Widgets.ThingIcon(rect, thingDef, stuffDef, styleDef, scale);
            }
            catch (Exception)
            {
                // 部分模组建筑可能缺少 UI 图标或材质，画布保留底色避免编辑窗口中断。
            }
        }

        /// <summary>
        /// 根据建筑占位大小计算 UI 图标缩放，让单格建筑不溢出，大型建筑尽量填满占位。
        /// </summary>
        private static float GetBuildingIconScale(Rect rect)
        {
            if (rect.width <= 0f || rect.height <= 0f)
                return 1f;

            float shortSide = Mathf.Min(rect.width, rect.height);
            if (shortSide < 14f)
                return 0.72f;
            if (shortSide < 24f)
                return 0.86f;
            return 1f;
        }

        /// <summary>
        /// 静默解析 ThingDef 并缓存结果，避免编辑窗口每帧重复查找 Def。
        /// </summary>
        private ThingDef ResolveThingDef(string defName)
        {
            if (string.IsNullOrEmpty(defName))
                return null;

            if (thingDefCache.TryGetValue(defName, out ThingDef cachedDef))
                return cachedDef;

            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            thingDefCache[defName] = def;
            return def;
        }

        /// <summary>
        /// 静默解析 ThingStyleDef 并缓存结果，保证带样式建筑尽量使用原样图标。
        /// </summary>
        private ThingStyleDef ResolveThingStyleDef(string defName)
        {
            if (string.IsNullOrEmpty(defName))
                return null;

            if (thingStyleDefCache.TryGetValue(defName, out ThingStyleDef cachedDef))
                return cachedDef;

            ThingStyleDef def = DefDatabase<ThingStyleDef>.GetNamedSilentFail(defName);
            thingStyleDefCache[defName] = def;
            return def;
        }
    }
}
