using System;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimThingComp
{
    //向具体货格或宿主的直接容器交货，共用原版收货规则和预约。
    public sealed partial class ThingComp_StorageSales
    {
        internal PathEndMode DeliveryEndMode => Slots == null ? InventoryInteractionEndMode : PathEndMode.ClosestTouch;

        //内仓由宿主接收预约，货架由具体目的格接收预约。
        internal LocalTargetInfo ReservationTarget(LocalTargetInfo access) => Slots == null ? parent : access;

        //检查货源适配宿主的品种、品质筛选和搬运开关。
        public bool AllowsRestockSource(Thing source) => AllowsCustomerSelfPurchase && source != null
            && GetTargetCount(source.def) > 0 && Accepts(source) && !Contains(source);

        //检查该件货物是否可由宿主接收。
        private bool Accepts(Thing thing) => HostAllowsDef(thing.def) && Accepts(parent as IStoreSettingsParent, thing)
            && Accepts(HolderSettings, thing);

        //按宿主和持货组件的实际物品筛选校验品质、材质和收货状态。
        private static bool Accepts(IStoreSettingsParent settings, Thing thing) =>
            (settings is not IHaulDestination destination || destination.HaulDestinationEnabled && destination.Accepts(thing))
            && settings?.GetStoreSettings()?.AllowedToAccept(thing) != false
            && settings?.GetParentStoreSettings()?.AllowedToAccept(thing) != false;

        //选择可预约和到达的真实目标，内仓预约宿主，货架预约具体格。
        internal LocalTargetInfo FindDeliveryTarget(Pawn pawn, Thing thing)
        {
            if (!AllowsRestockSource(thing) || parent.IsForbidden(pawn)) return LocalTargetInfo.Invalid;
            if (Slots is SlotGroup slots)
                return StoreUtility.TryFindBestBetterStoreCellForIn(thing, pawn, parent.Map, StoragePriority.Unstored,
                    pawn.Faction, slots, out var cell) && pawn.CanReach(cell, PathEndMode.ClosestTouch, Danger.Deadly)
                    ? (LocalTargetInfo)cell : LocalTargetInfo.Invalid;
            return pawn.CanReserve(parent) && InnerRoom(thing, pawn) > 0 ? InventoryInteractionTarget : LocalTargetInfo.Invalid;
        }

        //复核目标格内真正可以堆放的件数，排除不同品质导致的不可并栈空间。
        private int CellRoom(IntVec3 cell, Thing thing, Pawn pawn)
        {
            var map = parent.Map;
            if (!cell.IsValid || !cell.InBounds(map) || cell.GetSlotGroup(map) != Slots || !cell.IsValidStorageFor(map, thing)
                || cell.ContainsStaticFire(map) || (pawn != null && cell.IsForbidden(pawn))) return 0;
            if (pawn == null ? map.reservationManager.IsReserved(cell) : !pawn.CanReserve(cell)) return 0;
            int incompatible = cell.GetThingList(map).Where(t => t.def == thing.def && !t.CanStackWith(thing))
                .Sum(t => Math.Max(0, t.def.stackLimit - t.stackCount));
            return Math.Max(0, cell.GetItemStackSpaceLeftFor(map, thing.def) - incompatible);
        }

        //读取内仓真实接纳量并扣除其他原版搬运者的在途容量。
        private int InnerRoom(Thing thing, Pawn pawn)
        {
            int capacity = Inner?.GetCountCanAccept(thing) ?? 0;
            if (parent is IHaulEnroute route)
                capacity = Math.Min(capacity, route.GetSpaceRemainingWithEnroute(thing.def, pawn));
            return Math.Max(0, capacity);
        }

        //预约与交付两端都重查目标，以实际可交的部分结算。
        internal int DeliveryRoom(Pawn pawn, Thing thing, LocalTargetInfo target)
        {
            if (!target.IsValid || !AllowsCustomerSelfPurchase || !Accepts(thing) || parent.IsForbidden(pawn)) return 0;
            int need = GetTargetCount(thing.def) - StockThings.Where(t => t.def == thing.def).Sum(t => t.stackCount);
            int room = Slots != null ? CellRoom(target.Cell, thing, pawn)
                : target == InventoryInteractionTarget && pawn.CanReserve(parent) ? InnerRoom(thing, pawn) : 0;
            return Math.Max(0, Math.Min(need, room));
        }

        //把手中货物转入已预约的目的地，并通知宿主实际收货数。
        public int Deposit(Pawn pawn, ThingDef def, int count)
        {
            Thing thing = pawn.carryTracker.CarriedThing;
            if (thing?.def != def) return 0;
            LocalTargetInfo target = pawn.CurJob.targetC;
            int amount = Math.Min(thing.stackCount, Math.Min(count, DeliveryRoom(pawn, thing, target)));
            if (amount <= 0) return 0;
            int stored;
            if (Slots != null)
            {
                stored = 0;
                pawn.carryTracker.innerContainer.TryDrop(thing, target.Cell, parent.Map, ThingPlaceMode.Direct, amount, out _,
                    (_, n) => stored += n);
            }
            else
            {
                stored = pawn.carryTracker.innerContainer.TryTransferToContainer(thing, Inner, amount);
                if (stored > 0)
                {
                    (parent as INotifyHauledTo)?.Notify_HauledTo(pawn, thing, stored);
                    foreach (var comp in parent.AllComps.OfType<INotifyHauledTo>()) comp.Notify_HauledTo(pawn, thing, stored);
                }
            }
            if (stored > 0) NotifyStockChanged(thing.def);
            return stored;
        }

        //退货不要求货柜仍营业，但仍需遵守宿主筛选与容量。
        internal void ReturnToHost(Thing thing)
        {
            if (!parent.Spawned || !Accepts(thing)) return;
            if (Slots is SlotGroup slots)
            {
                foreach (var cell in slots.CellsList)
                {
                    int amount = Math.Min(thing.stackCount, CellRoom(cell, thing, null));
                    if (amount <= 0) continue;
                    Thing part = thing.SplitOff(amount);
                    if (!GenPlace.TryPlaceThing(part, cell, parent.Map, ThingPlaceMode.Direct) && part != thing && !part.Destroyed)
                        thing.TryAbsorbStack(part, false);
                    if (thing.Destroyed || thing.Spawned) break;
                }
            }
            else if (!parent.Map.reservationManager.IsReserved(parent))
            {
                int amount = Math.Min(thing.stackCount, InnerRoom(thing, null));
                if (amount > 0) Inner.TryAddOrTransfer(thing, amount);
            }
            NotifyStockChanged(thing.def);
        }
    }
}
