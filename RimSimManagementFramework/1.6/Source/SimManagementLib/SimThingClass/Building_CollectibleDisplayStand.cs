using System.Collections.Generic;
using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.SimThingComp;
using SimManagementLib.Tool;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimThingClass
{
    /// <summary>
    /// 收藏品展台建筑，职责是保存槽位收藏品、提供管理入口、渲染展示物并处理卸载和销毁。
    /// </summary>
    public partial class Building_CollectibleDisplayStand : Building, IThingHolder
    {
        private List<CollectibleDisplaySlotData> slots = new List<CollectibleDisplaySlotData>();
        private ThingOwner<Thing> emptyRootContainer;

        private ThingComp_CollectibleDisplayStand ConfigComp => GetComp<ThingComp_CollectibleDisplayStand>();
        public int Rows => ConfigComp?.Rows ?? 5;
        public int Columns => ConfigComp?.Columns ?? 5;
        public IReadOnlyList<CollectibleDisplaySlotData> Slots => slots;

        /// <summary>
        /// 返回当前已经展示的收藏品数量，供服务价格和 UI 状态使用。
        /// </summary>
        public int DisplayedCollectibleCount
        {
            get
            {
                EnsureSlots();
                int count = 0;
                for (int i = 0; i < slots.Count; i++)
                {
                    if (slots[i]?.HasStoredThing == true)
                        count++;
                }
                return count;
            }
        }

        /// <summary>
        /// 返回第一个槽位容器作为直接持有容器；真实子容器由 GetChildHolders 递归暴露。
        /// </summary>
        public ThingOwner GetDirectlyHeldThings()
        {
            if (emptyRootContainer == null)
                emptyRootContainer = new ThingOwner<Thing>(this, oneStackOnly: false);
            return emptyRootContainer;
        }

        /// <summary>
        /// 收集所有槽位容器，职责是让游戏保存和追踪槽位内收藏品。
        /// </summary>
        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            EnsureSlots();
            for (int i = 0; i < slots.Count; i++)
                outChildren.Add(slots[i]);
        }

        /// <summary>
        /// 建筑生成后补齐槽位并刷新运行时父容器引用。
        /// </summary>
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            EnsureSlots();
        }

        /// <summary>
        /// 保存或读取展台槽位列表。
        /// </summary>
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref slots, "collectibleDisplaySlots", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                EnsureSlots();
        }

        /// <summary>
        /// 销毁展台前处理槽位内物品，职责是避免收藏品被吞掉。
        /// </summary>
        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            if (!Destroyed)
                DropOrDestroyAllStored(mode);
            base.Destroy(mode);
        }

        /// <summary>
        /// 判断指定来源是否已经被任意槽位等待填充。
        /// </summary>
        public bool HasPendingSource(int sourceThingId)
        {
            EnsureSlots();
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].pendingSourceThingId == sourceThingId)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 尝试设置槽位待搬运来源，职责是接受 UI 选择并刷新工作扫描状态。
        /// </summary>
        public bool TrySetPendingSource(int slotIndex, Thing source, out string error)
        {
            error = "";
            CollectibleDisplaySlotData slot = GetSlot(slotIndex);
            if (slot == null)
            {
                error = SimTranslation.T("RSMF.CollectibleDisplayStand.Error.InvalidSlot");
                return false;
            }

            if (slot.HasStoredThing)
            {
                error = SimTranslation.T("RSMF.CollectibleDisplayStand.Error.SlotOccupied");
                return false;
            }

            if (!CollectibleDisplayStandUtility.IsValidSourceThing(source))
            {
                error = SimTranslation.T("RSMF.CollectibleDisplayStand.Error.InvalidSource");
                return false;
            }

            slot.SetPendingSource(source);
            RefreshDisplayMesh();
            return true;
        }

        /// <summary>
        /// 查找第一个可执行填充工作的槽位和来源。
        /// </summary>
        public bool TryFindFillTarget(Pawn pawn, out int slotIndex, out Thing source)
        {
            EnsureSlots();
            slotIndex = -1;
            source = null;

            for (int i = 0; i < slots.Count; i++)
            {
                CollectibleDisplaySlotData slot = slots[i];
                if (slot == null || slot.HasStoredThing || !slot.HasPendingSource)
                    continue;

                Thing candidate = CollectibleDisplayStandUtility.FindSourceById(Map, slot.pendingSourceThingId);
                if (candidate == null)
                {
                    slot.ClearPendingSource();
                    continue;
                }

                if (!CollectibleDisplayStandUtility.CanPawnUseSource(pawn, this, candidate))
                    continue;

                slotIndex = i;
                source = candidate;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 把小人携带的缩小收藏品安装到指定槽位。
        /// </summary>
        public bool TryInstallFromPawnCarry(Pawn pawn, int slotIndex)
        {
            CollectibleDisplaySlotData slot = GetSlot(slotIndex);
            Thing carried = pawn?.carryTracker?.CarriedThing;
            Thing inner = CollectibleDisplayStandUtility.GetCollectibleInnerThing(carried);
            if (slot == null || slot.HasStoredThing || !CollectibleDisplayStandUtility.IsCollectibleDef(inner?.def))
                return false;

            bool stored = slot.TryStore(inner);
            if (!stored)
                return false;

            if (carried is MinifiedThing minified)
            {
                pawn.carryTracker.innerContainer.Remove(minified);
                minified.Destroy(DestroyMode.Vanish);
            }

            RefreshDisplayMesh();
            return true;
        }

        /// <summary>
        /// 卸载指定槽位的收藏品，职责是以缩小物形式弹出到展台附近。
        /// </summary>
        public bool TryUnloadSlot(int slotIndex)
        {
            CollectibleDisplaySlotData slot = GetSlot(slotIndex);
            if (slot == null || !slot.HasStoredThing || Map == null)
                return false;

            Thing stored = slot.TakeStoredThing();
            if (stored == null)
                return false;

            Thing dropThing = stored.def.Minifiable ? stored.MakeMinified() : stored;
            bool placed = GenPlace.TryPlaceThing(dropThing, Position, Map, ThingPlaceMode.Near);
            if (!placed && !dropThing.Destroyed)
                dropThing.Destroy(DestroyMode.Vanish);

            RefreshDisplayMesh();
            return placed;
        }

        /// <summary>
        /// 根据索引读取槽位，职责是统一边界检查。
        /// </summary>
        public CollectibleDisplaySlotData GetSlot(int slotIndex)
        {
            EnsureSlots();
            if (slotIndex < 0 || slotIndex >= slots.Count)
                return null;
            return slots[slotIndex];
        }

        /// <summary>
        /// 刷新展台占用格的地图网格，职责是让实时绘制和选择轮廓立即更新。
        /// </summary>
        public void RefreshDisplayMesh()
        {
            if (!Spawned || Map == null)
                return;

            CellRect rect = GenAdj.OccupiedRect(Position, Rotation, def.Size);
            foreach (IntVec3 cell in rect)
                Map.mapDrawer.MapMeshDirty(cell, (ulong)MapMeshFlagDefOf.Buildings | (ulong)MapMeshFlagDefOf.Things);
        }

        /// <summary>
        /// 补齐并绑定槽位数据，职责是支持 XML 行列变化和旧存档读取。
        /// </summary>
        private void EnsureSlots()
        {
            if (slots == null)
                slots = new List<CollectibleDisplaySlotData>();

            int targetCount = Mathf.Max(1, Rows * Columns);
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] == null)
                    slots[i] = CreateDefaultSlot(i);
                slots[i].index = i;
                slots[i].BindParent(this);
            }

            while (slots.Count < targetCount)
            {
                CollectibleDisplaySlotData slot = CreateDefaultSlot(slots.Count);
                slot.BindParent(this);
                slots.Add(slot);
            }

            TrimEmptyOverflowSlots(targetCount);
        }

        /// <summary>
        /// 按 XML 配置创建槽位默认展示参数。
        /// </summary>
        private CollectibleDisplaySlotData CreateDefaultSlot(int index)
        {
            CollectibleDisplaySlotData slot = new CollectibleDisplaySlotData(index, Rows, Columns);
            ConfigComp?.ApplyDefaultTransform(slot);
            return slot;
        }

        /// <summary>
        /// 把指定槽位重置为 XML 配置的默认展示参数。
        /// </summary>
        public void ResetSlotToConfiguredDefault(CollectibleDisplaySlotData slot)
        {
            if (slot == null)
                return;

            ConfigComp?.ApplyDefaultTransform(slot);
            RefreshDisplayMesh();
        }

        /// <summary>
        /// 移除 XML 行列减少后产生的空溢出槽，职责是迁移旧默认 25 槽建筑且不吞掉已存物品。
        /// </summary>
        private void TrimEmptyOverflowSlots(int targetCount)
        {
            for (int i = slots.Count - 1; i >= targetCount; i--)
            {
                CollectibleDisplaySlotData slot = slots[i];
                if (slot != null && (slot.HasStoredThing || slot.HasPendingSource))
                    continue;

                slots.RemoveAt(i);
            }
        }

        /// <summary>
        /// 根据销毁方式掉落或清理所有槽位收藏品。
        /// </summary>
        private void DropOrDestroyAllStored(DestroyMode mode)
        {
            EnsureSlots();
            for (int i = 0; i < slots.Count; i++)
            {
                Thing stored = slots[i].TakeStoredThing();
                if (stored == null)
                    continue;

                if ((mode == DestroyMode.Deconstruct || mode == DestroyMode.KillFinalize) && Spawned && Map != null)
                {
                    Thing dropThing = stored.def.Minifiable ? stored.MakeMinified() : stored;
                    if (!GenPlace.TryPlaceThing(dropThing, Position, Map, ThingPlaceMode.Near) && !dropThing.Destroyed)
                        dropThing.Destroy(DestroyMode.Vanish);
                }
                else if (!stored.Destroyed)
                {
                    stored.Destroy(mode);
                }
            }
        }
    }
}
