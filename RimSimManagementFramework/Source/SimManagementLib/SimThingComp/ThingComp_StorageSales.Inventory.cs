using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using SimManagementLib.SimMapComp;
using Verse;

namespace SimManagementLib.SimThingComp
{
    //从宿主唯一的直接储物仓或本建筑的储物格读取商品。
    public sealed partial class ThingComp_StorageSales
    {
        private readonly Dictionary<ThingDef, List<Thing>> stockByDef = new();
        private readonly List<Thing> listingOrder = new();
        private bool indexDirty = true;

        private SlotGroup Slots => (parent as ISlotGroupParent)?.GetSlotGroup();

        //只绑定直接物品仓，避免把商品内部的背包或加工槽递归当作库存。
        private IThingHolder InventoryHolder
        {
            get
            {
                if (parent is IThingHolder holder && holder.GetDirectlyHeldThings() != null) return holder;
                IThingHolder found = null;
                foreach (var comp in parent.AllComps)
                {
                    if (comp is not IThingHolder owner || owner.GetDirectlyHeldThings() is not ThingOwner items) continue;
                    if (found != null && found.GetDirectlyHeldThings() != items) return null;
                    found = owner;
                }
                return found;
            }
        }

        private ThingOwner Inner => InventoryHolder?.GetDirectlyHeldThings();
        private IStoreSettingsParent HolderSettings => Slots == null ? InventoryHolder as IStoreSettingsParent : null;
        private bool CanWithdraw => (parent is not IHaulSource source || source.HaulSourceEnabled)
            && (Slots != null || InventoryHolder is not IHaulSource holder || holder.HaulSourceEnabled);

        //按品种索引宿主的实物引用，索引不持有或序列化库存。
        private Dictionary<ThingDef, List<Thing>> StockIndex
        {
            get
            {
                if (!indexDirty) return stockByDef;
                stockByDef.Clear();
                listingOrder.Clear();
                foreach (var thing in StockThings)
                {
                    if (!stockByDef.TryGetValue(thing.def, out var list)) stockByDef[thing.def] = list = new();
                    list.Add(thing);
                    if (SalesProps.maxListedCount >= 0) listingOrder.Add(thing);
                }
                listingOrder.Sort((left, right) => left.thingIDNumber.CompareTo(right.thingIDNumber));
                indexDirty = false;
                return stockByDef;
            }
        }

        public bool HasInventory => Slots != null || Inner != null;
        internal IEnumerable<Thing> StockThings => (Slots?.HeldThings ?? Inner?.Cast<Thing>() ?? Enumerable.Empty<Thing>())
            .Where(t => t.def.category == ThingCategory.Item && !t.Destroyed);

        //宿主收货筛选供空架选择品种，实际交货另按具体物品检查。
        internal bool AllowsDef(ThingDef def) => SalesProps.AllowsListing(def) && HostAllowsDef(def);

        //归还原物时按宿主自身的储存规则检查。
        private bool HostAllowsDef(ThingDef def)
        {
            if (def == null || def.category != ThingCategory.Item || !def.EverStorable(false)) return false;
            return FilterAllows(parent as IStoreSettingsParent, def) && FilterAllows(HolderSettings, def);
        }

        //同时遵守玩家设置与建筑固定的储存筛选。
        private static bool FilterAllows(IStoreSettingsParent settings, ThingDef def)
        {
            return (settings?.GetStoreSettings()?.filter?.Allows(def) ?? true)
                && (settings?.GetParentStoreSettings()?.filter?.Allows(def) ?? true);
        }

        //框架和原版搬运预约占用的份额不参与销售。
        private int UnreservedCount(Thing thing)
        {
            if (!CanWithdraw || !Contains(thing) || !thing.Spawned && parent.Map.reservationManager.IsReserved(parent)) return 0;
            var ledger = parent.Map.GetComponent<MapComponent_InventoryReservations>();
            if (thing.IsForbidden(Faction.OfPlayer) && !ledger.IsProtected(thing)) return 0;
            return ledger.Available(thing);
        }

        //按实物编号分配整柜共用的上架件数，查询单一品种时仍给其他品种保留其名额。
        internal IEnumerable<ThingCount> ListedStock(ThingDef def = null)
        {
            int limit = SalesProps.maxListedCount;
            if (!AllowsCustomerSelfPurchase || limit == 0 || def != null && !OnSale(def)) yield break;
            var index = StockIndex;
            IEnumerable<Thing> candidates;
            if (limit > 0) candidates = listingOrder;
            else if (def == null) candidates = index.Values.SelectMany(stacks => stacks);
            else if (index.TryGetValue(def, out var stacks)) candidates = stacks;
            else yield break;

            int remaining = limit;
            foreach (var thing in candidates)
            {
                if (!OnSale(thing.def)) continue;
                int available = UnreservedCount(thing);
                if (available <= 0) continue;
                int count = limit > 0 ? Math.Min(remaining, available) : available;
                if (def == null || thing.def == def) yield return new ThingCount(thing, count);
                if (limit > 0 && (remaining -= count) == 0) yield break;
            }
        }

        //取货前复核这一堆可售的份额，最后一堆可以只上架一部分。
        internal int Available(Thing thing)
        {
            if (SalesProps.maxListedCount < 0)
                return AllowsCustomerSelfPurchase && OnSale(thing.def) ? UnreservedCount(thing) : 0;
            return ListedStock(thing.def).FirstOrDefault(stack => stack.Thing == thing).Count;
        }

        //让选购仍能使用每件实物自身的品质与售价。
        internal IEnumerable<Thing> SaleThings(ThingDef def = null) => ListedStock(def).Select(stack => stack.Thing);

        //经营报表和选店只统计当前已经上架的件数。
        internal int SaleCount(ThingDef def) => ListedStock(def).Sum(stack => stack.Count);

        //补货依据已放入宿主的全部件数，包含暂时预留的库存。
        public int CountStored(ThingDef def) => StockIndex.TryGetValue(def, out var list) ? list.Where(Contains).Sum(t => t.stackCount) : 0;

        //检查成交前这堆实物仍在选中的货架或内仓。
        internal bool Contains(Thing thing) => thing != null && !thing.Destroyed &&
            (Slots is SlotGroup slots ? thing.Spawned && thing.GetSlotGroup() == slots : Inner?.Contains(thing) == true);
    }
}
