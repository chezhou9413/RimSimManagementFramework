using SimManagementLib.SimDef;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimThingComp;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
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

        public bool CanSpawn(Pojo.RuntimeCustomerKind kind)
        {
            return Shop != null
                && kind != null
                && !kind.pawnKindDefs.NullOrEmpty()
                && MatchesShopGoods(kind)
                && !IsAtCapacity;
        }

        private bool MatchesShopGoods(Pojo.RuntimeCustomerKind kind)
        {
            if (kind == null)
                return false;
            if (kind.GetTargetGoodsCategoryIds().NullOrEmpty())
                return true;

            foreach (Building_SimContainer storage in ShopDataUtility.GetStoragesInZone(Shop))
            {
                ThingComp_GoodsData comp = storage?.GetComp<ThingComp_GoodsData>();
                if (comp != null && kind.GetInterestMultiplier(comp.ActiveGoodsDefName) > 0f)
                    return true;
            }

            return false;
        }
    }
}
