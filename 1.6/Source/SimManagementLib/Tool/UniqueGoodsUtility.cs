using RimWorld;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimThingComp;
using System.Collections.Generic;
using Verse;

namespace SimManagementLib.Tool
{
    //单件商品工具，职责是统一资格校验、来源枚举和实例标签生成。
    public static class UniqueGoodsUtility
    {
        //判断真实物品是否满足专业货柜的硬限制。
        public static bool IsEligible(Thing thing, Building_UniqueGoodsContainer container = null)
        {
            if (thing == null || thing.Destroyed || thing.def == null || thing.stackCount <= 0) return false;
            if (CompBiocodable.IsBiocoded(thing)) return false;
            if (thing is Apparel apparel && apparel.WornByCorpse) return false;

            ThingCompProperties_UniqueGoodsContainer props = container?.UniqueComp?.UniqueProps;
            if (props?.requireStuffOrQuality != false
                && !thing.def.MadeFromStuff
                && !thing.def.HasComp(typeof(CompQuality)))
                return false;

            if (props?.allowedThingCategories != null && props.allowedThingCategories.Count > 0)
            {
                bool allowed = false;
                for (int i = 0; i < props.allowedThingCategories.Count; i++)
                {
                    ThingCategoryDef category = props.allowedThingCategories[i];
                    if (category != null && thing.def.IsWithinCategory(category))
                    {
                        allowed = true;
                        break;
                    }
                }
                if (!allowed) return false;
            }
            return true;
        }

        //枚举地图上可以被指定货柜上架的真实物品。
        public static IEnumerable<Thing> EnumerateAvailableSources(Map map, Building_UniqueGoodsContainer container)
        {
            List<Thing> things = map?.listerThings?.ThingsInGroup(ThingRequestGroup.HaulableEver);
            if (things == null) yield break;
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (thing == null || !thing.Spawned || thing.Map != map || thing.IsForbidden(Faction.OfPlayer)) continue;
                if (!IsEligible(thing, container) || container.IsSourceAssignedAnywhere(thing.thingIDNumber)) continue;
                yield return thing;
            }
        }

        //按运行时编号查找来源物品，职责是同时识别地图物品和搬运者暂时携带的物品。
        public static Thing FindSource(Map map, int thingId, Building_UniqueGoodsContainer container)
        {
            if (map == null || thingId < 0) return null;
            List<Thing> things = map?.listerThings?.AllThings;
            if (things != null)
            {
                for (int i = 0; i < things.Count; i++)
                {
                    Thing thing = things[i];
                    if (thing != null && thing.thingIDNumber == thingId && thing.Spawned && thing.Map == map && IsEligible(thing, container))
                        return thing;
                }
            }

            IReadOnlyList<Pawn> pawns = map.mapPawns?.AllPawnsSpawned;
            if (pawns != null)
            {
                for (int i = 0; i < pawns.Count; i++)
                {
                    Thing carried = pawns[i]?.carryTracker?.CarriedThing;
                    if (carried != null && carried.thingIDNumber == thingId && IsEligible(carried, container))
                        return carried;
                }
            }
            return null;
        }

        //生成包含材质、品质、耐久和市价的实例摘要。
        public static string BuildDetails(Thing thing)
        {
            if (thing == null) return "";
            string stuff = thing.Stuff?.LabelCap ?? "-";
            string quality = thing.TryGetQuality(out QualityCategory q) ? q.GetLabel().CapitalizeFirst() : "-";
            float hp = thing.MaxHitPoints > 0 ? thing.HitPoints / (float)thing.MaxHitPoints : 1f;
            string position = thing.Spawned ? $" · ({thing.Position.x}, {thing.Position.z})" : "";
            return $"{stuff} · {quality} · {hp.ToStringPercent()} · ${thing.MarketValue:F0}{position}";
        }
    }
}
