using RimWorld;
using SimManagementLib.SimThingClass;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimWorkGiver
{
    //补货货源分帧搜索缓存，职责是保存每个员工和货柜组合的跨帧货源扫描进度。
    internal static class RestockSupplySearchStateCache
    {
        private const int SupplyThingQueryCacheTicks = 11;
        private const int SupplySearchStateExpireTicks = 240;
        private const int SupplyNeedDefRefreshTicks = 61;
        private const int SupplyDefBudgetPerScan = 2;
        private const int SupplyThingBudgetPerScan = 24;
        private const int SupplyScanContinueDelayTicks = 1;
        private const int FailedSupplyRetryTicks = 37;
        private const int MaxSupplySearchStates = 1024;
        private static readonly Dictionary<SupplySearchKey, SupplySearchState> supplySearchStates = new Dictionary<SupplySearchKey, SupplySearchState>();
        private static readonly List<SupplySearchKey> tmpExpiredSupplySearchKeys = new List<SupplySearchKey>();
        private static int lastSupplyStateCleanupTick = -1;

        //按预算查找补货货源，职责是每次只扫描少量 Def 和少量物品并缓存可用结果。
        public static Thing FindBestSupplyBudgeted(Pawn pawn, Building_SimContainer storage, bool useCachedThingQueries)
        {
            if (pawn?.Map == null || storage == null)
                return null;

            int now = Find.TickManager?.TicksGame ?? 0;
            CleanupSupplySearchStates(now);
            SupplySearchKey key = new SupplySearchKey(pawn.Map.uniqueID, pawn.thingIDNumber, storage.thingIDNumber);
            if (!supplySearchStates.TryGetValue(key, out SupplySearchState state) || state == null || state.map != pawn.Map || state.storage != storage)
            {
                state = new SupplySearchState();
                state.map = pawn.Map;
                state.storage = storage;
                supplySearchStates[key] = state;
            }

            state.expireTick = now + SupplySearchStateExpireTicks;
            if (IsValidSupply(pawn, storage, state.cachedSupply, !useCachedThingQueries, useCachedThingQueries))
                return state.cachedSupply;

            state.cachedSupply = null;
            if (now < state.nextSearchTick)
                return null;

            RefreshNeededDefsIfNeeded(storage, state, now);
            if (state.neededDefs.Count <= 0)
            {
                state.nextSearchTick = now + FailedSupplyRetryTicks;
                return null;
            }

            Thing found = SearchSupplyWithBudget(pawn, storage, state, useCachedThingQueries);
            if (found != null)
            {
                state.cachedSupply = found;
                state.nextSearchTick = now + SupplyScanContinueDelayTicks;
                return found;
            }

            state.nextSearchTick = state.completedFullPass ? now + FailedSupplyRetryTicks : now + SupplyScanContinueDelayTicks;
            return null;
        }

        //按需刷新缺货 Def 列表，职责是让货源搜索只遍历当前确实缺货的商品。
        private static void RefreshNeededDefsIfNeeded(Building_SimContainer storage, SupplySearchState state, int now)
        {
            if (now < state.neededDefsExpireTick)
                return;

            state.neededDefs.Clear();
            foreach (ThingDef thingDef in storage.ActiveDefs)
            {
                if (storage.CountNeededForWorkScan(thingDef) > 0)
                    state.neededDefs.Add(thingDef);
            }

            state.neededDefsExpireTick = now + SupplyNeedDefRefreshTicks;
            if (state.defCursor < 0 || state.defCursor >= state.neededDefs.Count)
                state.defCursor = 0;
            state.thingCursor = 0;
        }

        //用固定预算扫描地图货源，职责是把最坏情况下的全图 ThingsOfDef 遍历拆到多帧。
        private static Thing SearchSupplyWithBudget(Pawn pawn, Building_SimContainer storage, SupplySearchState state, bool useCachedThingQueries)
        {
            state.completedFullPass = false;
            int defBudget = System.Math.Min(SupplyDefBudgetPerScan, state.neededDefs.Count);
            int thingBudget = SupplyThingBudgetPerScan;
            Thing bestThing = null;
            float bestDistance = float.MaxValue;

            while (defBudget > 0 && thingBudget > 0 && state.neededDefs.Count > 0)
            {
                if (!RestockWorkTickBudget.TryUseSupplyDefCheck(pawn.Map))
                    break;

                ThingDef thingDef = state.neededDefs[state.defCursor];
                if (storage.CountNeededForWorkScan(thingDef) <= 0)
                {
                    if (AdvanceSupplyDef(state))
                        state.completedFullPass = true;
                    defBudget--;
                    continue;
                }

                List<Thing> candidates = pawn.Map.listerThings.ThingsOfDef(thingDef);
                if (candidates == null || candidates.Count <= 0)
                {
                    if (AdvanceSupplyDef(state))
                        state.completedFullPass = true;
                    defBudget--;
                    continue;
                }

                while (state.thingCursor < candidates.Count && thingBudget > 0)
                {
                    if (!RestockWorkTickBudget.TryUseSupplyThingCheck(pawn.Map))
                        return bestThing;

                    Thing candidate = candidates[state.thingCursor++];
                    thingBudget--;
                    if (!IsValidSupply(pawn, storage, candidate, false, useCachedThingQueries))
                        continue;

                    float distance = (candidate.Position - pawn.Position).LengthHorizontalSquared;
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestThing = candidate;
                    }
                }

                if (state.thingCursor >= candidates.Count)
                {
                    if (AdvanceSupplyDef(state))
                        state.completedFullPass = true;
                    defBudget--;
                }
                else
                {
                    break;
                }
            }

            return bestThing;
        }

        //前进到下一个缺货 Def，职责是维护跨帧货源扫描游标。
        private static bool AdvanceSupplyDef(SupplySearchState state)
        {
            int previous = state.defCursor;
            state.defCursor++;
            state.thingCursor = 0;
            if (state.defCursor >= state.neededDefs.Count)
                state.defCursor = 0;
            return state.defCursor <= previous;
        }

        //判断货源是否可用于补货，职责是统一轻量扫描和强校验使用的条件。
        private static bool IsValidSupply(Pawn pawn, Building_SimContainer storage, Thing thing, bool strongNeedCheck, bool useCachedThingQueries)
        {
            if (pawn == null || storage == null || thing == null)
                return false;

            if (thing.Destroyed || !thing.Spawned || thing.stackCount <= 0)
                return false;

            if (thing.Map != pawn.Map || thing.IsForbidden(pawn) || IsInsideAnyStorageContainer(thing))
                return false;

            if (strongNeedCheck)
            {
                if (storage.CountNeeded(thing.def) <= 0)
                    return false;
            }
            else if (storage.CountNeededForWorkScan(thing.def) <= 0)
            {
                return false;
            }

            if (useCachedThingQueries)
            {
                if (!WorkGiverThingQueryCache.CanReserveThingCached(pawn, thing, 1, -1, false, SupplyThingQueryCacheTicks))
                    return false;

                return WorkGiverThingQueryCache.CanReachThingCached(pawn, thing, PathEndMode.ClosestTouch, Danger.Deadly, SupplyThingQueryCacheTicks);
            }

            return pawn.CanReserve(thing) && pawn.CanReach(thing, PathEndMode.ClosestTouch, Danger.Deadly);
        }

        //判断物品是否在本框架货柜内部，职责是避免货柜之间互相抽取虚拟库存。
        private static bool IsInsideAnyStorageContainer(Thing thing)
        {
            if (thing == null || !thing.Spawned)
                return true;

            return thing.GetSlotGroup()?.parent is Building_SimContainer;
        }

        //清理过期货源搜索状态，职责是避免长局中缓存无限增长。
        private static void CleanupSupplySearchStates(int now)
        {
            if (lastSupplyStateCleanupTick >= 0 && now - lastSupplyStateCleanupTick < 113 && supplySearchStates.Count <= MaxSupplySearchStates)
                return;

            lastSupplyStateCleanupTick = now;
            tmpExpiredSupplySearchKeys.Clear();
            foreach (KeyValuePair<SupplySearchKey, SupplySearchState> entry in supplySearchStates)
            {
                if (now > entry.Value.expireTick || entry.Value.map == null || entry.Value.storage == null || entry.Value.storage.Destroyed)
                    tmpExpiredSupplySearchKeys.Add(entry.Key);
            }

            for (int i = 0; i < tmpExpiredSupplySearchKeys.Count; i++)
                supplySearchStates.Remove(tmpExpiredSupplySearchKeys[i]);
            tmpExpiredSupplySearchKeys.Clear();
        }

        //货源搜索状态，职责是保存每个员工和货柜组合的跨帧扫描游标。
        private class SupplySearchState
        {
            public Map map;
            public Building_SimContainer storage;
            public int expireTick;
            public int nextSearchTick;
            public int neededDefsExpireTick = -1;
            public int defCursor;
            public int thingCursor;
            public bool completedFullPass;
            public Thing cachedSupply;
            public readonly List<ThingDef> neededDefs = new List<ThingDef>();
        }

        //货源搜索键，职责是按地图、小人和货柜区分跨帧扫描状态。
        private struct SupplySearchKey
        {
            private readonly int mapId;
            private readonly int pawnId;
            private readonly int storageId;

            //创建货源搜索键。
            public SupplySearchKey(int mapId, int pawnId, int storageId)
            {
                this.mapId = mapId;
                this.pawnId = pawnId;
                this.storageId = storageId;
            }

            //判断两个搜索键是否完全一致。
            public override bool Equals(object obj)
            {
                if (!(obj is SupplySearchKey other))
                    return false;

                return mapId == other.mapId && pawnId == other.pawnId && storageId == other.storageId;
            }

            //生成搜索键哈希，职责是让字典快速定位状态。
            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = mapId;
                    hash = hash * 397 ^ pawnId;
                    hash = hash * 397 ^ storageId;
                    return hash;
                }
            }
        }
    }
}
