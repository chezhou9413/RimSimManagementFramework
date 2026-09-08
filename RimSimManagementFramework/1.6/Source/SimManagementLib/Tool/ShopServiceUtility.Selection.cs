using SimManagementLib.Pojo;
using SimManagementLib.SimAI;
using SimManagementLib.SimService;
using SimManagementLib.SimThingComp;
using SimManagementLib.SimZone;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SimManagementLib.Tool
{
    //统一服务筛选，职责是让寻店、目标选择和下单使用相同的分类、预算及溢价规则。
    public static partial class ShopServiceUtility
    {
        //在指定建筑重新选择服务，职责是下单前重新验证实时价格和容量。
        public static bool TryFindServiceAtProvider(Pawn pawn, Zone_Shop shop, Thing provider, float budget,
            out ShopServiceDef serviceDef, out float price)
        {
            LordJob_CustomerVisit visit = pawn?.GetLord()?.LordJob as LordJob_CustomerVisit;
            return TryFindServiceFromProviders(pawn, shop, budget,
                CustomerShoppingMatchUtility.GetTargetServiceCategoryIds(visit?.RuntimeCustomerKind, visit?.customerKind),
                new[] { provider }, out _, out serviceDef, out price);
        }

        //筛选可达、可预约且价格被接受的服务，以购买意愿决定候选权重。
        private static bool TryFindServiceFromProviders(Pawn pawn, Zone_Shop shop, float budget,
            IReadOnlyCollection<string> categoryIds, IEnumerable<Thing> providers,
            out Thing provider, out ShopServiceDef serviceDef, out float price)
        {
            provider = null;
            serviceDef = null;
            price = 0f;
            if (pawn == null || shop == null || budget < 0f) return false;
            LordJob_CustomerVisit visit = pawn.GetLord()?.LordJob as LordJob_CustomerVisit;
            CustomerPriceSensitivityProps sensitivity = visit?.GetPriceSensitivity(pawn.thingIDNumber);
            var candidates = new List<(Thing provider, ShopServiceDef service, float price, float weight)>();
            foreach (Thing candidate in providers)
            {
                var comp = GetProviderComp(candidate);
                if (comp == null || !comp.enabled) continue;
                if (!CustomerSafetyUtility.CanCustomerReach(pawn, candidate, PathEndMode.Touch, Danger.Deadly)) continue;
                if (!CanCustomerReserveServiceProvider(pawn, candidate)) continue;
                foreach (ServiceSlotData slot in comp.EnabledSlots)
                {
                    ShopServiceDef def = slot.ServiceDef;
                    if (def == null || (categoryIds != null && categoryIds.Count > 0 && !categoryIds.Contains(def.serviceCategoryId))) continue;
                    if (!def.Worker.CanUse(pawn, candidate, shop, out _) || !CanAcceptMoreUsers(candidate, def)) continue;
                    float quote = def.Worker.GetPrice(pawn, candidate, shop);
                    float reference = def.Worker.GetReferencePrice(pawn, candidate, shop);
                    if (float.IsNaN(quote) || float.IsInfinity(quote) || quote < 0f
                        || float.IsNaN(reference) || float.IsInfinity(reference) || reference < 0f)
                    {
                        Log.ErrorOnce("[RSMF] 服务价格配置无效：" + def.defName + "，售价=" + quote + "，参考价=" + reference, def.shortHash + 194276);
                        continue;
                    }
                    if (quote > budget) continue;
                    CustomerPriceEvaluation evaluation = CustomerPriceUtility.EvaluateMarketValue(reference, quote, sensitivity);
                    if (evaluation.rejected) continue;
                    candidates.Add((candidate, def, quote, evaluation.purchaseWeight));
                }
            }
            if (candidates.Count == 0) return false;
            var chosen = candidates.RandomElementByWeight(c => c.weight);
            provider = chosen.provider;
            serviceDef = chosen.service;
            price = chosen.price;
            return true;
        }
    }
}
