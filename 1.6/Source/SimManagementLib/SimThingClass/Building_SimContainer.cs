using RimWorld;
using SimManagementLib.SimMapComp;
using SimManagementLib.SimThingComp;
using SimManagementLib.Tool;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimThingClass
{
    //虚拟化存储货柜，职责是保存商店商品库存、配置目标和补货预约。
    public partial class Building_SimContainer : Building, IThingHolder, IRenameable
    {
        private const int DefaultMaxTotalCapacity = 600;

        private ThingOwner<Thing> virtualStorage;
        private Dictionary<ThingDef, int> pendingIn = new Dictionary<ThingDef, int>();
        private Dictionary<ThingDef, int> pendingOut = new Dictionary<ThingDef, int>();
        private Dictionary<ThingDef, int> pendingInReservedAtTick = new Dictionary<ThingDef, int>();
        private Dictionary<ThingDef, int> storedCountCache = new Dictionary<ThingDef, int>();
        private string customName = "";
        private string lastPendingReservationDebug = "";
        private bool contentsDropped;
        private int cachedTotalStored;
        private bool storedCountCacheDirty = true;
        private int storedCountVersion;
        private int lastPendingReservationReconcileTick = int.MinValue;
        private const int PendingReservationReconcileIntervalTicks = 120;
        private const int PendingReservationGraceTicks = 180;

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
        public int StoredCountVersion => storedCountVersion;
        public string LastPendingReservationDebug => lastPendingReservationDebug ?? "";

        public int MaxTotalCapacity
        {
            get
            {
                int fromComp = GoodsComp?.MaxTotalCapacity ?? DefaultMaxTotalCapacity;
                return Mathf.Max(1, fromComp);
            }
        }

        //返回当前货柜用于视觉切换的库存占比，职责是按总容量计算阶段贴图比例。
        public float GetVisualFillPercent()
        {
            return Mathf.Clamp01(CountTotalStored() / (float)Mathf.Max(1, MaxTotalCapacity));
        }

        //优先返回进度阶段组件提供的贴图，职责是在未配置时回退到默认建筑贴图。
        public override Graphic Graphic => ProgressStageGraphicComp?.GetCurrentGraphic() ?? base.Graphic;

        //刷新货柜占用格网格，职责是在库存数量变化后立即切换阶段贴图。
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

        //复制当前虚拟库存的按物品统计，职责是让 UI 和统计逻辑不用直接枚举全部库存栈。
        public void CopyStoredCountsTo(Dictionary<ThingDef, int> target)
        {
            if (target == null) return;
            RebuildStoredCountCacheIfNeeded();
            target.Clear();
            foreach (KeyValuePair<ThingDef, int> entry in storedCountCache)
            {
                if (entry.Key != null && entry.Value > 0)
                    target[entry.Key] = entry.Value;
            }
        }

        //标记虚拟库存统计需要重建，职责是在入库、出库、购买和清空后同步缓存状态。
        private void MarkStoredCountCacheDirty()
        {
            storedCountCacheDirty = true;
            storedCountVersion++;
        }

        //按需重建虚拟库存统计，职责是把大量 Thing 栈聚合为按 ThingDef 查询的字典。
        private void RebuildStoredCountCacheIfNeeded()
        {
            if (!storedCountCacheDirty && storedCountCache != null)
                return;

            if (storedCountCache == null)
                storedCountCache = new Dictionary<ThingDef, int>();
            else
                storedCountCache.Clear();

            cachedTotalStored = 0;
            if (virtualStorage != null)
            {
                for (int i = 0; i < virtualStorage.Count; i++)
                {
                    Thing thing = virtualStorage[i];
                    if (thing == null || thing.Destroyed || thing.def == null || thing.stackCount <= 0) continue;
                    cachedTotalStored += Mathf.Max(0, thing.stackCount);
                    storedCountCache.TryGetValue(thing.def, out int current);
                    storedCountCache[thing.def] = current + thing.stackCount;
                }
            }

            storedCountCacheDirty = false;
        }

        //按固定间隔同步补货和下架预约，职责是避免 UI 每帧反复扫描地图 Pawn 任务。
        private void ReconcilePendingReservationsIfNeeded()
        {
            int ticks = Find.TickManager?.TicksGame ?? 0;
            if (lastPendingReservationReconcileTick >= 0 && ticks - lastPendingReservationReconcileTick < PendingReservationReconcileIntervalTicks)
                return;

            ReconcilePendingReservations();
        }

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
            MarkStoredCountCacheDirty();
            ReconcilePendingReservations();
            MapComponent_RestockTaskQueue queue = map?.GetComponent<MapComponent_RestockTaskQueue>();
            queue?.MarkStorageDirty(this, respawningAfterLoad ? "货柜读档生成" : "货柜生成");
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref virtualStorage, "virtualStorage", this);
            Scribe_Collections.Look(ref pendingIn, "pendingIn", LookMode.Def, LookMode.Value);
            Scribe_Collections.Look(ref pendingOut, "pendingOut", LookMode.Def, LookMode.Value);
            Scribe_Collections.Look(ref pendingInReservedAtTick, "pendingInReservedAtTick", LookMode.Def, LookMode.Value);
            Scribe_Values.Look(ref customName, "customName", "");
            Scribe_Values.Look(ref lastPendingReservationDebug, "lastPendingReservationDebug", "");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (virtualStorage == null)
                    virtualStorage = new ThingOwner<Thing>(this, oneStackOnly: false);
                if (pendingIn == null)
                    pendingIn = new Dictionary<ThingDef, int>();
                if (pendingOut == null)
                    pendingOut = new Dictionary<ThingDef, int>();
                if (pendingInReservedAtTick == null)
                    pendingInReservedAtTick = new Dictionary<ThingDef, int>();
                if (storedCountCache == null)
                    storedCountCache = new Dictionary<ThingDef, int>();
                if (lastPendingReservationDebug == null)
                    lastPendingReservationDebug = "";
                pendingIn.Clear();
                pendingInReservedAtTick.Clear();
                MarkStoredCountCacheDirty();
            }
        }

        //标记当前货柜补货队列需要重算，职责是把库存、预约和配置变化通知地图队列。
        public void MarkRestockQueueDirty(ThingDef thingDef, string reason)
        {
            if (!Spawned || Map == null)
                return;

            MapComponent_RestockTaskQueue queue = Map.GetComponent<MapComponent_RestockTaskQueue>();
            if (thingDef != null)
                queue?.MarkDirty(this, thingDef, reason);
            else
                queue?.MarkStorageDirty(this, reason);
        }
    }
}
