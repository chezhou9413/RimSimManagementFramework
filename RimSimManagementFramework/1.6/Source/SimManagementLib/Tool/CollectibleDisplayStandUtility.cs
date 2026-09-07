using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.SimThingComp;
using SimManagementLib.SimThingClass;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SimManagementLib.Tool
{
    //收藏品展台工具，职责是识别可展示来源、校验搬运条件并生成搬运用缩小物。
    public static class CollectibleDisplayStandUtility
    {
        private static readonly FieldInfo BlueprintMiniToInstallField = AccessTools.Field(typeof(Blueprint_Install), "miniToInstall");
        private static readonly FieldInfo BlueprintBuildingToReinstallField = AccessTools.Field(typeof(Blueprint_Install), "buildingToReinstall");

        //判断 ThingDef 是否声明为可被收藏品展台展示。
        public static bool IsCollectibleDef(ThingDef def)
        {
            return def != null && def.HasComp<ThingComp_DisplayStandCollectible>();
        }

        //从普通 Thing 或缩小物中读取真实收藏品实例。
        public static Thing GetCollectibleInnerThing(Thing thing)
        {
            if (thing is MinifiedThing minified)
                return minified.InnerThing;
            return thing;
        }

        //判断地图上的 Thing 是否可作为展台槽位来源。
        public static bool IsValidSourceThing(Thing thing)
        {
            if (thing == null || thing.Destroyed || !thing.Spawned)
                return false;

            if (IsReservedByInstallBlueprint(thing))
                return false;

            Thing inner = GetCollectibleInnerThing(thing);
            if (inner == null || inner.Destroyed)
                return false;

            if (!IsCollectibleDef(inner.def))
                return false;

            return thing is MinifiedThing || inner.def.Minifiable;
        }

        //枚举当前地图可被放入展台的收藏品来源。
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

        //根据运行时 ID 在地图上查找收藏品来源。
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

        //判断小人是否能搬运指定来源到展台。
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

        //判断来源是否已经被原版安装蓝图占用，职责是避免展台搬运把待安装物掏空。
        public static bool IsReservedByInstallBlueprint(Thing source)
        {
            Map map = source?.Map;
            if (map?.listerThings == null)
                return false;

            List<Thing> blueprints = map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint);
            if (blueprints == null)
                return false;

            for (int i = 0; i < blueprints.Count; i++)
            {
                if (!(blueprints[i] is Blueprint_Install blueprint))
                    continue;

                if (BlueprintMiniToInstallField?.GetValue(blueprint) == source)
                    return true;

                if (BlueprintBuildingToReinstallField?.GetValue(blueprint) == source)
                    return true;
            }
            return false;
        }

        //生成用于 UI 显示的来源名称，职责是区分地上缩小物和已摆放建筑。
        public static string SourceLabel(Thing source)
        {
            Thing inner = GetCollectibleInnerThing(source);
            string label = inner?.LabelCapNoCount ?? source?.LabelCapNoCount ?? "";
            return source is MinifiedThing ? label + "（已缩小）" : label + "（已摆放）";
        }

        //把已摆放收藏品转成缩小物并交给小人携带。
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

        //判断来源是否已被当前展台其他槽位等待，避免玩家重复选择同一个来源。
        private static bool IsReservedByStand(Building_CollectibleDisplayStand stand, Thing source)
        {
            return stand != null && source != null && stand.HasPendingSource(source.thingIDNumber);
        }
    }
}
