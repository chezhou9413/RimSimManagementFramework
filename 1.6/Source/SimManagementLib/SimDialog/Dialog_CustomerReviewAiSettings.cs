using SimManagementLib.Pojo;
using SimManagementLib.Tool;
using RimWorld;
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    /// <summary>
    /// 绘制顾客 AI 点评配置窗口，负责接口、提示词和词库设置。
    /// </summary>
    public partial class Dialog_CustomerReviewAiSettings : Window
    {
        private int tabIndex;
        private Vector2 scrollPos;
        private string connectionStatus = "";
        private bool testingApi;
        private Task<CustomerReviewConnectionTestResult> apiTestTask;
        private bool testingBaseUrl;
        private Task<CustomerReviewConnectionTestResult> baseUrlTestTask;

        public override Vector2 InitialSize => new Vector2(1040f, 760f);

        /// <summary>
        /// 初始化顾客 AI 点评配置窗口的基础行为。
        /// </summary>
        public Dialog_CustomerReviewAiSettings()
        {
            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = false;
            draggable = true;
            resizeable = true;
        }

        /// <summary>
        /// 关闭设置窗口前保存配置，负责避免接口配置页每帧写盘造成卡顿。
        /// </summary>
        public override void PreClose()
        {
            base.PreClose();
            SimManagementLibMod.Settings?.Write();
        }

        /// <summary>
        /// 绘制窗口主体内容。
        /// </summary>
        public override void DoWindowContents(Rect inRect)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWordWrap = Text.WordWrap;
            Color oldColor = GUI.color;

            try
            {
                SimManagementLibSettings settings = SimManagementLibMod.Settings;
                if (settings == null) return;
                PollConnectionTests();

                float titleH = Mathf.Max(34f, Text.LineHeightOf(GameFont.Medium) + 8f);
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = Color.white;
                Widgets.Label(new Rect(inRect.x, inRect.y, Mathf.Max(0f, inRect.width), titleH), SimTranslation.T("RSMF.ReviewSettings.Title"));
                ResetText();

                Rect tabRect = new Rect(inRect.x, inRect.y + titleH + 6f, Mathf.Max(0f, inRect.width), 34f);
                DrawTabs(tabRect);

                float bottomH = 38f;
                Rect bottomRect = new Rect(inRect.x, inRect.yMax - bottomH, Mathf.Max(0f, inRect.width), bottomH);
                Rect bodyRect = new Rect(inRect.x, tabRect.yMax + 8f, Mathf.Max(0f, inRect.width), Mathf.Max(80f, bottomRect.y - tabRect.yMax - 14f));
                Widgets.DrawBoxSolid(bodyRect, new Color(0f, 0f, 0f, 0.18f));
                SimUiStyle.DrawBorder(bodyRect, new Color(1f, 1f, 1f, 0.12f));
                DrawSelectedPage(bodyRect.ContractedBy(10f), settings);

                DrawBottomButtons(bottomRect, settings);
            }
            finally
            {
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
                Text.WordWrap = oldWordWrap;
                GUI.color = oldColor;
            }
        }

        private void DrawTabs(Rect rect)
        {
            float w = 120f;
            string[] labels =
            {
                SimTranslation.T("RSMF.ReviewSettings.Tab.Api"),
                SimTranslation.T("RSMF.ReviewSettings.Tab.Prompt"),
                SimTranslation.T("RSMF.ReviewSettings.Tab.Injector"),
                SimTranslation.T("RSMF.ReviewSettings.Tab.Lexicon")
            };
            for (int i = 0; i < labels.Length; i++)
            {
                Rect tab = new Rect(rect.x + i * (w + 8f), rect.y, w, rect.height);
                if (SimUiStyle.DrawTabButton(tab, labels[i], tabIndex == i, new Color(0.72f, 0.72f, 0.72f, 1f)))
                {
                    tabIndex = i;
                    scrollPos = Vector2.zero;
                }
            }
        }

        /// <summary>
        /// 绘制当前设置页签内容，负责给接口页预留足够滚动高度。
        /// </summary>
        private void DrawSelectedPage(Rect rect, SimManagementLibSettings settings)
        {
            float viewWidth = Mathf.Max(120f, rect.width - 18f);
            float viewHeight = tabIndex == 0 ? 1080f : (tabIndex == 2 ? CalcInjectorContentHeight() : 760f);
            if (tabIndex == 2)
                HandleInjectorNestedScrollWheel(rect, viewWidth, settings);
            Rect viewRect = new Rect(0f, 0f, viewWidth, viewHeight);
            Widgets.BeginScrollView(rect, ref scrollPos, viewRect);
            if (tabIndex == 0) DrawApiPage(viewRect, settings);
            else if (tabIndex == 1) DrawPromptPage(viewRect, settings);
            else if (tabIndex == 2) DrawInjectorPage(viewRect, settings);
            else DrawLexiconPage(viewRect, settings);
            Widgets.EndScrollView();
        }

        /// <summary>
        /// 绘制接口配置页，负责供应商、生成概率、论坛互动和上下文参数的设置。
        /// </summary>
        private void DrawApiPage(Rect rect, SimManagementLibSettings settings)
        {
            float y = 0f;
            DrawCheckbox(new Rect(0f, y, rect.width, 28f), SimTranslation.T("RSMF.ReviewSettings.EnableReviews"), ref settings.reviewAiEnabled, SimTranslation.T("RSMF.ReviewSettings.EnableReviewsTip"));
            y += 36f;
            DrawRimTalkImportRow(rect.width, ref y, settings);

            Rect providerRect = new Rect(0f, y, 260f, 30f);
            if (SimUiStyle.DrawSecondaryButton(providerRect, SimTranslation.T("RSMF.ReviewSettings.Provider", settings.reviewProvider.Named("provider")), true, GameFont.Small))
            {
                Find.WindowStack.Add(new FloatMenu(new System.Collections.Generic.List<FloatMenuOption>
                {
                    new FloatMenuOption(SimTranslation.T("RSMF.ReviewSettings.Provider.OpenAICompatible"), () => settings.reviewProvider = CustomerReviewProvider.OpenAICompatible),
                    new FloatMenuOption("Anthropic", () => settings.reviewProvider = CustomerReviewProvider.Anthropic)
                }));
            }
            y += 40f;

            if (settings.reviewProvider == CustomerReviewProvider.Anthropic)
            {
                DrawTextField(rect.width, ref y, "Anthropic API Key", ref settings.anthropicApiKey, true);
                DrawTextField(rect.width, ref y, "Anthropic Model", ref settings.anthropicModel, false);
            }
            else
            {
                DrawTextField(rect.width, ref y, SimTranslation.T("RSMF.ReviewSettings.OpenAIBaseUrl"), ref settings.openAiBaseUrl, false);
                DrawTextField(rect.width, ref y, SimTranslation.T("RSMF.ReviewSettings.OpenAIApiKey"), ref settings.openAiApiKey, true);
                DrawTextField(rect.width, ref y, SimTranslation.T("RSMF.ReviewSettings.OpenAIModel"), ref settings.openAiModel, false);
            }
            y += 8f;

            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            Widgets.Label(new Rect(0f, y, rect.width, 24f), SimTranslation.T("RSMF.ReviewSettings.SampleRate", (settings.reviewSampleRate * 100f).ToString("F0").Named("percent")));
            y += 24f;
            settings.reviewSampleRate = Widgets.HorizontalSlider(new Rect(0f, y, 360f, 24f), settings.reviewSampleRate, 0f, 1f, true);
            y += 34f;
            Widgets.Label(new Rect(0f, y, rect.width, 24f), SimTranslation.T("RSMF.ReviewSettings.RequestsPerMinute", settings.reviewRequestsPerMinute.Named("count")));
            y += 24f;
            settings.reviewRequestsPerMinute = Mathf.RoundToInt(Widgets.HorizontalSlider(new Rect(0f, y, 360f, 24f), settings.reviewRequestsPerMinute, 1f, 60f, true));
            y += 34f;
            Widgets.Label(new Rect(0f, y, rect.width, 24f), SimTranslation.T("RSMF.ReviewSettings.RequestTimeout", settings.reviewRequestTimeoutSeconds.Named("seconds")));
            y += 24f;
            settings.reviewRequestTimeoutSeconds = Mathf.RoundToInt(Widgets.HorizontalSlider(new Rect(0f, y, 360f, 24f), settings.reviewRequestTimeoutSeconds, 20f, 180f, true));
            y += 34f;
            Widgets.Label(new Rect(0f, y, rect.width, 24f), SimTranslation.T("RSMF.ReviewSettings.MaxReviewRecords", settings.maxReviewRecords.Named("count")));
            y += 24f;
            settings.maxReviewRecords = Mathf.RoundToInt(Widgets.HorizontalSlider(new Rect(0f, y, 360f, 24f), settings.maxReviewRecords, 50f, 10000f, true));
            y += 28f;
            bool oldWrapForLimit = Text.WordWrap;
            Text.WordWrap = true;
            GUI.color = new Color(0.72f, 0.76f, 0.82f, 1f);
            Widgets.Label(new Rect(0f, y, rect.width - 10f, Mathf.Max(Text.LineHeightOf(GameFont.Tiny), 22f)), SimTranslation.T("RSMF.ReviewSettings.MaxReviewRecordsTip"));
            Text.WordWrap = oldWrapForLimit;
            y += 30f;
            Widgets.Label(new Rect(0f, y, rect.width, 24f), SimTranslation.T("RSMF.ReviewSettings.Temperature", settings.reviewTemperature.ToString("F2").Named("value")));
            y += 24f;
            settings.reviewTemperature = Widgets.HorizontalSlider(new Rect(0f, y, 360f, 24f), settings.reviewTemperature, 0.1f, 2f, true);
            y += 34f;
            Widgets.Label(new Rect(0f, y, rect.width, 24f), SimTranslation.T("RSMF.ReviewSettings.ReactionChance", (settings.reviewForumReactionChance * 100f).ToString("F0").Named("percent")));
            y += 24f;
            settings.reviewForumReactionChance = Widgets.HorizontalSlider(new Rect(0f, y, 360f, 24f), settings.reviewForumReactionChance, 0f, 1f, true);
            y += 34f;
            Widgets.Label(new Rect(0f, y, rect.width, 24f), SimTranslation.T("RSMF.ReviewSettings.ReplyChance", (settings.reviewForumReplyChance * 100f).ToString("F0").Named("percent")));
            y += 24f;
            settings.reviewForumReplyChance = Widgets.HorizontalSlider(new Rect(0f, y, 360f, 24f), settings.reviewForumReplyChance, 0f, 1f, true);
            y += 28f;
            bool oldWrapForForum = Text.WordWrap;
            Text.WordWrap = true;
            GUI.color = new Color(0.72f, 0.76f, 0.82f, 1f);
            Widgets.Label(new Rect(0f, y, rect.width - 10f, 44f), SimTranslation.T("RSMF.ReviewSettings.ReplyChanceTip"));
            Text.WordWrap = oldWrapForForum;
            y += 52f;
            DrawCheckbox(new Rect(0f, y, rect.width, 28f), SimTranslation.T("RSMF.ReviewSettings.AbsurdNitpick"), ref settings.reviewAbsurdNitpickEnabled, SimTranslation.T("RSMF.ReviewSettings.AbsurdNitpickTip"));
            y += 30f;
            if (settings.reviewAbsurdNitpickEnabled)
            {
                Widgets.Label(new Rect(0f, y, rect.width, 24f), SimTranslation.T("RSMF.ReviewSettings.AbsurdNitpickChance", (settings.reviewAbsurdNitpickChance * 100f).ToString("F0").Named("percent")));
                y += 24f;
                settings.reviewAbsurdNitpickChance = Widgets.HorizontalSlider(new Rect(0f, y, 360f, 24f), settings.reviewAbsurdNitpickChance, 0f, 1f, true);
                y += 28f;
                bool oldWrapForAbsurd = Text.WordWrap;
                Text.WordWrap = true;
                GUI.color = new Color(0.72f, 0.76f, 0.82f, 1f);
                Widgets.Label(new Rect(0f, y, rect.width - 10f, 44f), SimTranslation.T("RSMF.ReviewSettings.AbsurdNitpickChanceTip"));
                Text.WordWrap = oldWrapForAbsurd;
                y += 52f;
            }
            Widgets.Label(new Rect(0f, y, rect.width, 24f), SimTranslation.T("RSMF.ReviewSettings.ContextBudget", settings.reviewConversationContextMaxChars.Named("chars")));
            y += 24f;
            settings.reviewConversationContextMaxChars = Mathf.RoundToInt(Widgets.HorizontalSlider(new Rect(0f, y, 360f, 24f), settings.reviewConversationContextMaxChars, 0f, 64000f, true));
            y += 28f;
            bool oldWordWrap = Text.WordWrap;
            Text.WordWrap = true;
            GUI.color = new Color(0.72f, 0.76f, 0.82f, 1f);
            Widgets.Label(new Rect(0f, y, rect.width - 10f, 44f), SimTranslation.T("RSMF.ReviewSettings.ContextBudgetTip"));
            Text.WordWrap = oldWordWrap;
            ResetText();
        }

        private void DrawPromptPage(Rect rect, SimManagementLibSettings settings)
        {
            float y = 0f;
            DrawTextArea(rect.width, ref y, SimTranslation.T("RSMF.ReviewSettings.SystemPrompt"), ref settings.reviewSystemPrompt, 150f);
            DrawTextArea(rect.width, ref y, SimTranslation.T("RSMF.ReviewSettings.UserPromptTemplate"), ref settings.reviewUserPrompt, 180f);
        }

        private void DrawLexiconPage(Rect rect, SimManagementLibSettings settings)
        {
            float y = 0f;
            DrawTextArea(rect.width, ref y, SimTranslation.T("RSMF.ReviewSettings.NicknameStyleA"), ref settings.reviewNicknamePrefixes, 95f);
            DrawTextArea(rect.width, ref y, SimTranslation.T("RSMF.ReviewSettings.NicknameStyleB"), ref settings.reviewNicknameSuffixes, 95f);
            DrawTextArea(rect.width, ref y, SimTranslation.T("RSMF.ReviewSettings.ToneWords"), ref settings.reviewToneWords, 95f);
            DrawTextArea(rect.width, ref y, SimTranslation.T("RSMF.ReviewSettings.PositiveWords"), ref settings.reviewPositiveWords, 95f);
            DrawTextArea(rect.width, ref y, SimTranslation.T("RSMF.ReviewSettings.NegativeWords"), ref settings.reviewNegativeWords, 95f);
            DrawTextArea(rect.width, ref y, SimTranslation.T("RSMF.ReviewSettings.BannedWords"), ref settings.reviewBannedWords, 95f);
        }

        private void DrawBottomButtons(Rect rect, SimManagementLibSettings settings)
        {
            bool canTestBaseUrl = settings.reviewProvider == CustomerReviewProvider.Anthropic || !string.IsNullOrWhiteSpace(settings.openAiBaseUrl);
            bool canTestApi = settings.HasReviewAiConnectionFields();
            float buttonH = Mathf.Min(34f, rect.height);
            float buttonY = rect.y + (rect.height - buttonH) * 0.5f;
            float buttonW = Mathf.Min(118f, Mathf.Max(92f, (rect.width - 42f) / 5f));
            Rect baseUrlRect = new Rect(rect.x, buttonY, buttonW, buttonH);
            if (SimUiStyle.DrawSecondaryButton(baseUrlRect, testingBaseUrl ? SimTranslation.T("RSMF.ReviewSettings.Probing") : SimTranslation.T("RSMF.ReviewSettings.TestBaseUrl"), canTestBaseUrl && !testingBaseUrl, GameFont.Small))
            {
                StartBaseUrlTest(settings);
            }

            Rect apiRect = new Rect(baseUrlRect.xMax + 8f, buttonY, buttonW, buttonH);
            if (SimUiStyle.DrawPrimaryButton(apiRect, testingApi ? SimTranslation.T("RSMF.ReviewSettings.Generating") : SimTranslation.T("RSMF.ReviewSettings.TestRequest"), canTestApi && !testingApi, GameFont.Small))
            {
                StartApiTest(settings);
            }

            Rect resetRect = new Rect(apiRect.xMax + 8f, buttonY, Mathf.Min(140f, buttonW + 12f), buttonH);
            if (SimUiStyle.DrawSecondaryButton(resetRect, SimTranslation.T("RSMF.ReviewSettings.ResetDefaultPrompt"), true, GameFont.Small))
            {
                CustomerReviewPromptDefaults.Reset(settings);
            }

            Rect terminalRect = new Rect(resetRect.xMax + 8f, buttonY, Mathf.Min(126f, buttonW + 8f), buttonH);
            if (SimUiStyle.DrawSecondaryButton(terminalRect, SimTranslation.T("RSMF.ReviewSettings.DebugTerminal"), true, GameFont.Small))
            {
                Find.WindowStack.Add(new Dialog_CustomerReviewAiTerminal());
            }

            float statusX = terminalRect.xMax + 10f;
            float statusW = Mathf.Max(0f, rect.xMax - statusX);
            if (statusW > 80f)
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = new Color(0.72f, 0.76f, 0.82f, 1f);
                Widgets.Label(new Rect(statusX, rect.y, statusW, rect.height), connectionStatus);
            }
            ResetText();
        }

        /// <summary>
        /// 绘制 RimTalk 配置导入行，负责让玩家一键复用 RimTalk 当前有效 API 和模型设置。
        /// </summary>
        private void DrawRimTalkImportRow(float width, ref float y, SimManagementLibSettings settings)
        {
            bool loaded = RimTalkConfigBridge.IsRimTalkLoaded();
            float buttonW = Mathf.Min(260f, Mathf.Max(190f, width * 0.34f));
            float rowH = 34f;
            Rect buttonRect = new Rect(0f, y, buttonW, rowH);
            if (SimUiStyle.DrawSecondaryButton(buttonRect, SimTranslation.T("RSMF.ReviewSettings.ImportRimTalkConfig"), loaded, GameFont.Small))
            {
                bool ok = RimTalkConfigBridge.TryApplyTo(settings, out string message);
                connectionStatus = message;
                Messages.Message(message, ok ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.RejectInput, false);
            }

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.WordWrap = true;
            GUI.color = loaded ? new Color(0.72f, 0.86f, 0.72f, 1f) : new Color(0.72f, 0.76f, 0.82f, 1f);
            string status = loaded ? SimTranslation.T("RSMF.ReviewSettings.RimTalkDetected") : SimTranslation.T("RSMF.ReviewSettings.RimTalkNotDetected");
            Rect statusRect = new Rect(buttonRect.xMax + 10f, y, Mathf.Max(0f, width - buttonW - 10f), rowH);
            Widgets.Label(statusRect, status);
            ResetText();
            y += rowH + 10f;
        }

        /// <summary>
        /// 启动 BaseUrl 联通性测试，负责只探测玩家填写的接口地址是否能建立 HTTP 连接。
        /// </summary>
        private void StartBaseUrlTest(SimManagementLibSettings settings)
        {
            testingBaseUrl = true;
            connectionStatus = SimTranslation.T("RSMF.ReviewSettings.Status.TestingBaseUrl");
            baseUrlTestTask = CustomerReviewAiClient.TestBaseUrlAsync(CopySettingsForTest(settings), CancellationToken.None);
        }

        /// <summary>
        /// 启动 API 生成测试，负责验证模型、密钥和响应解析是否可用。
        /// </summary>
        private void StartApiTest(SimManagementLibSettings settings)
        {
            testingApi = true;
            connectionStatus = SimTranslation.T("RSMF.ReviewSettings.Status.TestingApi");
            apiTestTask = CustomerReviewAiClient.TestConnectionDetailedAsync(CopySettingsForTest(settings), CancellationToken.None);
        }

        /// <summary>
        /// 复制设置给后台测试任务，负责避免后台线程读取正在被 UI 修改的设置对象。
        /// </summary>
        private SimManagementLibSettings CopySettingsForTest(SimManagementLibSettings settings)
        {
            SimManagementLibSettings copy = new SimManagementLibSettings();
            copy.reviewAiEnabled = settings.reviewAiEnabled;
            copy.reviewProvider = settings.reviewProvider;
            copy.openAiBaseUrl = settings.openAiBaseUrl;
            copy.openAiApiKey = settings.openAiApiKey;
            copy.openAiModel = settings.openAiModel;
            copy.anthropicApiKey = settings.anthropicApiKey;
            copy.anthropicModel = settings.anthropicModel;
            copy.reviewTemperature = settings.reviewTemperature;
            copy.reviewRequestTimeoutSeconds = settings.reviewRequestTimeoutSeconds;
            copy.reviewForumReactionChance = settings.reviewForumReactionChance;
            copy.reviewForumReplyChance = settings.reviewForumReplyChance;
            copy.reviewAbsurdNitpickEnabled = settings.reviewAbsurdNitpickEnabled;
            copy.reviewAbsurdNitpickChance = settings.reviewAbsurdNitpickChance;
            copy.reviewSystemPrompt = settings.reviewSystemPrompt;
            copy.reviewUserPrompt = settings.reviewUserPrompt;
            copy.reviewNicknamePrefixes = settings.reviewNicknamePrefixes;
            copy.reviewNicknameSuffixes = settings.reviewNicknameSuffixes;
            copy.reviewToneWords = settings.reviewToneWords;
            copy.reviewPositiveWords = settings.reviewPositiveWords;
            copy.reviewNegativeWords = settings.reviewNegativeWords;
            copy.reviewBannedWords = settings.reviewBannedWords;
            copy.reviewPromptInputFormat = settings.reviewPromptInputFormat;
            copy.reviewPromptEnabledNodeIds = settings.reviewPromptEnabledNodeIds;
            copy.reviewPromptNodeOrder = settings.reviewPromptNodeOrder;
            copy.reviewPromptCustomNodes = settings.reviewPromptCustomNodes;
            copy.reviewConversationContextMaxChars = 0;
            copy.reviewAiEnabled = true;
            copy.SanitizeReviewSettingsText();
            return copy;
        }

        /// <summary>
        /// 轮询后台测试结果，负责把完成状态显示回窗口底部。
        /// </summary>
        private void PollConnectionTests()
        {
            if (testingBaseUrl && baseUrlTestTask != null && baseUrlTestTask.IsCompleted)
            {
                testingBaseUrl = false;
                connectionStatus = FormatBaseUrlResult(baseUrlTestTask);
                baseUrlTestTask = null;
            }

            if (testingApi && apiTestTask != null && apiTestTask.IsCompleted)
            {
                testingApi = false;
                connectionStatus = FormatApiResult(apiTestTask);
                apiTestTask = null;
            }
        }

        /// <summary>
        /// 格式化 BaseUrl 测试结果，负责生成适合底栏显示的短文本。
        /// </summary>
        private static string FormatBaseUrlResult(Task<CustomerReviewConnectionTestResult> task)
        {
            if (task.Status != TaskStatus.RanToCompletion || task.Result == null)
                return SimTranslation.T("RSMF.ReviewSettings.Result.BaseUrlFailed");

            CustomerReviewConnectionTestResult result = task.Result;
            return result.baseUrlReachable ? SimTranslation.T("RSMF.ReviewSettings.Result.BaseUrlReachable", result.statusCode.Named("statusCode")) : result.message;
        }

        /// <summary>
        /// 格式化 API 测试结果，负责提示玩家区分地址、密钥、模型和 JSON 解析问题。
        /// </summary>
        private static string FormatApiResult(Task<CustomerReviewConnectionTestResult> task)
        {
            if (task.Status != TaskStatus.RanToCompletion || task.Result == null)
                return SimTranslation.T("RSMF.ReviewSettings.Result.RequestFailed");

            CustomerReviewConnectionTestResult result = task.Result;
            if (result.apiReachable)
                return SimTranslation.T("RSMF.ReviewSettings.Result.RequestSucceeded");

            if (!result.baseUrlReachable)
                return result.message;

            return SimTranslation.T("RSMF.ReviewSettings.Result.GenerationFailed");
        }

        private static void DrawTextField(float width, ref float y, string label, ref string value, bool secret)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.72f, 0.76f, 0.82f, 1f);
            Widgets.Label(new Rect(0f, y, width, 20f), label);
            y += 20f;
            GUI.color = Color.white;
            string current = value ?? "";
            if (secret)
                current = Widgets.TextField(new Rect(0f, y, width - 10f, 30f), current);
            else
                current = Widgets.TextField(new Rect(0f, y, width - 10f, 30f), current);
            value = current;
            y += 40f;
            ResetText();
        }

        private static void DrawTextArea(float width, ref float y, string label, ref string value, float height)
        {
            GameFont oldFont = Text.Font;
            bool oldWordWrap = Text.WordWrap;
            Color oldColor = GUI.color;

            try
            {
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.72f, 0.76f, 0.82f, 1f);
            Widgets.Label(new Rect(0f, y, width, 22f), label);
            y += 22f;
            GUI.color = Color.white;
            Text.WordWrap = true;
            value = Widgets.TextArea(new Rect(0f, y, width - 10f, height), value ?? "");
            y += height + 12f;
            }
            finally
            {
                Text.Font = oldFont;
                Text.WordWrap = oldWordWrap;
                GUI.color = oldColor;
            }
        }

        private static void DrawCheckbox(Rect rect, string label, ref bool value, string tooltip)
        {
            Widgets.CheckboxLabeled(rect, label, ref value);
            if (!string.IsNullOrEmpty(tooltip)) TooltipHandler.TipRegion(rect, tooltip);
        }

        private static void ResetText()
        {
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            Text.WordWrap = true;
            GUI.color = Color.white;
        }
    }
}
