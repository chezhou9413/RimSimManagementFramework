using SimManagementLib.GameComp;
using SimManagementLib.Pojo;
using SimManagementLib.SimMapComp;
using System.Collections.Generic;
using Verse;

namespace SimManagementLib.Tool
{
    //类职责：提供游戏级运行时顾客目录访问，并把目录变化传播到地图吸引力快照。
    public static class CustomerCatalog
    {
        public static GameComponent_CustomerCatalog Manager => Current.Game?.GetComponent<GameComponent_CustomerCatalog>();
        //确保运行时顾客目录已初始化。
        public static void EnsureInitialized() => Manager?.EnsureInitialized();
        //通知顾客目录变化，职责是同步失效所有地图的商店吸引力快照。
        public static void NotifyCatalogChanged()
        {
            Manager?.NotifyCatalogChanged();
            if (Find.Maps == null) return;
            for (int i = 0; i < Find.Maps.Count; i++)
                Find.Maps[i]?.GetComponent<CustomerArrivalManager>()?.NotifyCustomerCatalogDirty();
        }
        public static IReadOnlyCollection<RuntimeCustomerKind> Kinds => Manager?.Kinds;
        //按稳定类型编号返回运行时顾客定义。
        public static RuntimeCustomerKind GetKind(string kindId) => Manager?.GetKind(kindId);
    }
}
