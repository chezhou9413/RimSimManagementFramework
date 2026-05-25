using SimManagementLib.Pojo;
using SimManagementLib.SimService;
using SimManagementLib.SimThingComp;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SimManagementLib.Api
{
    /// <summary>
    /// 提供服务消费相关的稳定查询和创建入口，负责包装内部服务工具。
    /// </summary>
    public static class SimShopServiceApi
    {
        /// <summary>
        /// 返回地图中包含指定格子的商店区域。
        /// </summary>
        public static Zone_Shop FindShop(Map map, IntVec3 cell)
        {
            return ShopDataUtility.FindShopZone(map, cell);
        }

        /// <summary>
        /// 返回商店区域内所有服务建筑。
        /// </summary>
        public static IReadOnlyCollection<Thing> GetServiceProviders(Zone_Shop shop)
        {
            return ShopServiceUtility.GetServiceProvidersInZone(shop).ToList();
        }

        /// <summary>
        /// 返回建筑上的服务组件。
        /// </summary>
        public static ThingComp_ServiceProvider GetProviderComp(Thing provider)
        {
            return ShopServiceUtility.GetProviderComp(provider);
        }

        /// <summary>
        /// 返回服务建筑是否至少启用一项服务。
        /// </summary>
        public static bool HasEnabledService(Thing provider)
        {
            return ShopServiceUtility.HasEnabledService(provider);
        }

        /// <summary>
        /// 返回服务建筑指定服务的价格。
        /// </summary>
        public static float GetServicePrice(Thing provider, ShopServiceDef serviceDef)
        {
            return ShopServiceUtility.GetServicePrice(provider, serviceDef);
        }

        /// <summary>
        /// 判断服务建筑是否还有并发容量。
        /// </summary>
        public static bool CanAcceptMoreUsers(Thing provider, ShopServiceDef serviceDef)
        {
            return ShopServiceUtility.CanAcceptMoreUsers(provider, serviceDef);
        }

        /// <summary>
        /// 按 DefName 查找服务定义。
        /// </summary>
        public static ShopServiceDef GetServiceDef(string serviceDefName)
        {
            return string.IsNullOrEmpty(serviceDefName)
                ? null
                : DefDatabase<ShopServiceDef>.GetNamedSilentFail(serviceDefName);
        }

        /// <summary>
        /// 查找顾客可用的服务，支持按服务分类过滤。
        /// </summary>
        public static bool TryFindServiceForCustomer(
            Pawn pawn,
            Zone_Shop shop,
            float remainingBudget,
            IReadOnlyCollection<string> targetServiceCategoryIds,
            out Thing provider,
            out ShopServiceDef serviceDef,
            out float price)
        {
            return ShopServiceUtility.TryFindServiceForCustomer(pawn, shop, remainingBudget, targetServiceCategoryIds, out provider, out serviceDef, out price);
        }

        /// <summary>
        /// 创建普通服务订单数据但不加入任何顾客访问状态。
        /// </summary>
        public static CustomerServiceOrder CreateServiceOrder(int orderId, Thing provider, ShopServiceDef serviceDef, float price)
        {
            return ShopServiceUtility.CreateOrder(orderId, provider, serviceDef, price);
        }
    }
}
