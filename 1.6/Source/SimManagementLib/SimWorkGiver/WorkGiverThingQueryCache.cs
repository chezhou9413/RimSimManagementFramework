using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimWorkGiver
{
    /// <summary>
    /// 缓存 WorkGiver 预筛选阶段的寻路和预约查询，负责减少同一批候选被多名员工重复调用原版重函数。
    /// </summary>
    internal static class WorkGiverThingQueryCache
    {
        private const int MaxCacheEntries = 2048;
        private static readonly Dictionary<ThingQueryKey, BoolCacheEntry> boolCache = new Dictionary<ThingQueryKey, BoolCacheEntry>();

        /// <summary>
        /// 短时间缓存 pawn 到目标 Thing 的可达性，负责降低 HasJobOnThing 阶段的原版寻路查询频率。
        /// </summary>
        public static bool CanReachThingCached(Pawn pawn, Thing target, PathEndMode pathEndMode, Danger danger, int cacheTicks)
        {
            if (!IsValidPawnThingPair(pawn, target))
                return false;

            ThingQueryKey key = ThingQueryKey.ForReach(pawn, target, pathEndMode, danger);
            if (TryGetCachedValue(key, out bool cached))
                return cached;

            bool result = pawn.CanReach(target, pathEndMode, danger);
            StoreCachedValue(key, result, cacheTicks);
            return result;
        }

        /// <summary>
        /// 短时间缓存 pawn 对目标 Thing 的预约结果，负责减少同一 tick 附近重复访问 ReservationManager。
        /// </summary>
        public static bool CanReserveThingCached(Pawn pawn, Thing target, int maxPawns, int stackCount, bool ignoreOtherReservations, int cacheTicks)
        {
            if (!IsValidPawnThingPair(pawn, target))
                return false;

            ThingQueryKey key = ThingQueryKey.ForReserve(pawn, target, maxPawns, stackCount, ignoreOtherReservations);
            if (TryGetCachedValue(key, out bool cached))
                return cached;

            bool result = pawn.CanReserve(target, maxPawns, stackCount, null, ignoreOtherReservations);
            StoreCachedValue(key, result, cacheTicks);
            return result;
        }

        /// <summary>
        /// 判断 pawn 和目标是否可用于缓存查询，负责避免缓存已经离图或销毁的对象。
        /// </summary>
        private static bool IsValidPawnThingPair(Pawn pawn, Thing target)
        {
            return pawn?.Map != null
                && target?.Map == pawn.Map
                && pawn.Spawned
                && target.Spawned
                && !pawn.Destroyed
                && !target.Destroyed;
        }

        /// <summary>
        /// 从缓存读取布尔查询结果，负责按过期 tick 自动丢弃旧结果。
        /// </summary>
        private static bool TryGetCachedValue(ThingQueryKey key, out bool value)
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            if (boolCache.TryGetValue(key, out BoolCacheEntry entry) && now <= entry.expireTick)
            {
                value = entry.value;
                return true;
            }

            value = false;
            return false;
        }

        /// <summary>
        /// 写入布尔查询缓存，并在缓存过大时清理过期项。
        /// </summary>
        private static void StoreCachedValue(ThingQueryKey key, bool value, int cacheTicks)
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            boolCache[key] = new BoolCacheEntry
            {
                value = value,
                expireTick = now + System.Math.Max(1, cacheTicks)
            };

            if (boolCache.Count > MaxCacheEntries)
                RemoveExpiredEntries(now);
        }

        /// <summary>
        /// 移除已经过期的缓存项，负责避免长时间游戏后缓存无限增长。
        /// </summary>
        private static void RemoveExpiredEntries(int now)
        {
            List<ThingQueryKey> expired = new List<ThingQueryKey>();
            foreach (KeyValuePair<ThingQueryKey, BoolCacheEntry> entry in boolCache)
            {
                if (now > entry.Value.expireTick)
                    expired.Add(entry.Key);
            }

            for (int i = 0; i < expired.Count; i++)
                boolCache.Remove(expired[i]);
        }

        /// <summary>
        /// 表示一个缓存布尔值，负责记录查询结果和过期 tick。
        /// </summary>
        private struct BoolCacheEntry
        {
            public bool value;
            public int expireTick;
        }

        /// <summary>
        /// 表示一次 pawn 到 Thing 的查询键，负责把位置、目标和查询参数纳入缓存区分。
        /// </summary>
        private struct ThingQueryKey
        {
            private int mapId;
            private int pawnId;
            private int targetId;
            private int pawnCell;
            private int targetCell;
            private int queryKind;
            private int argA;
            private int argB;
            private int argC;

            /// <summary>
            /// 构建可达性查询键，负责按寻路模式和危险等级区分缓存。
            /// </summary>
            public static ThingQueryKey ForReach(Pawn pawn, Thing target, PathEndMode pathEndMode, Danger danger)
            {
                ThingQueryKey key = CreateBase(pawn, target, 1);
                key.argA = (int)pathEndMode;
                key.argB = (int)danger;
                return key;
            }

            /// <summary>
            /// 构建预约查询键，负责按预约参数区分缓存。
            /// </summary>
            public static ThingQueryKey ForReserve(Pawn pawn, Thing target, int maxPawns, int stackCount, bool ignoreOtherReservations)
            {
                ThingQueryKey key = CreateBase(pawn, target, 2);
                key.argA = maxPawns;
                key.argB = stackCount;
                key.argC = ignoreOtherReservations ? 1 : 0;
                return key;
            }

            /// <summary>
            /// 构建通用查询键基础字段，负责捕获 pawn 和目标当前所在格。
            /// </summary>
            private static ThingQueryKey CreateBase(Pawn pawn, Thing target, int queryKind)
            {
                return new ThingQueryKey
                {
                    mapId = pawn.Map.uniqueID,
                    pawnId = pawn.thingIDNumber,
                    targetId = target.thingIDNumber,
                    pawnCell = CellKey(pawn.Position),
                    targetCell = CellKey(target.Position),
                    queryKind = queryKind
                };
            }

            /// <summary>
            /// 把地图格压成稳定整数，负责减少结构体键中的字段数量。
            /// </summary>
            private static int CellKey(IntVec3 cell)
            {
                unchecked
                {
                    return cell.x * 397 ^ cell.z;
                }
            }

            /// <summary>
            /// 判断两个查询键是否完全相同，负责 Dictionary 精确匹配。
            /// </summary>
            public override bool Equals(object obj)
            {
                if (!(obj is ThingQueryKey other))
                    return false;

                return mapId == other.mapId
                    && pawnId == other.pawnId
                    && targetId == other.targetId
                    && pawnCell == other.pawnCell
                    && targetCell == other.targetCell
                    && queryKind == other.queryKind
                    && argA == other.argA
                    && argB == other.argB
                    && argC == other.argC;
            }

            /// <summary>
            /// 生成查询键哈希，负责让 Dictionary 快速定位缓存项。
            /// </summary>
            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = mapId;
                    hash = hash * 397 ^ pawnId;
                    hash = hash * 397 ^ targetId;
                    hash = hash * 397 ^ pawnCell;
                    hash = hash * 397 ^ targetCell;
                    hash = hash * 397 ^ queryKind;
                    hash = hash * 397 ^ argA;
                    hash = hash * 397 ^ argB;
                    hash = hash * 397 ^ argC;
                    return hash;
                }
            }
        }
    }
}
