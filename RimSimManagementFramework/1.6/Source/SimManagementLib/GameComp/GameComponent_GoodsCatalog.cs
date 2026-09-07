using SimManagementLib.Pojo;
using SimManagementLib.SimDef;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SimManagementLib.GameComp
{
    public class GameComponent_GoodsCatalog : GameComponent
    {
        private Dictionary<string, RuntimeGoodsCategory> categoriesById = new Dictionary<string, RuntimeGoodsCategory>();
        private Dictionary<string, RuntimeGoodsItem> itemsByDefName = new Dictionary<string, RuntimeGoodsItem>();
        private bool initialized;

        public GameComponent_GoodsCatalog(Game game)
        {
        }

        public IReadOnlyCollection<RuntimeGoodsCategory> Categories
        {
            get
            {
                EnsureInitialized();
                return categoriesById.Values;
            }
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            EnsureInitialized();
        }

        public void EnsureInitialized()
        {
            if (initialized) return;
            RebuildFromDefs();
        }

        public void RebuildFromDefs()
        {
            categoriesById.Clear();
            itemsByDefName.Clear();

            foreach (GoodsDef goodsDef in DefDatabase<GoodsDef>.AllDefsListForReading.Where(d => d != null))
            {
                RuntimeGoodsCategory category = GetOrCreateCategory(goodsDef.defName, goodsDef.label, goodsDef);
                category.Clear();

                if (goodsDef.GoodsList.NullOrEmpty()) continue;

                for (int i = 0; i < goodsDef.GoodsList.Count; i++)
                {
                    ThingDef thingDef = goodsDef.GoodsList[i];
                    if (thingDef == null) continue;
                    category.TryAdd(GetOrCreateItem(thingDef), false);
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

                        category.TryAdd(GetOrCreateItem(thingDef), true);
                    }
                }
            }

            initialized = true;
        }

        public RuntimeGoodsCategory GetCategory(string categoryId)
        {
            EnsureInitialized();
            if (string.IsNullOrEmpty(categoryId)) return null;
            categoriesById.TryGetValue(categoryId, out RuntimeGoodsCategory category);
            return category;
        }

        public RuntimeGoodsItem GetItem(ThingDef thingDef)
        {
            EnsureInitialized();
            if (thingDef == null) return null;
            itemsByDefName.TryGetValue(thingDef.defName, out RuntimeGoodsItem item);
            return item;
        }

        public IReadOnlyList<RuntimeGoodsItem> GetItemsForCategory(string categoryId)
        {
            return GetCategory(categoryId)?.Items ?? EmptyItems;
        }

        public bool CategoryContains(string categoryId, ThingDef thingDef)
        {
            RuntimeGoodsCategory category = GetCategory(categoryId);
            return category != null && category.Contains(thingDef);
        }

        public RuntimeGoodsCategory RegisterCategory(string categoryId, string label, IEnumerable<ThingDef> goodsList, GoodsDef sourceDef = null, bool replace = false)
        {
            EnsureInitialized();
            if (string.IsNullOrEmpty(categoryId)) return null;
            if (categoriesById.ContainsKey(categoryId) && !replace) return categoriesById[categoryId];

            RuntimeGoodsCategory category = GetOrCreateCategory(categoryId, label, sourceDef);
            category.label = string.IsNullOrEmpty(label) ? categoryId : label;
            category.sourceDef = sourceDef;
            category.Clear();

            if (goodsList != null)
            {
                foreach (ThingDef thingDef in goodsList)
                {
                    RegisterItemToCategory(categoryId, thingDef);
                }
            }

            return category;
        }

        public bool RegisterItemToCategory(string categoryId, ThingDef thingDef)
        {
            EnsureInitialized();
            if (string.IsNullOrEmpty(categoryId) || thingDef == null) return false;

            RuntimeGoodsCategory category = GetOrCreateCategory(categoryId, categoryId, null);
            return category.TryAdd(GetOrCreateItem(thingDef), true);
        }

        private RuntimeGoodsCategory GetOrCreateCategory(string categoryId, string label, GoodsDef sourceDef)
        {
            if (!categoriesById.TryGetValue(categoryId, out RuntimeGoodsCategory category))
            {
                category = new RuntimeGoodsCategory
                {
                    categoryId = categoryId,
                    label = string.IsNullOrEmpty(label) ? categoryId : label,
                    sourceDef = sourceDef
                };
                categoriesById[categoryId] = category;
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

        private RuntimeGoodsItem GetOrCreateItem(ThingDef thingDef)
        {
            if (!itemsByDefName.TryGetValue(thingDef.defName, out RuntimeGoodsItem item))
            {
                item = new RuntimeGoodsItem
                {
                    thingDefName = thingDef.defName,
                    thingDef = thingDef,
                    label = thingDef.LabelCap.RawText,
                    baseMarketValue = thingDef.BaseMarketValue
                };
                itemsByDefName[thingDef.defName] = item;
            }

            return item;
        }

        private static IReadOnlyList<RuntimeGoodsItem> EmptyItems { get; } = new List<RuntimeGoodsItem>();
    }
}
