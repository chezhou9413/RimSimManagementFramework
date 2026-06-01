using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SimManagementLib.SimThingClass
{
    /// <summary>
    /// 提供货柜虚拟库存的预约、入库、出库和购买转移能力。
    /// </summary>
    public partial class Building_SimContainer
    {
        /// <summary>
        /// 为指定商品预留一次待入库数量，负责避免多个搬运任务重复补同一批库存。
        /// </summary>
        public int ReservePending(ThingDef thingDef, int count)
        {
            ReconcilePendingReservations();
            if (count <= 0) return 0;
            int needed = CountNeeded(thingDef);
            if (needed <= 0) return 0;
            int actual = System.Math.Min(count, needed);
            pendingIn[thingDef] = CountPending(thingDef) + actual;
            return actual;
        }

        /// <summary>
        /// 取消指定商品的一段待入库预约，负责在任务中断或完成后回收预约数量。
        /// </summary>
        public void CancelPending(ThingDef thingDef, int reservedCount)
        {
            if (thingDef == null) return;
            if (reservedCount <= 0) return;
            int next = CountPending(thingDef) - reservedCount;
            if (next <= 0) pendingIn.Remove(thingDef);
            else pendingIn[thingDef] = next;
        }

        /// <summary>
        /// 接收搬运者携带的商品，负责按预约数量和剩余容量转入货柜虚拟库存。
        /// </summary>
        public int Deposit(Pawn pawn, ThingDef thingDef, int reservedCount)
        {
            CancelPending(thingDef, reservedCount);

            Thing carried = pawn.carryTracker?.CarriedThing;
            if (carried == null || carried.def != thingDef) return 0;

            int currentNeed = CountShortfallIgnoringPendingIn(thingDef);
            if (currentNeed <= 0) return 0;

            int maxByReservation = reservedCount > 0 ? reservedCount : carried.stackCount;
            int canStore = System.Math.Min(
                GetRemainingCapacityForStored(),
                System.Math.Min(currentNeed, System.Math.Min(carried.stackCount, maxByReservation)));
            if (canStore <= 0) return 0;

            if (canStore >= carried.stackCount)
            {
                virtualStorage.TryAddOrTransfer(carried, carried.stackCount);
                RefreshProgressStageGraphic();
                return canStore;
            }

            Thing part = carried.SplitOff(canStore);
            virtualStorage.TryAddOrTransfer(part, part.stackCount);

            if (pawn.carryTracker?.CarriedThing != null && pawn.Spawned && pawn.MapHeld != null)
            {
                pawn.carryTracker.TryDropCarriedThing(pawn.PositionHeld, ThingPlaceMode.Near, out _);
            }

            RefreshProgressStageGraphic();
            return canStore;
        }

        /// <summary>
        /// 接收退回或临时生成的物品，负责按剩余容量放入虚拟库存并返回实际入库数量。
        /// </summary>
        public int TryReceiveReturnedThing(Thing thing)
        {
            if (thing == null || thing.Destroyed) return 0;

            int canStore = System.Math.Min(GetRemainingCapacityForStored(), thing.stackCount);
            if (canStore <= 0) return 0;

            if (canStore >= thing.stackCount)
            {
                int all = thing.stackCount;
                virtualStorage.TryAddOrTransfer(thing, thing.stackCount);
                RefreshProgressStageGraphic();
                return all;
            }

            Thing part = thing.SplitOff(canStore);
            virtualStorage.TryAddOrTransfer(part, part.stackCount);
            RefreshProgressStageGraphic();
            return canStore;
        }

        /// <summary>
        /// 直接生成指定商品并存入货柜，负责调试补货和自动铺货时创建库存实物。
        /// </summary>
        public int TryCreateAndStore(ThingDef def, int desiredCount)
        {
            if (def == null || desiredCount <= 0) return 0;
            ReconcilePendingReservations();

            int totalStored = 0;
            int remaining = System.Math.Min(desiredCount, GetRemainingCapacityForStored());
            int stackLimit = def.stackLimit > 0 ? def.stackLimit : desiredCount;

            while (remaining > 0)
            {
                int chunk = System.Math.Min(stackLimit, remaining);
                Thing thing = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
                if (thing == null) break;

                thing.stackCount = chunk;
                int stored = TryReceiveReturnedThing(thing);
                totalStored += stored;
                remaining -= stored;

                // 完整入库时 thing 已经归 virtualStorage 持有，不能再对原引用执行销毁。
                if (stored < chunk && thing.holdingOwner == null && !thing.Destroyed && thing.stackCount > 0)
                    DestroyDetachedTemporaryThing(thing);

                if (stored <= 0)
                    break;
            }

            ReconcilePendingReservations();
            return totalStored;
        }

        /// <summary>
        /// 统计指定商品当前超过目标库存的数量，负责为下架和搬出任务提供可取数量。
        /// </summary>
        public int CountExcess(ThingDef thingDef)
        {
            ClearOrphanedPendingOut();
            int stored = CountStored(thingDef);
            int target = GetTargetCount(thingDef);
            int alreadyPendingOut = pendingOut.TryGetValue(thingDef, out int value) ? value : 0;
            return System.Math.Max(0, stored - target - alreadyPendingOut);
        }

        /// <summary>
        /// 枚举所有超过目标库存的商品，负责批量下架逻辑扫描货柜当前库存。
        /// </summary>
        public IEnumerable<(ThingDef td, int excess)> GetExcessItems()
        {
            HashSet<ThingDef> storedDefs = new HashSet<ThingDef>();
            foreach (Thing thing in virtualStorage)
                storedDefs.Add(thing.def);

            foreach (ThingDef thingDef in storedDefs)
            {
                int excess = CountExcess(thingDef);
                if (excess > 0) yield return (thingDef, excess);
            }
        }

        /// <summary>
        /// 为指定商品预留一次待出库数量，负责避免多个搬出任务同时取走同一批库存。
        /// </summary>
        public int ReservePendingOut(ThingDef thingDef, int count)
        {
            ReconcilePendingReservations();
            if (count <= 0) return 0;
            int excess = CountExcess(thingDef);
            if (excess <= 0) return 0;
            int actual = System.Math.Min(count, excess);
            pendingOut[thingDef] = (pendingOut.TryGetValue(thingDef, out int value) ? value : 0) + actual;
            return actual;
        }

        /// <summary>
        /// 取消指定商品的一段待出库预约，负责在搬出任务中断或完成后回收预约数量。
        /// </summary>
        public void CancelPendingOut(ThingDef thingDef, int count)
        {
            if (thingDef == null) return;
            if (count <= 0) return;
            int next = (pendingOut.TryGetValue(thingDef, out int value) ? value : 0) - count;
            if (next <= 0) pendingOut.Remove(thingDef);
            else pendingOut[thingDef] = next;
        }

        /// <summary>
        /// 从虚拟库存取出指定商品并掉落到地图，负责店员下架或调拨商品。
        /// </summary>
        public Thing Withdraw(ThingDef thingDef, int count, IntVec3 dropLoc, int reservedCount)
        {
            CancelPendingOut(thingDef, reservedCount);

            int currentExcess = CountExcessIgnoringPendingOut(thingDef);
            if (currentExcess <= 0) return null;

            Thing stored = null;
            foreach (Thing thing in virtualStorage)
            {
                if (thing.def == thingDef)
                {
                    stored = thing;
                    break;
                }
            }
            if (stored == null) return null;

            int actual = System.Math.Min(currentExcess, System.Math.Min(count, stored.stackCount));
            if (actual <= 0) return null;
            virtualStorage.TryDrop(stored, dropLoc, Map, ThingPlaceMode.Near, actual, out Thing result);
            if (result != null)
                RefreshProgressStageGraphic();
            return result;
        }

        /// <summary>
        /// 从虚拟库存中扣除顾客购买的商品，负责返回成交实物和成交价值。
        /// </summary>
        public Thing TryVirtualBuy(ThingDef thingDef, int count, out float itemMarketValue)
        {
            itemMarketValue = 0f;
            int stored = CountStored(thingDef);
            if (stored <= 0) return null;

            int takeCount = System.Math.Min(count, stored);

            float unitValue = 0f;
            Thing firstThing = null;
            foreach (Thing thing in virtualStorage)
            {
                if (thing.def == thingDef)
                {
                    firstThing = thing;
                    unitValue = thing.MarketValue;
                    break;
                }
            }
            if (firstThing == null) return null;

            itemMarketValue = unitValue * takeCount;

            Thing result = null;
            int remaining = takeCount;
            foreach (Thing thing in virtualStorage.ToList())
            {
                if (thing.def != thingDef) continue;
                int fromThis = System.Math.Min(remaining, thing.stackCount);
                Thing taken = virtualStorage.Take(thing, fromThis);
                if (result == null)
                {
                    result = taken;
                }
                else
                {
                    result.stackCount += taken.stackCount;
                    DestroyDetachedTemporaryThing(taken);
                }

                remaining -= fromThis;
                if (remaining <= 0) break;
            }

            if (result != null)
                RefreshProgressStageGraphic();
            return result;
        }

        /// <summary>
        /// 清理已经脱离地图和容器的临时物品，负责处理原版标记为不可普通销毁的特殊物品。
        /// </summary>
        private static void DestroyDetachedTemporaryThing(Thing thing)
        {
            if (thing == null || thing.Destroyed) return;

            bool previousAllowDestroyNonDestroyable = Thing.allowDestroyNonDestroyable;
            try
            {
                if (thing.def != null && !thing.def.destroyable)
                    Thing.allowDestroyNonDestroyable = true;

                thing.Destroy(DestroyMode.Vanish);
            }
            finally
            {
                Thing.allowDestroyNonDestroyable = previousAllowDestroyNonDestroyable;
            }
        }
    }
}
