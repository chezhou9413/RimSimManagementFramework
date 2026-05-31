using HarmonyLib;
using RimWorld;
using SimManagementLib.SimDialog;
using SimManagementLib.SimZone;
using SimManagementLib.Pojo;
using SimManagementLib.Tool;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib
{
    /// <summary>
    /// 保存模拟经营框架的模组设置，负责顾客系统、财务日志和 AI 顾客评价配置的持久化。
    /// </summary>
    public class SimManagementLibSettings : ModSettings
    {
        public bool showCustomerArrivalMessage = true;
        public bool showCustomerInspectDetails = true;
        public int customerArrivalCheckIntervalTicks = 500;
        public int maxFinanceBillRecords = 2000;
        public int financeLogPageSize = 30;
        public bool enableJourneyDebugLog = true;
        public bool mirrorJourneyDebugLogToGameLog;
        public int journeyDebugLogMaxBytes = 4194304;
        public string debugForcedCustomerKindId = "";
        public bool reviewAiEnabled;
        public CustomerReviewProvider reviewProvider = CustomerReviewProvider.OpenAICompatible;
        public string openAiBaseUrl = "https://api.openai.com/v1";
        public string openAiApiKey = "";
        public string openAiModel = "gpt-4o-mini";
        public string anthropicApiKey = "";
        public string anthropicModel = "claude-3-5-haiku-latest";
        public float reviewSampleRate = 0.30f;
        public int reviewRequestsPerMinute = 2;
        public int maxReviewRecords = 1000;
        public float reviewTemperature = 1.05f;
        public float reviewForumReactionChance = 0.95f;
        public float reviewForumReplyChance = 0.75f;
        public int reviewRequestTimeoutSeconds = 90;
        public bool reviewAbsurdNitpickEnabled;
        public float reviewAbsurdNitpickChance = 0.12f;
        public string reviewSystemPrompt = CustomerReviewPromptDefaults.SystemPrompt;
        public string reviewUserPrompt = CustomerReviewPromptDefaults.UserPrompt;
        public string reviewNicknamePrefixes = CustomerReviewPromptDefaults.NicknamePrefixes;
        public string reviewNicknameSuffixes = CustomerReviewPromptDefaults.NicknameSuffixes;
        public string reviewToneWords = CustomerReviewPromptDefaults.ToneWords;
        public string reviewPositiveWords = CustomerReviewPromptDefaults.PositiveWords;
        public string reviewNegativeWords = CustomerReviewPromptDefaults.NegativeWords;
        public string reviewBannedWords = CustomerReviewPromptDefaults.BannedWords;
        public int reviewConversationContextMaxChars = 48000;
        public string reviewPromptInputFormat = CustomerReviewPromptInjector.PromptInputFormatXml;
        public string reviewPromptEnabledNodeIds = "";
        public string reviewPromptNodeOrder = "";
        public string reviewPromptCustomNodes = "";

        /// <summary>
        /// 读写模组设置数据，负责兼容旧配置并限制数值范围。
        /// </summary>
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref showCustomerArrivalMessage, "showCustomerArrivalMessage", true);
            Scribe_Values.Look(ref showCustomerInspectDetails, "showCustomerInspectDetails", true);
            Scribe_Values.Look(ref customerArrivalCheckIntervalTicks, "customerArrivalCheckIntervalTicks", 500);
            Scribe_Values.Look(ref maxFinanceBillRecords, "maxFinanceBillRecords", 2000);
            Scribe_Values.Look(ref financeLogPageSize, "financeLogPageSize", 30);
            Scribe_Values.Look(ref enableJourneyDebugLog, "enableJourneyDebugLog", true);
            Scribe_Values.Look(ref mirrorJourneyDebugLogToGameLog, "mirrorJourneyDebugLogToGameLog", false);
            Scribe_Values.Look(ref journeyDebugLogMaxBytes, "journeyDebugLogMaxBytes", 4194304);
            Scribe_Values.Look(ref debugForcedCustomerKindId, "debugForcedCustomerKindId", "");
            Scribe_Values.Look(ref reviewAiEnabled, "reviewAiEnabled", false);
            Scribe_Values.Look(ref reviewProvider, "reviewProvider", CustomerReviewProvider.OpenAICompatible);
            Scribe_Values.Look(ref openAiBaseUrl, "openAiBaseUrl", "https://api.openai.com/v1");
            Scribe_Values.Look(ref openAiApiKey, "openAiApiKey", "");
            Scribe_Values.Look(ref openAiModel, "openAiModel", "gpt-4o-mini");
            Scribe_Values.Look(ref anthropicApiKey, "anthropicApiKey", "");
            Scribe_Values.Look(ref anthropicModel, "anthropicModel", "claude-3-5-haiku-latest");
            Scribe_Values.Look(ref reviewSampleRate, "reviewSampleRate", 0.30f);
            Scribe_Values.Look(ref reviewRequestsPerMinute, "reviewRequestsPerMinute", 2);
            Scribe_Values.Look(ref maxReviewRecords, "maxReviewRecords", 1000);
            Scribe_Values.Look(ref reviewTemperature, "reviewTemperature", 1.05f);
            Scribe_Values.Look(ref reviewForumReactionChance, "reviewForumReactionChance", 0.95f);
            Scribe_Values.Look(ref reviewForumReplyChance, "reviewForumReplyChance", 0.75f);
            Scribe_Values.Look(ref reviewRequestTimeoutSeconds, "reviewRequestTimeoutSeconds", 90);
            Scribe_Values.Look(ref reviewAbsurdNitpickEnabled, "reviewAbsurdNitpickEnabled", false);
            Scribe_Values.Look(ref reviewAbsurdNitpickChance, "reviewAbsurdNitpickChance", 0.12f);
            Scribe_Values.Look(ref reviewSystemPrompt, "reviewSystemPrompt", CustomerReviewPromptDefaults.DefaultSystemPrompt);
            Scribe_Values.Look(ref reviewUserPrompt, "reviewUserPrompt", CustomerReviewPromptDefaults.DefaultUserPrompt);
            Scribe_Values.Look(ref reviewNicknamePrefixes, "reviewNicknamePrefixes", CustomerReviewPromptDefaults.DefaultNicknamePrefixes);
            Scribe_Values.Look(ref reviewNicknameSuffixes, "reviewNicknameSuffixes", CustomerReviewPromptDefaults.DefaultNicknameSuffixes);
            Scribe_Values.Look(ref reviewToneWords, "reviewToneWords", CustomerReviewPromptDefaults.DefaultToneWords);
            Scribe_Values.Look(ref reviewPositiveWords, "reviewPositiveWords", CustomerReviewPromptDefaults.DefaultPositiveWords);
            Scribe_Values.Look(ref reviewNegativeWords, "reviewNegativeWords", CustomerReviewPromptDefaults.DefaultNegativeWords);
            Scribe_Values.Look(ref reviewBannedWords, "reviewBannedWords", CustomerReviewPromptDefaults.DefaultBannedWords);
            Scribe_Values.Look(ref reviewConversationContextMaxChars, "reviewConversationContextMaxChars", 48000);
            Scribe_Values.Look(ref reviewPromptInputFormat, "reviewPromptInputFormat", CustomerReviewPromptInjector.PromptInputFormatXml);
            Scribe_Values.Look(ref reviewPromptEnabledNodeIds, "reviewPromptEnabledNodeIds", "");
            Scribe_Values.Look(ref reviewPromptNodeOrder, "reviewPromptNodeOrder", "");
            Scribe_Values.Look(ref reviewPromptCustomNodes, "reviewPromptCustomNodes", "");

            customerArrivalCheckIntervalTicks = Mathf.Clamp(customerArrivalCheckIntervalTicks, 120, 5000);
            maxFinanceBillRecords = Mathf.Clamp(maxFinanceBillRecords, 200, 50000);
            financeLogPageSize = Mathf.Clamp(financeLogPageSize, 10, 200);
            journeyDebugLogMaxBytes = Mathf.Clamp(journeyDebugLogMaxBytes, 262144, 16777216);
            reviewSampleRate = Mathf.Clamp01(reviewSampleRate);
            reviewRequestsPerMinute = Mathf.Clamp(reviewRequestsPerMinute, 1, 60);
            maxReviewRecords = Mathf.Clamp(maxReviewRecords, 50, 10000);
            reviewTemperature = Mathf.Clamp(reviewTemperature, 0.1f, 2f);
            reviewForumReactionChance = Mathf.Clamp01(reviewForumReactionChance);
            reviewForumReplyChance = Mathf.Clamp01(reviewForumReplyChance);
            reviewRequestTimeoutSeconds = Mathf.Clamp(reviewRequestTimeoutSeconds, 20, 180);
            reviewAbsurdNitpickChance = Mathf.Clamp01(reviewAbsurdNitpickChance);
            reviewConversationContextMaxChars = Mathf.Clamp(reviewConversationContextMaxChars, 0, 64000);
            if (debugForcedCustomerKindId == null)
                debugForcedCustomerKindId = "";
            NormalizeReviewSettingsText();
        }

        /// <summary>
        /// 规范化 AI 点评设置文本，负责旧存档缺失字段时补齐安全默认值。
        /// </summary>
        private void NormalizeReviewSettingsText()
        {
            if (openAiBaseUrl == null) openAiBaseUrl = "https://api.openai.com/v1";
            if (openAiApiKey == null) openAiApiKey = "";
            if (openAiModel == null) openAiModel = "gpt-4o-mini";
            if (anthropicApiKey == null) anthropicApiKey = "";
            if (anthropicModel == null) anthropicModel = "claude-3-5-haiku-latest";
            if (string.IsNullOrEmpty(reviewSystemPrompt)) reviewSystemPrompt = CustomerReviewPromptDefaults.DefaultSystemPrompt;
            if (string.IsNullOrEmpty(reviewUserPrompt)) reviewUserPrompt = CustomerReviewPromptDefaults.DefaultUserPrompt;
            if (reviewNicknamePrefixes == null) reviewNicknamePrefixes = CustomerReviewPromptDefaults.DefaultNicknamePrefixes;
            if (reviewNicknameSuffixes == null) reviewNicknameSuffixes = CustomerReviewPromptDefaults.DefaultNicknameSuffixes;
            if (reviewToneWords == null) reviewToneWords = CustomerReviewPromptDefaults.DefaultToneWords;
            if (reviewPositiveWords == null) reviewPositiveWords = CustomerReviewPromptDefaults.DefaultPositiveWords;
            if (reviewNegativeWords == null) reviewNegativeWords = CustomerReviewPromptDefaults.DefaultNegativeWords;
            if (reviewBannedWords == null) reviewBannedWords = CustomerReviewPromptDefaults.DefaultBannedWords;
            if (reviewPromptInputFormat != CustomerReviewPromptInjector.PromptInputFormatPlain && reviewPromptInputFormat != CustomerReviewPromptInjector.PromptInputFormatXml) reviewPromptInputFormat = CustomerReviewPromptInjector.PromptInputFormatXml;
            if (reviewPromptEnabledNodeIds == null) reviewPromptEnabledNodeIds = "";
            if (reviewPromptNodeOrder == null) reviewPromptNodeOrder = "";
            if (reviewPromptCustomNodes == null) reviewPromptCustomNodes = "";
            UpgradeLegacyReviewForumDefaults();
            UpgradeLegacyReviewPromptDefaults();
        }

        /// <summary>
        /// 将旧版论坛互动默认概率升级为更活跃的默认值。
        /// </summary>
        private void UpgradeLegacyReviewForumDefaults()
        {
            if (Mathf.Approximately(reviewForumReactionChance, 0.60f) || Mathf.Approximately(reviewForumReactionChance, 0.85f)) reviewForumReactionChance = 0.95f;
            if (Mathf.Approximately(reviewForumReplyChance, 0.35f) || Mathf.Approximately(reviewForumReplyChance, 0.65f)) reviewForumReplyChance = 0.75f;
        }

        /// <summary>
        /// 将未手动改动过的旧默认提示词升级为更口语、多样的默认文本。
        /// </summary>
        private void UpgradeLegacyReviewPromptDefaults()
        {
            if (reviewSystemPrompt == CustomerReviewPromptDefaults.LegacySystemPrompt) reviewSystemPrompt = CustomerReviewPromptDefaults.DefaultSystemPrompt;
            if (reviewUserPrompt == CustomerReviewPromptDefaults.LegacyUserPrompt) reviewUserPrompt = CustomerReviewPromptDefaults.DefaultUserPrompt;
            if (reviewUserPrompt == CustomerReviewPromptDefaults.LegacyAggressiveUserPrompt) reviewUserPrompt = CustomerReviewPromptDefaults.DefaultUserPrompt;
            if (reviewUserPrompt == CustomerReviewPromptDefaults.LegacyAggressiveUserPromptWithBackstory) reviewUserPrompt = CustomerReviewPromptDefaults.DefaultUserPrompt;
            if (reviewUserPrompt == CustomerReviewPromptDefaults.LegacyReviewCoupledNicknamePrompt) reviewUserPrompt = CustomerReviewPromptDefaults.DefaultUserPrompt;
            if (reviewUserPrompt == CustomerReviewPromptDefaults.LegacyReceiptLikeUserPrompt) reviewUserPrompt = CustomerReviewPromptDefaults.DefaultUserPrompt;
            if (reviewUserPrompt == CustomerReviewPromptDefaults.LegacyNaturalVoiceUserPrompt) reviewUserPrompt = CustomerReviewPromptDefaults.DefaultUserPrompt;
            if (reviewSystemPrompt == CustomerReviewPromptDefaults.LegacyVerboseSystemPrompt) reviewSystemPrompt = CustomerReviewPromptDefaults.DefaultSystemPrompt;
            if (reviewUserPrompt == CustomerReviewPromptDefaults.LegacyVerboseUserPrompt) reviewUserPrompt = CustomerReviewPromptDefaults.DefaultUserPrompt;
            if (reviewNicknamePrefixes == CustomerReviewPromptDefaults.LegacyNicknamePrefixes) reviewNicknamePrefixes = CustomerReviewPromptDefaults.DefaultNicknamePrefixes;
            if (reviewNicknameSuffixes == CustomerReviewPromptDefaults.LegacyNicknameSuffixes) reviewNicknameSuffixes = CustomerReviewPromptDefaults.DefaultNicknameSuffixes;
            if (reviewNicknamePrefixes == CustomerReviewPromptDefaults.LegacyTemplateNicknamePrefixes) reviewNicknamePrefixes = CustomerReviewPromptDefaults.DefaultNicknamePrefixes;
            if (reviewNicknameSuffixes == CustomerReviewPromptDefaults.LegacyTemplateNicknameSuffixes) reviewNicknameSuffixes = CustomerReviewPromptDefaults.DefaultNicknameSuffixes;
            if (reviewToneWords == CustomerReviewPromptDefaults.LegacyToneWords) reviewToneWords = CustomerReviewPromptDefaults.DefaultToneWords;
            if (reviewPositiveWords == CustomerReviewPromptDefaults.LegacyPositiveWords) reviewPositiveWords = CustomerReviewPromptDefaults.DefaultPositiveWords;
            if (reviewNegativeWords == CustomerReviewPromptDefaults.LegacyNegativeWords) reviewNegativeWords = CustomerReviewPromptDefaults.DefaultNegativeWords;
            if (reviewBannedWords == CustomerReviewPromptDefaults.LegacyBannedWords) reviewBannedWords = CustomerReviewPromptDefaults.DefaultBannedWords;
        }

        /// <summary>
        /// 判断当前 AI 点评配置是否具备发起请求的必要字段。
        /// </summary>
        public bool HasValidReviewAiConfig()
        {
            if (!reviewAiEnabled) return false;
            return HasReviewAiConnectionFields();
        }

        /// <summary>
        /// 判断当前供应商的连接字段是否齐全，负责让测试按钮不依赖功能启用开关。
        /// </summary>
        public bool HasReviewAiConnectionFields()
        {
            if (reviewProvider == CustomerReviewProvider.Anthropic)
                return !string.IsNullOrEmpty(anthropicApiKey) && !string.IsNullOrEmpty(anthropicModel);

            return !string.IsNullOrEmpty(openAiBaseUrl)
                && !string.IsNullOrEmpty(openAiApiKey)
                && !string.IsNullOrEmpty(openAiModel);
        }
    }

    /// <summary>
    /// RimWorld 模组入口，负责初始化 Harmony、读取设置并绘制模组设置页。
    /// </summary>
    public class SimManagementLibMod : Mod
    {
        private const float SettingsScrollBottomPadding = 24f;
        public static SimManagementLibSettings Settings { get; private set; } = new SimManagementLibSettings();
        public static ModContentPack ActiveContentPack { get; private set; }
        private Vector2 settingsScrollPosition;
        private float settingsViewHeight = 700f;

        /// <summary>
        /// 初始化模组设置实例，负责保存内容包引用并读取玩家配置。
        /// </summary>
        public SimManagementLibMod(ModContentPack content) : base(content)
        {
            ActiveContentPack = content;
            Settings = GetSettings<SimManagementLibSettings>();
        }

        /// <summary>
        /// 返回模组设置页名称，负责让 RimWorld 在设置列表中显示本模组入口。
        /// </summary>
        public override string SettingsCategory()
        {
            return SimTranslation.T("RSMF.Settings.Category");
        }

        /// <summary>
        /// 绘制模组设置页面，负责提供顾客、财务、评价和注册面板入口。
        /// </summary>
        public override void DoSettingsWindowContents(Rect inRect)
        {
            Rect outRect = inRect.ContractedBy(2f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 18f, Mathf.Max(settingsViewHeight, outRect.height));
            Widgets.BeginScrollView(outRect, ref settingsScrollPosition, viewRect);

            Listing_Standard list = new Listing_Standard();
            list.maxOneColumn = true;
            list.Begin(viewRect);

            list.Label(SimTranslation.T("RSMF.Settings.CustomerSystem"));
            list.CheckboxLabeled(SimTranslation.T("RSMF.Settings.ShowCustomerArrivalMessage"), ref Settings.showCustomerArrivalMessage, SimTranslation.T("RSMF.Settings.ShowCustomerArrivalMessageTip"));
            list.CheckboxLabeled(SimTranslation.T("RSMF.Settings.ShowCustomerInspectDetails"), ref Settings.showCustomerInspectDetails, SimTranslation.T("RSMF.Settings.ShowCustomerInspectDetailsTip"));
            list.Label(SimTranslation.T("RSMF.Settings.CustomerArrivalCheckInterval", Settings.customerArrivalCheckIntervalTicks.Named("ticks")));
            Settings.customerArrivalCheckIntervalTicks = (int)list.Slider(Settings.customerArrivalCheckIntervalTicks, 120f, 5000f);
            DrawDebugForcedCustomerKindSelector(list);

            list.GapLine();
            list.Label(SimTranslation.T("RSMF.Settings.FinanceAndLogs"));
            list.Label(SimTranslation.T("RSMF.Settings.MaxFinanceBillRecords", Settings.maxFinanceBillRecords.Named("count")));
            Settings.maxFinanceBillRecords = (int)list.Slider(Settings.maxFinanceBillRecords, 200f, 50000f);
            list.Label(SimTranslation.T("RSMF.Settings.FinanceLogPageSize", Settings.financeLogPageSize.Named("count")));
            Settings.financeLogPageSize = (int)list.Slider(Settings.financeLogPageSize, 10f, 200f);
            list.CheckboxLabeled("启用顾客行程调试日志", ref Settings.enableJourneyDebugLog, "输出到 RimWorld 存档数据目录下的 RimSimManagementFramework/Logs/journey-debug.log。");
            list.CheckboxLabeled("同步调试日志到游戏日志", ref Settings.mirrorJourneyDebugLogToGameLog, "仅排查问题时开启，会增加游戏日志输出。");
            list.Label($"行程调试日志最大体积：{Settings.journeyDebugLogMaxBytes / 1024} KB");
            Settings.journeyDebugLogMaxBytes = (int)list.Slider(Settings.journeyDebugLogMaxBytes, 262144f, 16777216f);
            Rect clearJourneyLogRect = list.GetRect(32f);
            if (Widgets.ButtonText(clearJourneyLogRect, "清空顾客行程调试日志"))
                SimDebugLogger.ClearJourneyLog();

            list.GapLine();
            list.Label(SimTranslation.T("RSMF.Settings.CustomerReviews"));
            list.CheckboxLabeled(SimTranslation.T("RSMF.Settings.EnableCustomerReviews"), ref Settings.reviewAiEnabled, SimTranslation.T("RSMF.Settings.EnableCustomerReviewsTip"));
            string reviewState = Settings.HasValidReviewAiConfig() ? SimTranslation.T("RSMF.Settings.ReviewStateValid") : SimTranslation.T("RSMF.Settings.ReviewStateInvalid");
            list.Label(SimTranslation.T(
                "RSMF.Settings.ReviewStatusLine",
                reviewState.Named("state"),
                (Settings.reviewSampleRate * 100f).ToString("F0").Named("sampleRate"),
                Settings.reviewRequestsPerMinute.Named("rpm"),
                Settings.reviewRequestTimeoutSeconds.Named("timeout"),
                Settings.reviewConversationContextMaxChars.Named("chars")));
            Rect reviewSettingsRect = list.GetRect(40f);
            if (Widgets.ButtonText(reviewSettingsRect, SimTranslation.T("RSMF.Settings.OpenCustomerReviewSettings")))
                Find.WindowStack.Add(new Dialog_CustomerReviewAiSettings());

            list.GapLine();
            Rect customGoodsButtonRect = list.GetRect(40f);
            if (Widgets.ButtonText(customGoodsButtonRect, SimTranslation.T("RSMF.Settings.OpenCustomGoodsRegistry")))
                Find.WindowStack.Add(new Dialog_CustomGoodsRegistry());

            Rect customCustomerButtonRect = list.GetRect(40f);
            if (Widgets.ButtonText(customCustomerButtonRect, SimTranslation.T("RSMF.Settings.OpenCustomCustomerRegistry")))
                Find.WindowStack.Add(new Dialog_CustomCustomerRegistry());

            Rect resetRect = list.GetRect(36f);
            if (Widgets.ButtonText(resetRect, SimTranslation.T("RSMF.Settings.ResetDefaults")))
            {
                Settings.showCustomerArrivalMessage = true;
                Settings.showCustomerInspectDetails = true;
                Settings.customerArrivalCheckIntervalTicks = 500;
                Settings.maxFinanceBillRecords = 2000;
                Settings.financeLogPageSize = 30;
                Settings.enableJourneyDebugLog = true;
                Settings.mirrorJourneyDebugLogToGameLog = false;
                Settings.journeyDebugLogMaxBytes = 4194304;
                Settings.debugForcedCustomerKindId = "";
            }

            list.End();
            settingsViewHeight = Mathf.Max(outRect.height, list.CurHeight + SettingsScrollBottomPadding);
            Widgets.EndScrollView();
            Settings.Write();
        }

        /// <summary>
        /// 绘制用于测试刷客的强制顾客组选择控件。
        /// </summary>
        private static void DrawDebugForcedCustomerKindSelector(Listing_Standard list)
        {
            CustomerCatalog.EnsureInitialized();
            RuntimeCustomerKind selected = CustomerCatalog.GetKind(Settings.debugForcedCustomerKindId);
            string label = selected != null
                ? $"{selected.label} / {selected.kindId}"
                : (string.IsNullOrEmpty(Settings.debugForcedCustomerKindId)
                    ? SimTranslation.T("RSMF.Common.Off")
                    : SimTranslation.T("RSMF.Settings.InvalidForcedCustomerKind", Settings.debugForcedCustomerKindId.Named("kindId")));

            Rect rect = list.GetRect(34f);
            if (Widgets.ButtonText(rect, SimTranslation.T("RSMF.Settings.DebugForcedCustomerKind", label.Truncate(rect.width - 170f).Named("label"))))
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>
                {
                    new FloatMenuOption(SimTranslation.T("RSMF.Common.Off"), () => Settings.debugForcedCustomerKindId = "")
                };

                foreach (RuntimeCustomerKind kind in CustomerCatalog.Kinds
                    .Where(k => k != null)
                    .OrderBy(k => k.sourceDef != null ? 0 : 1)
                    .ThenBy(k => k.label))
                {
                    string optionLabel = $"{kind.label} / {kind.kindId}";
                    options.Add(new FloatMenuOption(optionLabel, () => Settings.debugForcedCustomerKindId = kind.kindId));
                }

                Find.WindowStack.Add(new FloatMenu(options));
            }

            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.72f, 0.76f, 0.82f, 1f);
            list.Label(SimTranslation.T("RSMF.Settings.DebugForcedCustomerKindTip"));
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }
    }

    [StaticConstructorOnStartup]
    public static class SimManagementLibBootstrap
    {
        /// <summary>
        /// 初始化框架补丁和快捷指令注册，负责在 Def 加载后接入必要的运行时入口。
        /// </summary>
        static SimManagementLibBootstrap()
        {
            Harmony harmony = new Harmony("com.Chezhou.simmanagementlib");
            harmony.PatchAll();
            EnsureShopDesignatorRegistered();
        }

        /// <summary>
        /// 将商店区和快捷商品注册指令接入原版区划分类，负责兼容只打开原版区划页的玩家操作路径。
        /// </summary>
        private static void EnsureShopDesignatorRegistered()
        {
            DesignationCategoryDef zoneCategory = DefDatabase<DesignationCategoryDef>.GetNamedSilentFail("Zone");
            if (zoneCategory == null)
                return;

            if (zoneCategory.specialDesignatorClasses == null)
                zoneCategory.specialDesignatorClasses = new List<Type>();

            Type shopDesignatorType = typeof(Designator_ZoneAdd_Shop);
            if (!zoneCategory.specialDesignatorClasses.Contains(shopDesignatorType))
                zoneCategory.specialDesignatorClasses.Add(shopDesignatorType);

            Type quickRegisterDesignatorType = typeof(Designator_RegisterGoodsFromSelection);
            if (zoneCategory.specialDesignatorClasses.Contains(quickRegisterDesignatorType))
                return;

            int shopIndex = zoneCategory.specialDesignatorClasses.IndexOf(shopDesignatorType);
            if (shopIndex >= 0 && shopIndex < zoneCategory.specialDesignatorClasses.Count - 1)
                zoneCategory.specialDesignatorClasses.Insert(shopIndex + 1, quickRegisterDesignatorType);
            else
                zoneCategory.specialDesignatorClasses.Add(quickRegisterDesignatorType);
        }
    }
}
