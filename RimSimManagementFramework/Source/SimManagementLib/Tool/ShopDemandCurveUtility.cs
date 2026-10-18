using SimManagementLib.SimDef;
using UnityEngine;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 负责把商店美观度和有效规模转换为刷客需求倍率与顾客容量倍率。
    /// </summary>
    public static class ShopDemandCurveUtility
    {
        /// <summary>
        /// 根据商店平均美观度计算刷客需求倍率。
        /// </summary>
        public static float CalculateBeautyDemandMultiplier(ShopTuningDef tuning, float beautyAverage)
        {
            if (tuning == null) return 1f;

            float beauty01 = Mathf.InverseLerp(tuning.beautyDemandRange.min, tuning.beautyDemandRange.max, beautyAverage);
            return Mathf.Lerp(tuning.beautyDemandMultiplierRange.min, tuning.beautyDemandMultiplierRange.max, Mathf.Clamp01(beauty01));
        }

        /// <summary>
        /// 计算商店有效规模分数，只有有库存货柜、可售商品、收银台和面积共同构成有效规模。
        /// </summary>
        public static float CalculateEffectiveScale(
            ShopTuningDef tuning,
            int registerCount,
            int mannedCount,
            int stockedStorageCount,
            int inStockGoodsKinds,
            int zoneCellCount)
        {
            if (tuning == null) return 0f;

            float areaScore = Mathf.Sqrt(Mathf.Max(0, zoneCellCount)) * tuning.scaleAreaSqrtWeight;
            float scale = registerCount * tuning.scaleRegisterWeight
                        + mannedCount * tuning.scaleMannedRegisterWeight
                        + stockedStorageCount * tuning.scaleStockedStorageWeight
                        + inStockGoodsKinds * tuning.scaleGoodsKindWeight
                        + areaScore;
            return Mathf.Max(0f, scale);
        }

        /// <summary>
        /// 根据有效规模分数计算刷客需求倍率，使用平滑递减收益避免大店倍率跳变。
        /// </summary>
        public static float CalculateScaleDemandMultiplier(ShopTuningDef tuning, float effectiveScale)
        {
            if (tuning == null) return 1f;

            float scale01 = CalculateScale01(tuning, effectiveScale);
            return Mathf.Lerp(tuning.scaleDemandMultiplierRange.min, tuning.scaleDemandMultiplierRange.max, scale01);
        }

        /// <summary>
        /// 根据有效规模分数计算顾客容量倍率，范围比刷客倍率更保守。
        /// </summary>
        public static float CalculateScaleCapacityMultiplier(ShopTuningDef tuning, float effectiveScale)
        {
            if (tuning == null) return 1f;

            float scale01 = CalculateScale01(tuning, effectiveScale);
            return Mathf.Lerp(tuning.scaleCapacityMultiplierRange.min, tuning.scaleCapacityMultiplierRange.max, scale01);
        }

        /// <summary>
        /// 将有效规模分数归一化为平滑曲线坐标。
        /// </summary>
        private static float CalculateScale01(ShopTuningDef tuning, float effectiveScale)
        {
            float raw = Mathf.Clamp01(effectiveScale / Mathf.Max(0.01f, tuning.scaleDemandTarget));
            return raw * raw * (3f - 2f * raw);
        }
    }
}
