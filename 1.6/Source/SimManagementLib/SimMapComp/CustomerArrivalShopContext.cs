using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using Verse;

namespace SimManagementLib.SimMapComp
{
    /// <summary>
    /// 保存单个商店的顾客刷新上下文，负责按容量和目标商品服务判断是否能生成顾客。
    /// </summary>
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
            return CustomerShoppingMatchUtility.ShopHasMatchingSellableGoodsOrServices(Shop, kind, kind?.sourceDef);
        }
    }
}
