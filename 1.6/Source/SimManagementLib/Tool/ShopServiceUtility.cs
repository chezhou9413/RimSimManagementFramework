using SimManagementLib.Pojo;
using SimManagementLib.SimService;
using SimManagementLib.SimThingComp;
using SimManagementLib.SimZone;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;
using SimManagementLib.SimAI;
using Verse.AI.Group;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 提供服务建筑扫描、价格计算、并发检查、服务订单创建和服务 Job 生成的公共入口。
    /// </summary>
    public static class ShopServiceUtility
    {
        public const int CustomerServiceProviderReservationSlots = 24;

        /// <summary>
        /// 获取商店区域内所有挂载服务组件的建筑。
        /// </summary>
        public static HashSet<Thing> GetServiceProvidersInZone(Zone zone)
        {
            HashSet<Thing> providers = new HashSet<Thing>();
            if (zone == null || zone.Map == null) return providers;

            foreach (IntVec3 cell in zone.Cells)
            {
                List<Thing> things = zone.Map.thingGrid.ThingsListAt(cell);
                for (int i = 0; i < things.Count; i++)
                {
                    Thing thing = things[i];
                    if (GetProviderComp(thing) != null)
                        providers.Add(thing);
                }
            }

            return providers;
        }

        /// <summary>
        /// 判断指定建筑是否至少提供一个已启用并可解析的服务。
        /// </summary>
        public static bool HasEnabledService(Thing provider)
        {
            ThingComp_ServiceProvider comp = GetProviderComp(provider);
            return comp != null && comp.enabled && comp.EnabledSlots.Any();
        }

        /// <summary>
        /// 判断商店区域内是否存在可用服务提供建筑。
        /// </summary>
        public static bool HasUsableServiceProvider(Zone_Shop shop, Pawn customer = null, IReadOnlyCollection<string> targetServiceCategoryIds = null)
        {
            if (shop == null) return false;

            foreach (Thing provider in GetServiceProvidersInZone(shop))
            {
                ThingComp_ServiceProvider comp = GetProviderComp(provider);
                if (comp == null || !comp.enabled) continue;

                foreach (ServiceSlotData slot in comp.EnabledSlots)
                {
                    ShopServiceDef serviceDef = slot.ServiceDef;
                    if (serviceDef == null) continue;
                    if (targetServiceCategoryIds != null && targetServiceCategoryIds.Count > 0 && !targetServiceCategoryIds.Contains(serviceDef.serviceCategoryId)) continue;
                    if (!CanAcceptMoreUsers(provider, serviceDef)) continue;
                    if (customer == null) return true;
                    if (serviceDef.Worker.CanUse(customer, provider, shop, out _)) return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 获取服务单价，优先读取建筑槽位覆盖价，再读取服务基础价，最低为 1。
        /// </summary>
        public static float GetServicePrice(Thing provider, ShopServiceDef serviceDef)
        {
            if (serviceDef == null) return 1f;

            ThingComp_ServiceProvider comp = GetProviderComp(provider);
            ServiceSlotData slot = comp?.FindSlot(serviceDef);
            if (slot != null && slot.priceOverride > 0f)
                return Mathf.Max(1f, slot.priceOverride);

            if (serviceDef.basePrice > 0f)
                return Mathf.Max(1f, serviceDef.basePrice);

            return 1f;
        }

        /// <summary>
        /// 判断指定服务建筑是否还有并发容量。
        /// </summary>
        public static bool CanAcceptMoreUsers(Thing provider, ShopServiceDef serviceDef)
        {
            if (provider == null || serviceDef == null) return false;
            ThingComp_ServiceProvider comp = GetProviderComp(provider);
            if (comp == null || !comp.enabled) return false;

            ServiceSlotData slot = comp.FindSlot(serviceDef);
            if (slot == null || !slot.enabled) return false;

            int limit = Mathf.Max(1, slot.maxSimultaneousUsers);
            return CountActiveUsers(provider.Map, provider.thingIDNumber, serviceDef.defName) < limit;
        }

        /// <summary>
        /// 统计地图中正在使用指定服务建筑和服务 Def 的顾客数量。
        /// </summary>
        public static int CountActiveUsers(Map map, int providerThingId, string serviceDefName)
        {
            if (map == null || string.IsNullOrEmpty(serviceDefName)) return 0;
            int count = 0;
            List<Lord> lords = map.lordManager?.lords;
            if (lords == null) return 0;

            for (int i = 0; i < lords.Count; i++)
            {
                if (lords[i]?.LordJob is SimAI.LordJob_CustomerVisit visit)
                    count += visit.CountActiveServiceOrders(providerThingId, serviceDefName);
            }

            return count;
        }

        /// <summary>
        /// 查找顾客当前商店里最适合的一项可消费服务。
        /// </summary>
        public static bool TryFindServiceForCustomer(Pawn pawn, Zone_Shop shop, float remainingBudget, out Thing provider, out ShopServiceDef serviceDef, out float price)
        {
            return TryFindServiceForCustomer(pawn, shop, remainingBudget, null, out provider, out serviceDef, out price);
        }

        /// <summary>
        /// 查找顾客当前商店里最适合的一项可消费服务，并允许调用方按服务分类过滤。
        /// </summary>
        public static bool TryFindServiceForCustomer(Pawn pawn, Zone_Shop shop, float remainingBudget, IReadOnlyCollection<string> targetServiceCategoryIds, out Thing provider, out ShopServiceDef serviceDef, out float price)
        {
            provider = null;
            serviceDef = null;
            price = 0f;
            if (pawn == null || shop == null || remainingBudget <= 0f) return false;

            List<ServiceCandidate> candidates = new List<ServiceCandidate>();
            foreach (Thing candidateProvider in GetServiceProvidersInZone(shop))
            {
                ThingComp_ServiceProvider comp = GetProviderComp(candidateProvider);
                if (comp == null || !comp.enabled) continue;
                if (!pawn.CanReach(candidateProvider, PathEndMode.Touch, Danger.Deadly)) continue;
                if (!CanCustomerReserveServiceProvider(pawn, candidateProvider)) continue;

                foreach (ServiceSlotData slot in comp.EnabledSlots)
                {
                    ShopServiceDef def = slot.ServiceDef;
                    if (def == null) continue;
                    if (targetServiceCategoryIds != null && targetServiceCategoryIds.Count > 0 && !targetServiceCategoryIds.Contains(def.serviceCategoryId)) continue;
                    float unitPrice = def.Worker.GetPrice(pawn, candidateProvider, shop);
                    if (unitPrice > remainingBudget) continue;
                    if (!def.Worker.CanUse(pawn, candidateProvider, shop, out _)) continue;
                    if (!CanAcceptMoreUsers(candidateProvider, def)) continue;

                    candidates.Add(new ServiceCandidate(candidateProvider, def, unitPrice));
                }
            }

            if (candidates.NullOrEmpty()) return false;

            ServiceCandidate chosen = candidates.RandomElementByWeight(c => Mathf.Max(1f, c.Price));
            provider = chosen.Provider;
            serviceDef = chosen.ServiceDef;
            price = chosen.Price;
            return true;
        }

        /// <summary>
        /// 创建一条服务订单并填充基础价格、建筑和计费字段。
        /// </summary>
        public static CustomerServiceOrder CreateOrder(int orderId, Thing provider, ShopServiceDef serviceDef, float price)
        {
            return new CustomerServiceOrder
            {
                orderId = orderId,
                serviceDefName = serviceDef?.defName ?? "",
                providerThingId = provider?.thingIDNumber ?? -1,
                providerLabel = provider?.LabelCap ?? SimTranslation.T("RSMF.Service.ProviderFallback"),
                count = 1,
                unitPrice = Mathf.Max(1f, price),
                totalPrice = Mathf.Max(1f, price),
                billingMode = serviceDef?.billingMode ?? ServiceBillingMode.PayBeforeUse,
                state = ServiceOrderState.Draft,
                reservedTick = Find.TickManager?.TicksGame ?? 0
            };
        }

        /// <summary>
        /// 按订单中的建筑 ID 查找当前地图上的服务建筑。
        /// </summary>
        public static Thing FindProviderByOrder(Map map, CustomerServiceOrder order)
        {
            if (map == null || order == null || order.providerThingId < 0) return null;
            IReadOnlyList<Thing> things = map.listerThings.AllThings;
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (thing != null && thing.thingIDNumber == order.providerThingId)
                    return thing;
            }

            return null;
        }

        /// <summary>
        /// 按服务订单创建实际使用 Job。
        /// </summary>
        public static Job MakeServiceUseJob(Pawn pawn, CustomerServiceOrder order)
        {
            if (pawn == null || order == null) return null;
            ShopServiceDef serviceDef = DefDatabase<ShopServiceDef>.GetNamedSilentFail(order.serviceDefName);
            Thing provider = FindProviderByOrder(pawn.Map, order);
            if (serviceDef == null || provider == null) return null;
            return serviceDef.Worker.MakeUseJob(pawn, provider, order);
        }

        /// <summary>
        /// 判断顾客是否能用共享服务预约访问建筑，负责避开维修、拆除等独占预约中的服务建筑。
        /// </summary>
        public static bool CanCustomerReserveServiceProvider(Pawn pawn, Thing provider)
        {
            if (pawn == null || provider == null || provider.Destroyed || !provider.Spawned) return false;
            return pawn.CanReserve(provider, CustomerServiceProviderReservationSlots, 0, null, false);
        }

        private sealed class ServiceCandidate
        {
            public readonly Thing Provider;
            public readonly ShopServiceDef ServiceDef;
            public readonly float Price;

            public ServiceCandidate(Thing provider, ShopServiceDef serviceDef, float price)
            {
                Provider = provider;
                ServiceDef = serviceDef;
                Price = price;
            }
        }

        /// <summary>
        /// 从可能带组件的 Thing 上安全获取服务提供组件。
        /// </summary>
        public static ThingComp_ServiceProvider GetProviderComp(Thing provider)
        {
            return (provider as ThingWithComps)?.GetComp<ThingComp_ServiceProvider>();
        }
    }
}
