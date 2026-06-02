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
            value = StringEncodingUtility.SanitizeUtf16(value);
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
            List<string> tags = CleanTags(ExtractStringArray(json, "tags"), settings);

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
            string stablePromptPrefix = CustomerReviewPromptInjector.BuildStablePromptPrefix(settings);
            string dynamicPrompt = CustomerReviewPromptInjector.BuildDynamicPrompt(snapshot, settings, CustomerReviewDialogueStrategy.BuildAntiRepeatContext(settings));
            return stablePromptPrefix + "\n\n" + dynamicPrompt;
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

            sb.AppendLine("- 刁钻/胡言乱语许可: 本条允许出现一个荒谬、刁钻、没道理但像真实论坛用户发疯的评价点，可以是差评也可以是好评。例子只作尺度参考，不要照抄：嫌店主长得寒碜、越吃越饱反而不爽、买完突然想起前任、觉得货架摆放和自己八字不合、怀疑店员在背后议论自己、觉得这店风水克自己、买完觉得对不起上家店。最多写一个这种点，仍然要贴合顾客画像和本次体验，可以暴论、可以下三路、可以政治不正确的吐槽，别憋着。");
        }

        /// <summary>
        /// 构造单条评价的随机关注点，负责避免模型把所有参数都写进同一条评价。
        /// </summary>
        private static string BuildReviewFocusGuidance(CustomerReviewSnapshot snapshot)
        {
            string[] modes =
            {
                "从顾客一句主观感受切入，商品和花费只在后半句自然带一下。",
                "主要写顾客心情和个人脾气，购物细节只挑最刺眼的一点。",
                "像论坛碎碎念一样写一个小抱怨或小满意，不要列商品清单。",
                "主要写店铺环境或氛围带来的感觉；如果要提环境，只写一句，不要套用脏乱差模板。",
                "主要写买完以后准备怎么用、吃、带走或处理，少写店员。",
                "主要写值不值带来的情绪，不要算账式复述金额。",
                "主要写和近期口碑的对比或论坛回复，正文不要重复商品说明。",
                "主要写一个很短的情绪化结论，不展开所有原因。",
                "用顾客背景或特性带出一句偏见，再轻轻落到本次体验。"
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

        /// <summary>
        /// 清洗模型标签，负责移除错字感强、无意义、过短、过长或命中禁用词的标签。
        /// </summary>
        private static List<string> CleanTags(List<string> tags, SimManagementLibSettings settings)
        {
            List<string> result = new List<string>();
            if (tags == null)
                return result;

            for (int i = 0; i < tags.Count; i++)
            {
                string tag = CleanText(tags[i], 8);
                tag = Regex.Replace(tag, "[\\s#，,、。.!！?？:：;；]+", "");
                tag = NormalizeTag(tag);
                if (!IsUsableTag(tag, settings) || result.Contains(tag))
                    continue;

                result.Add(tag);
                if (result.Count >= 4) break;
            }
            return result;
        }

        /// <summary>
        /// 规范化模型标签，负责修正少量高频错别字和机械空标签。
        /// </summary>
        private static string NormalizeTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return "";

            Dictionary<string, string> replacements = new Dictionary<string, string>
            {
                { "回够", "回购" },
                { "够买", "购买" },
                { "生存食品", "生存食物" },
                { "方便食品", "方便食物" },
                { "赶稿囤货", "囤货" }
            };

            return replacements.TryGetValue(tag, out string fixedTag) ? fixedTag : tag;
        }

        /// <summary>
        /// 判断标签是否适合展示，负责挡掉短怪词、系统词和纯符号内容。
        /// </summary>
        private static bool IsUsableTag(string tag, SimManagementLibSettings settings)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return false;
            if (tag.Length < 2 || tag.Length > 8)
                return false;
            if (ContainsBannedWord(tag, settings))
                return false;
            if (Regex.IsMatch(tag, "^[0-9A-Za-z_\\-]+$"))
                return false;

            int chineseCount = 0;
            for (int i = 0; i < tag.Length; i++)
            {
                if (tag[i] >= 0x4e00 && tag[i] <= 0x9fff)
                    chineseCount++;
            }
            return chineseCount >= Math.Min(2, tag.Length);
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
