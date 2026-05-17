using SimManagementLib.Pojo;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 保存接口测试结果，负责把 BaseUrl 连通性、API 生成结果和错误说明传回设置界面。
    /// </summary>
    public class CustomerReviewConnectionTestResult
    {
        public bool baseUrlReachable;
        public bool apiReachable;
        public int statusCode;
        public string endpoint = "";
        public string message = "";
    }

    /// <summary>
    /// 保存一次点评请求的对话准备结果，负责携带缓存键、历史上下文和是否开启新对话。
    /// </summary>
    public class CustomerReviewDialogueRequest
    {
        public string userPrompt = "";
        public string stablePromptPrefix = "";
        public string dynamicPrompt = "";
        public bool startedNewConversation;
        public List<CustomerReviewChatMessage> messages = new List<CustomerReviewChatMessage>();
        public int conversationCharCount;
        public int conversationTurnCount;
    }

    /// <summary>
    /// 保存一条发给 Chat Completions 的对话消息，负责让同一对话历史作为稳定前缀反复发送。
    /// </summary>
    public class CustomerReviewChatMessage
    {
        public string role = "";
        public string content = "";
    }

    /// <summary>
    /// 管理顾客点评的滚动对话上下文，负责让服务端前缀缓存命中并在上下文过长时切换到新对话。
    /// </summary>
    public static class CustomerReviewDialogueStrategy
    {
        private const int MaxRecentMemos = 8;
        private static readonly object Gate = new object();
        private static readonly List<CustomerReviewAntiRepeatMemo> RecentMemos = new List<CustomerReviewAntiRepeatMemo>();
        private static string rollingConversationSignature = "";

        /// <summary>
        /// 根据顾客快照和提示词生成对话请求，负责携带滚动历史和当前顾客资料。
        /// </summary>
        public static CustomerReviewDialogueRequest Prepare(CustomerReviewSnapshot snapshot, SimManagementLibSettings settings, string userPrompt)
        {
            return Prepare(snapshot, settings, "", userPrompt);
        }

        /// <summary>
        /// 根据稳定前缀和动态资料生成对话请求，负责让服务端前缀缓存命中且避免旧顾客资料污染当前评价。
        /// </summary>
        public static CustomerReviewDialogueRequest Prepare(CustomerReviewSnapshot snapshot, SimManagementLibSettings settings, string stablePromptPrefix, string dynamicPrompt)
        {
            CustomerReviewDialogueRequest request = new CustomerReviewDialogueRequest
            {
                stablePromptPrefix = stablePromptPrefix ?? "",
                dynamicPrompt = dynamicPrompt ?? "",
                userPrompt = BuildDebugPrompt(stablePromptPrefix, dynamicPrompt)
            };

            PrepareCacheableMessages(request, settings);
            return request;
        }

        /// <summary>
        /// 记录一次成功生成的点评结果，负责更新后续请求可复用的滚动对话前缀。
        /// </summary>
        public static void StoreSuccessfulResult(CustomerReviewDialogueRequest request, CustomerReviewSnapshot snapshot, CustomerReviewAiResult result, SimManagementLibSettings settings)
        {
            if (result == null || settings == null)
                return;

            lock (Gate)
            {
                AppendRollingTurn(request, snapshot, result, settings);
            }
        }

        /// <summary>
        /// 构造解析失败后的修正请求，负责复用同一前缀并追加简短修正要求。
        /// </summary>
        public static CustomerReviewDialogueRequest BuildRetryRequest(CustomerReviewDialogueRequest source, string failedOutput)
        {
            CustomerReviewDialogueRequest retry = CloneRequestForRetry(source);
            retry.messages.Add(new CustomerReviewChatMessage
            {
                role = "user",
                content = BuildRetryInstruction(failedOutput)
            });
            retry.conversationCharCount = CountMessageChars(retry.messages);
            return retry;
        }

        /// <summary>
        /// 构造当前可用的反重复备忘，负责只把最近句式和标签作为末尾动态提示的一部分。
        /// </summary>
        public static string BuildAntiRepeatContext(SimManagementLibSettings settings)
        {
            int limit = GetConversationCharLimit(settings);
            if (limit <= 0)
                return "";

            lock (Gate)
            {
                if (RecentMemos.Count == 0)
                    return "";

                int maxCount = Math.Max(1, Math.Min(MaxRecentMemos, limit / 240));
                StringBuilder sb = new StringBuilder();
                sb.AppendLine(SimTranslation.TOrFallback("RSMF.CustomerReview.Split.AntiRepeatHeader", "最近已用短评轮廓，只用于避开重复句式，不是当前顾客素材:"));
                int start = Math.Max(0, RecentMemos.Count - maxCount);
                for (int i = start; i < RecentMemos.Count; i++)
                {
                    CustomerReviewAntiRepeatMemo memo = RecentMemos[i];
                    if (memo == null)
                        continue;

                    sb.AppendLine("- " + SimTranslation.TOrFallback("RSMF.CustomerReview.Split.AntiRepeatLine", "开头: {opening}；标签: {tags}；口吻入口: {impulse}")
                        .Replace("{opening}", memo.opening ?? "")
                        .Replace("{tags}", memo.tags ?? "")
                        .Replace("{impulse}", memo.impulse ?? ""));
                }
                sb.AppendLine(SimTranslation.TOrFallback("RSMF.CustomerReview.Split.AntiRepeatRule", "本条要换一个开头、换一组标签，重复买同类商品也不要复用上一条模板。"));
                return sb.ToString();
            }
        }

        /// <summary>
        /// 准备可缓存对话消息，负责固定前缀顺序并把所有易变资料放到最后。
        /// </summary>
        private static void PrepareCacheableMessages(CustomerReviewDialogueRequest request, SimManagementLibSettings settings)
        {
            if (request == null)
                return;

            string signature = BuildConversationSignature(settings);

            lock (Gate)
            {
                if (rollingConversationSignature != signature)
                {
                    RecentMemos.Clear();
                    rollingConversationSignature = signature;
                    request.startedNewConversation = true;
                }

                if (!string.IsNullOrWhiteSpace(request.stablePromptPrefix))
                {
                    request.messages.Add(new CustomerReviewChatMessage
                    {
                        role = "user",
                        content = request.stablePromptPrefix
                    });
                }

                request.messages.Add(new CustomerReviewChatMessage
                {
                    role = "user",
                    content = string.IsNullOrWhiteSpace(request.dynamicPrompt) ? request.userPrompt ?? "" : request.dynamicPrompt
                });
                request.conversationCharCount = CountMessageChars(request.messages);
                request.conversationTurnCount = RecentMemos.Count;
            }
        }

        /// <summary>
        /// 复制请求消息，负责让不同重试类型共享滚动前缀和当前顾客资料。
        /// </summary>
        private static CustomerReviewDialogueRequest CloneRequestForRetry(CustomerReviewDialogueRequest source)
        {
            CustomerReviewDialogueRequest retry = new CustomerReviewDialogueRequest
            {
                userPrompt = source?.userPrompt ?? "",
                stablePromptPrefix = source?.stablePromptPrefix ?? "",
                dynamicPrompt = source?.dynamicPrompt ?? "",
                startedNewConversation = source?.startedNewConversation ?? true,
                conversationTurnCount = source?.conversationTurnCount ?? 0
            };

            if (source?.messages != null)
            {
                for (int i = 0; i < source.messages.Count; i++)
                {
                    retry.messages.Add(CloneMessage(source.messages[i]));
                }
            }

            return retry;
        }

        /// <summary>
        /// 将成功的一轮问答写入滚动对话，负责让下一次请求复用相同前缀。
        /// </summary>
        private static void AppendRollingTurn(CustomerReviewDialogueRequest request, CustomerReviewSnapshot snapshot, CustomerReviewAiResult result, SimManagementLibSettings settings)
        {
            if (request == null || result == null || settings == null)
                return;

            int limit = GetConversationCharLimit(settings);
            if (limit <= 0)
                return;

            RecentMemos.Add(new CustomerReviewAntiRepeatMemo
            {
                opening = BuildReviewOpeningMemo(result.reviewText),
                tags = BuildTagMemo(result.tags),
                impulse = Shorten(ExtractImpulseFromDynamicPrompt(request.dynamicPrompt), 36)
            });
            while (RecentMemos.Count > MaxRecentMemos)
                RecentMemos.RemoveAt(0);
        }

        /// <summary>
        /// 构造滚动对话签名，负责在提示词、词库、供应商或模型变化时自动开启新对话。
        /// </summary>
        private static string BuildConversationSignature(SimManagementLibSettings settings)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(settings?.reviewProvider.ToString() ?? "").Append('|');
            sb.Append(GetModelName(settings)).Append('|');
            sb.Append(settings?.reviewSystemPrompt ?? "").Append('|');
            sb.Append(settings?.reviewUserPrompt ?? "").Append('|');
            sb.Append(settings?.reviewNicknamePrefixes ?? "").Append('|');
            sb.Append(settings?.reviewNicknameSuffixes ?? "").Append('|');
            sb.Append(settings?.reviewToneWords ?? "").Append('|');
            sb.Append(settings?.reviewPositiveWords ?? "").Append('|');
            sb.Append(settings?.reviewNegativeWords ?? "").Append('|');
            sb.Append(settings?.reviewBannedWords ?? "").Append('|');
            sb.Append(settings?.reviewPromptInputFormat ?? "").Append('|');
            sb.Append(settings?.reviewPromptEnabledNodeIds ?? "").Append('|');
            sb.Append(settings?.reviewPromptNodeOrder ?? "").Append('|');
            sb.Append(settings?.reviewPromptCustomNodes ?? "").Append('|');
            sb.Append(settings?.reviewAbsurdNitpickEnabled == true ? "absurd-on" : "absurd-off").Append('|');
            sb.Append(settings?.reviewAbsurdNitpickChance.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) ?? "");
            return Sha256Hex(sb.ToString());
        }

        /// <summary>
        /// 读取滚动对话字符预算，负责给没有设置或旧设置提供可命中缓存的默认窗口。
        /// </summary>
        private static int GetConversationCharLimit(SimManagementLibSettings settings)
        {
            return settings?.reviewConversationContextMaxChars ?? 0;
        }

        /// <summary>
        /// 统计消息字符数，负责在接近上下文限制时开启新对话。
        /// </summary>
        private static int CountMessageChars(List<CustomerReviewChatMessage> messages)
        {
            if (messages == null)
                return 0;

            int total = 0;
            for (int i = 0; i < messages.Count; i++)
            {
                if (messages[i] == null)
                    continue;

                total += (messages[i].role?.Length ?? 0) + (messages[i].content?.Length ?? 0) + 8;
            }
            return total;
        }

        /// <summary>
        /// 复制对话消息，负责避免请求对象修改滚动历史。
        /// </summary>
        private static CustomerReviewChatMessage CloneMessage(CustomerReviewChatMessage source)
        {
            return new CustomerReviewChatMessage
            {
                role = source?.role ?? "",
                content = source?.content ?? ""
            };
        }

        /// <summary>
        /// 构造重试修正提示，负责让模型只输出合法 JSON 而不是重新解释规则。
        /// </summary>
        private static string BuildRetryInstruction(string failedOutput)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(SimTranslation.TOrFallback("RSMF.CustomerReview.Retry.ParseFailed", "上一次回答没有被本地解析为有效点评 JSON。请基于上一条当前顾客资料重新输出一次，只返回一个 JSON 对象。"));
            sb.AppendLine(SimTranslation.TOrFallback("RSMF.CustomerReview.Retry.RequiredFields", "必须包含 nickname、stars、reviewText、upvoteReviewId、downvoteReviewId、replyToReviewId、replyText、replyStance、tags。"));
            sb.AppendLine(SimTranslation.TOrFallback("RSMF.CustomerReview.Retry.JsonOnly", "不要解释，不要 Markdown，不要代码块；stars 必须是 1 到 5 的整数；tags 必须是字符串数组。"));
            string excerpt = Shorten(failedOutput, 240);
            if (!string.IsNullOrWhiteSpace(excerpt) && excerpt != SimTranslation.TOrFallback("RSMF.Common.None", "无"))
                sb.AppendLine(SimTranslation.TOrFallback("RSMF.CustomerReview.Retry.FailedExcerpt", "上一次输出片段仅供纠错，不要复读: {value}").Replace("{value}", excerpt));
            return sb.ToString();
        }

        /// <summary>
        /// 构造历史短评开头备忘，负责只提供去重目标而不是可复读正文。
        /// </summary>
        private static string BuildReviewOpeningMemo(string reviewText)
        {
            if (string.IsNullOrWhiteSpace(reviewText))
                return SimTranslation.TOrFallback("RSMF.CustomerReview.Split.EmptyReviewOpening", "空短评");

            string start = reviewText.Trim();
            if (start.Length > 8)
                start = start.Substring(0, 8);

            return "“" + start + "”这类开头";
        }

        /// <summary>
        /// 构造历史标签备忘，负责降低连续评论复用同一标签组合的概率。
        /// </summary>
        private static string BuildTagMemo(List<string> tags)
        {
            if (tags == null || tags.Count == 0)
                return SimTranslation.TOrFallback("RSMF.CustomerReview.Split.EmptyTags", "空标签");

            List<string> safe = new List<string>();
            for (int i = 0; i < tags.Count && i < 4; i++)
            {
                if (!string.IsNullOrWhiteSpace(tags[i]))
                    safe.Add(tags[i].Trim());
            }
            return safe.Count == 0 ? SimTranslation.TOrFallback("RSMF.CustomerReview.Split.EmptyTags", "空标签") : string.Join(SimTranslation.TOrFallback("RSMF.Common.ListSeparator", "、"), safe.ToArray());
        }

        /// <summary>
        /// 构造调试提示词文本，负责让终端仍能完整查看稳定前缀和动态资料。
        /// </summary>
        private static string BuildDebugPrompt(string stablePromptPrefix, string dynamicPrompt)
        {
            if (string.IsNullOrWhiteSpace(stablePromptPrefix))
                return dynamicPrompt ?? "";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine(stablePromptPrefix ?? "");
            sb.AppendLine();
            sb.AppendLine(dynamicPrompt ?? "");
            return sb.ToString();
        }

        /// <summary>
        /// 从动态提示词提取本条口吻入口，负责给后续反重复备忘提供短标签。
        /// </summary>
        private static string ExtractImpulseFromDynamicPrompt(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt))
                return SimTranslation.TOrFallback("RSMF.Common.None", "无");

            string marker = "currentImpulse";
            int index = prompt.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return SimTranslation.TOrFallback("RSMF.Common.None", "无");

            int start = prompt.IndexOf('>', index);
            int end = start >= 0 ? prompt.IndexOf('<', start + 1) : -1;
            if (start >= 0 && end > start)
                return prompt.Substring(start + 1, end - start - 1).Trim();

            int lineEnd = prompt.IndexOf('\n', index);
            string line = lineEnd >= 0 ? prompt.Substring(index, lineEnd - index) : prompt.Substring(index);
            return line.Trim();
        }

        /// <summary>
        /// 截断历史摘要字段，负责减少滚动上下文 token 并降低旧样本对新评价的措辞污染。
        /// </summary>
        private static string Shorten(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                return SimTranslation.TOrFallback("RSMF.Common.None", "无");

            value = value.Trim();
            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }

        /// <summary>
        /// 读取当前供应商模型名，负责让不同模型拥有独立缓存。
        /// </summary>
        private static string GetModelName(SimManagementLibSettings settings)
        {
            if (settings == null)
                return "";

            return settings.reviewProvider == CustomerReviewProvider.Anthropic
                ? settings.anthropicModel ?? ""
                : settings.openAiModel ?? "";
        }

        /// <summary>
        /// 计算 SHA256 十六进制摘要，负责把长提示词压缩成稳定键。
        /// </summary>
        private static string Sha256Hex(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? ""));
                StringBuilder sb = new StringBuilder(bytes.Length * 2);
                for (int i = 0; i < bytes.Length; i++)
                {
                    sb.Append(bytes[i].ToString("x2"));
                }
                return sb.ToString();
            }
        }

        /// <summary>
        /// 保存最近成功评价的短去重信息，负责避免完整历史进入缓存前缀。
        /// </summary>
        private class CustomerReviewAntiRepeatMemo
        {
            public string opening = "";
            public string tags = "";
            public string impulse = "";
        }
    }
}
