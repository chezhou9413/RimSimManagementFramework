using SimManagementLib.Pojo;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 负责集中管理蓝图外部运行时配置桥接器，避免蓝图主流程硬编码具体模组。
    /// </summary>
    public static class BlueprintExternalConfigRegistry
    {
        private static readonly List<IShopBlueprintExternalConfigBridge> Bridges = new List<IShopBlueprintExternalConfigBridge>
        {
            new ShopBlueprintTextureAdjustmentBridge(),
            new BlueprintFufuBridge()
        };

        /// <summary>
        /// 从建筑上收集全部已注册软依赖桥接器支持的运行时配置。
        /// </summary>
        public static List<ShopBlueprintExternalConfigData> CaptureConfigs(Thing thing)
        {
            List<ShopBlueprintExternalConfigData> result = new List<ShopBlueprintExternalConfigData>();
            for (int i = 0; i < Bridges.Count; i++)
            {
                IShopBlueprintExternalConfigBridge bridge = Bridges[i];
                if (bridge == null || !bridge.IsAvailable)
                    continue;

                ShopBlueprintExternalConfigData config = bridge.Capture(thing);
                if (config == null)
                    continue;

                bridge.EnsureDefaults(config);
                if (string.IsNullOrWhiteSpace(config.payload))
                    continue;

                result.Add(config);
            }

            return result;
        }

        /// <summary>
        /// 为本次放置创建外部配置副本，并让每个桥接器处理自身的旋转语义。
        /// </summary>
        public static List<ShopBlueprintExternalConfigData> PrepareForPlacement(List<ShopBlueprintExternalConfigData> configs, Rot4 blueprintRot)
        {
            if (configs.NullOrEmpty())
                return configs;

            List<ShopBlueprintExternalConfigData> result = new List<ShopBlueprintExternalConfigData>();
            for (int i = 0; i < configs.Count; i++)
            {
                ShopBlueprintExternalConfigData config = configs[i];
                if (config == null)
                    continue;

                IShopBlueprintExternalConfigBridge bridge = FindBridge(config);
                ShopBlueprintExternalConfigData prepared = bridge != null
                    ? bridge.PrepareForPlacement(config, blueprintRot)
                    : CloneConfig(config);
                if (prepared == null)
                    continue;

                bridge?.EnsureDefaults(prepared);
                result.Add(prepared);
            }

            return result;
        }

        /// <summary>
        /// 补齐外部配置基础字段并移除无法识别的空条目。
        /// </summary>
        public static void EnsureDefaults(List<ShopBlueprintExternalConfigData> configs)
        {
            if (configs == null)
                return;

            for (int i = configs.Count - 1; i >= 0; i--)
            {
                ShopBlueprintExternalConfigData config = configs[i];
                if (config == null)
                {
                    configs.RemoveAt(i);
                    continue;
                }

                config.bridgeId = config.bridgeId ?? "";
                config.packageId = config.packageId ?? "";
                config.version = config.version ?? "";
                config.payload = config.payload ?? "";

                IShopBlueprintExternalConfigBridge bridge = FindBridge(config);
                bridge?.EnsureDefaults(config);
                if (string.IsNullOrWhiteSpace(config.bridgeId) && string.IsNullOrWhiteSpace(config.packageId))
                    configs.RemoveAt(i);
            }
        }

        /// <summary>
        /// 把蓝图中保存的全部外部配置应用到目标建筑。
        /// </summary>
        public static void ApplyConfigs(Thing thing, List<ShopBlueprintExternalConfigData> configs)
        {
            if (thing == null || configs.NullOrEmpty())
                return;

            for (int i = 0; i < configs.Count; i++)
            {
                ShopBlueprintExternalConfigData config = configs[i];
                IShopBlueprintExternalConfigBridge bridge = FindBridge(config);
                if (bridge == null || !bridge.IsAvailable)
                    continue;

                bridge.Apply(thing, config);
            }
        }

        /// <summary>
        /// 返回外部配置声明的全部软依赖模组。
        /// </summary>
        public static List<ShopBlueprintRequiredModData> CollectRequiredMods(List<ShopBlueprintExternalConfigData> configs)
        {
            List<ShopBlueprintRequiredModData> result = new List<ShopBlueprintRequiredModData>();
            if (configs.NullOrEmpty())
                return result;

            HashSet<string> added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < configs.Count; i++)
            {
                ShopBlueprintExternalConfigData config = configs[i];
                if (config == null)
                    continue;

                IShopBlueprintExternalConfigBridge bridge = FindBridge(config);
                ShopBlueprintRequiredModData mod = bridge?.RequiredMod ?? BuildFallbackRequiredMod(config.packageId);
                if (mod == null || string.IsNullOrWhiteSpace(mod.packageId) || !added.Add(mod.packageId))
                    continue;

                result.Add(mod);
            }

            return result;
        }

        /// <summary>
        /// 判断列表是否包含可应用的外部配置。
        /// </summary>
        public static bool HasConfigs(List<ShopBlueprintExternalConfigData> configs)
        {
            return configs != null && configs.Any(config => config != null && !string.IsNullOrWhiteSpace(config.payload));
        }

        /// <summary>
        /// 返回指定配置对应的桥接器，优先按桥接标识匹配，其次按 PackageId 兼容旧数据。
        /// </summary>
        private static IShopBlueprintExternalConfigBridge FindBridge(ShopBlueprintExternalConfigData config)
        {
            if (config == null)
                return null;

            for (int i = 0; i < Bridges.Count; i++)
            {
                IShopBlueprintExternalConfigBridge bridge = Bridges[i];
                if (bridge == null)
                    continue;
                if (!string.IsNullOrWhiteSpace(config.bridgeId) && string.Equals(bridge.BridgeId, config.bridgeId, StringComparison.OrdinalIgnoreCase))
                    return bridge;
                if (!string.IsNullOrWhiteSpace(config.packageId) && string.Equals(bridge.PackageId, config.packageId, StringComparison.OrdinalIgnoreCase))
                    return bridge;
            }

            return null;
        }

        /// <summary>
        /// 克隆未知桥接器配置，确保放置流程不会修改蓝图原始数据。
        /// </summary>
        private static ShopBlueprintExternalConfigData CloneConfig(ShopBlueprintExternalConfigData source)
        {
            return new ShopBlueprintExternalConfigData
            {
                bridgeId = source.bridgeId ?? "",
                packageId = source.packageId ?? "",
                version = source.version ?? "",
                payload = source.payload ?? ""
            };
        }

        /// <summary>
        /// 为当前版本尚未注册桥接器的外部配置创建最低限度依赖信息。
        /// </summary>
        private static ShopBlueprintRequiredModData BuildFallbackRequiredMod(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId))
                return null;

            return new ShopBlueprintRequiredModData
            {
                packageId = packageId,
                displayName = packageId,
                steamWorkshopUrl = "",
                steamAppId = 0u,
                isOfficial = false
            };
        }
    }
}
