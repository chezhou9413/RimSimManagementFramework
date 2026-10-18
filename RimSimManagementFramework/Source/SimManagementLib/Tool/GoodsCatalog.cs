using SimManagementLib.GameComp;
using SimManagementLib.Pojo;
using SimManagementLib.SimDef;
using SimManagementLib.SimMapComp;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SimManagementLib.Tool
{
    //类职责：提供运行时商品分类查询、注册和目录变化通知入口。
    public static class GoodsCatalog
    {
        private static IReadOnlyList<RuntimeGoodsItem> EmptyItems { get; } = new List<RuntimeGoodsItem>();

        public static GameComponent_GoodsCatalog Manager => Current.Game?.GetComponent<GameComponent_GoodsCatalog>();
        public static IReadOnlyCollection<RuntimeGoodsCategory> Categories => Manager?.Categories ?? BuildPreviewCategories();

        //确保运行时商品目录已初始化。
        public static void EnsureInitialized()
        {
            Manager?.EnsureInitialized();
        }

        //按分类编号返回运行时商品分类。
        public static RuntimeGoodsCategory GetCategory(string categoryId)
        {
            return Manager?.GetCategory(categoryId);
        }

        //按分类编号返回来源 Def。
        public static GoodsDef GetSourceDef(string categoryId)
        {
            return GetCategory(categoryId)?.sourceDef;
        }

        //返回分类中的运行时商品列表。
        public static IReadOnlyList<RuntimeGoodsItem> GetItems(string categoryId)
        {
            return Manager?.GetItemsForCategory(categoryId) ?? EmptyItems;
        }

        //按 ThingDef 返回运行时商品项。
        public static RuntimeGoodsItem GetItem(ThingDef thingDef)
        {
            return Manager?.GetItem(thingDef);
        }

        //判断分类是否包含指定 ThingDef。
        public static bool Contains(string categoryId, ThingDef thingDef)
        {
            return Manager != null && Manager.CategoryContains(categoryId, thingDef);
        }

        //注册或替换运行时商品分类。
        public static RuntimeGoodsCategory RegisterCategory(string categoryId, string label, IEnumerable<ThingDef> goodsList, GoodsDef sourceDef = null, bool replace = false)
        {
            return Manager?.RegisterCategory(categoryId, label, goodsList, sourceDef, replace);
        }

        //把商品加入指定运行时分类。
        public static bool RegisterItemToCategory(string categoryId, ThingDef thingDef)
        {
            return Manager != null && Manager.RegisterItemToCategory(categoryId, thingDef);
        }

        //通知商品目录变化，职责是重建目录并分片失效补货配置与所有地图吸引力快照。
        public static void NotifyCatalogChanged()
        {
            Manager?.RebuildFromDefs();
            if (Find.Maps == null) return;
            for (int i = 0; i < Find.Maps.Count; i++)
            {
                Find.Maps[i]?.GetComponent<MapComponent_RestockTaskQueue>()?.NotifyGoodsCatalogChanged();
                Find.Maps[i]?.GetComponent<CustomerArrivalManager>()?.NotifyCustomerCatalogDirty();
            }
        }

        //构建未进入游戏时使用的商品目录预览。
        public static IReadOnlyCollection<RuntimeGoodsCategory> BuildPreviewCategories()
        {
            Dictionary<string, RuntimeGoodsCategory> categories = new Dictionary<string, RuntimeGoodsCategory>();
            Dictionary<string, RuntimeGoodsItem> items = new Dictionary<string, RuntimeGoodsItem>();

            foreach (GoodsDef goodsDef in DefDatabase<GoodsDef>.AllDefsListForReading.Where(def => def != null))
            {
                RuntimeGoodsCategory category = GetOrCreateCategory(categories, goodsDef.defName, goodsDef.label, goodsDef);
                category.Clear();
                if (goodsDef.GoodsList.NullOrEmpty())
                    continue;

                for (int i = 0; i < goodsDef.GoodsList.Count; i++)
                {
                    ThingDef thingDef = goodsDef.GoodsList[i];
                    if (thingDef == null)
                        continue;

                    category.TryAdd(GetOrCreateItem(items, thingDef), false);
                }
            }

            CustomGoodsDatabaseData customData = CustomGoodsDatabase.Load();
            if (customData?.categories != null)
            {
                for (int i = 0; i < customData.categories.Count; i++)
                {
                    CustomGoodsCategoryRecord record = customData.categories[i];
                    if (record == null || string.IsNullOrEmpty(record.categoryId))
                        continue;

                    GoodsDef sourceDef = DefDatabase<GoodsDef>.GetNamedSilentFail(record.categoryId);
                    RuntimeGoodsCategory category = GetOrCreateCategory(
                        categories,
                        record.categoryId,
                        sourceDef != null ? sourceDef.label : record.label,
                        sourceDef);

                    category.hasPlayerDefinedConfig = true;
                    if (sourceDef == null && !string.IsNullOrEmpty(record.label))
                        category.label = record.label;

                    if (record.itemDefNames.NullOrEmpty())
                        continue;

                    for (int itemIndex = 0; itemIndex < record.itemDefNames.Count; itemIndex++)
                    {
                        ThingDef thingDef = DefDatabase<ThingDef>.GetNamedSilentFail(record.itemDefNames[itemIndex]);
                        if (!CustomGoodsDatabase.IsValidCandidateThing(thingDef))
                            continue;

                        category.TryAdd(GetOrCreateItem(items, thingDef), true);
                    }
                }
            }

            return categories.Values.ToList();
        }

        //取得或创建预览分类，职责是合并 Def 和玩家自定义分类。
        private static RuntimeGoodsCategory GetOrCreateCategory(
            IDictionary<string, RuntimeGoodsCategory> categories,
            string categoryId,
            string label,
            GoodsDef sourceDef)
        {
            if (!categories.TryGetValue(categoryId, out RuntimeGoodsCategory category))
            {
                category = new RuntimeGoodsCategory
                {
                    categoryId = categoryId,
                    label = string.IsNullOrEmpty(label) ? categoryId : label,
                    sourceDef = sourceDef
                };
                categories[categoryId] = category;
            }
            else
            {
                if (!string.IsNullOrEmpty(label))
                    category.label = label;
                if (sourceDef != null)
                    category.sourceDef = sourceDef;
            }

            return category;
        }

        //取得或创建预览商品项，职责是复用相同 ThingDef 的运行时对象。
        private static RuntimeGoodsItem GetOrCreateItem(IDictionary<string, RuntimeGoodsItem> items, ThingDef thingDef)
        {
            if (!items.TryGetValue(thingDef.defName, out RuntimeGoodsItem item))
            {
                item = new RuntimeGoodsItem
                {
                    thingDefName = thingDef.defName,
                    thingDef = thingDef,
                    label = thingDef.LabelCap.RawText,
                    baseMarketValue = thingDef.BaseMarketValue
                };
                items[thingDef.defName] = item;
            }

            return item;
        }
    }
}
