using RimWorld;
using SimManagementLib.SimThingClass;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimMapComp
{
    //补货货源解析器，职责是使用原版区域搜索查找普通货源并批量解析专业货源编号。
    internal sealed class RestockSourceResolver
    {
        private readonly Map map;
        private readonly Dictionary<int, Thing> exactThingsById = new Dictionary<int, Thing>();
        private readonly HashSet<int> unresolvedExactThingIds = new HashSet<int>();

        //创建当前地图的货源解析器。
        public RestockSourceResolver(Map map)
        {
            this.map = map;
        }

        //记录玩家刚选择或运行中已经解析出的专业货源。
        public void RememberExactThing(Thing thing)
        {
            if (thing == null || thing.thingIDNumber < 0)
                return;
            exactThingsById[thing.thingIDNumber] = thing;
            unresolvedExactThingIds.Remove(thing.thingIDNumber);
        }

        //按编号解析专业货源，未命中时把多个编号合并到一次地图扫描中。
        public Thing ResolveExactThing(int thingId, bool allowBatchScan, out bool confirmedDestroyed)
        {
            confirmedDestroyed = false;
            if (thingId < 0)
                return null;

            if (exactThingsById.TryGetValue(thingId, out Thing cached))
            {
                confirmedDestroyed = cached == null || cached.Destroyed;
                return cached;
            }

            unresolvedExactThingIds.Add(thingId);
            if (allowBatchScan)
                ResolveExactThingsBatch();

            if (!exactThingsById.TryGetValue(thingId, out cached))
                return null;
            confirmedDestroyed = cached == null || cached.Destroyed;
            return cached;
        }

        //快速判断地图是否存在基础状态有效的普通货源，不执行预约或可达查询。
        public bool HasPotentialBulkSupply(Building_SimContainer storage, ThingDef thingDef)
        {
            if (storage == null || thingDef == null || storage.Map != map)
                return false;
            List<Thing> candidates = map.listerThings?.ThingsOfDef(thingDef);
            if (candidates == null)
                return false;
            for (int i = 0; i < candidates.Count; i++)
            {
                Thing thing = candidates[i];
                if (thing == null || thing.Destroyed || !thing.Spawned || thing.Map != map || thing.stackCount <= 0)
                    continue;
                if (!storage.AllowsRestockSource(thing) || storage.Map.GetComponent<MapComponent_InventoryReservations>().Available(thing) <= 0 || thing.GetSlotGroup()?.parent is Building_SimContainer)
                    continue;
                return true;
            }
            return false;
        }

        //查找当前 Pawn 最近的普通补货货源，深度模式只执行兜底搜索以避免重复区域查询。
        public Thing FindBulkSupply(Pawn pawn, Building_SimContainer storage, ThingDef thingDef, bool allowDeepSearch)
        {
            if (pawn?.Map != map || storage == null || thingDef == null)
                return null;

            List<Thing> candidates = map.listerThings?.ThingsOfDef(thingDef);
            if (candidates == null || candidates.Count <= 0)
                return null;

            Predicate<Thing> validator = thing => IsBulkSupplyCandidate(pawn, storage, thing, thingDef);
            if (!allowDeepSearch)
            {
                return GenClosest.ClosestThing_Regionwise_ReachablePrioritized(
                    pawn.Position,
                    map,
                    ThingRequest.ForDef(thingDef),
                    PathEndMode.ClosestTouch,
                    TraverseParms.For(pawn, Danger.Deadly),
                    9999f,
                    validator,
                    null,
                    0,
                    30);
            }

            return GenClosest.ClosestThingReachable(
                pawn.Position,
                map,
                ThingRequest.ForDef(thingDef),
                PathEndMode.ClosestTouch,
                TraverseParms.For(pawn, Danger.Deadly),
                9999f,
                validator,
                candidates,
                0,
                30,
                true);
        }

        //清空全部运行时货源索引。
        public void Clear()
        {
            exactThingsById.Clear();
            unresolvedExactThingIds.Clear();
        }

        //批量扫描地图和搬运者以解析所有尚未命中的专业货源编号。
        private void ResolveExactThingsBatch()
        {
            if (unresolvedExactThingIds.Count <= 0)
                return;

            List<Thing> things = map?.listerThings?.AllThings;
            if (things != null)
            {
                for (int i = 0; i < things.Count && unresolvedExactThingIds.Count > 0; i++)
                    TryResolveExactThing(things[i]);
            }

            IReadOnlyList<Pawn> pawns = map?.mapPawns?.AllPawnsSpawned;
            if (pawns == null)
                return;
            for (int i = 0; i < pawns.Count && unresolvedExactThingIds.Count > 0; i++)
                TryResolveExactThing(pawns[i]?.carryTracker?.CarriedThing);
        }

        //尝试把一个 Thing 写入待解析专业货源索引。
        private void TryResolveExactThing(Thing thing)
        {
            if (thing == null || !unresolvedExactThingIds.Contains(thing.thingIDNumber))
                return;
            exactThingsById[thing.thingIDNumber] = thing;
            unresolvedExactThingIds.Remove(thing.thingIDNumber);
        }

        //判断地图物品是否可作为指定 Pawn 的普通货柜货源。
        private static bool IsBulkSupplyCandidate(Pawn pawn, Building_SimContainer storage, Thing thing, ThingDef thingDef)
        {
            if (pawn == null || storage == null || thing == null || thingDef == null)
                return false;
            if (thing.Destroyed || !thing.Spawned || thing.stackCount <= 0 || thing.def != thingDef)
                return false;
            if (thing.Map != pawn.Map || storage.Map != pawn.Map || thing.IsForbidden(pawn))
                return false;
            if (!storage.AllowsRestockSource(thing) || storage.Map.GetComponent<MapComponent_InventoryReservations>().Available(thing) <= 0 || thing.GetSlotGroup()?.parent is Building_SimContainer)
                return false;
            return pawn.CanReserve(thing);
        }
    }
}
