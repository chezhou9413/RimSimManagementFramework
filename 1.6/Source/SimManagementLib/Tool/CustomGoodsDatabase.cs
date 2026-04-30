using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.SimDef;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using Verse;

namespace SimManagementLib.Tool
{
    public static class CustomGoodsDatabase
    {
        private const string FileName = "custom_goods_registry.json";
        private static CustomGoodsDatabaseData cache;

        public static CustomGoodsDatabaseData Load()
        {
            if (cache == null)
                cache = LoadFromDisk();

            return Clone(cache);
        }

        public static void Save(CustomGoodsDatabaseData data)
        {
            cache = Sanitize(data);
            EnsureDirectoryExists();

            using (FileStream stream = File.Create(FilePath))
            {
                JsonSerializer.WriteObject(stream, cache);
            }
        }

        public static void Reload()
        {
            cache = null;
        }

        public static string ExportBase64(CustomGoodsDatabaseData data)
        {
            CustomGoodsDatabaseData sanitized = Sanitize(data);
            using (MemoryStream stream = new MemoryStream())
            {
                JsonSerializer.WriteObject(stream, sanitized);
                return Convert.ToBase64String(stream.ToArray());
            }
        }

        public static bool TryImportBase64(string base64, out CustomGoodsDatabaseData data, out string error)
        {
            data = null;
            error = null;

            if (string.IsNullOrWhiteSpace(base64))
            {
                error = "导入内容为空。";
                return false;
            }

            try
            {
                byte[] bytes = Convert.FromBase64String(base64.Trim());
                using (MemoryStream stream = new MemoryStream(bytes))
                {
                    data = Sanitize((CustomGoodsDatabaseData)JsonSerializer.ReadObject(stream));
                    return true;
                }
            }
            catch (Exception ex)
            {
                error = "Base64 或 JSON 解析失败: " + ex.Message;
                return false;
            }
        }

        public static void NotifyRuntimeChanged()
        {
            GoodsCatalog.NotifyCatalogChanged();
        }

