using Verse;
namespace RimSimRestaurantExtension.Inventory
{
    //保存货柜商品上架规则，职责是区分库存补货开关与餐厅销售开关。
    public sealed class RestaurantStockRule : IExposable
    {
        public ThingDef item;
        public bool onSale;
        public RestaurantDeliveryMode mode;
        public int portions = 1;
        //保存每项商品的销售规则。
        public void ExposeData()
        {
            Scribe_Defs.Look(ref item, "item");
            Scribe_Values.Look(ref onSale, "onSale");
            Scribe_Values.Look(ref mode, "mode");
            Scribe_Values.Look(ref portions, "portions", 1);
        }
        //复制货柜销售草稿，职责是保持窗口编辑与运行状态独立。
        public RestaurantStockRule Clone() => new RestaurantStockRule { item = item, onSale = onSale, mode = mode, portions = portions };
    }
}
