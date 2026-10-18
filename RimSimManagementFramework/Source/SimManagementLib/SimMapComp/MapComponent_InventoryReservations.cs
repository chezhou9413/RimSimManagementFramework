using System;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using SimManagementLib.Pojo;
using SimManagementLib.SimThingClass;
using Verse;

namespace SimManagementLib.SimMapComp
{
    //协调地图上跨商店的实物预留，职责是为制作、配送和补货提供一致的库存占用记录。
    public sealed class MapComponent_InventoryReservations : MapComponent
    {
        private List<InventoryReservation> reservations = new List<InventoryReservation>();
        private List<InventoryProtection> protectedThings = new List<InventoryProtection>();
        private Dictionary<Thing, List<InventoryReservation>> byThing;
        private Dictionary<string, List<InventoryReservation>> byKey;

        //绑定库存预留所在地图。
        public MapComponent_InventoryReservations(Map map) : base(map) { }

        //计算业务已经预留的数量，职责是允许调用方排除自己的业务标识。
        public int Reserved(Thing thing, string exceptKey = null)
        {
            EnsureIndices();
            return thing != null && byThing.TryGetValue(thing, out var list) ? list.Where(r => r.key != exceptKey).Sum(r => r.count) : 0;
        }

        //计算当前可分配数量，职责是同时避让已有业务预留和原版搬运任务。
        public int Available(Thing thing, string ownKey = null)
        {
            if (thing == null || thing.Destroyed || thing.MapHeld != map) return 0;
            if (thing.ParentHolder is Building_SimContainer storage && storage.HasPendingInventoryWithdrawal(thing.def)) return 0;
            if (ownKey == null && thing.Spawned && map.reservationManager.IsReserved(thing)) return 0;
            return Math.Max(0, thing.stackCount - Reserved(thing, ownKey));
        }

        //原子登记整组预留，职责是在任一物资不足时保持原有库存记录。
        public bool Reserve(string key, IList<ThingCount> requested)
        {
            if (string.IsNullOrEmpty(key) || requested == null || requested.Count == 0) return false;
            var totals = requested.GroupBy(t => t.Thing).Select(g => new ThingCount(g.Key, g.Sum(t => t.Count))).ToList();
            if (totals.Any(t => t.Count <= 0 || Available(t.Thing, key) < t.Count)) return false;
            Release(key);
            foreach (var item in totals)
            {
                reservations.Add(new InventoryReservation { key = key, thing = item.Thing, count = item.Count });
                Protect(item.Thing);
            }
            byThing = null;
            return true;
        }

        //返回业务预留快照，职责是不向调用方暴露可变记录集合。
        public List<ThingCount> Query(string key)
        {
            EnsureIndices();
            //查询保留账面数量，让业务核验实物缺损，不能由 ThingCount 构造器截断数量或反复打印警告。
            return byKey.TryGetValue(key, out var list)
                ? list.Select(r => r.thing == null ? default(ThingCount) : new ThingCount(r.thing, r.count, true)).ToList()
                : new List<ThingCount>();
        }

        //提取指定预留实物，职责是保留组件状态且把预留随拆出的实际物品移动。
        public Thing Extract(string key, Thing source, int count, ThingOwner destination)
        {
            return Extract(key, source, count, destination, out _);
        }

