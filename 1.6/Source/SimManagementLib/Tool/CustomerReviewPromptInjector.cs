using SimManagementLib.Pojo;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 定义顾客评价提示词节点，负责描述可注入片段的身份、名称和默认启用状态。
    /// </summary>
    public class CustomerReviewPromptNodeDef
    {
        public string id = "";
        public string label = "";
        public string description = "";
        public bool defaultEnabled;
    }

    /// <summary>
    /// 保存玩家自定义提示词节点，负责持久化名称、正文和启用状态。
    /// </summary>
    public class CustomerReviewCustomPromptNode
    {
        public string id = "";
        public string label = "";
        public string body = "";
        public bool enabled = true;
    }

    /// <summary>
    /// 负责构造顾客评价提示词节点，支持内置资料节点、自定义节点和玩家排序。
    /// </summary>
    public static partial class CustomerReviewPromptInjector
    {
        public const string PromptInputFormatPlain = "plain";
        public const string PromptInputFormatXml = "xml";
        public const string NodeWritingStrategy = "writing_strategy";
        public const string NodeIdentity = "identity";
        public const string NodeTemporaryState = "temporary_state";
        public const string NodeShopping = "shopping";
        public const string NodeCheckout = "checkout";
        public const string NodeEnvironment = "environment";
        public const string NodeRoom = "room";
        public const string NodeRelations = "relations";
        public const string NodeWeather = "weather";
        public const string NodeGameConditions = "game_conditions";
        public const string NodeColonyWealth = "colony_wealth";
        public const string NodeColonyShops = "colony_shops";
        public const string NodeColonyLeader = "colony_leader";
        public const string NodeColonyCulture = "colony_culture";
        public const string NodeForum = "forum";
        public const string NodeLexicon = "lexicon";

        private static readonly List<CustomerReviewPromptNodeDef> BuiltInNodes = new List<CustomerReviewPromptNodeDef>
        {
            new CustomerReviewPromptNodeDef { id = NodeWritingStrategy, label = SimTranslation.T("RSMF.CustomerReview.Node.WritingStrategy.Label"), description = SimTranslation.T("RSMF.CustomerReview.Node.WritingStrategy.Desc"), defaultEnabled = true },
            new CustomerReviewPromptNodeDef { id = NodeIdentity, label = SimTranslation.T("RSMF.CustomerReview.Node.Identity.Label"), description = SimTranslation.T("RSMF.CustomerReview.Node.Identity.Desc"), defaultEnabled = true },
            new CustomerReviewPromptNodeDef { id = NodeTemporaryState, label = SimTranslation.T("RSMF.CustomerReview.Node.TemporaryState.Label"), description = SimTranslation.T("RSMF.CustomerReview.Node.TemporaryState.Desc"), defaultEnabled = true },
            new CustomerReviewPromptNodeDef { id = NodeShopping, label = SimTranslation.T("RSMF.CustomerReview.Node.Shopping.Label"), description = SimTranslation.T("RSMF.CustomerReview.Node.Shopping.Desc"), defaultEnabled = true },
            new CustomerReviewPromptNodeDef { id = NodeCheckout, label = SimTranslation.T("RSMF.CustomerReview.Node.Checkout.Label"), description = SimTranslation.T("RSMF.CustomerReview.Node.Checkout.Desc"), defaultEnabled = true },
            new CustomerReviewPromptNodeDef { id = NodeEnvironment, label = SimTranslation.T("RSMF.CustomerReview.Node.Environment.Label"), description = SimTranslation.T("RSMF.CustomerReview.Node.Environment.Desc"), defaultEnabled = true },
            new CustomerReviewPromptNodeDef { id = NodeRoom, label = SimTranslation.T("RSMF.CustomerReview.Node.Room.Label"), description = SimTranslation.T("RSMF.CustomerReview.Node.Room.Desc"), defaultEnabled = true },
            new CustomerReviewPromptNodeDef { id = NodeRelations, label = SimTranslation.T("RSMF.CustomerReview.Node.Relations.Label"), description = SimTranslation.T("RSMF.CustomerReview.Node.Relations.Desc"), defaultEnabled = true },
            new CustomerReviewPromptNodeDef { id = NodeWeather, label = SimTranslation.T("RSMF.CustomerReview.Node.Weather.Label"), description = SimTranslation.T("RSMF.CustomerReview.Node.Weather.Desc"), defaultEnabled = true },
            new CustomerReviewPromptNodeDef { id = NodeGameConditions, label = SimTranslation.T("RSMF.CustomerReview.Node.GameConditions.Label"), description = SimTranslation.T("RSMF.CustomerReview.Node.GameConditions.Desc"), defaultEnabled = true },
            new CustomerReviewPromptNodeDef { id = NodeColonyWealth, label = SimTranslation.T("RSMF.CustomerReview.Node.ColonyWealth.Label"), description = SimTranslation.T("RSMF.CustomerReview.Node.ColonyWealth.Desc"), defaultEnabled = true },
            new CustomerReviewPromptNodeDef { id = NodeColonyShops, label = SimTranslation.T("RSMF.CustomerReview.Node.ColonyShops.Label"), description = SimTranslation.T("RSMF.CustomerReview.Node.ColonyShops.Desc"), defaultEnabled = true },
            new CustomerReviewPromptNodeDef { id = NodeColonyLeader, label = SimTranslation.T("RSMF.CustomerReview.Node.ColonyLeader.Label"), description = SimTranslation.T("RSMF.CustomerReview.Node.ColonyLeader.Desc"), defaultEnabled = true },
            new CustomerReviewPromptNodeDef { id = NodeColonyCulture, label = SimTranslation.T("RSMF.CustomerReview.Node.ColonyCulture.Label"), description = SimTranslation.T("RSMF.CustomerReview.Node.ColonyCulture.Desc"), defaultEnabled = true },
            new CustomerReviewPromptNodeDef { id = NodeForum, label = SimTranslation.T("RSMF.CustomerReview.Node.Forum.Label"), description = SimTranslation.T("RSMF.CustomerReview.Node.Forum.Desc"), defaultEnabled = true },
            new CustomerReviewPromptNodeDef { id = NodeLexicon, label = SimTranslation.T("RSMF.CustomerReview.Node.Lexicon.Label"), description = SimTranslation.T("RSMF.CustomerReview.Node.Lexicon.Desc"), defaultEnabled = true }
        };

        /// <summary>
        /// 返回所有内置节点定义，负责设置界面绘制节点库。
        /// </summary>
        public static IReadOnlyList<CustomerReviewPromptNodeDef> AllBuiltInNodes => BuiltInNodes;

        /// <summary>
        /// 构造完整用户提示词，负责固定根提示词并按玩家优先级追加已启用节点。
        /// </summary>
        public static string BuildPrompt(CustomerReviewSnapshot snapshot, SimManagementLibSettings settings)
        {
            if (settings == null || settings.reviewPromptInputFormat == PromptInputFormatPlain)
                return BuildPlainTextPrompt(snapshot, settings);
            return BuildXmlPrompt(snapshot, settings);
        }

        /// <summary>
        /// 构造普通文本用户提示词，负责兼容旧版提示词组织方式。
        /// </summary>
        public static string BuildPlainTextPrompt(CustomerReviewSnapshot snapshot, SimManagementLibSettings settings)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(settings?.reviewUserPrompt ?? CustomerReviewPromptDefaults.UserPrompt);
            sb.AppendLine();
            AppendLanguageContext(sb);
            sb.AppendLine();
            foreach (string id in GetOrderedEnabledAllNodeIds(settings))
            {
                if (id.StartsWith("custom_", StringComparison.Ordinal))
                    AppendCustomNodeById(sb, settings, id);
                else
                    AppendBuiltInNode(sb, id, snapshot, settings);
            }
            return sb.ToString();
        }

        /// <summary>
        /// 构造 XML 风格用户提示词，负责用标签分隔根提示词、内置节点和自定义节点。
        /// </summary>
        public static string BuildXmlPrompt(CustomerReviewSnapshot snapshot, SimManagementLibSettings settings)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<customerReviewPrompt>");
            AppendXmlTextElement(sb, 1, "rootPrompt", settings?.reviewUserPrompt ?? CustomerReviewPromptDefaults.UserPrompt);
            AppendXmlTextElement(sb, 1, "inputStructureNote", SimTranslation.T("RSMF.CustomerReview.InputStructureNote"));
            AppendXmlOpen(sb, 1, "languageContext", 0);
            AppendXmlTextElement(sb, 2, "rimworldLanguageFolder", SimTranslation.ActiveLanguageFolderName);
            AppendXmlTextElement(sb, 2, "rimworldLanguageName", SimTranslation.ActiveLanguageDisplayName);
            AppendXmlTextElement(sb, 2, "outputLanguageRule", SimTranslation.T("RSMF.CustomerReview.OutputLanguageRule"));
            AppendXmlClose(sb, 1, "languageContext");

            List<string> ids = GetOrderedEnabledAllNodeIds(settings);
            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];
                if (id.StartsWith("custom_", StringComparison.Ordinal))
                    AppendXmlCustomNodeById(sb, settings, id, i + 1);
                else
                    AppendXmlBuiltInNode(sb, id, snapshot, settings, i + 1);
            }

            sb.AppendLine("</customerReviewPrompt>");
            return sb.ToString();
        }

        /// <summary>
        /// 追加普通文本语言环境，负责让 AI 点评跟随当前 RimWorld 语言。
        /// </summary>
        private static void AppendLanguageContext(StringBuilder sb)
        {
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.LanguageContextHeader"));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.LanguageFolderLine", SimTranslation.ActiveLanguageFolderName.Named("folder")));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.LanguageNameLine", SimTranslation.ActiveLanguageDisplayName.Named("language")));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.OutputLanguageLine"));
        }

        /// <summary>
        /// 返回按玩家优先级排序的已启用内置节点编号。
        /// </summary>
        public static List<string> GetOrderedEnabledNodeIds(SimManagementLibSettings settings)
        {
            List<string> enabled = ParseIdList(settings?.reviewPromptEnabledNodeIds);
            if (enabled.Count == 0)
                enabled = BuiltInNodes.Where(n => n.defaultEnabled).Select(n => n.id).ToList();

            List<string> order = ParseIdList(settings?.reviewPromptNodeOrder);
            List<string> result = new List<string>();
            for (int i = 0; i < order.Count; i++)
            {
                string id = order[i];
                if (enabled.Contains(id) && IsBuiltInNode(id) && !result.Contains(id))
                    result.Add(id);
            }
            for (int i = 0; i < enabled.Count; i++)
            {
                string id = enabled[i];
                if (IsBuiltInNode(id) && !result.Contains(id))
                    result.Add(id);
            }
            return result;
        }

        /// <summary>
        /// 返回按玩家优先级排序的全部已启用节点编号，负责统一内置节点和自定义节点排序。
        /// </summary>
        public static List<string> GetOrderedEnabledAllNodeIds(SimManagementLibSettings settings)
        {
            List<string> enabledBuiltIns = GetOrderedEnabledNodeIds(settings);
            List<CustomerReviewCustomPromptNode> customNodes = ParseCustomNodes(settings?.reviewPromptCustomNodes);
            List<string> enabledCustom = customNodes.Where(n => n != null && n.enabled).Select(n => n.id).Where(s => !string.IsNullOrEmpty(s)).ToList();
            List<string> order = ParseAnyIdList(settings?.reviewPromptNodeOrder);
            List<string> result = new List<string>();

            for (int i = 0; i < order.Count; i++)
            {
                string id = order[i];
                if ((enabledBuiltIns.Contains(id) || enabledCustom.Contains(id)) && !result.Contains(id))
                    result.Add(id);
            }
            for (int i = 0; i < enabledBuiltIns.Count; i++)
            {
                if (!result.Contains(enabledBuiltIns[i]))
                    result.Add(enabledBuiltIns[i]);
            }
            for (int i = 0; i < enabledCustom.Count; i++)
            {
                if (!result.Contains(enabledCustom[i]))
                    result.Add(enabledCustom[i]);
            }
            return result;
        }

        /// <summary>
        /// 设置内置节点启用状态，负责保持启用列表和排序列表同步。
        /// </summary>
        public static void SetBuiltInNodeEnabled(SimManagementLibSettings settings, string id, bool enabled)
        {
            if (settings == null || !IsBuiltInNode(id))
                return;

            List<string> enabledIds = ParseIdList(settings.reviewPromptEnabledNodeIds);
            if (enabledIds.Count == 0)
                enabledIds = BuiltInNodes.Where(n => n.defaultEnabled).Select(n => n.id).ToList();
            if (enabled && !enabledIds.Contains(id))
                enabledIds.Add(id);
            if (!enabled)
                enabledIds.Remove(id);
            settings.reviewPromptEnabledNodeIds = string.Join(",", enabledIds.ToArray());

            List<string> order = ParseAnyIdList(settings.reviewPromptNodeOrder);
            if (enabled && !order.Contains(id))
                order.Add(id);
            settings.reviewPromptNodeOrder = string.Join(",", order.Where(IsKnownNodeId(settings)).Distinct().ToArray());
        }

        /// <summary>
        /// 判断内置节点是否启用，负责设置界面复用默认状态。
        /// </summary>
        public static bool IsBuiltInNodeEnabled(SimManagementLibSettings settings, string id)
        {
            List<string> enabled = ParseIdList(settings?.reviewPromptEnabledNodeIds);
            if (enabled.Count == 0)
                enabled = BuiltInNodes.Where(n => n.defaultEnabled).Select(n => n.id).ToList();
            return enabled.Contains(id);
        }

        /// <summary>
        /// 移动内置节点优先级，负责支持上移、下移和拖拽排序。
        /// </summary>
        public static void MoveBuiltInNode(SimManagementLibSettings settings, string id, int targetIndex)
        {
            if (settings == null || !IsKnownNodeId(settings)(id))
                return;

            List<string> order = GetOrderedEnabledAllNodeIds(settings);
            order.Remove(id);
            targetIndex = Math.Max(0, Math.Min(targetIndex, order.Count));
            order.Insert(targetIndex, id);
            settings.reviewPromptNodeOrder = string.Join(",", order.ToArray());
        }

        /// <summary>
        /// 解析自定义节点，负责把设置字符串恢复成可编辑节点列表。
        /// </summary>
        public static List<CustomerReviewCustomPromptNode> ParseCustomNodes(string data)
        {
            List<CustomerReviewCustomPromptNode> result = new List<CustomerReviewCustomPromptNode>();
            if (string.IsNullOrEmpty(data))
                return result;

            string[] lines = data.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                string[] parts = lines[i].Split('|');
                if (parts.Length < 4) continue;
                result.Add(new CustomerReviewCustomPromptNode
                {
                    id = Decode(parts[0]),
                    enabled = parts[1] == "1",
                    label = Decode(parts[2]),
                    body = Decode(parts[3])
                });
            }
            return result;
        }

        /// <summary>
        /// 保存自定义节点，负责把玩家编辑结果写回设置字符串。
        /// </summary>
        public static string SerializeCustomNodes(List<CustomerReviewCustomPromptNode> nodes)
        {
            if (nodes == null || nodes.Count == 0)
                return "";

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < nodes.Count; i++)
            {
                CustomerReviewCustomPromptNode node = nodes[i];
                if (node == null) continue;
                if (string.IsNullOrWhiteSpace(node.id))
                    node.id = "custom_" + Guid.NewGuid().ToString("N");
                sb.Append(Encode(node.id)).Append('|')
                    .Append(node.enabled ? "1" : "0").Append('|')
                    .Append(Encode(node.label)).Append('|')
                    .Append(Encode(node.body)).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>
        /// 恢复注入器默认配置，负责启用所有默认内置节点并清空自定义节点。
        /// </summary>
        public static void Reset(SimManagementLibSettings settings)
        {
            if (settings == null) return;
            List<string> defaults = BuiltInNodes.Where(n => n.defaultEnabled).Select(n => n.id).ToList();
            settings.reviewPromptInputFormat = PromptInputFormatXml;
            settings.reviewPromptEnabledNodeIds = string.Join(",", defaults.ToArray());
            settings.reviewPromptNodeOrder = string.Join(",", BuiltInNodes.Select(n => n.id).ToArray());
            settings.reviewPromptCustomNodes = "";
        }

        private static bool IsBuiltInNode(string id)
        {
            return BuiltInNodes.Any(n => n.id == id);
        }

        private static List<string> ParseIdList(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return new List<string>();
            return value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(IsBuiltInNode).Distinct().ToList();
        }

        private static List<string> ParseAnyIdList(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return new List<string>();
            return value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();
        }

        private static Func<string, bool> IsKnownNodeId(SimManagementLibSettings settings)
        {
            List<string> customIds = ParseCustomNodes(settings?.reviewPromptCustomNodes).Select(n => n.id).ToList();
            return id => IsBuiltInNode(id) || customIds.Contains(id);
        }

        private static void AppendBuiltInNode(StringBuilder sb, string id, CustomerReviewSnapshot snapshot, SimManagementLibSettings settings)
        {
            if (id == NodeWritingStrategy) AppendWritingStrategy(sb, snapshot, settings);
            else if (id == NodeIdentity) AppendIdentity(sb, snapshot);
            else if (id == NodeTemporaryState) AppendTemporaryState(sb, snapshot);
            else if (id == NodeShopping) AppendShopping(sb, snapshot);
            else if (id == NodeCheckout) AppendCheckout(sb, snapshot);
            else if (id == NodeEnvironment) AppendEnvironment(sb, snapshot);
            else if (id == NodeRoom) AppendRoom(sb, snapshot);
            else if (id == NodeRelations) AppendRelations(sb, snapshot);
            else if (id == NodeWeather) AppendWeather(sb, snapshot);
            else if (id == NodeGameConditions) AppendGameConditions(sb, snapshot);
            else if (id == NodeColonyWealth) AppendColonyWealth(sb, snapshot);
            else if (id == NodeColonyShops) AppendColonyShops(sb, snapshot);
            else if (id == NodeColonyLeader) AppendColonyLeader(sb, snapshot);
            else if (id == NodeColonyCulture) AppendColonyCulture(sb, snapshot);
            else if (id == NodeForum) AppendForum(sb, snapshot);
            else if (id == NodeLexicon) AppendLexicon(sb, settings);
        }

        private static void AppendWritingStrategy(StringBuilder sb, CustomerReviewSnapshot snapshot, SimManagementLibSettings settings)
        {
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.WritingStrategyHeader"));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.BackgroundRule"));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.HumanVoiceLine", BuildHumanVoiceGuidance(snapshot).Named("value")));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.FocusLine", BuildReviewFocusGuidance(snapshot).Named("value")));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.AntiTemplateRule"));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.EmptyDataRule"));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.StarsRule"));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.SubjectiveRule"));
            AppendAbsurdNitpickGuidance(sb, snapshot, settings);
            sb.AppendLine();
        }

        private static void AppendIdentity(StringBuilder sb, CustomerReviewSnapshot snapshot)
        {
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.IdentityHeader"));
            sb.AppendLine("- kind: " + snapshot.kindLabel + " / " + snapshot.kindId);
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.KindDescriptionLine", EmptyAsNone(snapshot.kindDescription).Named("value")));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.RaceLine", snapshot.raceLabel.Named("value")));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.RaceDescriptionLine", EmptyAsNone(snapshot.raceDescription).Named("value")));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.NameLine", snapshot.customerDisplayName.Named("value")));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.AgeLine", snapshot.ageSummary.Named("value")));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.BackstoryNamesLine", snapshot.backstorySummary.Named("value")));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.BackstoryDetailsLine", EmptyAsNone(snapshot.backstoryDetailSummary).Named("value")));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.BackstoryRule"));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.TraitsLine", snapshot.traitSummary.Named("value")));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.XenotypeLine", EmptyAsNone(snapshot.xenotypeSummary).Named("value")));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.GenesLine", EmptyAsNone(snapshot.geneSummary).Named("value")));
            sb.AppendLine();
        }

        private static void AppendTemporaryState(StringBuilder sb, CustomerReviewSnapshot snapshot)
        {
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.TemporaryStateHeader"));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.PersonalityBiasLine", EmptyAsNone(snapshot.personalityBiasSummary).Named("value")));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.MoodLine", snapshot.moodSummary.Named("value")));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.HealthLine", snapshot.healthSummary.Named("value")));
            sb.AppendLine();
        }

        private static void AppendShopping(StringBuilder sb, CustomerReviewSnapshot snapshot)
        {
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.ShoppingHeader"));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.ShoppingWritingRule"));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.ShoppingUsageRule"));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.ShopLine", snapshot.zoneLabel.Named("value")));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.BudgetLine", snapshot.budgetSummary.Named("value")));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.SpentSilverLine", snapshot.spentSilver.ToString("F0", System.Globalization.CultureInfo.InvariantCulture).Named("value")));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.PurchasedLine", snapshot.purchasedSummary.Named("value")));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.ServiceLine", snapshot.serviceSummary.Named("value")));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.PostPurchaseLine", EmptyAsNone(snapshot.postPurchaseSummary).Named("value")));
            sb.AppendLine();
        }

        private static void AppendCheckout(StringBuilder sb, CustomerReviewSnapshot snapshot)
        {
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.CheckoutHeader"));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.CashierLine", EmptyAsNone(snapshot.cashierSummary).Named("value")));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.CheckoutJobLine", EmptyAsNone(snapshot.checkoutJobSummary).Named("value")));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.CheckoutWritingRule"));
            sb.AppendLine();
        }

        private static void AppendEnvironment(StringBuilder sb, CustomerReviewSnapshot snapshot)
        {
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.EnvironmentHeader"));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.EnvironmentLine", snapshot.shopEnvironmentSummary.Named("value")));
            sb.AppendLine();
        }

        private static void AppendRoom(StringBuilder sb, CustomerReviewSnapshot snapshot)
        {
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.RoomHeader"));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.RoomLine", EmptyAsNone(snapshot.roomSummary).Named("value")));
            sb.AppendLine();
        }

        private static void AppendRelations(StringBuilder sb, CustomerReviewSnapshot snapshot)
        {
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.RelationsHeader"));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.RelationsLine", EmptyAsNone(snapshot.relationSummary).Named("value")));
            sb.AppendLine();
        }

        private static void AppendWeather(StringBuilder sb, CustomerReviewSnapshot snapshot)
        {
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.WeatherHeader"));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.WeatherLine", EmptyAsNone(snapshot.weatherSummary).Named("value")));
            sb.AppendLine();
        }

        private static void AppendGameConditions(StringBuilder sb, CustomerReviewSnapshot snapshot)
        {
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.GameConditionsHeader"));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.GameConditionsLine", EmptyAsNone(snapshot.gameConditionSummary).Named("value")));
            sb.AppendLine();
        }

        private static void AppendColonyWealth(StringBuilder sb, CustomerReviewSnapshot snapshot)
        {
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.ColonyWealthHeader"));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.ColonyWealthLine", EmptyAsNone(snapshot.colonyWealthSummary).Named("value")));
            sb.AppendLine();
        }

        private static void AppendColonyShops(StringBuilder sb, CustomerReviewSnapshot snapshot)
        {
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.ColonyShopsHeader"));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.ColonyShopsLine", EmptyAsNone(snapshot.colonyShopSummary).Named("value")));
            sb.AppendLine();
        }

        private static void AppendColonyLeader(StringBuilder sb, CustomerReviewSnapshot snapshot)
        {
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.ColonyLeaderHeader"));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.ColonyLeaderLine", EmptyAsNone(snapshot.colonyLeaderSummary).Named("value")));
            sb.AppendLine();
        }

        private static void AppendColonyCulture(StringBuilder sb, CustomerReviewSnapshot snapshot)
        {
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.ColonyCultureHeader"));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.ColonyCultureLine", EmptyAsNone(snapshot.colonyCultureSummary).Named("value")));
            sb.AppendLine();
        }

        private static void AppendForum(StringBuilder sb, CustomerReviewSnapshot snapshot)
        {
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.ForumHeader"));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.RecentReviewsLine", EmptyAsNone(snapshot.recentReviewContextSummary).Named("value")));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.ReputationRule"));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.ReactionRule"));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.ReplyRule"));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.ThreadRule"));
            sb.AppendLine();
        }

        private static void AppendLexicon(StringBuilder sb, SimManagementLibSettings settings)
        {
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.LexiconHeader"));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.NicknameStyleALine", FlattenLines(settings.reviewNicknamePrefixes).Named("value")));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.NicknameStyleBLine", FlattenLines(settings.reviewNicknameSuffixes).Named("value")));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.ToneLine", FlattenLines(settings.reviewToneWords).Named("value")));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.PositiveWordsLine", FlattenLines(settings.reviewPositiveWords).Named("value")));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.NegativeWordsLine", FlattenLines(settings.reviewNegativeWords).Named("value")));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.BannedWordsLine", FlattenLines(settings.reviewBannedWords).Named("value")));
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.TagRule"));
            sb.AppendLine();
        }

        private static void AppendCustomNodeById(StringBuilder sb, SimManagementLibSettings settings, string id)
        {
            List<CustomerReviewCustomPromptNode> nodes = ParseCustomNodes(settings?.reviewPromptCustomNodes);
            CustomerReviewCustomPromptNode node = nodes.FirstOrDefault(n => n != null && n.id == id);
            if (node == null || !node.enabled || string.IsNullOrWhiteSpace(node.body))
                return;
            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.CustomNodeHeader", (string.IsNullOrWhiteSpace(node.label) ? node.id : node.label).Named("label")));
            sb.AppendLine(node.body);
            sb.AppendLine();
        }

        /// <summary>
        /// 追加 XML 风格内置节点，负责把每类资料放进稳定标签结构。
        /// </summary>
        private static void AppendXmlBuiltInNode(StringBuilder sb, string id, CustomerReviewSnapshot snapshot, SimManagementLibSettings settings, int priority)
        {
            if (id == NodeWritingStrategy) AppendXmlWritingStrategy(sb, snapshot, settings, priority);
            else if (id == NodeIdentity) AppendXmlIdentity(sb, snapshot, priority);
            else if (id == NodeTemporaryState) AppendXmlTemporaryState(sb, snapshot, priority);
            else if (id == NodeShopping) AppendXmlShopping(sb, snapshot, priority);
            else if (id == NodeCheckout) AppendXmlCheckout(sb, snapshot, priority);
            else if (id == NodeEnvironment) AppendXmlEnvironment(sb, snapshot, priority);
            else if (id == NodeRoom) AppendXmlRoom(sb, snapshot, priority);
            else if (id == NodeRelations) AppendXmlRelations(sb, snapshot, priority);
            else if (id == NodeWeather) AppendXmlWeather(sb, snapshot, priority);
            else if (id == NodeGameConditions) AppendXmlGameConditions(sb, snapshot, priority);
            else if (id == NodeColonyWealth) AppendXmlColonyWealth(sb, snapshot, priority);
            else if (id == NodeColonyShops) AppendXmlColonyShops(sb, snapshot, priority);
            else if (id == NodeColonyLeader) AppendXmlColonyLeader(sb, snapshot, priority);
            else if (id == NodeColonyCulture) AppendXmlColonyCulture(sb, snapshot, priority);
            else if (id == NodeForum) AppendXmlForum(sb, snapshot, priority);
            else if (id == NodeLexicon) AppendXmlLexicon(sb, settings, priority);
        }

        /// <summary>
        /// 追加 XML 风格写作策略节点，负责把随机关注点和评分原则作为高优先级规则。
        /// </summary>
        private static void AppendXmlWritingStrategy(StringBuilder sb, CustomerReviewSnapshot snapshot, SimManagementLibSettings settings, int priority)
        {
            AppendXmlOpen(sb, 1, "writingStrategy", priority);
            AppendXmlTextElement(sb, 2, "rule", SimTranslation.T("RSMF.CustomerReview.Xml.BackgroundRule"));
            AppendXmlTextElement(sb, 2, "humanVoice", BuildHumanVoiceGuidance(snapshot));
            AppendXmlTextElement(sb, 2, "focus", BuildReviewFocusGuidance(snapshot));
            AppendXmlTextElement(sb, 2, "antiTemplateRule", SimTranslation.T("RSMF.CustomerReview.Xml.AntiTemplateRule"));
            AppendXmlTextElement(sb, 2, "emptyDataRule", SimTranslation.T("RSMF.CustomerReview.Xml.EmptyDataRule"));
            AppendXmlTextElement(sb, 2, "starsRule", SimTranslation.T("RSMF.CustomerReview.Xml.StarsRule"));
            AppendXmlTextElement(sb, 2, "subjectiveRule", SimTranslation.T("RSMF.CustomerReview.Xml.SubjectiveRule"));
            if (settings != null && settings.reviewAbsurdNitpickEnabled && StableChance((snapshot?.reviewId ?? "") + "|absurd-nitpick", settings.reviewAbsurdNitpickChance))
                AppendXmlTextElement(sb, 2, "absurdNitpickPermission", SimTranslation.T("RSMF.CustomerReview.Xml.AbsurdNitpickPermission"));
            AppendXmlClose(sb, 1, "writingStrategy");
        }

        /// <summary>
        /// 追加 XML 风格身份节点，负责让模型区分网名来源和本次体验来源。
        /// </summary>
        private static void AppendXmlIdentity(StringBuilder sb, CustomerReviewSnapshot snapshot, int priority)
        {
            AppendXmlOpen(sb, 1, "identity", priority);
            AppendXmlTextElement(sb, 2, "nicknameSourceRule", SimTranslation.T("RSMF.CustomerReview.Xml.NicknameSourceRule"));
            AppendXmlTextElement(sb, 2, "kind", (snapshot?.kindLabel ?? "") + " / " + (snapshot?.kindId ?? ""));
            AppendXmlTextElement(sb, 2, "kindDescription", snapshot?.kindDescription);
            AppendXmlTextElement(sb, 2, "race", snapshot?.raceLabel);
            AppendXmlTextElement(sb, 2, "raceDescription", snapshot?.raceDescription);
            AppendXmlTextElement(sb, 2, "realName", snapshot?.customerDisplayName);
            AppendXmlTextElement(sb, 2, "age", snapshot?.ageSummary);
            AppendXmlTextElement(sb, 2, "backstoryNames", snapshot?.backstorySummary);
            AppendXmlTextElement(sb, 2, "backstoryDetails", snapshot?.backstoryDetailSummary);
            AppendXmlTextElement(sb, 2, "backstoryRule", SimTranslation.T("RSMF.CustomerReview.Xml.BackstoryRule"));
            AppendXmlTextElement(sb, 2, "traits", snapshot?.traitSummary);
            AppendXmlTextElement(sb, 2, "xenotype", snapshot?.xenotypeSummary);
            AppendXmlTextElement(sb, 2, "genes", snapshot?.geneSummary);
            AppendXmlClose(sb, 1, "identity");
        }

        /// <summary>
        /// 追加 XML 风格临时状态节点，负责提供心情、健康和评价倾向。
        /// </summary>
        private static void AppendXmlTemporaryState(StringBuilder sb, CustomerReviewSnapshot snapshot, int priority)
        {
            AppendXmlOpen(sb, 1, "temporaryState", priority);
            AppendXmlTextElement(sb, 2, "personalityBias", snapshot?.personalityBiasSummary);
            AppendXmlTextElement(sb, 2, "mood", snapshot?.moodSummary);
            AppendXmlTextElement(sb, 2, "health", snapshot?.healthSummary);
            AppendXmlClose(sb, 1, "temporaryState");
        }

        /// <summary>
        /// 追加 XML 风格购物体验节点，负责提供本次消费和售后资料。
        /// </summary>
        private static void AppendXmlShopping(StringBuilder sb, CustomerReviewSnapshot snapshot, int priority)
        {
            AppendXmlOpen(sb, 1, "shoppingExperience", priority);
            AppendXmlTextElement(sb, 2, "writingRule", SimTranslation.T("RSMF.CustomerReview.Xml.ShoppingWritingRule"));
            AppendXmlTextElement(sb, 2, "usageRule", SimTranslation.T("RSMF.CustomerReview.Xml.ShoppingUsageRule"));
            AppendXmlTextElement(sb, 2, "shop", snapshot?.zoneLabel);
            AppendXmlTextElement(sb, 2, "budget", snapshot?.budgetSummary);
            AppendXmlTextElement(sb, 2, "spentSilver", snapshot == null ? "" : SimTranslation.T("RSMF.CustomerReview.SilverAmount", snapshot.spentSilver.ToString("F0", System.Globalization.CultureInfo.InvariantCulture).Named("value")));
            AppendXmlTextElement(sb, 2, "purchasedItems", snapshot?.purchasedSummary);
            AppendXmlTextElement(sb, 2, "service", snapshot?.serviceSummary);
            AppendXmlTextElement(sb, 2, "postPurchase", snapshot?.postPurchaseSummary);
            AppendXmlClose(sb, 1, "shoppingExperience");
        }

        /// <summary>
        /// 追加 XML 风格结账节点，负责提供收银和排队资料及书写约束。
        /// </summary>
        private static void AppendXmlCheckout(StringBuilder sb, CustomerReviewSnapshot snapshot, int priority)
        {
            AppendXmlOpen(sb, 1, "checkout", priority);
            AppendXmlTextElement(sb, 2, "cashier", snapshot?.cashierSummary);
            AppendXmlTextElement(sb, 2, "job", snapshot?.checkoutJobSummary);
            AppendXmlTextElement(sb, 2, "writingRule", SimTranslation.T("RSMF.CustomerReview.Xml.CheckoutWritingRule"));
            AppendXmlClose(sb, 1, "checkout");
        }

        /// <summary>
        /// 追加 XML 风格环境节点，负责提供店铺经营环境摘要。
        /// </summary>
        private static void AppendXmlEnvironment(StringBuilder sb, CustomerReviewSnapshot snapshot, int priority)
        {
            AppendXmlOpen(sb, 1, "environment", priority);
            AppendXmlTextElement(sb, 2, "summary", snapshot?.shopEnvironmentSummary);
            AppendXmlClose(sb, 1, "environment");
        }

        /// <summary>
        /// 追加 XML 风格房间节点，负责提供原版房间数据。
        /// </summary>
        private static void AppendXmlRoom(StringBuilder sb, CustomerReviewSnapshot snapshot, int priority)
        {
            AppendXmlOpen(sb, 1, "room", priority);
            AppendXmlTextElement(sb, 2, "summary", snapshot?.roomSummary);
            AppendXmlClose(sb, 1, "room");
        }

        /// <summary>
        /// 追加 XML 风格关系节点，负责提供原版社交关系数据。
        /// </summary>
        private static void AppendXmlRelations(StringBuilder sb, CustomerReviewSnapshot snapshot, int priority)
        {
            AppendXmlOpen(sb, 1, "relations", priority);
            AppendXmlTextElement(sb, 2, "summary", snapshot?.relationSummary);
            AppendXmlClose(sb, 1, "relations");
        }

        /// <summary>
        /// 追加 XML 风格天气节点，负责提供当前天气和环境影响。
        /// </summary>
        private static void AppendXmlWeather(StringBuilder sb, CustomerReviewSnapshot snapshot, int priority)
        {
            AppendXmlOpen(sb, 1, "weather", priority);
            AppendXmlTextElement(sb, 2, "summary", snapshot?.weatherSummary);
            AppendXmlClose(sb, 1, "weather");
        }

        /// <summary>
        /// 追加 XML 风格事件节点，负责提供正在影响地图的持续事件。
        /// </summary>
        private static void AppendXmlGameConditions(StringBuilder sb, CustomerReviewSnapshot snapshot, int priority)
        {
            AppendXmlOpen(sb, 1, "gameConditions", priority);
            AppendXmlTextElement(sb, 2, "summary", snapshot?.gameConditionSummary);
            AppendXmlClose(sb, 1, "gameConditions");
        }

        /// <summary>
        /// 追加 XML 风格财富节点，负责提供殖民地经济规模背景。
        /// </summary>
        private static void AppendXmlColonyWealth(StringBuilder sb, CustomerReviewSnapshot snapshot, int priority)
        {
            AppendXmlOpen(sb, 1, "colonyWealth", priority);
            AppendXmlTextElement(sb, 2, "summary", snapshot?.colonyWealthSummary);
            AppendXmlClose(sb, 1, "colonyWealth");
        }

        /// <summary>
        /// 追加 XML 风格商店数量节点，负责提供殖民地商业规模背景。
        /// </summary>
        private static void AppendXmlColonyShops(StringBuilder sb, CustomerReviewSnapshot snapshot, int priority)
        {
            AppendXmlOpen(sb, 1, "colonyShops", priority);
            AppendXmlTextElement(sb, 2, "summary", snapshot?.colonyShopSummary);
            AppendXmlClose(sb, 1, "colonyShops");
        }

        /// <summary>
        /// 追加 XML 风格领袖节点，负责提供殖民地代表人物背景。
        /// </summary>
        private static void AppendXmlColonyLeader(StringBuilder sb, CustomerReviewSnapshot snapshot, int priority)
        {
            AppendXmlOpen(sb, 1, "colonyLeader", priority);
            AppendXmlTextElement(sb, 2, "summary", snapshot?.colonyLeaderSummary);
            AppendXmlClose(sb, 1, "colonyLeader");
        }

        /// <summary>
        /// 追加 XML 风格文化节点，负责提供殖民地信仰和文化氛围。
        /// </summary>
        private static void AppendXmlColonyCulture(StringBuilder sb, CustomerReviewSnapshot snapshot, int priority)
        {
            AppendXmlOpen(sb, 1, "colonyCulture", priority);
            AppendXmlTextElement(sb, 2, "summary", snapshot?.colonyCultureSummary);
            AppendXmlClose(sb, 1, "colonyCulture");
        }

        /// <summary>
        /// 追加 XML 风格论坛节点，负责提供短期口碑和互动规则。
        /// </summary>
        private static void AppendXmlForum(StringBuilder sb, CustomerReviewSnapshot snapshot, int priority)
        {
            AppendXmlOpen(sb, 1, "forumContext", priority);
            AppendXmlTextElement(sb, 2, "recentReviews", snapshot?.recentReviewContextSummary);
            AppendXmlTextElement(sb, 2, "reputationRule", SimTranslation.T("RSMF.CustomerReview.Xml.ReputationRule"));
            AppendXmlTextElement(sb, 2, "reactionRule", SimTranslation.T("RSMF.CustomerReview.Xml.ReactionRule"));
            AppendXmlTextElement(sb, 2, "replyRule", SimTranslation.T("RSMF.CustomerReview.Xml.ReplyRule"));
            AppendXmlTextElement(sb, 2, "threadRule", SimTranslation.T("RSMF.CustomerReview.Xml.ThreadRule"));
            AppendXmlClose(sb, 1, "forumContext");
        }

        /// <summary>
        /// 追加 XML 风格词库节点，负责提供风格边界和标签约束。
        /// </summary>
        private static void AppendXmlLexicon(StringBuilder sb, SimManagementLibSettings settings, int priority)
        {
            AppendXmlOpen(sb, 1, "lexicon", priority);
            AppendXmlTextElement(sb, 2, "nicknameStyleA", FlattenLines(settings?.reviewNicknamePrefixes));
            AppendXmlTextElement(sb, 2, "nicknameStyleB", FlattenLines(settings?.reviewNicknameSuffixes));
            AppendXmlTextElement(sb, 2, "tone", FlattenLines(settings?.reviewToneWords));
            AppendXmlTextElement(sb, 2, "positiveWords", FlattenLines(settings?.reviewPositiveWords));
            AppendXmlTextElement(sb, 2, "negativeWords", FlattenLines(settings?.reviewNegativeWords));
            AppendXmlTextElement(sb, 2, "bannedWords", FlattenLines(settings?.reviewBannedWords));
            AppendXmlTextElement(sb, 2, "tagRule", SimTranslation.T("RSMF.CustomerReview.Xml.TagRule"));
            AppendXmlClose(sb, 1, "lexicon");
        }

        /// <summary>
        /// 追加 XML 风格自定义节点，负责把玩家自写内容包进独立节点。
        /// </summary>
        private static void AppendXmlCustomNodeById(StringBuilder sb, SimManagementLibSettings settings, string id, int priority)
        {
            List<CustomerReviewCustomPromptNode> nodes = ParseCustomNodes(settings?.reviewPromptCustomNodes);
            CustomerReviewCustomPromptNode node = nodes.FirstOrDefault(n => n != null && n.id == id);
            if (node == null || !node.enabled || string.IsNullOrWhiteSpace(node.body))
                return;

            string pad = Indent(1);
            sb.Append(pad).Append("<customPromptNode priority=\"").Append(priority.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append("\" label=\"").Append(EscapeXml(string.IsNullOrWhiteSpace(node.label) ? node.id : node.label)).AppendLine("\">");
            AppendXmlTextElement(sb, 2, "body", node.body);
            sb.Append(pad).AppendLine("</customPromptNode>");
        }

        private static void AppendAbsurdNitpickGuidance(StringBuilder sb, CustomerReviewSnapshot snapshot, SimManagementLibSettings settings)
        {
            if (settings == null || !settings.reviewAbsurdNitpickEnabled)
                return;

            string seed = (snapshot?.reviewId ?? "") + "|absurd-nitpick";
            if (!StableChance(seed, settings.reviewAbsurdNitpickChance))
                return;

            sb.AppendLine(SimTranslation.T("RSMF.CustomerReview.Plain.AbsurdNitpickGuidance"));
        }

        private static string BuildReviewFocusGuidance(CustomerReviewSnapshot snapshot)
        {
            string[] modes =
            {
                SimTranslation.T("RSMF.CustomerReview.Focus.0"),
                SimTranslation.T("RSMF.CustomerReview.Focus.1"),
                SimTranslation.T("RSMF.CustomerReview.Focus.2"),
                SimTranslation.T("RSMF.CustomerReview.Focus.3"),
                SimTranslation.T("RSMF.CustomerReview.Focus.4"),
                SimTranslation.T("RSMF.CustomerReview.Focus.5"),
                SimTranslation.T("RSMF.CustomerReview.Focus.6"),
                SimTranslation.T("RSMF.CustomerReview.Focus.7"),
                SimTranslation.T("RSMF.CustomerReview.Focus.8")
            };

            int index = StableIndex(snapshot?.reviewId ?? "", modes.Length);
            return modes[index];
        }

        /// <summary>
        /// 构造稳定的真人口吻入口，负责让每条评论从不同生活瞬间切入。
        /// </summary>
        private static string BuildHumanVoiceGuidance(CustomerReviewSnapshot snapshot)
        {
            string[] voices =
            {
                SimTranslation.T("RSMF.CustomerReview.HumanVoice.0"),
                SimTranslation.T("RSMF.CustomerReview.HumanVoice.1"),
                SimTranslation.T("RSMF.CustomerReview.HumanVoice.2"),
                SimTranslation.T("RSMF.CustomerReview.HumanVoice.3"),
                SimTranslation.T("RSMF.CustomerReview.HumanVoice.4"),
                SimTranslation.T("RSMF.CustomerReview.HumanVoice.5"),
                SimTranslation.T("RSMF.CustomerReview.HumanVoice.6"),
                SimTranslation.T("RSMF.CustomerReview.HumanVoice.7"),
                SimTranslation.T("RSMF.CustomerReview.HumanVoice.8"),
                SimTranslation.T("RSMF.CustomerReview.HumanVoice.9")
            };

            int index = StableIndex((snapshot?.reviewId ?? "") + "|human-voice", voices.Length);
            return voices[index];
        }

        private static int StableIndex(string value, int count)
        {
            if (count <= 1) return 0;
            unchecked
            {
                int hash = 23;
                if (!string.IsNullOrEmpty(value))
                {
                    for (int i = 0; i < value.Length; i++)
                        hash = hash * 31 + value[i];
                }
                return Math.Abs(hash == int.MinValue ? 0 : hash) % count;
            }
        }

        private static bool StableChance(string value, float chance)
        {
            if (chance <= 0f) return false;
            if (chance >= 1f) return true;
            int bucket = StableIndex(value, 10000);
            return bucket < chance * 10000f;
        }

        private static string EmptyAsNone(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? SimTranslation.T("RSMF.Common.None") : value;
        }

        /// <summary>
        /// 判断提示词字段是否只有占位含义，负责减少模型复述“无”和“普通”。
        /// </summary>
        private static bool IsMeaninglessPromptValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return true;

            string trimmed = value.Trim();
            return trimmed == "无"
                || trimmed == "暂无"
                || trimmed == "未知"
                || trimmed == "普通"
                || trimmed == "环境普通"
                || trimmed == "测试"
                || trimmed == "测试顾客"
                || trimmed == "测试商店"
                || trimmed == "无文本"
                || trimmed == "暂无近期评价。"
                || trimmed == "暂无同店近期评价。";
        }

        private static string FlattenLines(string value)
        {
            if (string.IsNullOrEmpty(value)) return SimTranslation.T("RSMF.Common.None");
            return System.Text.RegularExpressions.Regex.Replace(value, "[\\r\\n]+", SimTranslation.T("RSMF.Common.ListSeparator")).Trim('、', ',', ' ');
        }

        /// <summary>
        /// 追加 XML 开始标签，负责统一节点优先级属性格式。
        /// </summary>
        private static void AppendXmlOpen(StringBuilder sb, int level, string tag, int priority)
        {
            sb.Append(Indent(level)).Append('<').Append(tag).Append(" priority=\"").Append(priority.ToString(System.Globalization.CultureInfo.InvariantCulture)).AppendLine("\">");
        }

        /// <summary>
        /// 追加 XML 结束标签，负责统一缩进。
        /// </summary>
        private static void AppendXmlClose(StringBuilder sb, int level, string tag)
        {
            sb.Append(Indent(level)).Append("</").Append(tag).AppendLine(">");
        }

        /// <summary>
        /// 追加 XML 文本元素，负责转义源数据并保留中文原文。
        /// </summary>
        private static void AppendXmlTextElement(StringBuilder sb, int level, string tag, string value)
        {
            if (IsMeaninglessPromptValue(value))
                return;

            sb.Append(Indent(level)).Append('<').Append(tag).Append('>')
                .Append(EscapeXml(value))
                .Append("</").Append(tag).AppendLine(">");
        }

        /// <summary>
        /// 转义 XML 文本，负责避免 RimWorld 富文本和玩家自定义内容破坏节点结构。
        /// </summary>
        private static string EscapeXml(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            StringBuilder sb = new StringBuilder(value.Length + 16);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '&') sb.Append("&amp;");
                else if (c == '<') sb.Append("&lt;");
                else if (c == '>') sb.Append("&gt;");
                else if (c == '"') sb.Append("&quot;");
                else if (c == '\'') sb.Append("&apos;");
                else sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// 构造 XML 缩进，负责保持调试终端中的提示词可读。
        /// </summary>
        private static string Indent(int level)
        {
            return new string(' ', Math.Max(0, level) * 2);
        }

        private static string Encode(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? ""));
        }

        private static string Decode(string value)
        {
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(value ?? ""));
            }
            catch
            {
                return "";
            }
        }
    }
}
