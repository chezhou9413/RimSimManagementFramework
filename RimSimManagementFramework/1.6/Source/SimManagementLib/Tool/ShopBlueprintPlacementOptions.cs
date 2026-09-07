using SimManagementLib.Pojo;
using System;
using System.Collections.Generic;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 负责保存一次店铺蓝图放置过程中玩家确认的材料替换选择。
    /// </summary>
    public sealed class ShopBlueprintPlacementOptions
    {
        private readonly Dictionary<string, string> stuffReplacementByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 记录指定建筑和缺失材料对应的替代材料。
        /// </summary>
        public void SetStuffReplacement(string buildingDefName, string missingStuffDefName, ThingDef replacementStuff)
        {
            string key = BuildStuffReplacementKey(buildingDefName, missingStuffDefName);
            if (string.IsNullOrEmpty(key))
                return;

            if (replacementStuff == null || string.IsNullOrEmpty(replacementStuff.defName))
            {
                stuffReplacementByKey.Remove(key);
                return;
            }

            stuffReplacementByKey[key] = replacementStuff.defName;
        }

        /// <summary>
        /// 尝试返回指定蓝图建筑当前可用的替代材料。
        /// </summary>
        public bool TryGetStuffReplacement(ShopBlueprintBuildingData buildingData, ThingDef buildingDef, out ThingDef replacementStuff)
        {
            replacementStuff = null;
            if (buildingData == null || buildingDef == null || string.IsNullOrEmpty(buildingData.stuffDefName))
                return false;

            string key = BuildStuffReplacementKey(buildingData.defName, buildingData.stuffDefName);
            if (string.IsNullOrEmpty(key) || !stuffReplacementByKey.TryGetValue(key, out string replacementDefName))
                return false;

            replacementStuff = DefDatabase<ThingDef>.GetNamedSilentFail(replacementDefName);
            if (!ShopBlueprintPlacementPrecheckUtility.IsUsableStuffFor(replacementStuff, buildingDef))
            {
                replacementStuff = null;
                return false;
            }

            return true;
        }

        /// <summary>
        /// 根据建筑 DefName 和缺失材料 DefName 构造稳定替换键。
        /// </summary>
        private static string BuildStuffReplacementKey(string buildingDefName, string missingStuffDefName)
        {
            if (string.IsNullOrEmpty(buildingDefName) || string.IsNullOrEmpty(missingStuffDefName))
                return null;

            return buildingDefName + "||" + missingStuffDefName;
        }
    }
}
