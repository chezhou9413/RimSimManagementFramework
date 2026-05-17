using SimManagementLib.Pojo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 管理顾客评价 AI 的本局运行时调试日志，负责记录请求、响应、解析和导出内容但不写入存档。
    /// </summary>
    public static class CustomerReviewAiDebugLog
    {
        private const int MaxRecords = 60;
        private static readonly object Gate = new object();
        private static readonly List<CustomerReviewAiDebugRecord> Records = new List<CustomerReviewAiDebugRecord>();
        private static int nextId = 1;

        /// <summary>
        /// 开始记录一次 AI 调用，负责捕获快照、提示词、对话消息和供应商摘要。
        /// </summary>
        public static int AddStarted(CustomerReviewSnapshot snapshot, SimManagementLibSettings settings, CustomerReviewDialogueRequest request)
        {
            if (snapshot == null || settings == null || request == null)
                return 0;

            CustomerReviewAiDebugRecord record = new CustomerReviewAiDebugRecord
            {
                id = NextId(),
                status = CustomerReviewAiDebugRecord.StatusPending,
                createdAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                tickAbs = snapshot.tickAbs,
                gameDay = snapshot.gameDay,
                provider = settings.reviewProvider.ToString(),
                model = GetModelName(settings),
                endpoint = GetEndpoint(settings),
                reviewId = snapshot.reviewId ?? "",
                customerDisplayName = snapshot.customerDisplayName ?? "",
                zoneLabel = snapshot.zoneLabel ?? "",
                startedNewConversation = request.startedNewConversation,
                conversationCharCount = request.conversationCharCount,
                conversationTurnCount = request.conversationTurnCount,
                snapshotText = BuildSnapshotText(snapshot),
                systemPrompt = settings.reviewSystemPrompt ?? "",
                userPrompt = request.userPrompt ?? "",
                stablePromptPrefix = request.stablePromptPrefix ?? "",
                dynamicPrompt = request.dynamicPrompt ?? "",
                messagesText = BuildMessagesText(request)
            };

            lock (Gate)
            {
                Records.Insert(0, record);
                TrimLocked();
            }
            return record.id;
        }

        /// <summary>
        /// 记录一次 HTTP 尝试，负责保留发送体、状态码、原始响应和抽取后的助手文本。
        /// </summary>
        public static void UpdateHttpAttempt(int id, string label, string endpoint, string requestBody, int statusCode, bool success, string rawResponse, string extractedText)
        {
            if (id <= 0)
                return;

            lock (Gate)
            {
                CustomerReviewAiDebugRecord record = FindLocked(id);
                if (record == null) return;
                record.endpoint = endpoint ?? record.endpoint;
                record.rawAssistantText = extractedText ?? record.rawAssistantText;
                record.attempts.Add(new CustomerReviewAiDebugAttempt
                {
                    label = label ?? "",
                    endpoint = endpoint ?? "",
                    requestBody = requestBody ?? "",
                    statusCode = statusCode,
                    success = success,
                    rawResponse = rawResponse ?? "",
                    extractedText = extractedText ?? ""
                });
            }
        }

        /// <summary>
        /// 标记模型文本已经成功解析，负责保存结构化结果摘要。
        /// </summary>
        public static void MarkParsed(int id, CustomerReviewAiResult result, string rawAssistantText)
        {
            if (id <= 0)
                return;

            lock (Gate)
            {
                CustomerReviewAiDebugRecord record = FindLocked(id);
                if (record == null) return;
                record.status = CustomerReviewAiDebugRecord.StatusSuccess;
                record.rawAssistantText = rawAssistantText ?? record.rawAssistantText;
                record.parsedStars = result?.stars ?? 0;
                record.parsedResultText = BuildResultText(result);
                record.failureReason = "";
            }
        }

        /// <summary>
        /// 标记 AI 调用失败，负责保存最终失败原因供终端排查。
        /// </summary>
        public static void MarkFailed(int id, string reason)
        {
            if (id <= 0)
                return;

            lock (Gate)
            {
                CustomerReviewAiDebugRecord record = FindLocked(id);
                if (record == null) return;
                record.status = CustomerReviewAiDebugRecord.StatusFailed;
                record.failureReason = reason ?? "";
            }
        }

        /// <summary>
        /// 返回当前内存中的调试记录副本，负责避免 UI 枚举时和后台请求线程冲突。
        /// </summary>
        public static List<CustomerReviewAiDebugRecord> GetRecords()
        {
            lock (Gate)
            {
                List<CustomerReviewAiDebugRecord> result = new List<CustomerReviewAiDebugRecord>();
                for (int i = 0; i < Records.Count; i++)
                {
                    result.Add(Records[i].Clone());
                }
                return result;
            }
        }

        /// <summary>
        /// 清空本局调试日志，负责只影响终端记录不影响顾客评价历史。
        /// </summary>
        public static void Clear()
        {
            lock (Gate)
            {
                Records.Clear();
            }
        }

        /// <summary>
        /// 构造单条记录的 JSON 文本，负责导出 UTF-8 调试数据且不包含密钥。
        /// </summary>
        public static string BuildRecordJson(CustomerReviewAiDebugRecord record)
        {
            StringBuilder sb = new StringBuilder();
            AppendRecordJson(sb, record, 0);
            return sb.ToString();
        }

        /// <summary>
        /// 构造全部记录的 JSON 文本，负责导出当前本局内存中的调试数据。
        /// </summary>
        public static string BuildAllJson(List<CustomerReviewAiDebugRecord> records)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\n  \"records\": [\n");
            if (records != null)
            {
                for (int i = 0; i < records.Count; i++)
                {
                    if (i > 0) sb.Append(",\n");
                    AppendRecordJson(sb, records[i], 4);
                }
            }
            sb.Append("\n  ]\n}");
            return sb.ToString();
        }

        /// <summary>
        /// 导出 JSON 文本到配置目录，负责只在玩家主动点击导出时落盘。
        /// </summary>
        public static string ExportJson(string json, string prefix)
        {
            string dir = Path.Combine(GenFilePaths.ConfigFolderPath, "SimManagementLib", "AiDebugExports");
            Directory.CreateDirectory(dir);
            string safePrefix = string.IsNullOrWhiteSpace(prefix) ? "ai-debug" : prefix;
            string fileName = safePrefix + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".json";
            string path = Path.Combine(dir, fileName);
            File.WriteAllText(path, json ?? "", new UTF8Encoding(false));
            return path;
        }

        /// <summary>
        /// 构造完整记录文本，负责提供剪贴板复制用的可读内容。
        /// </summary>
        public static string BuildRecordText(CustomerReviewAiDebugRecord record)
        {
            if (record == null) return "";
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(SimTranslation.T("RSMF.AiTerminal.StatusLine", record.StatusLabel().Named("value")));
            sb.AppendLine(SimTranslation.T("RSMF.AiTerminal.TimeLine", record.createdAt.Named("value")));
            sb.AppendLine(SimTranslation.T("RSMF.AiTerminal.GameDayTickLine", record.gameDay.Named("day"), record.tickAbs.Named("tick")));
            sb.AppendLine(SimTranslation.T("RSMF.AiTerminal.ProviderModelLine", record.provider.Named("provider"), record.model.Named("model")));
            sb.AppendLine("Endpoint: " + record.endpoint);
            sb.AppendLine(SimTranslation.T("RSMF.AiTerminal.CustomerShopLine", record.customerDisplayName.Named("customer"), record.zoneLabel.Named("shop"), record.reviewId.Named("reviewId")));
            sb.AppendLine();
            sb.AppendLine(SimTranslation.T("RSMF.AiTerminal.SourceSection"));
            sb.AppendLine(record.snapshotText);
            sb.AppendLine(SimTranslation.T("RSMF.AiTerminal.RequestSection"));
            sb.AppendLine(SimTranslation.T("RSMF.AiTerminal.Request.SystemPrompt"));
            sb.AppendLine(record.systemPrompt);
            sb.AppendLine(SimTranslation.T("RSMF.AiTerminal.Request.UserPrompt"));
            sb.AppendLine(record.userPrompt);
            sb.AppendLine(SimTranslation.T("RSMF.AiTerminal.Request.StablePrefix"));
            sb.AppendLine(record.stablePromptPrefix);
            sb.AppendLine(SimTranslation.T("RSMF.AiTerminal.Request.DynamicInput"));
            sb.AppendLine(record.dynamicPrompt);
            sb.AppendLine(SimTranslation.T("RSMF.AiTerminal.Request.Messages"));
            sb.AppendLine(record.messagesText);
            sb.AppendLine(SimTranslation.T("RSMF.AiTerminal.HttpSection"));
            AppendAttemptsText(sb, record);
            sb.AppendLine(SimTranslation.T("RSMF.AiTerminal.OutputSection"));
            sb.AppendLine(record.rawAssistantText);
            sb.AppendLine(SimTranslation.T("RSMF.AiTerminal.ParsedSection"));
            sb.AppendLine(record.parsedResultText);
            if (!string.IsNullOrWhiteSpace(record.failureReason))
            {
                sb.AppendLine(SimTranslation.T("RSMF.AiTerminal.FailureSection"));
                sb.AppendLine(record.failureReason);
            }
            return sb.ToString();
        }

        /// <summary>
        /// 构造 HTTP 尝试文本，负责让 UI 和复制内容复用同一份格式。
        /// </summary>
        public static string BuildAttemptsText(CustomerReviewAiDebugRecord record)
        {
            StringBuilder sb = new StringBuilder();
            AppendAttemptsText(sb, record);
            return sb.ToString();
        }

        private static int NextId()
        {
            lock (Gate)
            {
                return nextId++;
            }
        }

        /// <summary>
        /// 裁剪内存记录数量，负责避免本局长时间游玩时调试日志无限增长。
        /// </summary>
        private static void TrimLocked()
        {
            while (Records.Count > MaxRecords)
            {
                Records.RemoveAt(Records.Count - 1);
            }
        }

        /// <summary>
        /// 在锁内按编号查找调试记录，负责让更新操作定位同一条请求链路。
        /// </summary>
        private static CustomerReviewAiDebugRecord FindLocked(int id)
        {
            for (int i = 0; i < Records.Count; i++)
            {
                if (Records[i].id == id)
                    return Records[i];
            }
            return null;
        }

        /// <summary>
        /// 读取当前供应商模型名，负责在不暴露密钥的前提下展示请求目标。
        /// </summary>
        private static string GetModelName(SimManagementLibSettings settings)
        {
            if (settings == null)
                return "";
            return settings.reviewProvider == CustomerReviewProvider.Anthropic ? settings.anthropicModel ?? "" : settings.openAiModel ?? "";
        }

        /// <summary>
        /// 读取当前供应商端点，负责让调试记录显示实际请求地址。
        /// </summary>
        private static string GetEndpoint(SimManagementLibSettings settings)
        {
            if (settings == null)
                return "";
            return settings.reviewProvider == CustomerReviewProvider.Anthropic
                ? "https://api.anthropic.com/v1/messages"
                : CustomerReviewAiClient.NormalizeOpenAiUrl(settings.openAiBaseUrl);
        }

        /// <summary>
        /// 构造顾客快照文本，负责把后台线程可安全读取的纯数据展开给终端查看。
        /// </summary>
        private static string BuildSnapshotText(CustomerReviewSnapshot snapshot)
        {
            if (snapshot == null)
                return "";

            StringBuilder sb = new StringBuilder();
            AppendLine(sb, "reviewId", snapshot.reviewId);
            AppendLine(sb, "tickAbs", snapshot.tickAbs.ToString(CultureInfo.InvariantCulture));
            AppendLine(sb, "gameDay", snapshot.gameDay.ToString(CultureInfo.InvariantCulture));
            AppendLine(sb, "zoneId", snapshot.zoneId.ToString(CultureInfo.InvariantCulture));
            AppendLine(sb, "zoneLabel", snapshot.zoneLabel);
            AppendLine(sb, "customerDisplayName", snapshot.customerDisplayName);
            AppendLine(sb, "spentSilver", snapshot.spentSilver.ToString("F0", CultureInfo.InvariantCulture));
            AppendLine(sb, "kindId", snapshot.kindId);
            AppendLine(sb, "kindLabel", snapshot.kindLabel);
            AppendLine(sb, "kindDescription", snapshot.kindDescription);
            AppendLine(sb, "raceLabel", snapshot.raceLabel);
            AppendLine(sb, "raceDescription", snapshot.raceDescription);
            AppendLine(sb, "ageSummary", snapshot.ageSummary);
            AppendLine(sb, "backstorySummary", snapshot.backstorySummary);
            AppendLine(sb, "backstoryDetailSummary", snapshot.backstoryDetailSummary);
            AppendLine(sb, "traitSummary", snapshot.traitSummary);
            AppendLine(sb, "xenotypeSummary", snapshot.xenotypeSummary);
            AppendLine(sb, "geneSummary", snapshot.geneSummary);
            AppendLine(sb, "personalityBiasSummary", snapshot.personalityBiasSummary);
            AppendLine(sb, "moodSummary", snapshot.moodSummary);
            AppendLine(sb, "healthSummary", snapshot.healthSummary);
            AppendLine(sb, "budgetSummary", snapshot.budgetSummary);
            AppendLine(sb, "purchasedSummary", snapshot.purchasedSummary);
            AppendLine(sb, "serviceSummary", snapshot.serviceSummary);
            AppendLine(sb, "shopEnvironmentSummary", snapshot.shopEnvironmentSummary);
            AppendLine(sb, "roomSummary", snapshot.roomSummary);
            AppendLine(sb, "relationSummary", snapshot.relationSummary);
            AppendLine(sb, "weatherSummary", snapshot.weatherSummary);
            AppendLine(sb, "gameConditionSummary", snapshot.gameConditionSummary);
            AppendLine(sb, "colonyWealthSummary", snapshot.colonyWealthSummary);
            AppendLine(sb, "colonyShopSummary", snapshot.colonyShopSummary);
            AppendLine(sb, "colonyLeaderSummary", snapshot.colonyLeaderSummary);
            AppendLine(sb, "colonyCultureSummary", snapshot.colonyCultureSummary);
            AppendLine(sb, "cashierSummary", snapshot.cashierSummary);
            AppendLine(sb, "checkoutJobSummary", snapshot.checkoutJobSummary);
            AppendLine(sb, "postPurchaseSummary", snapshot.postPurchaseSummary);
            AppendLine(sb, "recentReviewContextSummary", snapshot.recentReviewContextSummary);
            AppendLine(sb, "avatarImageId", snapshot.avatarImageId);
            AppendLine(sb, "featuredItems", BuildFeaturedItemsText(snapshot.featuredItems));
            return sb.ToString();
        }

        /// <summary>
        /// 构造快照中的展示商品文本，负责压缩商品和套餐摘要便于人工排查。
        /// </summary>
        private static string BuildFeaturedItemsText(List<ReviewFeaturedItem> items)
        {
            if (items == null || items.Count == 0)
                return SimTranslation.T("RSMF.Common.None");

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < items.Count; i++)
            {
                ReviewFeaturedItem item = items[i];
                if (item == null) continue;
                if (sb.Length > 0) sb.Append("；");
                sb.Append(SimTranslation.T("RSMF.AiTerminal.FeaturedItemLine",
                    item.label.Named("label"),
                    item.defName.Named("defName"),
                    item.count.Named("count"),
                    item.amount.ToString("F0", CultureInfo.InvariantCulture).Named("amount")));
            }
            return sb.Length > 0 ? sb.ToString() : SimTranslation.T("RSMF.Common.None");
        }

        /// <summary>
        /// 构造对话消息文本，负责保留每条 user 或 assistant 消息的顺序和角色。
        /// </summary>
        private static string BuildMessagesText(CustomerReviewDialogueRequest request)
        {
            if (request?.messages == null || request.messages.Count == 0)
                return "";

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < request.messages.Count; i++)
            {
                CustomerReviewChatMessage message = request.messages[i];
                if (message == null) continue;
                sb.AppendLine("[" + i + "] " + (message.role ?? ""));
                sb.AppendLine(message.content ?? "");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        /// <summary>
        /// 构造解析结果文本，负责把模型结构化字段转换成终端可读格式。
        /// </summary>
        private static string BuildResultText(CustomerReviewAiResult result)
        {
            if (result == null)
                return "";

            StringBuilder sb = new StringBuilder();
            AppendLine(sb, "nickname", result.nickname);
            AppendLine(sb, "stars", result.stars.ToString(CultureInfo.InvariantCulture));
            AppendLine(sb, "reviewText", result.reviewText);
            AppendLine(sb, "upvoteReviewId", result.upvoteReviewId);
            AppendLine(sb, "downvoteReviewId", result.downvoteReviewId);
            AppendLine(sb, "replyToReviewId", result.replyToReviewId);
            AppendLine(sb, "replyText", result.replyText);
            AppendLine(sb, "replyStance", result.replyStance);
            AppendLine(sb, "tags", result.tags == null ? "" : string.Join("，", result.tags.ToArray()));
            return sb.ToString();
        }

        /// <summary>
        /// 追加 HTTP 尝试文本，负责展示每次请求体、响应体和提取结果。
        /// </summary>
        private static void AppendAttemptsText(StringBuilder sb, CustomerReviewAiDebugRecord record)
        {
            if (record?.attempts == null || record.attempts.Count == 0)
            {
                sb.AppendLine(SimTranslation.T("RSMF.AiTerminal.NoHttpAttempts"));
                return;
            }

            for (int i = 0; i < record.attempts.Count; i++)
            {
                CustomerReviewAiDebugAttempt attempt = record.attempts[i];
                if (attempt == null) continue;
                sb.AppendLine(SimTranslation.T("RSMF.AiTerminal.HttpAttemptLine", (i + 1).Named("index"), attempt.label.Named("label"), attempt.statusCode.Named("statusCode"), attempt.success.Named("success")));
                sb.AppendLine("Endpoint: " + attempt.endpoint);
                sb.AppendLine("Request Body:");
                sb.AppendLine(attempt.requestBody);
                sb.AppendLine("Raw Response:");
                sb.AppendLine(attempt.rawResponse);
                sb.AppendLine("Extracted Text:");
                sb.AppendLine(attempt.extractedText);
                sb.AppendLine();
            }
        }

        /// <summary>
        /// 追加键值行文本，负责统一源数据和解析结果的可读格式。
        /// </summary>
        private static void AppendLine(StringBuilder sb, string name, string value)
        {
            sb.Append(name).Append(": ").AppendLine(value ?? "");
        }

        /// <summary>
        /// 追加单条记录 JSON，负责导出不包含密钥的完整调试记录。
        /// </summary>
        private static void AppendRecordJson(StringBuilder sb, CustomerReviewAiDebugRecord record, int indent)
        {
            string pad = new string(' ', indent);
            string child = new string(' ', indent + 2);
            record = record ?? new CustomerReviewAiDebugRecord();
            sb.Append(pad).Append("{\n");
            AppendJsonField(sb, child, "id", record.id.ToString(CultureInfo.InvariantCulture), false, true);
            AppendJsonField(sb, child, "status", record.status, true, true);
            AppendJsonField(sb, child, "createdAt", record.createdAt, true, true);
            AppendJsonField(sb, child, "tickAbs", record.tickAbs.ToString(CultureInfo.InvariantCulture), false, true);
            AppendJsonField(sb, child, "gameDay", record.gameDay.ToString(CultureInfo.InvariantCulture), false, true);
            AppendJsonField(sb, child, "provider", record.provider, true, true);
            AppendJsonField(sb, child, "model", record.model, true, true);
            AppendJsonField(sb, child, "endpoint", record.endpoint, true, true);
            AppendJsonField(sb, child, "reviewId", record.reviewId, true, true);
            AppendJsonField(sb, child, "customerDisplayName", record.customerDisplayName, true, true);
            AppendJsonField(sb, child, "zoneLabel", record.zoneLabel, true, true);
            AppendJsonField(sb, child, "startedNewConversation", record.startedNewConversation ? "true" : "false", false, true);
            AppendJsonField(sb, child, "conversationCharCount", record.conversationCharCount.ToString(CultureInfo.InvariantCulture), false, true);
            AppendJsonField(sb, child, "conversationTurnCount", record.conversationTurnCount.ToString(CultureInfo.InvariantCulture), false, true);
            AppendJsonField(sb, child, "snapshotText", record.snapshotText, true, true);
            AppendJsonField(sb, child, "systemPrompt", record.systemPrompt, true, true);
            AppendJsonField(sb, child, "userPrompt", record.userPrompt, true, true);
            AppendJsonField(sb, child, "stablePromptPrefix", record.stablePromptPrefix, true, true);
            AppendJsonField(sb, child, "dynamicPrompt", record.dynamicPrompt, true, true);
            AppendJsonField(sb, child, "messagesText", record.messagesText, true, true);
            AppendJsonField(sb, child, "rawAssistantText", record.rawAssistantText, true, true);
            AppendJsonField(sb, child, "parsedResultText", record.parsedResultText, true, true);
            AppendJsonField(sb, child, "failureReason", record.failureReason, true, true);
            sb.Append(child).Append("\"attempts\": [\n");
            for (int i = 0; record.attempts != null && i < record.attempts.Count; i++)
            {
                if (i > 0) sb.Append(",\n");
                AppendAttemptJson(sb, record.attempts[i], indent + 4);
            }
            sb.Append("\n").Append(child).Append("]\n");
            sb.Append(pad).Append("}");
        }

        /// <summary>
        /// 追加单次 HTTP 尝试 JSON，负责保留请求体、响应体和抽取文本。
        /// </summary>
        private static void AppendAttemptJson(StringBuilder sb, CustomerReviewAiDebugAttempt attempt, int indent)
        {
            string pad = new string(' ', indent);
            string child = new string(' ', indent + 2);
            attempt = attempt ?? new CustomerReviewAiDebugAttempt();
            sb.Append(pad).Append("{\n");
            AppendJsonField(sb, child, "label", attempt.label, true, true);
            AppendJsonField(sb, child, "endpoint", attempt.endpoint, true, true);
            AppendJsonField(sb, child, "requestBody", attempt.requestBody, true, true);
            AppendJsonField(sb, child, "statusCode", attempt.statusCode.ToString(CultureInfo.InvariantCulture), false, true);
            AppendJsonField(sb, child, "success", attempt.success ? "true" : "false", false, true);
            AppendJsonField(sb, child, "rawResponse", attempt.rawResponse, true, true);
            AppendJsonField(sb, child, "extractedText", attempt.extractedText, true, false);
            sb.Append("\n").Append(pad).Append("}");
        }

        /// <summary>
        /// 追加 JSON 字段，负责按字符串或原始数值布尔值写入导出内容。
        /// </summary>
        private static void AppendJsonField(StringBuilder sb, string pad, string name, string value, bool quoteValue, bool comma)
        {
            sb.Append(pad).Append('"').Append(EscapeJson(name)).Append("\": ");
            if (quoteValue)
                sb.Append('"').Append(EscapeJson(value)).Append('"');
            else
                sb.Append(value ?? "0");
            if (comma) sb.Append(',');
            sb.Append('\n');
        }

        /// <summary>
        /// 转义 JSON 字符串，负责保留中文原文并处理控制字符和引号。
        /// </summary>
        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            StringBuilder sb = new StringBuilder(value.Length + 8);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (char.IsControl(c))
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
