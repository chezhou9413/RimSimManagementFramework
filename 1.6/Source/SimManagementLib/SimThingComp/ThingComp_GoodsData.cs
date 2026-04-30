using SimManagementLib.SimDef;
using SimManagementLib.SimThingClass;
using SimManagementLib.Tool;
using System;
using System.Collections.Generic;
using Verse;

namespace SimManagementLib.SimThingComp
{
    public class ThingCompProperties_GoodsData : CompProperties
    {
        // 货柜总容量上限（按件数，跨所有商品共享）
        public int maxTotalCapacity = 600;

        public ThingCompProperties_GoodsData()
        {
            compClass = typeof(ThingComp_GoodsData);
        }
    }

    public class ThingComp_GoodsData : ThingComp
    {
        public string ActiveGoodsDefName = "";

        // 每种物品的配置，key = ThingDef.defName
        public Dictionary<string, GoodsItemData> itemData = new Dictionary<string, GoodsItemData>();

        [NonSerialized] public Dictionary<string, string> countBuffers = new Dictionary<string, string>();
        [NonSerialized] public Dictionary<string, string> priceBuffers = new Dictionary<string, string>();

        public int MaxTotalCapacity
        {
            get
            {
                ThingCompProperties_GoodsData p = props as ThingCompProperties_GoodsData;
                int value = p?.maxTotalCapacity ?? 600;
                return Math.Max(1, value);
            }
        }

        public GoodsDef ActiveGoodsDef
        {
            get
            {
                return GoodsCatalog.GetSourceDef(ActiveGoodsDefName);
            }
        }

        public GoodsItemData GetOrCreate(ThingDef td)
        {
            if (!itemData.TryGetValue(td.defName, out var d))
                itemData[td.defName] = d = new GoodsItemData();
            return d;
        }

        public GoodsItemData FindItemData(ThingDef td)
        {
            if (!itemData.TryGetValue(td.defName, out var d)) return null;
            if (!d.enabled) return null;
            return d;
        }

        // ── 草稿与应用逻辑（UI隔离使用） ──
        public Dictionary<string, GoodsItemData> CloneItemData()
        {
            var dict = new Dictionary<string, GoodsItemData>();
            foreach (var kv in itemData)
            {
                dict[kv.Key] = new GoodsItemData
                {
                    enabled = kv.Value.enabled,
                    count = kv.Value.count,
                    price = kv.Value.price
                };
            }
            return dict;
        }

        public void ApplySettings(string newDefName, Dictionary<string, GoodsItemData> newSettings)
        {
            ActiveGoodsDefName = newDefName ?? "";
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
        }

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
                    price = Math.Max(0f, s?.price ?? 0f)
                };
            }

            return result;
        }

        public override void PostExposeData()
        {
            Scribe_Values.Look(ref ActiveGoodsDefName, "activeGoodsDefName", "");
            Scribe_Collections.Look(ref itemData, "itemData", LookMode.Value, LookMode.Deep);
            if (itemData == null) itemData = new Dictionary<string, GoodsItemData>();
        }
    }

    public class GoodsItemData : IExposable
    {
        public bool enabled = false;
        public int count = 1;
        public float price = 0f;

        [NonSerialized] public string countBuffer;
        [NonSerialized] public string priceBuffer;

        public void ExposeData()
        {
            Scribe_Values.Look(ref enabled, "enabled", false);
            Scribe_Values.Look(ref count, "count", 1);
            Scribe_Values.Look(ref price, "price", 0f);
        }
    }
}
