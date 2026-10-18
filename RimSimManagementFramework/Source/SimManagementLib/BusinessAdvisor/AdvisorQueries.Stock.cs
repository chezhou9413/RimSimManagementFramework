using System.Collections.Generic;
using System.Linq;
using RimWorld;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimThingComp;
using SimManagementLib.Tool;
using Verse;

namespace SimManagementLib.BusinessAdvisor
{
    //按目标货柜返回候选实物和不可用原因，减少通过失败操作试探资格。
    internal static partial class AdvisorQueries
    {
        //只返回本柜当前分类实际接受的商品，容量按保留其他商品配置后的可用量计算。
        private static IEnumerable<string> ShelfGoods(AdvisorQuery q)
        {
            Map map = Map(Required(q.mapId, "mapId"));
            int id = Required(q.targetId, "targetId（普通货柜编号）");
            var shelf = map.listerThings.AllThings.OfType<Building_SimContainer>().FirstOrDefault(s => s.thingIDNumber == id);
            if (shelf == null || shelf is Building_UniqueGoodsContainer)
                throw new System.InvalidOperationException("shelf_goods 需要普通货柜；专业货柜请查 unique_sources。");
            var comp = shelf.GetComp<ThingComp_GoodsData>();
            var category = GoodsCatalog.GetCategory(comp?.ActiveGoodsDefName);
            if (category == null || !comp.AllowsGoodsCategory(category.categoryId))
                throw new System.InvalidOperationException("当前分类无效，请先查询 inventory 并设置该柜允许的分类。");
            var ground = map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver)
                .Where(t => t.Spawned && !t.IsForbidden(Faction.OfPlayer)).GroupBy(t => t.def)
                .ToDictionary(g => g.Key, g => g.Sum(t => (long)t.stackCount));
            long configured = comp.itemData.Where(p => p.Value.enabled).Sum(p => (long)p.Value.count);
            yield return "containerId=" + id + " category=" + category.categoryId + " capacity=" + shelf.MaxTotalCapacity
                + " configuredTotal=" + configured + "；以下条目已通过分类和货柜硬限制。groundAvailable 只统计地面已解禁实物，不保证员工可达或可预留。";
            foreach (var item in category.Items)
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(item.thingDefName);
                if (def == null || !shelf.AllowsInventoryItem(def) || !Match(def.defName + " " + def.label, q.filter)) continue;
                comp.itemData.TryGetValue(def.defName, out var current);
                long others = configured - (current?.enabled == true ? current.count : 0);
                ground.TryGetValue(def, out long available);
                yield return "containerId=" + id + " category=" + category.categoryId + " definition=" + def.defName + " label=" + Q(def.label)
                    + " maxConfigCount=" + System.Math.Max(0, shelf.MaxTotalCapacity - others) + " groundAvailable=" + available
                    + " stored=" + shelf.CountStored(def) + " market=" + item.baseMarketValue + " current=" + ScalarFields(current);
            }
        }

        //保留禁用候选并明确标记；不把它们藏起来，也不自动解禁。
        private static IEnumerable<string> UniqueSources(AdvisorQuery q)
        {
            Map map = Map(Required(q.mapId, "mapId"));
            int id = Required(q.targetId, "targetId（专业货柜编号）");
            var shelf = map.listerThings.AllThings.OfType<Building_UniqueGoodsContainer>().FirstOrDefault(s => s.thingIDNumber == id)
                ?? throw new System.InvalidOperationException("该 targetId 不是本地图的专业货柜，请查 inventory。");
            foreach (Thing source in map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver))
            {
                if (!UniqueGoodsUtility.IsEligibleDef(source.def, shelf)) continue;
                string row = ThingRow(source);
                if (!Match(row, q.filter)) continue;
                string issue = shelf.AdvisorSourceIssue(source);
                yield return row + " containerId=" + id + " canQueue=" + (issue.Length == 0) + " reason=" + Q(issue)
                    + "；仅检查来源与槽位资格，不保证工人的可达性和实际搬运时间。";
            }
        }
    }
}
