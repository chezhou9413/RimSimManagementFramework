using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RimSimRestaurantExtension.Inventory
{
    //定义餐厅现成商品的交付方式。
    public enum RestaurantDeliveryMode { Consume, TakeAway }

    //定义商品范围和默认售价，职责是让其他模组通过 XML 接入餐厅柜中现货。
    public sealed class RestaurantProductDef : Def
    {
        public List<ThingDef> things = new List<ThingDef>();
        public List<ThingCategoryDef> categories = new List<ThingCategoryDef>();
        public List<ThingDef> excludedThings = new List<ThingDef>();
        public float defaultPrice = -1f;
        public int defaultCount = 1;
        public float selectionWeight = 1f;
        public RestaurantDeliveryMode deliveryMode;
        public bool ingredientsOnly;

        //判断物品是否属于配置范围，职责是使排除项优先于分类和显式列表。
        public bool Allows(ThingDef item)
        {
            return item != null && !excludedThings.Contains(item)
                && (things.Contains(item) || categories.Any(c => item.IsWithinCategory(c)));
        }

        //验证商品可交付性，职责是指出具体配置和不支持的物品。
        public override IEnumerable<string> ConfigErrors()
        {
            foreach (var error in base.ConfigErrors()) yield return error;
            if (defaultCount < 1 || (defaultPrice <= 0 && defaultPrice != -1) || float.IsNaN(defaultPrice)
                || float.IsInfinity(defaultPrice) || selectionWeight <= 0 || float.IsNaN(selectionWeight) || float.IsInfinity(selectionWeight)
                || !System.Enum.IsDefined(typeof(RestaurantDeliveryMode), deliveryMode))
                yield return defName + "：默认数量、售价或选择权重无效";
            foreach (var item in DefDatabase<ThingDef>.AllDefsListForReading.Where(Allows))
            {
                if (item.category != ThingCategory.Item || !item.EverHaulable)
                    yield return defName + "：" + item.defName + " 不是可搬运普通物品";
                else if (!ingredientsOnly && deliveryMode == RestaurantDeliveryMode.Consume && item.ingestible == null)
                    yield return defName + "：" + item.defName + " 不能执行原版进食";
            }
        }
    }
}
