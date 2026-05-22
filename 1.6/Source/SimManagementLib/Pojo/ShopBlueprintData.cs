using System.Collections.Generic;
using System.Runtime.Serialization;

namespace SimManagementLib.Pojo
{
    /// <summary>
    /// 负责保存本地店铺蓝图的完整结构、配置和预览元数据。
    /// </summary>
    [DataContract]
    public sealed class ShopBlueprintData
    {
        [DataMember] public int version = 1;
        [DataMember] public string blueprintId = "";
        [DataMember] public string label = "";
        [DataMember] public string description = "";
        [DataMember] public string sourceMapName = "";
        [DataMember] public int sourceZoneId = -1;
        [DataMember] public long createdAtTicks;
        [DataMember] public int width;
        [DataMember] public int height;
        [DataMember] public int minX;
        [DataMember] public int minZ;
        [DataMember] public List<ShopBlueprintCellData> zoneCells = new List<ShopBlueprintCellData>();
        [DataMember] public List<ShopBlueprintTerrainData> terrains = new List<ShopBlueprintTerrainData>();
        [DataMember] public List<ShopBlueprintBuildingData> buildings = new List<ShopBlueprintBuildingData>();
        [DataMember] public ShopBlueprintScheduleData schedule = new ShopBlueprintScheduleData();
        [DataMember] public List<ShopBlueprintRequiredModData> requiredMods = new List<ShopBlueprintRequiredModData>();
        [DataMember] public string remoteBlueprintCode = "";
        [DataMember] public string remoteAuthorSteamId = "";
        [DataMember] public long remoteImportedAtTicks;
    }

    /// <summary>
    /// 负责保存一个相对坐标格子。
    /// </summary>
    [DataContract]
    public sealed class ShopBlueprintCellData
    {
        [DataMember] public int x;
        [DataMember] public int z;
    }

    /// <summary>
    /// 负责保存蓝图范围内单格地形、地基、涂色和屋顶。
    /// </summary>
    [DataContract]
    public sealed class ShopBlueprintTerrainData
    {
        [DataMember] public int x;
        [DataMember] public int z;
        [DataMember] public string terrainDefName = "";
        [DataMember] public string foundationDefName = "";
        [DataMember] public string colorDefName = "";
        [DataMember] public string roofDefName = "";
    }

    /// <summary>
    /// 负责保存蓝图中的单个建筑及其经营组件配置。
    /// </summary>
    [DataContract]
    public sealed class ShopBlueprintBuildingData
    {
        [DataMember] public string localId = "";
        [DataMember] public string defName = "";
        [DataMember] public string label = "";
        [DataMember] public string stuffDefName = "";
        [DataMember] public string styleDefName = "";
        [DataMember] public string paintColorDefName = "";
        [DataMember] public string rotation = "South";
        [DataMember] public int x;
        [DataMember] public int z;
        [DataMember] public int width = 1;
        [DataMember] public int height = 1;
        [DataMember] public ShopBlueprintGoodsConfig goods;
        [DataMember] public ShopBlueprintSignConfig sign;
        [DataMember] public ShopBlueprintServiceConfig service;
        [DataMember] public ShopBlueprintVendingConfig vending;
        [DataMember] public ShopBlueprintCashConfig cash;
        [DataMember] public ShopBlueprintContainerConfig container;
    }

    /// <summary>
    /// 负责保存商店营业日程配置，避免直接序列化 IExposable 数据。
    /// </summary>
    [DataContract]
    public sealed class ShopBlueprintScheduleData
    {
        [DataMember] public bool manualOpen = true;
        [DataMember] public bool useSchedule;
        [DataMember] public List<bool> openHours = new List<bool>();
    }

    /// <summary>
    /// 负责保存货柜销售分类和单品目标配置。
    /// </summary>
    [DataContract]
    public sealed class ShopBlueprintGoodsConfig
    {
        [DataMember] public string activeGoodsDefName = "";
        [DataMember] public List<ShopBlueprintGoodsItemConfig> items = new List<ShopBlueprintGoodsItemConfig>();
    }

    /// <summary>
    /// 负责保存货柜中单个商品的启用、目标数量和售价。
    /// </summary>
    [DataContract]
    public sealed class ShopBlueprintGoodsItemConfig
    {
        [DataMember] public string thingDefName = "";
        [DataMember] public bool enabled;
        [DataMember] public int count;
        [DataMember] public float price;
    }

    /// <summary>
    /// 负责保存自定义招牌三面图层配置。
    /// </summary>
    [DataContract]
    public sealed class ShopBlueprintSignConfig
    {
        [DataMember] public List<ShopBlueprintSignLayerConfig> southLayers = new List<ShopBlueprintSignLayerConfig>();
        [DataMember] public List<ShopBlueprintSignLayerConfig> eastLayers = new List<ShopBlueprintSignLayerConfig>();
        [DataMember] public List<ShopBlueprintSignLayerConfig> northLayers = new List<ShopBlueprintSignLayerConfig>();
    }

    /// <summary>
    /// 负责保存招牌单个图片图层的引用和变换参数。
    /// </summary>
    [DataContract]
    public sealed class ShopBlueprintSignLayerConfig
    {
        [DataMember] public string imageId = "";
        [DataMember] public string label = "";
        [DataMember] public bool enabled = true;
        [DataMember] public float x;
        [DataMember] public float y;
        [DataMember] public float scaleX = 1f;
        [DataMember] public float scaleY = 1f;
        [DataMember] public float angle;
        [DataMember] public int drawOrder;
    }

    /// <summary>
    /// 负责保存服务建筑开关和服务槽位配置。
    /// </summary>
    [DataContract]
    public sealed class ShopBlueprintServiceConfig
    {
        [DataMember] public bool enabled = true;
        [DataMember] public List<ShopBlueprintServiceSlotConfig> slots = new List<ShopBlueprintServiceSlotConfig>();
    }

    /// <summary>
    /// 负责保存服务建筑中单项服务的价格和并发配置。
    /// </summary>
    [DataContract]
    public sealed class ShopBlueprintServiceSlotConfig
    {
        [DataMember] public string serviceDefName = "";
        [DataMember] public bool enabled = true;
        [DataMember] public float priceOverride;
        [DataMember] public int maxSimultaneousUsers = 1;
    }

    /// <summary>
    /// 负责保存自动售货机营业开关。
    /// </summary>
    [DataContract]
    public sealed class ShopBlueprintVendingConfig
    {
        [DataMember] public bool enabled = true;
    }

    /// <summary>
    /// 负责保存现金建筑的自动取现阈值，不保存实际白银。
    /// </summary>
    [DataContract]
    public sealed class ShopBlueprintCashConfig
    {
        [DataMember] public int withdrawThreshold;
    }

    /// <summary>
    /// 负责保存货柜自定义名称，不保存真实库存。
    /// </summary>
    [DataContract]
    public sealed class ShopBlueprintContainerConfig
    {
        [DataMember] public string customName = "";
    }

    /// <summary>
    /// 负责保存蓝图依赖模组信息，供网络共享和本地兼容校验使用。
    /// </summary>
    [DataContract]
    public sealed class ShopBlueprintRequiredModData
    {
        [DataMember] public string packageId = "";
        [DataMember] public string displayName = "";
        [DataMember] public string steamWorkshopUrl = "";
        [DataMember] public uint steamAppId;
        [DataMember] public bool isOfficial;
    }
}