        public static string GenerateUniqueCategoryId(string label, IEnumerable<string> existingIds)
        {
            string baseId = "custom_" + Slugify(label);
            if (baseId == "custom_")
                baseId = "custom_category";

            HashSet<string> usedIds = new HashSet<string>(
                existingIds?.Where(id => !string.IsNullOrWhiteSpace(id)).Select(NormalizeId) ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            string candidate = baseId;
            int suffix = 2;
            while (usedIds.Contains(candidate))
            {
                candidate = baseId + "_" + suffix;
                suffix++;
            }

            return candidate;
        }

        public static bool IsValidCandidateThing(ThingDef thingDef)
        {
            if (thingDef == null) return false;
            if (thingDef.category != ThingCategory.Item) return false;
            if (thingDef.IsCorpse || thingDef.IsBlueprint || thingDef.IsFrame || thingDef.destroyOnDrop)
                return false;
            return thingDef.tradeability != Tradeability.None || thingDef.BaseMarketValue > 0f;
        }

        public static List<ThingDef> GetAllCandidateThings()
        {
            return DefDatabase<ThingDef>.AllDefsListForReading
                .Where(IsValidCandidateThing)
                .OrderBy(def => def.label ?? def.defName)
                .ThenBy(def => def.defName)
                .ToList();
        }

        public static string NormalizeId(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        public static string NormalizeLabel(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static string ConfigDirectory => Path.Combine(GenFilePaths.ConfigFolderPath, "SimManagementLib");
        private static string FilePath => Path.Combine(ConfigDirectory, FileName);
        private static DataContractJsonSerializer JsonSerializer { get; } = new DataContractJsonSerializer(typeof(CustomGoodsDatabaseData));

        private static CustomGoodsDatabaseData LoadFromDisk()
        {
            if (!File.Exists(FilePath))
                return new CustomGoodsDatabaseData();

            try
            {
                using (FileStream stream = File.OpenRead(FilePath))
                {
                    return Sanitize((CustomGoodsDatabaseData)JsonSerializer.ReadObject(stream));
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[SimManagementLib] Failed to load custom goods database, using empty fallback. " + ex);
                return new CustomGoodsDatabaseData();
            }
        }

        private static void EnsureDirectoryExists()
        {
            if (!Directory.Exists(ConfigDirectory))
                Directory.CreateDirectory(ConfigDirectory);
        }

        private static CustomGoodsDatabaseData Sanitize(CustomGoodsDatabaseData data)
        {
            CustomGoodsDatabaseData sanitized = new CustomGoodsDatabaseData();
            if (data == null || data.categories == null)
                return sanitized;

            Dictionary<string, CustomGoodsCategoryRecord> merged = new Dictionary<string, CustomGoodsCategoryRecord>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> builtInIds = new HashSet<string>(
                DefDatabase<GoodsDef>.AllDefsListForReading.Where(def => def != null).Select(def => def.defName),
                StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < data.categories.Count; i++)
            {
                CustomGoodsCategoryRecord source = data.categories[i];
                string categoryId = NormalizeId(source?.categoryId);
                if (string.IsNullOrEmpty(categoryId))
                    continue;

                bool builtInCategory = source != null && (source.builtInCategory || builtInIds.Contains(categoryId));
                string label = NormalizeLabel(source?.label);

                if (!merged.TryGetValue(categoryId, out CustomGoodsCategoryRecord target))
                {
                    target = new CustomGoodsCategoryRecord
                    {
                        categoryId = categoryId,
                        label = builtInCategory ? string.Empty : (string.IsNullOrEmpty(label) ? categoryId : label),
                        builtInCategory = builtInCategory,
                        itemDefNames = new List<string>()
                    };
                    merged[categoryId] = target;
                }
                else
                {
                    target.builtInCategory |= builtInCategory;
                    if (!target.builtInCategory && string.IsNullOrEmpty(target.label) && !string.IsNullOrEmpty(label))
                        target.label = label;
                }

                IEnumerable<string> itemDefNames = source?.itemDefNames ?? Enumerable.Empty<string>();
                foreach (string itemDefName in itemDefNames)
                {
                    ThingDef thingDef = DefDatabase<ThingDef>.GetNamedSilentFail(itemDefName);
                    if (IsValidCandidateThing(thingDef) && !target.itemDefNames.Contains(thingDef.defName))
                        target.itemDefNames.Add(thingDef.defName);
                }
            }

            sanitized.categories = merged.Values
                .OrderBy(record => record.builtInCategory ? 0 : 1)
                .ThenBy(record => record.label ?? record.categoryId)
                .ToList();

            return sanitized;
        }

        private static CustomGoodsDatabaseData Clone(CustomGoodsDatabaseData source)
        {
            CustomGoodsDatabaseData clone = new CustomGoodsDatabaseData
            {
                version = source?.version ?? 1,
                categories = new List<CustomGoodsCategoryRecord>()
            };

            if (source?.categories == null)
                return clone;

            for (int i = 0; i < source.categories.Count; i++)
            {
                CustomGoodsCategoryRecord record = source.categories[i];
                clone.categories.Add(new CustomGoodsCategoryRecord
                {
                    categoryId = record?.categoryId ?? string.Empty,
                    label = record?.label ?? string.Empty,
                    builtInCategory = record?.builtInCategory ?? false,
                    itemDefNames = record?.itemDefNames?.Where(name => !string.IsNullOrEmpty(name)).Distinct().ToList() ?? new List<string>()
                });
            }

            return clone;
        }

        private static string Slugify(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < value.Length; i++)
            {
                char ch = char.ToLowerInvariant(value[i]);
                if (char.IsLetterOrDigit(ch))
                {
                    builder.Append(ch);
                }
                else if (builder.Length == 0 || builder[builder.Length - 1] != '_')
                {
                    builder.Append('_');
                }
            }

            return builder.ToString().Trim('_');
        }
    }
}
