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
            float spent = customerLord.cartValues.TryGetValue(pawnId, out float v) ? v : 0f;
            float remaining = budget - spent;
            int patience = customerLord.GetQueuePatienceForPawn(pawnId);
            float spentRatio = budget > 0 ? spent / Mathf.Max(1f, budget) : 0f;
            int spentPct = Mathf.Clamp(Mathf.RoundToInt(spentRatio * 100f), 0, 100);
            string jobLabel = __instance.CurJobDef?.LabelCap.RawText ?? "无";
            string queueState = customerLord.readyForCheckout.Contains(pawnId) ? "已准备结账" : "浏览中";

            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(__result))
            {
                sb.Append(__result).AppendLine();
            }

            sb.AppendLine("=== 顾客面板 ===");
            sb.Append("档案: ").AppendLine(settings?.profileLabel ?? customerLord.RuntimeCustomerKind?.label ?? customerLord.customerKind?.LabelCap ?? "默认");
            sb.Append("状态: ").Append(queueState).Append(" / 当前行为: ").AppendLine(jobLabel);
            sb.Append("预算: ").Append(budget)
                .Append(" | 已花: ").Append(spent.ToString("F0"))
                .Append(" | 剩余: ").Append(remaining.ToString("F0"))
                .Append(" | 使用率: ").Append(spentPct).AppendLine("%");
            sb.Append("结账耐心: ").AppendLine(patience + " ticks");

            if (settings != null)
            {
                if (!settings.preferredThings.NullOrEmpty())
                {
                    sb.Append("偏好物品: ").AppendLine(string.Join("、", settings.preferredThings.Where(t => t != null).Take(6).Select(t => t.LabelCap.RawText)));
                }

                if (!settings.preferredGoodsCategoryIds.NullOrEmpty())
                {
                    sb.Append("偏好品类: ").AppendLine(string.Join("、", settings.preferredGoodsCategoryIds
                        .Select(id => GoodsCatalog.GetCategory(id)?.label)
                        .Where(label => !string.IsNullOrEmpty(label))
                        .Take(4)));
                }

                if (!settings.allowedWeathers.NullOrEmpty())
                {
                    sb.Append("出没天气: ").AppendLine(string.Join("、", settings.allowedWeathers.Where(w => w != null).Select(w => w.LabelCap.RawText)));
                }

                sb.Append("出没时间: ").Append(settings.activeHourRange.TrueMin.ToString("F1"))
                    .Append(" ~ ").AppendLine(settings.activeHourRange.TrueMax.ToString("F1"));
            }

            __result = sb.ToString().TrimEnd();
        }
    }
}
