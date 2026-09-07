using SimManagementLib.Pojo;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 负责定义蓝图外部运行时配置的软依赖桥接契约。
    /// </summary>
    public interface IShopBlueprintExternalConfigBridge
    {
        /// <summary>
        /// 返回桥接器的稳定标识，用于从蓝图 payload 路由到具体兼容逻辑。
        /// </summary>
        string BridgeId { get; }

        /// <summary>
        /// 返回提供该运行时配置的模组 PackageId。
        /// </summary>
        string PackageId { get; }

        /// <summary>
        /// 返回当前存档是否已加载对应软依赖模组和类型。
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>
        /// 返回上传和下载校验需要声明的软依赖模组信息。
        /// </summary>
        ShopBlueprintRequiredModData RequiredMod { get; }

        /// <summary>
        /// 从真实建筑读取当前桥接器负责的运行时配置。
        /// </summary>
        ShopBlueprintExternalConfigData Capture(Thing thing);

        /// <summary>
        /// 为一次蓝图放置创建配置副本，并按蓝图整体旋转调整坐标类数据。
        /// </summary>
        ShopBlueprintExternalConfigData PrepareForPlacement(ShopBlueprintExternalConfigData config, Rot4 blueprintRot);

        /// <summary>
        /// 补齐外部配置的基础字段，避免旧数据缺字段导致应用失败。
        /// </summary>
        void EnsureDefaults(ShopBlueprintExternalConfigData config);

        /// <summary>
        /// 把蓝图外部配置写入目标建筑实例。
        /// </summary>
        bool Apply(Thing thing, ShopBlueprintExternalConfigData config);
    }
}
