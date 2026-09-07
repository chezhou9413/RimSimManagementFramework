using SimManagementLib.SimThingClass;
using SimManagementLib.SimThingComp;
using UnityEngine;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 统一计算商店商品的顾客购买价格，保证预算判断和实际购买使用同一套规则。
    /// </summary>
    public static class ShopPricingUtility
    {
        /// <summary>
        /// 返回指定货柜中指定商品的单件售价，优先使用货柜配置价，其次使用库存实例市价，最后回退到 Def 市价。
        /// </summary>
        public static float GetUnitPrice(Building_SimContainer storage, ThingDef thingDef)
        {
            if (thingDef == null) return 1f;

            float configuredPrice = GetConfiguredPrice(storage, thingDef);
            if (configuredPrice > 0f)
                return Mathf.Max(1f, configuredPrice);

            float storedMarketValue = GetStoredMarketValue(storage, thingDef);
            if (storedMarketValue > 0f)
                return Mathf.Max(1f, storedMarketValue);

            return Mathf.Max(1f, thingDef.BaseMarketValue);
        }

        /// <summary>
        /// 读取货柜对指定商品的启用配置价，未启用或未配置时返回零。
        /// </summary>
        private static float GetConfiguredPrice(Building_SimContainer storage, ThingDef thingDef)
        {
            ThingComp_GoodsData comp = storage?.GetComp<ThingComp_GoodsData>();
            GoodsItemData data = comp?.FindItemData(thingDef);
            return data != null && data.price > 0f ? data.price : 0f;
        }

        /// <summary>
        /// 从货柜虚拟库存中读取首个同类商品实例的市价，找不到库存时返回零。
        /// </summary>
        private static float GetStoredMarketValue(Building_SimContainer storage, ThingDef thingDef)
        {
            ThingOwner heldThings = storage?.GetDirectlyHeldThings();
            if (heldThings == null) return 0f;

            for (int i = 0; i < heldThings.Count; i++)
            {
                Thing thing = heldThings[i];
                if (thing != null && !thing.Destroyed && thing.def == thingDef)
                    return thing.MarketValue;
            }

            return 0f;
        }
    }
}