        //提取实物并返回拒绝原因，职责是让取料岗位区分预留、数量、位置与容器容量错误。
        public Thing Extract(string key, Thing source, int count, ThingOwner destination, out string failReason)
        {
            failReason = "";
            var record = reservations.FirstOrDefault(r => r.key == key && r.thing == source);
            if (source == null || source.Destroyed || source.MapHeld != map)
                return RejectExtraction("预留实物不存在或已离开当前地图", out failReason);
            if (record == null)
                return RejectExtraction($"{key} 未预留 {source}", out failReason);
            if (count <= 0 || count > record.count || count > source.stackCount)
                return RejectExtraction($"提取数量无效：请求={count}，本单预留={record.count}，实物={source.stackCount}", out failReason);
            if (destination == null)
                return RejectExtraction("接收容器不存在", out failReason);
            if (destination.Owner is Map)
                return RejectExtraction("地图不能作为接收容器，地面放置必须使用生成接口", out failReason);
            if (source.holdingOwner == destination)
                return source.stackCount == count ? source : RejectExtraction("实物已在接收容器内，但数量与本次交接不符", out failReason);
            int capacity = destination.GetCountCanAccept(source, false);
            if (capacity < count)
                return RejectExtraction($"接收容器空间不足：请求={count}，可接收={capacity}", out failReason);
            bool wasForbidden = protectedThings.FirstOrDefault(p => p.thing == source)?.wasForbidden ?? source.IsForbidden(Faction.OfPlayer);
            var storage = source.ParentHolder as Building_SimContainer;
            Thing part;
            //地图上的实物同样有 holdingOwner，必须先按生成状态区分，不能把地图当成普通容器。
            if (source.Spawned)
            {
                IntVec3 origin = source.Position;
                part = source.SplitOff(count);
                if (!destination.TryAdd(part, false))
                {
                    //完整堆恢复原位；部分堆合回来源，保持原有预留数量和物品引用有效。
                    if (part == source) GenSpawn.Spawn(part, origin, map);
                    else if (!source.TryAbsorbStack(part, false))
                    {
                        GenSpawn.Spawn(part, origin, map);
                        throw new InvalidOperationException("预留食材交接失败后无法合回来源：" + source);
                    }
                    return RejectExtraction("接收容器拒绝拆出的地面实物", out failReason);
                }
            }
            else if (source.holdingOwner != null)
            {
                if (source.holdingOwner.TryTransferToContainer(source, destination, count, out part, false) != count)
                    return RejectExtraction("持有容器未能转移完整的预留数量", out failReason);
            }
            else return RejectExtraction("实物既未生成在地图，也不在持有容器内", out failReason);
            record.count -= count;
            if (record.count == 0) reservations.Remove(record);
            reservations.Add(new InventoryReservation { key = key, thing = part, count = count });
            Protect(part, wasForbidden);
            byThing = null;
            RestoreUnusedProtection();
            storage?.NotifyInventoryChanged();
            return part;
        }

        //返回提取失败详情，职责是保持所有拒绝分支的返回约定一致。
        private static Thing RejectExtraction(string reason, out string failReason)
        {
            failReason = reason;
            return null;
        }

        //释放业务预留，职责是让取消、消费和配置变更归还库存分配权。
        public void Release(string key)
        {
            reservations.RemoveAll(r => r.key == key);
            byThing = null;
            RestoreUnusedProtection();
        }

        //保存跨岗位预留，职责是读档后清除已经不存在的实物引用。
        public override void ExposeData()
        {
            Scribe_Collections.Look(ref reservations, "inventoryReservations", LookMode.Deep);
            Scribe_Collections.Look(ref protectedThings, "inventoryProtection", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                reservations.RemoveAll(r => r.thing == null || r.thing.Destroyed || r.count <= 0);
                byThing = null;
                RestoreUnusedProtection();
            }
        }
        //判断禁用标记是否仅由库存预留设置，职责是允许业务查询剩余未占用数量。
        public bool IsProtected(Thing thing) => protectedThings.Any(p => p.thing == thing && !p.wasForbidden);

        //记录并设置临时禁用，职责是防止原版普通搬运抢走已接单实物。
        private void Protect(Thing thing, bool? original = null)
        {
            if (!protectedThings.Any(p => p.thing == thing))
                protectedThings.Add(new InventoryProtection { thing = thing, wasForbidden = original ?? thing.IsForbidden(Faction.OfPlayer) });
            thing.SetForbidden(true, false);
        }

        //恢复不再被业务占用的物品，职责是不遗留永久禁用。
        private void RestoreUnusedProtection()
        {
            foreach (var entry in protectedThings.ToList())
            {
                if (entry.thing != null && !entry.thing.Destroyed && reservations.Any(r => r.thing == entry.thing)) continue;
                if (entry.thing != null && !entry.thing.Destroyed) entry.thing.SetForbidden(entry.wasForbidden, false);
                protectedThings.Remove(entry);
            }
        }

        //按需建立预留索引，职责是避免同一轮菜单和补货查询反复扫描完整预留集合。
        private void EnsureIndices()
        {
            if (byThing != null) return;
            byThing = reservations.Where(r => r.thing != null).GroupBy(r => r.thing).ToDictionary(g => g.Key, g => g.ToList());
            byKey = reservations.GroupBy(r => r.key).ToDictionary(g => g.Key, g => g.ToList());
        }
    }
}
