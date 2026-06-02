using SimManagementLib.GameComp;
using SimManagementLib.Pojo;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimAI.CustomerVisit
{
    /// <summary>
    /// 选择顾客跨店行程中的下一家商店，负责匹配商品服务、可达性、队列和距离权重。
    /// </summary>
    public static class CustomerNextShopSelector
    {
        /// <summary>
        /// 从当前地图选择下一家适合的商店。
        /// </summary>
        public static Zone_Shop FindNextShop(LordJob_CustomerVisit visit, Pawn pawn, Zone_Shop currentShop, CustomerVisitSession session)
        {
            if (visit == null || pawn?.Map == null || currentShop == null || session == null)
                return null;

            Zone_Shop selected = null;
            float totalWeight = 0f;
            List<Zone> zones = pawn.Map.zoneManager.AllZones;
            for (int i = 0; i < zones.Count; i++)
            {
                Zone_Shop shop = zones[i] as Zone_Shop;
                if (shop == null || shop == currentShop) continue;
                if (!shop.IsOpenNow()) continue;
                if (session.HasVisitedShop(shop.ID)) continue;
                if (!TryGetReachableShopCell(pawn, shop, out IntVec3 reachableCell)) continue;
                if (!HasMatchingGoodsOrService(visit, pawn, shop)) continue;

                float weight = Mathf.Max(0.01f, ScoreNextShop(visit, pawn, shop, reachableCell));
                totalWeight += weight;
                if (Rand.Value * totalWeight <= weight)
                    selected = shop;
            }

            return selected;
        }

        /// <summary>
        /// 返回当前店队列人数，用于判断拥挤度。
        /// </summary>
        public static int GetCheckoutQueueSize(Map map, Zone_Shop shop)
        {
            if (map == null || shop == null) return 0;
            int count = 0;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn?.CurJobDef == null || pawn.CurJobDef.defName != "Customer_PayAtRegister") continue;
                if (pawn.CurJob?.targetA.Thing is Building_CashRegister register && shop.Cells.Contains(register.Position))
                    count++;
            }
            return count;
        }

        /// <summary>
        /// 判断商店是否存在顾客可能消费的商品或服务。
        /// </summary>
        private static bool HasMatchingGoodsOrService(LordJob_CustomerVisit visit, Pawn pawn, Zone_Shop shop)
        {
            float remainingBudget = visit.GetRemainingTripBudget(pawn, shop);
            if (remainingBudget <= 0f) return false;
            return CustomerShoppingMatchUtility.ShopHasMatchingAffordableGoodsOrServices(pawn, shop, visit, remainingBudget);
        }

        /// <summary>
        /// 计算下一家店的选择权重。
        /// </summary>
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

        /// <summary>
        /// 查找顾客可到达的商店格，负责避免跨店选择时依赖第一个格子不可达导致误判。
        /// </summary>
        private static bool TryGetReachableShopCell(Pawn pawn, Zone_Shop shop, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            if (pawn?.Map == null || shop == null) return false;
            foreach (IntVec3 candidate in shop.Cells)
            {
                if (!candidate.IsValid) continue;
                if (!pawn.CanReach(candidate, PathEndMode.ClosestTouch, Danger.Deadly)) continue;
                cell = candidate;
                return true;
            }
            return false;
        }
    }
}
