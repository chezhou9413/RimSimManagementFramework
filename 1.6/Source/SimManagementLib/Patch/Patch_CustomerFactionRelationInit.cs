using System;
using HarmonyLib;
using RimWorld;
using SimManagementLib.Tool;
using Verse;

namespace SimManagementLib.Patch
{
    /// <summary>
    /// 保护商店顾客临时派系的初始关系生成流程，避免外部派系兼容补丁的空引用中断顾客刷新。
    /// </summary>
    [HarmonyPatch(typeof(Faction), nameof(Faction.TryMakeInitialRelationsWith))]
    public static class Patch_CustomerFactionRelationInit
    {
        /// <summary>
        /// 在原版关系已建立后处理外部 Postfix 抛出的空引用，只吞掉商店顾客派系参与关系初始化时的兼容异常。
        /// </summary>
        public static Exception Finalizer(Faction __instance, Faction other, Exception __exception)
        {
            if (__exception == null)
                return null;

            if (!CustomerNeutralFactionUtility.IsCustomerFaction(__instance) && !CustomerNeutralFactionUtility.IsCustomerFaction(other))
                return __exception;

            if (!IsAlienRaceRelationInferenceNullReference(__exception))
                return __exception;

            Log.WarningOnce("[SimShop] 商店顾客临时派系初始化关系时遇到 Humanoid Alien Races 派系种族推断空引用，已保留原版关系结果并继续生成顾客。异常来源：" + __exception.GetType().Name, 74203191);
            return null;
        }

        /// <summary>
        /// 判断异常是否来自 Humanoid Alien Races 在派系关系 Postfix 中推断派系主要种族的空引用。
        /// </summary>
        private static bool IsAlienRaceRelationInferenceNullReference(Exception exception)
        {
            if (!(exception is NullReferenceException))
                return false;

            string stackTrace = exception.StackTrace;
            if (string.IsNullOrEmpty(stackTrace))
                return false;

            return stackTrace.Contains("AlienRace.HarmonyPatches")
                && stackTrace.Contains("TryMakeInitialRelationsWithPostfix");
        }
    }
}
