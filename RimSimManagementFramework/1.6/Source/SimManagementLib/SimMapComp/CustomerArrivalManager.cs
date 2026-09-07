using RimWorld;
using SimManagementLib.Api;
using SimManagementLib.GameComp;
using SimManagementLib.Pojo;
using SimManagementLib.SimAI;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimThingComp;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SimManagementLib.SimMapComp
{
    //类职责：协调地图顾客索引、商店吸引力缓存、分片刷新、结账票据和可靠离店。
    public partial class CustomerArrivalManager : MapComponent
    {
        private const int DefaultCheckInterval = 500;
        private const int ReviewInfluenceMinCount = 3;
        private const float ReviewInfluenceMinMultiplier = 0.70f;
        private const float ReviewInfluenceMaxMultiplier = 1.30f;
        private const int SpawnFailureBackoffTicks = 2500;
        private const int SpawnFailureLogIntervalTicks = 10000;
        private readonly Dictionary<string, int> spawnRetryTicks = new Dictionary<string, int>();
        private readonly Dictionary<string, int> spawnFailureLogTicks = new Dictionary<string, int>();
        private CustomerRuntimeIndex customerIndex;
        private CustomerArrivalRuntime arrivalRuntime;
        private int nextArrivalCheckTick = -1;
        private bool arrivalCycleActive;
        private bool vendingAttemptPending;
        private string lastSpawnFailureReason = "";

        //绑定地图顾客生成器，职责是维护该地图的到店调度状态。
        public CustomerArrivalManager(Map map) : base(map)
        {
        }

        //推进地图级顾客运行态，职责是把刷新重算和候选尝试稳定分摊到多个 tick。
        public override void MapComponentTick()
        {
            base.MapComponentTick();
            EnsureRuntime();
            customerIndex.Tick();
            arrivalRuntime.Tick();

            int checkInterval = GetCheckIntervalTicks();
            if (checkInterval <= 0) checkInterval = DefaultCheckInterval;
            int now = Find.TickManager?.TicksGame ?? 0;
            if (nextArrivalCheckTick < 0)
                nextArrivalCheckTick = now + GetInitialCheckDelay(checkInterval);
            if (!arrivalCycleActive && now >= nextArrivalCheckTick)
            {
                ScheduleNextArrivalCheck(now, checkInterval);
                if (CustomerSafetyUtility.IsLargeHostileRaidActive(map))
                    return;
                arrivalRuntime.BeginCycle();
                arrivalCycleActive = true;
                vendingAttemptPending = true;
            }

            if (!arrivalCycleActive || CustomerSafetyUtility.IsLargeHostileRaidActive(map))
            {
                ProcessCustomerMetricsBudget();
                return;
            }

            int candidatesUsed = 0;
            while (candidatesUsed < 4 && arrivalRuntime.TryTakeNextContext(out CustomerArrivalShopContext context))
            {
                candidatesUsed++;
                context.CurrentCustomers = customerIndex.CountActiveForShop(context.Shop?.ID ?? -1);
                if (TrySpawnOneCustomerForShop(context))
                {
                    EndArrivalCycle();
                    return;
                }
            }

            if (!arrivalRuntime.CycleCompleted)
            {
                ProcessCustomerMetricsBudget();
                return;
            }

            if (vendingAttemptPending)
            {
                if (candidatesUsed >= 4)
                {
                    ProcessCustomerMetricsBudget();
                    return;
                }
                vendingAttemptPending = false;
                candidatesUsed++;
                if (TrySpawnOneCustomerForVendingMachines(checkInterval))
                {
                    EndArrivalCycle();
                    return;
                }
            }

            EndArrivalCycle();
            ProcessCustomerMetricsBudget();
        }

        //推进商店指标预算，职责是确保生成 Pawn 的 tick 不再执行经营指标采样。
        private void ProcessCustomerMetricsBudget()
        {
            Current.Game?.GetComponent<GameComponent_ShopAnalyticsManager>()?.ProcessMetricsBudget(map, 64, 8);
        }

        //结束一次分片刷新周期，职责是清理临时候选游标而不丢弃长期缓存。
        private void EndArrivalCycle()
        {
            arrivalCycleActive = false;
            vendingAttemptPending = false;
            arrivalRuntime.EndCycle();
        }

        //强制刷新一波顾客，负责保留旧版外部调用签名。
        public bool ForceSpawnOneWave(bool ignoreConditions, out string resultMessage)
        {
            return ForceSpawnOneWave(ignoreConditions, out resultMessage, out _);
        }

        //强制刷新一波顾客，负责在成功时把生成的 Pawn 引用返回给外部调用方。
        public bool ForceSpawnOneWave(bool ignoreConditions, out string resultMessage, out Pawn spawnedPawn)
        {
            EnsureRuntime();
            Dictionary<int, string> actionReasons = RefreshActionAttractionDiagnostics();
            arrivalRuntime.RefreshAllSynchronously();
            spawnedPawn = null;
            if (CustomerSafetyUtility.IsLargeHostileRaidActive(map))
            {
                resultMessage = "强制刷新失败：当前地图存在超过 1000 点战斗力的敌对袭击，顾客不会到访。";
                return false;
            }

            List<CustomerArrivalShopContext> contexts = CollectActiveShopContexts();
            List<Building_SimContainer> vendingMachines = VendingMachineUtility.GetVendingMachineSnapshot(map)
                .Where(VendingMachineUtility.IsUsableVendingMachine)
                .ToList();

            if (contexts.NullOrEmpty() && vendingMachines.NullOrEmpty())
            {
                resultMessage = BuildNoOpenShopFailureMessage();
                return false;
            }

            List<RuntimeCustomerKind> kinds = CustomerCatalog.Kinds
                .Where(k => k != null && !k.pawnKindDefs.NullOrEmpty())
                .ToList();
            kinds = ApplyForcedCustomerKindFilter(kinds);
            if (!ignoreConditions)
            {
                kinds = kinds.Where(k => k.CanAppearNow(map)).ToList();
            }

            if (kinds.NullOrEmpty())
            {
                resultMessage = ignoreConditions
                    ? SimTranslation.T("RSMF.CustomerArrival.ForceFailNoKinds")
                    : SimTranslation.T("RSMF.CustomerArrival.ForceFailNoKindForConditions");
                return false;
            }

            float hour = GenLocalDate.HourFloat(map);
            List<RuntimeCustomerKind> orderedKinds = kinds
                .OrderByDescending(k => k.EvaluateArrivalWeight(hour))
                .ToList();
            string lastFailReason = "";

            foreach (CustomerArrivalShopContext context in contexts.OrderBy(_ => Rand.Value))
            {
                foreach (RuntimeCustomerKind kind in orderedKinds)
                {
                    if (ignoreConditions)
                    {
                        if (!CanForceSpawnWave(context, kind)) continue;
                    }
                    else if (!CanSpawnWave(context, kind))
                    {
                        continue;
                    }

                    if (TrySpawnCustomerWave(context.Shop, kind, false, false, out int spawnedCount, out string failReason, out Pawn pawn))
                    {
                        spawnedPawn = pawn;
                        resultMessage = SimTranslation.T("RSMF.CustomerArrival.ForceSpawnShopSuccess", context.Shop.label.Named("shop"), spawnedCount.Named("count"));
                        return true;
                    }

                    if (!string.IsNullOrEmpty(failReason))
                        lastFailReason = failReason;
                }
            }

            foreach (Building_SimContainer machine in vendingMachines.InRandomOrder())
            {
                foreach (RuntimeCustomerKind kind in orderedKinds)
                {
                    if (!VendingMachineUtility.MatchesCustomerKind(machine, kind)) continue;
                    if (!ignoreConditions && !kind.CanAppearNow(map)) continue;
                    if (TrySpawnVendingMachineCustomer(machine, kind, true, false, out int spawnedCount, out string failReason, out Pawn pawn))
                    {
                        spawnedPawn = pawn;
                        resultMessage = SimTranslation.T("RSMF.CustomerArrival.ForceSpawnVendingSuccess", machine.StorageDisplayLabel.Named("machine"), spawnedCount.Named("count"));
                        return true;
                    }

                    if (!string.IsNullOrEmpty(failReason))
                        lastFailReason = failReason;
                }
            }

            if (string.IsNullOrEmpty(lastFailReason))
                lastFailReason = DescribeForcedSpawnRejections(contexts, orderedKinds, actionReasons);
            lastSpawnFailureReason = lastFailReason;
            resultMessage = string.IsNullOrEmpty(lastFailReason)
                ? SimTranslation.T("RSMF.CustomerArrival.ForceFailNoMatchingKind")
                : SimTranslation.T("RSMF.CustomerArrival.ForceFailWithReason", lastFailReason.Named("reason"));
            return false;
        }

        //为单个商店尝试生成一位顾客，职责是向地图级生成预算返回是否已经成功占用本轮名额。
        private bool TrySpawnOneCustomerForShop(CustomerArrivalShopContext context)
        {
            if (context?.Shop == null || context.IsAtCapacity) return false;
            float hour = GenLocalDate.HourFloat(map);
            RuntimeCustomerKind selected = null;
            float totalWeight = 0f;
            string forcedKindId = ResolveValidForcedKindId();
            IReadOnlyCollection<RuntimeCustomerKind> kinds = CustomerCatalog.Kinds;
            if (kinds == null) return false;
            foreach (RuntimeCustomerKind kind in kinds)
            {
                if (!string.IsNullOrEmpty(forcedKindId) && !string.Equals(kind?.kindId, forcedKindId, System.StringComparison.OrdinalIgnoreCase)) continue;
                if (!CanSpawnWave(context, kind)) continue;
                float weight = Mathf.Max(0.001f, kind.EvaluateArrivalWeight(hour));
                totalWeight += weight;
                if (Rand.Value * totalWeight <= weight)
                    selected = kind;
            }
            if (selected == null) return false;

            return TrySpawnCustomerWave(context.Shop, selected, true, true, out _, out _, out _);
        }

        //收集可营业店铺上下文，职责是供顾客生成规则评估。
        private List<CustomerArrivalShopContext> CollectActiveShopContexts()
        {
            EnsureRuntime();
            return arrivalRuntime.GetOpenContexts(customerIndex);
        }
        //周期性尝试为地图上的自动售货机刷新顾客。
        private bool TrySpawnOneCustomerForVendingMachines(int checkInterval)
        {
            IReadOnlyList<Building_SimContainer> machines = VendingMachineUtility.GetVendingMachineSnapshot(map);
            Building_SimContainer selectedMachine = null;
            int machineSeen = 0;
            for (int i = 0; i < machines.Count; i++)
            {
                Building_SimContainer machine = machines[i];
                if (!VendingMachineUtility.IsUsableVendingMachine(machine)) continue;
                ThingComp_VendingMachine comp = machine.GetComp<ThingComp_VendingMachine>();
                int currentCustomers = customerIndex.CountActiveForVendingMachine(machine.thingIDNumber);
                if (comp == null || currentCustomers >= comp.MaxSimultaneousCustomers) continue;
                machineSeen++;
                if (Rand.RangeInclusive(1, machineSeen) == 1)
                    selectedMachine = machine;
            }
            if (selectedMachine == null) return false;

            float hour = GenLocalDate.HourFloat(map);
            RuntimeCustomerKind selectedKind = null;
            float totalWeight = 0f;
            string forcedKindId = ResolveValidForcedKindId();
            IReadOnlyCollection<RuntimeCustomerKind> kinds = CustomerCatalog.Kinds;
            if (kinds == null) return false;
            foreach (RuntimeCustomerKind kind in kinds)
            {
                if (kind == null || kind.pawnKindDefs.NullOrEmpty() || !kind.CanAppearNow(map)) continue;
                if (!string.IsNullOrEmpty(forcedKindId) && !string.Equals(kind.kindId, forcedKindId, System.StringComparison.OrdinalIgnoreCase)) continue;
                if (!VendingMachineUtility.MatchesCustomerKind(selectedMachine, kind)) continue;
                float weight = Mathf.Max(0.001f, kind.EvaluateArrivalWeight(hour));
                totalWeight += weight;
                if (Rand.Value * totalWeight <= weight)
                    selectedKind = kind;
            }
            if (selectedKind == null) return false;

            ThingComp_VendingMachine selectedComp = selectedMachine.GetComp<ThingComp_VendingMachine>();
            float mtbDays = selectedComp.BaseMtbDays / Mathf.Max(selectedKind.EvaluateArrivalWeight(hour), 0.05f);
            return Rand.MTBEventOccurs(mtbDays, 60000f, checkInterval)
                && TrySpawnVendingMachineCustomer(selectedMachine, selectedKind, false, true, out _, out _, out _);
        }

        //解析有效 Debug 强制类型，职责是在配置目标不存在时保持普通候选语义。
        private static string ResolveValidForcedKindId()
        {
            string forcedKindId = SimManagementLibMod.Settings?.debugForcedCustomerKindId;
            return !string.IsNullOrEmpty(forcedKindId) && CustomerCatalog.GetKind(forcedKindId) != null ? forcedKindId : "";
        }
        //根据设置中的 Debug 强制顾客组过滤候选列表。
        private static List<RuntimeCustomerKind> ApplyForcedCustomerKindFilter(List<RuntimeCustomerKind> kinds)
        {
            string forcedKindId = SimManagementLibMod.Settings?.debugForcedCustomerKindId;
            if (string.IsNullOrEmpty(forcedKindId) || kinds.NullOrEmpty())
                return kinds;

            List<RuntimeCustomerKind> forced = kinds
                .Where(kind => string.Equals(kind.kindId, forcedKindId, System.StringComparison.OrdinalIgnoreCase))
                .ToList();
            return forced.NullOrEmpty() ? kinds : forced;
        }

        //构建商店刷新快照，职责是把昂贵匹配计算限制在脏队列预算内。
        internal CustomerArrivalShopContext BuildShopContext(Zone_Shop shop, GameComponent_ShopAnalyticsManager analytics, int currentCustomers)
        {
            if (shop == null) return null;

            ShopMetricsSnapshot metrics = analytics?.GetOrEvaluateShopMetrics(shop);
            CustomerArrivalShopContext context = new CustomerArrivalShopContext
            {
                Shop = shop,
                CurrentCustomers = currentCustomers,
                Capacity = metrics != null ? Mathf.Max(2, metrics.dynamicCapacity) : CalculateShopCustomerCapacity(shop),
                DemandFactor = ApplyReviewDemandInfluence(shop, metrics?.spawnDemandFactor ?? 1f),
                HasCheckoutService = ShopStaffUtility.HasMannedCashRegister(shop),
                IsOpen = shop.IsOpenNow()
            };

            TryFindReachableShopEntryCell(shop, out context.EntryCell);
            HashSet<ThingDef> stockedDefs = CollectStockedDefs(shop);
            HashSet<string> serviceCategoryIds = CollectServiceCategoryIds(shop);
            IReadOnlyList<Building_CashRegister> registers = ShopDataUtility.GetCashRegisterSnapshotInZone(shop);
            for (int i = 0; i < registers.Count; i++) RegisterCustomerTarget(registers[i]);
            IReadOnlyCollection<RuntimeCustomerKind> kinds = CustomerCatalog.Kinds;
            if (kinds != null)
            {
                foreach (RuntimeCustomerKind kind in kinds)
                {
                    if (kind != null && (SnapshotMatchesKind(stockedDefs, serviceCategoryIds, kind)
                        || CustomerActionAttractionUtility.MatchesCustomer(shop, kind)))
                        context.MatchingKindIds.Add(kind.kindId ?? "");
                }
            }

            return context;
        }

        //构造强制刷新无开放商店的诊断文本，职责是区分未划区、校验失败、计划关店和快照异常。
        private string BuildNoOpenShopFailureMessage()
        {
            List<Zone_Shop> shops = map?.zoneManager?.AllZones?
                .OfType<Zone_Shop>()
                .Where(shop => shop != null)
                .ToList() ?? new List<Zone_Shop>();
            if (shops.Count == 0)
                return SimTranslation.T("RSMF.CustomerArrival.ForceFailNoShopOrVending");

            List<Zone_Shop> validShops = shops.Where(shop => shop.IsValidShop()).ToList();
            if (validShops.Count == 0)
            {
                Zone_Shop first = shops[0];
                return SimTranslation.T("RSMF.CustomerArrival.ForceFailInvalidShops",
                    shops.Count.Named("count"),
                    first.label.Named("shop"),
                    first.GetValidationMessage().Named("reason"));
            }

            Zone_Shop openShop = validShops.FirstOrDefault(shop => shop.IsOpenNow());
            if (openShop == null)
            {
                Zone_Shop first = validShops[0];
                return SimTranslation.T("RSMF.CustomerArrival.ForceFailClosedShops",
                    validShops.Count.Named("count"),
                    first.label.Named("shop"),
                    first.GetOpenStatusMessage().Named("reason"));
            }

            return SimTranslation.T("RSMF.CustomerArrival.ForceFailShopContext",
                openShop.label.Named("shop"));
        }

        //汇总商店有货商品 Def，职责是让顾客类型匹配不再为每个类型重复扫描货柜。
        private static HashSet<ThingDef> CollectStockedDefs(Zone_Shop shop)
        {
            HashSet<ThingDef> result = new HashSet<ThingDef>();
            IReadOnlyList<Building_SimContainer> storages = ShopDataUtility.GetStorageSnapshotInZone(shop);
            for (int i = 0; i < storages.Count; i++)
            {
                Building_SimContainer storage = storages[i];
                if (storage == null || storage.Destroyed || !storage.Spawned || !storage.AllowsCustomerSelfPurchase) continue;
                foreach (ThingDef def in storage.ActiveDefs)
                {
                    if (def != null && storage.CountStored(def) > 0)
                        result.Add(def);
                }
            }
            return result;
        }

        //汇总商店当前可用服务分类，职责是让服务匹配只扫描一次区划设施。
        private HashSet<string> CollectServiceCategoryIds(Zone_Shop shop)
        {
            HashSet<string> result = new HashSet<string>();
            foreach (Thing provider in ShopServiceUtility.GetServiceProvidersInZone(shop))
            {
                RegisterCustomerTarget(provider);
                ThingComp_ServiceProvider comp = provider.TryGetComp<ThingComp_ServiceProvider>();
                if (comp == null || !comp.enabled) continue;
                foreach (ServiceSlotData slot in comp.EnabledSlots)
                {
                    string categoryId = slot?.ServiceDef?.serviceCategoryId;
                    if (!string.IsNullOrEmpty(categoryId) && ShopServiceUtility.CanAcceptMoreUsers(provider, slot.ServiceDef))
                        result.Add(categoryId);
                }
            }
            return result;
        }

        //判断原子商店快照是否匹配顾客类型，职责是只遍历聚合后的 Def 和分类集合。
        private static bool SnapshotMatchesKind(HashSet<ThingDef> stockedDefs, HashSet<string> serviceCategoryIds, RuntimeCustomerKind kind)
        {
            List<string> goodsTargets = kind.targetGoodsCategoryIds;
            List<string> serviceTargets = kind.targetServiceCategoryIds;
            bool goodsAllowed = !goodsTargets.NullOrEmpty() || serviceTargets.NullOrEmpty();
            if (goodsAllowed && stockedDefs.Count > 0)
            {
                if (goodsTargets.NullOrEmpty()) return true;
                foreach (ThingDef def in stockedDefs)
                {
                    for (int i = 0; i < goodsTargets.Count; i++)
                    {
                        if (!string.IsNullOrEmpty(goodsTargets[i]) && GoodsCatalog.Contains(goodsTargets[i], def))
                            return true;
                    }
                }
            }

            if (serviceCategoryIds.Count == 0) return false;
            if (serviceTargets.NullOrEmpty()) return true;
            for (int i = 0; i < serviceTargets.Count; i++)
            {
                if (!string.IsNullOrEmpty(serviceTargets[i]) && serviceCategoryIds.Contains(serviceTargets[i]))
                    return true;
            }
            return false;
        }
        //根据店铺评价调整刷客需求倍率，负责让玩家可选地把口碑反馈接入真实来客概率。
        private static float ApplyReviewDemandInfluence(Zone_Shop shop, float demandFactor)
        {
            if (shop == null || SimManagementLibMod.Settings?.reviewInfluencesCustomerSpawn != true)
                return demandFactor;

            GameComponent_CustomerReviewManager reviewManager = Current.Game?.GetComponent<GameComponent_CustomerReviewManager>();
            if (reviewManager == null)
                return demandFactor;

            reviewManager.GetShopReviewStats(shop.ID, out float averageStars, out int count);
            if (count < ReviewInfluenceMinCount || averageStars <= 0f)
                return demandFactor;

            float normalized = Mathf.InverseLerp(1f, 5f, averageStars);
            float multiplier = Mathf.Lerp(ReviewInfluenceMinMultiplier, ReviewInfluenceMaxMultiplier, normalized);
            return Mathf.Max(0.01f, demandFactor * multiplier);
        }

        //检查本轮是否能够生成顾客，职责是应用店铺和客群限制。
        private bool CanSpawnWave(CustomerArrivalShopContext context, RuntimeCustomerKind kind)
        {
            if (context == null || !context.CanSpawn(kind, true)) return false;
            if (!kind.CanAppearNow(map)) return false;
            if (kind.minShopReputation > 0f)
            {
                float reputation = Current.Game?.GetComponent<GameComponent_ShopAnalyticsManager>()?.GetReputation(context.Shop.ID) ?? 0f;
                if (reputation < kind.minShopReputation)
                    return false;
            }

            float hour = GenLocalDate.HourFloat(map);
            float weight = 1f;
            weight = kind.EvaluateArrivalWeight(hour);

            if (weight <= 0.01f) return false;

            float baseMtb = kind.baseMtbDays > 0f ? kind.baseMtbDays : 0.25f;
            float mtbDays = baseMtb / Mathf.Max(weight * context.DemandFactor, 0.05f);
            return Rand.MTBEventOccurs(mtbDays, 60000f, GetCheckIntervalTicks());
        }
        //判断 Debug 强制刷新是否允许指定顾客进入商店，只跳过时间、天气和随机概率，不跳过商店匹配。
        private bool CanForceSpawnWave(CustomerArrivalShopContext context, RuntimeCustomerKind kind)
        {
            if (context == null || !context.CanSpawn(kind, false)) return false;
            if (kind == null) return false;
            if (kind.minShopReputation > 0f)
            {
                float reputation = Current.Game?.GetComponent<GameComponent_ShopAnalyticsManager>()?.GetReputation(context.Shop.ID) ?? 0f;
                if (reputation < kind.minShopReputation)
                    return false;
            }

            return true;
        }

        //计算店铺顾客容量，职责是限制同时到店的人数。
        private int CalculateShopCustomerCapacity(Zone_Shop shop)
        {
            int storageCount = ShopDataUtility.GetStorageSnapshotInZone(shop).Count;
            int registerCount = ShopDataUtility.GetCashRegisterSnapshotInZone(shop).Count;
            int estimated = registerCount * 8 + storageCount * 4;
            return Mathf.Max(6, estimated);
        }
        //查找商店内可作为顾客入店目标的站立格，负责避免不可达商店生成后直接离图。
        internal bool TryFindReachableShopEntryCell(Zone_Shop shop, out IntVec3 targetCell)
        {
            targetCell = IntVec3.Invalid;
            if (shop == null) return false;

            List<IntVec3> storageCells = ShopDataUtility.GetStorageSnapshotInZone(shop)
                .Where(storage => storage != null && !storage.Destroyed && storage.Spawned)
                .Select(storage => storage.InteractionCell)
                .Where(cell => cell.IsValid && cell.Standable(map))
                .ToList();
            if (!storageCells.NullOrEmpty())
            {
                targetCell = storageCells.RandomElement();
                return true;
            }

            List<IntVec3> shopCells = shop.Cells
                .Where(cell => cell.IsValid && cell.Standable(map))
                .ToList();
            if (shopCells.NullOrEmpty()) return false;

            targetCell = shopCells.RandomElement();
            return true;
        }
        //为指定商店生成一位顾客并绑定顾客 Lord，失败时返回具体原因。
        private bool TrySpawnCustomerWave(Zone_Shop shop, RuntimeCustomerKind kind, bool showArrivalMessage, bool respectFailureBackoff, out int spawnedCount, out string failReason, out Pawn spawnedPawn)
        {
            spawnedCount = 0;
            failReason = string.Empty;
            spawnedPawn = null;
            string attemptKey = BuildSpawnAttemptKey("shop", shop?.ID ?? -1, kind);
            if (!CanStartSpawnAttempt(attemptKey, respectFailureBackoff, out failReason))
                return false;

            Faction customerFaction = CustomerNeutralFactionUtility.GetOrCreateCustomerFaction();
            if (customerFaction == null)
                return FailSpawnAttempt(attemptKey, "缺少顾客中立派系", respectFailureBackoff, out failReason);

            PawnKindDef selectedKind = SelectPawnKindForCustomerFaction(kind, customerFaction);
            if (selectedKind == null)
                return FailSpawnAttempt(attemptKey, "没有与顾客派系兼容的 PawnKind", respectFailureBackoff, out failReason);

            if (!TryFindReachableShopEntryCell(shop, out IntVec3 shopTargetCell))
                return FailSpawnAttempt(attemptKey, "商店没有可用目标格", respectFailureBackoff, out failReason);

            if (!TryFindCustomerEdgeSpawnCell(out IntVec3 spawnSpot))
                return FailSpawnAttempt(attemptKey, "地图边缘没有可达入口", respectFailureBackoff, out failReason);
            if (respectFailureBackoff && !TryConsumeReachabilityBudget())
            {
                failReason = "等待顾客可达性预算";
                DeferArrivalForBudget();
                return false;
            }

            PawnGenerationRequest request = CreateCustomerPawnGenerationRequest(selectedKind, customerFaction);
            Pawn pawn = PawnGenerator.GeneratePawn(request);
            if (pawn == null)
                return FailSpawnAttempt(attemptKey, "PawnGenerator 未返回顾客", respectFailureBackoff, out failReason);

            if (CustomerNeutralFactionUtility.IsProtectedFactionLeader(pawn))
            {
                DiscardGeneratedPawn(pawn);
                return FailSpawnAttempt(attemptKey, "生成结果是受保护的派系领袖", respectFailureBackoff, out failReason);
            }

            if (!CustomerNeutralFactionUtility.ConvertPawnToCustomerFaction(pawn, customerFaction))
            {
                DiscardGeneratedPawn(pawn);
                return FailSpawnAttempt(attemptKey, "无法设置顾客中立派系", respectFailureBackoff, out failReason);
            }

            GenSpawn.Spawn(pawn, spawnSpot, map);
            CustomerNeedUtility.StabilizeCustomerNeeds(pawn);

            int fallbackBudget = kind.budgetRange.RandomInRange;
            LordJob_CustomerVisit lordJob = new LordJob_CustomerVisit(kind.sourceDef, shop.ID, shopTargetCell, fallbackBudget);
            lordJob.customerKindId = kind.kindId;
            CustomerRuntimeSettings settings = kind.BuildRuntimeSettings(map);
            lordJob.SetPawnSettings(pawn.thingIDNumber, settings);
            //顾客 Pawn 和 Lord 都使用商店专用中立派系，避免敌对来源派系残留为红名或触发战斗 AI。
            Lord newLord = LordMaker.MakeNewLord(customerFaction, lordJob, map, new List<Pawn> { pawn });
            RegisterShopCustomer(pawn, lordJob);
            if (!CustomerSafetyUtility.CanCustomerReach(pawn, shopTargetCell, PathEndMode.OnCell, Danger.Deadly))
            {
                RejectSpawnedCustomerAtEdge(pawn, newLord);
                return FailSpawnAttempt(attemptKey, "真实顾客门禁规则无法到达商店", respectFailureBackoff, out failReason);
            }
            spawnedCount = 1;
            spawnedPawn = pawn;
            ClearSpawnFailure(attemptKey);
            CustomerExpressionUtility.TryShowExpression(pawn, CustomerExpressionEvents.Arrival);
            SimShopEvents.NotifyCustomerArrived(pawn);

            if (showArrivalMessage)
            {
                WeatherDef weather = map.weatherManager?.curWeather;
                string weatherLabel = weather != null ? weather.LabelCap.RawText : SimTranslation.T("RSMF.CustomerArrival.UnknownWeather");
                if (SimManagementLibMod.Settings?.showCustomerArrivalMessage ?? true)
                {
                    Messages.Message(
                        SimTranslation.T("RSMF.CustomerArrival.ShopArrivalMessage", weatherLabel.Named("weather")),
                        pawn,
                        MessageTypeDefOf.NeutralEvent,
                        historical: true);
                }
            }

            return true;
        }
        //为指定自动售货机生成一位顾客并绑定独立的自动售货机访问 Lord。
        private bool TrySpawnVendingMachineCustomer(Building_SimContainer machine, RuntimeCustomerKind kind, bool showArrivalMessage, bool respectFailureBackoff, out int spawnedCount, out string failReason, out Pawn spawnedPawn)
        {
            spawnedCount = 0;
            failReason = string.Empty;
            spawnedPawn = null;
            string attemptKey = BuildSpawnAttemptKey("vending", machine?.thingIDNumber ?? -1, kind);
            if (!CanStartSpawnAttempt(attemptKey, respectFailureBackoff, out failReason))
                return false;
            if (machine == null || kind == null || !VendingMachineUtility.IsUsableVendingMachine(machine))
                return FailSpawnAttempt(attemptKey, "自动售货机不可用", respectFailureBackoff, out failReason);

            Faction customerFaction = CustomerNeutralFactionUtility.GetOrCreateCustomerFaction();
            if (customerFaction == null)
                return FailSpawnAttempt(attemptKey, "缺少顾客中立派系", respectFailureBackoff, out failReason);

            PawnKindDef selectedKind = SelectPawnKindForCustomerFaction(kind, customerFaction);
            if (selectedKind == null)
                return FailSpawnAttempt(attemptKey, "没有与顾客派系兼容的 PawnKind", respectFailureBackoff, out failReason);

            if (!TryFindCustomerEdgeSpawnCell(out IntVec3 spawnSpot))
                return FailSpawnAttempt(attemptKey, "地图边缘没有可达入口", respectFailureBackoff, out failReason);
            if (respectFailureBackoff && !TryConsumeReachabilityBudget())
            {
                failReason = "等待顾客可达性预算";
                DeferArrivalForBudget();
                return false;
            }

            Pawn pawn = PawnGenerator.GeneratePawn(CreateCustomerPawnGenerationRequest(selectedKind, customerFaction));
            if (pawn == null)
                return FailSpawnAttempt(attemptKey, "PawnGenerator 未返回顾客", respectFailureBackoff, out failReason);

            if (CustomerNeutralFactionUtility.IsProtectedFactionLeader(pawn))
            {
                DiscardGeneratedPawn(pawn);
                return FailSpawnAttempt(attemptKey, "生成结果是受保护的派系领袖", respectFailureBackoff, out failReason);
            }

            if (!CustomerNeutralFactionUtility.ConvertPawnToCustomerFaction(pawn, customerFaction))
            {
                DiscardGeneratedPawn(pawn);
                return FailSpawnAttempt(attemptKey, "无法设置顾客中立派系", respectFailureBackoff, out failReason);
            }

            GenSpawn.Spawn(pawn, spawnSpot, map);
            CustomerNeedUtility.StabilizeCustomerNeeds(pawn);
            int fallbackBudget = kind.budgetRange.RandomInRange;
            LordJob_VendingMachineVisit lordJob = new LordJob_VendingMachineVisit(kind.sourceDef, machine, fallbackBudget);
            lordJob.customerKindId = kind.kindId;
            lordJob.SetPawnSettings(pawn.thingIDNumber, kind.BuildRuntimeSettings(map));
            Lord newLord = LordMaker.MakeNewLord(customerFaction, lordJob, map, new List<Pawn> { pawn });
            RegisterVendingCustomer(pawn, lordJob);
            if (!CustomerSafetyUtility.CanCustomerReach(pawn, machine, PathEndMode.Touch, Danger.Deadly))
            {
                RejectSpawnedCustomerAtEdge(pawn, newLord);
                return FailSpawnAttempt(attemptKey, "真实顾客门禁规则无法到达自动售货机", respectFailureBackoff, out failReason);
            }
            spawnedCount = 1;
            spawnedPawn = pawn;
            ClearSpawnFailure(attemptKey);
            CustomerExpressionUtility.TryShowExpression(pawn, CustomerExpressionEvents.Arrival);
            SimShopEvents.NotifyCustomerArrived(pawn);

            if (showArrivalMessage && (SimManagementLibMod.Settings?.showCustomerArrivalMessage ?? true))
            {
                Messages.Message(
                    SimTranslation.T("RSMF.CustomerArrival.VendingArrivalMessage", machine.StorageDisplayLabel.Named("machine")),
                    pawn,
                    MessageTypeDefOf.NeutralEvent,
                    historical: true);
            }

            return true;
        }

        //创建临时顾客生成请求，职责是使用最终中立派系并关闭关系、头衔、武器和随身食物生成。
        private PawnGenerationRequest CreateCustomerPawnGenerationRequest(PawnKindDef selectedKind, Faction customerFaction)
        {
            return new PawnGenerationRequest(
                selectedKind,
                customerFaction,
                PawnGenerationContext.NonPlayer,
                tile: map.Tile,
                forceGenerateNewPawn: true,
                canGeneratePawnRelations: false,
                colonistRelationChanceFactor: 0f,
                allowFood: false,
                forbidAnyTitle: true,
                dontGiveWeapon: true);
        }

        //查找顾客可用的地图边缘生成点，职责是只做区域级粗筛并把真实寻路交给 Pawn 路径器。
        private bool TryFindCustomerEdgeSpawnCell(out IntVec3 spawnSpot)
        {
            spawnSpot = IntVec3.Invalid;
            if (map == null)
                return false;

            return CellFinder.TryFindRandomEdgeCellWith(IsUsableCustomerEdgeCell, map, CellFinder.EdgeRoadChance_Neutral, out spawnSpot);
        }

        //判断地图边缘格是否值得执行完整寻路，职责是用常数时间条件过滤雾区和不可站立格。
        private bool IsUsableCustomerEdgeCell(IntVec3 cell)
        {
            return cell.IsValid
                && cell.InBounds(map)
                && cell.Standable(map)
                && !cell.Fogged(map);
        }

        //选择顾客 PawnKind，职责是只保留与最终顾客派系人类属性一致的种类。
        private static PawnKindDef SelectPawnKindForCustomerFaction(RuntimeCustomerKind kind, Faction customerFaction)
        {
            if (kind == null || kind.pawnKindDefs.NullOrEmpty() || customerFaction?.def == null)
                return null;

            List<PawnKindDef> candidates = kind.pawnKindDefs
                .Where(pawnKind => pawnKind?.race?.race != null
                    && pawnKind.race.race.Humanlike == customerFaction.def.humanlikeFaction)
                .ToList();
            return candidates.NullOrEmpty() ? null : candidates.RandomElement();
        }

        //生成顾客失败键，职责是按目标和顾客类型隔离退避状态。
        private static string BuildSpawnAttemptKey(string targetType, int targetId, RuntimeCustomerKind kind)
        {
            return targetType + ":" + targetId + ":" + (kind?.kindId ?? "unknown");
        }

        //判断生成尝试是否已结束退避，职责是阻止同一失败条件反复占用主线程。
        private bool CanStartSpawnAttempt(string attemptKey, bool respectFailureBackoff, out string failReason)
        {
            failReason = string.Empty;
            if (!respectFailureBackoff)
                return true;

            int now = Find.TickManager?.TicksGame ?? 0;
            if (!spawnRetryTicks.TryGetValue(attemptKey, out int retryTick) || now >= retryTick)
                return true;

            failReason = "顾客生成正在等待失败重试";
            return false;
        }

        //记录顾客生成失败，职责是设置重试间隔并用限频日志保留诊断原因。
        private bool FailSpawnAttempt(string attemptKey, string reason, bool respectFailureBackoff, out string failReason)
        {
            failReason = reason ?? "未知生成失败";
            lastSpawnFailureReason = failReason;
            if (!respectFailureBackoff)
                return false;

            int now = Find.TickManager?.TicksGame ?? 0;
            spawnRetryTicks[attemptKey] = now + SpawnFailureBackoffTicks;
            if (!spawnFailureLogTicks.TryGetValue(attemptKey, out int nextLogTick) || now >= nextLogTick)
            {
                spawnFailureLogTicks[attemptKey] = now + SpawnFailureLogIntervalTicks;
                Log.Warning("[SimShop] 顾客生成暂缓：" + attemptKey + "，原因：" + failReason);
            }

            return false;
        }

        //清除成功目标的失败状态，职责是让暂时性故障恢复后立即回到正常刷新节奏。
        private void ClearSpawnFailure(string attemptKey)
        {
            spawnRetryTicks.Remove(attemptKey);
            spawnFailureLogTicks.Remove(attemptKey);
        }

        //丢弃未进入地图的临时顾客，职责是避免失败生成结果进入 WorldPawns 并长期占用内存。
        private static void DiscardGeneratedPawn(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed || pawn.Spawned)
                return;

            Find.WorldPawns.PassToWorld(pawn, RimWorld.Planet.PawnDiscardDecideMode.Discard);
        }

        //拒绝生成后不可达的顾客，职责是在发送到店事件前释放索引并从边缘立即退出。
        private void RejectSpawnedCustomerAtEdge(Pawn pawn, Lord customerLord)
        {
            if (pawn == null) return;
            UnregisterCustomer(pawn);
            customerLord?.Notify_PawnLost(pawn, PawnLostCondition.LeftVoluntarily);
            if (pawn.Spawned && pawn.Map == map && !pawn.Destroyed && !pawn.Dead)
                pawn.ExitMap(false, CellRect.WholeMap(map).GetClosestEdge(pawn.Position));
        }

        //计算地图首次刷新延迟，职责是让多地图组件避免在同一 tick 同步执行。
        private int GetInitialCheckDelay(int checkInterval)
        {
            int mapId = map?.uniqueID ?? 0;
            return 1 + Mathf.Abs((mapId * 397) % Mathf.Max(1, checkInterval));
        }

        //安排下一次顾客检查，职责是用稳定抖动打散长期周期峰值。
        private void ScheduleNextArrivalCheck(int now, int checkInterval)
        {
            int jitterRange = Mathf.Max(1, checkInterval / 5);
            int mapId = map?.uniqueID ?? 0;
            int jitter = Mathf.Abs((now + mapId * 31) % jitterRange);
            nextArrivalCheckTick = now + checkInterval + jitter;
        }

        //缩短预算等待后的下一轮刷新时间，职责是保证暂缓候选在 120 tick 内重新进入调度。
        private void DeferArrivalForBudget()
        {
            int retryTick = (Find.TickManager?.TicksGame ?? 0) + 120;
            if (nextArrivalCheckTick < 0 || retryTick < nextArrivalCheckTick)
                nextArrivalCheckTick = retryTick;
        }

        //计算生成检查间隔，职责是统一周期调度节奏。
        private static int GetCheckIntervalTicks()
        {
            int value = SimManagementLibMod.Settings?.customerArrivalCheckIntervalTicks ?? DefaultCheckInterval;
            return Mathf.Clamp(value, 120, 5000);
        }

    }
}
