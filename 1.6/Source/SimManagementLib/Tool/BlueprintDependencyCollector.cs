using RimWorld;
using SimManagementLib.Pojo;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 负责从店铺蓝图中收集全部真实依赖模组，用于上传和兼容校验。
    /// </summary>
    public static class BlueprintDependencyCollector
    {
        /// <summary>
        /// 根据蓝图内容收集依赖模组列表。
        /// </summary>
        public static List<ShopBlueprintRequiredModData> CollectRequiredMods(ShopBlueprintData data)
        {
            Dictionary<string, ShopBlueprintRequiredModData> result = new Dictionary<string, ShopBlueprintRequiredModData>(System.StringComparer.OrdinalIgnoreCase);
            if (data == null)
                return result.Values.ToList();

            CollectTerrainMods(data, result);
            CollectBuildingMods(data, result);
            return result.Values
                .OrderBy(item => item.isOfficial ? 0 : 1)
                .ThenBy(item => item.displayName)
                .ThenBy(item => item.packageId)
                .ToList();
        }

        /// <summary>
        /// 为蓝图补齐依赖模组列表。
        /// </summary>
        public static void PopulateRequiredMods(ShopBlueprintData data)
        {
            if (data == null)
                return;

            data.requiredMods = CollectRequiredMods(data);
        }

        private static void CollectTerrainMods(ShopBlueprintData data, Dictionary<string, ShopBlueprintRequiredModData> result)
        {
            if (data.terrains == null)
                return;

            for (int i = 0; i < data.terrains.Count; i++)
            {
                TerrainDef terrainDef = ResolveTerrainDef(data.terrains[i]);
                AddModFromDef(terrainDef, result);
            }
        }

        private static void CollectBuildingMods(ShopBlueprintData data, Dictionary<string, ShopBlueprintRequiredModData> result)
        {
            if (data.buildings == null)
                return;

            for (int i = 0; i < data.buildings.Count; i++)
            {
                ShopBlueprintBuildingData building = data.buildings[i];
                if (building == null)
                    continue;

                AddModFromDef(DefDatabase<ThingDef>.GetNamedSilentFail(building.defName), result);
                AddModFromDef(DefDatabase<ThingDef>.GetNamedSilentFail(building.stuffDefName), result);
                AddModFromDef(DefDatabase<ThingStyleDef>.GetNamedSilentFail(building.styleDefName), result);
                if (building.textureAdjustment != null)
                    AddRequiredMod(ShopBlueprintTextureAdjustmentBridge.BuildRequiredMod(), result);
                List<ShopBlueprintRequiredModData> externalMods = BlueprintExternalConfigRegistry.CollectRequiredMods(building.externalConfigs);
                for (int e = 0; e < externalMods.Count; e++)
                    AddRequiredMod(externalMods[e], result);

                if (building.goods?.items != null)
                {
                    for (int g = 0; g < building.goods.items.Count; g++)
                    {
                        ShopBlueprintGoodsItemConfig item = building.goods.items[g];
                        if (item == null)
                            continue;

                        AddModFromDef(DefDatabase<ThingDef>.GetNamedSilentFail(item.thingDefName), result);
                    }
                }

                if (building.service?.slots != null)
                {
                    for (int s = 0; s < building.service.slots.Count; s++)
                    {
                        ShopBlueprintServiceSlotConfig slot = building.service.slots[s];
                        if (slot == null)
                            continue;

                        AddModFromDef(DefDatabase<Def>.GetNamedSilentFail(slot.serviceDefName), result);
                    }
                }
            }
        }

        private static TerrainDef ResolveTerrainDef(ShopBlueprintTerrainData data)
        {
            if (data == null || string.IsNullOrWhiteSpace(data.terrainDefName))
                return null;
            return DefDatabase<TerrainDef>.GetNamedSilentFail(data.terrainDefName);
        }

        /// <summary>
        /// 把外部运行时配置声明的模组依赖加入结果集。
        /// </summary>
        private static void AddRequiredMod(ShopBlueprintRequiredModData mod, Dictionary<string, ShopBlueprintRequiredModData> result)
        {
            if (mod == null || string.IsNullOrWhiteSpace(mod.packageId))
                return;

            if (result.TryGetValue(mod.packageId, out ShopBlueprintRequiredModData existing))
            {
                MergeRequiredMod(existing, mod);
                return;
            }

            result[mod.packageId] = mod;
        }

        /// <summary>
        /// 用软依赖桥接器提供的更完整信息补齐已收集的同包依赖。
        /// </summary>
        private static void MergeRequiredMod(ShopBlueprintRequiredModData existing, ShopBlueprintRequiredModData incoming)
        {
            if (existing == null || incoming == null)
                return;

            if (string.IsNullOrWhiteSpace(existing.displayName) && !string.IsNullOrWhiteSpace(incoming.displayName))
                existing.displayName = incoming.displayName;
            if (string.IsNullOrWhiteSpace(existing.steamWorkshopUrl) && !string.IsNullOrWhiteSpace(incoming.steamWorkshopUrl))
                existing.steamWorkshopUrl = incoming.steamWorkshopUrl;
            if (existing.steamAppId == 0u && incoming.steamAppId != 0u)
                existing.steamAppId = incoming.steamAppId;
            existing.isOfficial = existing.isOfficial || incoming.isOfficial;
        }

        private static void AddModFromDef(Def def, Dictionary<string, ShopBlueprintRequiredModData> result)
        {
            ModContentPack mod = def?.modContentPack;
            if (mod == null || string.IsNullOrWhiteSpace(mod.PackageId))
                return;
            if (result.ContainsKey(mod.PackageId))
                return;

            string workshopUrl = "";
            uint steamAppId = mod.SteamAppId;
            if (mod.ModMetaData != null && mod.ModMetaData.OnSteamWorkshop)
                workshopUrl = TryBuildWorkshopUrl(mod.ModMetaData);
            else if (steamAppId != 0)
                workshopUrl = "https://steamcommunity.com/workshop/filedetails/?id=" + steamAppId;

            result[mod.PackageId] = new ShopBlueprintRequiredModData
            {
                packageId = mod.PackageId,
                displayName = mod.Name ?? mod.PackageId,
                steamWorkshopUrl = workshopUrl ?? "",
                steamAppId = steamAppId,
                isOfficial = mod.IsOfficialMod
            };
        }

        /// <summary>
        /// 通过反射读取模组创意工坊文件 ID，并拼出玩家可打开的创意工坊地址。
        /// </summary>
        private static string TryBuildWorkshopUrl(ModMetaData modMetaData)
        {
            if (modMetaData == null)
                return "";

            try
            {
                MethodInfo getPublishedFileId = typeof(ModMetaData).GetMethod("GetPublishedFileId", BindingFlags.Public | BindingFlags.Instance);
                object publishedFileId = getPublishedFileId?.Invoke(modMetaData, null);
                string fileIdText = publishedFileId?.ToString() ?? "";
                return string.IsNullOrWhiteSpace(fileIdText)
                    ? ""
                    : "https://steamcommunity.com/workshop/filedetails/?id=" + fileIdText;
            }
            catch
            {
                return "";
            }
        }
    }
}
