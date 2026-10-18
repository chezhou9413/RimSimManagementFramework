using SimManagementLib.SimDef;
using System.Collections.Generic;
using Verse;

namespace SimManagementLib.Pojo
{
    public sealed class RuntimeGoodsCategory
    {
        public string categoryId;
        public string label;
        public GoodsDef sourceDef;
        public bool hasPlayerDefinedConfig;

        private readonly List<RuntimeGoodsItem> items = new List<RuntimeGoodsItem>();
        private readonly HashSet<string> thingDefNames = new HashSet<string>();
        private readonly HashSet<string> playerDefinedThingDefNames = new HashSet<string>();

        public IReadOnlyList<RuntimeGoodsItem> Items => items;
        public bool IsBuiltInCategory => sourceDef != null;
        public bool IsPlayerCategory => sourceDef == null && hasPlayerDefinedConfig;

        public bool Contains(ThingDef thingDef)
        {
            return thingDef != null && thingDefNames.Contains(thingDef.defName);
        }

        public bool IsPlayerDefinedItem(ThingDef thingDef)
        {
            return thingDef != null && playerDefinedThingDefNames.Contains(thingDef.defName);
        }

        internal bool TryAdd(RuntimeGoodsItem item, bool playerDefined)
        {
            if (item?.thingDef == null) return false;
            if (!thingDefNames.Add(item.thingDef.defName)) return false;
            items.Add(item);
            if (playerDefined)
                playerDefinedThingDefNames.Add(item.thingDef.defName);
            return true;
        }

        internal void Clear()
        {
            items.Clear();
            thingDefNames.Clear();
            playerDefinedThingDefNames.Clear();
            hasPlayerDefinedConfig = false;
        }
    }
}
