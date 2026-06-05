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
    /// 绘制通用 LLM 设置窗口，负责统一接口配置和各类 AI 功能子面板入口。
    /// </summary>
    public partial class Dialog_CustomerReviewAiSettings : Window
    {
        private int tabIndex;
        private int reviewTabIndex;
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
                Widgets.Label(new Rect(inRect.x, inRect.y, Mathf.Max(0f, inRect.width), titleH), SimTranslation.TOrFallback("RSMF.LlmSettings.Title", "通用 LLM 设置"));
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
            float w = 132f;
            string[] labels =
            {
                SimTranslation.TOrFallback("RSMF.LlmSettings.Tab.Api", "LLM 设置"),
                SimTranslation.TOrFallback("RSMF.LlmSettings.Tab.Reviews", "顾客评价")
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
            if (tabIndex == 0)
            {
                Rect viewRect = new Rect(0f, 0f, viewWidth, 420f);
                Widgets.BeginScrollView(rect, ref scrollPos, viewRect);
                DrawLlmApiPage(viewRect, settings);
                Widgets.EndScrollView();
                return;
            }

            Rect reviewTabsRect = new Rect(rect.x, rect.y, rect.width, 34f);
            DrawReviewSubTabs(reviewTabsRect);

            Rect scrollRect = new Rect(rect.x, reviewTabsRect.yMax + 8f, rect.width, Mathf.Max(80f, rect.yMax - reviewTabsRect.yMax - 8f));
            float viewHeight = reviewTabIndex == 0 ? 860f : (reviewTabIndex == 2 ? CalcInjectorContentHeight() : 760f);
            if (reviewTabIndex == 2)
                HandleInjectorNestedScrollWheel(scrollRect, viewWidth, settings);
            Rect reviewViewRect = new Rect(0f, 0f, viewWidth, viewHeight);
            Widgets.BeginScrollView(scrollRect, ref scrollPos, reviewViewRect);
            if (reviewTabIndex == 0) DrawReviewGeneralPage(reviewViewRect, settings);
            else if (reviewTabIndex == 1) DrawPromptPage(reviewViewRect, settings);
            else if (reviewTabIndex == 2) DrawInjectorPage(reviewViewRect, settings);
            else DrawLexiconPage(reviewViewRect, settings);
            Widgets.EndScrollView();
        }

        /// <summary>
        /// 绘制顾客评价子页签，负责把评价基础、提示词、注入器和词库设置收纳到评价子面板。
        /// </summary>
        private void DrawReviewSubTabs(Rect rect)
        {
            float w = 116f;
            string[] labels =
            {
                SimTranslation.TOrFallback("RSMF.ReviewSettings.Tab.General", "基础"),
                SimTranslation.T("RSMF.ReviewSettings.Tab.Prompt"),
                SimTranslation.T("RSMF.ReviewSettings.Tab.Injector"),
                SimTranslation.T("RSMF.ReviewSettings.Tab.Lexicon")
            };
            for (int i = 0; i < labels.Length; i++)
            {
                Rect tab = new Rect(rect.x + i * (w + 8f), rect.y, w, rect.height);
                if (SimUiStyle.DrawTabButton(tab, labels[i], reviewTabIndex == i, new Color(0.72f, 0.72f, 0.72f, 1f)))
                {
                    reviewTabIndex = i;
                    scrollPos = Vector2.zero;
                }
            }
        }

        /// <summary>
        /// 绘制通用 LLM 接口配置页，负责供应商、密钥、模型和接口测试。
        /// </summary>
        private void DrawLlmApiPage(Rect rect, SimManagementLibSettings settings)
        {
            float y = 0f;
            DrawCheckbox(new Rect(0f, y, rect.width, 28f), SimTranslation.TOrFallback("RSMF.LlmSettings.Enable", "启用通用 LLM"), ref settings.llmEnabled, SimTranslation.TOrFallback("RSMF.LlmSettings.EnableTip", "开启后，套餐取名、顾客评价和后续经营 AI 功能可以调用同一套模型接口。"));
            y += 36f;
            DrawRimTalkImportRow(rect.width, ref y, settings);

            Rect providerRect = new Rect(0f, y, 260f, 30f);
            if (SimUiStyle.DrawSecondaryButton(providerRect, SimTranslation.T("RSMF.ReviewSettings.Provider", settings.llmProvider.Named("provider")), true, GameFont.Small))
            {
                Find.WindowStack.Add(new FloatMenu(new System.Collections.Generic.List<FloatMenuOption>
                {
                    new FloatMenuOption(SimTranslation.T("RSMF.ReviewSettings.Provider.OpenAICompatible"), () => settings.llmProvider = SimLlmProvider.OpenAICompatible),
                    new FloatMenuOption("Anthropic", () => settings.llmProvider = SimLlmProvider.Anthropic)
                }));
            }
            y += 40f;

            if (settings.llmProvider == SimLlmProvider.Anthropic)
            {
                DrawTextField(rect.width, ref y, "Anthropic API Key", ref settings.llmAnthropicApiKey, true);
                DrawTextField(rect.width, ref y, "Anthropic Model", ref settings.llmAnthropicModel, false);
            }
            else
            {
                DrawTextField(rect.width, ref y, SimTranslation.T("RSMF.ReviewSettings.OpenAIBaseUrl"), ref settings.llmOpenAiBaseUrl, false);
                DrawTextField(rect.width, ref y, SimTranslation.T("RSMF.ReviewSettings.OpenAIApiKey"), ref settings.llmOpenAiApiKey, true);
                DrawTextField(rect.width, ref y, SimTranslation.T("RSMF.ReviewSettings.OpenAIModel"), ref settings.llmOpenAiModel, false);
            }
            y += 12f;
            ResetText();
        }

        /// <summary>
        /// 绘制顾客评价基础设置页，负责抽样、限速、论坛互动和重型模式配置。
        /// </summary>
        private void DrawReviewGeneralPage(Rect rect, SimManagementLibSettings settings)
        {
            float y = 0f;
            DrawCheckbox(new Rect(0f, y, rect.width, 28f), SimTranslation.T("RSMF.ReviewSettings.EnableReviews"), ref settings.reviewAiEnabled, SimTranslation.T("RSMF.ReviewSettings.EnableReviewsTip"));
            y += 36f;
            DrawCheckbox(new Rect(0f, y, rect.width, 28f), SimTranslation.TOrFallback("RSMF.ReviewSettings.HeavyMode", "重型评价模式（特别耗 token）"), ref settings.reviewHeavyModeEnabled, SimTranslation.TOrFallback("RSMF.ReviewSettings.HeavyModeTip", "开启后每条评价会使用独立初稿上下文和独立润色上下文，重型评价还允许玩家在评价页回复申诉。"));
            y += 30f;
            Text.Font = GameFont.Tiny;
            Text.WordWrap = true;
            GUI.color = settings.reviewHeavyModeEnabled ? new Color(1f, 0.72f, 0.38f, 1f) : new Color(0.72f, 0.76f, 0.82f, 1f);
            float heavyTipH = Mathf.Max(Text.LineHeightOf(GameFont.Tiny), Text.CalcHeight(SimTranslation.TOrFallback("RSMF.ReviewSettings.HeavyModeWarning", "重型模式会明显增加 token 消耗：一次评价至少两次模型调用，玩家每次回复还会额外调用模型。"), rect.width - 10f));
            Widgets.Label(new Rect(0f, y, rect.width - 10f, heavyTipH), SimTranslation.TOrFallback("RSMF.ReviewSettings.HeavyModeWarning", "重型模式会明显增加 token 消耗：一次评价至少两次模型调用，玩家每次回复还会额外调用模型。"));
            y += heavyTipH + 10f;
            ResetText();
            DrawCheckbox(new Rect(0f, y, rect.width, 28f), SimTranslation.TOrFallback("RSMF.ReviewSettings.InfluenceSpawn", "评价影响顾客刷新概率"), ref settings.reviewInfluencesCustomerSpawn, SimTranslation.TOrFallback("RSMF.ReviewSettings.InfluenceSpawnTip", "默认关闭。开启后店铺评价均分会影响真实顾客刷新概率：高分更容易来客，低分会降低来客。"));
            y += 30f;
            Text.Font = GameFont.Tiny;
            Text.WordWrap = true;
            GUI.color = settings.reviewInfluencesCustomerSpawn ? new Color(0.72f, 0.86f, 0.72f, 1f) : new Color(0.72f, 0.76f, 0.82f, 1f);
            string influenceTip = SimTranslation.TOrFallback("RSMF.ReviewSettings.InfluenceSpawnDetail", "至少 3 条有效主评价后生效；撤回评价和楼中楼回复不会参与计算。");
            float influenceTipH = Mathf.Max(Text.LineHeightOf(GameFont.Tiny), Text.CalcHeight(influenceTip, rect.width - 10f));
            Widgets.Label(new Rect(0f, y, rect.width - 10f, influenceTipH), influenceTip);
            y += influenceTipH + 10f;
            ResetText();

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
            bool canTestBaseUrl = settings.llmProvider == SimLlmProvider.Anthropic || !string.IsNullOrWhiteSpace(settings.llmOpenAiBaseUrl);
            bool canTestApi = settings.HasLlmConnectionFields();
            float buttonH = Mathf.Min(34f, rect.height);
            float buttonY = rect.y + (rect.height - buttonH) * 0.5f;
            bool showReviewButtons = tabIndex == 1;
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

            Rect lastButtonRect = apiRect;
            if (showReviewButtons)
            {
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
                lastButtonRect = terminalRect;
            }

            float statusX = lastButtonRect.xMax + 10f;
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
            baseUrlTestTask = SimLlmUtility.TestBaseUrlAsync(CopySettingsForTest(settings), CancellationToken.None);
        }

        /// <summary>
        /// 启动 API 生成测试，负责验证模型、密钥和响应解析是否可用。
        /// </summary>
        private void StartApiTest(SimManagementLibSettings settings)
        {
            testingApi = true;
            connectionStatus = SimTranslation.T("RSMF.ReviewSettings.Status.TestingApi");
            apiTestTask = SimLlmUtility.TestGenerationAsync(CopySettingsForTest(settings), CancellationToken.None);
        }

        /// <summary>
        /// 复制设置给后台测试任务，负责避免后台线程读取正在被 UI 修改的设置对象。
        /// </summary>
        private SimManagementLibSettings CopySettingsForTest(SimManagementLibSettings settings)
        {
            SimManagementLibSettings copy = new SimManagementLibSettings();
            copy.llmEnabled = settings.llmEnabled;
            copy.llmProvider = settings.llmProvider;
            copy.llmOpenAiBaseUrl = settings.llmOpenAiBaseUrl;
            copy.llmOpenAiApiKey = settings.llmOpenAiApiKey;
            copy.llmOpenAiModel = settings.llmOpenAiModel;
            copy.llmAnthropicApiKey = settings.llmAnthropicApiKey;
            copy.llmAnthropicModel = settings.llmAnthropicModel;
            copy.reviewAiEnabled = settings.reviewAiEnabled;
            copy.reviewTemperature = settings.reviewTemperature;
            copy.reviewRequestTimeoutSeconds = settings.reviewRequestTimeoutSeconds;
            copy.reviewForumReactionChance = settings.reviewForumReactionChance;
            copy.reviewForumReplyChance = settings.reviewForumReplyChance;
            copy.reviewHeavyModeEnabled = settings.reviewHeavyModeEnabled;
            copy.reviewInfluencesCustomerSpawn = settings.reviewInfluencesCustomerSpawn;
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
            copy.llmEnabled = true;
            copy.reviewAiEnabled = true;
            copy.SyncLegacyReviewAiConnectionFields();
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
