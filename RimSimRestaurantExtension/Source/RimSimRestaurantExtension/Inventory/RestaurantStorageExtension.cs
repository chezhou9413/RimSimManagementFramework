using System.Collections.Generic;
using Verse;
namespace RimSimRestaurantExtension.Inventory
{
    //配置餐厅货柜用途与商品范围，职责是让容量以外的建筑策略由 XML 决定。
    public sealed class RestaurantStorageExtension : DefModExtension
    {
        public bool refrigerated;
        public bool wallMounted;
        public List<RestaurantProductDef> products = new List<RestaurantProductDef>();
    }
}
