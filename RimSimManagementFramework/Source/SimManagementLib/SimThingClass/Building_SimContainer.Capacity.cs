using SimManagementLib.SimMapComp;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimThingClass
{
    //货柜容量统计模块，职责是从实际库存和地图租约计算补货缺口，并维护独立的下架预约。
    public partial class Building_SimContainer
    {
        //统计货柜当前已经实际存入的总件数。
        public int CountTotalStored()
        {
            RebuildStoredCountCacheIfNeeded();
            return cachedTotalStored;
        }

        //统计货柜当前全部有效普通补货租约数量。
        public int CountTotalPendingIn(bool forceReconcile = false)
        {
            MapComponent_RestockTaskQueue queue = Map?.GetComponent<MapComponent_RestockTaskQueue>();
            return queue?.CountTotalPending(this) ?? 0;
        }

        //清理没有对应下架任务的待出库数量，职责是回收中断任务留下的库存占用。
        public void ClearOrphanedPendingOut()
        {
            if (pendingOut == null || pendingOut.Count == 0)
                return;

            List<ThingDef> removeDefs = null;
            Dictionary<ThingDef, int> trimDefs = null;
            foreach (KeyValuePair<ThingDef, int> entry in pendingOut.ToList())
            {
                int activeCount = CountActiveWithdrawJobs(entry.Key);
                int allowed = Math.Min(activeCount, CountExcessIgnoringPendingOut(entry.Key));
                if (entry.Key == null || entry.Value <= 0 || allowed <= 0)
                {
                    if (removeDefs == null)
                        removeDefs = new List<ThingDef>();
                    removeDefs.Add(entry.Key);
                }
                else if (entry.Value > allowed)
                {
                    if (trimDefs == null)
                        trimDefs = new Dictionary<ThingDef, int>();
                    trimDefs[entry.Key] = allowed;
                }
            }

            if (trimDefs != null)
            {
                foreach (KeyValuePair<ThingDef, int> entry in trimDefs)
                    pendingOut[entry.Key] = entry.Value;
            }
            if (removeDefs == null)
                return;
            for (int i = 0; i < removeDefs.Count; i++)
                pendingOut.Remove(removeDefs[i]);
        }

        //同步下架预约，普通补货在途数量由地图租约看门狗独立维护。
        public void ReconcilePendingReservations()
        {
            ClearOrphanedPendingOut();
            lastPendingReservationReconcileTick = Find.TickManager?.TicksGame ?? 0;
        }

        //按固定间隔同步下架预约，职责是供下架工作扫描共享一次校正。
        public void ReconcilePendingReservationsForWorkScan()
        {
            ReconcilePendingReservationsIfNeeded();
        }

        //统计当前地图上仍然执行指定商品下架工作的数量。
        private int CountActiveWithdrawJobs(ThingDef thingDef)
        {
            if (thingDef == null || Map?.mapPawns?.AllPawnsSpawned == null)
                return 0;

            int total = 0;
            IReadOnlyList<Pawn> pawns = Map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                Job job = pawn?.CurJob;
                if (job?.def?.defName != "WithdrawFromMegaStorage"
                    || job.GetTarget(TargetIndex.A).Thing != this
                    || job.plantDefToSow != thingDef
                    || job.count <= 0
                    || !IsPawnStillExecutingReservation(pawn))
                    continue;
                total += Math.Max(0, job.count);
            }
            return total;
        }

        //判断 Pawn 是否仍在地图上执行当前预约工作。
        private static bool IsPawnStillExecutingReservation(Pawn pawn)
        {
            return pawn != null
                && !pawn.Destroyed
                && pawn.Spawned
                && !pawn.Dead
                && !pawn.Downed
                && pawn.CurJob != null;
        }

        //返回扣除有效补货租约后的剩余总容量。
        public int GetRemainingCapacityForPending()
        {
            return Math.Max(0, MaxTotalCapacity - CountTotalStored() - CountTotalPendingIn());
        }

        //返回只考虑实际库存的剩余总容量。
        public int GetRemainingCapacityForStored()
        {
            return Math.Max(0, MaxTotalCapacity - CountTotalStored());
        }

        //统计当前配置的目标库存总量。
        public int CountConfiguredTargets()
        {
            int total = 0;
            foreach (ThingDef thingDef in ActiveDefs)
                total += Math.Max(0, GetTargetCount(thingDef));
            return total;
        }

        //返回指定商品配置的目标库存数量。
        public int GetTargetCount(ThingDef thingDef)
        {
            SimThingComp.ThingComp_GoodsData comp = GoodsComp;
            if (comp == null || string.IsNullOrEmpty(comp.ActiveGoodsDefName))
                return 0;
            if (!comp.AllowsGoodsCategory(comp.ActiveGoodsDefName) || !Tool.GoodsCatalog.Contains(comp.ActiveGoodsDefName, thingDef))
                return 0;
            SimThingComp.GoodsItemData item = comp.FindItemData(thingDef);
            return item == null || !item.enabled ? 0 : Math.Max(0, item.count);
        }

        //返回指定商品触发自动补货的库存阈值。
        public int GetRestockThreshold(ThingDef thingDef)
        {
            SimThingComp.ThingComp_GoodsData comp = GoodsComp;
            if (comp == null || string.IsNullOrEmpty(comp.ActiveGoodsDefName))
                return 0;
            if (!comp.AllowsGoodsCategory(comp.ActiveGoodsDefName) || !Tool.GoodsCatalog.Contains(comp.ActiveGoodsDefName, thingDef))
                return 0;
            SimThingComp.GoodsItemData item = comp.FindItemData(thingDef);
            return item == null || !item.enabled ? 0 : item.EffectiveRestockThreshold;
        }

        //返回指定商品的实际库存数量。
        public int CountStored(ThingDef thingDef)
        {
            if (thingDef == null)
                return 0;
            RebuildStoredCountCacheIfNeeded();
            return storedCountCache.TryGetValue(thingDef, out int value) ? value : 0;
        }

        //返回当前有实际库存的商品定义快照。
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

        //返回指定商品当前全部有效普通补货租约数量。
        public int CountPending(ThingDef thingDef, bool forceReconcile = false)
        {
            if (thingDef == null)
                return 0;
            MapComponent_RestockTaskQueue queue = Map?.GetComponent<MapComponent_RestockTaskQueue>();
            return queue?.CountPending(this, thingDef) ?? 0;
        }

        //直接计算指定商品距离目标量的剩余缺口。
        private int CountRemainingToTargetRaw(ThingDef thingDef)
        {
            if (thingDef == null)
                return 0;
            int perDefNeed = Math.Max(0, GetTargetCount(thingDef) - CountStored(thingDef) - CountPending(thingDef));
            return Math.Min(perDefNeed, GetRemainingCapacityForPending());
        }

        //返回指定商品距离目标量的剩余缺口，强制补货可绕过阈值。
        public int CountRemainingToTarget(ThingDef thingDef)
        {
            return CountRemainingToTargetRaw(thingDef);
        }

        //返回工作扫描阶段距离目标量的剩余缺口。
        public int CountRemainingToTargetForWorkScan(ThingDef thingDef)
        {
            return CountRemainingToTargetRaw(thingDef);
        }

        //返回指定商品按照阈值语义还需要补货的数量。
        public int CountNeeded(ThingDef thingDef)
        {
            int storedAndPending = CountStored(thingDef) + CountPending(thingDef);
            if (storedAndPending > GetRestockThreshold(thingDef))
                return 0;
            return CountRemainingToTargetRaw(thingDef);
        }

        //返回工作扫描阶段按照阈值语义还需要补货的数量。
        public int CountNeededForWorkScan(ThingDef thingDef)
        {
            return CountNeeded(thingDef);
        }

        //查找第一个按照阈值语义需要补货的商品。
        public bool TryFindRestockDefForWorkScan(out ThingDef restockDef)
        {
            foreach (ThingDef thingDef in ActiveDefs)
            {
                if (CountNeeded(thingDef) <= 0)
                    continue;
                restockDef = thingDef;
                return true;
            }
            restockDef = null;
            return false;
        }

        //不考虑补货租约时统计指定商品的实际库存缺口。
        public int CountShortfallIgnoringPendingIn(ThingDef thingDef)
        {
            return thingDef == null ? 0 : Math.Max(0, GetTargetCount(thingDef) - CountStored(thingDef));
        }

        //不考虑下架预约时统计指定商品的多余库存。
        public int CountExcessIgnoringPendingOut(ThingDef thingDef)
        {
            return thingDef == null ? 0 : Math.Max(0, CountStored(thingDef) - GetTargetCount(thingDef) - CountReserved(thingDef));
        }
    }
}
