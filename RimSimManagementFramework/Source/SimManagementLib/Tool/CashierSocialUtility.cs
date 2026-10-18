using RimWorld;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 提供收银员社交能力兜底计算，负责让无技能组件的机械体也能稳定参与收银流程。
    /// </summary>
    public static class CashierSocialUtility
    {
        private const int DefaultMissingSocialLevel = 10;
        private const float SocialImpactBaseValue = 0.82f;
        private const float SocialImpactBonusPerLevel = 0.0275f;

        /// <summary>
        /// 返回收银相关逻辑使用的有效社交等级，负责在缺少技能系统时使用默认社交等级。
        /// </summary>
        public static int GetEffectiveSocialLevel(Pawn pawn)
        {
            SkillRecord record = GetSocialRecord(pawn);
            return record?.Level ?? DefaultMissingSocialLevel;
        }

        /// <summary>
        /// 尝试给收银员增加社交经验，负责跳过没有社交技能记录的 Pawn。
        /// </summary>
        public static void TryLearnSocial(Pawn pawn, float xp)
        {
            SkillRecord record = GetSocialRecord(pawn);
            if (record == null)
                return;

            record.Learn(xp);
        }

        /// <summary>
        /// 返回收银服务使用的社交影响力，负责把无技能 Pawn 按默认社交等级补入社交技能因子。
        /// </summary>
        public static float GetServiceSocialImpact(Pawn pawn)
        {
            if (pawn == null)
                return 1f;

            float socialImpact = pawn.GetStatValue(StatDefOf.SocialImpact);
            if (GetSocialRecord(pawn) != null)
                return socialImpact;

            return socialImpact * GetSocialImpactFactorForLevel(DefaultMissingSocialLevel);
        }

        /// <summary>
        /// 判断 Pawn 是否存在可显示和可训练的社交技能，负责避免机械体暴露虚假的当前技能。
        /// </summary>
        public static bool HasSocialSkillRecord(Pawn pawn)
        {
            return GetSocialRecord(pawn) != null;
        }

        /// <summary>
        /// 安全读取社交技能记录，负责把缺失技能追踪器和缺失技能记录都视为无社交技能。
        /// </summary>
        private static SkillRecord GetSocialRecord(Pawn pawn)
        {
            if (pawn?.skills == null)
                return null;

            try
            {
                return pawn.skills.GetSkill(SkillDefOf.Social);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 按原版 SocialImpact 的 SkillNeed_BaseBonus 配置计算指定社交等级的倍率。
        /// </summary>
        private static float GetSocialImpactFactorForLevel(int level)
        {
            return SocialImpactBaseValue + SocialImpactBonusPerLevel * level;
        }
    }
}
