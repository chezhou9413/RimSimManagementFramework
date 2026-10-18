using SimManagementLib.GameComp;
using SimManagementLib.Pojo;
using SimManagementLib.SimDef;
using SimManagementLib.SimZone;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimAI.CustomerVisit
{
    /// <summary>
    /// 计算顾客预算、消费意愿和排队耐心，负责把预算策略从顾客 LordJob 中拆出。
    /// </summary>
    public static class CustomerBudgetPolicy
    {
        /// <summary>
        /// 返回顾客原始预算，优先使用按顾客写入的运行时预算。
        /// </summary>
        public static int GetBudgetForPawn(LordJob_CustomerVisit visit, int pawnId)
        {
            if (visit?.pawnSettings != null
                && visit.pawnSettings.TryGetValue(pawnId, out CustomerRuntimeSettings settings)
                && settings != null
                && settings.budget > 0)
                return settings.budget;
            return visit != null && visit.totalBudget > 0 ? visit.totalBudget : 1;
        }

        /// <summary>
        /// 返回顾客本次愿意消费的预算上限，负责让低品质商店难以吃满顾客原始预算。
        /// </summary>
        public static int GetEffectiveBudgetForPawn(LordJob_CustomerVisit visit, Pawn pawn, Zone_Shop shopZone)
        {
            if (visit == null || pawn == null)
                return 1;

            int pawnId = pawn.thingIDNumber;
            int rawBudget = GetBudgetForPawn(visit, pawnId);
            if (rawBudget <= 1)
                return 1;

            if (visit.effectiveBudgetCaps.TryGetValue(pawnId, out int cached) && cached > 0)
                return Mathf.Clamp(cached, 1, rawBudget);

            float spendRatio = CalculateBudgetSpendRatio(pawn, shopZone);
            int cap = Mathf.Clamp(Mathf.RoundToInt(rawBudget * spendRatio), 1, rawBudget);
            visit.effectiveBudgetCaps[pawnId] = cap;
            return cap;
        }

        /// <summary>
        /// 返回顾客排队耐心，优先使用个体设置，其次使用顾客类型配置。
        /// </summary>
        public static int GetQueuePatienceForPawn(LordJob_CustomerVisit visit, int pawnId)
        {
            if (visit?.pawnSettings != null
                && visit.pawnSettings.TryGetValue(pawnId, out CustomerRuntimeSettings settings)
                && settings != null
                && settings.queuePatienceTicks > 0)
                return settings.queuePatienceTicks;
            if (visit?.RuntimeCustomerKind?.shoppingBehavior != null && visit.RuntimeCustomerKind.shoppingBehavior.queuePatience > 0)
                return visit.RuntimeCustomerKind.shoppingBehavior.queuePatience;
            return 2500;
        }

        /// <summary>
        /// 计算品质驱动的消费意愿比例，负责把商店评分、口碑和个人波动转成预算使用率。
        /// </summary>
        private static float CalculateBudgetSpendRatio(Pawn pawn, Zone_Shop shopZone)
        {
            ShopTuningDef tuning = DefDatabase<ShopTuningDef>.AllDefsListForReading.FirstOrDefault() ?? new ShopTuningDef();
            float qualityScore = 50f;
            float reputation = 50f;

            GameComponent_ShopAnalyticsManager analytics = Current.Game?.GetComponent<GameComponent_ShopAnalyticsManager>();
            ShopMetricsSnapshot metrics = analytics?.GetOrEvaluateShopMetrics(shopZone);
            if (metrics != null)
            {
                qualityScore = metrics.score;
                reputation = metrics.reputation;
            }

            float quality01 = Mathf.InverseLerp(tuning.budgetSpendQualityRange.min, tuning.budgetSpendQualityRange.max, qualityScore);
            float reputation01 = Mathf.Clamp01(reputation / 100f);
            float confidence01 = Mathf.Clamp01(quality01 * 0.80f + reputation01 * 0.20f);
            float ratio = Mathf.Lerp(tuning.budgetSpendRatioRange.min, tuning.budgetSpendRatioRange.max, confidence01);

            float jitter = Mathf.Clamp(tuning.budgetSpendRandomJitter, 0f, 0.50f);
            if (jitter > 0f && pawn != null)
                ratio += RandByPawnAndShop(pawn, shopZone) * jitter * 2f - jitter;

            return Mathf.Clamp(ratio, 0.05f, 1f);
        }

        /// <summary>
        /// 生成顾客和商店稳定绑定的随机值，负责让同一位顾客本次访问的消费意愿保持一致。
        /// </summary>
        private static float RandByPawnAndShop(Pawn pawn, Zone_Shop shopZone)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (pawn?.thingIDNumber ?? 0);
                hash = hash * 31 + (shopZone?.ID ?? -1);
                hash = hash * 31 + (Find.TickManager?.TicksGame / 60000 ?? 0);
                int positive = hash & int.MaxValue;
                return positive / (float)int.MaxValue;
            }
        }
    }
}
