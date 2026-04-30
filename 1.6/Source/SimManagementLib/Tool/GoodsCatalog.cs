using SimManagementLib.GameComp;
using SimManagementLib.Pojo;
using SimManagementLib.SimDef;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SimManagementLib.Tool
{
    public static class GoodsCatalog
    {
        private static IReadOnlyList<RuntimeGoodsItem> EmptyItems { get; } = new List<RuntimeGoodsItem>();

        public static GameComponent_GoodsCatalog Manager => Current.Game?.GetComponent<GameComponent_GoodsCatalog>();
        public static IReadOnlyCollection<RuntimeGoodsCategory> Categories => Manager?.Categories ?? BuildPreviewCategories();

        public static void EnsureInitialized()
        {
            Manager?.EnsureInitialized();
        }

        public static RuntimeGoodsCategory GetCategory(string categoryId)
        {
            return Manager?.GetCategory(categoryId);
        }

        public static GoodsDef GetSourceDef(string categoryId)
        {
            return GetCategory(categoryId)?.sourceDef;
        }

        public static IReadOnlyList<RuntimeGoodsItem> GetItems(string categoryId)
        {
            return Manager?.GetItemsForCategory(categoryId) ?? EmptyItems;
        }

        public static RuntimeGoodsItem GetItem(ThingDef thingDef)
        {
            return Manager?.GetItem(thingDef);
        }

        public static bool Contains(string categoryId, ThingDef thingDef)
        {
            return Manager != null && Manager.CategoryContains(categoryId, thingDef);
        }

        public static RuntimeGoodsCategory RegisterCategory(string categoryId, string label, IEnumerable<ThingDef> goodsList, GoodsDef sourceDef = null, bool replace = false)
        {
            return Manager?.RegisterCategory(categoryId, label, goodsList, sourceDef, replace);
        }

        public static bool RegisterItemToCategory(string categoryId, ThingDef thingDef)
        {
            return Manager != null && Manager.RegisterItemToCategory(categoryId, thingDef);
        }

        public static void NotifyCatalogChanged()
        {
            Manager?.RebuildFromDefs();
        }

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
