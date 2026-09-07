using SimManagementLib.GameComp;
using Verse;

namespace SimManagementLib.Api
{
    /// <summary>
    /// 提供商店框架对外 API 的根入口，负责暴露版本和各子系统门面。
    /// </summary>
    public static class SimShopApi
    {
        public const int ApiMajor = 1;
        public const int ApiMinor = 0;
        public const string ApiVersion = "1.0";

        /// <summary>
        /// 返回当前游戏是否已经具备商店 API 运行环境。
        /// </summary>
        public static bool IsAvailable => Current.Game != null;

        /// <summary>
        /// 返回现做订单管理器，缺少游戏实例时返回 null。
        /// </summary>
        internal static GameComponent_PreparedShopOrderManager OrderManager => Current.Game?.GetComponent<GameComponent_PreparedShopOrderManager>();

        /// <summary>
        /// 返回顾客动作订单管理器，缺少游戏实例时返回 null。
        /// </summary>
        internal static GameComponent_CustomerActionOrderManager CustomerActionOrderManager => Current.Game?.GetComponent<GameComponent_CustomerActionOrderManager>();
    }
}
