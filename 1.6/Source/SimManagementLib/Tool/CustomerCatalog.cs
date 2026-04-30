using SimManagementLib.GameComp;
using SimManagementLib.Pojo;
using System.Collections.Generic;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 提供对游戏级运行时顾客目录的静态访问入口。
    /// </summary>
    public static class CustomerCatalog
    {
        public static GameComponent_CustomerCatalog Manager => Current.Game?.GetComponent<GameComponent_CustomerCatalog>();
        public static void EnsureInitialized() => Manager?.EnsureInitialized();
        public static void NotifyCatalogChanged() => Manager?.NotifyCatalogChanged();
        public static IReadOnlyCollection<RuntimeCustomerKind> Kinds => Manager?.Kinds;
        public static RuntimeCustomerKind GetKind(string kindId) => Manager?.GetKind(kindId);
    }
}
