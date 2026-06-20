using SimManagementLib.SimDef;
using SimManagementLib.SimThingComp;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimThingClass
{
    /// <summary>
    /// 提供商店货柜容量、目标库存和补货预约数量的统计能力。
    /// </summary>
    public partial class Building_SimContainer
    {
        /// <summary>
        /// 统计货柜当前已经实际存入的总件数。
        /// </summary>
        public int CountTotalStored()
        {
            RebuildStoredCountCacheIfNeeded();
            return cachedTotalStored;
        }

        /// <summary>
        /// 统计货柜当前仍在路上的补货数量。
        /// </summary>
        public int CountTotalPendingIn(bool forceReconcile = false)
        {
            if (forceReconcile)
                ReconcilePendingReservations();
            else
                ReconcilePendingReservationsIfNeeded();
            int total = 0;
            if (pendingIn == null || pendingIn.Count == 0) return 0;

            foreach (int value in pendingIn.Values)
            {
                total += UnityEngine.Mathf.Max(0, value);
            }

            return total;
        }

        /// <summary>
        /// 清理没有对应补货任务的待入库数量，负责修正任务中断或旧存档残留的“途中”显示。
        /// </summary>
        public void ClearOrphanedPendingIn()
        {
            if (pendingIn == null || pendingIn.Count == 0) return;

            int now = Find.TickManager?.TicksGame ?? 0;
            List<ThingDef> removeDefs = null;
            Dictionary<ThingDef, int> trimDefs = null;
            foreach (KeyValuePair<ThingDef, int> entry in pendingIn.ToList())
            {
                ThingDef thingDef = entry.Key;
                int activeCount = CountActiveReservationJobs(thingDef, "DepositToMegaStorage", TargetIndex.B, true);
                int shortfall = CountShortfallIgnoringPendingIn(thingDef);
                int allowed = System.Math.Min(activeCount, shortfall);
                if (thingDef == null || entry.Value <= 0 || allowed <= 0)
                {
                    if (IsPendingInWithinGrace(thingDef, now) && shortfall > 0)
                        continue;

                    if (removeDefs == null)
                        removeDefs = new List<ThingDef>();
                    removeDefs.Add(thingDef);
                    RememberPendingReservationDebug(thingDef, entry.Value, allowed, activeCount, shortfall, "清理待入库预约");
                }
                else if (entry.Value > allowed)
                {
                    if (trimDefs == null)
                        trimDefs = new Dictionary<ThingDef, int>();
                    trimDefs[thingDef] = allowed;
                    RememberPendingReservationDebug(thingDef, entry.Value, allowed, activeCount, shortfall, "裁剪待入库预约");
                }
            }

            if (trimDefs != null)
            {
                foreach (KeyValuePair<ThingDef, int> entry in trimDefs)
                    pendingIn[entry.Key] = entry.Value;
            }

            if (removeDefs == null) return;
            for (int i = 0; i < removeDefs.Count; i++)
            {
                pendingIn.Remove(removeDefs[i]);
                pendingInReservedAtTick?.Remove(removeDefs[i]);
            }
        }

        /// <summary>
        /// 清理没有对应下架任务的待出库数量，负责修正任务中断或配置变更后残留的库存占用。
        /// </summary>
        public void ClearOrphanedPendingOut()
        {
            if (pendingOut == null || pendingOut.Count == 0) return;

            List<ThingDef> removeDefs = null;
            Dictionary<ThingDef, int> trimDefs = null;
            foreach (KeyValuePair<ThingDef, int> entry in pendingOut.ToList())
            {
                ThingDef thingDef = entry.Key;
                int activeCount = CountActiveReservationJobs(thingDef, "WithdrawFromMegaStorage", TargetIndex.A, false);
                int excess = CountExcessIgnoringPendingOut(thingDef);
                int allowed = System.Math.Min(activeCount, excess);
                if (thingDef == null || entry.Value <= 0 || allowed <= 0)
                {
                    if (removeDefs == null)
                        removeDefs = new List<ThingDef>();
                    removeDefs.Add(thingDef);
                }
                else if (entry.Value > allowed)
                {
                    if (trimDefs == null)
                        trimDefs = new Dictionary<ThingDef, int>();
                    trimDefs[thingDef] = allowed;
                }
            }

            if (trimDefs != null)
            {
                foreach (KeyValuePair<ThingDef, int> entry in trimDefs)
                    pendingOut[entry.Key] = entry.Value;
            }

            if (removeDefs == null) return;
            for (int i = 0; i < removeDefs.Count; i++)
                pendingOut.Remove(removeDefs[i]);
        }

        /// <summary>
        /// 判断待入库预约是否还处于启动宽限期，负责避免 Job 刚开始时被校正过早清掉。
        /// </summary>
        private bool IsPendingInWithinGrace(ThingDef thingDef, int now)
        {
            if (thingDef == null || pendingInReservedAtTick == null)
                return false;

            if (!pendingInReservedAtTick.TryGetValue(thingDef, out int reservedAt) || reservedAt <= 0)
                return false;

            return now >= reservedAt && now - reservedAt <= PendingReservationGraceTicks;
        }

        /// <summary>
        /// 记录最近一次预约校正原因，负责让调试文本能解释“途中”数量为什么变化。
        /// </summary>
        private void RememberPendingReservationDebug(ThingDef thingDef, int oldValue, int newValue, int activeCount, int shortfall, string reason)
        {
            string label = thingDef?.defName ?? "null";
            lastPendingReservationDebug = $"{reason}: {label} {oldValue}->{newValue}, 当前任务={activeCount}, 缺口={shortfall}";
        }

        /// <summary>
        /// 同步所有补货和下架预约，负责在强制补货、改配置或读档后主动修正运行态。
        /// </summary>
        public void ReconcilePendingReservations()
        {
            ClearOrphanedPendingIn();
            ClearOrphanedPendingOut();
            lastPendingReservationReconcileTick = Find.TickManager?.TicksGame ?? 0;
        }

        /// <summary>
        /// 按固定间隔同步补货和下架预约，负责供高频工作扫描使用，避免每个候选货柜都全图扫描 Pawn 任务。
        /// </summary>
        public void ReconcilePendingReservationsForWorkScan()
        {
            ReconcilePendingReservationsIfNeeded();
        }

        /// <summary>
        /// 统计当前地图上仍然有效的补货或下架任务预约数量。
        /// </summary>
        private int CountActiveReservationJobs(ThingDef thingDef, string jobDefName, TargetIndex storageTarget, bool depositJob)
        {
            if (thingDef == null || Map == null) return 0;

            IReadOnlyList<Pawn> pawns = Map.mapPawns?.AllPawnsSpawned;
            if (pawns == null) return 0;

            int total = 0;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn == null) continue;
                total += GetReservationCountFromJob(pawn.CurJob, pawn, thingDef, jobDefName, storageTarget, depositJob);
            }

            return total;
        }

        /// <summary>
        /// 从指定 Job 读取对当前货柜和物品的预约数量。
        /// </summary>
        private int GetReservationCountFromJob(Job job, Pawn pawn, ThingDef thingDef, string jobDefName, TargetIndex storageTarget, bool depositJob)
        {
            if (job == null || job.def?.defName != jobDefName) return 0;
            if (job.GetTarget(storageTarget).Thing != this) return 0;
            if (!IsPawnStillExecutingReservation(pawn)) return 0;
            if (job.count <= 0) return 0;

            ThingDef jobThingDef = job.plantDefToSow;
            if (jobThingDef == null && depositJob)
                jobThingDef = job.GetTarget(TargetIndex.A).Thing?.def ?? pawn?.carryTracker?.CarriedThing?.def;
            if (jobThingDef != thingDef) return 0;
            if (depositJob && !HasValidDepositSource(job, pawn, thingDef)) return 0;

            return UnityEngine.Mathf.Max(0, job.count);
        }

        /// <summary>
        /// 判断小人是否仍在执行预约任务，负责避免倒地、死亡或离图小人的旧任务保留“途中”数量。
        /// </summary>
        private static bool IsPawnStillExecutingReservation(Pawn pawn)
        {
            return pawn != null
                && !pawn.Destroyed
                && pawn.Spawned
                && !pawn.Dead
                && !pawn.Downed
                && pawn.CurJob != null;
        }

        /// <summary>
        /// 判断补货任务的货源是否仍有效，负责避免货源消失后待入库数量长期残留。
        /// </summary>
        private static bool HasValidDepositSource(Job job, Pawn pawn, ThingDef thingDef)
        {
            Thing carried = pawn?.carryTracker?.CarriedThing;
            if (carried != null && !carried.Destroyed && carried.def == thingDef && carried.stackCount > 0)
                return true;

            Thing source = job?.GetTarget(TargetIndex.A).Thing;
            return source != null
                && !source.Destroyed
                && source.Spawned
                && source.def == thingDef
                && source.stackCount > 0;
        }

        /// <summary>
        /// 返回考虑待入库预约后的剩余容量。
        /// </summary>
        public int GetRemainingCapacityForPending()
        {
            ReconcilePendingReservationsIfNeeded();
            int remain = MaxTotalCapacity - CountTotalStored() - CountTotalPendingIn();
            return UnityEngine.Mathf.Max(0, remain);
        }

        /// <summary>
        /// 返回只考虑实际库存的剩余容量。
        /// </summary>
        public int GetRemainingCapacityForStored()
        {
            int remain = MaxTotalCapacity - CountTotalStored();
            return UnityEngine.Mathf.Max(0, remain);
        }

        /// <summary>
        /// 统计当前配置的目标库存总量。
        /// </summary>
        public int CountConfiguredTargets()
        {
            int total = 0;
            foreach (ThingDef thingDef in ActiveDefs)
            {
                int target = GetTargetCount(thingDef);
                if (target > 0)
                    total += target;
            }

            return total;
        }

        /// <summary>
        /// 返回指定商品的目标库存数量。
        /// </summary>
        public int GetTargetCount(ThingDef thingDef)
        {
            ThingComp_GoodsData comp = GoodsComp;
            if (comp == null || string.IsNullOrEmpty(comp.ActiveGoodsDefName)) return 0;
            if (!comp.AllowsGoodsCategory(comp.ActiveGoodsDefName)) return 0;
            if (!GoodsCatalog.Contains(comp.ActiveGoodsDefName, thingDef)) return 0;
            GoodsItemData item = comp.FindItemData(thingDef);
            if (item == null || !item.enabled) return 0;
            return UnityEngine.Mathf.Max(0, item.count);
        }

        //返回指定商品触发自动补货的库存阈值。
        public int GetRestockThreshold(ThingDef thingDef)
        {
            ThingComp_GoodsData comp = GoodsComp;
            if (comp == null || string.IsNullOrEmpty(comp.ActiveGoodsDefName)) return 0;
            if (!comp.AllowsGoodsCategory(comp.ActiveGoodsDefName)) return 0;
            if (!GoodsCatalog.Contains(comp.ActiveGoodsDefName, thingDef)) return 0;
            GoodsItemData item = comp.FindItemData(thingDef);
            if (item == null || !item.enabled) return 0;
            return item.EffectiveRestockThreshold;
        }

        /// <summary>
        /// 返回指定商品的实际库存数量。
        /// </summary>
        public int CountStored(ThingDef thingDef)
        {
            if (thingDef == null) return 0;
            RebuildStoredCountCacheIfNeeded();
            return storedCountCache.TryGetValue(thingDef, out int value) ? value : 0;
        }

        /// <summary>
        /// 返回当前有实际库存的商品定义快照，负责让高频扫描不用枚举虚拟库存中的每个物品栈。
        /// </summary>
        public List<ThingDef> GetStoredThingDefsSnapshot()
        {
            RebuildStoredCountCacheIfNeeded();
            List<ThingDef> result = new List<ThingDef>();
            foreach (KeyValuePair<ThingDef, int> entry in storedCountCache)
            {
                if (entry.Key != null && entry.Value > 0)
                    result.Add(entry.Key);
            }
            return result;
        }

        /// <summary>
        /// 返回指定商品仍在路上的补货数量。
        /// </summary>
        public int CountPending(ThingDef thingDef, bool forceReconcile = false)
        {
            if (forceReconcile)
                ReconcilePendingReservations();
            else
                ReconcilePendingReservationsIfNeeded();
            return pendingIn.TryGetValue(thingDef, out int value) ? value : 0;
        }

        /// <summary>
        /// 直接读取待入库预约数量，负责在预约读写内部避免再次触发校正。
        /// </summary>
        private int CountPendingRaw(ThingDef thingDef)
        {
            if (thingDef == null || pendingIn == null)
                return 0;

            return pendingIn.TryGetValue(thingDef, out int value) ? UnityEngine.Mathf.Max(0, value) : 0;
        }

        /// <summary>
        /// 直接统计全部待入库预约数量，负责在预约读写内部避免再次触发校正。
        /// </summary>
        private int CountTotalPendingInRaw()
        {
            if (pendingIn == null || pendingIn.Count == 0)
                return 0;

            int total = 0;
            foreach (int value in pendingIn.Values)
                total += UnityEngine.Mathf.Max(0, value);
            return total;
        }

        /// <summary>
        /// 直接计算指定商品还需要补货的数量，负责让创建预约时不递归触发校正。
        /// </summary>
        private int CountNeededRaw(ThingDef thingDef)
        {
            int storedAndPending = CountStored(thingDef) + CountPendingRaw(thingDef);
            if (storedAndPending > GetRestockThreshold(thingDef)) return 0;

            int perDefNeed = System.Math.Max(0, GetTargetCount(thingDef) - storedAndPending);
            if (perDefNeed <= 0) return 0;

            int capacityRemain = MaxTotalCapacity - CountTotalStored() - CountTotalPendingInRaw();
            if (capacityRemain <= 0) return 0;

            return System.Math.Min(perDefNeed, capacityRemain);
        }

        /// <summary>
        /// 返回指定商品还需要补货的数量。
        /// </summary>
        public int CountNeeded(ThingDef thingDef)
        {
            int storedAndPending = CountStored(thingDef) + CountPending(thingDef);
            if (storedAndPending > GetRestockThreshold(thingDef)) return 0;

            int perDefNeed = System.Math.Max(0, GetTargetCount(thingDef) - storedAndPending);
            if (perDefNeed <= 0) return 0;

            int capacityRemain = GetRemainingCapacityForPending();
            if (capacityRemain <= 0) return 0;

            return System.Math.Min(perDefNeed, capacityRemain);
        }

        //返回工作扫描阶段使用的补货缺口，职责是避免高频 WorkGiver 路径触发预约校正。
        public int CountNeededForWorkScan(ThingDef thingDef)
        {
            if (thingDef == null)
                return 0;

            int storedAndPending = CountStored(thingDef) + CountPendingRaw(thingDef);
            if (storedAndPending > GetRestockThreshold(thingDef))
                return 0;

            int perDefNeed = System.Math.Max(0, GetTargetCount(thingDef) - storedAndPending);
            if (perDefNeed <= 0)
                return 0;

            int capacityRemain = MaxTotalCapacity - CountTotalStored() - CountTotalPendingInRaw();
            if (capacityRemain <= 0)
                return 0;

            return System.Math.Min(perDefNeed, capacityRemain);
        }

        //查找工作扫描阶段第一个需要补货的商品，职责是让候选预筛不用枚举完整业务链路。
        public bool TryFindRestockDefForWorkScan(out ThingDef restockDef)
        {
            foreach (ThingDef thingDef in ActiveDefs)
            {
                if (CountNeededForWorkScan(thingDef) > 0)
                {
                    restockDef = thingDef;
                    return true;
                }
            }

            restockDef = null;
            return false;
        }

        /// <summary>
        /// 不考虑待入库预约时统计指定商品的缺口数量。
        /// </summary>
        public int CountShortfallIgnoringPendingIn(ThingDef thingDef)
        {
            if (thingDef == null) return 0;
            return System.Math.Max(0, GetTargetCount(thingDef) - CountStored(thingDef));
        }

        /// <summary>
        /// 不考虑待出库预约时统计指定商品的多余数量。
        /// </summary>
        public int CountExcessIgnoringPendingOut(ThingDef thingDef)
        {
            if (thingDef == null) return 0;
            return System.Math.Max(0, CountStored(thingDef) - GetTargetCount(thingDef));
        }

        /// <summary>
        /// 枚举当前配置中所有可售商品定义。
        /// </summary>
        public IEnumerable<ThingDef> ActiveDefs
        {
            get
            {
                ThingComp_GoodsData comp = GoodsComp;
                if (comp == null || string.IsNullOrEmpty(comp.ActiveGoodsDefName)) yield break;
                if (!comp.AllowsGoodsCategory(comp.ActiveGoodsDefName)) yield break;

                IReadOnlyList<Pojo.RuntimeGoodsItem> items = GoodsCatalog.GetItems(comp.ActiveGoodsDefName);
                for (int i = 0; i < items.Count; i++)
                {
                    ThingDef thingDef = items[i]?.thingDef;
                    if (thingDef != null)
                        yield return thingDef;
                }
            }
        }

        /// <summary>
        /// 将传入的商品设置限制到当前货柜容量内。
        /// </summary>
        public Dictionary<string, GoodsItemData> ClampSettingsToCapacity(string activeDefName, Dictionary<string, GoodsItemData> source, out int trimmedCount)
        {
            trimmedCount = 0;
            ThingComp_GoodsData comp = GoodsComp;
            if (comp != null && !comp.AllowsGoodsCategory(activeDefName))
                return new Dictionary<string, GoodsItemData>();

            Dictionary<string, GoodsItemData> result = CloneSettings(source);
            IReadOnlyList<Pojo.RuntimeGoodsItem> items = GoodsCatalog.GetItems(activeDefName);
            if (items.Count <= 0) return result;

            int used = 0;
            int max = MaxTotalCapacity;

            for (int i = 0; i < items.Count; i++)
            {
                ThingDef thingDef = items[i]?.thingDef;
                if (thingDef == null) continue;
                if (!result.TryGetValue(thingDef.defName, out GoodsItemData data) || data == null) continue;

                if (!data.enabled || data.count <= 0)
                {
                    data.enabled = false;
                    data.count = 0;
                    data.restockThreshold = 0;
                    continue;
                }

                int allow = max - used;
                if (allow <= 0)
                {
                    trimmedCount += data.count;
                    data.enabled = false;
                    data.count = 0;
                    data.restockThreshold = 0;
                    continue;
                }

                if (data.count > allow)
                {
                    trimmedCount += data.count - allow;
                    data.count = allow;
                }

                data.restockThreshold = GoodsItemData.NormalizeRestockThreshold(data.restockThreshold, data.count);
                used += data.count;
            }

            return result;
        }

        /// <summary>
        /// 复制商品配置，负责避免 UI 编辑直接修改原始配置对象。
        /// </summary>
        private static Dictionary<string, GoodsItemData> CloneSettings(Dictionary<string, GoodsItemData> source)
        {
            Dictionary<string, GoodsItemData> result = new Dictionary<string, GoodsItemData>();
            if (source == null) return result;

            foreach (KeyValuePair<string, GoodsItemData> kvp in source)
            {
                GoodsItemData item = kvp.Value;
                result[kvp.Key] = new GoodsItemData
                {
                    enabled = item?.enabled ?? false,
                    count = UnityEngine.Mathf.Max(0, item?.count ?? 0),
                    price = UnityEngine.Mathf.Max(0f, item?.price ?? 0f),
                    restockThreshold = GoodsItemData.NormalizeRestockThreshold(item?.restockThreshold ?? -1, UnityEngine.Mathf.Max(0, item?.count ?? 0))
                };
            }

            return result;
        }
    }
}
