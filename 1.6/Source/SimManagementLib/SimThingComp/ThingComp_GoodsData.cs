using SimManagementLib.SimDef;
using SimManagementLib.SimThingClass;
using SimManagementLib.Tool;
using System;
using System.Collections.Generic;
using Verse;

namespace SimManagementLib.SimThingComp
{
    /// <summary>
    /// 定义货柜商品数据组件的容量和可售分类限制参数。
    /// </summary>
    public class ThingCompProperties_GoodsData : CompProperties
    {
        // 货柜总容量上限（按件数，跨所有商品共享）
        public int maxTotalCapacity = 600;

        // 允许该货柜选择的商品分类 ID。为空时表示通用货柜，允许全部商品分类。
        public List<string> allowedGoodsCategoryIds = new List<string>();

        // 允许该货柜选择的 GoodsDef 分类。用于 XML 里直接写 Def 引用，与 allowedGoodsCategoryIds 合并生效。
        public List<GoodsDef> allowedGoodsCategories = new List<GoodsDef>();

        /// <summary>
        /// 初始化组件类型，供 RimWorld 根据 XML 创建组件实例。
        /// </summary>
        public ThingCompProperties_GoodsData()
        {
            compClass = typeof(ThingComp_GoodsData);
        }
    }

    /// <summary>
    /// 保存货柜可售商品配置，并提供容量和分类限制判断。
    /// </summary>
    public class ThingComp_GoodsData : ThingComp
    {
        public string ActiveGoodsDefName = "";

        // 每种物品的配置，key = ThingDef.defName
        public Dictionary<string, GoodsItemData> itemData = new Dictionary<string, GoodsItemData>();

        [NonSerialized] public Dictionary<string, string> countBuffers = new Dictionary<string, string>();
        [NonSerialized] public Dictionary<string, string> priceBuffers = new Dictionary<string, string>();
        [NonSerialized] public Dictionary<string, string> restockThresholdBuffers = new Dictionary<string, string>();

        private ThingCompProperties_GoodsData GoodsProps => props as ThingCompProperties_GoodsData;

        public int MaxTotalCapacity
        {
            get
            {
                ThingCompProperties_GoodsData p = GoodsProps;
                int value = p?.maxTotalCapacity ?? 600;
                return Math.Max(1, value);
            }
        }

        /// <summary>
        /// 获取当前选中的内置 GoodsDef；自定义运行时分类会返回 null。
        /// </summary>
        public GoodsDef ActiveGoodsDef
        {
            get
            {
                return GoodsCatalog.GetSourceDef(ActiveGoodsDefName);
            }
        }

        /// <summary>
        /// 判断该货柜是否配置了可售分类限制。
        /// </summary>
        public bool HasGoodsCategoryRestriction => GetAllowedGoodsCategoryIds().Count > 0;

        /// <summary>
        /// 返回合并后的可售分类 ID 列表，去除空值和重复项。
        /// </summary>
        public List<string> GetAllowedGoodsCategoryIds()
        {
            List<string> result = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ThingCompProperties_GoodsData p = GoodsProps;
            if (p == null) return result;

            if (p.allowedGoodsCategoryIds != null)
            {
                for (int i = 0; i < p.allowedGoodsCategoryIds.Count; i++)
                    TryAddAllowedCategoryId(result, seen, p.allowedGoodsCategoryIds[i]);
            }

            if (p.allowedGoodsCategories != null)
            {
                for (int i = 0; i < p.allowedGoodsCategories.Count; i++)
                    TryAddAllowedCategoryId(result, seen, p.allowedGoodsCategories[i]?.defName);
            }

            return result;
        }

