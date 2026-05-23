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
    /// 负责通过反射桥接通用建筑贴图调整，让店铺蓝图在不引用其 DLL 的情况下保存和恢复贴图偏移。
    /// </summary>
    public sealed class ShopBlueprintTextureAdjustmentBridge : IShopBlueprintExternalConfigBridge
    {
        public const string BridgeIdValue = "GeneralBuildingTextureAdjustment";
        public const string PackageId = "General.Building.Texture.Adjustment";
        private const string DisplayName = "General Building Texture Adjustment";
        private const string WorkshopUrl = "https://steamcommunity.com/sharedfiles/filedetails/?id=3666687522";
        private const uint SteamAppId = 3666687522u;

        private static Type mapCompType;
        private static Type adjustmentDataType;
        private static MethodInfo getDataMethod;
        private static MethodInfo setDataMethod;
        private static FieldInfo offsetField;
        private static FieldInfo scaleField;
        private static FieldInfo altitudeField;
        private static FieldInfo mirrorField;
        private static FieldInfo rotationField;
        private static FieldInfo transparencyField;
        private static bool reflectionResolved;

        /// <summary>
        /// 返回通用建筑贴图调整桥接器的稳定标识。
        /// </summary>
        public string BridgeId => BridgeIdValue;

        /// <summary>
        /// 返回通用建筑贴图调整模组的 PackageId。
        /// </summary>
        string IShopBlueprintExternalConfigBridge.PackageId => PackageId;

        /// <summary>
        /// 返回当前存档是否可以访问通用建筑贴图调整的反射入口。
        /// </summary>
        public bool IsAvailable => EnsureReflection();

        /// <summary>
        /// 返回蓝图依赖校验需要声明的通用建筑贴图调整模组信息。
        /// </summary>
        public ShopBlueprintRequiredModData RequiredMod => BuildRequiredMod();

        /// <summary>
        /// 从建筑读取通用建筑贴图调整配置，并打包为通用外部配置。
        /// </summary>
        public ShopBlueprintExternalConfigData Capture(Thing thing)
        {
            return ToExternalConfig(CaptureConfig(thing));
        }

        /// <summary>
        /// 按蓝图整体旋转调整通用建筑贴图调整配置，并返回通用外部配置副本。
        /// </summary>
        public ShopBlueprintExternalConfigData PrepareForPlacement(ShopBlueprintExternalConfigData config, Rot4 blueprintRot)
        {
            ShopBlueprintTextureAdjustmentConfig typedConfig = FromExternalConfig(config);
            return ToExternalConfig(PrepareForPlacement(typedConfig, blueprintRot));
        }

        /// <summary>
        /// 补齐通用外部配置的基础字段。
        /// </summary>
        public void EnsureDefaults(ShopBlueprintExternalConfigData config)
        {
            if (config == null)
                return;

            config.bridgeId = BridgeIdValue;
            config.packageId = PackageId;
            config.version = string.IsNullOrWhiteSpace(config.version) ? "1" : config.version;
            config.payload = config.payload ?? "";
        }

        /// <summary>
        /// 将通用外部配置应用到指定建筑。
        /// </summary>
        public bool Apply(Thing thing, ShopBlueprintExternalConfigData config)
        {
            return ApplyConfig(thing, FromExternalConfig(config));
        }

        /// <summary>
        /// 将旧版贴图调整字段转换为通用外部配置。
        /// </summary>
        public static ShopBlueprintExternalConfigData ToExternalConfig(ShopBlueprintTextureAdjustmentConfig config)
        {
            EnsureConfigDefaults(config);
            if (config == null || IsDefaultConfig(config))
                return null;

            return new ShopBlueprintExternalConfigData
            {
                bridgeId = BridgeIdValue,
                packageId = PackageId,
                version = "1",
                payload = BlueprintExternalConfigPayload.Serialize(config)
            };
        }

        /// <summary>
        /// 将通用外部配置还原为旧版贴图调整配置对象。
        /// </summary>
        public static ShopBlueprintTextureAdjustmentConfig FromExternalConfig(ShopBlueprintExternalConfigData config)
        {
            ShopBlueprintTextureAdjustmentConfig result = BlueprintExternalConfigPayload.Deserialize<ShopBlueprintTextureAdjustmentConfig>(config?.payload);
            EnsureConfigDefaults(result);
            return result;
        }

        /// <summary>
        /// 从建筑当前地图上的通用贴图调整组件读取配置，并转换成蓝图可序列化数据。
        /// </summary>
        public static ShopBlueprintTextureAdjustmentConfig CaptureConfig(Thing thing)
        {
            if (thing?.Map == null || !EnsureReflection())
                return null;

            try
            {
                MapComponent comp = thing.Map.GetComponent(mapCompType);
                object adjustmentData = getDataMethod?.Invoke(comp, new object[] { thing.thingIDNumber });
                if (adjustmentData == null)
                    return null;

                Vector3 offset = (Vector3)offsetField.GetValue(adjustmentData);
                ShopBlueprintTextureAdjustmentConfig config = new ShopBlueprintTextureAdjustmentConfig
                {
                    offsetX = offset.x,
                    offsetZ = offset.y,
                    offsetVertical = offset.z,
                    scale = GetFloat(scaleField, adjustmentData, 1f),
                    altitude = GetFloat(altitudeField, adjustmentData, 0f),
                    mirror = GetBool(mirrorField, adjustmentData),
                    rotation = GetFloat(rotationField, adjustmentData, 0f),
                    transparency = GetFloat(transparencyField, adjustmentData, 1f)
                };

                return IsDefaultConfig(config) ? null : config;
            }
            catch (Exception ex)
            {
                Log.Warning("[SimManagementLib] 读取通用贴图调整蓝图配置失败：" + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// 把蓝图中的贴图调整配置写入目标建筑当前地图上的通用贴图调整组件。
        /// </summary>
        public static bool ApplyConfig(Thing thing, ShopBlueprintTextureAdjustmentConfig config)
        {
            if (thing?.Map == null || config == null || !EnsureReflection())
                return false;

            try
            {
                MapComponent comp = thing.Map.GetComponent(mapCompType);
                if (comp == null)
                    return false;

                object adjustmentData = Activator.CreateInstance(adjustmentDataType);
                offsetField.SetValue(adjustmentData, new Vector3(config.offsetX, config.offsetZ, config.offsetVertical));
                scaleField.SetValue(adjustmentData, config.scale);
                altitudeField.SetValue(adjustmentData, config.altitude);
                mirrorField.SetValue(adjustmentData, config.mirror);
                rotationField.SetValue(adjustmentData, config.rotation);
                transparencyField.SetValue(adjustmentData, config.transparency);
                setDataMethod.Invoke(comp, new object[] { thing.thingIDNumber, adjustmentData });
                if (thing.Spawned)
                    thing.Map.mapDrawer.MapMeshDirty(thing.Position, MapMeshFlagDefOf.Things);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning("[SimManagementLib] 应用通用贴图调整蓝图配置失败：" + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 根据蓝图整体旋转返回用于本次放置的贴图调整配置副本。
        /// </summary>
        public static ShopBlueprintTextureAdjustmentConfig PrepareForPlacement(ShopBlueprintTextureAdjustmentConfig source, Rot4 blueprintRot)
        {
            if (source == null)
                return null;

            ShopBlueprintTextureAdjustmentConfig result = CloneConfig(source);
            if (blueprintRot == Rot4.North)
                return result;

            Vector2 rotated = RotateOffset(source.offsetX, source.offsetZ, blueprintRot);
            result.offsetX = rotated.x;
            result.offsetZ = rotated.y;
            result.rotation = NormalizeRotation(source.rotation + blueprintRot.AsInt * 90f);
            return result;
        }

        /// <summary>
        /// 补齐贴图调整配置中的默认值，避免旧蓝图缺少缩放或透明度时出现异常表现。
        /// </summary>
        public static void EnsureConfigDefaults(ShopBlueprintTextureAdjustmentConfig config)
        {
            if (config == null)
                return;

            if (Mathf.Approximately(config.scale, 0f))
                config.scale = 1f;
            if (Mathf.Approximately(config.transparency, 0f))
                config.transparency = 1f;
            config.rotation = NormalizeRotation(config.rotation);
        }

        /// <summary>
        /// 返回蓝图上传和下载校验使用的通用贴图调整模组依赖信息。
        /// </summary>
        public static ShopBlueprintRequiredModData BuildRequiredMod()
        {
            ModMetaData meta = ModLister.GetActiveModWithIdentifier(PackageId, true);
            return new ShopBlueprintRequiredModData
            {
                packageId = PackageId,
                displayName = meta?.Name ?? DisplayName,
                steamWorkshopUrl = WorkshopUrl,
                steamAppId = SteamAppId,
                isOfficial = false
            };
        }

        /// <summary>
        /// 判断蓝图贴图调整配置是否等同于默认值。
        /// </summary>
        private static bool IsDefaultConfig(ShopBlueprintTextureAdjustmentConfig config)
        {
            if (config == null)
                return true;

            return Mathf.Abs(config.offsetX) < 0.0001f
                && Mathf.Abs(config.offsetZ) < 0.0001f
                && Mathf.Abs(config.offsetVertical) < 0.0001f
                && Mathf.Abs(config.altitude) < 0.0001f
                && Mathf.Abs(config.rotation) < 0.0001f
                && Mathf.Abs(config.scale - 1f) < 0.0001f
                && Mathf.Abs(config.transparency - 1f) < 0.0001f
                && !config.mirror;
        }

        /// <summary>
        /// 解析通用贴图调整的类型、方法和字段，负责确保软依赖缺失时安全跳过。
        /// </summary>
        private static bool EnsureReflection()
        {
            if (reflectionResolved)
                return mapCompType != null
                    && adjustmentDataType != null
                    && getDataMethod != null
                    && setDataMethod != null
                    && offsetField != null
                    && scaleField != null
                    && altitudeField != null
                    && mirrorField != null
                    && rotationField != null
                    && transparencyField != null;

            reflectionResolved = true;
            if (ModLister.GetActiveModWithIdentifier(PackageId, true) == null)
                return false;

            mapCompType = AccessTools.TypeByName("General_Building_Texture_Adjustment.Core.Data.TextureAdjustmentMapComp");
            adjustmentDataType = AccessTools.TypeByName("General_Building_Texture_Adjustment.Core.Data.AdjustmentData");
            if (mapCompType == null || adjustmentDataType == null)
                return false;

            getDataMethod = AccessTools.Method(mapCompType, "GetData", new[] { typeof(int) });
            setDataMethod = AccessTools.Method(mapCompType, "SetData", new[] { typeof(int), adjustmentDataType });
            offsetField = AccessTools.Field(adjustmentDataType, "offset");
            scaleField = AccessTools.Field(adjustmentDataType, "scale");
            altitudeField = AccessTools.Field(adjustmentDataType, "altitude");
            mirrorField = AccessTools.Field(adjustmentDataType, "mirror");
            rotationField = AccessTools.Field(adjustmentDataType, "rotation");
            transparencyField = AccessTools.Field(adjustmentDataType, "transparency");

            return mapCompType != null
                && adjustmentDataType != null
                && getDataMethod != null
                && setDataMethod != null
                && offsetField != null
                && scaleField != null
                && altitudeField != null
                && mirrorField != null
                && rotationField != null
                && transparencyField != null;
        }

        /// <summary>
        /// 读取反射字段中的浮点数，字段缺失时返回指定默认值。
        /// </summary>
        private static float GetFloat(FieldInfo field, object instance, float defaultValue)
        {
            object value = field?.GetValue(instance);
            return value is float number ? number : defaultValue;
        }

        /// <summary>
        /// 读取反射字段中的布尔值，字段缺失时返回 false。
        /// </summary>
        private static bool GetBool(FieldInfo field, object instance)
        {
            object value = field?.GetValue(instance);
            return value is bool flag && flag;
        }

        /// <summary>
        /// 克隆贴图调整配置，避免放置旋转时修改蓝图原始数据。
        /// </summary>
        private static ShopBlueprintTextureAdjustmentConfig CloneConfig(ShopBlueprintTextureAdjustmentConfig source)
        {
            return new ShopBlueprintTextureAdjustmentConfig
            {
                offsetX = source.offsetX,
                offsetZ = source.offsetZ,
                offsetVertical = source.offsetVertical,
                scale = source.scale,
                altitude = source.altitude,
                mirror = source.mirror,
                rotation = source.rotation,
                transparency = source.transparency
            };
        }

        /// <summary>
        /// 按蓝图整体旋转同步旋转贴图偏移向量。
        /// </summary>
        private static Vector2 RotateOffset(float x, float z, Rot4 blueprintRot)
        {
            switch (blueprintRot.AsInt)
            {
                case Rot4.EastInt:
                    return new Vector2(-z, x);
                case Rot4.SouthInt:
                    return new Vector2(-x, -z);
                case Rot4.WestInt:
                    return new Vector2(z, -x);
                default:
                    return new Vector2(x, z);
            }
        }

        /// <summary>
        /// 将角度归一到 0 到 360 度范围内。
        /// </summary>
        private static float NormalizeRotation(float value)
        {
            value %= 360f;
            if (value < 0f)
                value += 360f;
            return value;
        }
    }
}
