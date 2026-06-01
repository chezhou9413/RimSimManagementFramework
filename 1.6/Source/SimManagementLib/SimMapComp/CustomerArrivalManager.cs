using RimWorld;
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

        public CustomerArrivalManager(Map map) : base(map)
        {
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            int checkInterval = GetCheckIntervalTicks();
            if (checkInterval <= 0) checkInterval = DefaultCheckInterval;
            if (Find.TickManager.TicksGame % checkInterval != 0) return;

            List<CustomerArrivalShopContext> contexts = CollectActiveShopContexts();
            if (!contexts.NullOrEmpty())
            {
                foreach (CustomerArrivalShopContext context in contexts)
                {
                    TrySpawnOneCustomerForShop(context);
                }
            }

            TrySpawnOneCustomerForVendingMachines(checkInterval);
        }

        public bool ForceSpawnOneWave(bool ignoreConditions, out string resultMessage)
        {
            List<CustomerArrivalShopContext> contexts = CollectActiveShopContexts();
            List<Building_SimContainer> vendingMachines = VendingMachineUtility.GetAllVendingMachines(map)
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

                    if (TrySpawnCustomerWave(context.Shop, kind, false, out int spawnedCount, out string failReason))
                    {
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
                    if (TrySpawnVendingMachineCustomer(machine, kind, true, out int spawnedCount, out string failReason))
                    {
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

        private void TrySpawnOneCustomerForShop(CustomerArrivalShopContext context)
        {
            if (context?.Shop == null || context.IsAtCapacity) return;

            List<RuntimeCustomerKind> candidates = CustomerCatalog.Kinds
                .Where(k => CanSpawnWave(context, k))
                .ToList();
            candidates = ApplyForcedCustomerKindFilter(candidates);
            if (candidates.NullOrEmpty()) return;

            float hour = GenLocalDate.HourFloat(map);
            RuntimeCustomerKind selected = candidates.RandomElementByWeight(k => k.EvaluateArrivalWeight(hour));
            if (selected == null) return;

            TrySpawnCustomerWave(context.Shop, selected, true, out _, out _);
        }

        private List<CustomerArrivalShopContext> CollectActiveShopContexts()
        {
            List<CustomerArrivalShopContext> result = new List<CustomerArrivalShopContext>();
            GameComponent_ShopAnalyticsManager analytics = Current.Game?.GetComponent<GameComponent_ShopAnalyticsManager>();

            foreach (Zone_Shop shop in map.zoneManager.AllZones.OfType<Zone_Shop>().Where(z => z.IsOpenNow()))
            {
                CustomerArrivalShopContext context = BuildShopContext(shop, analytics);
                if (context != null)
                {
                    result.Add(context);
                }
            }

            return result;
        }

        /// <summary>
        /// 周期性尝试为地图上的自动售货机刷新顾客。
        /// </summary>
        private void TrySpawnOneCustomerForVendingMachines(int checkInterval)
        {
            List<Building_SimContainer> machines = VendingMachineUtility.GetAllVendingMachines(map)
                .Where(VendingMachineUtility.IsUsableVendingMachine)
                .Where(machine =>
                {
                    ThingComp_VendingMachine comp = machine.GetComp<ThingComp_VendingMachine>();
                    return comp != null && VendingMachineUtility.CountActiveCustomers(map, machine) < comp.MaxSimultaneousCustomers;
                })
                .ToList();
            if (machines.NullOrEmpty()) return;

            List<RuntimeCustomerKind> kinds = CustomerCatalog.Kinds
                .Where(k => k != null && !k.pawnKindDefs.NullOrEmpty() && k.CanAppearNow(map))
                .ToList();
            kinds = ApplyForcedCustomerKindFilter(kinds);
            if (kinds.NullOrEmpty()) return;

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

                TrySpawnVendingMachineCustomer(machine, selected, false, out _, out _);
            }
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

        private CustomerArrivalShopContext BuildShopContext(Zone_Shop shop, GameComponent_ShopAnalyticsManager analytics)
        {
            if (shop == null) return null;

            analytics?.GetOrEvaluateShopMetrics(shop);
            return new CustomerArrivalShopContext
            {
                Shop = shop,
                CurrentCustomers = CountCustomersForShop(shop),
                Capacity = analytics != null ? analytics.GetDynamicCustomerCapacity(shop) : CalculateShopCustomerCapacity(shop),
                DemandFactor = analytics != null ? analytics.GetSpawnDemandFactor(shop, map) : 1f
            };
        }

        private bool CanSpawnWave(CustomerArrivalShopContext context, RuntimeCustomerKind kind)
        {
            if (context == null || !context.CanSpawn(kind)) return false;
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
            if (context == null || !context.CanSpawn(kind)) return false;
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
            int storageCount = 0;
            int registerCount = 0;

            foreach (IntVec3 cell in shop.Cells)
            {
                List<Thing> things = map.thingGrid.ThingsListAt(cell);
                for (int i = 0; i < things.Count; i++)
                {
                    if (things[i] is SimThingClass.Building_SimContainer) storageCount++;
                    else if (things[i] is SimThingClass.Building_CashRegister) registerCount++;
                }
            }

            int estimated = registerCount * 8 + storageCount * 4;
            return Mathf.Max(6, estimated);
        }

        private int CountCustomersForShop(Zone_Shop shop)
        {
            int count = 0;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn == null || pawn.Destroyed || pawn.Dead) continue;

                Lord lord = map.lordManager.LordOf(pawn);
                LordJob_CustomerVisit visit = lord?.LordJob as LordJob_CustomerVisit;
                if (visit == null) continue;

                if (visit.GetCurrentShop(pawn) == shop)
                    count++;
            }

            return count;
        }

        /// <summary>
        /// 查找商店内可作为顾客入店目标的站立格，负责避免不可达商店生成后直接离图。
        /// </summary>
        private bool TryFindReachableShopEntryCell(Zone_Shop shop, out IntVec3 targetCell)
        {
            targetCell = IntVec3.Invalid;
            if (shop == null) return false;

            List<IntVec3> storageCells = ShopDataUtility.GetStoragesInZone(shop)
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
        private bool TrySpawnCustomerWave(Zone_Shop shop, RuntimeCustomerKind kind, bool showArrivalMessage, out int spawnedCount, out string failReason)
        {
            spawnedCount = 0;
            failReason = string.Empty;

            PawnKindDef selectedKind = SelectPawnKindWithCompatibleFaction(kind, out Faction faction);
            if (selectedKind == null)
            {
                failReason = "no pawn kind";
                return false;
            }

            PawnGenerationRequest request = CreateCustomerPawnGenerationRequest(selectedKind, faction);
            Pawn pawn = PawnGenerator.GeneratePawn(request);
            if (pawn == null)
            {
                failReason = "no pawn generated";
                return false;
            }

            if (CustomerNeutralFactionUtility.IsProtectedFactionLeader(pawn))
            {
                Find.WorldPawns.PassToWorld(pawn);
                failReason = "generated faction leader";
                return false;
            }

            if (!CustomerNeutralFactionUtility.ConvertPawnToCustomerFaction(pawn, out Faction customerFaction))
            {
                Find.WorldPawns.PassToWorld(pawn);
                failReason = "no neutral customer faction";
                return false;
            }

            if (!TryFindReachableShopEntryCell(shop, out IntVec3 shopTargetCell))
            {
                Find.WorldPawns.PassToWorld(pawn);
                failReason = "no reachable shop target cell";
                return false;
            }

            if (!CellFinder.TryFindRandomEdgeCellWith(
                c => !c.Fogged(map) && map.reachability.CanReach(c, shopTargetCell, PathEndMode.OnCell, TraverseParms.For(TraverseMode.PassDoors)),
                map,
                CellFinder.EdgeRoadChance_Neutral,
                out IntVec3 spawnSpot))
            {
                Find.WorldPawns.PassToWorld(pawn);
                failReason = "no edge spawn cell";
                return false;
            }

            GenSpawn.Spawn(pawn, spawnSpot, map);

            int fallbackBudget = kind.budgetRange.RandomInRange;
            LordJob_CustomerVisit lordJob = new LordJob_CustomerVisit(kind.sourceDef, shop.ID, shopTargetCell, fallbackBudget);
            lordJob.customerKindId = kind.kindId;
            CustomerRuntimeSettings settings = kind.BuildRuntimeSettings(map);
            lordJob.SetPawnSettings(pawn.thingIDNumber, settings);
            // 顾客 Pawn 和 Lord 都使用商店专用中立派系，避免敌对来源派系残留为红名或触发战斗 AI。
            LordMaker.MakeNewLord(customerFaction, lordJob, map, new List<Pawn> { pawn });
            spawnedCount = 1;
            CustomerExpressionUtility.TryShowExpression(pawn, CustomerExpressionEvents.Arrival);

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
        private bool TrySpawnVendingMachineCustomer(Building_SimContainer machine, RuntimeCustomerKind kind, bool showArrivalMessage, out int spawnedCount, out string failReason)
        {
            spawnedCount = 0;
            failReason = string.Empty;
            if (machine == null || kind == null || !VendingMachineUtility.IsUsableVendingMachine(machine))
            {
                failReason = "invalid vending machine";
                return false;
            }

            PawnKindDef selectedKind = SelectPawnKindWithCompatibleFaction(kind, out Faction faction);
            if (selectedKind == null)
            {
                failReason = "no pawn kind";
                return false;
            }

            Pawn pawn = PawnGenerator.GeneratePawn(CreateCustomerPawnGenerationRequest(selectedKind, faction));
            if (pawn == null)
            {
                failReason = "no pawn generated";
                return false;
            }

            if (CustomerNeutralFactionUtility.IsProtectedFactionLeader(pawn))
            {
                Find.WorldPawns.PassToWorld(pawn);
                failReason = "generated faction leader";
                return false;
            }

            if (!CustomerNeutralFactionUtility.ConvertPawnToCustomerFaction(pawn, out Faction customerFaction))
            {
                Find.WorldPawns.PassToWorld(pawn);
                failReason = "no neutral customer faction";
                return false;
            }

            if (!CellFinder.TryFindRandomEdgeCellWith(
                c => map.reachability.CanReach(c, machine.Position, PathEndMode.Touch, TraverseParms.For(TraverseMode.PassDoors)) && !c.Fogged(map),
                map,
                CellFinder.EdgeRoadChance_Neutral,
                out IntVec3 spawnSpot))
            {
                Find.WorldPawns.PassToWorld(pawn);
                failReason = "no edge spawn cell";
                return false;
            }

            GenSpawn.Spawn(pawn, spawnSpot, map);
            int fallbackBudget = kind.budgetRange.RandomInRange;
            LordJob_VendingMachineVisit lordJob = new LordJob_VendingMachineVisit(kind.sourceDef, machine, fallbackBudget);
            lordJob.customerKindId = kind.kindId;
            lordJob.SetPawnSettings(pawn.thingIDNumber, kind.BuildRuntimeSettings(map));
            LordMaker.MakeNewLord(customerFaction, lordJob, map, new List<Pawn> { pawn });
            spawnedCount = 1;
            CustomerExpressionUtility.TryShowExpression(pawn, CustomerExpressionEvents.Arrival);

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

        /// <summary>
        /// 使用真实来源派系创建顾客生成请求，负责避免从世界 Pawn 池复用派系领袖。
        /// </summary>
        private static PawnGenerationRequest CreateCustomerPawnGenerationRequest(PawnKindDef selectedKind, Faction sourceFaction)
        {
            return new PawnGenerationRequest(
                selectedKind,
                sourceFaction,
                PawnGenerationContext.NonPlayer,
                tile: -1,
                forceGenerateNewPawn: true);
        }

        /// <summary>
        /// 从顾客候选 PawnKind 中选择一个能匹配当前世界派系的 PawnKind，并输出兼容派系。
        /// </summary>
        private PawnKindDef SelectPawnKindWithCompatibleFaction(RuntimeCustomerKind kind, out Faction faction)
        {
            faction = null;
            if (kind == null || kind.pawnKindDefs.NullOrEmpty()) return null;

            List<PawnKindDef> shuffledKinds = kind.pawnKindDefs
                .Where(k => k?.race?.race != null)
                .InRandomOrder()
                .ToList();

            for (int i = 0; i < shuffledKinds.Count; i++)
            {
                PawnKindDef pawnKind = shuffledKinds[i];
                faction = FindCustomerFactionForPawnKind(pawnKind);
                if (faction != null)
                    return pawnKind;
            }

            return null;
        }

        /// <summary>
        /// 从当前世界派系列表中选择可作为指定 PawnKind 顾客来源的派系，不按敌对关系排除候选。
        /// </summary>
        private Faction FindCustomerFactionForPawnKind(PawnKindDef pawnKind)
        {
            if (pawnKind?.race?.race == null) return null;

            List<Faction> candidates = new List<Faction>();
            List<Faction> allFactions = Find.FactionManager.AllFactionsListForReading;
            for (int i = 0; i < allFactions.Count; i++)
            {
                Faction faction = allFactions[i];
                if (IsValidCustomerFaction(faction, pawnKind))
                    candidates.Add(faction);
            }

            if (candidates.NullOrEmpty()) return null;
            return candidates.RandomElement();
        }

        /// <summary>
        /// 判断派系是否能作为指定 PawnKind 的顾客来源，保留敌对派系但排除物种类型不兼容的派系。
        /// </summary>
        private static bool IsValidCustomerFaction(Faction faction, PawnKindDef pawnKind)
        {
            if (faction == null || faction == Faction.OfPlayer) return false;
            if (faction.defeated) return false;
            if (faction.def == null || pawnKind?.race?.race == null) return false;

            bool pawnHumanlike = pawnKind.race.race.Humanlike;
            if (pawnHumanlike && !faction.def.humanlikeFaction) return false;
            if (!pawnHumanlike && faction.def.humanlikeFaction) return false;

            return true;
        }

        private static int GetCheckIntervalTicks()
        {
            int value = SimManagementLibMod.Settings?.customerArrivalCheckIntervalTicks ?? DefaultCheckInterval;
            return Mathf.Clamp(value, 120, 5000);
        }

    }
}
