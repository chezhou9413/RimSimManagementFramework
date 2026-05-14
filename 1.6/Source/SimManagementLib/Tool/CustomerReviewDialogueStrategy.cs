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
        private static readonly object Gate = new object();
        private static readonly List<CustomerReviewChatMessage> RollingMessages = new List<CustomerReviewChatMessage>();
        private static string rollingConversationSignature = "";

        /// <summary>
        /// 根据顾客快照和提示词生成对话请求，负责携带滚动历史和当前顾客资料。
        /// </summary>
        public static CustomerReviewDialogueRequest Prepare(CustomerReviewSnapshot snapshot, SimManagementLibSettings settings, string userPrompt)
        {
            CustomerReviewDialogueRequest request = new CustomerReviewDialogueRequest
            {
                userPrompt = userPrompt ?? ""
            };

            PrepareRollingMessages(request, settings);
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
        /// 准备滚动对话消息，负责让 DeepSeek 一类服务端前缀缓存可以命中历史输入。
        /// </summary>
        private static void PrepareRollingMessages(CustomerReviewDialogueRequest request, SimManagementLibSettings settings)
        {
            if (request == null)
                return;

            string signature = BuildConversationSignature(settings);
            int limit = GetConversationCharLimit(settings);

            lock (Gate)
            {
                if (limit <= 0)
                {
                    RollingMessages.Clear();
                    request.messages.Add(new CustomerReviewChatMessage
                    {
                        role = "user",
                        content = request.userPrompt ?? ""
                    });
                    request.startedNewConversation = true;
                    request.conversationCharCount = CountMessageChars(request.messages);
                    request.conversationTurnCount = 0;
                    return;
                }

                if (rollingConversationSignature != signature)
                {
                    RollingMessages.Clear();
                    rollingConversationSignature = signature;
                    request.startedNewConversation = true;
                }

                int projectedChars = CountMessageChars(RollingMessages) + (request.userPrompt?.Length ?? 0);
                if (projectedChars > limit)
                {
                    RollingMessages.Clear();
                    request.startedNewConversation = true;
                }

                for (int i = 0; i < RollingMessages.Count; i++)
                {
                    request.messages.Add(CloneMessage(RollingMessages[i]));
                }

                request.messages.Add(new CustomerReviewChatMessage
                {
                    role = "user",
                    content = request.userPrompt ?? ""
                });
                request.conversationCharCount = CountMessageChars(request.messages);
                request.conversationTurnCount = RollingMessages.Count / 2;
            }
        }

        /// <summary>
        /// 将成功的一轮问答写入滚动对话，负责让下一次请求复用相同前缀。
        /// </summary>
        private static void AppendRollingTurn(CustomerReviewDialogueRequest request, CustomerReviewSnapshot snapshot, CustomerReviewAiResult result, SimManagementLibSettings settings)
        {
            if (request == null || result == null || settings == null)
                return;

            int limit = GetConversationCharLimit(settings);
            string userPrompt = BuildHistoryUserSummary(snapshot);
            string assistantJson = BuildAssistantJson(result);
            if (limit <= 0)
                return;

            int projectedChars = CountMessageChars(RollingMessages) + userPrompt.Length + assistantJson.Length;
            if (projectedChars > limit)
            {
                RollingMessages.Clear();
            }

            RollingMessages.Add(new CustomerReviewChatMessage
            {
                role = "user",
                content = userPrompt
            });
            RollingMessages.Add(new CustomerReviewChatMessage
            {
                role = "assistant",
                content = assistantJson
            });
        }

        /// <summary>
        /// 构造历史顾客摘要，负责保留对话缓存需要的上下文，同时避免旧购买清单和旧评价正文诱导模型复读句式。
        /// </summary>
        private static string BuildHistoryUserSummary(CustomerReviewSnapshot snapshot)
        {
            if (snapshot == null)
                return "上一位顾客已处理。下一位顾客必须重新生成独立网名和不同句式。";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("上一位顾客已处理，只作为轻量上下文参考，不要求当前评价模仿或反向避开。");
            sb.AppendLine("稳定身份: " + Shorten(snapshot.kindLabel, 24) + " / " + Shorten(snapshot.raceLabel, 24) + " / " + Shorten(snapshot.ageSummary, 24));
            sb.AppendLine("性格背景: " + Shorten(snapshot.backstorySummary, 60) + " / " + Shorten(snapshot.traitSummary, 60));
            sb.AppendLine("背景故事完整描述摘要: " + Shorten(snapshot.backstoryDetailSummary, 220));
            sb.AppendLine("体验轮廓: " + Shorten(snapshot.serviceSummary, 70) + " / 实际付款 " + snapshot.spentSilver.ToString("F0") + " 银");
            sb.AppendLine("提示: 当前顾客应按自己的画像自然说话，切入点可以和上一条不同，也可以偶尔相似。");
            return sb.ToString();
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
            sb.Append(settings?.reviewBannedWords ?? "");
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
        /// 构造助手 JSON 历史，负责让下一轮请求的前缀完全复用上一轮输入输出。
        /// </summary>
        private static string BuildAssistantJson(CustomerReviewAiResult result)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"nickname\":\"").Append(EscapeJson(BuildNicknameShape(result.nickname))).Append("\",");
            sb.Append("\"stars\":").Append(Math.Max(1, Math.Min(5, result.stars))).Append(",");
            sb.Append("\"reviewText\":\"").Append(EscapeJson(BuildReviewShape(result.reviewText))).Append("\",");
            sb.Append("\"upvoteReviewId\":\"").Append(EscapeJson(SummarizeInteraction(result.upvoteReviewId))).Append("\",");
            sb.Append("\"downvoteReviewId\":\"").Append(EscapeJson(SummarizeInteraction(result.downvoteReviewId))).Append("\",");
            sb.Append("\"replyToReviewId\":\"").Append(EscapeJson(SummarizeInteraction(result.replyToReviewId))).Append("\",");
            sb.Append("\"replyText\":\"").Append(EscapeJson(BuildReviewShape(result.replyText))).Append("\",");
            sb.Append("\"replyStance\":\"").Append(EscapeJson(result.replyStance)).Append("\",");
            sb.Append("\"tags\":[");
            if (result.tags != null)
            {
                for (int i = 0; i < result.tags.Count && i < 4; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append("\"").Append(EscapeJson(result.tags[i])).Append("\"");
                }
            }
            sb.Append("]}");
            return sb.ToString();
        }

        /// <summary>
        /// 归纳历史互动字段，负责保留论坛行为轮廓但不暴露旧评论正文。
        /// </summary>
        private static string SummarizeInteraction(string reviewId)
        {
            return string.IsNullOrWhiteSpace(reviewId) ? "" : "曾互动一条历史评论";
        }

        /// <summary>
        /// 归纳历史网名形态，负责避免把真实上一条网名作为下一轮可模仿样本。
        /// </summary>
        private static string BuildNicknameShape(string nickname)
        {
            if (string.IsNullOrWhiteSpace(nickname))
                return "已生成独立网名";

            int len = nickname.Trim().Length;
            return "已生成" + Math.Max(1, Math.Min(12, len)) + "字独立网名";
        }

        /// <summary>
        /// 归纳历史评价形态，负责让对话缓存保留去重信号但不保留可复读的正文。
        /// </summary>
        private static string BuildReviewShape(string reviewText)
        {
            if (string.IsNullOrWhiteSpace(reviewText))
                return "已生成一条独立短评，下一条需换切入方式";

            string start = reviewText.Trim();
            if (start.Length > 6)
                start = start.Substring(0, 6);

            return "上一条大致从「" + EscapeJson(start) + "」开头，当前评价按新顾客自然发挥";
        }

        /// <summary>
        /// 截断历史摘要字段，负责减少滚动上下文 token 并降低旧样本对新评价的措辞污染。
        /// </summary>
        private static string Shorten(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "无";

            value = value.Trim();
            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }

        /// <summary>
        /// 转义 JSON 字符串内容，负责稳定保存助手历史输出。
        /// </summary>
        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
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
    }
}
