using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.SimDef;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimMapComp;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SimManagementLib.GameComp
{
    //类职责：为商店经营指标提供按地图分片的计算队列，避免查询调用同步扫描整家商店。
    public partial class GameComponent_ShopAnalyticsManager
    {
        private readonly Dictionary<int, Queue<Zone_Shop>> metricsQueues = new Dictionary<int, Queue<Zone_Shop>>();
        private readonly HashSet<int> queuedMetricKeys = new HashSet<int>();
        private readonly Dictionary<int, ShopMetricsWork> activeMetricsWork = new Dictionary<int, ShopMetricsWork>();

        //把商店加入指标重算队列，职责是合并同一商店的重复失效通知。
        private void QueueMetricsRecalculation(Zone_Shop zone)
        {
            if (zone?.Map == null) return;
            int key = MakeMetricKey(zone.Map.uniqueID, zone.ID);
            if (!queuedMetricKeys.Add(key)) return;
            if (!metricsQueues.TryGetValue(zone.Map.uniqueID, out Queue<Zone_Shop> queue))
            {
                queue = new Queue<Zone_Shop>();
                metricsQueues[zone.Map.uniqueID] = queue;
            }
            queue.Enqueue(zone);
        }

        //按预算推进指定地图指标计算，职责是每 tick 最多读取 64 个区划格和 8 个设施样本。
        public void ProcessMetricsBudget(Map map, int cellBudget, int facilityBudget)
        {
            if (map == null) return;
            int mapId = map.uniqueID;
            if (!activeMetricsWork.TryGetValue(mapId, out ShopMetricsWork work) || work == null)
            {
                work = DequeueMetricsWork(map);
                if (work == null) return;
                activeMetricsWork[mapId] = work;
            }

            work.ProcessCells(Mathf.Max(0, cellBudget));
            work.ProcessFacilities(Mathf.Max(0, facilityBudget));
            if (!work.IsComplete) return;

            PublishMetricsWork(work);
            activeMetricsWork.Remove(mapId);
            queuedMetricKeys.Remove(MakeMetricKey(mapId, work.Zone.ID));
        }

        //构建指定地图指标预算诊断，职责是显示排队数量和当前分片进度。
        public string BuildMetricsBudgetDiagnostics(Map map)
        {
            if (map == null) return "无地图";
            metricsQueues.TryGetValue(map.uniqueID, out Queue<Zone_Shop> queue);
            if (!activeMetricsWork.TryGetValue(map.uniqueID, out ShopMetricsWork work) || work == null)
                return "等待=" + (queue?.Count ?? 0) + "，当前=空闲";
            return "等待=" + (queue?.Count ?? 0)
                + "，区划格=" + work.ProcessedCells + "/" + work.CellCount
                + "，设施=" + work.ProcessedFacilities + "/" + work.FacilityCount;
        }

        //返回保守指标，职责是在首次分片计算完成前避免调用方同步重算或过度刷客。
        private ShopMetricsSnapshot CreateConservativeMetrics(Zone_Shop zone, ShopAnalyticsState state)
        {
            return new ShopMetricsSnapshot
            {
                zoneId = zone.ID,
                zoneLabel = zone.label ?? ("Shop #" + zone.ID),
                score = state?.lastScore ?? 0f,
                reputation = state?.reputation ?? 0f,
                satisfaction = state?.satisfactionEma ?? 0f,
                beautyAverage = state?.lastBeauty ?? 0f,
                environmentScore = state?.lastEnvironment ?? 0f,
                dynamicCapacity = 6,
                spawnDemandFactor = 1f,
                beautyDemandMultiplier = 1f,
                scaleDemandMultiplier = 1f,
                scaleCapacityMultiplier = 1f
            };
        }

        //取出下一个有效指标任务，职责是跳过已删除或跨地图的商店引用。
        private ShopMetricsWork DequeueMetricsWork(Map map)
        {
            if (!metricsQueues.TryGetValue(map.uniqueID, out Queue<Zone_Shop> queue)) return null;
            while (queue.Count > 0)
            {
                Zone_Shop zone = queue.Dequeue();
                if (zone != null && zone.Map == map)
                    return new ShopMetricsWork(zone, GetOrCreateState(zone.ID, zone.label));
                if (zone != null)
                    queuedMetricKeys.Remove(MakeMetricKey(map.uniqueID, zone.ID));
            }
            return null;
        }

        //发布完成的指标快照，职责是使用完整采样结果一次性替换旧快照。
        private void PublishMetricsWork(ShopMetricsWork work)
        {
            Zone_Shop zone = work.Zone;
            ShopAnalyticsState state = work.State;
            ShopTuningDef tuning = GetTuning();
            int comboCount = Current.Game?.GetComponent<GameComponent_ShopComboManager>()?.GetCombosForZone(zone)?.Count ?? 0;
            float operation01 = ComputeOperationScore01(tuning, zone, work.RegisterCount, work.MannedCount, work.StorageCount);
            float goods01 = ComputeGoodsScore01(tuning, work.GoodsKinds.Count, work.InStockKinds.Count, comboCount);
            float service01 = ComputeServiceScore01(tuning, state, work.RegisterCount, work.MannedCount);
            float beautyAverage = work.BeautySamples > 0 ? work.BeautyTotal / work.BeautySamples : 0f;
            float beauty01 = Mathf.InverseLerp(tuning.beautyRange.min, tuning.beautyRange.max, beautyAverage);
            float clean01 = 1f - Mathf.Clamp01((work.FilthCells / (float)Mathf.Max(1, work.CellCount)) / 0.18f);
            float indoor01 = work.RoofedCells / (float)Mathf.Max(1, work.CellCount);
            float environment01 = WeightedNormalized4(beauty01, tuning.beautyWeight, clean01, tuning.cleanlinessWeight, indoor01, tuning.indoorWeight, 0f, 0f);
            float score01 = WeightedNormalized4(operation01, tuning.operationWeight, goods01, tuning.goodsWeight, service01, tuning.serviceWeight, environment01, tuning.environmentWeight);
            float reputation = GetReputation(zone.ID);
            float satisfaction = GetSatisfaction(zone.ID);
            float effectiveScale = ShopDemandCurveUtility.CalculateEffectiveScale(tuning, work.RegisterCount, work.MannedCount, work.StockedStorageCount, work.InStockKinds.Count, work.CellCount);
            float beautyDemand = ShopDemandCurveUtility.CalculateBeautyDemandMultiplier(tuning, beautyAverage);
            float scaleDemand = ShopDemandCurveUtility.CalculateScaleDemandMultiplier(tuning, effectiveScale);
            float scaleCapacity = ShopDemandCurveUtility.CalculateScaleCapacityMultiplier(tuning, effectiveScale);
            int capacity = CalculateDynamicCapacity(tuning, zone, score01, reputation / 100f, work.RegisterCount, work.MannedCount, work.StockedStorageCount, scaleCapacity);
            float demand = CalculateSpawnDemandFactor(tuning, zone, score01, reputation / 100f, beautyDemand, scaleDemand);
            ShopMetricsSnapshot snapshot = new ShopMetricsSnapshot
            {
                zoneId = zone.ID,
                zoneLabel = zone.label ?? ("Shop #" + zone.ID),
                score = score01 * 100f,
                operationScore = operation01 * 100f,
                goodsScore = goods01 * 100f,
                serviceScore = service01 * 100f,
                environmentScore = environment01 * 100f,
                reputation = reputation,
                satisfaction = satisfaction,
                beautyAverage = beautyAverage,
                effectiveScale = effectiveScale,
                beautyDemandMultiplier = beautyDemand,
                scaleDemandMultiplier = scaleDemand,
                scaleCapacityMultiplier = scaleCapacity,
                dynamicCapacity = capacity,
                spawnDemandFactor = demand
            };
            state.cachedMetrics = snapshot;
            state.lastEvaluateTick = Find.TickManager?.TicksGame ?? 0;
            state.lastScore = snapshot.score;
            state.lastBeauty = beautyAverage;
            state.lastEnvironment = snapshot.environmentScore;
            state.metricsDirty = false;
            zone.Map?.GetComponent<CustomerArrivalManager>()?.NotifyShopDirty(zone);
        }

        //构造地图和区划复合键，职责是避免多地图区划 ID 冲突。
        private static int MakeMetricKey(int mapId, int zoneId)
        {
            unchecked { return mapId * 397 ^ zoneId; }
        }

        //类职责：保存一间商店尚未完成的纯主线程采样进度。
        private sealed class ShopMetricsWork
        {
            private readonly IReadOnlyList<Building_CashRegister> registers;
            private readonly IReadOnlyList<Building_SimContainer> storages;
            private int cellCursor;
            private int facilityCursor;
            public Zone_Shop Zone { get; }
            public ShopAnalyticsState State { get; }
            public int CellCount => Zone?.Cells?.Count ?? 0;
            public int ProcessedCells => cellCursor;
            public int ProcessedFacilities => facilityCursor;
            public int FacilityCount => registers.Count + storages.Count;
            public int FilthCells { get; private set; }
            public int RoofedCells { get; private set; }
            public int RegisterCount => registers.Count;
            public int StorageCount => storages.Count;
            public int MannedCount { get; private set; }
            public int StockedStorageCount { get; private set; }
            public float BeautyTotal { get; private set; }
            public int BeautySamples { get; private set; }
            public HashSet<ThingDef> GoodsKinds { get; } = new HashSet<ThingDef>();
            public HashSet<ThingDef> InStockKinds { get; } = new HashSet<ThingDef>();
            public bool IsComplete => cellCursor >= CellCount && facilityCursor >= registers.Count + storages.Count;

            //创建指标任务，职责是取得设施快照但不扫描区划格或库存内容。
            public ShopMetricsWork(Zone_Shop zone, ShopAnalyticsState state)
            {
                Zone = zone;
                State = state;
                registers = ShopDataUtility.GetCashRegisterSnapshotInZone(zone);
                storages = ShopDataUtility.GetStorageSnapshotInZone(zone);
            }

            //分片处理区划格，职责是累计清洁度和室内率。
            public void ProcessCells(int budget)
            {
                while (budget-- > 0 && cellCursor < CellCount)
                {
                    IntVec3 cell = Zone.Cells[cellCursor++];
                    if (Zone.Map.roofGrid.Roofed(cell)) RoofedCells++;
                    List<Thing> things = Zone.Map.thingGrid.ThingsListAt(cell);
                    for (int i = 0; i < things.Count; i++)
                    {
                        if (things[i] is Filth)
                        {
                            FilthCells++;
                            break;
                        }
                    }
                }
            }

            //分片处理设施，职责是累计收银、库存种类和美观样本。
            public void ProcessFacilities(int budget)
            {
                int total = registers.Count + storages.Count;
                while (budget-- > 0 && facilityCursor < total)
                {
                    if (facilityCursor < registers.Count) ProcessRegister(registers[facilityCursor]);
                    else ProcessStorage(storages[facilityCursor - registers.Count]);
                    facilityCursor++;
                }
                if (facilityCursor >= total && BeautySamples == 0 && CellCount > 0)
                {
                    BeautyTotal = ComputeCellBeautyApprox(Zone.Map, Zone.Cells[0]);
                    BeautySamples = 1;
                }
            }

            //采样收银台，职责是累计岗位和周边美观数据。
            private void ProcessRegister(Building_CashRegister register)
            {
                if (register == null || register.Destroyed) return;
                if (register.IsManned) MannedCount++;
                AddBeauty(register.Position);
                AddBeauty(register.InteractionCell);
            }

            //采样货柜，职责是按商品 Def 聚合有售和有货种类。
            private void ProcessStorage(Building_SimContainer storage)
            {
                if (storage == null || storage.Destroyed) return;
                bool stocked = false;
                foreach (ThingDef def in storage.ActiveDefs)
                {
                    if (def == null) continue;
                    GoodsKinds.Add(def);
                    if (storage.CountStored(def) <= 0) continue;
                    InStockKinds.Add(def);
                    stocked = true;
                }
                if (stocked) StockedStorageCount++;
                AddBeauty(storage.Position);
            }

            //增加一个美观样本，职责是忽略地图外格并累计近似美观。
            private void AddBeauty(IntVec3 cell)
            {
                if (!cell.IsValid || !cell.InBounds(Zone.Map)) return;
                BeautyTotal += ComputeCellBeautyApprox(Zone.Map, cell);
                BeautySamples++;
            }
        }
    }
}
