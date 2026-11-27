using System.Collections.Generic;
using System.Linq;
using RimWorld;
using SimManagementLib.SimThingClass;
using SimManagementLib.Tool;
using Verse;

namespace SimManagementLib.SimThingComp
{
    //将现有仓储建筑的实际库存接入商店。
    public sealed class CompProperties_StorageSales : CompProperties
    {
        //每柜同时上架的总件数，-1 表示全部现货均可上架。
        public int maxListedCount = -1;

        //三个名单取并集，全部留空时允许宿主上的各类商品。
        public List<ThingDef> allowedThingDefs;
        public List<ThingCategoryDef> allowedThingCategories;
        public List<string> allowedGoodsCategoryIds;

        //按具体物品、原版分类树或经营框架的运行时商品分类匹配。
        internal bool AllowsListing(ThingDef def)
        {
            if (def == null) return false;
            if (allowedThingDefs.NullOrEmpty() && allowedThingCategories.NullOrEmpty() && allowedGoodsCategoryIds.NullOrEmpty()) return true;
            return allowedThingDefs?.Contains(def) == true
                || allowedThingCategories?.Any(def.IsWithinCategory) == true
                || allowedGoodsCategoryIds?.Any(id => GoodsCatalog.Contains(id, def)) == true;
        }

        //通过一个 XML 组件创建销售功能。
        public CompProperties_StorageSales() => compClass = typeof(ThingComp_StorageSales);

        //在加载定义时指出重复货柜或缺少公开仓储接口的建筑。
        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef)) yield return error;
            if (maxListedCount < -1) yield return "StorageSales 的 maxListedCount 应为 -1 或非负件数：" + parentDef.defName;
            var type = parentDef.thingClass;
            if (!typeof(Building).IsAssignableFrom(type) || typeof(Building_SimContainer).IsAssignableFrom(type))
                yield return "StorageSales 需要使用宿主储存逻辑的建筑：" + parentDef.defName;
            else if (!typeof(ISlotGroupParent).IsAssignableFrom(type) && !typeof(IThingHolder).IsAssignableFrom(type)
                && parentDef.comps.Count(p => typeof(IThingHolder).IsAssignableFrom(p.compClass)) != 1)
                yield return "StorageSales 需要储物格、建筑直接物品仓或唯一持货组件：" + parentDef.defName;
        }
    }
}
