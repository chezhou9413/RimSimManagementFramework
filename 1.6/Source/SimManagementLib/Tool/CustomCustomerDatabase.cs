using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.SimDef;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using UnityEngine;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 负责加载、保存、校验和导出玩家注册的运行时顾客类型。
    /// </summary>
    public static class CustomCustomerDatabase
    {
        private const string FileName = "custom_customer_registry.json";
        private static CustomCustomerDatabaseData cache;

        private static string ConfigDirectory => Path.Combine(GenFilePaths.ConfigFolderPath, "SimManagementLib");
        private static string FilePath => Path.Combine(ConfigDirectory, FileName);
        private static DataContractJsonSerializer JsonSerializer { get; } = new DataContractJsonSerializer(typeof(CustomCustomerDatabaseData));

        /// <summary>
        /// 返回玩家顾客数据库缓存的副本，首次访问时从磁盘读取。
        /// </summary>
        public static CustomCustomerDatabaseData Load()
        {
            if (cache == null)
                cache = LoadFromDisk();

            return Clone(cache);
        }

        /// <summary>
        /// 清洗玩家顾客数据库并写入 RimWorld 配置目录。
        /// </summary>
        public static void Save(CustomCustomerDatabaseData data)
        {
            cache = Sanitize(data);
            EnsureDirectoryExists();

            using (FileStream stream = File.Create(FilePath))
            {
                JsonSerializer.WriteObject(stream, cache);
            }
        }

        /// <summary>
        /// 清空内存缓存，使下一次加载重新读取磁盘文件。
        /// </summary>
        public static void Reload()
        {
            cache = null;
        }

        /// <summary>
        /// 将顾客注册数据序列化为可分享的 Base64 文本。
        /// </summary>
        public static string ExportBase64(CustomCustomerDatabaseData data)
        {
            CustomCustomerDatabaseData sanitized = Sanitize(data);
            using (MemoryStream stream = new MemoryStream())
            {
                JsonSerializer.WriteObject(stream, sanitized);
                return Convert.ToBase64String(stream.ToArray());
            }
        }

        /// <summary>
        /// 将 Base64 顾客注册文本解析为清洗后的数据。
        /// </summary>
        public static bool TryImportBase64(string base64, out CustomCustomerDatabaseData data, out string error)
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
                    data = Sanitize((CustomCustomerDatabaseData)JsonSerializer.ReadObject(stream));
                    return true;
                }
            }
            catch (Exception ex)
            {
                error = "Base64 或 JSON 解析失败: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 通知运行时顾客目录玩家数据已经变化。
        /// </summary>
        public static void NotifyRuntimeChanged()
        {
            CustomerCatalog.NotifyCatalogChanged();
        }

        /// <summary>
        /// 根据显示名称和已有 ID 生成稳定且唯一的顾客类型 ID。
        /// </summary>
        public static string GenerateUniqueKindId(string label, IEnumerable<string> existingIds)
        {
            string baseId = "custom_customer_" + Slugify(label);
            if (baseId == "custom_customer_")
                baseId = "custom_customer_kind";

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

        /// <summary>
        /// 判断 PawnKindDef 是否适合作为运行时顾客候选项暴露给玩家选择。
        /// </summary>
        public static bool IsValidCandidatePawnKind(PawnKindDef pawnKindDef)
        {
            if (pawnKindDef == null || pawnKindDef.race == null) return false;
            if (pawnKindDef.race.race == null) return false;
            return pawnKindDef.race.race.Humanlike || pawnKindDef.race.race.Animal || pawnKindDef.race.race.IsMechanoid;
        }

        /// <summary>
        /// 返回运行时顾客注册面板可以选择的全部 PawnKindDef。
        /// </summary>
        public static List<PawnKindDef> GetAllCandidatePawnKinds()
        {
            return DefDatabase<PawnKindDef>.AllDefsListForReading
                .Where(IsValidCandidatePawnKind)
                .OrderBy(def => def.LabelCap.RawText)
                .ThenBy(def => def.defName)
                .ToList();
        }

        /// <summary>
        /// 返回可以作为顾客偏好目标的全部物品 ThingDef。
        /// </summary>
        public static List<ThingDef> GetAllCandidatePreferenceThings()
        {
            return CustomGoodsDatabase.GetAllCandidateThings();
        }

        /// <summary>
        /// 返回可以作为顾客出现条件的全部天气 Def。
        /// </summary>
        public static List<WeatherDef> GetAllWeatherDefs()
        {
            return DefDatabase<WeatherDef>.AllDefsListForReading
                .Where(def => def != null)
                .OrderBy(def => def.LabelCap.RawText)
                .ThenBy(def => def.defName)
                .ToList();
        }

        /// <summary>
        /// 修剪 ID 文本并保留原始可读内容。
        /// </summary>
        public static string NormalizeId(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        /// <summary>
        /// 修剪显示名称并保留中文和其他本地化字符。
        /// </summary>
        public static string NormalizeLabel(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static CustomCustomerDatabaseData LoadFromDisk()
        {
            if (!File.Exists(FilePath))
                return new CustomCustomerDatabaseData();

            try
            {
                using (FileStream stream = File.OpenRead(FilePath))
                {
                    return Sanitize((CustomCustomerDatabaseData)JsonSerializer.ReadObject(stream));
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[SimManagementLib] Failed to load custom customer database, using empty fallback. " + ex);
                return new CustomCustomerDatabaseData();
            }
        }

        private static void EnsureDirectoryExists()
        {
            if (!Directory.Exists(ConfigDirectory))
                Directory.CreateDirectory(ConfigDirectory);
        }

        private static CustomCustomerDatabaseData Sanitize(CustomCustomerDatabaseData data)
        {
            CustomCustomerDatabaseData sanitized = new CustomCustomerDatabaseData();
            if (data == null || data.kinds == null)
                return sanitized;

            HashSet<string> builtInIds = new HashSet<string>(
                DefDatabase<CustomerKindDef>.AllDefsListForReading.Where(def => def != null).Select(def => def.defName),
                StringComparer.OrdinalIgnoreCase);
            Dictionary<string, CustomCustomerKindRecord> merged = new Dictionary<string, CustomCustomerKindRecord>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < data.kinds.Count; i++)
            {
                CustomCustomerKindRecord source = data.kinds[i];
                string kindId = NormalizeId(source?.kindId);
                if (string.IsNullOrEmpty(kindId) || builtInIds.Contains(kindId))
                    continue;

                string label = NormalizeLabel(source?.label);
                if (string.IsNullOrEmpty(label))
                    label = kindId;

                CustomCustomerKindRecord target = new CustomCustomerKindRecord
                {
                    kindId = kindId,
                    label = label,
                    pawnKindDefNames = NormalizePawnKinds(source?.pawnKindDefNames),
                    baseMtbDays = Mathf.Clamp(source?.baseMtbDays ?? 0.25f, 0.01f, 20f),
                    budgetMin = Mathf.Clamp(Math.Min(source?.budgetMin ?? 100, source?.budgetMax ?? 400), 1, 1000000),
                    budgetMax = Mathf.Clamp(Math.Max(source?.budgetMin ?? 100, source?.budgetMax ?? 400), 1, 1000000),
                    queuePatienceMin = Mathf.Clamp(Math.Min(source?.queuePatienceMin ?? 900, source?.queuePatienceMax ?? 3000), 60, 120000),
                    queuePatienceMax = Mathf.Clamp(Math.Max(source?.queuePatienceMin ?? 900, source?.queuePatienceMax ?? 3000), 60, 120000),
                    activeHourMin = Mathf.Clamp(source?.activeHourMin ?? 0f, 0f, 24f),
                    activeHourMax = Mathf.Clamp(source?.activeHourMax ?? 24f, 0f, 24f),
                    allowedWeatherDefNames = NormalizeWeatherDefs(source?.allowedWeatherDefNames),
                    minShopReputation = Mathf.Clamp(source?.minShopReputation ?? 0f, 0f, 100f),
                    targetGoodsCategoryIds = NormalizeGoodsCategoryIds(source?.targetGoodsCategoryIds),
                    itemPreferences = NormalizePreferences(source?.itemPreferences),
                    spawnProfiles = NormalizeProfiles(source?.spawnProfiles)
                };

                if (target.pawnKindDefNames.Count == 0)
                    continue;

                merged[kindId] = target;
            }

            sanitized.kinds = merged.Values
                .OrderBy(record => record.label)
                .ThenBy(record => record.kindId)
                .ToList();
            return sanitized;
        }

        private static List<string> NormalizePawnKinds(IEnumerable<string> source)
        {
            List<string> result = new List<string>();
            foreach (string defName in source ?? Enumerable.Empty<string>())
            {
                PawnKindDef pawnKindDef = DefDatabase<PawnKindDef>.GetNamedSilentFail(defName);
                if (IsValidCandidatePawnKind(pawnKindDef) && !result.Contains(pawnKindDef.defName))
                    result.Add(pawnKindDef.defName);
            }
            return result;
        }

        private static List<string> NormalizeWeatherDefs(IEnumerable<string> source)
        {
            List<string> result = new List<string>();
            foreach (string defName in source ?? Enumerable.Empty<string>())
            {
                WeatherDef weatherDef = DefDatabase<WeatherDef>.GetNamedSilentFail(defName);
                if (weatherDef != null && !result.Contains(weatherDef.defName))
                    result.Add(weatherDef.defName);
            }
            return result;
        }

        private static List<string> NormalizeGoodsCategoryIds(IEnumerable<string> source)
        {
            List<string> knownIds = GoodsCatalog.Categories?
                .Select(category => category.categoryId)
                .Where(id => !string.IsNullOrEmpty(id))
                .ToList() ?? new List<string>();
            HashSet<string> known = new HashSet<string>(knownIds, StringComparer.OrdinalIgnoreCase);

            return (source ?? Enumerable.Empty<string>())
                .Select(NormalizeId)
                .Where(id => !string.IsNullOrEmpty(id) && known.Contains(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<CustomCustomerPreferenceRecord> NormalizePreferences(IEnumerable<CustomCustomerPreferenceRecord> source)
        {
            List<CustomCustomerPreferenceRecord> result = new List<CustomCustomerPreferenceRecord>();
            foreach (CustomCustomerPreferenceRecord pref in source ?? Enumerable.Empty<CustomCustomerPreferenceRecord>())
            {
                string categoryId = NormalizeGoodsCategoryIds(new[] { pref?.preferredGoodsCategoryId }).FirstOrDefault() ?? string.Empty;
                ThingDef thingDef = DefDatabase<ThingDef>.GetNamedSilentFail(pref?.preferredThingDefName);
                if (string.IsNullOrEmpty(categoryId) && !CustomGoodsDatabase.IsValidCandidateThing(thingDef))
                    continue;

                result.Add(new CustomCustomerPreferenceRecord
                {
                    preferredGoodsCategoryId = categoryId,
                    preferredThingDefName = thingDef != null ? thingDef.defName : string.Empty,
                    tag = NormalizeLabel(pref?.tag),
                    weight = Mathf.Clamp(pref?.weight ?? 1f, 1f, 20f)
                });
            }
            return result;
        }

        private static List<CustomCustomerProfileRecord> NormalizeProfiles(IEnumerable<CustomCustomerProfileRecord> source)
        {
            List<CustomCustomerProfileRecord> result = new List<CustomCustomerProfileRecord>();
            foreach (CustomCustomerProfileRecord profile in source ?? Enumerable.Empty<CustomCustomerProfileRecord>())
            {
                string label = NormalizeLabel(profile?.label);
                if (string.IsNullOrEmpty(label))
                    label = "顾客档案";

                result.Add(new CustomCustomerProfileRecord
                {
                    label = label,
                    weight = Mathf.Clamp(profile?.weight ?? 1f, 0.01f, 100f),
                    budgetMin = Mathf.Clamp(Math.Min(profile?.budgetMin ?? 100, profile?.budgetMax ?? 400), 1, 1000000),
                    budgetMax = Mathf.Clamp(Math.Max(profile?.budgetMin ?? 100, profile?.budgetMax ?? 400), 1, 1000000),
                    queuePatienceMin = Mathf.Clamp(Math.Min(profile?.queuePatienceMin ?? 900, profile?.queuePatienceMax ?? 3000), 60, 120000),
                    queuePatienceMax = Mathf.Clamp(Math.Max(profile?.queuePatienceMin ?? 900, profile?.queuePatienceMax ?? 3000), 60, 120000),
                    activeHourMin = Mathf.Clamp(profile?.activeHourMin ?? 0f, 0f, 24f),
                    activeHourMax = Mathf.Clamp(profile?.activeHourMax ?? 24f, 0f, 24f),
                    allowedWeatherDefNames = NormalizeWeatherDefs(profile?.allowedWeatherDefNames),
                    preferredThingDefNames = NormalizePreferenceThings(profile?.preferredThingDefNames),
                    preferredGoodsCategoryIds = NormalizeGoodsCategoryIds(profile?.preferredGoodsCategoryIds)
                });
            }
            return result;
        }

        private static List<string> NormalizePreferenceThings(IEnumerable<string> source)
        {
            List<string> result = new List<string>();
            foreach (string defName in source ?? Enumerable.Empty<string>())
            {
                ThingDef thingDef = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                if (CustomGoodsDatabase.IsValidCandidateThing(thingDef) && !result.Contains(thingDef.defName))
                    result.Add(thingDef.defName);
            }
            return result;
        }

        private static CustomCustomerDatabaseData Clone(CustomCustomerDatabaseData source)
        {
            CustomCustomerDatabaseData clone = new CustomCustomerDatabaseData
            {
                version = source?.version ?? 1,
                kinds = new List<CustomCustomerKindRecord>()
            };

            foreach (CustomCustomerKindRecord record in source?.kinds ?? Enumerable.Empty<CustomCustomerKindRecord>())
            {
                clone.kinds.Add(CloneKind(record));
            }

            return clone;
        }

        private static CustomCustomerKindRecord CloneKind(CustomCustomerKindRecord record)
        {
            return new CustomCustomerKindRecord
            {
                kindId = record?.kindId ?? string.Empty,
                label = record?.label ?? string.Empty,
                pawnKindDefNames = record?.pawnKindDefNames?.ToList() ?? new List<string>(),
                baseMtbDays = record?.baseMtbDays ?? 0.25f,
                budgetMin = record?.budgetMin ?? 100,
                budgetMax = record?.budgetMax ?? 400,
                queuePatienceMin = record?.queuePatienceMin ?? 900,
                queuePatienceMax = record?.queuePatienceMax ?? 3000,
                activeHourMin = record?.activeHourMin ?? 0f,
                activeHourMax = record?.activeHourMax ?? 24f,
                allowedWeatherDefNames = record?.allowedWeatherDefNames?.ToList() ?? new List<string>(),
                minShopReputation = record?.minShopReputation ?? 0f,
                targetGoodsCategoryIds = record?.targetGoodsCategoryIds?.ToList() ?? new List<string>(),
                itemPreferences = record?.itemPreferences?.Select(ClonePreference).ToList() ?? new List<CustomCustomerPreferenceRecord>(),
                spawnProfiles = record?.spawnProfiles?.Select(CloneProfile).ToList() ?? new List<CustomCustomerProfileRecord>()
            };
        }

        private static CustomCustomerPreferenceRecord ClonePreference(CustomCustomerPreferenceRecord pref)
        {
            return new CustomCustomerPreferenceRecord
            {
                preferredGoodsCategoryId = pref?.preferredGoodsCategoryId ?? string.Empty,
                preferredThingDefName = pref?.preferredThingDefName ?? string.Empty,
                tag = pref?.tag ?? string.Empty,
                weight = pref?.weight ?? 1f
            };
        }

        private static CustomCustomerProfileRecord CloneProfile(CustomCustomerProfileRecord profile)
        {
            return new CustomCustomerProfileRecord
            {
                label = profile?.label ?? string.Empty,
                weight = profile?.weight ?? 1f,
                budgetMin = profile?.budgetMin ?? 100,
                budgetMax = profile?.budgetMax ?? 400,
                queuePatienceMin = profile?.queuePatienceMin ?? 900,
                queuePatienceMax = profile?.queuePatienceMax ?? 3000,
                activeHourMin = profile?.activeHourMin ?? 0f,
                activeHourMax = profile?.activeHourMax ?? 24f,
                allowedWeatherDefNames = profile?.allowedWeatherDefNames?.ToList() ?? new List<string>(),
                preferredThingDefNames = profile?.preferredThingDefNames?.ToList() ?? new List<string>(),
                preferredGoodsCategoryIds = profile?.preferredGoodsCategoryIds?.ToList() ?? new List<string>()
            };
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
