using SimManagementLib.GameComp;
using SimManagementLib.Pojo;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimMapComp;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimAI.CustomerVisit
{
    //类职责：从地图级开放商店快照选择下一家店，并只对最终候选执行一次可达判断。
    public static class CustomerNextShopSelector
    {
        //从当前地图选择下一家适合的商店。
        public static Zone_Shop FindNextShop(LordJob_CustomerVisit visit, Pawn pawn, Zone_Shop currentShop, CustomerVisitSession session)
        {
            if (visit == null || pawn?.Map == null || currentShop == null || session == null)
                return null;

            CustomerArrivalManager manager = pawn.Map.GetComponent<CustomerArrivalManager>();
            List<CustomerArrivalShopContext> contexts = manager?.GetOpenShopContexts();
            if (contexts.NullOrEmpty()) return null;
            Zone_Shop selected = null;
            IntVec3 selectedCell = IntVec3.Invalid;
            float totalWeight = 0f;
            for (int i = 0; i < contexts.Count; i++)
            {
                CustomerArrivalShopContext context = contexts[i];
                Zone_Shop shop = context?.Shop;
                if (shop == null || shop == currentShop) continue;
                if (session.HasVisitedShop(shop.ID)) continue;
                if (!context.EntryCell.IsValid) continue;
                if (!context.MatchingKindIds.Contains(visit.customerKindId ?? "")) continue;
                if (visit.GetRemainingTripBudget(pawn, shop) <= 0f) continue;

                float weight = Mathf.Max(0.01f, ScoreNextShop(visit, pawn, shop, context.EntryCell));
                totalWeight += weight;
                if (Rand.Value * totalWeight <= weight)
                {
                    selected = shop;
                    selectedCell = context.EntryCell;
                }
            }

            return selected != null
                && manager.TryConsumeReachabilityBudget()
                && CustomerSafetyUtility.CanCustomerReach(pawn, selectedCell, PathEndMode.OnCell, Danger.Deadly)
                ? selected
                : null;
        }
        //返回当前店队列人数，用于判断拥挤度。
        public static int GetCheckoutQueueSize(Map map, Zone_Shop shop)
        {
            if (map == null || shop == null) return 0;
            CustomerCheckoutQueueRegistry registry = map.GetComponent<CustomerArrivalManager>()?.CheckoutQueue;
            if (registry == null) return 0;
            int count = 0;
            IReadOnlyList<Building_CashRegister> registers = ShopDataUtility.GetCashRegisterSnapshotInZone(shop);
            for (int i = 0; i < registers.Count; i++)
            {
                count += registry.CountForRegister(registers[i]);
            }
            return count;
        }
        //计算下一家店的选择权重。
        private static float ScoreNextShop(LordJob_CustomerVisit visit, Pawn pawn, Zone_Shop shop, IntVec3 shopCell)
        {
            float score = 1f;
            GameComponent_ShopAnalyticsManager analytics = Current.Game?.GetComponent<GameComponent_ShopAnalyticsManager>();
            ShopMetricsSnapshot metrics = analytics?.GetOrEvaluateShopMetrics(shop);
            if (metrics != null)
                score += Mathf.Clamp(metrics.score, 0f, 100f) / 100f;
            score += Mathf.Clamp01(visit.GetRemainingTripBudget(pawn, shop) / Mathf.Max(1f, visit.GetBudgetForPawn(pawn.thingIDNumber)));
            int queue = GetCheckoutQueueSize(pawn.Map, shop);
            score *= 1f / Mathf.Max(1f, 1f + queue * 0.35f);
            float dist = (shopCell - pawn.Position).LengthHorizontal;
            score *= 1f / Mathf.Max(1f, dist / 20f);
            return score;
        }

    }
}
