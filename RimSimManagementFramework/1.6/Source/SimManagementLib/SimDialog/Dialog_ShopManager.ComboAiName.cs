using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.Tool;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    //管理套餐自动命名操作，职责是提交命名请求并显示执行结果。
    public partial class Dialog_ShopManager
    {
        private Task<string> comboAiNameTask;
        private CancellationTokenSource comboAiNameCancellation;
        private ComboData comboAiNameTarget;
        private bool comboAiNameTaskHandled = true;
        //绘制套餐 AI 取名按钮，负责启动异步命名请求并展示配置状态提示。
        private void DrawComboAiNameButton(Rect rect)
        {
            bool configured = SimManagementLibMod.Settings?.HasValidLlmConfig() == true;
            bool hasItems = curCombo != null && !curCombo.items.NullOrEmpty();
            bool running = comboAiNameTask != null && !comboAiNameTask.IsCompleted;
            string label = running
                ? SimTranslation.T("RSMF.ShopManager.AiNamingRunning")
                : SimTranslation.T("RSMF.ShopManager.AiNameCombo");
            bool enabled = configured && hasItems && !running;

            if (SimUiStyle.DrawSecondaryButton(rect, label, enabled, GameFont.Tiny))
                StartComboAiNameRequest(curCombo);

            if (Mouse.IsOver(rect))
            {
                string tip = configured
                    ? SimTranslation.T("RSMF.ShopManager.AiNameComboTip")
                    : SimTranslation.T("RSMF.ShopManager.AiNameNeedsConfig");
                if (!hasItems)
                    tip = SimTranslation.T("RSMF.ShopManager.AiNameNeedsItems");
                TooltipHandler.TipRegion(rect, tip);
            }
        }
        //轮询套餐 AI 取名任务，负责在请求完成后把结果安全写回当前套餐。
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
                    Messages.Message(SimTranslation.T("RSMF.ShopManager.AiNameSucceeded"), MessageTypeDefOf.PositiveEvent, false);
                }
                else
                {
                    Messages.Message(SimTranslation.T("RSMF.ShopManager.AiNameFailed"), MessageTypeDefOf.RejectInput, false);
                }
            }
            catch
            {
                Messages.Message(SimTranslation.T("RSMF.ShopManager.AiNameFailed"), MessageTypeDefOf.RejectInput, false);
            }
            finally
            {
                comboAiNameTask = null;
                comboAiNameCancellation?.Dispose();
                comboAiNameCancellation = null;
                comboAiNameTarget = null;
            }
        }
        //启动套餐 AI 取名请求，负责取消旧请求并捕获当前套餐引用。
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
        //取消套餐 AI 取名请求，负责在窗口关闭时释放异步取消令牌。
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
