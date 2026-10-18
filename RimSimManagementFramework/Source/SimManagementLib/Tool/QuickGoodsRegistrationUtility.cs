using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.SimDef;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 处理框选物品快捷注册为商品的流程，负责收集有效物品、写入自定义商品数据并刷新运行时目录。
    /// </summary>
    public static class QuickGoodsRegistrationUtility
    {
        /// <summary>
        /// 从地图格子中收集可注册商品，负责按 ThingDef 去重并过滤不可出售物品。
        /// </summary>
        public static List<ThingDef> CollectCandidateThingDefs(Map map, IEnumerable<IntVec3> cells)
        {
            Dictionary<string, ThingDef> result = new Dictionary<string, ThingDef>(StringComparer.OrdinalIgnoreCase);
            if (map == null || cells == null) return result.Values.ToList();

            foreach (IntVec3 cell in cells)
            {
                if (!cell.InBounds(map) || cell.Fogged(map)) continue;
                List<Thing> things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    ThingDef def = things[i]?.def;
                    if (!CustomGoodsDatabase.IsValidCandidateThing(def)) continue;
                    if (!result.ContainsKey(def.defName))
                        result.Add(def.defName, def);
                }
            }

            return result.Values
                .OrderBy(def => def.LabelCap.RawText)
                .ThenBy(def => def.defName)
                .ToList();
        }

        /// <summary>
        /// 返回可供快捷注册选择的商品分类，负责合并内置和玩家自定义分类。
        /// </summary>
        public static List<RuntimeGoodsCategory> GetAvailableCategories()
        {
            GoodsCatalog.EnsureInitialized();
            return (GoodsCatalog.Categories ?? Enumerable.Empty<RuntimeGoodsCategory>())
                .Where(category => category != null && !string.IsNullOrEmpty(category.categoryId))
                .OrderBy(category => category.IsBuiltInCategory ? 0 : 1)
                .ThenBy(category => category.label)
                .ToList();
        }

        /// <summary>
        /// 将商品追加到指定分类的玩家自定义记录，负责跳过已存在项并返回实际新增数量。
        /// </summary>
        public static int RegisterItemsToCategory(string categoryId, IEnumerable<ThingDef> thingDefs)
        {
            if (string.IsNullOrEmpty(categoryId) || thingDefs == null) return 0;

            List<ThingDef> validDefs = thingDefs
                .Where(CustomGoodsDatabase.IsValidCandidateThing)
                .GroupBy(def => def.defName, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            if (validDefs.NullOrEmpty()) return 0;

            RuntimeGoodsCategory category = GoodsCatalog.Categories?.FirstOrDefault(item => string.Equals(item.categoryId, categoryId, StringComparison.OrdinalIgnoreCase));
            GoodsDef sourceDef = DefDatabase<GoodsDef>.GetNamedSilentFail(categoryId);
            bool builtInCategory = sourceDef != null || category?.IsBuiltInCategory == true;
            string label = category?.label ?? sourceDef?.label ?? categoryId;

            CustomGoodsDatabaseData data = CustomGoodsDatabase.Load();
            if (data.categories == null)
                data.categories = new List<CustomGoodsCategoryRecord>();

            CustomGoodsCategoryRecord record = data.categories.FirstOrDefault(item => string.Equals(item.categoryId, categoryId, StringComparison.OrdinalIgnoreCase));
            if (record == null)
            {
                record = new CustomGoodsCategoryRecord
                {
                    categoryId = categoryId,
                    label = builtInCategory ? string.Empty : label,
                    builtInCategory = builtInCategory,
                    itemDefNames = new List<string>()
                };
                data.categories.Add(record);
            }

            record.builtInCategory |= builtInCategory;
            if (!record.builtInCategory && string.IsNullOrEmpty(record.label))
                record.label = label;
            if (record.itemDefNames == null)
                record.itemDefNames = new List<string>();

            HashSet<string> existing = new HashSet<string>(record.itemDefNames.Where(name => !string.IsNullOrEmpty(name)), StringComparer.OrdinalIgnoreCase);
            int added = 0;
            for (int i = 0; i < validDefs.Count; i++)
            {
                ThingDef def = validDefs[i];
                if (category != null && category.Contains(def)) continue;
                if (!existing.Add(def.defName)) continue;
                record.itemDefNames.Add(def.defName);
                added++;
            }

            if (added <= 0) return 0;

            CustomGoodsDatabase.Save(data);
            CustomGoodsDatabase.NotifyRuntimeChanged();
            return added;
        }

        /// <summary>
        /// 构造商品预览文本，负责让弹窗用短列表提示本次会注册的物品。
        /// </summary>
        public static string BuildThingPreview(IEnumerable<ThingDef> thingDefs, int maxCount = 8)
        {
            List<ThingDef> defs = thingDefs?.Where(def => def != null).ToList() ?? new List<ThingDef>();
            if (defs.NullOrEmpty()) return string.Empty;

            List<string> labels = defs
                .Take(maxCount)
                .Select(def => def.LabelCap.RawText)
                .ToList();
            if (defs.Count > maxCount)
                labels.Add(SimTranslation.T("RSMF.QuickGoodsRegister.MoreItems", (defs.Count - maxCount).Named("count")));
            return string.Join("、", labels);
        }
    }
}
