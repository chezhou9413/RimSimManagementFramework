using System.Collections.Generic;
using Verse;

namespace SimManagementLib.SimThingComp
{
    //单件商品货柜组件参数，职责是声明槽位容量和允许的物品分类。
    public sealed class ThingCompProperties_UniqueGoodsContainer : CompProperties
    {
        public int slotCount = 90;
        public bool requireStuffOrQuality = true;
        public List<ThingCategoryDef> allowedThingCategories = new List<ThingCategoryDef>();

        //初始化组件类型。
        public ThingCompProperties_UniqueGoodsContainer()
        {
            compClass = typeof(ThingComp_UniqueGoodsContainer);
        }
    }

    //单件商品货柜组件，职责是向建筑提供 XML 配置。
    public sealed class ThingComp_UniqueGoodsContainer : ThingComp
    {
        public ThingCompProperties_UniqueGoodsContainer UniqueProps => props as ThingCompProperties_UniqueGoodsContainer;
        public int SlotCount => System.Math.Max(1, UniqueProps?.slotCount ?? 90);
    }
}
