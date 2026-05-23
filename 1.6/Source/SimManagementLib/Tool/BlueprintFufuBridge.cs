using HarmonyLib;
using RimWorld;
using SimManagementLib.Pojo;
using System;
using System.Reflection;
using UnityEngine;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 负责通过反射桥接 Fufu 建筑的缩放、偏移和绘制层级配置。
    /// </summary>
    public sealed class BlueprintFufuBridge : IShopBlueprintExternalConfigBridge
    {
        private const string BridgeIdValue = "FufuBuildingOffset";
        private const string PackageIdValue = "fxz.myafufu";
        private const string DisplayName = "Mya's fufu";
        private const string WorkshopUrl = "https://steamcommunity.com/workshop/filedetails/?id=3266224389";
        private const uint SteamAppId = 3266224389u;

        private static Type fufuType;
        private static FieldInfo sizeFixedField;
        private static FieldInfo sizeFixedFakeField;
        private static FieldInfo offsetFixedField;
        private static FieldInfo offsetXFixedField;
        private static FieldInfo layerField;
        private static bool reflectionResolved;

        /// <summary>
        /// 返回 Fufu 桥接器的稳定标识。
        /// </summary>
        public string BridgeId => BridgeIdValue;

        /// <summary>
        /// 返回 Fufu 模组的 PackageId。
        /// </summary>
        public string PackageId => PackageIdValue;

        /// <summary>
        /// 返回当前存档是否可以访问 Fufu 类型和字段。
        /// </summary>
        public bool IsAvailable => EnsureReflection();

        /// <summary>
        /// 返回蓝图依赖校验需要声明的 Fufu 模组信息。
        /// </summary>
        public ShopBlueprintRequiredModData RequiredMod => BuildRequiredMod();

        /// <summary>
        /// 从 Fufu 建筑读取缩放、偏移和绘制层级配置。
        /// </summary>
        public ShopBlueprintExternalConfigData Capture(Thing thing)
        {
            if (thing == null || !EnsureReflection() || !fufuType.IsInstanceOfType(thing))
                return null;

            try
            {
                FufuOffsetConfig config = new FufuOffsetConfig
                {
                    sizeFixed = GetFloat(sizeFixedField, thing, 1f),
                    sizeFixedFake = GetInt(sizeFixedFakeField, thing, 10),
                    offsetFixed = GetInt(offsetFixedField, thing, 0),
                    offsetXFixed = GetInt(offsetXFixedField, thing, 0),
                    layer = GetLayerName(layerField, thing)
                };
                EnsureConfigDefaults(config);
                return IsDefaultConfig(config) ? null : ToExternalConfig(config);
            }
            catch (Exception ex)
            {
                Log.Warning("[SimManagementLib] 读取 Fufu 蓝图偏移配置失败：" + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// 按蓝图整体旋转调整 Fufu 偏移配置，并返回通用外部配置副本。
        /// </summary>
        public ShopBlueprintExternalConfigData PrepareForPlacement(ShopBlueprintExternalConfigData config, Rot4 blueprintRot)
        {
            FufuOffsetConfig typedConfig = FromExternalConfig(config);
            if (typedConfig == null)
                return null;

            if (blueprintRot != Rot4.North)
            {
                int sourceX = typedConfig.offsetXFixed;
                int sourceZ = typedConfig.offsetFixed;
                switch (blueprintRot.AsInt)
                {
                    case Rot4.EastInt:
                        typedConfig.offsetXFixed = -sourceZ;
                        typedConfig.offsetFixed = sourceX;
                        break;
                    case Rot4.SouthInt:
                        typedConfig.offsetXFixed = -sourceX;
                        typedConfig.offsetFixed = -sourceZ;
                        break;
                    case Rot4.WestInt:
                        typedConfig.offsetXFixed = sourceZ;
                        typedConfig.offsetFixed = -sourceX;
                        break;
                }
            }

            EnsureConfigDefaults(typedConfig);
            return ToExternalConfig(typedConfig);
        }

        /// <summary>
        /// 补齐 Fufu 外部配置的基础字段。
        /// </summary>
        public void EnsureDefaults(ShopBlueprintExternalConfigData config)
        {
            if (config == null)
                return;

            config.bridgeId = BridgeIdValue;
            config.packageId = PackageIdValue;
            config.version = string.IsNullOrWhiteSpace(config.version) ? "1" : config.version;
            config.payload = config.payload ?? "";
        }

        /// <summary>
        /// 将蓝图中的 Fufu 偏移配置写入目标 Fufu 建筑。
        /// </summary>
        public bool Apply(Thing thing, ShopBlueprintExternalConfigData config)
        {
            if (thing == null || !EnsureReflection() || !fufuType.IsInstanceOfType(thing))
                return false;

            FufuOffsetConfig typedConfig = FromExternalConfig(config);
            if (typedConfig == null)
                return false;

            try
            {
                sizeFixedField.SetValue(thing, typedConfig.sizeFixed);
                sizeFixedFakeField.SetValue(thing, typedConfig.sizeFixedFake);
                offsetFixedField.SetValue(thing, typedConfig.offsetFixed);
                offsetXFixedField.SetValue(thing, typedConfig.offsetXFixed);
                layerField.SetValue(thing, ParseLayer(typedConfig.layer));
                if (thing.Spawned)
                    thing.Map.mapDrawer.MapMeshDirty(thing.Position, MapMeshFlagDefOf.Things);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning("[SimManagementLib] 应用 Fufu 蓝图偏移配置失败：" + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 返回蓝图上传和下载校验使用的 Fufu 模组依赖信息。
        /// </summary>
        private static ShopBlueprintRequiredModData BuildRequiredMod()
        {
            ModMetaData meta = ModLister.GetActiveModWithIdentifier(PackageIdValue, true);
            return new ShopBlueprintRequiredModData
            {
                packageId = PackageIdValue,
                displayName = meta?.Name ?? DisplayName,
                steamWorkshopUrl = WorkshopUrl,
                steamAppId = SteamAppId,
                isOfficial = false
            };
        }

        /// <summary>
        /// 解析 Fufu 类型和字段，负责确保软依赖缺失时安全跳过。
        /// </summary>
        private static bool EnsureReflection()
        {
            if (reflectionResolved)
                return fufuType != null
                    && sizeFixedField != null
                    && sizeFixedFakeField != null
                    && offsetFixedField != null
                    && offsetXFixedField != null
                    && layerField != null;

            reflectionResolved = true;
            if (ModLister.GetActiveModWithIdentifier(PackageIdValue, true) == null)
                return false;

            fufuType = AccessTools.TypeByName("Fufu.Fufu");
            if (fufuType == null)
                return false;

            sizeFixedField = AccessTools.Field(fufuType, "sizeFixed");
            sizeFixedFakeField = AccessTools.Field(fufuType, "sizeFixedFake");
            offsetFixedField = AccessTools.Field(fufuType, "offsetFixed");
            offsetXFixedField = AccessTools.Field(fufuType, "offsetXFixed");
            layerField = AccessTools.Field(fufuType, "layer");

            return fufuType != null
                && sizeFixedField != null
                && sizeFixedFakeField != null
                && offsetFixedField != null
                && offsetXFixedField != null
                && layerField != null;
        }

        /// <summary>
        /// 将 Fufu 私有配置对象打包为通用外部配置。
        /// </summary>
        private static ShopBlueprintExternalConfigData ToExternalConfig(FufuOffsetConfig config)
        {
            if (config == null)
                return null;

            return new ShopBlueprintExternalConfigData
            {
                bridgeId = BridgeIdValue,
                packageId = PackageIdValue,
                version = "1",
                payload = BlueprintExternalConfigPayload.Serialize(config)
            };
        }

        /// <summary>
        /// 将通用外部配置还原为 Fufu 私有配置对象。
        /// </summary>
        private static FufuOffsetConfig FromExternalConfig(ShopBlueprintExternalConfigData config)
        {
            FufuOffsetConfig result = BlueprintExternalConfigPayload.Deserialize<FufuOffsetConfig>(config?.payload);
            EnsureConfigDefaults(result);
            return result;
        }

        /// <summary>
        /// 补齐 Fufu 配置中的默认值并限制到模组 UI 使用的有效范围。
        /// </summary>
        private static void EnsureConfigDefaults(FufuOffsetConfig config)
        {
            if (config == null)
                return;

            if (config.sizeFixedFake <= 0)
                config.sizeFixedFake = config.sizeFixed > 0f ? Mathf.RoundToInt(config.sizeFixed * 10f) : 10;
            config.sizeFixedFake = Mathf.Clamp(config.sizeFixedFake, 1, 20);
            if (config.sizeFixed <= 0f)
                config.sizeFixed = config.sizeFixedFake / 10f;
            config.offsetFixed = Mathf.Clamp(config.offsetFixed, -10, 10);
            config.offsetXFixed = Mathf.Clamp(config.offsetXFixed, -10, 10);
            if (string.IsNullOrWhiteSpace(config.layer))
                config.layer = AltitudeLayer.BuildingOnTop.ToString();
        }

        /// <summary>
        /// 判断 Fufu 配置是否等同于模组默认值。
        /// </summary>
        private static bool IsDefaultConfig(FufuOffsetConfig config)
        {
            if (config == null)
                return true;

            return Mathf.Abs(config.sizeFixed - 1f) < 0.0001f
                && config.sizeFixedFake == 10
                && config.offsetFixed == 0
                && config.offsetXFixed == 0
                && string.Equals(config.layer, AltitudeLayer.BuildingOnTop.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 从反射字段读取浮点数。
        /// </summary>
        private static float GetFloat(FieldInfo field, object instance, float defaultValue)
        {
            object value = field?.GetValue(instance);
            return value is float number ? number : defaultValue;
        }

        /// <summary>
        /// 从反射字段读取整数。
        /// </summary>
        private static int GetInt(FieldInfo field, object instance, int defaultValue)
        {
            object value = field?.GetValue(instance);
            return value is int number ? number : defaultValue;
        }

        /// <summary>
        /// 从反射字段读取绘制层级名称。
        /// </summary>
        private static string GetLayerName(FieldInfo field, object instance)
        {
            object value = field?.GetValue(instance);
            return value == null ? AltitudeLayer.BuildingOnTop.ToString() : value.ToString();
        }

        /// <summary>
        /// 将蓝图保存的绘制层级名称解析为 RimWorld 绘制层级。
        /// </summary>
        private static AltitudeLayer ParseLayer(string layer)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(layer))
                    return (AltitudeLayer)Enum.Parse(typeof(AltitudeLayer), layer, true);
            }
            catch
            {
            }

            return AltitudeLayer.BuildingOnTop;
        }

    }
}
