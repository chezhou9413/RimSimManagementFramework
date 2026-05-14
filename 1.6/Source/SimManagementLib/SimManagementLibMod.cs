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
            Scribe_Values.Look(ref reviewAbsurdNitpickEnabled, "reviewAbsurdNitpickEnabled", false);
            Scribe_Values.Look(ref reviewAbsurdNitpickChance, "reviewAbsurdNitpickChance", 0.12f);
            Scribe_Values.Look(ref reviewSystemPrompt, "reviewSystemPrompt", CustomerReviewPromptDefaults.SystemPrompt);
            Scribe_Values.Look(ref reviewUserPrompt, "reviewUserPrompt", CustomerReviewPromptDefaults.UserPrompt);
            Scribe_Values.Look(ref reviewNicknamePrefixes, "reviewNicknamePrefixes", CustomerReviewPromptDefaults.NicknamePrefixes);
            Scribe_Values.Look(ref reviewNicknameSuffixes, "reviewNicknameSuffixes", CustomerReviewPromptDefaults.NicknameSuffixes);
            Scribe_Values.Look(ref reviewToneWords, "reviewToneWords", CustomerReviewPromptDefaults.ToneWords);
            Scribe_Values.Look(ref reviewPositiveWords, "reviewPositiveWords", CustomerReviewPromptDefaults.PositiveWords);
            Scribe_Values.Look(ref reviewNegativeWords, "reviewNegativeWords", CustomerReviewPromptDefaults.NegativeWords);
            Scribe_Values.Look(ref reviewBannedWords, "reviewBannedWords", CustomerReviewPromptDefaults.BannedWords);
            Scribe_Values.Look(ref reviewConversationContextMaxChars, "reviewConversationContextMaxChars", 48000);

            customerArrivalCheckIntervalTicks = Mathf.Clamp(customerArrivalCheckIntervalTicks, 120, 5000);
            maxFinanceBillRecords = Mathf.Clamp(maxFinanceBillRecords, 200, 50000);
            financeLogPageSize = Mathf.Clamp(financeLogPageSize, 10, 200);
            reviewSampleRate = Mathf.Clamp01(reviewSampleRate);
            reviewRequestsPerMinute = Mathf.Clamp(reviewRequestsPerMinute, 1, 60);
            maxReviewRecords = Mathf.Clamp(maxReviewRecords, 50, 10000);
            reviewTemperature = Mathf.Clamp(reviewTemperature, 0.1f, 2f);
            reviewForumReactionChance = Mathf.Clamp01(reviewForumReactionChance);
            reviewForumReplyChance = Mathf.Clamp01(reviewForumReplyChance);
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
            if (string.IsNullOrEmpty(reviewSystemPrompt)) reviewSystemPrompt = CustomerReviewPromptDefaults.SystemPrompt;
            if (string.IsNullOrEmpty(reviewUserPrompt)) reviewUserPrompt = CustomerReviewPromptDefaults.UserPrompt;
            if (reviewNicknamePrefixes == null) reviewNicknamePrefixes = CustomerReviewPromptDefaults.NicknamePrefixes;
            if (reviewNicknameSuffixes == null) reviewNicknameSuffixes = CustomerReviewPromptDefaults.NicknameSuffixes;
            if (reviewToneWords == null) reviewToneWords = CustomerReviewPromptDefaults.ToneWords;
            if (reviewPositiveWords == null) reviewPositiveWords = CustomerReviewPromptDefaults.PositiveWords;
            if (reviewNegativeWords == null) reviewNegativeWords = CustomerReviewPromptDefaults.NegativeWords;
            if (reviewBannedWords == null) reviewBannedWords = CustomerReviewPromptDefaults.BannedWords;
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
            if (reviewSystemPrompt == CustomerReviewPromptDefaults.LegacySystemPrompt) reviewSystemPrompt = CustomerReviewPromptDefaults.SystemPrompt;
            if (reviewUserPrompt == CustomerReviewPromptDefaults.LegacyUserPrompt) reviewUserPrompt = CustomerReviewPromptDefaults.UserPrompt;
            if (reviewUserPrompt == CustomerReviewPromptDefaults.LegacyAggressiveUserPrompt) reviewUserPrompt = CustomerReviewPromptDefaults.UserPrompt;
            if (reviewUserPrompt == CustomerReviewPromptDefaults.LegacyAggressiveUserPromptWithBackstory) reviewUserPrompt = CustomerReviewPromptDefaults.UserPrompt;
            if (reviewUserPrompt == CustomerReviewPromptDefaults.LegacyReviewCoupledNicknamePrompt) reviewUserPrompt = CustomerReviewPromptDefaults.UserPrompt;
            if (reviewNicknamePrefixes == CustomerReviewPromptDefaults.LegacyNicknamePrefixes) reviewNicknamePrefixes = CustomerReviewPromptDefaults.NicknamePrefixes;
            if (reviewNicknameSuffixes == CustomerReviewPromptDefaults.LegacyNicknameSuffixes) reviewNicknameSuffixes = CustomerReviewPromptDefaults.NicknameSuffixes;
            if (reviewNicknamePrefixes == CustomerReviewPromptDefaults.LegacyTemplateNicknamePrefixes) reviewNicknamePrefixes = CustomerReviewPromptDefaults.NicknamePrefixes;
            if (reviewNicknameSuffixes == CustomerReviewPromptDefaults.LegacyTemplateNicknameSuffixes) reviewNicknameSuffixes = CustomerReviewPromptDefaults.NicknameSuffixes;
            if (reviewToneWords == CustomerReviewPromptDefaults.LegacyToneWords) reviewToneWords = CustomerReviewPromptDefaults.ToneWords;
            if (reviewPositiveWords == CustomerReviewPromptDefaults.LegacyPositiveWords) reviewPositiveWords = CustomerReviewPromptDefaults.PositiveWords;
            if (reviewNegativeWords == CustomerReviewPromptDefaults.LegacyNegativeWords) reviewNegativeWords = CustomerReviewPromptDefaults.NegativeWords;
            if (reviewBannedWords == CustomerReviewPromptDefaults.LegacyBannedWords) reviewBannedWords = CustomerReviewPromptDefaults.BannedWords;
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
        public static SimManagementLibSettings Settings { get; private set; } = new SimManagementLibSettings();
        public static ModContentPack ActiveContentPack { get; private set; }

        public SimManagementLibMod(ModContentPack content) : base(content)
        {
            ActiveContentPack = content;
            Settings = GetSettings<SimManagementLibSettings>();
        }

        public override string SettingsCategory()
        {
            return "边缘模拟经营框架";
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard list = new Listing_Standard();
            list.Begin(inRect);

            list.Label("顾客系统");
            list.CheckboxLabeled("显示顾客到访提醒", ref Settings.showCustomerArrivalMessage, "关闭后不再显示“有顾客正在前往商店”的提示。");
            list.CheckboxLabeled("显示顾客检查详情", ref Settings.showCustomerInspectDetails, "关闭后不再在角色检查面板中附加顾客运行时参数。");
            list.Label($"顾客刷新检查间隔: {Settings.customerArrivalCheckIntervalTicks} ticks");
            Settings.customerArrivalCheckIntervalTicks = (int)list.Slider(Settings.customerArrivalCheckIntervalTicks, 120f, 5000f);
            DrawDebugForcedCustomerKindSelector(list);

            list.GapLine();
            list.Label("财务与日志");
            list.Label($"财务日志最大保留条数: {Settings.maxFinanceBillRecords}");
            Settings.maxFinanceBillRecords = (int)list.Slider(Settings.maxFinanceBillRecords, 200f, 50000f);
            list.Label($"统计面板每页条数: {Settings.financeLogPageSize}");
            Settings.financeLogPageSize = (int)list.Slider(Settings.financeLogPageSize, 10f, 200f);

            list.GapLine();
            list.Label("顾客评价");
            list.CheckboxLabeled("启用顾客评价", ref Settings.reviewAiEnabled, "只有配置有效接口后才会在顾客离店时按抽样率生成点评。");
            string reviewState = Settings.HasValidReviewAiConfig() ? "配置有效" : "未配置或未启用";
            list.Label($"当前状态: {reviewState}   抽样率: {(Settings.reviewSampleRate * 100f):F0}%   限速: {Settings.reviewRequestsPerMinute}/分钟   对话预算: {Settings.reviewConversationContextMaxChars} 字符");
            Rect reviewSettingsRect = list.GetRect(40f);
            if (Widgets.ButtonText(reviewSettingsRect, "打开顾客评价配置"))
                Find.WindowStack.Add(new Dialog_CustomerReviewAiSettings());

            list.GapLine();
            Rect customGoodsButtonRect = list.GetRect(40f);
            if (Widgets.ButtonText(customGoodsButtonRect, "打开自定义商品注册面板"))
                Find.WindowStack.Add(new Dialog_CustomGoodsRegistry());

            Rect customCustomerButtonRect = list.GetRect(40f);
            if (Widgets.ButtonText(customCustomerButtonRect, "打开自定义顾客注册面板"))
                Find.WindowStack.Add(new Dialog_CustomCustomerRegistry());

            Rect resetRect = list.GetRect(36f);
            if (Widgets.ButtonText(resetRect, "恢复默认设置"))
            {
                Settings.showCustomerArrivalMessage = true;
                Settings.showCustomerInspectDetails = true;
                Settings.customerArrivalCheckIntervalTicks = 500;
                Settings.maxFinanceBillRecords = 2000;
                Settings.financeLogPageSize = 30;
                Settings.debugForcedCustomerKindId = "";
            }

            list.End();
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
                : (string.IsNullOrEmpty(Settings.debugForcedCustomerKindId) ? "关闭" : "已失效: " + Settings.debugForcedCustomerKindId);

            Rect rect = list.GetRect(34f);
            if (Widgets.ButtonText(rect, "Debug 强制顾客组: " + label.Truncate(rect.width - 170f)))
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>
                {
                    new FloatMenuOption("关闭", () => Settings.debugForcedCustomerKindId = "")
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
            list.Label("开启后自然刷客和强制刷新会优先使用该顾客组；为空时恢复正常权重随机。");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }
    }

    [StaticConstructorOnStartup]
    public static class SimManagementLibBootstrap
    {
        static SimManagementLibBootstrap()
        {
            Harmony harmony = new Harmony("com.Chezhou.simmanagementlib");
            harmony.PatchAll();
            EnsureShopDesignatorRegistered();
        }

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
        }
    }
}
