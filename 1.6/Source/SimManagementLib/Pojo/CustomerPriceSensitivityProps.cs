using System.Runtime.Serialization;
using UnityEngine;
using Verse;

namespace SimManagementLib.Pojo
{
    /// <summary>
    /// 保存顾客对商品溢价和折扣的反应参数，负责为购物权重、拒买和评价价格信号提供统一默认值。
    /// </summary>
    [DataContract]
    public class CustomerPriceSensitivityProps : IExposable
    {
        [DataMember]
        public float discountWeightMultiplier = 1.35f;

        [DataMember]
        public float softMarkupRatio = 1.5f;

        [DataMember]
        public float rejectMarkupRatio = 2.5f;

        [DataMember]
        public float overpricedMinWeight = 0.05f;

        [DataMember]
        public float complainMarkupRatio = 2.0f;

        /// <summary>
        /// 创建默认价格敏感度配置，负责让缺省顾客和旧存档顾客行为一致。
        /// </summary>
        public static CustomerPriceSensitivityProps Default()
        {
            return new CustomerPriceSensitivityProps();
        }

        /// <summary>
        /// 复制一份价格敏感度配置，负责避免运行时顾客共享 Def 原始对象。
        /// </summary>
        public CustomerPriceSensitivityProps Clone()
        {
            CustomerPriceSensitivityProps clone = new CustomerPriceSensitivityProps
            {
                discountWeightMultiplier = discountWeightMultiplier,
                softMarkupRatio = softMarkupRatio,
                rejectMarkupRatio = rejectMarkupRatio,
                overpricedMinWeight = overpricedMinWeight,
                complainMarkupRatio = complainMarkupRatio
            };
            clone.EnsureDefaults();
            return clone;
        }

        /// <summary>
        /// 从覆盖配置和父级配置生成最终配置，负责让档案配置可以继承顾客类型配置。
        /// </summary>
        public static CustomerPriceSensitivityProps Resolve(CustomerPriceSensitivityProps overrideProps, CustomerPriceSensitivityProps parentProps = null)
        {
            CustomerPriceSensitivityProps result = overrideProps != null
                ? overrideProps.Clone()
                : parentProps != null
                    ? parentProps.Clone()
                    : Default();
            result.EnsureDefaults();
            return result;
        }

        /// <summary>
        /// 读写价格敏感度配置，并在读档后补齐安全默认值。
        /// </summary>
        public void ExposeData()
        {
            Scribe_Values.Look(ref discountWeightMultiplier, "discountWeightMultiplier", 1.35f);
            Scribe_Values.Look(ref softMarkupRatio, "softMarkupRatio", 1.5f);
            Scribe_Values.Look(ref rejectMarkupRatio, "rejectMarkupRatio", 2.5f);
            Scribe_Values.Look(ref overpricedMinWeight, "overpricedMinWeight", 0.05f);
            Scribe_Values.Look(ref complainMarkupRatio, "complainMarkupRatio", 2.0f);
            EnsureDefaults();
        }

        /// <summary>
        /// 修正无效配置，负责保证价格曲线始终单调且不会产生零权重异常。
        /// </summary>
        public void EnsureDefaults()
        {
            if (discountWeightMultiplier <= 0f) discountWeightMultiplier = 1.35f;
            if (softMarkupRatio <= 0f) softMarkupRatio = 1.5f;
            if (rejectMarkupRatio <= 0f) rejectMarkupRatio = 2.5f;
            if (complainMarkupRatio <= 0f) complainMarkupRatio = 2.0f;

            discountWeightMultiplier = Mathf.Clamp(discountWeightMultiplier, 0.1f, 10f);
            softMarkupRatio = Mathf.Clamp(softMarkupRatio, 1f, 20f);
            rejectMarkupRatio = Mathf.Clamp(Mathf.Max(rejectMarkupRatio, softMarkupRatio + 0.01f), 1.01f, 50f);
            overpricedMinWeight = Mathf.Clamp(overpricedMinWeight, 0.001f, 1f);
            complainMarkupRatio = Mathf.Clamp(complainMarkupRatio, 1f, rejectMarkupRatio);
        }
    }
}
