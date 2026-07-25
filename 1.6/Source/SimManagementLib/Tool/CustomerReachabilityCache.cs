using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace SimManagementLib.Tool
{
    //类职责：缓存顾客短时间内的路径安全结果，确保一次查询只执行一次完整寻路。
    internal static class CustomerReachabilityCache
    {
        private const int CacheLifetimeTicks = 30;
        private const int MaxCacheEntries = 1024;
        private static readonly Dictionary<ReachabilityCacheKey, ReachabilityCacheEntry> Cache = new Dictionary<ReachabilityCacheKey, ReachabilityCacheEntry>();

        //判断顾客能否安全到达目标，职责是复用短期结果并检查路径上的玩家禁用门。
        public static bool CanReach(Pawn customer, LocalTargetInfo target, PathEndMode pathEndMode, Danger danger)
        {
            ReachabilityCacheKey key = ReachabilityCacheKey.Create(customer, target, pathEndMode, danger);
            int now = Find.TickManager?.TicksGame ?? 0;
            if (Cache.TryGetValue(key, out ReachabilityCacheEntry entry) && now < entry.ExpireTick)
                return entry.CanReach;

            bool canReach = EvaluatePath(customer, target, pathEndMode, danger);
            if (Cache.Count >= MaxCacheEntries)
                Cache.Clear();
            Cache[key] = new ReachabilityCacheEntry(canReach, now + CacheLifetimeTicks);
            return canReach;
        }

        //执行一次真实寻路，职责是同时完成可达性和禁用门校验。
        private static bool EvaluatePath(Pawn customer, LocalTargetInfo target, PathEndMode pathEndMode, Danger danger)
        {
            using (PawnPath path = customer.Map.pathFinder.FindPathNow(
                customer.Position,
                target,
                TraverseParms.For(customer, danger, TraverseMode.ByPawn),
                null,
                pathEndMode))
            {
                if (path == null || !path.Found)
                    return false;

                List<IntVec3> nodes = path.NodesReversed;
                for (int i = 0; i < nodes.Count; i++)
                {
                    Building_Door door = nodes[i].GetDoor(customer.Map);
                    if (CustomerSafetyUtility.IsPlayerForbiddenDoor(door))
                        return false;
                }
            }

            return true;
        }

        //结构职责：描述会影响顾客路径结果的稳定输入，用作短缓存键。
        private struct ReachabilityCacheKey : IEquatable<ReachabilityCacheKey>
        {
            private int mapId;
            private int pawnId;
            private int startCellIndex;
            private int targetCellIndex;
            private int targetThingId;
            private byte pathEndMode;
            private byte danger;

            //创建路径缓存键，职责是把地图格和目标对象转换成无引用值。
            public static ReachabilityCacheKey Create(Pawn pawn, LocalTargetInfo target, PathEndMode mode, Danger dangerValue)
            {
                Map map = pawn.Map;
                IntVec3 targetCell = target.Cell;
                return new ReachabilityCacheKey
                {
                    mapId = map.uniqueID,
                    pawnId = pawn.thingIDNumber,
                    startCellIndex = map.cellIndices.CellToIndex(pawn.Position),
                    targetCellIndex = targetCell.IsValid && targetCell.InBounds(map) ? map.cellIndices.CellToIndex(targetCell) : -1,
                    targetThingId = target.Thing?.thingIDNumber ?? -1,
                    pathEndMode = (byte)mode,
                    danger = (byte)dangerValue
                };
            }

            //比较两个路径缓存键，职责是保证所有寻路输入一致时才复用结果。
            public bool Equals(ReachabilityCacheKey other)
            {
                return mapId == other.mapId
                    && pawnId == other.pawnId
                    && startCellIndex == other.startCellIndex
                    && targetCellIndex == other.targetCellIndex
                    && targetThingId == other.targetThingId
                    && pathEndMode == other.pathEndMode
                    && danger == other.danger;
            }

            //比较缓存键对象，职责是支持字典键的标准相等判断。
            public override bool Equals(object obj)
            {
                return obj is ReachabilityCacheKey other && Equals(other);
            }

            //计算路径缓存键哈希，职责是均匀组合地图、Pawn、格子和路径参数。
            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = mapId;
                    hash = hash * 397 ^ pawnId;
                    hash = hash * 397 ^ startCellIndex;
                    hash = hash * 397 ^ targetCellIndex;
                    hash = hash * 397 ^ targetThingId;
                    hash = hash * 397 ^ pathEndMode;
                    return hash * 397 ^ danger;
                }
            }
        }

        //结构职责：保存一次路径判断结果及其失效 tick。
        private struct ReachabilityCacheEntry
        {
            public readonly bool CanReach;
            public readonly int ExpireTick;

            //创建路径缓存项，职责是绑定结果和短期有效时间。
            public ReachabilityCacheEntry(bool canReach, int expireTick)
            {
                CanReach = canReach;
                ExpireTick = expireTick;
            }
        }
    }
}
