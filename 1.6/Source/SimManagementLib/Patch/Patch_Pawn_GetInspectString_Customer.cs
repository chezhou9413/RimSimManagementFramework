using HarmonyLib;
using SimManagementLib.Pojo;
using SimManagementLib.SimAI;
using SimManagementLib.Tool;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace SimManagementLib.Patch
{
    [HarmonyPatch(typeof(Pawn), "GetInspectString")]
    public static class Patch_Pawn_GetInspectString_Customer
    {
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
            float spent = customerLord.GetAmountOwedForCheckout(pawnId);
            float remaining = budget - spent;
            int patience = customerLord.GetQueuePatienceForPawn(pawnId);
            float spentRatio = budget > 0 ? spent / Mathf.Max(1f, budget) : 0f;
            int spentPct = Mathf.Clamp(Mathf.RoundToInt(spentRatio * 100f), 0, 100);
            string jobLabel = __instance.CurJobDef?.LabelCap.RawText ?? SimTranslation.T("RSMF.Common.None");
            string queueState = GetCustomerQueueState(customerLord, pawnId);

            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(__result))
            {
                sb.Append(__result).AppendLine();
            }

            sb.AppendLine(SimTranslation.T("RSMF.CustomerInspect.Header"));
            sb.Append(SimTranslation.T("RSMF.CustomerInspect.ProfilePrefix")).AppendLine(settings?.profileLabel ?? customerLord.RuntimeCustomerKind?.label ?? customerLord.customerKind?.LabelCap ?? SimTranslation.T("RSMF.Common.Default"));
            sb.Append(SimTranslation.T("RSMF.CustomerInspect.StatePrefix")).Append(queueState).Append(SimTranslation.T("RSMF.CustomerInspect.CurrentJobPrefix")).AppendLine(jobLabel);
            sb.Append(SimTranslation.T(
                    "RSMF.CustomerInspect.BudgetLine",
                    budget.Named("budget"),
                    spent.ToString("F0").Named("spent"),
                    remaining.ToString("F0").Named("remaining"),
                    spentPct.Named("spentPct")))
                .AppendLine();
            sb.Append(SimTranslation.T("RSMF.CustomerInspect.QueuePatience", patience.Named("ticks"))).AppendLine();

            if (settings != null)
            {
                if (!settings.preferredThings.NullOrEmpty())
                {
                    sb.Append(SimTranslation.T("RSMF.CustomerInspect.PreferredThingsPrefix")).AppendLine(string.Join(SimTranslation.T("RSMF.Common.ListSeparator"), settings.preferredThings.Where(t => t != null).Take(6).Select(t => t.LabelCap.RawText)));
                }

                if (!settings.preferredGoodsCategoryIds.NullOrEmpty())
                {
                    sb.Append(SimTranslation.T("RSMF.CustomerInspect.PreferredCategoriesPrefix")).AppendLine(string.Join(SimTranslation.T("RSMF.Common.ListSeparator"), settings.preferredGoodsCategoryIds
                        .Select(id => GoodsCatalog.GetCategory(id)?.label)
                        .Where(label => !string.IsNullOrEmpty(label))
                        .Take(4)));
                }

                if (!settings.allowedWeathers.NullOrEmpty())
                {
                    sb.Append(SimTranslation.T("RSMF.CustomerInspect.AllowedWeathersPrefix")).AppendLine(string.Join(SimTranslation.T("RSMF.Common.ListSeparator"), settings.allowedWeathers.Where(w => w != null).Select(w => w.LabelCap.RawText)));
                }

                sb.Append(SimTranslation.T("RSMF.CustomerInspect.ActiveHoursPrefix")).Append(settings.activeHourRange.TrueMin.ToString("F1"))
                    .Append(" ~ ").AppendLine(settings.activeHourRange.TrueMax.ToString("F1"));
            }

            __result = sb.ToString().TrimEnd();
        }

        /// <summary>
        /// 返回顾客检查面板的经营状态文本，负责区分待付款和无消费离店。
        /// </summary>
        private static string GetCustomerQueueState(LordJob_CustomerVisit customerLord, int pawnId)
        {
            if (customerLord == null) return SimTranslation.T("RSMF.CustomerInspect.Browsing");
            if (!customerLord.readyForCheckout.Contains(pawnId)) return SimTranslation.T("RSMF.CustomerInspect.Browsing");
            return customerLord.GetAmountOwedForCheckout(pawnId) > 0f
                ? SimTranslation.T("RSMF.CustomerInspect.ReadyForCheckout")
                : SimTranslation.T("RSMF.CustomerInspect.ReadyToLeave");
        }
    }
}
