using SimManagementLib.Pojo;
using System;
using System.Collections.Generic;
using System.Text;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 负责把顾客评价提示词拆成稳定缓存前缀和动态顾客资料，降低服务端前缀缓存失效和旧评论句式污染。
    /// </summary>
    public static partial class CustomerReviewPromptInjector
    {
        /// <summary>
        /// 构造可缓存的稳定提示词前缀，负责放置输出格式、语言、写作架构和词库等跨顾客不变内容。
        /// </summary>
        public static string BuildStablePromptPrefix(SimManagementLibSettings settings)
        {
            return settings == null || settings.reviewPromptInputFormat == PromptInputFormatPlain
                ? BuildStablePlainPrefix(settings)
                : BuildStableXmlPrefix(settings);
        }

        /// <summary>
        /// 构造每位顾客专属的动态提示词，负责把身份、状态、购物结果和反重复备忘放在请求末尾。
        /// </summary>
        public static string BuildDynamicPrompt(CustomerReviewSnapshot snapshot, SimManagementLibSettings settings, string antiRepeatContext)
        {
            return settings == null || settings.reviewPromptInputFormat == PromptInputFormatPlain
                ? BuildDynamicPlainPrompt(snapshot, settings, antiRepeatContext)
                : BuildDynamicXmlPrompt(snapshot, settings, antiRepeatContext);
        }

        /// <summary>
        /// 构造普通文本稳定前缀，负责兼容玩家选择的非 XML 输入格式。
        /// </summary>
        private static string BuildStablePlainPrefix(SimManagementLibSettings settings)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(settings?.reviewUserPrompt ?? CustomerReviewPromptDefaults.UserPrompt);
            sb.AppendLine();
            AppendLanguageContext(sb);
            sb.AppendLine();
            sb.AppendLine(SimTranslation.TOrFallback("RSMF.CustomerReview.Split.StableArchitectureHeader", "稳定写作架构:"));
            sb.AppendLine(SimTranslation.TOrFallback("RSMF.CustomerReview.Split.JsonContract", "只输出一个 JSON 对象，字段固定为 nickname、stars、reviewText、upvoteReviewId、downvoteReviewId、replyToReviewId、replyText、replyStance、tags。"));
            sb.AppendLine(SimTranslation.TOrFallback("RSMF.CustomerReview.Split.CacheRule", "前面的规则是稳定前缀；最后一条顾客资料才是本次素材，不要续写或复读旧评价。"));
            sb.AppendLine(SimTranslation.TOrFallback("RSMF.CustomerReview.Split.RealVoiceRule", "reviewText 像刚离店随手发的论坛短帖，从一句当下反应切入，不写字段清单、质检报告或商品收据。"));
            sb.AppendLine(SimTranslation.TOrFallback("RSMF.CustomerReview.Split.OptionalFactsRule", "optionalFacts 只是备查资料，只有成为本条情绪焦点时才写入正文。"));
            sb.AppendLine(SimTranslation.TOrFallback("RSMF.CustomerReview.Split.AgeUseRule", "年龄、寿命、活了多久是低优先级身份素材；多数时候只当背景，不主动拿来开头，只有它和本次商品或体验形成明显反差时才自然带一句。"));
            sb.AppendLine(SimTranslation.TOrFallback("RSMF.CustomerReview.Split.OpeningBanRule", "除非本条冲动要求商品或价格，否则不要用“买了、花了、囤了、拿了、入手了、这次买、实际付款”开头。"));
            sb.AppendLine(SimTranslation.TOrFallback("RSMF.CustomerReview.Split.EmptyDataRule", "空、无、普通、未知、测试类资料直接忽略，不要写进玩家可见文本。"));
            sb.AppendLine(SimTranslation.TOrFallback("RSMF.CustomerReview.Split.StarsRule", "stars 按顾客主观心情给，1 到 5 都可用，不把 3 星当默认值。"));
            if (IsBuiltInNodeEnabled(settings, NodeLexicon))
                AppendLexicon(sb, settings);
            AppendStableCustomNodes(sb, settings, false);
            return sb.ToString();
        }

        /// <summary>
        /// 构造 XML 稳定前缀，负责让服务端看到字节稳定的开头消息。
        /// </summary>
        private static string BuildStableXmlPrefix(SimManagementLibSettings settings)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<customerReviewStablePrefix>");
            AppendXmlTextElement(sb, 1, "rootPrompt", settings?.reviewUserPrompt ?? CustomerReviewPromptDefaults.UserPrompt);
            AppendXmlTextElement(sb, 1, "inputStructureNote", SimTranslation.T("RSMF.CustomerReview.InputStructureNote"));
            AppendXmlOpen(sb, 1, "languageContext", 0);
            AppendXmlTextElement(sb, 2, "rimworldLanguageFolder", SimTranslation.ActiveLanguageFolderName);
            AppendXmlTextElement(sb, 2, "rimworldLanguageName", SimTranslation.ActiveLanguageDisplayName);
            AppendXmlTextElement(sb, 2, "outputLanguageRule", SimTranslation.T("RSMF.CustomerReview.OutputLanguageRule"));
            AppendXmlClose(sb, 1, "languageContext");
            AppendXmlOpen(sb, 1, "stableWritingArchitecture", 1);
            AppendXmlTextElement(sb, 2, "jsonContract", SimTranslation.TOrFallback("RSMF.CustomerReview.Split.JsonContract", "只输出一个 JSON 对象，字段固定为 nickname、stars、reviewText、upvoteReviewId、downvoteReviewId、replyToReviewId、replyText、replyStance、tags。"));
            AppendXmlTextElement(sb, 2, "cacheRule", SimTranslation.TOrFallback("RSMF.CustomerReview.Split.CacheRule", "前面的规则是稳定前缀；最后一条顾客资料才是本次素材，不要续写或复读旧评价。"));
            AppendXmlTextElement(sb, 2, "realVoiceRule", SimTranslation.TOrFallback("RSMF.CustomerReview.Split.RealVoiceRule", "reviewText 像刚离店随手发的论坛短帖，从一句当下反应切入，不写字段清单、质检报告或商品收据。"));
            AppendXmlTextElement(sb, 2, "optionalFactsRule", SimTranslation.TOrFallback("RSMF.CustomerReview.Split.OptionalFactsRule", "optionalFacts 只是备查资料，只有成为本条情绪焦点时才写入正文。"));
            AppendXmlTextElement(sb, 2, "ageUseRule", SimTranslation.TOrFallback("RSMF.CustomerReview.Split.AgeUseRule", "年龄、寿命、活了多久是低优先级身份素材；多数时候只当背景，不主动拿来开头，只有它和本次商品或体验形成明显反差时才自然带一句。"));
            AppendXmlTextElement(sb, 2, "openingBanRule", SimTranslation.TOrFallback("RSMF.CustomerReview.Split.OpeningBanRule", "除非本条冲动要求商品或价格，否则不要用“买了、花了、囤了、拿了、入手了、这次买、实际付款”开头。"));
            AppendXmlTextElement(sb, 2, "emptyDataRule", SimTranslation.TOrFallback("RSMF.CustomerReview.Split.EmptyDataRule", "空、无、普通、未知、测试类资料直接忽略，不要写进玩家可见文本。"));
            AppendXmlTextElement(sb, 2, "starsRule", SimTranslation.TOrFallback("RSMF.CustomerReview.Split.StarsRule", "stars 按顾客主观心情给，1 到 5 都可用，不把 3 星当默认值。"));
            AppendXmlClose(sb, 1, "stableWritingArchitecture");
            if (IsBuiltInNodeEnabled(settings, NodeLexicon))
                AppendXmlLexicon(sb, settings, 2);
            AppendStableCustomNodes(sb, settings, true);
            sb.AppendLine("</customerReviewStablePrefix>");
            return sb.ToString();
        }

        /// <summary>
        /// 构造普通文本动态提示词，负责提供当前顾客资料和本条口吻入口。
        /// </summary>
        private static string BuildDynamicPlainPrompt(CustomerReviewSnapshot snapshot, SimManagementLibSettings settings, string antiRepeatContext)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(SimTranslation.TOrFallback("RSMF.CustomerReview.Split.DynamicHeader", "当前顾客资料:"));
            AppendDynamicPlainWritingStrategy(sb, snapshot, settings);
            AppendDynamicPlainNodeList(sb, snapshot, settings);
            AppendPlainOptionalFacts(sb, snapshot);
            AppendPlainAntiRepeat(sb, antiRepeatContext);
            sb.AppendLine(SimTranslation.TOrFallback("RSMF.CustomerReview.Split.FinalRule", "现在只根据这一位顾客写一条新的论坛评价，返回 JSON 对象。"));
            return sb.ToString();
        }

        /// <summary>
        /// 构造 XML 动态提示词，负责把易变资料集中放在最后一条消息。
        /// </summary>
        private static string BuildDynamicXmlPrompt(CustomerReviewSnapshot snapshot, SimManagementLibSettings settings, string antiRepeatContext)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<customerReviewDynamicInput>");
            AppendXmlDynamicWritingStrategy(sb, snapshot, settings);
            List<string> ids = GetOrderedEnabledAllNodeIds(settings);
            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];
                if (id.StartsWith("custom_", StringComparison.Ordinal) || id == NodeWritingStrategy || id == NodeLexicon)
                    continue;
                if (id == NodeShopping)
                {
                    AppendXmlCompactShopping(sb, snapshot, i + 1);
                    continue;
                }
                if (id == NodeIdentity)
                {
                    AppendXmlCompactIdentity(sb, snapshot, i + 1);
                    continue;
                }
                AppendXmlBuiltInNode(sb, id, snapshot, settings, i + 1);
            }
            AppendXmlOptionalFacts(sb, snapshot);
            if (!string.IsNullOrWhiteSpace(antiRepeatContext))
            {
                AppendXmlOpen(sb, 1, "antiRepeatContext", 80);
                AppendXmlTextElement(sb, 2, "memo", antiRepeatContext);
                AppendXmlClose(sb, 1, "antiRepeatContext");
            }
            AppendXmlTextElement(sb, 1, "finalRule", SimTranslation.TOrFallback("RSMF.CustomerReview.Split.FinalRule", "现在只根据这一位顾客写一条新的论坛评价，返回 JSON 对象。"));
            sb.AppendLine("</customerReviewDynamicInput>");
            return sb.ToString();
        }

        /// <summary>
        /// 追加动态写作策略，负责把每条评价的冲动和焦点放在动态资料最前面。
        /// </summary>
        private static void AppendDynamicPlainWritingStrategy(StringBuilder sb, CustomerReviewSnapshot snapshot, SimManagementLibSettings settings)
        {
            if (!IsBuiltInNodeEnabled(settings, NodeWritingStrategy))
                return;

            sb.AppendLine(SimTranslation.TOrFallback("RSMF.CustomerReview.Split.ImpulseLine", "- currentImpulse: {value}").Replace("{value}", BuildCurrentImpulse(snapshot)));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.HumanVoiceLine", BuildHumanVoiceGuidance(snapshot).Named("value")));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.FocusLine", BuildReviewFocusGuidance(snapshot).Named("value")));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.SubjectiveRule"));
            AppendAbsurdNitpickGuidance(sb, snapshot, settings);
            sb.AppendLine();
        }

        /// <summary>
        /// 追加 XML 动态写作策略，负责让模型优先看到本条独立口吻。
        /// </summary>
        private static void AppendXmlDynamicWritingStrategy(StringBuilder sb, CustomerReviewSnapshot snapshot, SimManagementLibSettings settings)
        {
            if (!IsBuiltInNodeEnabled(settings, NodeWritingStrategy))
                return;

            AppendXmlOpen(sb, 1, "dynamicWritingStrategy", 1);
            AppendXmlTextElement(sb, 2, "currentImpulse", BuildCurrentImpulse(snapshot));
            AppendXmlTextElement(sb, 2, "humanVoice", BuildHumanVoiceGuidance(snapshot));
            AppendXmlTextElement(sb, 2, "focus", BuildReviewFocusGuidance(snapshot));
            AppendXmlTextElement(sb, 2, "subjectiveRule", SimTranslation.T("RSMF.CustomerReview.Xml.SubjectiveRule"));
            if (settings != null && settings.reviewAbsurdNitpickEnabled && StableChance((snapshot?.reviewId ?? "") + "|absurd-nitpick", settings.reviewAbsurdNitpickChance))
                AppendXmlTextElement(sb, 2, "absurdNitpickPermission", SimTranslation.T("RSMF.CustomerReview.Xml.AbsurdNitpickPermission"));
            AppendXmlClose(sb, 1, "dynamicWritingStrategy");
        }

        /// <summary>
        /// 按玩家启用列表追加普通文本动态节点，负责复用原有节点编辑设置。
        /// </summary>
        private static void AppendDynamicPlainNodeList(StringBuilder sb, CustomerReviewSnapshot snapshot, SimManagementLibSettings settings)
        {
            List<string> ids = GetOrderedEnabledAllNodeIds(settings);
            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];
                if (id.StartsWith("custom_", StringComparison.Ordinal) || id == NodeWritingStrategy || id == NodeLexicon)
                    continue;
                if (id == NodeShopping)
                {
                    AppendCompactShopping(sb, snapshot);
                    continue;
                }
                if (id == NodeIdentity)
                {
                    AppendCompactIdentity(sb, snapshot);
                    continue;
                }
                AppendBuiltInNode(sb, id, snapshot, settings);
            }
        }

        /// <summary>
        /// 追加稳定自定义节点，负责保留玩家写入的额外提示词且不破坏动态资料末尾结构。
        /// </summary>
        private static void AppendStableCustomNodes(StringBuilder sb, SimManagementLibSettings settings, bool xml)
        {
            List<CustomerReviewCustomPromptNode> nodes = ParseCustomNodes(settings?.reviewPromptCustomNodes);
            int priority = 30;
            for (int i = 0; i < nodes.Count; i++)
            {
                CustomerReviewCustomPromptNode node = nodes[i];
                if (node == null || !node.enabled || string.IsNullOrWhiteSpace(node.body))
                    continue;

                if (xml)
                {
                    string pad = Indent(1);
                    sb.Append(pad).Append("<customPromptNode priority=\"").Append(priority++).Append("\" label=\"").Append(EscapeXml(string.IsNullOrWhiteSpace(node.label) ? node.id : node.label)).AppendLine("\">");
                    AppendXmlTextElement(sb, 2, "body", node.body);
                    sb.Append(pad).AppendLine("</customPromptNode>");
                }
                else
                {
                    sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.CustomNodeHeader", (string.IsNullOrWhiteSpace(node.label) ? node.id : node.label).Named("label")));
                    sb.AppendLine(node.body);
                    sb.AppendLine();
                }
            }
        }

        /// <summary>
        /// 追加压缩身份节点，负责保留稳定画像但降低年龄和履历对正文的诱导。
        /// </summary>
        private static void AppendCompactIdentity(StringBuilder sb, CustomerReviewSnapshot snapshot)
        {
            if (snapshot == null)
                return;

            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.IdentityHeader"));
            sb.AppendLine("- kind: " + snapshot.kindLabel + " / " + snapshot.kindId);
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.KindDescriptionLine", EmptyAsNone(snapshot.kindDescription).Named("value")));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.RaceLine", snapshot.raceLabel.Named("value")));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.NameLine", snapshot.customerDisplayName.Named("value")));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Split.AgeLowPriorityLine", snapshot.ageSummary.Named("value")));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.BackstoryNamesLine", snapshot.backstorySummary.Named("value")));
            AppendMeaningfulLine(sb, "RSMF.CustomerReview.Plain.TraitsLine", snapshot.traitSummary);
            AppendMeaningfulLine(sb, "RSMF.CustomerReview.Plain.XenotypeLine", snapshot.xenotypeSummary);
            AppendMeaningfulLine(sb, "RSMF.CustomerReview.Plain.GenesLine", snapshot.geneSummary);
            sb.AppendLine(SimTranslation.TOrFallback("RSMF.CustomerReview.Split.IdentityReviewRule", "- 身份资料只提供口吻底色；年龄、寿命、背景故事名称不要连续复用，只有与本次体验有具体反差时才写进 reviewText。"));
            sb.AppendLine();
        }

        /// <summary>
        /// 追加 XML 压缩身份节点，负责避免年龄字段成为短评正文的高优先级素材。
        /// </summary>
        private static void AppendXmlCompactIdentity(StringBuilder sb, CustomerReviewSnapshot snapshot, int priority)
        {
            AppendXmlOpen(sb, 1, "identity", priority);
            AppendXmlTextElement(sb, 2, "nicknameSourceRule", SimTranslation.T("RSMF.CustomerReview.Xml.NicknameSourceRule"));
            AppendXmlTextElement(sb, 2, "identityReviewRule", SimTranslation.TOrFallback("RSMF.CustomerReview.Split.IdentityReviewRule", "身份资料只提供口吻底色；年龄、寿命、背景故事名称不要连续复用，只有与本次体验有具体反差时才写进 reviewText。"));
            AppendXmlTextElement(sb, 2, "kind", (snapshot?.kindLabel ?? "") + " / " + (snapshot?.kindId ?? ""));
            AppendXmlTextElement(sb, 2, "kindDescription", snapshot?.kindDescription);
            AppendXmlTextElement(sb, 2, "race", snapshot?.raceLabel);
            AppendXmlTextElement(sb, 2, "realName", snapshot?.customerDisplayName);
            AppendXmlTextElement(sb, 2, "lowPriorityAge", snapshot?.ageSummary);
            AppendXmlTextElement(sb, 2, "backstoryNames", snapshot?.backstorySummary);
            AppendXmlTextElement(sb, 2, "traits", snapshot?.traitSummary);
            AppendXmlTextElement(sb, 2, "xenotype", snapshot?.xenotypeSummary);
            AppendXmlTextElement(sb, 2, "genes", snapshot?.geneSummary);
            AppendXmlClose(sb, 1, "identity");
        }

        /// <summary>
        /// 追加有意义的普通文本资料行，负责减少无、普通和未知资料进入动态提示词。
        /// </summary>
        private static void AppendMeaningfulLine(StringBuilder sb, string key, string value)
        {
            if (IsMeaninglessPromptValue(value))
                return;

            sb.AppendLine(SimTranslation.T(key, value.Named("value")));
        }

        /// <summary>
        /// 追加压缩购物节点，负责保留消费结果但降低商品说明对短评正文的主导权。
        /// </summary>
        private static void AppendCompactShopping(StringBuilder sb, CustomerReviewSnapshot snapshot)
        {
            if (snapshot == null)
                return;

            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.ShoppingHeader"));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.ShoppingWritingRule"));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.ShopLine", snapshot.zoneLabel.Named("value")));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.BudgetLine", snapshot.budgetSummary.Named("value")));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.SpentSilverLine", snapshot.spentSilver.ToString("F0", System.Globalization.CultureInfo.InvariantCulture).Named("value")));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.PurchasedLine", StripDescriptionDetails(snapshot.purchasedSummary).Named("value")));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.ServiceLine", snapshot.serviceSummary.Named("value")));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.PostPurchaseLine", EmptyAsNone(snapshot.postPurchaseSummary).Named("value")));
            sb.AppendLine();
        }

        /// <summary>
        /// 追加 XML 压缩购物节点，负责把购买清单压成结果信号而不是商品介绍。
        /// </summary>
        private static void AppendXmlCompactShopping(StringBuilder sb, CustomerReviewSnapshot snapshot, int priority)
        {
            AppendXmlOpen(sb, 1, "shoppingExperience", priority);
            AppendXmlTextElement(sb, 2, "writingRule", SimTranslation.T("RSMF.CustomerReview.Xml.ShoppingWritingRule"));
            AppendXmlTextElement(sb, 2, "shop", snapshot?.zoneLabel);
            AppendXmlTextElement(sb, 2, "budget", snapshot?.budgetSummary);
            AppendXmlTextElement(sb, 2, "spentSilver", snapshot == null ? "" : SimTranslation.T("RSMF.CustomerReview.SilverAmount", snapshot.spentSilver.ToString("F0", System.Globalization.CultureInfo.InvariantCulture).Named("value")));
            AppendXmlTextElement(sb, 2, "purchasedItems", StripDescriptionDetails(snapshot?.purchasedSummary));
            AppendXmlTextElement(sb, 2, "service", snapshot?.serviceSummary);
            AppendXmlTextElement(sb, 2, "postPurchase", snapshot?.postPurchaseSummary);
            AppendXmlClose(sb, 1, "shoppingExperience");
        }

        /// <summary>
        /// 追加普通文本备查事实，负责把商品说明降权为可选资料而不是正文提纲。
        /// </summary>
        private static void AppendPlainOptionalFacts(StringBuilder sb, CustomerReviewSnapshot snapshot)
        {
            sb.AppendLine(SimTranslation.TOrFallback("RSMF.CustomerReview.Split.OptionalFactsHeader", "optionalFacts 备查资料:"));
            sb.AppendLine(SimTranslation.TOrFallback("RSMF.CustomerReview.Split.GoodsSignalLine", "- goodsSignal: {value}").Replace("{value}", BuildGoodsSignal(snapshot)));
            sb.AppendLine(SimTranslation.TOrFallback("RSMF.CustomerReview.Split.BudgetSignalLine", "- budgetSignal: {value}").Replace("{value}", BuildBudgetSignal(snapshot)));
            sb.AppendLine(SimTranslation.TOrFallback("RSMF.CustomerReview.Split.ItemNamesLine", "- itemNames: {value}").Replace("{value}", BuildFeaturedItemNames(snapshot)));
            sb.AppendLine(SimTranslation.TOrFallback("RSMF.CustomerReview.Split.RawPurchaseLine", "- rawPurchasedItems: {value}").Replace("{value}", StripDescriptionDetails(snapshot?.purchasedSummary)));
            sb.AppendLine();
        }

        /// <summary>
        /// 追加 XML 备查事实，负责压低商品说明对正文风格的主导权。
        /// </summary>
        private static void AppendXmlOptionalFacts(StringBuilder sb, CustomerReviewSnapshot snapshot)
        {
            AppendXmlOpen(sb, 1, "optionalFacts", 70);
            AppendXmlTextElement(sb, 2, "goodsSignal", BuildGoodsSignal(snapshot));
            AppendXmlTextElement(sb, 2, "budgetSignal", BuildBudgetSignal(snapshot));
            AppendXmlTextElement(sb, 2, "itemNames", BuildFeaturedItemNames(snapshot));
            AppendXmlTextElement(sb, 2, "rawPurchasedItems", StripDescriptionDetails(snapshot?.purchasedSummary));
            AppendXmlClose(sb, 1, "optionalFacts");
        }

        /// <summary>
        /// 追加普通文本反重复备忘，负责让动态消息末尾提醒模型换开头和标签。
        /// </summary>
        private static void AppendPlainAntiRepeat(StringBuilder sb, string antiRepeatContext)
        {
            if (string.IsNullOrWhiteSpace(antiRepeatContext))
                return;

            sb.AppendLine(antiRepeatContext.Trim());
            sb.AppendLine();
        }

        /// <summary>
        /// 构造本条当前冲动，负责让同类消费从不同真人瞬间切入。
        /// </summary>
        private static string BuildCurrentImpulse(CustomerReviewSnapshot snapshot)
        {
            string[] modes =
            {
                SimTranslation.TOrFallback("RSMF.CustomerReview.Impulse.0", "先说一句离店后的即时感受，再轻轻落到这次体验。"),
                SimTranslation.TOrFallback("RSMF.CustomerReview.Impulse.1", "从赶路、带走、马上要用的担心切入，不要先报商品清单。"),
                SimTranslation.TOrFallback("RSMF.CustomerReview.Impulse.2", "从嘴硬或挑刺切入，允许明明还行也不想夸满。"),
                SimTranslation.TOrFallback("RSMF.CustomerReview.Impulse.3", "从预算戒备或花钱后的心里嘀咕切入，但不要写成算账。"),
                SimTranslation.TOrFallback("RSMF.CustomerReview.Impulse.4", "从店里某个小感受切入，只抓一个环境或流程细节。"),
                SimTranslation.TOrFallback("RSMF.CustomerReview.Impulse.5", "从顾客特性、职业习惯或过往经历带来的一点偏见切入，再落到本次体验；年龄和寿命不是默认入口。"),
                SimTranslation.TOrFallback("RSMF.CustomerReview.Impulse.6", "像论坛楼里接话一样先赞同或不服，再补一句自己的体验。"),
                SimTranslation.TOrFallback("RSMF.CustomerReview.Impulse.7", "写一个很短的结论，允许半句话和临场情绪，不解释完整原因。"),
                SimTranslation.TOrFallback("RSMF.CustomerReview.Impulse.8", "从买完后的用途或后果切入，商品名不是重点。")
            };

            int index = StableIndex((snapshot?.reviewId ?? "") + "|current-impulse", modes.Length);
            return modes[index];
        }

        /// <summary>
        /// 构造购物信号摘要，负责把商品事实转成用途和结果。
        /// </summary>
        private static string BuildGoodsSignal(CustomerReviewSnapshot snapshot)
        {
            if (snapshot == null)
                return SimTranslation.T("RSMF.Common.None");

            string itemNames = BuildFeaturedItemNames(snapshot);
            if (!IsMeaninglessPromptValue(itemNames))
                return SimTranslation.TOrFallback("RSMF.CustomerReview.Split.GoodsSignalWithItems", "带走了这些东西: {items}。正文可以只写用途或感受，不必复述清单。").Replace("{items}", itemNames);

            string purchased = StripDescriptionDetails(snapshot.purchasedSummary);
            if (!IsMeaninglessPromptValue(purchased))
                return SimTranslation.TOrFallback("RSMF.CustomerReview.Split.GoodsSignalFromSummary", "完成了一次购物；正文只在情绪需要时提商品。");

            return SimTranslation.TOrFallback("RSMF.CustomerReview.Split.GoodsSignalNone", "没有可写的明确商品焦点。");
        }

        /// <summary>
        /// 构造预算信号摘要，负责让模型知道消费强弱但不强迫正文算钱。
        /// </summary>
        private static string BuildBudgetSignal(CustomerReviewSnapshot snapshot)
        {
            if (snapshot == null)
                return SimTranslation.T("RSMF.Common.None");

            if (ContainsAny(snapshot.serviceSummary, "完成了免费服务", "fee 0", "费用 0"))
                return SimTranslation.TOrFallback("RSMF.CustomerReview.Split.BudgetSignalFreeService", "完成了免费服务，没有花钱；免费本身不是差评理由，按等待、完成状态和服务内容判断。");

            if (ContainsAny(snapshot.budgetSummary, "价格观察", "价格远高于市价", "拒绝购买"))
                return SimTranslation.TOrFallback("RSMF.CustomerReview.Split.BudgetSignalPriceRejected", "顾客发现目标商品价格远高于市价并拒绝购买；这是真实负面体验，可以结合预算和是否买到其他东西评价。");

            if (snapshot.spentSilver <= 0f)
                return SimTranslation.TOrFallback("RSMF.CustomerReview.Split.BudgetSignalNoSpend", "没有成功花钱或没有记录到花费。");

            string budget = StripDescriptionDetails(snapshot.budgetSummary);
            if (!IsMeaninglessPromptValue(budget))
                return SimTranslation.TOrFallback("RSMF.CustomerReview.Split.BudgetSignalSpend", "花费 {spent} 银；预算背景: {budget}。正文不要默认写金额。")
                    .Replace("{spent}", snapshot.spentSilver.ToString("F0", System.Globalization.CultureInfo.InvariantCulture))
                    .Replace("{budget}", budget);

            return SimTranslation.TOrFallback("RSMF.CustomerReview.Split.BudgetSignalSpendNoBudget", "花费 {spent} 银；正文不要默认写金额。")
                .Replace("{spent}", snapshot.spentSilver.ToString("F0", System.Globalization.CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// 构造展示商品名称列表，负责提供比完整说明更轻的购物线索。
        /// </summary>
        private static string BuildFeaturedItemNames(CustomerReviewSnapshot snapshot)
        {
            if (snapshot?.featuredItems == null || snapshot.featuredItems.Count == 0)
                return SimTranslation.T("RSMF.Common.None");

            List<string> names = new List<string>();
            for (int i = 0; i < snapshot.featuredItems.Count && names.Count < 6; i++)
            {
                ReviewFeaturedItem item = snapshot.featuredItems[i];
                if (item == null || string.IsNullOrWhiteSpace(item.label))
                    continue;

                string label = item.label.Trim();
                if (item.count > 1)
                    label += " x" + item.count;
                names.Add(label);
            }

            return names.Count == 0 ? SimTranslation.T("RSMF.Common.None") : string.Join(SimTranslation.T("RSMF.Common.ListSeparator"), names.ToArray());
        }

        /// <summary>
        /// 去掉长说明文本，负责避免物品 Def 描述压过真人短评口吻。
        /// </summary>
        private static string StripDescriptionDetails(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return SimTranslation.T("RSMF.Common.None");

            string text = value.Trim();
            string[] markers = { "，说明:", ", description:", " description:", "，描述:", ", desc:" };
            for (int i = 0; i < markers.Length; i++)
            {
                int index = text.IndexOf(markers[i], StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                    text = text.Substring(0, index);
            }

            return text.Length > 220 ? text.Substring(0, 220) : text;
        }

        /// <summary>
        /// 判断文本是否包含任意片段，负责识别免费服务等评价提示信号。
        /// </summary>
        private static bool ContainsAny(string text, params string[] words)
        {
            if (string.IsNullOrEmpty(text) || words == null)
                return false;

            for (int i = 0; i < words.Length; i++)
            {
                string word = words[i];
                if (!string.IsNullOrEmpty(word) && text.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }
    }
}
