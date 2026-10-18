using System.Collections.Generic;
using System.Linq;
using RimWorld;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimThingComp;
using SimManagementLib.Tool;
using Verse;

namespace SimManagementLib.BusinessAdvisor
{
    //查询殖民者可执行的工作，让顾问先查能力再分配实际任务。
    internal static partial class AdvisorQueries
    {
        //等待清理或施工时只轮询指定区域，不反复调用模型，也不改变游戏速度。
        internal static int WorkRemaining(AdvisorQuery q, out string detail)
        {
            if (q.shopId.HasValue) return RestockRemaining(q, out detail);
            Map map = Map(Required(q.mapId, "mapId"));
            int width = Required(q.width, "width"), height = Required(q.height, "height");
            if (width < 1 || height < 1 || width > 50 || height > 50) throw new System.InvalidOperationException("等待工作范围每边需为 1~50 格。");
            var rect = new CellRect(Required(q.x, "x"), Required(q.z, "z"), width, height);
            if (!rect.InBounds(map)) throw new System.InvalidOperationException("等待工作范围超出地图。");
            var things = new HashSet<int>();
            var orders = new HashSet<Designation>();
            int construction = 0, roofs = 0;
            foreach (IntVec3 cell in rect)
            {
                if (cell.Fogged(map)) throw new System.InvalidOperationException("等待工作范围包含迷雾。");
                orders.UnionWith(map.designationManager.AllDesignationsAt(cell));
                if (map.areaManager.BuildRoof[cell] && !cell.Roofed(map) || map.areaManager.NoRoof[cell] && cell.Roofed(map)) roofs++;
                foreach (Thing thing in cell.GetThingList(map))
                    if (things.Add(thing.thingIDNumber))
                    {
                        if (thing is Blueprint || thing is Frame) construction++;
                    }
            }
            detail = "mapId=" + map.uniqueID + " rect=" + rect + " remainingOrders=" + orders.Count + " construction=" + construction + " roofCells=" + roofs;
            return orders.Count + construction + roofs;
        }

        //店铺等待只观察已配置的库存需求，专业货柜区分待搬运与实际入柜。
        private static int RestockRemaining(AdvisorQuery q, out string detail)
        {
            var shop = Shop(Required(q.mapId, "mapId"), q.shopId.Value);
            int remaining = 0;
            var rows = new List<string>();
            foreach (var storage in ShopDataUtility.GetStorageSnapshotInZone(shop))
            {
                if (storage is Building_UniqueGoodsContainer unique)
                {
                    int pending = unique.AdvisorSlots.Count(s => s.HasPendingSource);
                    remaining += pending;
                    if (pending > 0) rows.Add("containerId=" + storage.thingIDNumber + " pendingSources=" + pending);
                }
                else
                {
                    var comp = storage.GetComp<ThingComp_GoodsData>();
                    if (comp == null) continue;
                    foreach (var pair in comp.itemData.Where(p => p.Value.enabled && p.Value.count > 0))
                    {
                        ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(pair.Key);
                        int stored = def == null ? 0 : storage.CountStored(def);
                        if (stored >= pair.Value.count) continue;
                        remaining++;
                        if (rows.Count < 20) rows.Add("containerId=" + storage.thingIDNumber + " definition=" + pair.Key + " stored=" + stored + " target=" + pair.Value.count);
                    }
                }
            }
            detail = "mode=restock shopId=" + shop.ID + " remainingStockTargets=" + remaining + "\n" + string.Join("\n", rows.Take(20))
                + "\n仅观察已配置的店铺库存需求，不代表施工、营业条件或全地图搬运已完成。";
            return remaining;
        }

        //输出原版工作开关、优先级、技能和当前任务，不改变工作安排。
        private static IEnumerable<string> ColonistWork(AdvisorQuery q)
        {
            yield return "manualPriorities=" + Find.PlaySettings.useWorkPriorities;
            foreach (Map map in Maps(q))
                foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned.Where(p => !q.targetId.HasValue || p.thingIDNumber == q.targetId))
                {
                    yield return ThingRow(pawn) + " drafted=" + pawn.Drafted + " downed=" + pawn.Downed
                        + " mentalState=" + pawn.MentalStateDef?.defName + " job=" + pawn.CurJobDef?.defName
                        + " work=" + WorkPriorities(pawn)
                        + " disabled=" + string.Join(",", DefDatabase<WorkTypeDef>.AllDefsListForReading.Where(pawn.WorkTypeIsDisabled).Select(w => w.defName))
                        + " skills=" + (pawn.skills == null ? "" : string.Join(",", pawn.skills.skills.Select(s => s.def.defName + ":" + s.Level)));
                }
        }
    }
}
