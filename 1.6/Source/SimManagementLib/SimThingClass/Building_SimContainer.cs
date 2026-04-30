using RimWorld;
using SimManagementLib.SimThingComp;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimThingClass
{
    /// <summary>
    /// 虚拟化存储货柜。
    /// </summary>
    public partial class Building_SimContainer : Building, IThingHolder, IRenameable
    {
        private const int DefaultMaxTotalCapacity = 600;

        private ThingOwner<Thing> virtualStorage;
        private Dictionary<ThingDef, int> pendingIn = new Dictionary<ThingDef, int>();
        private Dictionary<ThingDef, int> pendingOut = new Dictionary<ThingDef, int>();
        private string customName = "";
        private bool contentsDropped;

        private ThingComp_GoodsData GoodsComp => GetComp<ThingComp_GoodsData>();

        public string RenamableLabel
        {
            get => string.IsNullOrWhiteSpace(customName) ? BaseLabel : customName;
            set => customName = value?.Trim() ?? "";
        }

        public string BaseLabel => def?.label?.CapitalizeFirst() ?? "货柜";
        public string InspectLabel => StorageDisplayLabel;

        public string StorageDisplayLabel
        {
            get
            {
                if (string.IsNullOrWhiteSpace(customName))
                    return BaseLabel;
                return $"{customName} ({BaseLabel})";
            }
        }

        public override string LabelNoCount => StorageDisplayLabel;

        public int MaxTotalCapacity
        {
            get
            {
                int fromComp = GoodsComp?.MaxTotalCapacity ?? DefaultMaxTotalCapacity;
                return Mathf.Max(1, fromComp);
            }
        }

        public ThingOwner GetDirectlyHeldThings() => virtualStorage;

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            contentsDropped = false;
            if (virtualStorage == null)
                virtualStorage = new ThingOwner<Thing>(this, oneStackOnly: false);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref virtualStorage, "virtualStorage", this);
            Scribe_Collections.Look(ref pendingIn, "pendingIn", LookMode.Def, LookMode.Value);
            Scribe_Collections.Look(ref pendingOut, "pendingOut", LookMode.Def, LookMode.Value);
            Scribe_Values.Look(ref customName, "customName", "");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (virtualStorage == null)
                    virtualStorage = new ThingOwner<Thing>(this, oneStackOnly: false);
                if (pendingIn == null)
                    pendingIn = new Dictionary<ThingDef, int>();
                if (pendingOut == null)
                    pendingOut = new Dictionary<ThingDef, int>();
            }
        }
    }
}
