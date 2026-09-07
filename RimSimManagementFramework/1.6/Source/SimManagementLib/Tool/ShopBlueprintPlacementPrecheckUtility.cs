using RimWorld;
using SimManagementLib.Pojo;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 负责在店铺蓝图真正放置前检查缺失建筑和缺失材料，并提供可替换的原版材料列表。
    /// </summary>
    public static class ShopBlueprintPlacementPrecheckUtility
    {
        /// <summary>
        /// 检查蓝图放置依赖，并返回缺失建筑和缺失材料摘要。
        /// </summary>
        public static ShopBlueprintPlacementPrecheckResult Check(ShopBlueprintData data, ShopBlueprintPlacementOptions options)
        {
            ShopBlueprintPlacementPrecheckResult result = new ShopBlueprintPlacementPrecheckResult();
            if (data?.buildings == null)
                return result;

            Dictionary<string, ShopBlueprintMissingBuilding> missingBuildings = new Dictionary<string, ShopBlueprintMissingBuilding>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, ShopBlueprintMissingStuff> missingStuffs = new Dictionary<string, ShopBlueprintMissingStuff>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < data.buildings.Count; i++)
            {
                ShopBlueprintBuildingData building = data.buildings[i];
                if (building == null || string.IsNullOrEmpty(building.defName))
                    continue;

                ThingDef buildingDef = DefDatabase<ThingDef>.GetNamedSilentFail(building.defName);
                if (buildingDef == null)
                {
                    AddMissingBuilding(missingBuildings, building);
                    continue;
                }

                if (!NeedsStuffReplacement(building, buildingDef, options))
                    continue;

                AddMissingStuff(missingStuffs, building, buildingDef);
            }

            result.MissingBuildings.AddRange(missingBuildings.Values);
            result.MissingStuffs.AddRange(missingStuffs.Values.Where(item => item.ReplacementOptions.Count > 0));
            result.UnreplaceableStuffs.AddRange(missingStuffs.Values.Where(item => item.ReplacementOptions.Count == 0));
            return result;
        }

        /// <summary>
        /// 判断材料是否能用于指定建筑。
        /// </summary>
        public static bool IsUsableStuffFor(ThingDef stuff, ThingDef buildingDef)
        {
            return stuff != null
                && buildingDef != null
                && buildingDef.MadeFromStuff
                && stuff.IsStuff
                && stuff.stuffProps != null
                && stuff.stuffProps.CanMake(buildingDef);
        }

        /// <summary>
        /// 返回指定建筑可用的原版材料列表。
        /// </summary>
        public static List<ThingDef> GetCoreReplacementStuffs(ThingDef buildingDef)
        {
            List<ThingDef> result = new List<ThingDef>();
            if (buildingDef == null || !buildingDef.MadeFromStuff)
                return result;

            foreach (ThingDef stuff in GenStuff.AllowedStuffsFor(buildingDef))
            {
                if (!IsCoreStuff(stuff) || !IsUsableStuffFor(stuff, buildingDef))
                    continue;

                result.Add(stuff);
            }

            result.Sort((left, right) => string.Compare(left.LabelCap.Resolve(), right.LabelCap.Resolve(), StringComparison.CurrentCultureIgnoreCase));
            return result;
        }

        /// <summary>
        /// 返回缺失建筑提示文本。
        /// </summary>
        public static string BuildMissingBuildingsMessage(ShopBlueprintPlacementPrecheckResult result)
        {
            string list = "\n" + string.Join("\n", result.MissingBuildings.Select(item => "- " + item.DisplayLabel));
            return SimTranslation.T("RSMF.Blueprint.Place.Error.MissingBuildings", list.Named("items"));
        }

        /// <summary>
        /// 返回无法替换材料提示文本。
        /// </summary>
        public static string BuildUnreplaceableStuffsMessage(ShopBlueprintPlacementPrecheckResult result)
        {
            string list = "\n" + string.Join("\n", result.UnreplaceableStuffs.Select(item => "- " + item.DisplayLabel));
            return SimTranslation.T("RSMF.Blueprint.Place.Error.MissingStuffNoReplacement", list.Named("items"));
        }

        /// <summary>
        /// 判断指定蓝图建筑是否因为材料缺失或不兼容而需要玩家选择替代材料。
        /// </summary>
        private static bool NeedsStuffReplacement(ShopBlueprintBuildingData building, ThingDef buildingDef, ShopBlueprintPlacementOptions options)
        {
            if (building == null || buildingDef == null || !buildingDef.MadeFromStuff)
                return false;
            if (string.IsNullOrEmpty(building.stuffDefName))
                return false;

            ThingDef savedStuff = DefDatabase<ThingDef>.GetNamedSilentFail(building.stuffDefName);
            if (IsUsableStuffFor(savedStuff, buildingDef))
                return false;

            return options == null || !options.TryGetStuffReplacement(building, buildingDef, out _);
        }

        /// <summary>
        /// 把缺失建筑记录合并到摘要表。
        /// </summary>
        private static void AddMissingBuilding(Dictionary<string, ShopBlueprintMissingBuilding> missingBuildings, ShopBlueprintBuildingData building)
        {
            if (building == null || string.IsNullOrEmpty(building.defName))
                return;

            if (!missingBuildings.TryGetValue(building.defName, out ShopBlueprintMissingBuilding missing))
            {
                missing = new ShopBlueprintMissingBuilding
                {
                    DefName = building.defName,
                    SavedLabel = building.label ?? ""
                };
                missingBuildings[building.defName] = missing;
            }

            missing.Count++;
        }

        /// <summary>
        /// 把缺失材料记录合并到摘要表。
        /// </summary>
        private static void AddMissingStuff(Dictionary<string, ShopBlueprintMissingStuff> missingStuffs, ShopBlueprintBuildingData building, ThingDef buildingDef)
        {
            if (building == null || buildingDef == null || string.IsNullOrEmpty(building.stuffDefName))
                return;

            string key = building.defName + "||" + building.stuffDefName;
            if (!missingStuffs.TryGetValue(key, out ShopBlueprintMissingStuff missing))
            {
                missing = new ShopBlueprintMissingStuff
                {
                    BuildingDefName = building.defName,
                    BuildingLabel = buildingDef.LabelCap.Resolve(),
                    MissingStuffDefName = building.stuffDefName,
                    ReplacementOptions = GetCoreReplacementStuffs(buildingDef)
                };
                missingStuffs[key] = missing;
            }

            missing.Count++;
        }

        /// <summary>
        /// 判断材料是否来自原版 Core。
        /// </summary>
        private static bool IsCoreStuff(ThingDef stuff)
        {
            return stuff?.modContentPack != null && stuff.modContentPack.IsCoreMod;
        }
    }

    /// <summary>
    /// 负责承载店铺蓝图放置前的依赖检查结果。
    /// </summary>
    public sealed class ShopBlueprintPlacementPrecheckResult
    {
        public readonly List<ShopBlueprintMissingBuilding> MissingBuildings = new List<ShopBlueprintMissingBuilding>();
        public readonly List<ShopBlueprintMissingStuff> MissingStuffs = new List<ShopBlueprintMissingStuff>();
        public readonly List<ShopBlueprintMissingStuff> UnreplaceableStuffs = new List<ShopBlueprintMissingStuff>();

        /// <summary>
        /// 判断当前检查结果是否存在缺失建筑。
        /// </summary>
        public bool HasMissingBuildings => MissingBuildings.Count > 0;

        /// <summary>
        /// 判断当前检查结果是否存在可替换的缺失材料。
        /// </summary>
        public bool HasReplaceableStuffs => MissingStuffs.Count > 0;

        /// <summary>
        /// 判断当前检查结果是否存在无法替换的缺失材料。
        /// </summary>
        public bool HasUnreplaceableStuffs => UnreplaceableStuffs.Count > 0;
    }

    /// <summary>
    /// 负责描述蓝图中缺失的建筑 Def。
    /// </summary>
    public sealed class ShopBlueprintMissingBuilding
    {
        public string DefName;
        public string SavedLabel;
        public int Count;

        /// <summary>
        /// 返回用于玩家提示的缺失建筑文本。
        /// </summary>
        public string DisplayLabel
        {
            get
            {
                string label = string.IsNullOrEmpty(SavedLabel) ? DefName : SavedLabel + " (" + DefName + ")";
                return Count > 1 ? label + " x" + Count : label;
            }
        }
    }

    /// <summary>
    /// 负责描述蓝图中缺失或不可用的材料 Def。
    /// </summary>
    public sealed class ShopBlueprintMissingStuff
    {
        public string BuildingDefName;
        public string BuildingLabel;
        public string MissingStuffDefName;
        public int Count;
        public List<ThingDef> ReplacementOptions = new List<ThingDef>();

        /// <summary>
        /// 返回用于玩家提示的缺失材料文本。
        /// </summary>
        public string DisplayLabel
        {
            get
            {
                string label = SimTranslation.T(
                    "RSMF.Blueprint.Place.MissingStuff.Entry",
                    BuildingLabel.Named("building"),
                    MissingStuffDefName.Named("stuff"));
                return Count > 1 ? label + " x" + Count : label;
            }
        }
    }
}
