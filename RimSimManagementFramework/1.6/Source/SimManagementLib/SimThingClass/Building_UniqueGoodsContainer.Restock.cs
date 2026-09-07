using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.SimMapComp;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimThingClass
{
    //专业货柜补货模块，职责是维护玩家精确来源指派并把搬运实物安装到对应槽位。
    public partial class Building_UniqueGoodsContainer
    {
        //将地图上的具体物品加入第一个兼容空槽的补货请求。
        public bool TryQueueSource(Thing source, out string reason)
        {
            reason = "";
            EnsureUniqueSlots();
            if (!IsEligibleUniqueThing(source) || !source.Spawned || source.Map != Map)
            {
                reason = "该物品不符合专业货柜上架条件。";
                return false;
            }
            if (IsSourceAssignedAnywhere(source.thingIDNumber))
            {
                reason = "该物品已经被其他槽位指定。";
                return false;
            }
            UniqueGoodsSlotData slot = uniqueSlots.FirstOrDefault(candidate => candidate != null
                && !candidate.IsOccupied
                && IsEligibleUniqueThingForSlot(source, candidate.index));
            if (slot == null)
            {
                reason = "专业货柜没有可接收该物品的空槽。";
                return false;
            }

            slot.pendingSourceThingId = source.thingIDNumber;
            slot.pendingSourceLabel = source.LabelCapNoCount;
            slot.price = Mathf.Max(1f, source.MarketValue);
            MapComponent_RestockTaskQueue queue = Map?.GetComponent<MapComponent_RestockTaskQueue>();
            queue?.RememberUniqueSource(source);
            NotifyUniqueSlotsChanged("玩家指定专业货源");
            return true;
        }

        //判断来源是否已被地图协调器中的任一专业槽位指派。
        public bool IsSourceAssignedAnywhere(int sourceThingId)
        {
            if (sourceThingId < 0)
                return false;
            MapComponent_RestockTaskQueue queue = Map?.GetComponent<MapComponent_RestockTaskQueue>();
            return queue != null && queue.IsUniqueSourceAssigned(sourceThingId);
        }

        //查找一个当前可执行的精确来源，暂时不可用时始终保留玩家指派。
        public bool TryFindPendingSource(Pawn pawn, out UniqueGoodsSlotData slot, out Thing source)
        {
            EnsureUniqueSlots();
            MapComponent_RestockTaskQueue queue = Map?.GetComponent<MapComponent_RestockTaskQueue>();
            for (int i = 0; i < uniqueSlots.Count; i++)
            {
                UniqueGoodsSlotData candidate = uniqueSlots[i];
                if (candidate == null || !candidate.HasPendingSource)
                    continue;
                bool confirmedDestroyed = false;
                Thing found = queue?.ResolveUniqueSource(candidate.pendingSourceThingId, out confirmedDestroyed);
                if (confirmedDestroyed)
                {
                    ClearDestroyedPendingSource(i, candidate.pendingSourceThingId);
                    continue;
                }
                if (found == null || !IsEligibleUniqueThingForSlot(found, candidate.index)
                    || !found.Spawned || found.Map != Map)
                    continue;
                if (pawn == null || found.IsForbidden(pawn) || !pawn.CanReserve(found)
                    || !pawn.CanReach(found, PathEndMode.ClosestTouch, Danger.Deadly)
                    || !pawn.CanReach(this, PathEndMode.Touch, Danger.Deadly))
                    continue;
                slot = candidate;
                source = found;
                return true;
            }
            slot = null;
            source = null;
            return false;
        }

        //查找精确来源当前对应的等待槽位索引。
        public bool TryGetPendingSlotIndex(int sourceThingId, out int slotIndex)
        {
            EnsureUniqueSlots();
            for (int i = 0; i < uniqueSlots.Count; i++)
            {
                if (uniqueSlots[i]?.pendingSourceThingId != sourceThingId)
                    continue;
                slotIndex = i;
                return true;
            }
            slotIndex = -1;
            return false;
        }

        //判断指定槽位是否仍等待同一个精确来源。
        public bool IsPendingSlotMatch(int slotIndex, int sourceThingId)
        {
            UniqueGoodsSlotData slot = GetUniqueSlot(slotIndex);
            return slot != null && slot.pendingSourceThingId == sourceThingId;
        }

        //判断解析出的真实来源是否仍符合原指派槽位的接收条件。
        public bool IsEligiblePendingSource(int slotIndex, Thing source)
        {
            return source != null && IsPendingSlotMatch(slotIndex, source.thingIDNumber)
                && IsEligibleUniqueThingForSlot(source, slotIndex);
        }

        //确认来源已销毁后清除对应指派，其他暂时不可用状态不会调用此入口。
        public void ClearDestroyedPendingSource(int slotIndex, int sourceThingId)
        {
            UniqueGoodsSlotData slot = GetUniqueSlot(slotIndex);
            if (slot == null || slot.pendingSourceThingId != sourceThingId)
                return;
            slot.pendingSourceThingId = -1;
            slot.pendingSourceLabel = "";
            NotifyUniqueSlotsChanged("专业货源确认销毁");
        }

        //把搬运者携带的真实物品存入其指定槽位。
        public bool TryInstallFromCarry(Pawn pawn, int sourceThingId)
        {
            EnsureUniqueSlots();
            Thing carried = pawn?.carryTracker?.CarriedThing;
            UniqueGoodsSlotData slot = uniqueSlots.FirstOrDefault(candidate => candidate?.pendingSourceThingId == sourceThingId);
            if (slot == null && carried != null)
                slot = uniqueSlots.FirstOrDefault(candidate => candidate?.pendingSourceThingId == carried.thingIDNumber);
            if (slot == null)
            {
                Log.Error($"专业货柜入库失败：{LabelCap} 找不到来源编号 {sourceThingId} 对应的等待槽位。");
                return false;
            }
            if (carried == null)
            {
                Log.Error($"专业货柜入库失败：搬运者 {pawn?.LabelShort ?? "无"} 没有携带物品。");
                return false;
            }
            if (!IsEligibleUniqueThingForSlot(carried, slot.index))
            {
                Log.Error($"专业货柜入库失败：{carried.LabelCapNoCount} 不符合 {LabelCap} 的上架条件。");
                return false;
            }

            Thing stored = carried.stackCount > 1 ? carried.SplitOff(1) : carried;
            if (VirtualStorage.TryAddOrTransfer(stored, stored.stackCount, false) <= 0)
            {
                Log.Error($"专业货柜入库失败：{stored.LabelCapNoCount} 无法从搬运者转入 {LabelCap} 的虚拟库存。");
                return false;
            }
            slot.storedThingId = stored.thingIDNumber;
            slot.pendingSourceThingId = -1;
            slot.pendingSourceLabel = "";
            slot.price = Mathf.Max(1f, slot.price > 0f ? slot.price : stored.MarketValue);
            NotifyUniqueSlotsChanged("专业商品安装完成");
            return true;
        }
    }
}
