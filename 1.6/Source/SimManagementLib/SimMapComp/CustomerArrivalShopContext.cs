using SimManagementLib.SimDef;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimThingComp;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
using Verse;

namespace SimManagementLib.SimMapComp
{
    internal sealed class CustomerArrivalShopContext
    {
        public Zone_Shop Shop;
        public int CurrentCustomers;
        public int Capacity;
        public float DemandFactor = 1f;

        public bool IsAtCapacity => CurrentCustomers >= Capacity;

        /// <summary>
        /// 判断指定顾客类型是否能被当前商店吸引并生成。
        /// </summary>
        public bool CanSpawn(Pojo.RuntimeCustomerKind kind)
        {
            return Shop != null
                && kind != null
                && !kind.pawnKindDefs.NullOrEmpty()
                && MatchesShopGoodsOrServices(kind)
                && !IsAtCapacity;
        }

        /// <summary>
        /// 判断商店内是否存在符合顾客目标分类的可售商品或可用服务。
        /// </summary>
        private bool MatchesShopGoodsOrServices(Pojo.RuntimeCustomerKind kind)
        {
            if (kind == null)
                return false;
            bool acceptsAnyGoodsCategory = kind.GetTargetGoodsCategoryIds().NullOrEmpty();
            List<string> serviceTargets = kind.GetTargetServiceCategoryIds();
            bool acceptsAnyServiceCategory = serviceTargets.NullOrEmpty();
            bool serviceOnlyCustomer = kind.GetTargetGoodsCategoryIds().NullOrEmpty() && !acceptsAnyServiceCategory;
            bool hasGoodsMatch = false;

            if (!serviceOnlyCustomer)
            {
                foreach (Building_SimContainer storage in ShopDataUtility.GetStoragesInZone(Shop))
                {
                    ThingComp_GoodsData comp = storage?.GetComp<ThingComp_GoodsData>();
                    if (comp == null || string.IsNullOrEmpty(comp.ActiveGoodsDefName))
                        continue;
                    if (!acceptsAnyGoodsCategory && kind.GetInterestMultiplier(comp.ActiveGoodsDefName) <= 0f)
                        continue;
                    if (HasSellableStock(storage, comp))
                    {
                        hasGoodsMatch = true;
                        break;
                    }
                }
            }

            if (hasGoodsMatch) return true;

            if (!acceptsAnyServiceCategory)
                return ShopServiceUtility.HasUsableServiceProvider(Shop, null, serviceTargets);

            return acceptsAnyGoodsCategory && ShopServiceUtility.HasUsableServiceProvider(Shop);
        }

        /// <summary>
        /// 判断货柜当前分类下是否至少有一个启用并有库存的商品。
        /// </summary>
        private static bool HasSellableStock(Building_SimContainer storage, ThingComp_GoodsData comp)
        {
            if (storage == null || comp == null) return false;

            foreach (ThingDef thingDef in storage.ActiveDefs)
            {
                GoodsItemData data = comp.FindItemData(thingDef);
                if (data != null && storage.CountStored(thingDef) > 0)
                    return true;
            }

            return false;
        }
    }
}
