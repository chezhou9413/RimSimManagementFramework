using SimManagementLib.Pojo;
using System.Collections.Generic;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 保存一次大模型 HTTP 尝试的调试信息，负责让终端区分首次请求、降级重试和解析重试。
    /// </summary>
    public class CustomerReviewAiDebugAttempt
    {
        public string label = "";
        public string endpoint = "";
        public string requestBody = "";
        public int statusCode;
        public bool success;
        public string rawResponse = "";
        public string extractedText = "";
    }

    /// <summary>
    /// 保存一次顾客评价 AI 调用的运行时调试记录，负责承载本局内可查看但不存档的请求全过程。
    /// </summary>
    public class CustomerReviewAiDebugRecord
    {
        public const string StatusPending = "Pending";
        public const string StatusSuccess = "Success";
        public const string StatusFailed = "Failed";

        public int id;
        public string status = StatusPending;
        public string createdAt = "";
        public int tickAbs;
        public int gameDay;
        public string provider = "";
        public string model = "";
        public string endpoint = "";
        public string reviewId = "";
        public string customerDisplayName = "";
        public string zoneLabel = "";
        public bool startedNewConversation;
        public int conversationCharCount;
        public int conversationTurnCount;
        public string snapshotText = "";
        public string systemPrompt = "";
        public string userPrompt = "";
        public string stablePromptPrefix = "";
        public string dynamicPrompt = "";
        public string messagesText = "";
        public string rawAssistantText = "";
        public string parsedResultText = "";
        public int parsedStars;
        public string failureReason = "";
        public List<CustomerReviewAiDebugAttempt> attempts = new List<CustomerReviewAiDebugAttempt>();

        /// <summary>
        /// 创建当前记录的浅层副本，负责让 UI 读取时不直接持有后台线程正在更新的对象。
        /// </summary>
        public CustomerReviewAiDebugRecord Clone()
        {
            CustomerReviewAiDebugRecord copy = (CustomerReviewAiDebugRecord)MemberwiseClone();
            copy.attempts = new List<CustomerReviewAiDebugAttempt>();
            if (attempts != null)
            {
                for (int i = 0; i < attempts.Count; i++)
                {
                    CustomerReviewAiDebugAttempt attempt = attempts[i];
                    if (attempt == null) continue;
                    copy.attempts.Add(new CustomerReviewAiDebugAttempt
                    {
                        label = attempt.label,
                        endpoint = attempt.endpoint,
                        requestBody = attempt.requestBody,
                        statusCode = attempt.statusCode,
                        success = attempt.success,
                        rawResponse = attempt.rawResponse,
                        extractedText = attempt.extractedText
                    });
                }
            }
            return copy;
        }

        /// <summary>
        /// 返回当前记录是否处于请求中，负责给筛选和列表状态显示提供统一判断。
        /// </summary>
        public bool IsPending()
        {
            return status == StatusPending || status == "请求中";
        }

        /// <summary>
        /// 返回当前记录是否已经成功解析，负责给筛选和列表状态显示提供统一判断。
        /// </summary>
        public bool IsSuccess()
        {
            return status == StatusSuccess || status == "成功";
        }

        /// <summary>
        /// 返回当前记录是否已经失败，负责给筛选和列表状态显示提供统一判断。
        /// </summary>
        public bool IsFailed()
        {
            return status == StatusFailed || status == "失败";
        }

        /// <summary>
        /// 返回当前状态的本地化展示文本，负责让 UI 和复制文本不直接显示内部状态值。
        /// </summary>
        public string StatusLabel()
        {
            if (IsSuccess()) return SimTranslation.T("RSMF.AiTerminal.Filter.Success");
            if (IsFailed()) return SimTranslation.T("RSMF.AiTerminal.Filter.Failed");
            return SimTranslation.T("RSMF.AiTerminal.Filter.Pending");
        }
    }
}
