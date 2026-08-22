using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.SimDialog;
using SimManagementLib.SimMapComp;
using SimManagementLib.SimThingComp;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimThingClass
{
    //单件品质商品货柜基类，职责是按槽位保存真实物品并提供精确补货和购买能力。
    public partial class Building_UniqueGoodsContainer : Building_SimContainer
    {
        private List<UniqueGoodsSlotData> uniqueSlots = new List<UniqueGoodsSlotData>();
        private int cachedInspectVersion = -1;
        private string cachedUniqueInspect = "";

        public ThingComp_UniqueGoodsContainer UniqueComp => GetComp<ThingComp_UniqueGoodsContainer>();
        public IReadOnlyList<UniqueGoodsSlotData> UniqueSlots
        {
            get
            {
                EnsureUniqueSlots();
                return uniqueSlots;
            }
        }
        public int UniqueSlotCount => UniqueComp?.SlotCount ?? MaxTotalCapacity;

        //枚举当前可售实例涉及的商品定义，职责是接入商店匹配和统计。
        public override IEnumerable<ThingDef> ActiveDefs
        {
            get
            {
                EnsureUniqueSlots();
                HashSet<ThingDef> seen = new HashSet<ThingDef>();
                for (int i = 0; i < uniqueSlots.Count; i++)
                {
                    Thing thing = GetStoredThing(uniqueSlots[i]);
                    if (thing != null && IsEligibleUniqueThingForSlot(thing, i) && seen.Add(thing.def))
                        yield return thing.def;
                }
            }
        }

        //生成建筑后恢复槽位引用。
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            EnsureUniqueSlots();
        }

        //保存或读取专业货柜槽位。
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref uniqueSlots, "uniqueGoodsSlots", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                EnsureUniqueSlots();
        }

        //绘制货柜及派生类提供的单件商品效果。
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            base.DrawAt(drawLoc, flip);
            DrawUniqueGoods(drawLoc, flip);
        }

        //绘制单件商品附加效果，职责是供派生货柜按需覆盖。
        protected virtual void DrawUniqueGoods(Vector3 drawLoc, bool flip)
        {
        }

        //判断真实物品能否由当前专业货柜接收。
        protected virtual bool IsEligibleUniqueThing(Thing thing)
        {
            return UniqueGoodsUtility.IsEligible(thing, this);
        }

        //判断真实物品能否进入指定槽位，职责是允许专用货柜约束各槽位的商品类型。
        protected virtual bool IsEligibleUniqueThingForSlot(Thing thing, int slotIndex)
        {
            return IsEligibleUniqueThing(thing);
        }

        //处理槽位变化，职责是刷新库存显示并向地图协调器发出精确需求通知。
        protected virtual void NotifyUniqueSlotsChanged(string reason = "专业货柜槽位变化")
        {
            MarkStoredCountCacheDirty();
            RefreshProgressStageGraphic();
            Map?.GetComponent<MapComponent_RestockTaskQueue>()?.MarkUniqueStorageDirty(this, reason);
        }

        //创建专业货柜管理窗口。
        protected override Window CreateManagementWindow()
        {
            return new Dialog_UniqueGoodsManager(this);
        }

        //构建专业货柜检查文本，职责是按库存版本缓存槽位摘要并跳过普通 Def 目标扫描。
        protected override string BuildContainerInspectString(string baseStr)
        {
            if (cachedInspectVersion != StoredCountVersion)
            {
                EnsureUniqueSlots();
                int stored = 0;
                int pending = 0;
                int reserved = 0;
                for (int i = 0; i < uniqueSlots.Count; i++)
                {
                    UniqueGoodsSlotData slot = uniqueSlots[i];
                    if (slot == null) continue;
                    if (slot.IsReservedByCustomer) reserved++;
                    else if (GetStoredThing(slot) != null) stored++;
                    else if (slot.HasPendingSource) pending++;
                }
                cachedUniqueInspect = $"单件库存: {stored}/{UniqueSlotCount}";
                if (pending > 0) cachedUniqueInspect += $"  待补货: {pending}";
                if (reserved > 0) cachedUniqueInspect += $"  顾客挑选中: {reserved}";
                cachedInspectVersion = StoredCountVersion;
            }
            return string.IsNullOrEmpty(baseStr) ? cachedUniqueInspect : baseStr + "\n" + cachedUniqueInspect;
        }

        //查找槽位持有的真实物品。
        public Thing GetStoredThing(UniqueGoodsSlotData slot)
        {
            if (slot == null || slot.storedThingId < 0 || VirtualStorage == null) return null;
            for (int i = 0; i < VirtualStorage.Count; i++)
            {
                Thing thing = VirtualStorage[i];
                if (thing != null && !thing.Destroyed && thing.thingIDNumber == slot.storedThingId)
                    return thing;
            }
            return null;
        }

        //读取指定索引的槽位。
        public UniqueGoodsSlotData GetUniqueSlot(int index)
        {
            EnsureUniqueSlots();
            return index >= 0 && index < uniqueSlots.Count ? uniqueSlots[index] : null;
        }

        //直接存入一件真实商品，职责是供场景生成和外部测试初始化现货槽位。
        public bool TryStoreDirectly(Thing thing)
        {
            EnsureUniqueSlots();
            if (!IsEligibleUniqueThing(thing)) return false;
            UniqueGoodsSlotData slot = uniqueSlots.FirstOrDefault(s => s != null
                && !s.IsOccupied
                && IsEligibleUniqueThingForSlot(thing, s.index));
            if (slot == null) return false;
            Map sourceMap = thing.Map;
            IntVec3 sourcePosition = thing.Position;
            if (thing.Spawned)
                thing.DeSpawn(DestroyMode.Vanish);
            if (!VirtualStorage.TryAddOrTransfer(thing, false) || !VirtualStorage.Contains(thing))
            {
                if (!thing.Destroyed && !thing.Spawned && sourceMap != null)
                    GenPlace.TryPlaceThing(thing, sourcePosition, sourceMap, ThingPlaceMode.Near, out _);
                Log.Error($"专业货柜直接入库失败：{thing.LabelCapNoCount} 未进入 {LabelCap} 的虚拟库存。");
                return false;
            }
            slot.storedThingId = thing.thingIDNumber;
            slot.price = Mathf.Max(1f, thing.MarketValue);
            NotifyUniqueSlotsChanged();
            return true;
        }

        //返回所有当前可被顾客购买的槽位。
        public IEnumerable<UniqueGoodsSlotData> GetSellableSlots()
        {
            EnsureUniqueSlots();
            for (int i = 0; i < uniqueSlots.Count; i++)
            {
                UniqueGoodsSlotData slot = uniqueSlots[i];
                Thing thing = GetStoredThing(slot);
                if (slot != null && !slot.IsReservedByCustomer && thing != null
                    && IsEligibleUniqueThingForSlot(thing, slot.index))
                    yield return slot;
            }
        }

        //将指定槽位的真实商品转入顾客购物车托管。
        public Thing TakeForCustomer(int slotIndex, int customerId, out float price, out float marketValue)
        {
            price = 0f;
            marketValue = 0f;
            UniqueGoodsSlotData slot = GetUniqueSlot(slotIndex);
            Thing thing = GetStoredThing(slot);
            if (slot == null || slot.IsReservedByCustomer || thing == null
                || !IsEligibleUniqueThingForSlot(thing, slot.index)) return null;

            marketValue = Mathf.Max(1f, thing.MarketValue);
            price = Mathf.Max(1f, slot.price > 0f ? slot.price : marketValue);
            Thing taken = VirtualStorage.Take(thing, 1);
            if (taken == null) return null;
            slot.reservedCustomerId = customerId;
            NotifyUniqueSlotsChanged();
            return taken;
        }

        //将未付款的真实商品退回原槽位。
        public bool TryRestoreCustomerThing(int slotIndex, Thing thing)
        {
            UniqueGoodsSlotData slot = GetUniqueSlot(slotIndex);
            if (slot == null || thing == null || thing.Destroyed
                || !IsEligibleUniqueThingForSlot(thing, slot.index)
                || GetStoredThing(slot) != null) return false;
            if (VirtualStorage.TryAddOrTransfer(thing, thing.stackCount, false) <= 0) return false;
            slot.storedThingId = thing.thingIDNumber;
            slot.reservedCustomerId = -1;
            NotifyUniqueSlotsChanged();
            return true;
        }

        //完成指定槽位的成交并释放槽位。
        public void FinalizeCustomerSale(int slotIndex, int customerId)
        {
            UniqueGoodsSlotData slot = GetUniqueSlot(slotIndex);
            if (slot == null || (customerId >= 0 && slot.reservedCustomerId >= 0 && slot.reservedCustomerId != customerId)) return;
            slot.Clear();
            NotifyUniqueSlotsChanged();
        }

        //取消等待补货或将已上架物品卸到货柜附近。
        public bool TryClearSlot(int slotIndex)
        {
            UniqueGoodsSlotData slot = GetUniqueSlot(slotIndex);
            if (slot == null || slot.IsReservedByCustomer) return false;
            Thing thing = GetStoredThing(slot);
            if (thing != null)
            {
                Thing taken = VirtualStorage.Take(thing, thing.stackCount);
                if (taken != null && Map != null)
                    GenPlace.TryPlaceThing(taken, Position, Map, ThingPlaceMode.Near, out _);
            }
            slot.Clear();
            NotifyUniqueSlotsChanged();
            return true;
        }

        //更新指定槽位的固定售价。
        public void SetSlotPrice(int slotIndex, float price)
        {
            UniqueGoodsSlotData slot = GetUniqueSlot(slotIndex);
            if (slot != null && !slot.IsReservedByCustomer)
                slot.price = Mathf.Max(1f, price);
        }

        //补齐槽位并为已有真实库存恢复引用。
        private void EnsureUniqueSlots()
        {
            if (uniqueSlots == null) uniqueSlots = new List<UniqueGoodsSlotData>();
            int target = System.Math.Max(1, UniqueSlotCount);
            for (int i = 0; i < uniqueSlots.Count; i++)
            {
                if (uniqueSlots[i] == null) uniqueSlots[i] = new UniqueGoodsSlotData();
                uniqueSlots[i].index = i;
            }
            while (uniqueSlots.Count < target)
                uniqueSlots.Add(new UniqueGoodsSlotData { index = uniqueSlots.Count });

            if (VirtualStorage == null) return;
            HashSet<int> actualIds = new HashSet<int>();
            for (int i = 0; i < VirtualStorage.Count; i++)
            {
                Thing thing = VirtualStorage[i];
                if (thing != null && !thing.Destroyed)
                    actualIds.Add(thing.thingIDNumber);
            }

            //顾客托管期间实例暂时不在货柜中，其余失效引用必须释放，避免形成无法购买的假库存。
            bool repaired = false;
            for (int i = 0; i < uniqueSlots.Count; i++)
            {
                UniqueGoodsSlotData slot = uniqueSlots[i];
                if (slot == null || slot.storedThingId < 0 || slot.IsReservedByCustomer || actualIds.Contains(slot.storedThingId))
                    continue;
                int misplacedThingId = slot.storedThingId;
                slot.storedThingId = -1;
                Thing misplacedThing = UniqueGoodsUtility.FindSource(Map, misplacedThingId, this);
                if (!slot.HasPendingSource && misplacedThing != null)
                {
                    slot.pendingSourceThingId = misplacedThing.thingIDNumber;
                    slot.pendingSourceLabel = misplacedThing.LabelCapNoCount;
                    if (slot.price <= 0f)
                        slot.price = Mathf.Max(1f, misplacedThing.MarketValue);
                }
                else if (!slot.HasPendingSource)
                {
                    slot.price = 0f;
                }
                repaired = true;
            }

            HashSet<int> assigned = new HashSet<int>(uniqueSlots.Where(s => s != null && s.storedThingId >= 0).Select(s => s.storedThingId));
            for (int i = 0; i < VirtualStorage.Count; i++)
            {
                Thing thing = VirtualStorage[i];
                if (thing == null || assigned.Contains(thing.thingIDNumber)) continue;
                UniqueGoodsSlotData empty = uniqueSlots.FirstOrDefault(s => s != null && !s.IsOccupied);
                if (empty == null) break;
                empty.storedThingId = thing.thingIDNumber;
                empty.price = Mathf.Max(1f, thing.MarketValue);
                assigned.Add(thing.thingIDNumber);
                repaired = true;
            }
            if (repaired)
                MarkStoredCountCacheDirty();
        }
    }
}
