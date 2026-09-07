using SimManagementLib.Pojo;
using SimManagementLib.SimMapComp;
using Verse;

namespace SimManagementLib.SimZone
{
    //保存商店运行态短缓存，负责降低工作扫描中反复校验区域设施和营业状态的成本。
    public partial class Zone_Shop
    {
        private const int RuntimeCacheTicks = 61;
        private int runtimeCacheExpireTick = -1;
        private bool cachedValidShop;
        private bool cachedOpenNow;
        private string cachedValidationMessage;
        //失效商店运行态缓存，负责在区划、日程或岗位配置变更后重新计算状态。
        public void InvalidateShopRuntimeCache()
        {
            runtimeCacheExpireTick = -1;
            cachedValidationMessage = null;
            InvalidateShopFacilityCache();
            Map?.GetComponent<CustomerArrivalManager>()?.NotifyShopDirty(this);
        }
        //返回缓存后的商店有效性，负责避免高频工作扫描重复遍历商店全部格子。
        private bool GetCachedValidShopNow(out string message)
        {
            RefreshRuntimeCacheIfNeeded();
            message = cachedValidationMessage;
            return cachedValidShop;
        }
        //返回缓存后的营业状态，负责把设施有效性和日程判断合并到一次短周期计算中。
        private bool GetCachedOpenNow()
        {
            RefreshRuntimeCacheIfNeeded();
            return cachedOpenNow;
        }
        //按短周期刷新商店运行状态，负责把昂贵的格子扫描限制到固定间隔内。
        private void RefreshRuntimeCacheIfNeeded()
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            if (now < runtimeCacheExpireTick && cachedValidationMessage != null)
                return;

            cachedValidShop = ComputeValidShopNow(out cachedValidationMessage);
            ShopScheduleData data = GetSchedule();
            cachedOpenNow = cachedValidShop && data.IsOpenNow(Map);
            runtimeCacheExpireTick = now + RuntimeCacheTicks;
        }
    }
}
