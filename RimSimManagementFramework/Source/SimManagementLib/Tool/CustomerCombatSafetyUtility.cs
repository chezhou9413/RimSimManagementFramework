using RimWorld;
using SimManagementLib.SimAI;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 判断顾客访问期间的战斗保护状态，供敌对关系和攻击入口补丁共享同一套快速判断。
    /// </summary>
    public static class CustomerCombatSafetyUtility
    {
        /// <summary>
        /// 判断指定 Pawn 是否正在以顾客访问身份受商店战斗保护。
        /// </summary>
        public static bool IsProtectedCustomer(Pawn pawn)
        {
            return pawn != null
                && !pawn.Destroyed
                && !pawn.Dead
                && pawn.lord != null
                && pawn.lord.LordJob is LordJob_CustomerVisit;
        }

        /// <summary>
        /// 判断指定 Thing 是否是受保护的顾客 Pawn。
        /// </summary>
        public static bool IsProtectedCustomerThing(Thing thing)
        {
            Pawn pawn = thing as Pawn;
            return IsProtectedCustomer(pawn);
        }

        /// <summary>
        /// 判断两个 Thing 之间的敌对关系是否应因顾客保护而被压制。
        /// </summary>
        public static bool ShouldSuppressHostility(Thing first, Thing second)
        {
            return IsProtectedCustomerThing(first) || IsProtectedCustomerThing(second);
        }

        /// <summary>
        /// 判断 Thing 对派系的敌对关系是否应因顾客保护而被压制。
        /// </summary>
        public static bool ShouldSuppressHostility(Thing thing, Faction faction)
        {
            return faction != null && IsProtectedCustomerThing(thing);
        }

        /// <summary>
        /// 判断一次攻击行为是否应因攻击者或目标处于顾客保护状态而被阻止。
        /// </summary>
        public static bool ShouldSuppressAttack(Pawn attacker, Thing target)
        {
            return IsProtectedCustomer(attacker) || IsProtectedCustomerThing(target);
        }

        /// <summary>
        /// 判断一次 Verb 释放是否应因施放者、主目标或落点目标处于顾客保护状态而被阻止。
        /// </summary>
        public static bool ShouldSuppressVerbCast(Pawn caster, LocalTargetInfo castTarget, LocalTargetInfo destinationTarget)
        {
            return IsProtectedCustomer(caster)
                || IsProtectedCustomerThing(castTarget.Thing)
                || IsProtectedCustomerThing(destinationTarget.Thing);
        }
    }
}
