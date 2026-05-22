using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimThingComp;
using SimManagementLib.SimZone;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using Verse;

namespace SimManagementLib.SimMapComp
{
    /// <summary>
    /// 负责保存店铺蓝图放置后的待应用建筑配置，并在建筑完工后把货柜、招牌和经营参数写回建筑。
    /// </summary>
    public class MapComponent_ShopBlueprintPlacement : MapComponent
    {
        private static readonly DataContractJsonSerializer BuildingSerializer = new DataContractJsonSerializer(typeof(ShopBlueprintBuildingData));

        private List<ShopBlueprintPendingBuildingConfig> pendingBuildings = new List<ShopBlueprintPendingBuildingConfig>();

        /// <summary>
        /// 初始化地图蓝图放置组件。
        /// </summary>
        public MapComponent_ShopBlueprintPlacement(Map map) : base(map)
        {
        }

        /// <summary>
        /// 保存和读取还没有完成建造的蓝图建筑配置。
        /// </summary>
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref pendingBuildings, "pendingShopBlueprintBuildings", LookMode.Deep);
            if (pendingBuildings == null)
                pendingBuildings = new List<ShopBlueprintPendingBuildingConfig>();
        }

        /// <summary>
        /// 登记一个建筑施工计划完成后需要自动写入的经营配置。
        /// </summary>
        public void RegisterPendingBuilding(IntVec3 position, ThingDef def, Rot4 rotation, ShopBlueprintBuildingData data)
        {
            if (def == null || data == null || !HasAnyRuntimeConfig(data))
                return;

            pendingBuildings.RemoveAll(p => p == null || p.Matches(position, def, rotation));
            pendingBuildings.Add(new ShopBlueprintPendingBuildingConfig
            {
                x = position.x,
                z = position.z,
                defName = def.defName,
                rotation = rotation.ToStringWord(),
                dataJson = SerializeBuildingData(data)
            });
        }

        /// <summary>
        /// 尝试把待应用配置写入指定建筑，成功后移除对应记录。
        /// </summary>
        public bool TryApplyPendingConfig(Thing thing)
        {
            if (thing == null || thing.def == null || pendingBuildings.NullOrEmpty())
                return false;

            for (int i = pendingBuildings.Count - 1; i >= 0; i--)
            {
                ShopBlueprintPendingBuildingConfig pending = pendingBuildings[i];
                if (pending == null)
                {
                    pendingBuildings.RemoveAt(i);
                    continue;
                }

                if (!pending.Matches(thing.Position, thing.def, thing.Rotation))
                    continue;

                ShopBlueprintBuildingData data = pending.GetData();
                if (data == null)
                {
                    pendingBuildings.RemoveAt(i);
                    continue;
                }

                ApplyBlueprintConfigDirectly(thing, data);
                pendingBuildings.RemoveAt(i);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 直接把蓝图中的兼容运行时配置写入现有建筑，供复用已存在建筑时立即套用。
        /// </summary>
        public static void ApplyBlueprintConfigDirectly(Thing thing, ShopBlueprintBuildingData data)
        {
            if (thing == null || data == null)
                return;

            ApplyBuildingConfig(thing, data);
        }

        /// <summary>
        /// 判断建筑蓝图数据是否包含需要在完工后写回的运行时配置。
        /// </summary>
        private static bool HasAnyRuntimeConfig(ShopBlueprintBuildingData data)
        {
            return data.goods != null
                || data.sign != null
                || data.service != null
                || data.vending != null
                || data.cash != null
                || data.container != null
                || !string.IsNullOrEmpty(data.paintColorDefName);
        }

        /// <summary>
        /// 将蓝图中的经营配置应用到已经生成的建筑实例。
        /// </summary>
        private static void ApplyBuildingConfig(Thing thing, ShopBlueprintBuildingData data)
        {
            ThingWithComps thingWithComps = thing as ThingWithComps;
            ApplyGoodsConfig(thingWithComps?.GetComp<ThingComp_GoodsData>(), data.goods);
            ApplySignConfig(thingWithComps?.GetComp<ThingComp_CustomSign>(), data.sign);
            ApplyServiceConfig(thingWithComps?.GetComp<ThingComp_ServiceProvider>(), data.service);
            ApplyVendingConfig(thingWithComps?.GetComp<ThingComp_VendingMachine>(), data.vending);
            ApplyCashConfig(thingWithComps?.GetComp<ThingComp_CashStorage>(), data.cash);
            ApplyContainerConfig(thing as Building_SimContainer, data.container);
            ApplyPaintColor(thing as Building, data.paintColorDefName);
        }

        /// <summary>
        /// 把蓝图货柜商品设置写入货柜组件。
        /// </summary>
        private static void ApplyGoodsConfig(ThingComp_GoodsData comp, ShopBlueprintGoodsConfig config)
        {
            if (comp == null || config == null)
                return;

            Dictionary<string, GoodsItemData> items = new Dictionary<string, GoodsItemData>();
            if (config.items != null)
            {
                for (int i = 0; i < config.items.Count; i++)
                {
                    ShopBlueprintGoodsItemConfig item = config.items[i];
                    if (item == null || string.IsNullOrEmpty(item.thingDefName))
                        continue;

                    items[item.thingDefName] = new GoodsItemData
                    {
                        enabled = item.enabled,
                        count = Math.Max(0, item.count),
                        price = Math.Max(0f, item.price)
                    };
                }
            }

            comp.ApplySettings(config.activeGoodsDefName, items);
        }

        /// <summary>
        /// 把蓝图招牌三面图层写入招牌组件。
        /// </summary>
        private static void ApplySignConfig(ThingComp_CustomSign comp, ShopBlueprintSignConfig config)
        {
            if (comp == null || config == null)
                return;

            comp.SetFaces(ToFaceData(config.southLayers), ToFaceData(config.eastLayers), ToFaceData(config.northLayers));
        }

        /// <summary>
        /// 把蓝图服务槽位写入服务组件。
        /// </summary>
        private static void ApplyServiceConfig(ThingComp_ServiceProvider comp, ShopBlueprintServiceConfig config)
        {
            if (comp == null || config == null)
                return;

            comp.enabled = config.enabled;
            comp.serviceSlots = config.slots?
                .Where(slot => slot != null)
                .Select(slot => new ServiceSlotData
                {
                    serviceDefName = slot.serviceDefName ?? "",
                    enabled = slot.enabled,
                    priceOverride = Math.Max(0f, slot.priceOverride),
                    maxSimultaneousUsers = Math.Max(1, slot.maxSimultaneousUsers)
                })
                .ToList() ?? new List<ServiceSlotData>();
            comp.EnsureDefaultSlots();
        }

        /// <summary>
        /// 把蓝图自动售货机营业开关写入组件。
        /// </summary>
        private static void ApplyVendingConfig(ThingComp_VendingMachine comp, ShopBlueprintVendingConfig config)
        {
            if (comp == null || config == null)
                return;

            comp.enabled = config.enabled;
        }

        /// <summary>
        /// 把蓝图现金取现阈值写入现金组件。
        /// </summary>
        private static void ApplyCashConfig(ThingComp_CashStorage comp, ShopBlueprintCashConfig config)
        {
            if (comp == null || config == null)
                return;

            comp.SetWithdrawThreshold(config.withdrawThreshold);
        }

        /// <summary>
        /// 把蓝图货柜自定义名称写入货柜建筑。
        /// </summary>
        private static void ApplyContainerConfig(Building_SimContainer container, ShopBlueprintContainerConfig config)
        {
            if (container == null || config == null)
                return;

            container.RenamableLabel = config.customName ?? "";
        }

        /// <summary>
        /// 把蓝图建筑涂色写入支持涂色的建筑。
        /// </summary>
        private static void ApplyPaintColor(Building building, string colorDefName)
        {
            if (building == null || string.IsNullOrEmpty(colorDefName))
                return;

            ColorDef colorDef = DefDatabase<ColorDef>.GetNamedSilentFail(colorDefName);
            if (colorDef != null)
                building.SetColor(colorDef.color, false);
        }

        /// <summary>
        /// 将蓝图招牌图层列表转换为组件可使用的面数据。
        /// </summary>
        private static SignFaceData ToFaceData(List<ShopBlueprintSignLayerConfig> source)
        {
            SignFaceData face = new SignFaceData();
            if (source == null)
                return face;

            for (int i = 0; i < source.Count; i++)
            {
                ShopBlueprintSignLayerConfig layer = source[i];
                if (layer == null)
                    continue;

                face.layers.Add(new SignImageLayerData
                {
                    imageId = layer.imageId ?? "",
                    label = layer.label ?? "",
                    enabled = layer.enabled,
                    x = layer.x,
                    y = layer.y,
                    scaleX = layer.scaleX,
                    scaleY = layer.scaleY,
                    angle = layer.angle,
                    drawOrder = layer.drawOrder
                });
            }

            return face;
        }

        /// <summary>
        /// 将建筑蓝图配置序列化为存档可保存的 JSON 字符串。
        /// </summary>
        private static string SerializeBuildingData(ShopBlueprintBuildingData data)
        {
            if (data == null)
                return "";

            try
            {
                using (MemoryStream stream = new MemoryStream())
                {
                    BuildingSerializer.WriteObject(stream, data);
                    return Convert.ToBase64String(stream.ToArray());
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[SimManagementLib] 店铺蓝图建筑配置序列化失败：" + ex);
                return "";
            }
        }

        /// <summary>
        /// 从存档字符串还原建筑蓝图配置。
        /// </summary>
        internal static ShopBlueprintBuildingData DeserializeBuildingData(string dataJson)
        {
            if (string.IsNullOrEmpty(dataJson))
                return null;

            try
            {
                byte[] bytes = Convert.FromBase64String(dataJson);
                using (MemoryStream stream = new MemoryStream(bytes))
                {
                    return BuildingSerializer.ReadObject(stream) as ShopBlueprintBuildingData;
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[SimManagementLib] 店铺蓝图建筑配置反序列化失败：" + ex);
                return null;
            }
        }
    }

    /// <summary>
    /// 负责保存一个建筑施工计划完工后需要套用的蓝图配置。
    /// </summary>
    public class ShopBlueprintPendingBuildingConfig : IExposable
    {
        public int x;
        public int z;
        public string defName = "";
        public string rotation = "North";
        public string dataJson = "";

        /// <summary>
        /// 保存和读取待应用建筑配置。
        /// </summary>
        public void ExposeData()
        {
            Scribe_Values.Look(ref x, "x");
            Scribe_Values.Look(ref z, "z");
            Scribe_Values.Look(ref defName, "defName", "");
            Scribe_Values.Look(ref rotation, "rotation", "North");
            Scribe_Values.Look(ref dataJson, "dataJson", "");
        }

        /// <summary>
        /// 判断指定建筑是否对应这条待应用配置。
        /// </summary>
        public bool Matches(IntVec3 position, ThingDef def, Rot4 rot)
        {
            return def != null
                && position.x == x
                && position.z == z
                && string.Equals(def.defName, defName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(rot.ToStringWord(), rotation, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 返回这条待应用记录中的建筑蓝图配置。
        /// </summary>
        public ShopBlueprintBuildingData GetData()
        {
            return MapComponent_ShopBlueprintPlacement.DeserializeBuildingData(dataJson);
        }
    }
}
