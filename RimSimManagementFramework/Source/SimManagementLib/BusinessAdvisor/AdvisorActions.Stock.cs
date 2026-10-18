using System;
using System.Linq;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimThingComp;
using SimManagementLib.Tool;
using Verse;

namespace SimManagementLib.BusinessAdvisor
{
    //复用货柜配置和搬运请求入口，不直接增减实物库存。
    internal static partial class AdvisorActions
    {
        //校验分类、商品资格、总容量与阈值，再生成货柜设置草稿。
        private static AdvisorPreparedStep Shelf(AdvisorAction a, Map map)
        {
            var storage = Target(map, a.targetId) as Building_SimContainer;
            if (storage == null || storage is Building_UniqueGoodsContainer) throw new InvalidOperationException("需要普通货柜，专业衣架使用 unique 操作。");
            var comp = storage.GetComp<ThingComp_GoodsData>();
            if (comp == null || string.IsNullOrEmpty(a.category) || GoodsCatalog.GetCategory(a.category) == null || !comp.AllowsGoodsCategory(a.category))
                throw new InvalidOperationException("货柜 #" + storage.thingIDNumber + " 不允许 category=" + a.category
                    + "；当前 category=" + comp?.ActiveGoodsDefName + "；允许的分类 ID=" + AdvisorQueries.AllowedCategoryIds(comp)
                    + "。请查询 inventory 后使用真实 ID；当前分类合适时无需重新设置。");
            var data = comp.CloneItemData();
            string before = comp.ActiveGoodsDefName + "/" + string.Join(";", data.OrderBy(p => p.Key).Select(p => p.Key + ":" + AdvisorQueries.ScalarFields(p.Value)));
            string description;
            if (a.kind == "shelf_category")
            {
                description = "分类：" + comp.ActiveGoodsDefName + " → " + a.category + "\n清空原商品配置 " + data.Count + " 项，实物由正常搬运整理。";
                data.Clear();
            }
            else
            {
                if (comp.ActiveGoodsDefName != a.category) throw new InvalidOperationException("请先独立批准 shelf_category，再按新分类配置商品。");
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(a.definition ?? "");
                if (def == null) throw new InvalidOperationException("商品 definition=" + a.definition + " 不存在，请查询 shelf_goods 获取真实定义。");
                if (!GoodsCatalog.Contains(a.category, def))
                    throw new InvalidOperationException(def.defName + " 未注册到分类 " + a.category + "；请用 shelf_goods 指定 mapId 和货柜 targetId 查询，不能根据物品名称猜分类。");
                if (!storage.AllowsInventoryItem(def))
                    throw new InvalidOperationException("货柜 #" + storage.thingIDNumber + " 的物品硬限制拒绝 " + def.defName
                        + "（MadeFromStuff=" + def.MadeFromStuff + "，HasQuality=" + def.HasComp(typeof(RimWorld.CompQuality))
                        + "）。允许分类不代表接受该分类的全部物品；有品质/材质的武器服装请查专业货柜和 unique_sources，不要继续换同类物品盲试。");
                int count = AdvisorQueries.Required(a.count, "count"), threshold = AdvisorQueries.Required(a.threshold, "threshold");
                float price = Price(a.price);
                if (!a.enabled.HasValue || count < 0 || threshold < 0 || threshold > count || (!a.enabled.Value && count != 0))
                    throw new InvalidOperationException("目标库存或补货阈值无效，禁用商品时 count 必须为 0。");
                int otherConfigured = data.Where(p => p.Key != def.defName && p.Value.enabled).Sum(p => p.Value.count);
                if ((long)otherConfigured + count > storage.MaxTotalCapacity)
                    throw new InvalidOperationException("货柜 #" + storage.thingIDNumber + " 容量=" + storage.MaxTotalCapacity
                        + "，其他启用商品目标=" + otherConfigured + "，本商品请求=" + count + "；本商品最多可配置 "
                        + Math.Max(0, storage.MaxTotalCapacity - otherConfigured) + "。请缩小 count，并保持 threshold<=count。");
                data.TryGetValue(def.defName, out var previous);
                var next = new GoodsItemData { enabled = a.enabled.Value, count = count, restockThreshold = threshold, price = price };
                data[def.defName] = next;
                description = def.LabelCap + " (" + def.defName + ")\n" + AdvisorQueries.ScalarFields(previous) + " → " + AdvisorQueries.ScalarFields(next);
            }
            var normalized = storage.ClampSettingsToCapacity(a.category, data, out int trimmed);
            if (trimmed != 0 || data.Any(p => !normalized.TryGetValue(p.Key, out var n)
                || AdvisorQueries.ScalarFields(p.Value) != AdvisorQueries.ScalarFields(n)))
                throw new InvalidOperationException("现有货柜配置含容量或资格冲突，应用时会影响其他商品；请先在货柜管理中整理配置。");
            return new AdvisorPreparedStep { resource = "shelf:" + storage.thingIDNumber, before = before,
                preview = "货柜：" + storage.LabelCap + " (#" + storage.thingIDNumber + ") " + storage.Position + "\n" + description,
                apply = () => comp.ApplySettings(a.category, data) };
        }

        //针对真实单件商品生成搬运或定价步骤，保留槽位身份检查。
        private static AdvisorPreparedStep UniqueShelf(AdvisorAction a, Map map)
        {
            var shelf = Target(map, a.targetId) as Building_UniqueGoodsContainer;
            if (shelf == null) throw new InvalidOperationException("需要专业货柜。");
            string before = string.Join(";", shelf.AdvisorSlots.Select(AdvisorQueries.ScalarFields));
            if (a.kind == "unique_price")
            {
                int index = AdvisorQueries.Required(a.slot, "slot");
                var slot = shelf.AdvisorSlots.FirstOrDefault(s => s.index == index);
                if (slot == null) throw new InvalidOperationException("槽位不存在。");
                float price = Price(a.price);
                return new AdvisorPreparedStep { resource = "shelf:" + shelf.thingIDNumber, before = before,
                    preview = "专业货柜 #" + shelf.thingIDNumber + " / 槽位 " + index + " 售价：" + slot.price + " → " + price,
                    apply = () => shelf.SetSlotPrice(index, price) };
            }
            Thing source = Target(map, a.pawnId, false);
            string issue = shelf.AdvisorSourceIssue(source);
            if (issue.Length > 0) throw new InvalidOperationException("货柜 #" + shelf.thingIDNumber + "，物品 #" + source.thingIDNumber + "：" + issue);
            before += "/source:" + source.thingIDNumber + "/" + source.Position + "/" + source.stackCount;
            return new AdvisorPreparedStep { resource = "shelf:" + shelf.thingIDNumber, before = before,
                preview = "上架搬运：" + source.LabelCap + " (#" + source.thingIDNumber + ") " + source.Position + " → " + shelf.LabelCap
                    + " (#" + shelf.thingIDNumber + ")；初始售价=" + Math.Max(1f, source.MarketValue)
                    + "；仅登记待搬运来源，尚未入柜，不能宣称已经上架。",
                apply = () => { if (!shelf.TryQueueSource(source, out string reason)) throw new InvalidOperationException(reason); } };
        }

        //拒绝缺失、非有限或负售价。
        private static float Price(float? value)
        {
            if (!value.HasValue || float.IsNaN(value.Value) || float.IsInfinity(value.Value) || value.Value < 0 || value.Value > 100000000)
                throw new InvalidOperationException("售价需要 0~100000000 的有限数值。");
            return value.Value;
        }
    }
}
