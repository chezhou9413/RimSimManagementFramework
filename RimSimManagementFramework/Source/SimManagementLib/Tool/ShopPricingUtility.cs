using SimManagementLib.SimThingClass;
using SimManagementLib.SimThingComp;
using UnityEngine;
using Verse;
using RimWorld;
using SimManagementLib.SimZone;
using SimManagementLib.Api;

namespace SimManagementLib.Tool
{
    //统一顾客报价，使预算检查与确认购买采用相同的收银员加成。
    public static class ShopPricingUtility
    {
        //按货柜配置或实物市价报价，确认购买后由账单保存金额。
        public static float GetUnitPrice(Building_SimContainer storage, ThingDef thingDef)
        {
            if (thingDef == null) return 1f;

            float configuredPrice = GetConfiguredPrice(storage, thingDef);
            if (configuredPrice > 0f)
                return GetContainerPrice(storage, Mathf.Max(1f, configuredPrice));

            float storedMarketValue = GetStoredMarketValue(storage, thingDef);
            if (storedMarketValue > 0f)
                return GetContainerPrice(storage, Mathf.Max(1f, storedMarketValue));

            return GetContainerPrice(storage, Mathf.Max(1f, thingDef.BaseMarketValue));
        }

        //自动售货机独立收款，普通货柜使用本店收银员的交易能力。
        public static float GetContainerPrice(Building_SimContainer storage, float basePrice)
        {
            return storage?.Map == null || VendingMachineUtility.IsVendingMachine(storage) ? basePrice
                : ApplyCashierPrice(SimShopServiceApi.FindShop(storage.Map, storage.Position), basePrice);
        }

        //由当班收银员中最高的交易价格改善形成报价，结账时不再重复加成。
        public static float ApplyCashierPrice(Zone_Shop shop, float basePrice)
        {
            if (shop == null || basePrice <= 0f) return basePrice;
            float improvement = float.NegativeInfinity;
            foreach (var register in ShopDataUtility.GetCashRegisterSnapshotInZone(shop))
            {
                Pawn cashier = register.CurrentCashier;
                if (cashier != null) improvement = Mathf.Max(improvement, cashier.GetStatValue(StatDefOf.TradePriceImprovement));
            }
            return float.IsNegativeInfinity(improvement) ? basePrice : Mathf.Max(1f, basePrice * (1f + improvement));
        }

        //读取货柜的商品配置价。
        private static float GetConfiguredPrice(Building_SimContainer storage, ThingDef thingDef)
        {
            ThingComp_GoodsData comp = storage?.GetComp<ThingComp_GoodsData>();
            GoodsItemData data = comp?.FindItemData(thingDef);
            return data != null && data.price > 0f ? data.price : 0f;
        }

        //从货柜库存中读取首个同类商品的市价。
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
