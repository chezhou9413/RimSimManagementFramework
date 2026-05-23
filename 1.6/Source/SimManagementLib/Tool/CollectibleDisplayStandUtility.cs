using System;
using System.Collections.Generic;
using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.SimThingClass;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 提供收藏品展台的来源识别、搬运校验和缩小物生成工具。
    /// </summary>
    public static class CollectibleDisplayStandUtility
    {
        private const string CollectiblePrefix = "RSMF_Collections_";

        /// <summary>
        /// 判断 ThingDef 是否属于模拟经营框架收藏品。
        /// </summary>
        public static bool IsCollectibleDef(ThingDef def)
        {
            return def != null && def.defName.StartsWith(CollectiblePrefix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 从普通 Thing 或缩小物中读取真实收藏品实例。
        /// </summary>
        public static Thing GetCollectibleInnerThing(Thing thing)
        {
            if (thing is MinifiedThing minified)
                return minified.InnerThing;
            return thing;
        }

        /// <summary>
        /// 判断地图上的 Thing 是否可作为展台槽位来源。
        /// </summary>
        public static bool IsValidSourceThing(Thing thing)
        {
            if (thing == null || thing.Destroyed || !thing.Spawned)
                return false;

            Thing inner = GetCollectibleInnerThing(thing);
            if (inner == null || inner.Destroyed)
                return false;

            if (!IsCollectibleDef(inner.def))
                return false;

            return thing is MinifiedThing || inner.def.Minifiable;
        }

        /// <summary>
        /// 枚举当前地图可被放入展台的收藏品来源。
        /// </summary>
        public static IEnumerable<Thing> EnumerateAvailableSources(Map map, Building_CollectibleDisplayStand stand = null)
        {
            if (map == null)
                yield break;

            List<Thing> minified = map.listerThings?.ThingsInGroup(ThingRequestGroup.MinifiedThing);
            if (minified != null)
            {
                for (int i = 0; i < minified.Count; i++)
                {
                    Thing source = minified[i];
                    if (IsValidSourceThing(source) && !IsReservedByStand(stand, source))
                        yield return source;
                }
            }

            List<Building> buildings = map.listerBuildings?.allBuildingsColonist;
            if (buildings == null)
                yield break;

            for (int i = 0; i < buildings.Count; i++)
            {
                Thing source = buildings[i];
                if (source == stand)
                    continue;

                if (IsValidSourceThing(source) && !IsReservedByStand(stand, source))
                    yield return source;
            }
        }

        /// <summary>
        /// 根据运行时 ID 在地图上查找收藏品来源。
        /// </summary>
        public static Thing FindSourceById(Map map, int thingId)
        {
            if (thingId < 0)
                return null;

            foreach (Thing source in EnumerateAvailableSources(map))
            {
                if (source.thingIDNumber == thingId)
                    return source;
            }
            return null;
        }

        /// <summary>
        /// 判断小人是否能搬运指定来源到展台。
        /// </summary>
        public static bool CanPawnUseSource(Pawn pawn, Building_CollectibleDisplayStand stand, Thing source)
        {
            if (pawn == null || stand == null || source == null)
                return false;

            if (!stand.Spawned || stand.Destroyed || pawn.Map != stand.Map || source.Map != stand.Map)
                return false;

            if (!pawn.CanReserve(stand) || !pawn.CanReach(stand, PathEndMode.Touch, Danger.Deadly))
                return false;

            if (source.IsForbidden(pawn) || !pawn.CanReserve(source))
                return false;

            return pawn.CanReach(source, PathEndMode.ClosestTouch, Danger.Deadly);
        }

        /// <summary>
        /// 生成用于 UI 显示的来源名称，职责是区分地上缩小物和已摆放建筑。
        /// </summary>
        public static string SourceLabel(Thing source)
        {
            Thing inner = GetCollectibleInnerThing(source);
            string label = inner?.LabelCapNoCount ?? source?.LabelCapNoCount ?? "";
            return source is MinifiedThing ? label + "（已缩小）" : label + "（已摆放）";
        }

        /// <summary>
        /// 把已摆放收藏品转成缩小物并交给小人携带。
        /// </summary>
        public static bool TryStartCarryAsMinified(Pawn pawn, Thing source, out MinifiedThing carried)
        {
            carried = null;
            if (pawn?.carryTracker == null || !IsValidSourceThing(source))
                return false;

            MinifiedThing minified = source as MinifiedThing;
            if (minified == null)
            {
                minified = source.MakeMinified();
                if (minified == null)
                    return false;
            }

            if (minified.Spawned)
                minified.DeSpawnOrDeselect(DestroyMode.Vanish);

            minified.SetPositionDirect(pawn.Position);
            if (!pawn.carryTracker.TryStartCarry(minified))
                return false;

            carried = minified;
            return true;
        }

        /// <summary>
        /// 判断来源是否已被当前展台其他槽位等待，避免玩家重复选择同一个来源。
        /// </summary>
        private static bool IsReservedByStand(Building_CollectibleDisplayStand stand, Thing source)
        {
            return stand != null && source != null && stand.HasPendingSource(source.thingIDNumber);
        }
    }
}
