using SimManagementLib.GameComp;
using SimManagementLib.Pojo;
using SimManagementLib.Tool;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    //顾问页优先保留聊天与输入空间，查询记录按需展开。
    public partial class MainTabWindow_BusinessManager
    {
        private const string AdvisorInputControl = "RSMF.BusinessAdvisor.Input";
        private string advisorInput = "";
        private readonly AdvisorTranscriptView advisorChat = new AdvisorTranscriptView();
        private readonly AdvisorTranscriptView advisorActivity = new AdvisorTranscriptView();
        private bool advisorShowActivity;
        private bool advisorFocusInput = true;

        //原版先处理确认键；顾问页接管它，避免输入框收到回车前窗口已经关闭。
        public override void OnAcceptKeyPressed()
        {
            if (CurrentBusinessPageOrNull()?.Worker is BusinessPageWorker_Advisor)
            {
                HandleAdvisorEnter();
                return;
            }
            base.OnAcceptKeyPressed();
        }

        //只在输入框内发送；Shift+Enter 交给文本框换行，审批不走键盘发送入口。
        private void HandleAdvisorEnter()
        {
            Event e = Event.current;
            if (e.type != EventType.KeyDown || (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter)
                || e.shift || GUI.GetNameOfFocusedControl() != AdvisorInputControl) return;
            e.Use();
            SendAdvisorInput(Current.Game.GetComponent<GameComponent_BusinessAdvisor>());
        }

        //按钮和回车共用发送逻辑，忙碌或待审批时保留输入草稿。
        private void SendAdvisorInput(GameComponent_BusinessAdvisor advisor)
        {
            if (advisor.Busy || advisor.HasPendingPlan || string.IsNullOrWhiteSpace(advisorInput)) return;
            if (SendAdvisorMessage(advisor, advisorInput.Trim())) advisorInput = "";
            advisorFocusInput = true;
        }

        //先固定底部输入区，再分配上方导航和剩余历史视口。
        internal void DrawBusinessAdvisor(Rect rect)
        {
            var oldFont = Text.Font;
            var oldAnchor = Text.Anchor;
            bool oldWrap = Text.WordWrap;
            Color oldColor = GUI.color;
            try
            {
                var advisor = Current.Game.GetComponent<GameComponent_BusinessAdvisor>();
                Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; Text.WordWrap = true; GUI.color = Color.white;
                float line = Text.LineHeight, control = Mathf.Max(32f, line + 10f);
                bool compact = rect.height < 500f;
                float inputHeight = line * (compact ? 2f : 3f) + 16f;
                float footerHeight = inputHeight + control + Text.LineHeightOf(GameFont.Tiny) + 22f;
                if (!advisor.HasPendingPlan && advisorMarkedMap != null) footerHeight += control + 6f;
                Rect footer = new Rect(rect.x, rect.yMax - footerHeight, rect.width, footerHeight);
                float y = DrawAdvisorHeader(rect, control);
                y = DrawAdvisorShortcuts(new Rect(rect.x, y, rect.width, control), advisor) + 8f;
                y = DrawAdvisorStatus(new Rect(rect.x, y, rect.width, control), advisor) + 8f;
                Rect history = new Rect(rect.x, y, rect.width, Mathf.Max(0f, footer.y - y - 10f));
                if (advisorShowActivity && rect.width >= 960f)
                {
                    float right = Mathf.Clamp(rect.width * 0.34f, 300f, 420f);
                    DrawAdvisorHistory(new Rect(history.x, history.y, history.width - right - 10f, history.height), advisor, false);
                    DrawAdvisorHistory(new Rect(history.xMax - right, history.y, right, history.height), advisor, true);
                }
                else DrawAdvisorHistory(history, advisor, advisorShowActivity);
                if (advisor.HasPendingPlan) DrawAdvisorApproval(footer, advisor, control);
                else
                {
                    advisorApprovalOwner = null;
                    advisorApprovalPreview = "";
                    DrawAdvisorComposer(footer, advisor, inputHeight, control);
                }
            }
            finally
            {
                Text.Font = oldFont; Text.Anchor = oldAnchor; Text.WordWrap = oldWrap; GUI.color = oldColor;
            }
        }

        //没有头像时不留下空槽，名称下直接显示实际模型及审批提示。
        private float DrawAdvisorHeader(Rect rect, float control)
        {
            var settings = SimManagementLibMod.Settings;
            float line = Text.LineHeight, y = rect.y;
            bool narrow = rect.width < 700f;
            Texture2D avatar = string.IsNullOrEmpty(settings.advisorAvatarId) ? null : SignTextureCache.GetTexture(settings.advisorAvatarId);
            float avatarWidth = avatar == null ? 0f : line * 2f + 12f;
            if (avatar != null) GUI.DrawTexture(new Rect(rect.x, y, avatarWidth - 8f, avatarWidth - 8f), avatar, ScaleMode.ScaleToFit);
            float titleWidth = rect.width - avatarWidth - (narrow ? 0f : 260f);
            Rect nameRect = new Rect(rect.x + avatarWidth, y, titleWidth, line + 4f);
            AdvisorLabel(nameRect, settings.advisorName);
            SimLlmProvider provider = settings.advisorSeparateConnection ? settings.advisorProvider : settings.llmProvider;
            string model = string.IsNullOrWhiteSpace(settings.advisorModel)
                ? (provider == SimLlmProvider.Anthropic ? settings.llmAnthropicModel : settings.llmOpenAiModel) : settings.advisorModel;
            GUI.color = CDim;
            AdvisorLabel(new Rect(nameRect.x, y + line + 4f, titleWidth, line + 4f), AdvisorText("CurrentModel") + model
                + " · " + AdvisorText(settings.advisorAutoApprove ? "AutoApprovalActive" : "ManualApprovalActive"));
            GUI.color = Color.white;
            float buttonY = narrow ? y + line * 2f + 12f : y + 4f;
            float buttonX = narrow ? rect.x : rect.xMax - 250f;
            if (SimUiStyle.DrawSecondaryButton(new Rect(buttonX, buttonY, 120f, control), AdvisorText("Settings")))
                Find.WindowStack.Add(new Dialog_CustomerReviewAiSettings(true));
            if (SimUiStyle.DrawSecondaryButton(new Rect(buttonX + 128f, buttonY, 122f, control), AdvisorText("Clear")))
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(AdvisorText("ClearConfirm"), () =>
                { Current.Game.GetComponent<GameComponent_BusinessAdvisor>().ClearConversation(); advisorInput = ""; advisorMarkedMap = null; advisorFocusInput = true; }, true));
            return narrow ? buttonY + control + 8f : y + Mathf.Max(line * 2f + 8f, control + 4f) + 10f;
        }

        //快捷问题与过程开关同排，过程栏默认收起以保留完整聊天宽度。
        private float DrawAdvisorShortcuts(Rect rect, GameComponent_BusinessAdvisor advisor)
        {
            float width = (rect.width - 18f) / 4f;
            bool available = !advisor.Busy && !advisor.HasPendingPlan;
            string[] labels = { "Summary", "Advice", "Diagnose" };
            for (int i = 0; i < labels.Length; i++)
            {
                if (SimUiStyle.DrawSecondaryButton(new Rect(rect.x + i * (width + 6f), rect.y, width, rect.height), AdvisorText(labels[i]), available))
                    SendAdvisorMessage(advisor, AdvisorText(labels[i] + "Prompt"));
            }
            string trace = AdvisorText(advisorShowActivity ? "HideTrace" : "Trace") + " (" + advisor.Activity.Count + ")";
            Rect traceButton = new Rect(rect.xMax - width, rect.y, width, rect.height);
            //页签控件选中后不再响应点击；展开开关始终使用可点击按钮。
            bool toggle = advisorShowActivity ? SimUiStyle.DrawPrimaryButton(traceButton, trace) : SimUiStyle.DrawSecondaryButton(traceButton, trace);
            if (toggle)
                advisorShowActivity = !advisorShowActivity;
            return rect.yMax;
        }

        //状态条只呈现短原因，完整接口说明放入可滚动详情窗口。
        private float DrawAdvisorStatus(Rect rect, GameComponent_BusinessAdvisor advisor)
        {
            Color accent = advisor.HasError ? new Color(0.9f, 0.42f, 0.36f) : advisor.HasPendingPlan ? CWarn : CAccent;
            Widgets.DrawBoxSolid(rect, new Color(accent.r, accent.g, accent.b, 0.09f));
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 3f, rect.height), accent);
            float detailWidth = advisor.HasError ? 100f : 0f;
            string status = string.IsNullOrWhiteSpace(advisor.Status) ? AdvisorText("Ready") : advisor.Status;
            AdvisorLabel(new Rect(rect.x + 12f, rect.y, rect.width - detailWidth - 24f, rect.height), status);
            if (advisor.HasError && SimUiStyle.DrawSecondaryButton(new Rect(rect.xMax - detailWidth, rect.y, detailWidth, rect.height), AdvisorText("ErrorDetails")))
                Find.WindowStack.Add(new Dialog_MessageBox(advisor.ErrorDetail, title: AdvisorText("ErrorDetails")));
            return rect.yMax;
        }

        //聊天和查询使用同样的有界视口，空过程栏也说明如何产生记录。
        private void DrawAdvisorHistory(Rect rect, GameComponent_BusinessAdvisor advisor, bool tools)
        {
            if (rect.height < Text.LineHeight + 24f) return;
            Widgets.DrawBoxSolid(rect, CPanel);
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.10f));
            var entries = tools ? advisor.Activity : advisor.Messages;
            float headerHeight = Text.LineHeight + 12f;
            AdvisorLabel(new Rect(rect.x + 10f, rect.y, rect.width - 20f, headerHeight),
                AdvisorText(tools ? "Trace" : "Conversation") + " · " + entries.Count);
            Rect body = new Rect(rect.x + 6f, rect.y + headerHeight, rect.width - 12f, rect.height - headerHeight - 6f);
            if (entries.Count == 0)
            {
                GUI.color = CDim;
                string empty = AdvisorText(tools ? "TraceEmpty" : "ChatEmpty");
                Rect inner = body.ContractedBy(10f);
                string visible = Text.CalcHeight(empty, inner.width) <= inner.height ? empty : AdvisorText(tools ? "TraceEmptyShort" : "ChatEmptyShort");
                if (inner.height >= Text.LineHeight) Widgets.Label(inner, visible);
                TooltipHandler.TipRegion(body, empty);
                GUI.color = Color.white;
            }
            else (tools ? advisorActivity : advisorChat).Draw(body, entries, tools);
        }

        //输入区锚定底部，等待审批时由方案卡占用同一区域并保留草稿。
        private void DrawAdvisorComposer(Rect rect, GameComponent_BusinessAdvisor advisor, float inputHeight, float control)
        {
            Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; Text.WordWrap = true;
            float inputY = rect.y;
            if (advisorMarkedMap != null)
            {
                DrawAdvisorMarkedArea(new Rect(rect.x, inputY, rect.width, control));
                inputY += control + 6f;
            }
            Rect input = new Rect(rect.x, inputY, rect.width, inputHeight);
            HandleAdvisorEnter();
            GUI.SetNextControlName(AdvisorInputControl);
            advisorInput = Widgets.TextArea(input, advisorInput);
            if (advisorInput.Length > 8000) advisorInput = advisorInput.Substring(0, 8000);
            if (advisorFocusInput && Event.current.type == EventType.Repaint)
            { GUI.FocusControl(AdvisorInputControl); advisorFocusInput = false; }
            TooltipHandler.TipRegion(input, AdvisorText("InputHint"));
            float y = input.yMax + 6f;
            float right = rect.xMax - 100f;
            bool available = !advisor.Busy && !advisor.HasPendingPlan;
            if (SimUiStyle.DrawPrimaryButton(new Rect(right, y, 100f, control), AdvisorText("Send"), available && !string.IsNullOrWhiteSpace(advisorInput)))
                SendAdvisorInput(advisor);
            if (advisor.Busy)
            {
                right -= 118f;
                if (SimUiStyle.DrawSecondaryButton(new Rect(right, y, 110f, control), AdvisorText("Stop"))) advisor.Cancel();
            }
            float markWidth = Mathf.Min(150f, (rect.width - 24f) / 3f);
            Rect markButton = new Rect(rect.x, y, markWidth, control);
            if (SimUiStyle.DrawSecondaryButton(markButton, AdvisorText("MarkArea"), available && Find.CurrentMap != null))
            {
                StartAdvisorAreaSelection();
                return;
            }
            TooltipHandler.TipRegion(markButton, AdvisorText("MarkAreaHint"));
            GUI.color = CDim;
            float hintX = markButton.xMax + 10f;
            if (right - hintX > 120f) AdvisorLabel(new Rect(hintX, y, right - hintX - 10f, control), AdvisorText("InputHint"));
            Text.Font = GameFont.Tiny;
            Rect usageRect = new Rect(rect.x, y + control + 6f, rect.width, Text.LineHeight + 2f);
            string usage = string.IsNullOrWhiteSpace(advisor.Usage)
                ? AdvisorText(SimManagementLibMod.Settings.advisorAutoApprove ? "AutoReadOnlyHint" : "ReadOnlyHint") : advisor.Usage;
            AdvisorLabel(usageRect, usage, AdvisorText("CacheHint"));
            Text.Font = GameFont.Small; GUI.color = Color.white;
        }

        //单行信息居中对齐，截断只影响显示，悬停仍能查看原文。
        private static void AdvisorLabel(Rect rect, string text, string hint = null)
        {
            var anchor = Text.Anchor;
            bool wrap = Text.WordWrap;
            Text.Anchor = TextAnchor.MiddleLeft; Text.WordWrap = false;
            string oneLine = (text ?? "").Replace("\r", "").Replace("\n", " · ");
            Widgets.Label(rect, oneLine.Truncate(rect.width));
            TooltipHandler.TipRegion(rect, hint == null ? text : text + "\n\n" + hint);
            Text.Anchor = anchor; Text.WordWrap = wrap;
        }

        //复用顾问页翻译前缀。
        private static string AdvisorText(string key) => SimTranslation.T("RSMF.Business.Advisor." + key);
    }
}
