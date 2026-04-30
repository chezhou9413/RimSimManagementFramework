using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.SimDef;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.GameComp
{
    public class GameComponent_ShopAnalyticsManager : GameComponent
    {
        private static readonly ShopTuningDef FallbackTuning = new ShopTuningDef();

        private Dictionary<int, ShopAnalyticsState> shopStates = new Dictionary<int, ShopAnalyticsState>();
        private Dictionary<int, float> dailySatisfactionSum = new Dictionary<int, float>();
        private Dictionary<int, int> dailySatisfactionCount = new Dictionary<int, int>();

        private List<int> tmpIntKeys;
        private List<int> tmpIntValues;
        private List<int> tmpStateKeys;
        private List<ShopAnalyticsState> tmpStateValues;
        private List<string> tmpStringValues;

        // Legacy save compatibility.
        private Dictionary<int, float> legacyShopReputation;
        private Dictionary<int, float> legacyShopSatisfactionEma;
        private Dictionary<int, int> legacyShopSuccessfulCheckouts;
        private Dictionary<int, int> legacyShopTimeoutCheckouts;
        private Dictionary<int, float> legacyShopQueueWaitTicksTotal;
        private Dictionary<int, int> legacyShopQueueWaitSamples;
        private Dictionary<int, string> legacyShopLabels;
        private Dictionary<int, float> legacyShopLastScore;
        private Dictionary<int, float> legacyShopLastBeauty;
        private Dictionary<int, float> legacyShopLastEnvironment;

        public GameComponent_ShopAnalyticsManager(Game game)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Collections.Look(ref shopStates, "shopStates", LookMode.Value, LookMode.Deep, ref tmpStateKeys, ref tmpStateValues);
            Scribe_Collections.Look(ref dailySatisfactionSum, "dailySatisfactionSum", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref dailySatisfactionCount, "dailySatisfactionCount", LookMode.Value, LookMode.Value, ref tmpIntKeys, ref tmpIntValues);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                Scribe_Collections.Look(ref legacyShopReputation, "shopReputation", LookMode.Value, LookMode.Value);
                Scribe_Collections.Look(ref legacyShopSatisfactionEma, "shopSatisfactionEma", LookMode.Value, LookMode.Value);
                Scribe_Collections.Look(ref legacyShopSuccessfulCheckouts, "shopSuccessfulCheckouts", LookMode.Value, LookMode.Value);
                Scribe_Collections.Look(ref legacyShopTimeoutCheckouts, "shopTimeoutCheckouts", LookMode.Value, LookMode.Value);
                Scribe_Collections.Look(ref legacyShopQueueWaitTicksTotal, "shopQueueWaitTicksTotal", LookMode.Value, LookMode.Value);
                Scribe_Collections.Look(ref legacyShopQueueWaitSamples, "shopQueueWaitSamples", LookMode.Value, LookMode.Value);
                Scribe_Collections.Look(ref legacyShopLabels, "shopLabels", LookMode.Value, LookMode.Value, ref tmpStateKeys, ref tmpStringValues);
                Scribe_Collections.Look(ref legacyShopLastScore, "shopLastScore", LookMode.Value, LookMode.Value);
                Scribe_Collections.Look(ref legacyShopLastBeauty, "shopLastBeauty", LookMode.Value, LookMode.Value);
                Scribe_Collections.Look(ref legacyShopLastEnvironment, "shopLastEnvironment", LookMode.Value, LookMode.Value);
            }

            if (shopStates == null) shopStates = new Dictionary<int, ShopAnalyticsState>();
            if (dailySatisfactionSum == null) dailySatisfactionSum = new Dictionary<int, float>();
            if (dailySatisfactionCount == null) dailySatisfactionCount = new Dictionary<int, int>();

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                MigrateLegacyData();
                ResetTransientCaches();
            }
        }

        public ShopMetricsSnapshot GetOrEvaluateShopMetrics(Zone_Shop zone, bool forceRecalculate = false)
        {
            if (zone == null || zone.Map == null) return null;

            ShopTuningDef tuning = GetTuning();
            int interval = Mathf.Max(1, tuning.evaluateIntervalTicks);
            int zoneId = zone.ID;
            int ticks = Find.TickManager?.TicksGame ?? 0;
            ShopAnalyticsState state = GetOrCreateState(zoneId, zone.label);

            if (!forceRecalculate
                && state.cachedMetrics != null
                && state.lastEvaluateTick >= 0
                && ticks - state.lastEvaluateTick < interval)
            {
                return state.cachedMetrics;
            }

            Map map = zone.Map;
            List<Building_CashRegister> registers = map.listerBuildings.allBuildingsColonist
                .OfType<Building_CashRegister>()
                .Where(r => r != null && !r.Destroyed && r.Spawned && zone.Cells.Contains(r.Position))
                .ToList();
            int registerCount = registers.Count;
            int mannedCount = registers.Count(r => r.IsManned);

            int storageCount = ShopDataUtility.GetStoragesInZone(zone).Count;

            List<ShopItemStatus> goods = ShopDataUtility.GetAllSellableGoods(zone);
            int goodsKinds = goods.Count;
            int inStockKinds = goods.Count(g => g.CurrentStock > 0);

            GameComponent_ShopComboManager comboManager = Current.Game?.GetComponent<GameComponent_ShopComboManager>();
            int comboCount = comboManager?.GetCombosForZone(zone)?.Count ?? 0;

            float operation01 = ComputeOperationScore01(tuning, zone, registerCount, mannedCount, storageCount);
            float goods01 = ComputeGoodsScore01(tuning, goodsKinds, inStockKinds, comboCount);
            float service01 = ComputeServiceScore01(tuning, state, registerCount, mannedCount);

            float beautyAverage = ComputeAverageBeauty(zone, registers);
            float beauty01 = Mathf.InverseLerp(tuning.beautyRange.min, tuning.beautyRange.max, beautyAverage);
            float clean01 = ComputeCleanliness01(zone);
            float indoor01 = ComputeIndoor01(zone);
            float environment01 = WeightedNormalized4(
                beauty01, tuning.beautyWeight,
                clean01, tuning.cleanlinessWeight,
                indoor01, tuning.indoorWeight,
                0f, 0f);

            float score01 = WeightedNormalized4(
                operation01, tuning.operationWeight,
                goods01, tuning.goodsWeight,
                service01, tuning.serviceWeight,
                environment01, tuning.environmentWeight);
            float score = score01 * 100f;

            float reputation = GetReputation(zoneId);
            float satisfaction = GetSatisfaction(zoneId);
            int capacity = CalculateDynamicCapacity(tuning, zone, score01, reputation / 100f, registerCount, mannedCount, storageCount);
            float demand = CalculateSpawnDemandFactor(tuning, zone, score01, reputation / 100f);

            ShopMetricsSnapshot snapshot = new ShopMetricsSnapshot
            {
                zoneId = zoneId,
                zoneLabel = zone.label ?? ("Shop #" + zoneId),
                score = score,
                operationScore = operation01 * 100f,
                goodsScore = goods01 * 100f,
                serviceScore = service01 * 100f,
                environmentScore = environment01 * 100f,
                reputation = reputation,
                satisfaction = satisfaction,
                beautyAverage = beautyAverage,
                dynamicCapacity = capacity,
                spawnDemandFactor = demand
            };

            state.cachedMetrics = snapshot;
            state.lastEvaluateTick = ticks;
            state.lastScore = score;
            state.lastBeauty = beautyAverage;
            state.lastEnvironment = environment01 * 100f;

            return snapshot;
        }

        public float GetSpawnDemandFactor(Zone_Shop zone, Map map)
        {
            ShopMetricsSnapshot snapshot = GetOrEvaluateShopMetrics(zone);
            return snapshot != null ? snapshot.spawnDemandFactor : 1f;
        }

        public int GetDynamicCustomerCapacity(Zone_Shop zone)
        {
            ShopMetricsSnapshot snapshot = GetOrEvaluateShopMetrics(zone);
            return snapshot != null ? Mathf.Max(2, snapshot.dynamicCapacity) : 6;
        }

        public float GetReputation(int zoneId)
        {
            ShopAnalyticsState state = GetOrCreateState(zoneId, null);
            return Mathf.Clamp(state.reputation, 0f, 100f);
        }

        public float GetSatisfaction(int zoneId)
        {
            ShopAnalyticsState state = GetOrCreateState(zoneId, null);
            return Mathf.Clamp(state.satisfactionEma, 0f, 100f);
        }

        public void RecordCheckoutResult(
            Zone_Shop zone,
            int waitTicks,
            int patienceTicks,
            int paidSilver,
            int budget,
            bool success,
            bool timeout)
        {
            if (zone == null) return;
            ShopTuningDef tuning = GetTuning();
            int zoneId = zone.ID;
            ShopAnalyticsState state = GetOrCreateState(zoneId, zone.label);

            if (success && paidSilver > 0)
                state.successfulCheckouts++;
            if (timeout)
                state.timeoutCheckouts++;

            if (waitTicks > 0)
            {
                state.queueWaitTicksTotal += waitTicks;
                state.queueWaitSamples++;
            }

            ShopMetricsSnapshot snapshot = GetOrEvaluateShopMetrics(zone, true);

            float queueScore = 100f * Mathf.Clamp01(1f - waitTicks / (float)Mathf.Max(1, patienceTicks));
            float budgetUse = budget > 0 ? paidSilver / (float)Mathf.Max(1, budget) : (paidSilver > 0 ? 1f : 0f);
            float budgetScore = tuning.satisfactionBudgetBase
                                + tuning.satisfactionBudgetScale * Mathf.Clamp01(budgetUse / Mathf.Max(0.01f, tuning.satisfactionBudgetTargetUsage));
            float goodsScore = paidSilver > 0 ? tuning.satisfactionPaidGoods : (timeout ? tuning.satisfactionTimeoutNoBuy : tuning.satisfactionNoBuyBase);
            float envScore = snapshot != null ? snapshot.environmentScore : 60f;
            float completionScore = success
                ? tuning.satisfactionCompletionSuccess
                : (timeout ? tuning.satisfactionCompletionTimeout : tuning.satisfactionCompletionFail);

            float satisfaction = WeightedNormalized5(
                queueScore, tuning.satisfactionQueueWeight,
                budgetScore, tuning.satisfactionBudgetWeight,
                goodsScore, tuning.satisfactionGoodsWeight,
                envScore, tuning.satisfactionEnvironmentWeight,
                completionScore, tuning.satisfactionCompletionWeight);
            satisfaction = Mathf.Clamp(satisfaction, 0f, 100f);

            float oldSat = GetSatisfaction(zoneId);
            float satOldW = Mathf.Clamp01(tuning.satisfactionEmaOldWeight);
            float satNewW = Mathf.Clamp01(tuning.satisfactionEmaNewWeight);
            float satNorm = Mathf.Max(0.001f, satOldW + satNewW);
            state.satisfactionEma = Mathf.Clamp((oldSat * satOldW + satisfaction * satNewW) / satNorm, 0f, 100f);

            float oldRep = GetReputation(zoneId);
            float repOldW = Mathf.Clamp01(tuning.reputationEmaOldWeight);
            float repNewW = Mathf.Clamp01(tuning.reputationEmaNewWeight);
            float repNorm = Mathf.Max(0.001f, repOldW + repNewW);
            state.reputation = Mathf.Clamp((oldRep * repOldW + satisfaction * repNewW) / repNorm, 0f, 100f);

            int day = GenDate.DaysPassed;
            AddFloat(dailySatisfactionSum, day, satisfaction);
            AddInt(dailySatisfactionCount, day, 1);
        }

        private static float ComputeOperationScore01(ShopTuningDef tuning, Zone_Shop zone, int registerCount, int mannedCount, int storageCount)
        {
            if (zone == null || registerCount <= 0 || storageCount <= 0) return 0f;

            float registerScore = Mathf.Clamp01((registerCount + mannedCount * 0.75f) / Mathf.Max(0.1f, tuning.operationRegisterDivisor));
            float storageScore = Mathf.Clamp01(storageCount / Mathf.Max(0.1f, tuning.operationStorageDivisor));
            float coverageDivisorCells = Mathf.Max(1f, tuning.operationCoverageDivisorCells);
            float coverageScore = Mathf.Clamp01((registerCount * 1.5f + storageCount) / Mathf.Max(1f, zone.Cells.Count / coverageDivisorCells));

            return WeightedNormalized3(
                registerScore, tuning.operationRegisterComponentWeight,
                storageScore, tuning.operationStorageComponentWeight,
                coverageScore, tuning.operationCoverageComponentWeight);
        }

        private static float ComputeGoodsScore01(ShopTuningDef tuning, int goodsKinds, int inStockKinds, int comboCount)
        {
            float variety = Mathf.Clamp01(goodsKinds / Mathf.Max(1f, tuning.goodsVarietyTarget));
            float stockRate = goodsKinds > 0 ? inStockKinds / (float)goodsKinds : 0f;
            float comboScore = Mathf.Clamp01(comboCount / Mathf.Max(1f, tuning.goodsComboTarget));

            return WeightedNormalized3(
                variety, tuning.goodsVarietyWeight,
                stockRate, tuning.goodsStockWeight,
                comboScore, tuning.goodsComboWeight);
        }

        private static float ComputeServiceScore01(ShopTuningDef tuning, ShopAnalyticsState state, int registerCount, int mannedCount)
        {
            int total = state.successfulCheckouts + state.timeoutCheckouts;

            float successRate = total >= 5 ? state.successfulCheckouts / (float)Mathf.Max(1, total) : tuning.serviceFallbackSuccessRate;
            float avgWait = state.queueWaitSamples > 0
                ? state.queueWaitTicksTotal / Mathf.Max(1f, state.queueWaitSamples)
                : tuning.serviceFallbackAvgWaitTicks;
            float waitScore = 1f - Mathf.Clamp01(avgWait / Mathf.Max(1f, tuning.serviceWaitPenaltyTicks));
            float staffing = registerCount > 0 ? mannedCount / (float)registerCount : 0f;

            return WeightedNormalized3(
                successRate, tuning.serviceSuccessWeight,
                waitScore, tuning.serviceWaitWeight,
                staffing, tuning.serviceStaffWeight);
        }

        private float ComputeAverageBeauty(Zone_Shop zone, List<Building_CashRegister> registers)
        {
            if (zone == null || zone.Map == null || zone.Cells.Count == 0) return 0f;

            HashSet<Building_SimContainer> storages = ShopDataUtility.GetStoragesInZone(zone);
            List<IntVec3> samples = new List<IntVec3>();

            for (int i = 0; i < registers.Count; i++)
            {
                Building_CashRegister register = registers[i];
                if (register == null || register.Destroyed) continue;

                AddSample(samples, zone.Map, register.Position);
                AddSample(samples, zone.Map, register.InteractionCell);

                IntVec3 d = register.InteractionCell - register.Position;
                IntVec3 mirrored = register.Position - new IntVec3(Mathf.Clamp(d.x, -1, 1), 0, Mathf.Clamp(d.z, -1, 1));
                AddSample(samples, zone.Map, mirrored);
            }

            foreach (Building_SimContainer storage in storages)
            {
                if (storage == null || storage.Destroyed) continue;
                AddSample(samples, zone.Map, storage.Position);
            }

            if (samples.Count == 0)
                AddSample(samples, zone.Map, zone.Cells.First());

            float sum = 0f;
            for (int i = 0; i < samples.Count; i++)
            {
                sum += ComputeCellBeautyApprox(zone.Map, samples[i]);
            }

            return samples.Count > 0 ? sum / samples.Count : 0f;
        }

        private static float ComputeCleanliness01(Zone_Shop zone)
        {
            if (zone == null || zone.Map == null || zone.Cells.Count == 0) return 0f;

            int filthCells = 0;
            foreach (IntVec3 cell in zone.Cells)
            {
                List<Thing> things = zone.Map.thingGrid.ThingsListAt(cell);
                for (int i = 0; i < things.Count; i++)
                {
                    if (things[i] is Filth)
                    {
                        filthCells++;
                        break;
                    }
                }
            }

            float density = filthCells / (float)Mathf.Max(1, zone.Cells.Count);
            return 1f - Mathf.Clamp01(density / 0.18f);
        }

        private static float ComputeIndoor01(Zone_Shop zone)
        {
            if (zone == null || zone.Map == null || zone.Cells.Count == 0) return 0f;

            int roofed = 0;
            foreach (IntVec3 cell in zone.Cells)
            {
                if (zone.Map.roofGrid.Roofed(cell)) roofed++;
            }

            return roofed / (float)Mathf.Max(1, zone.Cells.Count);
        }

        private float CalculateSpawnDemandFactor(ShopTuningDef tuning, Zone_Shop zone, float score01, float rep01)
        {
            if (zone?.Map == null) return 1f;

            Map map = zone.Map;
            float wealth = map.wealthWatcher != null ? map.wealthWatcher.WealthTotal : 0f;
            int colonists = map.mapPawns.FreeColonistsSpawnedCount;
            int shopCount = map.zoneManager.AllZones.OfType<Zone_Shop>().Count(z => z.IsValidShop());

            float wealth01 = Mathf.InverseLerp(tuning.wealthRange.min, tuning.wealthRange.max, wealth);
            float colonist01 = Mathf.Clamp01(colonists / Mathf.Max(1f, tuning.colonistTarget));
            float shop01 = Mathf.Clamp01((shopCount - 1) / Mathf.Max(1f, tuning.shopCountTarget));

            float stage = tuning.stageBase
                        + wealth01 * tuning.wealthWeight
                        + colonist01 * tuning.colonistWeight
                        + shop01 * tuning.shopCountWeight;

            float quality = Mathf.Lerp(tuning.qualityMultiplierRange.min, tuning.qualityMultiplierRange.max, Mathf.Clamp01(score01));
            float reputation = Mathf.Lerp(tuning.reputationMultiplierRange.min, tuning.reputationMultiplierRange.max, Mathf.Clamp01(rep01));

            float demand = stage * quality * reputation;
            return Mathf.Clamp(demand, tuning.demandFactorClamp.min, tuning.demandFactorClamp.max);
        }

        private int CalculateDynamicCapacity(ShopTuningDef tuning, Zone_Shop zone, float score01, float rep01, int registerCount, int mannedCount, int storageCount)
        {
            float baseCapacity = registerCount * tuning.capacityRegisterFactor
                               + mannedCount * tuning.capacityMannedFactor
                               + storageCount * tuning.capacityStorageFactor;

            float qualityMul = Mathf.Lerp(tuning.capacityQualityMulRange.min, tuning.capacityQualityMulRange.max, Mathf.Clamp01(score01));
            float repMul = Mathf.Lerp(tuning.capacityReputationMulRange.min, tuning.capacityReputationMulRange.max, Mathf.Clamp01(rep01));
            int cap = Mathf.RoundToInt(baseCapacity * qualityMul * repMul);

            if (zone != null && zone.Cells.Count > 0)
            {
                float divisor = Mathf.Max(1f, tuning.capacityZoneCellCapDivisor);
                cap = Mathf.Min(cap, Mathf.Max(tuning.capacityMin, Mathf.FloorToInt(zone.Cells.Count / divisor)));
            }

            return Mathf.Clamp(cap, Mathf.Max(1, tuning.capacityMin), Mathf.Max(tuning.capacityMin, tuning.capacityMax));
        }

        private static float ComputeCellBeautyApprox(Map map, IntVec3 center)
        {
            if (map == null || !center.InBounds(map)) return 0f;

            float total = 0f;
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    IntVec3 cell = new IntVec3(center.x + dx, center.y, center.z + dz);
                    if (!cell.InBounds(map)) continue;

                    float weight = (dx == 0 && dz == 0) ? 1f : 0.45f;
                    List<Thing> things = map.thingGrid.ThingsListAt(cell);
                    for (int i = 0; i < things.Count; i++)
                    {
                        Thing thing = things[i];
                        if (thing == null || thing.Destroyed || thing is Filth) continue;
                        total += TryGetThingBeauty(thing) * weight;
                    }
                }
            }

            return Mathf.Clamp(total, -20f, 20f);
        }

        private static float TryGetThingBeauty(Thing thing)
        {
            try
            {
                float val = thing.GetStatValue(StatDefOf.Beauty, true);
                if (float.IsNaN(val) || float.IsInfinity(val)) return 0f;
                return Mathf.Clamp(val, -20f, 20f);
            }
            catch
            {
                return 0f;
            }
        }

        private static void AddSample(List<IntVec3> samples, Map map, IntVec3 cell)
        {
            if (!cell.IsValid || !cell.InBounds(map)) return;
            if (samples.Contains(cell)) return;
            if (samples.Count >= 24) return;
            samples.Add(cell);
        }

        private ShopAnalyticsState GetOrCreateState(int zoneId, string label)
        {
            bool created = false;
            if (!shopStates.TryGetValue(zoneId, out ShopAnalyticsState state) || state == null)
            {
                state = new ShopAnalyticsState();
                shopStates[zoneId] = state;
                created = true;
            }

            if (created)
            {
                ShopTuningDef tuning = GetTuning();
                state.reputation = tuning.defaultReputation;
                state.satisfactionEma = tuning.defaultSatisfaction;
            }

            if (!string.IsNullOrEmpty(label)) state.label = label;
            if (string.IsNullOrEmpty(state.label)) state.label = "Shop #" + zoneId;
            return state;
        }

        private void ResetTransientCaches()
        {
            foreach (ShopAnalyticsState state in shopStates.Values)
            {
                if (state == null) continue;
                state.lastEvaluateTick = -1;
                state.cachedMetrics = null;
            }
        }

        private void MigrateLegacyData()
        {
            if (legacyShopReputation == null
                && legacyShopSatisfactionEma == null
                && legacyShopSuccessfulCheckouts == null
                && legacyShopTimeoutCheckouts == null
                && legacyShopQueueWaitTicksTotal == null
                && legacyShopQueueWaitSamples == null
                && legacyShopLabels == null
                && legacyShopLastScore == null
                && legacyShopLastBeauty == null
                && legacyShopLastEnvironment == null)
            {
                return;
            }

            HashSet<int> keys = new HashSet<int>();
            AddKeys(keys, legacyShopReputation);
            AddKeys(keys, legacyShopSatisfactionEma);
            AddKeys(keys, legacyShopSuccessfulCheckouts);
            AddKeys(keys, legacyShopTimeoutCheckouts);
            AddKeys(keys, legacyShopQueueWaitTicksTotal);
            AddKeys(keys, legacyShopQueueWaitSamples);
            AddKeys(keys, legacyShopLabels);
            AddKeys(keys, legacyShopLastScore);
            AddKeys(keys, legacyShopLastBeauty);
            AddKeys(keys, legacyShopLastEnvironment);

            foreach (int zoneId in keys)
            {
                string label = TryGet(legacyShopLabels, zoneId, out string savedLabel) ? savedLabel : null;
                ShopAnalyticsState state = GetOrCreateState(zoneId, label);
                if (TryGet(legacyShopReputation, zoneId, out float reputation)) state.reputation = reputation;
                if (TryGet(legacyShopSatisfactionEma, zoneId, out float satisfaction)) state.satisfactionEma = satisfaction;
                if (TryGet(legacyShopSuccessfulCheckouts, zoneId, out int success)) state.successfulCheckouts = success;
                if (TryGet(legacyShopTimeoutCheckouts, zoneId, out int timeout)) state.timeoutCheckouts = timeout;
                if (TryGet(legacyShopQueueWaitTicksTotal, zoneId, out float waitTotal)) state.queueWaitTicksTotal = waitTotal;
                if (TryGet(legacyShopQueueWaitSamples, zoneId, out int waitSamples)) state.queueWaitSamples = waitSamples;
                if (TryGet(legacyShopLastScore, zoneId, out float score)) state.lastScore = score;
                if (TryGet(legacyShopLastBeauty, zoneId, out float beauty)) state.lastBeauty = beauty;
                if (TryGet(legacyShopLastEnvironment, zoneId, out float environment)) state.lastEnvironment = environment;
            }
        }

        private static void AddKeys<T>(HashSet<int> keys, Dictionary<int, T> source)
        {
            if (source == null) return;
            foreach (int key in source.Keys)
            {
                keys.Add(key);
            }
        }

        private static bool TryGet<T>(Dictionary<int, T> source, int key, out T value)
        {
            if (source != null && source.TryGetValue(key, out value)) return true;
            value = default(T);
            return false;
        }

        private static ShopTuningDef GetTuning()
        {
            return DefDatabase<ShopTuningDef>.AllDefsListForReading.FirstOrDefault() ?? FallbackTuning;
        }

        private static float WeightedNormalized3(float v1, float w1, float v2, float w2, float v3, float w3)
        {
            float ws = Mathf.Max(0f, w1) + Mathf.Max(0f, w2) + Mathf.Max(0f, w3);
            if (ws <= 0.0001f) return Mathf.Clamp01((v1 + v2 + v3) / 3f);
            return Mathf.Clamp01((v1 * Mathf.Max(0f, w1) + v2 * Mathf.Max(0f, w2) + v3 * Mathf.Max(0f, w3)) / ws);
        }

        private static float WeightedNormalized4(float v1, float w1, float v2, float w2, float v3, float w3, float v4, float w4)
        {
            float ws = Mathf.Max(0f, w1) + Mathf.Max(0f, w2) + Mathf.Max(0f, w3) + Mathf.Max(0f, w4);
            if (ws <= 0.0001f) return Mathf.Clamp01((v1 + v2 + v3 + v4) / 4f);
            return Mathf.Clamp01((v1 * Mathf.Max(0f, w1) + v2 * Mathf.Max(0f, w2) + v3 * Mathf.Max(0f, w3) + v4 * Mathf.Max(0f, w4)) / ws);
        }

        private static float WeightedNormalized5(
            float v1, float w1,
            float v2, float w2,
            float v3, float w3,
            float v4, float w4,
            float v5, float w5)
        {
            float ws = Mathf.Max(0f, w1) + Mathf.Max(0f, w2) + Mathf.Max(0f, w3) + Mathf.Max(0f, w4) + Mathf.Max(0f, w5);
            if (ws <= 0.0001f) return (v1 + v2 + v3 + v4 + v5) / 5f;
            return (
                v1 * Mathf.Max(0f, w1)
              + v2 * Mathf.Max(0f, w2)
              + v3 * Mathf.Max(0f, w3)
              + v4 * Mathf.Max(0f, w4)
              + v5 * Mathf.Max(0f, w5)) / ws;
        }

        private static void AddInt(Dictionary<int, int> map, int key, int delta)
        {
            if (delta == 0) return;
            if (map.TryGetValue(key, out int old))
                map[key] = old + delta;
            else
                map[key] = delta;
        }

        private static void AddFloat(Dictionary<int, float> map, int key, float delta)
        {
            if (Math.Abs(delta) < 0.001f) return;
            if (map.TryGetValue(key, out float old))
                map[key] = old + delta;
            else
                map[key] = delta;
        }
    }
}
