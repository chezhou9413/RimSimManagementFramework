using RimWorld;
using SimManagementLib.SimThingComp;
using SimManagementLib.Tool;
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
        private ThingComp_ProgressStageGraphic ProgressStageGraphicComp => GetComp<ThingComp_ProgressStageGraphic>();

        public string RenamableLabel
        {
            get => string.IsNullOrWhiteSpace(customName) ? BaseLabel : customName;
            set => customName = value?.Trim() ?? "";
        }

        public string BaseLabel => def?.label?.CapitalizeFirst() ?? SimTranslation.T("RSMF.Container.DefaultLabel");
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

        /// <summary>
        /// 返回当前货柜用于视觉切换的库存占比。
        /// 这里固定按总容量计算，避免受到当前商品配置目标数量影响。
        /// </summary>
        public float GetVisualFillPercent()
        {
            return Mathf.Clamp01(CountTotalStored() / (float)Mathf.Max(1, MaxTotalCapacity));
        }

        /// <summary>
        /// 优先返回进度阶段组件提供的贴图，未配置时回退到默认建筑贴图。
        /// </summary>
        public override Graphic Graphic => ProgressStageGraphicComp?.GetCurrentGraphic() ?? base.Graphic;

        /// <summary>
        /// 在库存数量变化后刷新货柜占用格的网格，让阶段贴图立即切换。
        /// </summary>
        public void RefreshProgressStageGraphic()
        {
            if (!Spawned || Map == null)
                return;

            CellRect occupiedRect = GenAdj.OccupiedRect(Position, Rotation, def.Size);
            for (int z = occupiedRect.minZ; z <= occupiedRect.maxZ; z++)
            {
                for (int x = occupiedRect.minX; x <= occupiedRect.maxX; x++)
                {
                    IntVec3 cell = new IntVec3(x, 0, z);
                    Map.mapDrawer.MapMeshDirty(cell, (ulong)MapMeshFlagDefOf.Buildings | (ulong)MapMeshFlagDefOf.Things);
                }
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
            ReconcilePendingReservations();
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
