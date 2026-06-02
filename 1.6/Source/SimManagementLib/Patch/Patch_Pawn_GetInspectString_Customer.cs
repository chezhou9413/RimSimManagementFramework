using HarmonyLib;
using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.SimAI;
using SimManagementLib.SimAI.CustomerVisit;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace SimManagementLib.Patch
{
    /// <summary>
    /// 为顾客 Pawn 的检查信息追加经营摘要，负责让玩家看到低密度状态，并在开发上帝模式下显示诊断信息。
    /// </summary>
    [HarmonyPatch(typeof(Pawn), "GetInspectString")]
    public static class Patch_Pawn_GetInspectString_Customer
    {
        /// <summary>
        /// 在原版检查信息末尾追加顾客信息。
        /// </summary>
        [HarmonyPostfix]
        public static void Postfix(Pawn __instance, ref string __result)
        {
            if (__instance == null || !__instance.Spawned || __instance.Map == null) return;
            if (!(SimManagementLibMod.Settings?.showCustomerInspectDetails ?? true)) return;

            Lord lord = __instance.Map.lordManager?.LordOf(__instance);
            LordJob_CustomerVisit customerLord = lord?.LordJob as LordJob_CustomerVisit;
            if (customerLord == null) return;

            int pawnId = __instance.thingIDNumber;
            CustomerRuntimeSettings settings = customerLord.GetPawnSettings(pawnId);
            int budget = customerLord.GetBudgetForPawn(pawnId);
            float owed = customerLord.GetAmountOwedForCheckout(pawnId);
            string queueState = GetCustomerQueueState(customerLord, pawnId);
            CustomerVisitSession session = customerLord.GetOrCreateSession(__instance);
            Zone_Shop currentShop = customerLord.GetCurrentShop(__instance);
            string profile = ResolveProfileLabel(settings, customerLord);
            string shopLabel = currentShop?.label ?? SimTranslation.T("RSMF.Common.None");
            float totalSpent = session?.TotalSpentAcrossShops ?? 0f;
            float remaining = Mathf.Max(0f, budget - totalSpent - owed);

            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(__result))
            {
                sb.Append(__result).AppendLine();
            }

            sb.AppendLine(SimTranslation.T("RSMF.CustomerInspect.Header"));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerInspect.ProfileLine", profile.Named("profile")));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerInspect.StateLine", queueState.Named("state"), shopLabel.Named("shop")));
            sb.AppendLine(BuildSpendLine(budget, owed, remaining));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerInspect.PreferenceLine", BuildPreferenceSummary(settings).Named("preferences")));

            if (ShouldShowDeveloperDiagnostics)
                AppendDeveloperDiagnostics(sb, customerLord, __instance, session);

            __result = sb.ToString().TrimEnd();
        }

        /// <summary>
        /// 判断是否显示开发诊断信息，负责避免普通玩家面板出现高密度运行态。
        /// </summary>
        private static bool ShouldShowDeveloperDiagnostics => Prefs.DevMode && DebugSettings.godMode;

        /// <summary>
        /// 返回顾客类型显示名，负责在档案标签为空时回退到顾客定义名称。
        /// </summary>
        private static string ResolveProfileLabel(CustomerRuntimeSettings settings, LordJob_CustomerVisit customerLord)
        {
            if (!string.IsNullOrEmpty(settings?.profileLabel))
                return settings.profileLabel;
            if (!string.IsNullOrEmpty(customerLord?.RuntimeCustomerKind?.label))
                return customerLord.RuntimeCustomerKind.label;
            if (!string.IsNullOrEmpty(customerLord?.customerKind?.LabelCap))
                return customerLord.customerKind.LabelCap;
            return SimTranslation.T("RSMF.Common.Default");
        }

        /// <summary>
        /// 构建消费摘要行，负责用玩家可读的预算和待付款信息替代调试数值。
        /// </summary>
        private static string BuildSpendLine(int budget, float owed, float remaining)
        {
            if (owed > 0.01f)
            {
                return SimTranslation.T(
                    "RSMF.CustomerInspect.SpendLineWithOwed",
                    owed.ToString("F0").Named("owed"),
                    remaining.ToString("F0").Named("remaining"));
            }

            return SimTranslation.T(
                "RSMF.CustomerInspect.SpendLine",
                budget.Named("budget"),
                remaining.ToString("F0").Named("remaining"));
        }

        /// <summary>
        /// 构建偏好摘要，负责把物品和品类压缩到一行。
        /// </summary>
        private static string BuildPreferenceSummary(CustomerRuntimeSettings settings)
        {
            List<string> parts = new List<string>();
            if (settings != null)
            {
                if (!settings.preferredThings.NullOrEmpty())
                {
                    parts.AddRange(settings.preferredThings
                        .Where(thingDef => thingDef != null)
                        .Take(3)
                        .Select(thingDef => thingDef.LabelCap.RawText));
                }

                if (!settings.preferredGoodsCategoryIds.NullOrEmpty())
                {
                    parts.AddRange(settings.preferredGoodsCategoryIds
                        .Select(id => GoodsCatalog.GetCategory(id)?.label)
                        .Where(label => !string.IsNullOrEmpty(label))
                        .Take(2));
                }
            }

            return parts.Count > 0
                ? string.Join(SimTranslation.T("RSMF.Common.ListSeparator"), parts)
                : SimTranslation.T("RSMF.CustomerInspect.PreferenceNone");
        }

        /// <summary>
        /// 追加开发诊断行，负责保留阶段、行为、浏览和停留信息但不污染玩家视图。
        /// </summary>
        private static void AppendDeveloperDiagnostics(StringBuilder sb, LordJob_CustomerVisit customerLord, Pawn pawn, CustomerVisitSession session)
        {
            if (sb == null || customerLord == null || pawn == null || session == null) return;

            string jobLabel = pawn.CurJobDef?.LabelCap.RawText ?? SimTranslation.T("RSMF.Common.None");
            string stageLabel = GetStageLabel(session.Stage);
            string stayTime = FormatDuration(Find.TickManager?.TicksGame - session.TotalVisitStartTick);
            sb.AppendLine(SimTranslation.T(
                "RSMF.CustomerInspect.DebugLine",
                stageLabel.Named("stage"),
                jobLabel.Named("job"),
                session.CurrentShopBrowseAttempts.Named("browse"),
                customerLord.GetCurrentShopBrowseLimit().Named("browseLimit"),
                stayTime.Named("time")));

            if (!string.IsNullOrEmpty(session.LastReason))
                sb.AppendLine(SimTranslation.T("RSMF.CustomerInspect.DebugReasonLine", session.LastReason.Named("reason")));
        }

        /// <summary>
        /// 把游戏时间长度转换成玩家可读文本，负责避免在检查面板显示内部计数单位。
        /// </summary>
        private static string FormatDuration(int? durationTicks)
        {
            int value = Mathf.Max(0, durationTicks ?? 0);
            if (value <= 0)
                return SimTranslation.T("RSMF.CustomerInspect.DurationJustArrived");
            if (value < GenDate.TicksPerHour)
                return SimTranslation.T("RSMF.CustomerInspect.DurationLessThanHour");

            int displayTicks = Mathf.CeilToInt(value / (float)GenDate.TicksPerHour) * GenDate.TicksPerHour;
            return displayTicks.ToStringTicksToPeriod(allowSeconds: false, shortForm: false, canUseDecimals: false, allowYears: false);
        }

        /// <summary>
        /// 返回顾客检查面板的经营状态文本，负责区分待付款和无消费离店。
        /// </summary>
        private static string GetCustomerQueueState(LordJob_CustomerVisit customerLord, int pawnId)
        {
            if (customerLord == null) return SimTranslation.T("RSMF.CustomerInspect.Browsing");
            Pawn pawn = customerLord.lord?.ownedPawns?.FirstOrDefault(item => item != null && item.thingIDNumber == pawnId);
            CustomerVisitSession session = pawn != null ? customerLord.GetOrCreateSession(pawn) : null;
            if (session != null)
            {
                if (session.Stage == CustomerVisitStage.WaitingCheckout) return customerLord.GetAmountOwedForCheckout(pawnId) > 0f ? SimTranslation.T("RSMF.CustomerInspect.ReadyForCheckout") : SimTranslation.T("RSMF.CustomerInspect.ReadyToLeave");
                if (session.Stage == CustomerVisitStage.Checkout) return SimTranslation.T("RSMF.CustomerInspect.Checkout");
                if (session.Stage == CustomerVisitStage.Leaving || session.Stage == CustomerVisitStage.Ended) return SimTranslation.T("RSMF.CustomerInspect.ReadyToLeave");
                return GetStageLabel(session.Stage);
            }
            if (!customerLord.IsPawnReadyForCheckout(pawnId)) return SimTranslation.T("RSMF.CustomerInspect.Browsing");
            return customerLord.GetAmountOwedForCheckout(pawnId) > 0f
                ? SimTranslation.T("RSMF.CustomerInspect.ReadyForCheckout")
                : SimTranslation.T("RSMF.CustomerInspect.ReadyToLeave");
        }

        /// <summary>
        /// 返回顾客阶段的本地化标签，负责隐藏内部枚举名称。
        /// </summary>
        private static string GetStageLabel(CustomerVisitStage stage)
        {
            switch (stage)
            {
                case CustomerVisitStage.Arriving:
                    return SimTranslation.T("RSMF.CustomerInspect.Arriving");
                case CustomerVisitStage.Browsing:
                    return SimTranslation.T("RSMF.CustomerInspect.Browsing");
                case CustomerVisitStage.SelectingService:
                    return SimTranslation.T("RSMF.CustomerInspect.SelectingService");
                case CustomerVisitStage.RunningExternalAction:
                    return SimTranslation.T("RSMF.CustomerInspect.RunningExternalAction");
                case CustomerVisitStage.WaitingCheckout:
                    return SimTranslation.T("RSMF.CustomerInspect.ReadyForCheckout");
                case CustomerVisitStage.Checkout:
                    return SimTranslation.T("RSMF.CustomerInspect.Checkout");
                case CustomerVisitStage.PostCheckout:
                    return SimTranslation.T("RSMF.CustomerInspect.PostCheckout");
                case CustomerVisitStage.Leaving:
                case CustomerVisitStage.Ended:
                    return SimTranslation.T("RSMF.CustomerInspect.ReadyToLeave");
                default:
                    return SimTranslation.T("RSMF.CustomerInspect.Browsing");
            }
        }
    }
}
