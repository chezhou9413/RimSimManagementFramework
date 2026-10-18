using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SimManagementLib.Pojo
{
    public sealed class RuntimeItemPreference
    {
        public string preferredGoodsCategoryId;
        public ThingDef preferredThing;
        public string tag;
        public float weight = 1f;

        public bool Matches(ThingDef item)
        {
            if (item == null) return false;
            if (preferredThing != null && preferredThing == item) return true;
            return !string.IsNullOrEmpty(preferredGoodsCategoryId) && Tool.GoodsCatalog.Contains(preferredGoodsCategoryId, item);
        }

        public static List<RuntimeItemPreference> FromDefs(IEnumerable<SimDef.ItemPreference> defs)
        {
            if (defs == null) return new List<RuntimeItemPreference>();

            return defs
                .Where(p => p != null)
                .Select(p => new RuntimeItemPreference
                {
                    preferredGoodsCategoryId = p.GetPreferredGoodsCategoryIds().FirstOrDefault(),
                    preferredThing = p.preferredThing,
                    tag = p.tag,
                    weight = p.weight
                })
                .ToList();
        }

        /// <summary>
        /// 将玩家注册的偏好记录转换为运行时偏好条目。
        /// </summary>
        public static List<RuntimeItemPreference> FromCustomRecords(IEnumerable<CustomCustomerPreferenceRecord> records)
        {
            if (records == null) return new List<RuntimeItemPreference>();

            return records
                .Where(p => p != null)
                .Select(p => new RuntimeItemPreference
                {
                    preferredGoodsCategoryId = p.preferredGoodsCategoryId,
                    preferredThing = DefDatabase<ThingDef>.GetNamedSilentFail(p.preferredThingDefName),
                    tag = p.tag,
                    weight = p.weight
                })
                .Where(p => !string.IsNullOrEmpty(p.preferredGoodsCategoryId) || p.preferredThing != null)
                .ToList();
        }
    }
}