        /// <summary>
        /// 判断指定商品分类是否允许在该货柜中售卖；没有限制时始终允许。
        /// </summary>
        public bool AllowsGoodsCategory(string categoryId)
        {
            if (string.IsNullOrEmpty(categoryId)) return !HasGoodsCategoryRestriction;

            List<string> allowed = GetAllowedGoodsCategoryIds();
            if (allowed.Count <= 0) return true;

            for (int i = 0; i < allowed.Count; i++)
            {
                if (string.Equals(allowed[i], categoryId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 获取可售分类限制的显示文本，用于提示玩家该货柜能选择哪些分类。
        /// </summary>
        public string GetAllowedGoodsCategoryLabelSummary()
        {
            List<string> allowed = GetAllowedGoodsCategoryIds();
            if (allowed.Count <= 0) return SimTranslation.T("RSMF.GoodsManager.AllGoodsCategories");

            List<string> labels = new List<string>();
            for (int i = 0; i < allowed.Count; i++)
            {
                string id = allowed[i];
                string label = GoodsCatalog.GetCategory(id)?.label;
                labels.Add(string.IsNullOrEmpty(label) ? id : label);
            }

            return string.Join(SimTranslation.T("RSMF.Common.ListSeparator"), labels);
        }

        /// <summary>
        /// 获取或创建指定物品的配置数据。
        /// </summary>
        public GoodsItemData GetOrCreate(ThingDef td)
        {
            if (!itemData.TryGetValue(td.defName, out var d))
                itemData[td.defName] = d = new GoodsItemData();
            return d;
        }

        /// <summary>
        /// 查找指定物品的有效配置；未启用或分类不允许时返回 null。
        /// </summary>
        public GoodsItemData FindItemData(ThingDef td)
        {
            if (!AllowsGoodsCategory(ActiveGoodsDefName)) return null;
            if (!itemData.TryGetValue(td.defName, out var d)) return null;
            if (!d.enabled) return null;
            return d;
        }

        /// <summary>
        /// 克隆当前商品配置，供 UI 使用草稿数据隔离编辑。
        /// </summary>
        public Dictionary<string, GoodsItemData> CloneItemData()
        {
            var dict = new Dictionary<string, GoodsItemData>();
            foreach (var kv in itemData)
            {
                dict[kv.Key] = new GoodsItemData
                {
                    enabled = kv.Value.enabled,
                    count = kv.Value.count,
                    price = kv.Value.price,
                    restockThreshold = GoodsItemData.NormalizeRestockThreshold(kv.Value.restockThreshold, kv.Value.count)
                };
            }
            return dict;
        }

        /// <summary>
        /// 应用 UI 草稿配置，并按货柜容量和分类限制过滤无效数据。
        /// </summary>
        public void ApplySettings(string newDefName, Dictionary<string, GoodsItemData> newSettings)
        {
            ActiveGoodsDefName = newDefName ?? "";
            if (!AllowsGoodsCategory(ActiveGoodsDefName))
            {
                ActiveGoodsDefName = "";
                itemData = new Dictionary<string, GoodsItemData>();
                countBuffers.Clear();
                priceBuffers.Clear();
                restockThresholdBuffers.Clear();
                return;
            }

            if (parent is Building_SimContainer storage)
            {
                itemData = storage.ClampSettingsToCapacity(ActiveGoodsDefName, newSettings, out _);
            }
            else
            {
                itemData = CloneSettings(newSettings);
            }

            countBuffers.Clear();
            priceBuffers.Clear();
            restockThresholdBuffers.Clear();
        }

        /// <summary>
        /// 克隆传入配置字典，并规范化数量与价格的最小值。
        /// </summary>
        private static Dictionary<string, GoodsItemData> CloneSettings(Dictionary<string, GoodsItemData> source)
        {
            var result = new Dictionary<string, GoodsItemData>();
            if (source == null) return result;

            foreach (KeyValuePair<string, GoodsItemData> kv in source)
            {
                GoodsItemData s = kv.Value;
                result[kv.Key] = new GoodsItemData
                {
                    enabled = s?.enabled ?? false,
                    count = Math.Max(0, s?.count ?? 0),
                    price = Math.Max(0f, s?.price ?? 0f),
                    restockThreshold = GoodsItemData.NormalizeRestockThreshold(s?.restockThreshold ?? -1, Math.Max(0, s?.count ?? 0))
                };
            }

            return result;
        }

        /// <summary>
        /// 将非空分类 ID 加入限制列表，并保持大小写不敏感去重。
        /// </summary>
        private static void TryAddAllowedCategoryId(List<string> result, HashSet<string> seen, string categoryId)
        {
            if (string.IsNullOrWhiteSpace(categoryId)) return;
            string trimmed = categoryId.Trim();
            if (!seen.Add(trimmed)) return;
            result.Add(trimmed);
        }

        /// <summary>
        /// 保存或读取货柜商品配置数据。
        /// </summary>
        public override void PostExposeData()
        {
            Scribe_Values.Look(ref ActiveGoodsDefName, "activeGoodsDefName", "");
            Scribe_Collections.Look(ref itemData, "itemData", LookMode.Value, LookMode.Deep);
            if (itemData == null) itemData = new Dictionary<string, GoodsItemData>();
        }
    }

    /// <summary>
    /// 保存单个商品的启用状态、目标数量、补货阈值和售价配置。
    /// </summary>
    public class GoodsItemData : IExposable
    {
        public bool enabled = false;
        public int count = 1;
        public float price = 0f;
        public int restockThreshold = -1;

        [NonSerialized] public string countBuffer;
        [NonSerialized] public string priceBuffer;
        [NonSerialized] public string restockThresholdBuffer;

        // 返回实际生效的补货触发阈值，负责让未配置阈值的商品默认补到目标量。
        public int EffectiveRestockThreshold => GetEffectiveRestockThreshold(count, restockThreshold);

        // 根据目标量和保存值计算有效阈值，负责兼容旧存档未保存阈值的情况。
        public static int GetEffectiveRestockThreshold(int targetCount, int configuredThreshold)
        {
            int target = Math.Max(0, targetCount);
            if (target <= 0) return 0;
            int fallback = target;
            return Math.Max(0, Math.Min(target, configuredThreshold < 0 ? fallback : configuredThreshold));
        }

        // 规范化保存的补货阈值，负责避免阈值超过目标量或出现非法负数。
        public static int NormalizeRestockThreshold(int configuredThreshold, int targetCount)
        {
            return GetEffectiveRestockThreshold(targetCount, configuredThreshold);
        }

        /// <summary>
        /// 保存或读取单个商品的货柜配置。
        /// </summary>
        public void ExposeData()
        {
            Scribe_Values.Look(ref enabled, "enabled", false);
            Scribe_Values.Look(ref count, "count", 1);
            Scribe_Values.Look(ref price, "price", 0f);
            Scribe_Values.Look(ref restockThreshold, "restockThreshold", -1);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                count = Math.Max(0, count);
                price = Math.Max(0f, price);
                restockThreshold = NormalizeRestockThreshold(restockThreshold, count);
            }
        }
    }
}
