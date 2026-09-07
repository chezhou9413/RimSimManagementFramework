using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.Tool;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    public partial class Dialog_ShopManager
    {
        private Task<string> comboAiNameTask;
        private CancellationTokenSource comboAiNameCancellation;
        private ComboData comboAiNameTarget;
        private bool comboAiNameTaskHandled = true;

        /// <summary>
        /// 绘制套餐 AI 取名按钮，负责启动异步命名请求并展示配置状态提示。
        /// </summary>
        private void DrawComboAiNameButton(Rect rect)
        {
            bool configured = SimManagementLibMod.Settings?.HasValidLlmConfig() == true;
            bool hasItems = curCombo != null && !curCombo.items.NullOrEmpty();
            bool running = comboAiNameTask != null && !comboAiNameTask.IsCompleted;
            string label = running
                ? SimTranslation.TOrFallback("RSMF.ShopManager.AiNamingRunning", "AI取名中")
                : SimTranslation.TOrFallback("RSMF.ShopManager.AiNameCombo", "AI取名");
            bool enabled = configured && hasItems && !running;

            if (SimUiStyle.DrawSecondaryButton(rect, label, enabled, GameFont.Tiny))
                StartComboAiNameRequest(curCombo);

            if (Mouse.IsOver(rect))
            {
                string tip = configured
                    ? SimTranslation.TOrFallback("RSMF.ShopManager.AiNameComboTip", "根据当前套餐商品调用已配置的大模型生成名称。")
                    : SimTranslation.TOrFallback("RSMF.ShopManager.AiNameNeedsConfig", "需要先在模组设置中启用并配置通用 LLM。");
                if (!hasItems)
                    tip = SimTranslation.TOrFallback("RSMF.ShopManager.AiNameNeedsItems", "需要先给套餐选择至少一个商品。");
                TooltipHandler.TipRegion(rect, tip);
            }
        }

        /// <summary>
        /// 轮询套餐 AI 取名任务，负责在请求完成后把结果安全写回当前套餐。
        /// </summary>
        private void PollComboAiNameTask()
        {
            if (comboAiNameTask == null || !comboAiNameTask.IsCompleted || comboAiNameTaskHandled)
                return;

            comboAiNameTaskHandled = true;
            ComboData target = comboAiNameTarget;
            try
            {
                string generatedName = comboAiNameTask.Result;
                if (!string.IsNullOrWhiteSpace(generatedName) && target != null && zoneCombos != null && zoneCombos.Contains(target))
                {
                    target.comboName = generatedName;
                    Messages.Message(SimTranslation.TOrFallback("RSMF.ShopManager.AiNameSucceeded", "AI 已生成套餐名。"), MessageTypeDefOf.PositiveEvent, false);
                }
                else
                {
                    Messages.Message(SimTranslation.TOrFallback("RSMF.ShopManager.AiNameFailed", "AI 取名失败，请检查接口配置或稍后重试。"), MessageTypeDefOf.RejectInput, false);
                }
            }
            catch
            {
                Messages.Message(SimTranslation.TOrFallback("RSMF.ShopManager.AiNameFailed", "AI 取名失败，请检查接口配置或稍后重试。"), MessageTypeDefOf.RejectInput, false);
            }
            finally
            {
                comboAiNameTask = null;
                comboAiNameCancellation?.Dispose();
                comboAiNameCancellation = null;
                comboAiNameTarget = null;
            }
        }

        /// <summary>
        /// 启动套餐 AI 取名请求，负责取消旧请求并捕获当前套餐引用。
        /// </summary>
        private void StartComboAiNameRequest(ComboData combo)
        {
            if (combo == null || combo.items.NullOrEmpty())
                return;

            comboAiNameCancellation?.Cancel();
            comboAiNameCancellation?.Dispose();
            comboAiNameCancellation = new CancellationTokenSource();
            comboAiNameTarget = combo;
            comboAiNameTaskHandled = false;
            comboAiNameTask = ComboAiNameUtility.GenerateNameAsync(combo, shopZone, SimManagementLibMod.Settings, comboAiNameCancellation.Token);
        }

        /// <summary>
        /// 取消套餐 AI 取名请求，负责在窗口关闭时释放异步取消令牌。
        /// </summary>
        private void CancelComboAiNameRequest()
        {
            comboAiNameCancellation?.Cancel();
            comboAiNameCancellation?.Dispose();
            comboAiNameCancellation = null;
            comboAiNameTask = null;
            comboAiNameTarget = null;
            comboAiNameTaskHandled = true;
        }
    }
}
