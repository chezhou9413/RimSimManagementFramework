using HarmonyLib;
using RimWorld;
using SimManagementLib.Tool;
using Verse;

namespace SimManagementLib.Patch
{
    /// <summary>
    /// 为顾客访问期间添加局部战斗保护，避免修改全局派系关系造成跨模组副作用。
    /// </summary>
    public static class Patch_CustomerCombatSafety
    {
        /// <summary>
        /// 压制任意 Thing 与受保护顾客之间的敌对判断。
        /// </summary>
        [HarmonyPatch(typeof(GenHostility), nameof(GenHostility.HostileTo), new[] { typeof(Thing), typeof(Thing) })]
        public static class GenHostility_HostileToThing
        {
            /// <summary>
            /// 在原版敌对判断前识别顾客保护状态，命中时直接返回非敌对。
            /// </summary>
            public static bool Prefix(Thing a, Thing b, ref bool __result)
            {
                if (!CustomerCombatSafetyUtility.ShouldSuppressHostility(a, b))
                    return true;

                __result = false;
                return false;
            }
        }

        /// <summary>
        /// 压制受保护顾客对任意派系的敌对判断。
        /// </summary>
        [HarmonyPatch(typeof(GenHostility), nameof(GenHostility.HostileTo), new[] { typeof(Thing), typeof(Faction) })]
        public static class GenHostility_HostileToFaction
        {
            /// <summary>
            /// 在原版敌对判断前识别顾客保护状态，命中时直接返回非敌对。
            /// </summary>
            public static bool Prefix(Thing t, Faction fac, ref bool __result)
            {
                if (!CustomerCombatSafetyUtility.ShouldSuppressHostility(t, fac))
                    return true;

                __result = false;
                return false;
            }
        }

        /// <summary>
        /// 阻止 Pawn 对受保护顾客发起攻击，也阻止顾客主动发起攻击。
        /// </summary>
        [HarmonyPatch(typeof(Pawn), nameof(Pawn.TryStartAttack))]
        public static class Pawn_TryStartAttack
        {
            /// <summary>
            /// 在攻击任务真正启动前检查顾客保护状态。
            /// </summary>
            public static bool Prefix(Pawn __instance, LocalTargetInfo targ, ref bool __result)
            {
                if (!CustomerCombatSafetyUtility.ShouldSuppressAttack(__instance, targ.Thing))
                    return true;

                __result = false;
                return false;
            }
        }

        /// <summary>
        /// 阻止 Verb 对受保护顾客释放，也阻止顾客通过 Verb 造成攻击。
        /// </summary>
        [HarmonyPatch(typeof(Verb), nameof(Verb.TryStartCastOn), new[] { typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
        public static class Verb_TryStartCastOn
        {
            /// <summary>
            /// 在射击、近战和能力类 Verb 进入暖身或立即释放前检查顾客保护状态。
            /// </summary>
            public static bool Prefix(Verb __instance, LocalTargetInfo castTarg, LocalTargetInfo destTarg, ref bool __result)
            {
                if (__instance == null || !CustomerCombatSafetyUtility.ShouldSuppressVerbCast(__instance.CasterPawn, castTarg, destTarg))
                    return true;

                __result = false;
                return false;
            }
        }
    }
}
