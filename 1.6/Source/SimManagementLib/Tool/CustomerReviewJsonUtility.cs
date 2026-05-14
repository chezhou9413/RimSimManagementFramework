using SimManagementLib.Pojo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 提供顾客点评请求和响应的轻量 JSON 工具，负责构造请求、修复模型输出和容错提取字段。
    /// </summary>
    public static class CustomerReviewJsonUtility
    {
        /// <summary>
        /// 将字符串转义为 JSON 字符串字面量。
        /// </summary>
        public static string Quote(string value)
        {
            if (value == null) return "\"\"";
            StringBuilder sb = new StringBuilder(value.Length + 8);
            sb.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        if (char.IsControl(c))
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        /// <summary>
        /// 将模型返回文本解析为点评结果，允许常见的非标准 JSON 包装和标点错误。
        /// </summary>
        public static bool TryParseReviewResult(string raw, SimManagementLibSettings settings, out CustomerReviewAiResult result)
        {
            result = null;
            string json = RepairJsonObject(raw);
            if (string.IsNullOrEmpty(json)) return false;

            string nickname = ExtractString(json, "nickname");
            string reviewText = ExtractString(json, "reviewText");
            string upvoteReviewId = ExtractString(json, "upvoteReviewId");
            string downvoteReviewId = ExtractString(json, "downvoteReviewId");
            string replyToReviewId = ExtractString(json, "replyToReviewId");
            string replyText = ExtractString(json, "replyText");
            string replyStance = ExtractString(json, "replyStance");
            int stars = ExtractInt(json, "stars");
            List<string> tags = ExtractStringArray(json, "tags");

            if (string.IsNullOrWhiteSpace(nickname) || string.IsNullOrWhiteSpace(reviewText) || stars <= 0)
                return false;

            nickname = CleanText(nickname, 32);
            reviewText = CleanText(reviewText, 180);
            replyText = CleanText(replyText, 160);
            replyStance = CleanText(replyStance, 12);
            upvoteReviewId = CleanText(upvoteReviewId, 64);
            downvoteReviewId = CleanText(downvoteReviewId, 64);
            replyToReviewId = CleanText(replyToReviewId, 64);
            if (ContainsBannedWord(nickname, settings) || ContainsBannedWord(reviewText, settings) || ContainsBannedWord(replyText, settings))
                return false;

            result = new CustomerReviewAiResult
            {
                nickname = nickname,
                stars = Math.Max(1, Math.Min(5, stars)),
                reviewText = reviewText,
                upvoteReviewId = upvoteReviewId,
                downvoteReviewId = downvoteReviewId,
                replyToReviewId = replyToReviewId,
                replyText = replyText,
                replyStance = replyStance,
                tags = tags
            };
            return true;
        }

        /// <summary>
        /// 从 OpenAI Chat Completions 响应中提取助手消息内容。
        /// </summary>
        public static string ExtractOpenAiMessageContent(string responseJson)
        {
            if (string.IsNullOrEmpty(responseJson)) return "";
            Match match = Regex.Match(responseJson, "\"content\"\\s*:\\s*\"(?<value>(?:\\\\.|[^\"])*)\"", RegexOptions.Singleline);
            return match.Success ? UnescapeJsonString(match.Groups["value"].Value) : "";
        }

        /// <summary>
        /// 从 Anthropic Messages 响应中提取第一段文本内容。
        /// </summary>
        public static string ExtractAnthropicMessageContent(string responseJson)
        {
            if (string.IsNullOrEmpty(responseJson)) return "";
            Match match = Regex.Match(responseJson, "\"text\"\\s*:\\s*\"(?<value>(?:\\\\.|[^\"])*)\"", RegexOptions.Singleline);
            return match.Success ? UnescapeJsonString(match.Groups["value"].Value) : "";
        }

        /// <summary>
        /// 构造传给模型的顾客点评资料文本。
        /// </summary>
        public static string BuildSnapshotPrompt(CustomerReviewSnapshot snapshot, SimManagementLibSettings settings)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(settings.reviewUserPrompt);
            sb.AppendLine();
            sb.AppendLine("本条评论写作策略:");
            sb.AppendLine("- 所有资料都是可选背景，不是逐项打卡清单；reviewText 只能自然挑 1 到 3 个点写，其他资料只暗中影响语气、星级和取舍。");
            sb.AppendLine("- 不要每条都固定评价环境、收银员、服务、价格或商品质量；即使某个参数很差，也只是更可能被注意到，不代表必须写出来。");
            sb.AppendLine("- 本条随机关注点: " + BuildReviewFocusGuidance(snapshot));
            sb.AppendLine("- 星级分布要求: 不要把 3 星当默认值。明显满意或买到想要的东西可以 4-5 星；没买到、等待失败、心情很差、疼痛或特性难伺候可以 1-2 星；3 星只用于真的一般或好坏抵消。");
            sb.AppendLine("- 主观意识要求: 这不是客观质检报告，顾客可以因为心情、背景、特性、偏见、跟风或一点小事放大好恶；评价可以带私人情绪和不完全公平的判断。");
            AppendAbsurdNitpickGuidance(sb, snapshot, settings);
            sb.AppendLine();
            sb.AppendLine("网名只能参考的稳定身份资料:");
            sb.AppendLine("- kind: " + snapshot.kindLabel + " / " + snapshot.kindId);
            sb.AppendLine("- kind说明文本: " + EmptyAsNone(snapshot.kindDescription));
            sb.AppendLine("- 种族: " + snapshot.raceLabel);
            sb.AppendLine("- 种族说明: " + EmptyAsNone(snapshot.raceDescription));
            sb.AppendLine("- 名字: " + snapshot.customerDisplayName);
            sb.AppendLine("- 年龄: " + snapshot.ageSummary);
            sb.AppendLine("- 背景故事名称: " + snapshot.backstorySummary);
            sb.AppendLine("- 背景故事完整描述文本: " + EmptyAsNone(snapshot.backstoryDetailSummary));
            sb.AppendLine("- 背景故事使用要求: 网名和评价都可以参考背景故事完整描述文本里的经历、技能倾向和不能做的工作，但不要直接复述原文。");
            sb.AppendLine("- 特性说明文本: " + snapshot.traitSummary);
            sb.AppendLine("- 异种说明文本: " + EmptyAsNone(snapshot.xenotypeSummary));
            sb.AppendLine("- 基因说明文本: " + EmptyAsNone(snapshot.geneSummary));
            sb.AppendLine();
            sb.AppendLine("评价可以参考的临时状态:");
            sb.AppendLine("- 评价倾向: " + EmptyAsNone(snapshot.personalityBiasSummary));
            sb.AppendLine("- 心情: " + snapshot.moodSummary);
            sb.AppendLine("- 健康: " + snapshot.healthSummary);
            sb.AppendLine();
            sb.AppendLine("评价可以参考的购物体验:");
            sb.AppendLine("- 商店: " + snapshot.zoneLabel);
            sb.AppendLine("- 预算: " + snapshot.budgetSummary);
            sb.AppendLine("- 实际付款: " + snapshot.spentSilver.ToString("F0", CultureInfo.InvariantCulture) + " 银");
            sb.AppendLine("- 商品结果: " + snapshot.purchasedSummary);
            sb.AppendLine("- 服务: " + snapshot.serviceSummary);
            sb.AppendLine("- 结账背景资料: " + EmptyAsNone(snapshot.cashierSummary));
            sb.AppendLine("- 结账执行: " + EmptyAsNone(snapshot.checkoutJobSummary));
            sb.AppendLine("- 结账书写要求: 收银员和结账参数只是背景。只有排队久、结账失败、服务特别快/慢、收银员表现明显影响体验时，才在 reviewText 里提到收银员；普通顺利付款不要每条都写收银员。");
            sb.AppendLine("- 售后行为: " + EmptyAsNone(snapshot.postPurchaseSummary));
            sb.AppendLine("- 环境: " + snapshot.shopEnvironmentSummary);
            sb.AppendLine("- 短期口碑: " + EmptyAsNone(snapshot.recentReviewContextSummary));
            sb.AppendLine("- 口碑约束: 短期口碑只能作为背景参考，不能压过本次顾客自己的实际体验。");
            sb.AppendLine("- 论坛互动概率: 当前论坛非常活跃，看到短期口碑时多数顾客会顺手点赞或点踩；很多顾客也会回一句。除非这个顾客完全没兴趣，否则尽量产生一种互动。");
            sb.AppendLine("- 论坛互动写法: 主观赞同、跟风认同或觉得“说到点上了”就写 upvoteReviewId；主观不服、觉得别人夸过头、骂错点或和自己体验相反就写 downvoteReviewId；想接话、反驳、补充或吐槽时写 replyToReviewId 和 replyText。replyText 要像论坛楼中楼的一句话，不要像客服回复。");
            sb.AppendLine("- 回复目标选择: 可以回复已有回复数较多或争议较大的帖子；允许多名顾客回复同一个 reviewId，形成真实论坛讨论串。");
            sb.AppendLine();
            sb.AppendLine("词库约束:");
            sb.AppendLine("- 网名风格边界 A，不是候选词，不能直接复制或拼接: " + FlattenLines(settings.reviewNicknamePrefixes));
            sb.AppendLine("- 网名风格边界 B，不是候选词，不能直接复制或拼接: " + FlattenLines(settings.reviewNicknameSuffixes));
            sb.AppendLine("- 语气: " + FlattenLines(settings.reviewToneWords));
            sb.AppendLine("- 正面词: " + FlattenLines(settings.reviewPositiveWords));
            sb.AppendLine("- 负面词: " + FlattenLines(settings.reviewNegativeWords));
            sb.AppendLine("- 禁用词: " + FlattenLines(settings.reviewBannedWords));
            return sb.ToString();
        }

        /// <summary>
        /// 追加刁钻胡言乱语写作许可，负责让少量评价出现荒谬但像真人发疯的主观理由。
        /// </summary>
        private static void AppendAbsurdNitpickGuidance(StringBuilder sb, CustomerReviewSnapshot snapshot, SimManagementLibSettings settings)
        {
            if (settings == null || !settings.reviewAbsurdNitpickEnabled)
                return;

            string seed = (snapshot?.reviewId ?? "") + "|absurd-nitpick";
            if (!StableChance(seed, settings.reviewAbsurdNitpickChance))
                return;

            sb.AppendLine("- 刁钻/胡言乱语许可: 本条允许出现一个荒谬、刁钻、没道理但像真实论坛用户发疯的评价点，可以是差评也可以是好评。例子只作尺度参考，不要照抄：嫌店主长得寒碜、越吃越饱反而不爽、买完突然想起前任、觉得货架摆放和自己八字不合。最多写一个这种点，仍然要贴合顾客画像和本次体验，不要攻击现实群体，不要写色情、仇恨或现实政治。");
        }

        /// <summary>
        /// 构造单条评价的随机关注点，负责避免模型把所有参数都写进同一条评价。
        /// </summary>
        private static string BuildReviewFocusGuidance(CustomerReviewSnapshot snapshot)
        {
            string[] modes =
            {
                "主要写商品和花费感受，环境与收银员最多带一句。",
                "主要写顾客心情和个人脾气，购物细节只挑最刺眼的一点。",
                "主要写服务或结账流程；如果流程顺利，就不要硬写收银员。",
                "主要写店铺环境或氛围；如果要提环境，只写一句，不要套用脏乱差模板。",
                "主要写买到东西后的用途或售后行为，少写店员。",
                "主要写价格值不值，其他参数只作为背景。",
                "主要写和近期口碑的对比或论坛回复，正文不要重复商品说明。",
                "主要写一个很短的情绪化结论，不展开所有原因。"
            };

            int index = StableIndex(snapshot?.reviewId ?? "", modes.Length);
            return modes[index];
        }

        /// <summary>
        /// 根据稳定字符串计算数组下标，负责让同一条快照重试时保持同一写作焦点。
        /// </summary>
        private static int StableIndex(string value, int count)
        {
            if (count <= 1)
                return 0;

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

        /// <summary>
        /// 根据稳定字符串计算概率命中，负责让同一条快照重试时保持同一随机写作许可。
        /// </summary>
        private static bool StableChance(string value, float chance)
        {
            if (chance <= 0f)
                return false;
            if (chance >= 1f)
                return true;

            int bucket = StableIndex(value, 10000);
            return bucket < chance * 10000f;
        }

        /// <summary>
        /// 将空字段替换为无，负责避免提示词出现空白画像项。
        /// </summary>
        private static string EmptyAsNone(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "无" : value;
        }

        /// <summary>
        /// 修复模型输出中的常见 JSON 包装和格式问题。
        /// </summary>
        private static string RepairJsonObject(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string text = raw.Trim();
            text = Regex.Replace(text, "^```(?:json)?\\s*", "", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, "\\s*```$", "");
            text = text.Replace('“', '"').Replace('”', '"').Replace('‘', '\'').Replace('’', '\'');

            int start = text.IndexOf('{');
            int end = text.LastIndexOf('}');
            if (start < 0 || end <= start) return "";
            text = text.Substring(start, end - start + 1);
            text = Regex.Replace(text, ",\\s*([}\\]])", "$1");
            text = Regex.Replace(text, @"[\x00-\x08\x0B\x0C\x0E-\x1F]", "");
            return text;
        }

        private static string ExtractString(string json, string key)
        {
            Match match = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"(?<value>(?:\\\\.|[^\"])*)\"", RegexOptions.Singleline);
            return match.Success ? UnescapeJsonString(match.Groups["value"].Value) : "";
        }

        private static int ExtractInt(string json, string key)
        {
            Match match = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*(?<value>-?\\d+)", RegexOptions.Singleline);
            if (match.Success && int.TryParse(match.Groups["value"].Value, out int value))
                return value;
            return 0;
        }

        private static List<string> ExtractStringArray(string json, string key)
        {
            List<string> result = new List<string>();
            Match match = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\\[(?<value>.*?)\\]", RegexOptions.Singleline);
            if (!match.Success) return result;

            MatchCollection values = Regex.Matches(match.Groups["value"].Value, "\"(?<value>(?:\\\\.|[^\"])*)\"");
            foreach (Match valueMatch in values)
            {
                string value = CleanText(UnescapeJsonString(valueMatch.Groups["value"].Value), 12);
                if (!string.IsNullOrEmpty(value)) result.Add(value);
                if (result.Count >= 4) break;
            }
            return result;
        }

        private static string CleanText(string text, int maxLength)
        {
            if (text == null) return "";
            text = Regex.Replace(text, "\\s+", " ").Trim();
            if (text.Length > maxLength)
                text = text.Substring(0, maxLength);
            return text;
        }

        private static bool ContainsBannedWord(string text, SimManagementLibSettings settings)
        {
            if (settings == null || string.IsNullOrEmpty(settings.reviewBannedWords) || string.IsNullOrEmpty(text))
                return false;

            string[] words = settings.reviewBannedWords.Split(new[] { '\r', '\n', '，', ',', '、' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++)
            {
                string word = words[i].Trim();
                if (word.Length > 0 && text.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static string FlattenLines(string value)
        {
            if (string.IsNullOrEmpty(value)) return "无";
            return Regex.Replace(value, "[\\r\\n]+", "、").Trim('、', ' ');
        }

        private static string UnescapeJsonString(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            StringBuilder sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c != '\\' || i + 1 >= value.Length)
                {
                    sb.Append(c);
                    continue;
                }

                char n = value[++i];
                switch (n)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 < value.Length && int.TryParse(value.Substring(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                        {
                            sb.Append((char)code);
                            i += 4;
                        }
                        break;
                    default:
                        sb.Append(n);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
