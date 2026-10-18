using SimManagementLib.Pojo;
using Verse;

namespace SimManagementLib.SimAI.CustomerVisit
{
    /// <summary>
    /// 计算顾客对商品的偏好倍率，负责合并个体画像和顾客类型的偏好。
    /// </summary>
    public static class CustomerPreferencePolicy
    {
        /// <summary>
        /// 返回顾客对指定物品的偏好倍率。
        /// </summary>
        public static float GetPreferenceMultiplier(LordJob_CustomerVisit visit, int pawnId, ThingDef def)
        {
            float multiplier = 1f;

            if (visit?.pawnSettings != null
                && visit.pawnSettings.TryGetValue(pawnId, out CustomerRuntimeSettings settings)
                && settings != null)
                multiplier *= settings.GetPreferenceMultiplier(def);

            if (visit?.RuntimeCustomerKind != null)
                multiplier *= visit.RuntimeCustomerKind.GetPreferenceMultiplier(def);

            return multiplier;
        }
    }
}
