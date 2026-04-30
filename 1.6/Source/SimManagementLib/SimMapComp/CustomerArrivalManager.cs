using RimWorld;
using SimManagementLib.GameComp;
using SimManagementLib.Pojo;
using SimManagementLib.SimAI;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace SimManagementLib.SimMapComp
{
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
            if (contexts.NullOrEmpty()) return;

            foreach (CustomerArrivalShopContext context in contexts)
            {
                TrySpawnOneCustomerForShop(context);
            }
        }

        public bool ForceSpawnOneWave(bool ignoreConditions, out string resultMessage)
        {
            List<CustomerArrivalShopContext> contexts = CollectActiveShopContexts();
            if (contexts.NullOrEmpty())
            {
                resultMessage = "强制刷新失败：当前地图没有可营业的商店区域。";
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
                    ? "强制刷新失败：没有可用的顾客类型定义。"
                    : "强制刷新失败：当前时段或天气下没有可出现的顾客类型。";
                return false;
            }

            float hour = GenLocalDate.HourFloat(map);
            List<RuntimeCustomerKind> orderedKinds = kinds
                .OrderByDescending(k => k.EvaluateArrivalWeight(hour))
                .ToList();

            foreach (CustomerArrivalShopContext context in contexts.OrderBy(_ => Rand.Value))
            {
                foreach (RuntimeCustomerKind kind in orderedKinds)
                {
                    if (!ignoreConditions && !CanSpawnWave(context, kind)) continue;
                    if (TrySpawnCustomerWave(context.Shop, kind, false, out int spawnedCount, out _))
                    {
                        resultMessage = $"已强制刷新顾客：商店[{context.Shop.label}]，人数 {spawnedCount}。";
                        return true;
                    }
                }
            }

            resultMessage = "强制刷新失败：找不到可用的出生点或顾客派系。";
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

            foreach (Zone_Shop shop in map.zoneManager.AllZones.OfType<Zone_Shop>().Where(z => z.IsValidShop()))
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

                if (visit.targetShopZoneId >= 0)
                {
                    if (visit.targetShopZoneId == shop.ID)
                        count++;
                }
                else if (shop.Cells.Contains(visit.targetShopCell))
                {
                    count++;
                }
            }

            return count;
        }

        private bool TrySpawnCustomerWave(Zone_Shop shop, RuntimeCustomerKind kind, bool showArrivalMessage, out int spawnedCount, out string failReason)
        {
            spawnedCount = 0;
            failReason = string.Empty;

            Faction faction = FindSafeCustomerFaction();
            if (faction == null)
            {
                failReason = "no safe faction";
                return false;
            }

            PawnKindDef selectedKind = kind.pawnKindDefs.RandomElement();
            if (selectedKind == null)
            {
                failReason = "no pawn kind";
                return false;
            }

            PawnGenerationRequest request = new PawnGenerationRequest(
                selectedKind,
                faction,
                PawnGenerationContext.NonPlayer,
                tile: -1,
                forceGenerateNewPawn: false);
            Pawn pawn = PawnGenerator.GeneratePawn(request);
            if (pawn == null)
            {
                failReason = "no pawn generated";
                return false;
            }

            if (!CellFinder.TryFindRandomEdgeCellWith(
                c => map.reachability.CanReachColony(c) && !c.Fogged(map),
                map,
                CellFinder.EdgeRoadChance_Neutral,
                out IntVec3 spawnSpot))
            {
                Find.WorldPawns.PassToWorld(pawn);
                failReason = "no edge spawn cell";
                return false;
            }

            GenSpawn.Spawn(pawn, spawnSpot, map);

            IntVec3 shopTargetCell = shop.Cells.FirstOrDefault();
            int fallbackBudget = kind.budgetRange.RandomInRange;
            LordJob_CustomerVisit lordJob = new LordJob_CustomerVisit(kind.sourceDef, shop.ID, shopTargetCell, fallbackBudget);
            lordJob.customerKindId = kind.kindId;
            CustomerRuntimeSettings settings = kind.BuildRuntimeSettings(map);
            lordJob.SetPawnSettings(pawn.thingIDNumber, settings);
            LordMaker.MakeNewLord(faction, lordJob, map, new List<Pawn> { pawn });
            spawnedCount = 1;
            CustomerExpressionUtility.TryShowExpression(pawn, CustomerExpressionEvents.Arrival);

            if (showArrivalMessage)
            {
                WeatherDef weather = map.weatherManager?.curWeather;
                string weatherLabel = weather != null ? weather.LabelCap.RawText : "未知天气";
                if (SimManagementLibMod.Settings?.showCustomerArrivalMessage ?? true)
                {
                    Messages.Message(
                        $"有一位顾客正在前往商店。当前天气：{weatherLabel}",
                        pawn,
                        MessageTypeDefOf.NeutralEvent,
                        historical: true);
                }
            }

            return true;
        }

        private Faction FindSafeCustomerFaction()
        {
            HashSet<Faction> activeCustomerFactions = new HashSet<Faction>();
            for (int i = 0; i < map.lordManager.lords.Count; i++)
            {
                Lord lord = map.lordManager.lords[i];
                if (lord?.LordJob is LordJob_CustomerVisit && lord.faction != null)
                {
                    activeCustomerFactions.Add(lord.faction);
                }
            }

            if (activeCustomerFactions.Count > 0)
            {
                List<Faction> activeValid = activeCustomerFactions
                    .Where(IsValidCustomerFaction)
                    .ToList();
                if (!activeValid.NullOrEmpty())
                    return activeValid.RandomElement();
            }

            List<Faction> candidates = Find.FactionManager.AllFactionsListForReading
                .Where(IsValidCustomerFaction)
                .Where(f => IsCompatibleWithActiveCustomerFactions(f, activeCustomerFactions))
                .ToList();
            if (candidates.NullOrEmpty())
            {
                candidates = Find.FactionManager.AllFactionsListForReading
                    .Where(IsValidCustomerFaction)
                    .ToList();
            }
            if (candidates.NullOrEmpty()) return null;

            return candidates.RandomElement();
        }

        private static bool IsValidCustomerFaction(Faction faction)
        {
            if (faction == null || faction == Faction.OfPlayer) return false;
            if (faction.defeated) return false;
            if (faction.HostileTo(Faction.OfPlayer)) return false;
            return true;
        }

        private static int GetCheckIntervalTicks()
        {
            int value = SimManagementLibMod.Settings?.customerArrivalCheckIntervalTicks ?? DefaultCheckInterval;
            return Mathf.Clamp(value, 120, 5000);
        }

        private static bool IsCompatibleWithActiveCustomerFactions(Faction candidate, HashSet<Faction> activeFactions)
        {
            if (candidate == null || activeFactions == null || activeFactions.Count == 0) return true;

            foreach (Faction active in activeFactions)
            {
                if (active == null) continue;
                if (candidate.HostileTo(active) || active.HostileTo(candidate))
                    return false;
            }

            return true;
        }
    }
}
