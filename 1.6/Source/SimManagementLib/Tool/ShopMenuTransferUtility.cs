using SimManagementLib.Pojo;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 提供店铺套餐菜单的 Base64 导入导出，负责把运行时套餐转换为可分享文本并还原为安全数据。
    /// </summary>
    public static class ShopMenuTransferUtility
    {
        private static readonly DataContractJsonSerializer Serializer = new DataContractJsonSerializer(typeof(ShopMenuTransferPackage));

        /// <summary>
        /// 将套餐列表导出为 Base64 文本，负责过滤空套餐项和非法数量。
        /// </summary>
        public static string ExportBase64(IEnumerable<ComboData> combos)
        {
            ShopMenuTransferPackage package = BuildPackage(combos);
            using (MemoryStream stream = new MemoryStream())
            {
                Serializer.WriteObject(stream, package);
                return Convert.ToBase64String(stream.ToArray());
            }
        }

        /// <summary>
        /// 从 Base64 文本解析可导入套餐，负责报告缺失物品和被跳过的套餐数量。
        /// </summary>
        public static bool TryImportBase64(string base64, out ShopMenuImportResult result, out string error)
        {
            result = null;
            error = null;
            if (string.IsNullOrWhiteSpace(base64))
            {
                error = SimTranslation.TOrFallback("RSMF.Common.Import.EmptyContent", "导入内容为空。");
                return false;
            }

            try
            {
                byte[] bytes = Convert.FromBase64String(base64.Trim());
                using (MemoryStream stream = new MemoryStream(bytes))
                {
                    ShopMenuTransferPackage package = Serializer.ReadObject(stream) as ShopMenuTransferPackage;
                    result = BuildImportResult(package);
                    return true;
                }
            }
            catch (Exception ex)
            {
                error = SimTranslation.T("RSMF.Common.Import.ParseFailed", ex.Message.Named("message"));
                return false;
            }
        }

        /// <summary>
        /// 合并导入套餐到目标列表，负责保留现有套餐并为重名套餐生成唯一名称。
        /// </summary>
        public static List<ComboData> MergeImportedCombos(List<ComboData> target, IEnumerable<ComboData> imported)
        {
            List<ComboData> added = new List<ComboData>();
            if (target == null || imported == null)
                return added;

            HashSet<string> usedNames = new HashSet<string>(
                target.Where(combo => combo != null).Select(combo => NormalizeName(combo.comboName)),
                StringComparer.OrdinalIgnoreCase);

            foreach (ComboData source in imported)
            {
                ComboData clone = CloneCombo(source);
                if (clone == null || clone.items.NullOrEmpty())
                    continue;

                clone.comboName = MakeUniqueName(clone.comboName, usedNames);
                target.Add(clone);
                added.Add(clone);
            }

            return added;
        }

        /// <summary>
        /// 构建导出包，负责只保留可被稳定还原的套餐字段。
        /// </summary>
        private static ShopMenuTransferPackage BuildPackage(IEnumerable<ComboData> combos)
        {
            ShopMenuTransferPackage package = new ShopMenuTransferPackage();
            foreach (ComboData combo in combos ?? Enumerable.Empty<ComboData>())
            {
                ComboData sanitized = CloneCombo(combo);
                if (sanitized == null)
                    continue;

                ShopMenuTransferCombo record = new ShopMenuTransferCombo
                {
                    comboName = sanitized.comboName,
                    totalPrice = sanitized.totalPrice
                };

                for (int i = 0; i < sanitized.items.Count; i++)
                {
                    ComboItem item = sanitized.items[i];
                    record.items.Add(new ShopMenuTransferItem
                    {
                        thingDefName = item.def.defName,
                        count = item.count
                    });
                }

                if (record.items.Count > 0)
                    package.combos.Add(record);
            }

            return package;
        }

        /// <summary>
        /// 构建导入结果，负责把 DefName 解析为 ThingDef 并跳过无效条目。
        /// </summary>
        private static ShopMenuImportResult BuildImportResult(ShopMenuTransferPackage package)
        {
            ShopMenuImportResult result = new ShopMenuImportResult();
            if (package?.combos == null)
                return result;

            for (int i = 0; i < package.combos.Count; i++)
            {
                ShopMenuTransferCombo source = package.combos[i];
                ComboData combo = new ComboData
                {
                    comboName = NormalizeName(source?.comboName),
                    totalPrice = Math.Max(0f, source?.totalPrice ?? 0f),
                    items = new List<ComboItem>()
                };

                List<ShopMenuTransferItem> items = source?.items ?? new List<ShopMenuTransferItem>();
                for (int j = 0; j < items.Count; j++)
                {
                    ShopMenuTransferItem item = items[j];
                    ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(item?.thingDefName ?? "");
                    if (def == null)
                    {
                        if (!string.IsNullOrWhiteSpace(item?.thingDefName))
                            result.MissingThingDefNames.Add(item.thingDefName);
                        result.SkippedItemCount++;
                        continue;
                    }

                    combo.items.Add(new ComboItem
                    {
                        def = def,
                        count = Math.Max(1, item.count)
                    });
                }

                if (combo.items.Count == 0)
                {
                    result.SkippedComboCount++;
                    continue;
                }

                result.Combos.Add(combo);
            }

            return result;
        }

        /// <summary>
        /// 克隆并清理套餐，负责移除空物品和非法数量。
        /// </summary>
        private static ComboData CloneCombo(ComboData source)
        {
            if (source == null)
                return null;

            ComboData clone = new ComboData
            {
                comboName = NormalizeName(source.comboName),
                totalPrice = Math.Max(0f, source.totalPrice),
                items = new List<ComboItem>()
            };

            List<ComboItem> items = source.items ?? new List<ComboItem>();
            for (int i = 0; i < items.Count; i++)
            {
                ComboItem item = items[i];
                if (item?.def == null)
                    continue;

                clone.items.Add(new ComboItem
                {
                    def = item.def,
                    count = Math.Max(1, item.count)
                });
            }

            return clone;
        }

        /// <summary>
        /// 规范化套餐名，负责清理非法字符并为空名称提供统一文本。
        /// </summary>
        private static string NormalizeName(string name)
        {
            string value = StringEncodingUtility.SanitizeUtf16(name ?? "").Trim();
            return string.IsNullOrEmpty(value)
                ? SimTranslation.TOrFallback("RSMF.Common.UnnamedCombo", "未命名套餐")
                : value;
        }

        /// <summary>
        /// 生成不重复的套餐名称，负责合并导入时保留已有菜单。
        /// </summary>
        private static string MakeUniqueName(string name, HashSet<string> usedNames)
        {
            string baseName = NormalizeName(name);
            if (usedNames == null || usedNames.Add(baseName))
                return baseName;

            int suffix = 2;
            string candidate;
            do
            {
                candidate = baseName + " (" + suffix + ")";
                suffix++;
            }
            while (!usedNames.Add(candidate));

            return candidate;
        }
    }

    /// <summary>
    /// 保存套餐菜单导入结果，负责向 UI 汇报导入数量和跳过原因。
    /// </summary>
    public sealed class ShopMenuImportResult
    {
        public List<ComboData> Combos = new List<ComboData>();
        public HashSet<string> MissingThingDefNames = new HashSet<string>();
        public int SkippedComboCount;
        public int SkippedItemCount;
    }

    /// <summary>
    /// 保存 Base64 菜单包，负责作为 JSON 序列化根对象。
    /// </summary>
    [DataContract]
    internal sealed class ShopMenuTransferPackage
    {
        [DataMember] public int version = 1;
        [DataMember] public List<ShopMenuTransferCombo> combos = new List<ShopMenuTransferCombo>();
    }

    /// <summary>
    /// 保存单个套餐的可分享字段，负责避免导出运行时 ThingDef 对象。
    /// </summary>
    [DataContract]
    internal sealed class ShopMenuTransferCombo
    {
        [DataMember] public string comboName = "";
        [DataMember] public float totalPrice;
        [DataMember] public List<ShopMenuTransferItem> items = new List<ShopMenuTransferItem>();
    }

    /// <summary>
    /// 保存套餐中的单个商品项，负责通过 ThingDef 名称跨存档还原。
    /// </summary>
    [DataContract]
    internal sealed class ShopMenuTransferItem
    {
        [DataMember] public string thingDefName = "";
        [DataMember] public int count = 1;
    }
}
