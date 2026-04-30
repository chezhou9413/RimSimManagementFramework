using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SimManagementLib.SimThingClass
{
    public partial class Building_SimContainer
    {
        public int ReservePending(ThingDef thingDef, int count)
        {
            if (count <= 0) return 0;
            int needed = CountNeeded(thingDef);
            if (needed <= 0) return 0;
            int actual = System.Math.Min(count, needed);
            pendingIn[thingDef] = CountPending(thingDef) + actual;
            return actual;
        }

        public void CancelPending(ThingDef thingDef, int reservedCount)
        {
            if (reservedCount <= 0) return;
            int next = CountPending(thingDef) - reservedCount;
            if (next <= 0) pendingIn.Remove(thingDef);
            else pendingIn[thingDef] = next;
        }

        public int Deposit(Pawn pawn, ThingDef thingDef, int reservedCount)
        {
            CancelPending(thingDef, reservedCount);

            Thing carried = pawn.carryTracker?.CarriedThing;
            if (carried == null || carried.def != thingDef) return 0;

            int maxByReservation = reservedCount > 0 ? reservedCount : carried.stackCount;
            int canStore = System.Math.Min(GetRemainingCapacityForStored(), System.Math.Min(carried.stackCount, maxByReservation));
            if (canStore <= 0) return 0;

            if (canStore >= carried.stackCount)
            {
                virtualStorage.TryAddOrTransfer(carried, carried.stackCount);
                return canStore;
            }

            Thing part = carried.SplitOff(canStore);
            virtualStorage.TryAddOrTransfer(part, part.stackCount);

            if (pawn.carryTracker?.CarriedThing != null && pawn.Spawned && pawn.MapHeld != null)
            {
                pawn.carryTracker.TryDropCarriedThing(pawn.PositionHeld, ThingPlaceMode.Near, out _);
            }

            return canStore;
        }

        public int TryReceiveReturnedThing(Thing thing)
        {
            if (thing == null || thing.Destroyed) return 0;

            int canStore = System.Math.Min(GetRemainingCapacityForStored(), thing.stackCount);
            if (canStore <= 0) return 0;

            if (canStore >= thing.stackCount)
            {
                int all = thing.stackCount;
                virtualStorage.TryAddOrTransfer(thing, thing.stackCount);
                return all;
            }

            Thing part = thing.SplitOff(canStore);
            virtualStorage.TryAddOrTransfer(part, part.stackCount);
            return canStore;
        }

        public int TryCreateAndStore(ThingDef def, int desiredCount)
        {
            if (def == null || desiredCount <= 0) return 0;

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

                if (!thing.Destroyed && thing.stackCount > 0)
                    thing.Destroy(DestroyMode.Vanish);

                if (stored <= 0)
                    break;
            }

            return totalStored;
        }

        public int CountExcess(ThingDef thingDef)
        {
            int stored = CountStored(thingDef);
            int target = GetTargetCount(thingDef);
            int alreadyPendingOut = pendingOut.TryGetValue(thingDef, out int value) ? value : 0;
            return System.Math.Max(0, stored - target - alreadyPendingOut);
        }

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

        public int ReservePendingOut(ThingDef thingDef, int count)
        {
            if (count <= 0) return 0;
            int excess = CountExcess(thingDef);
            if (excess <= 0) return 0;
            int actual = System.Math.Min(count, excess);
            pendingOut[thingDef] = (pendingOut.TryGetValue(thingDef, out int value) ? value : 0) + actual;
            return actual;
        }

        public void CancelPendingOut(ThingDef thingDef, int count)
        {
            if (count <= 0) return;
            int next = (pendingOut.TryGetValue(thingDef, out int value) ? value : 0) - count;
            if (next <= 0) pendingOut.Remove(thingDef);
            else pendingOut[thingDef] = next;
        }

        public Thing Withdraw(ThingDef thingDef, int count, IntVec3 dropLoc, int reservedCount)
        {
            CancelPendingOut(thingDef, reservedCount);

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

            int actual = System.Math.Min(count, stored.stackCount);
            virtualStorage.TryDrop(stored, dropLoc, Map, ThingPlaceMode.Near, actual, out Thing result);
            return result;
        }

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
                    taken.Destroy(DestroyMode.Vanish);
                }

                remaining -= fromThis;
                if (remaining <= 0) break;
            }

            return result;
        }
    }
}
