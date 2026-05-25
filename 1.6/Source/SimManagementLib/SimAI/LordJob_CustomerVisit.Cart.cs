using SimManagementLib.GameComp;
using SimManagementLib.Pojo;
using SimManagementLib.SimDef;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimAI
{
    public partial class LordJob_CustomerVisit
    {
        private const int DefaultMaxConsumptionActions = 3;

        /// <summary>
        /// 添加指定商品到顾客购物车。
        /// </summary>
        public void AddCartItem(int pawnId, ThingDef def, int count)
        {
            cartState.AddCartItem(pawnId, def, count);
        }

        /// <summary>
        /// 把套餐商品加入顾客购物车。
        /// </summary>
        public void AddCartItemsFromCombo(int pawnId, List<ComboItem> comboItems)
        {
            cartState.AddCartItemsFromCombo(pawnId, comboItems);
        }

        /// <summary>
        /// 返回指定顾客的购物车条目。
        /// </summary>
        public List<CustomerCartItem> GetCartItems(int pawnId)
        {
            return cartState.GetCartItems(pawnId);
        }

        /// <summary>
        /// 清除顾客的购物车、预算缓存、消费次数、结账顺序和浏览等待计时。
        /// </summary>
        public void ClearCustomerCart(int pawnId)
        {
            cartState.ClearCustomerCart(pawnId);
            checkoutState.ClearCheckoutOrder(pawnId);
        }

        /// <summary>
        /// 记录一次商品或服务消费动作，并在达到上限时要求顾客去结账。
        /// </summary>
        public bool RegisterConsumptionActionAndShouldCheckout(int pawnId)
        {
            return cartState.RegisterConsumptionActionAndShouldCheckout(pawnId, DefaultMaxConsumptionActions);
        }

        /// <summary>
        /// 判断顾客是否已经达到本次访问允许的最大消费动作数量。
        /// </summary>
        public bool HasReachedConsumptionLimit(int pawnId)
        {
            return cartState.HasReachedConsumptionLimit(pawnId, DefaultMaxConsumptionActions);
        }

        /// <summary>
        /// 设置指定顾客的运行时配置。
        /// </summary>
        public void SetPawnSettings(int pawnId, CustomerRuntimeSettings settings)
        {
            pawnSettingsState.SetPawnSettings(pawnId, settings);
        }

        /// <summary>
        /// 获取指定顾客的运行时配置。
        /// </summary>
        public CustomerRuntimeSettings GetPawnSettings(int pawnId)
        {
            return pawnSettingsState.GetPawnSettings(pawnId);
        }

        /// <summary>
        /// 返回顾客原始预算，优先使用按顾客写入的运行时预算。
        /// </summary>
        public int GetBudgetForPawn(int pawnId)
        {
            if (pawnSettings.TryGetValue(pawnId, out CustomerRuntimeSettings settings) && settings != null && settings.budget > 0)
                return settings.budget;
            return totalBudget > 0 ? totalBudget : 1;
        }

        /// <summary>
        /// 返回顾客本次愿意消费的预算上限，负责让低品质商店难以吃满顾客原始预算。
        /// </summary>
        public int GetEffectiveBudgetForPawn(Pawn pawn, Zone_Shop shopZone)
        {
            if (pawn == null)
                return 1;

            int pawnId = pawn.thingIDNumber;
            int rawBudget = GetBudgetForPawn(pawnId);
            if (rawBudget <= 1)
                return 1;

            if (effectiveBudgetCaps.TryGetValue(pawnId, out int cached) && cached > 0)
                return Mathf.Clamp(cached, 1, rawBudget);

            float spendRatio = CalculateBudgetSpendRatio(pawn, shopZone);
            int cap = Mathf.Clamp(Mathf.RoundToInt(rawBudget * spendRatio), 1, rawBudget);
            effectiveBudgetCaps[pawnId] = cap;
            return cap;
        }

        /// <summary>
        /// 计算品质驱动的消费意愿比例，负责把商店评分、口碑和个人波动转成预算使用率。
        /// </summary>
        private float CalculateBudgetSpendRatio(Pawn pawn, Zone_Shop shopZone)
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

        /// <summary>
        /// 返回顾客排队耐心，优先使用个体设置，其次使用顾客类型配置。
        /// </summary>
        public int GetQueuePatienceForPawn(int pawnId)
        {
            if (pawnSettings.TryGetValue(pawnId, out CustomerRuntimeSettings settings) && settings != null && settings.queuePatienceTicks > 0)
                return settings.queuePatienceTicks;
            if (RuntimeCustomerKind?.shoppingBehavior != null && RuntimeCustomerKind.shoppingBehavior.queuePatience > 0)
                return RuntimeCustomerKind.shoppingBehavior.queuePatience;
            return 2500;
        }

        /// <summary>
        /// 返回顾客对指定物品的偏好倍率。
        /// </summary>
        public float GetPreferenceMultiplier(int pawnId, ThingDef def)
        {
            float multiplier = 1f;

            if (pawnSettings.TryGetValue(pawnId, out CustomerRuntimeSettings settings) && settings != null)
                multiplier *= settings.GetPreferenceMultiplier(def);

            if (RuntimeCustomerKind != null)
                multiplier *= RuntimeCustomerKind.GetPreferenceMultiplier(def);

            return multiplier;
        }

        /// <summary>
        /// 获取或初始化顾客浏览等待开始 Tick。
        /// </summary>
        public int GetOrInitBrowseWaitStartTick(int pawnId, int nowTick)
        {
            return cartState.GetOrInitBrowseWaitStartTick(pawnId, nowTick);
        }

        /// <summary>
        /// 清除顾客浏览等待计时。
        /// </summary>
        public void ClearBrowseWaitStartTick(int pawnId)
        {
            cartState.ClearBrowseWaitStartTick(pawnId);
        }
    }
}
