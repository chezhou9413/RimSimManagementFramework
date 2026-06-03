using SimManagementLib.Pojo;
using UnityEngine;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 计算顾客对商品价格的购买意愿，负责把市价倍率转换为筛选、权重和评价信号。
    /// </summary>
    public static class CustomerPriceUtility
    {
        /// <summary>
        /// 根据单件售价和商品市价构建价格评估结果。
        /// </summary>
        public static CustomerPriceEvaluation Evaluate(ThingDef def, float unitPrice, CustomerPriceSensitivityProps sensitivity)
        {
            CustomerPriceSensitivityProps props = CustomerPriceSensitivityProps.Resolve(sensitivity);
            float referenceValue = GetReferenceMarketValue(def);
            float safePrice = Mathf.Max(0f, unitPrice);
            float ratio = referenceValue > 0f ? safePrice / referenceValue : 1f;
            bool rejected = ratio > props.rejectMarkupRatio;
            bool complain = ratio >= props.complainMarkupRatio;
            float weight = CalculateWeight(ratio, props);
            return new CustomerPriceEvaluation(referenceValue, safePrice, ratio, weight, rejected, complain);
        }

        /// <summary>
        /// 根据套餐总价和套餐市价构建价格评估结果。
        /// </summary>
        public static CustomerPriceEvaluation EvaluateCombo(float totalPrice, float referenceValue, CustomerPriceSensitivityProps sensitivity)
        {
            CustomerPriceSensitivityProps props = CustomerPriceSensitivityProps.Resolve(sensitivity);
            float safeReference = Mathf.Max(1f, referenceValue);
            float safePrice = Mathf.Max(0f, totalPrice);
            float ratio = safePrice / safeReference;
            bool rejected = ratio > props.rejectMarkupRatio;
            bool complain = ratio >= props.complainMarkupRatio;
            float weight = CalculateWeight(ratio, props);
            return new CustomerPriceEvaluation(safeReference, safePrice, ratio, weight, rejected, complain);
        }

        /// <summary>
        /// 返回商品参考市价，负责统一处理无市价 Def。
        /// </summary>
        public static float GetReferenceMarketValue(ThingDef def)
        {
            return Mathf.Max(1f, def?.BaseMarketValue ?? 1f);
        }

        /// <summary>
        /// 返回套餐内商品的参考市价总和。
        /// </summary>
        public static float GetComboReferenceValue(ComboData combo)
        {
            if (combo == null || combo.items.NullOrEmpty())
                return 1f;

            float value = 0f;
            for (int i = 0; i < combo.items.Count; i++)
            {
                ComboItem item = combo.items[i];
                if (item?.def == null || item.count <= 0)
                    continue;
                value += GetReferenceMarketValue(item.def) * item.count;
            }
            return Mathf.Max(1f, value);
        }

        /// <summary>
        /// 按价格倍率计算购买权重，负责让折扣提高意愿、溢价降低意愿。
        /// </summary>
        private static float CalculateWeight(float ratio, CustomerPriceSensitivityProps props)
        {
            if (ratio <= 0f)
                return props.discountWeightMultiplier;

            if (ratio <= 1f)
            {
                float discount01 = Mathf.Clamp01(1f - ratio);
                return Mathf.Lerp(1f, props.discountWeightMultiplier, discount01);
            }

            if (ratio <= props.softMarkupRatio)
            {
                float t = Mathf.InverseLerp(1f, props.softMarkupRatio, ratio);
                return Mathf.Lerp(1f, 0.35f, t);
            }

            if (ratio <= props.rejectMarkupRatio)
            {
                float t = Mathf.InverseLerp(props.softMarkupRatio, props.rejectMarkupRatio, ratio);
                return Mathf.Lerp(0.35f, props.overpricedMinWeight, t);
            }

            return 0f;
        }
    }

    /// <summary>
    /// 保存一次商品或套餐价格评估结果，负责减少购买流程中的重复计算。
    /// </summary>
    public struct CustomerPriceEvaluation
    {
        public readonly float referenceValue;
        public readonly float price;
        public readonly float ratio;
        public readonly float purchaseWeight;
        public readonly bool rejected;
        public readonly bool shouldComplain;

        public CustomerPriceEvaluation(float referenceValue, float price, float ratio, float purchaseWeight, bool rejected, bool shouldComplain)
        {
            this.referenceValue = referenceValue;
            this.price = price;
            this.ratio = ratio;
            this.purchaseWeight = purchaseWeight;
            this.rejected = rejected;
            this.shouldComplain = shouldComplain;
        }
    }
}
