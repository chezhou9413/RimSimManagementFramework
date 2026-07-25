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
    /// <summary>
    /// 管理地图上商店顾客的周期性刷新、强制刷新和顾客队伍生成。
    /// </summary>
    public class CustomerArrivalManager : MapComponent
    {
        private const int DefaultCheckInterval = 500;
        private const int ReviewInfluenceMinCount = 3;
        private const float ReviewInfluenceMinMultiplier = 0.70f;
        private const float ReviewInfluenceMaxMultiplier = 1.30f;
        private const int SpawnFailureBackoffTicks = 2500;
        private const int SpawnFailureLogIntervalTicks = 10000;
        private const int MaxEdgeSpawnPathChecks = 8;
        private readonly Dictionary<string, int> spawnRetryTicks = new Dictionary<string, int>();
        private readonly Dictionary<string, int> spawnFailureLogTicks = new Dictionary<string, int>();
        private int nextArrivalCheckTick = -1;

        public CustomerArrivalManager(Map map) : base(map)
        {
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            int checkInterval = GetCheckIntervalTicks();
            if (checkInterval <= 0) checkInterval = DefaultCheckInterval;
            int now = Find.TickManager?.TicksGame ?? 0;
            if (nextArrivalCheckTick < 0)
                nextArrivalCheckTick = now + GetInitialCheckDelay(checkInterval);
            if (now < nextArrivalCheckTick) return;
            ScheduleNextArrivalCheck(now, checkInterval);

            List<CustomerArrivalShopContext> contexts = CollectActiveShopContexts();
            if (contexts.NullOrEmpty() && VendingMachineUtility.GetVendingMachineSnapshot(map).Count <= 0)
                return;
            if (CustomerSafetyUtility.IsLargeHostileRaidActive(map)) return;

            if (!contexts.NullOrEmpty())
            {
                contexts.Shuffle();
                foreach (CustomerArrivalShopContext context in contexts)
                {
                    if (TrySpawnOneCustomerForShop(context))
                        return;
                }
            }

            TrySpawnOneCustomerForVendingMachines(checkInterval);
        }

        // 强制刷新一波顾客，负责保留旧版外部调用签名。
        public bool ForceSpawnOneWave(bool ignoreConditions, out string resultMessage)
        {
            return ForceSpawnOneWave(ignoreConditions, out resultMessage, out _);
        }

        // 强制刷新一波顾客，负责在成功时把生成的 Pawn 引用返回给外部调用方。
        public bool ForceSpawnOneWave(bool ignoreConditions, out string resultMessage, out Pawn spawnedPawn)
        {
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
                resultMessage = SimTranslation.T("RSMF.CustomerArrival.ForceFailNoShopOrVending");
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

            resultMessage = string.IsNullOrEmpty(lastFailReason)
                ? SimTranslation.T("RSMF.CustomerArrival.ForceFailNoMatchingKind")
                : SimTranslation.T("RSMF.CustomerArrival.ForceFailWithReason", lastFailReason.Named("reason"));
            return false;
        }

        //为单个商店尝试生成一位顾客，职责是向地图级生成预算返回是否已经成功占用本轮名额。
        private bool TrySpawnOneCustomerForShop(CustomerArrivalShopContext context)
        {
            if (context?.Shop == null || context.IsAtCapacity) return false;

            List<RuntimeCustomerKind> candidates = CustomerCatalog.Kinds
                .Where(k => CanSpawnWave(context, k))
                .ToList();
            candidates = ApplyForcedCustomerKindFilter(candidates);
            if (candidates.NullOrEmpty()) return false;

            float hour = GenLocalDate.HourFloat(map);
            RuntimeCustomerKind selected = candidates.RandomElementByWeight(k => k.EvaluateArrivalWeight(hour));
            if (selected == null) return false;

            return TrySpawnCustomerWave(context.Shop, selected, true, true, out _, out _, out _);
        }

        private List<CustomerArrivalShopContext> CollectActiveShopContexts()
        {
            List<CustomerArrivalShopContext> result = new List<CustomerArrivalShopContext>();
            if (map?.zoneManager?.AllZones == null)
                return result;

            GameComponent_ShopAnalyticsManager analytics = Current.Game?.GetComponent<GameComponent_ShopAnalyticsManager>();
            List<Zone_Shop> openShops = new List<Zone_Shop>();
            List<Zone> zones = map.zoneManager.AllZones;
            for (int i = 0; i < zones.Count; i++)
            {
                Zone_Shop shop = zones[i] as Zone_Shop;
                if (shop == null || !shop.IsOpenNow())
                    continue;
                openShops.Add(shop);
            }

            if (openShops.Count <= 0)
                return result;

            Dictionary<int, int> customerCounts = CountActiveCustomersByShop();
            for (int i = 0; i < openShops.Count; i++)
            {
                Zone_Shop shop = openShops[i];
                customerCounts.TryGetValue(shop.ID, out int currentCustomers);
                CustomerArrivalShopContext context = BuildShopContext(shop, analytics, currentCustomers);
                if (context != null)
                    result.Add(context);
            }

            return result;
        }

        /// <summary>
        /// 周期性尝试为地图上的自动售货机刷新顾客。
        /// </summary>
        private bool TrySpawnOneCustomerForVendingMachines(int checkInterval)
        {
            List<Building_SimContainer> machines = VendingMachineUtility.GetVendingMachineSnapshot(map)
                .Where(VendingMachineUtility.IsUsableVendingMachine)
                .ToList();
            if (machines.NullOrEmpty()) return false;

            Dictionary<int, int> customerCounts = CountActiveCustomersByVendingMachine();
            machines.RemoveAll(machine =>
            {
                ThingComp_VendingMachine comp = machine.GetComp<ThingComp_VendingMachine>();
                customerCounts.TryGetValue(machine.thingIDNumber, out int currentCustomers);
                return comp == null || currentCustomers >= comp.MaxSimultaneousCustomers;
            });
            if (machines.NullOrEmpty()) return false;

            List<RuntimeCustomerKind> kinds = CustomerCatalog.Kinds
                .Where(k => k != null && !k.pawnKindDefs.NullOrEmpty() && k.CanAppearNow(map))
                .ToList();
            kinds = ApplyForcedCustomerKindFilter(kinds);
            if (kinds.NullOrEmpty()) return false;

            float hour = GenLocalDate.HourFloat(map);
            foreach (Building_SimContainer machine in machines.InRandomOrder())
            {
                ThingComp_VendingMachine comp = machine.GetComp<ThingComp_VendingMachine>();
                List<RuntimeCustomerKind> candidates = kinds
                    .Where(k => VendingMachineUtility.MatchesCustomerKind(machine, k))
                    .ToList();
                if (candidates.NullOrEmpty()) continue;

                RuntimeCustomerKind selected = candidates.RandomElementByWeight(k => k.EvaluateArrivalWeight(hour));
                if (selected == null) continue;

                float mtbDays = comp.BaseMtbDays / Mathf.Max(selected.EvaluateArrivalWeight(hour), 0.05f);
                if (!Rand.MTBEventOccurs(mtbDays, 60000f, checkInterval)) continue;

                if (TrySpawnVendingMachineCustomer(machine, selected, false, true, out _, out _, out _))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 根据设置中的 Debug 强制顾客组过滤候选列表。
        /// </summary>
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

        private CustomerArrivalShopContext BuildShopContext(Zone_Shop shop, GameComponent_ShopAnalyticsManager analytics, int currentCustomers)
        {
            if (shop == null) return null;

            analytics?.GetOrEvaluateShopMetrics(shop);
            return new CustomerArrivalShopContext
            {
                Shop = shop,
                CurrentCustomers = currentCustomers,
                Capacity = analytics != null ? analytics.GetDynamicCustomerCapacity(shop) : CalculateShopCustomerCapacity(shop),
                DemandFactor = ApplyReviewDemandInfluence(shop, analytics != null ? analytics.GetSpawnDemandFactor(shop, map) : 1f),
                HasCheckoutService = ShopStaffUtility.HasMannedCashRegister(shop)
            };
        }

        /// <summary>
        /// 根据店铺评价调整刷客需求倍率，负责让玩家可选地把口碑反馈接入真实来客概率。
        /// </summary>
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

        /// <summary>
        /// 判断 Debug 强制刷新是否允许指定顾客进入商店，只跳过时间、天气和随机概率，不跳过商店匹配。
        /// </summary>
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

        private int CalculateShopCustomerCapacity(Zone_Shop shop)
        {
            int storageCount = ShopDataUtility.GetStorageSnapshotInZone(shop).Count;
            int registerCount = ShopDataUtility.GetCashRegisterSnapshotInZone(shop).Count;
            int estimated = registerCount * 8 + storageCount * 4;
            return Mathf.Max(6, estimated);
        }

        //按商店统计当前顾客，职责是一次遍历顾客 Lord 供全部商店上下文复用。
        private Dictionary<int, int> CountActiveCustomersByShop()
        {
            Dictionary<int, int> counts = new Dictionary<int, int>();
            List<Lord> lords = map?.lordManager?.lords;
            if (lords == null)
                return counts;

            for (int i = 0; i < lords.Count; i++)
            {
                Lord lord = lords[i];
                LordJob_CustomerVisit visit = lord?.LordJob as LordJob_CustomerVisit;
                if (visit == null || lord.ownedPawns == null)
                    continue;

                for (int p = 0; p < lord.ownedPawns.Count; p++)
                {
                    Pawn pawn = lord.ownedPawns[p];
                    if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned)
                        continue;
                    Zone_Shop shop = visit.GetCurrentShop(pawn);
                    if (shop == null)
                        continue;
                    counts.TryGetValue(shop.ID, out int current);
                    counts[shop.ID] = current + 1;
                }
            }

            return counts;
        }

        //按自动售货机统计当前顾客，职责是避免每台机器分别扫描全部 Lord。
        private Dictionary<int, int> CountActiveCustomersByVendingMachine()
        {
            Dictionary<int, int> counts = new Dictionary<int, int>();
            List<Lord> lords = map?.lordManager?.lords;
            if (lords == null)
                return counts;

            for (int i = 0; i < lords.Count; i++)
            {
                Lord lord = lords[i];
                LordJob_VendingMachineVisit visit = lord?.LordJob as LordJob_VendingMachineVisit;
                if (visit == null || lord.ownedPawns == null)
                    continue;

                int active = 0;
                for (int p = 0; p < lord.ownedPawns.Count; p++)
                {
                    Pawn pawn = lord.ownedPawns[p];
                    if (pawn != null && !pawn.Destroyed && !pawn.Dead && pawn.Spawned)
                        active++;
                }

                if (active <= 0)
                    continue;
                counts.TryGetValue(visit.vendingMachineThingId, out int current);
                counts[visit.vendingMachineThingId] = current + active;
            }

            return counts;
        }

        /// <summary>
        /// 查找商店内可作为顾客入店目标的站立格，负责避免不可达商店生成后直接离图。
        /// </summary>
        private bool TryFindReachableShopEntryCell(Zone_Shop shop, out IntVec3 targetCell)
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

        /// <summary>
        /// 为指定商店生成一位顾客并绑定顾客 Lord，失败时返回具体原因。
        /// </summary>
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

            if (!TryFindCustomerEdgeSpawnCell(shopTargetCell, PathEndMode.OnCell, out IntVec3 spawnSpot))
                return FailSpawnAttempt(attemptKey, "地图边缘没有可达入口", respectFailureBackoff, out failReason);

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
            // 顾客 Pawn 和 Lord 都使用商店专用中立派系，避免敌对来源派系残留为红名或触发战斗 AI。
            LordMaker.MakeNewLord(customerFaction, lordJob, map, new List<Pawn> { pawn });
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

        /// <summary>
        /// 为指定自动售货机生成一位顾客并绑定独立的自动售货机访问 Lord。
        /// </summary>
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

            if (!TryFindCustomerEdgeSpawnCell(machine.Position, PathEndMode.Touch, out IntVec3 spawnSpot))
                return FailSpawnAttempt(attemptKey, "地图边缘没有可达入口", respectFailureBackoff, out failReason);

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
            LordMaker.MakeNewLord(customerFaction, lordJob, map, new List<Pawn> { pawn });
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

        // 查找顾客可用的地图边缘生成点，负责在顾客入图前筛掉无法走到目标的位置。
        private bool TryFindCustomerEdgeSpawnCell(LocalTargetInfo target, PathEndMode pathEndMode, out IntVec3 spawnSpot)
        {
            spawnSpot = IntVec3.Invalid;
            if (map == null || !target.IsValid)
                return false;

            HashSet<IntVec3> checkedCells = new HashSet<IntVec3>();
            for (int i = 0; i < MaxEdgeSpawnPathChecks; i++)
            {
                if (!CellFinder.TryFindRandomEdgeCellWith(
                    c => !checkedCells.Contains(c) && IsUsableCustomerEdgeCell(c),
                    map,
                    CellFinder.EdgeRoadChance_Neutral,
                    out IntVec3 candidate))
                {
                    break;
                }

                checkedCells.Add(candidate);
                if (!CanReachFromCellWithoutForbiddenPlayerDoor(candidate, target, pathEndMode))
                    continue;

                spawnSpot = candidate;
                return true;
            }

            return false;
        }

        //判断地图边缘格是否值得执行完整寻路，职责是用常数时间条件过滤雾区和不可站立格。
        private bool IsUsableCustomerEdgeCell(IntVec3 cell)
        {
            return cell.IsValid
                && cell.InBounds(map)
                && cell.Standable(map)
                && !cell.Fogged(map);
        }

        // 从指定起点检查目标可达性，负责在顾客未入图前避免使用绑定 Pawn 的寻路参数。
        private bool CanReachFromCellWithoutForbiddenPlayerDoor(IntVec3 start, LocalTargetInfo target, PathEndMode pathEndMode)
        {
            if (map == null || !start.IsValid || !target.IsValid)
                return false;

            using (PawnPath path = map.pathFinder.FindPathNow(
                start,
                target,
                TraverseParms.For(TraverseMode.PassDoors, Danger.Deadly),
                null,
                pathEndMode))
            {
                if (path == null || !path.Found)
                    return false;

                List<IntVec3> nodes = path.NodesReversed;
                for (int i = 0; i < nodes.Count; i++)
                {
                    Building_Door door = nodes[i].GetDoor(map);
                    if (door != null && door.Faction == Faction.OfPlayer && door.IsForbidden(Faction.OfPlayer))
                        return false;
                }
            }

            return true;
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

        private static int GetCheckIntervalTicks()
        {
            int value = SimManagementLibMod.Settings?.customerArrivalCheckIntervalTicks ?? DefaultCheckInterval;
            return Mathf.Clamp(value, 120, 5000);
        }

    }
}
