using SimManagementLib.Pojo;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 负责调用 OpenAI 兼容接口或 Anthropic 接口生成顾客点评文本。
    /// </summary>
    public static class CustomerReviewAiClient
    {
        private const int DefaultTimeoutSeconds = 90;
        private const int RequestPollDelayMs = 100;

        /// <summary>
        /// 根据当前设置异步请求大模型，并返回解析后的点评结果。
        /// </summary>
        public static async Task<CustomerReviewAiResult> GenerateReviewAsync(CustomerReviewSnapshot snapshot, SimManagementLibSettings settings, CancellationToken token)
        {
            SanitizeSettingsForRequest(settings);
            if (snapshot == null || settings == null || !settings.HasValidReviewAiConfig()) return null;

            string stablePromptPrefix = CustomerReviewPromptInjector.BuildStablePromptPrefix(settings);
            string antiRepeatContext = CustomerReviewDialogueStrategy.BuildAntiRepeatContext(settings);
            string dynamicPrompt = CustomerReviewPromptInjector.BuildDynamicPrompt(snapshot, settings, antiRepeatContext);
            CustomerReviewDialogueRequest request = CustomerReviewDialogueStrategy.Prepare(snapshot, settings, stablePromptPrefix, dynamicPrompt);
            SanitizeDialogueRequest(request);
            int debugId = CustomerReviewAiDebugLog.AddStarted(snapshot, settings, request);

            try
            {
                string raw = await CallProviderAsync(settings, request, token, debugId);
                if (!CustomerReviewJsonUtility.TryParseReviewResult(raw, settings, out CustomerReviewAiResult result))
                {
                    CustomerReviewAiDebugLog.MarkFailed(debugId, SimTranslation.T("RSMF.CustomerReview.AiError.FirstParseFailedRetrying"));
                    CustomerReviewDialogueRequest retryRequest = CustomerReviewDialogueStrategy.BuildRetryRequest(request, raw);
                    raw = await CallProviderAsync(settings, retryRequest, token, debugId);
                    if (!CustomerReviewJsonUtility.TryParseReviewResult(raw, settings, out result))
                    {
                        CustomerReviewAiDebugLog.MarkFailed(debugId, SimTranslation.T("RSMF.CustomerReview.AiError.ParseFailedFinal"));
                        return null;
                    }
                }

                CustomerReviewDialogueStrategy.StoreSuccessfulResult(request, snapshot, result, settings);
                CustomerReviewAiDebugLog.MarkParsed(debugId, result, raw);
                return result;
            }
            catch (Exception ex)
            {
                CustomerReviewAiDebugLog.MarkFailed(debugId, SimTranslation.T("RSMF.CustomerReview.AiError.RequestException", StringEncodingUtility.SanitizeUtf16(ex.Message).Named("message")));
                if (ex is OperationCanceledException)
                    return null;
                return null;
            }
        }

        /// <summary>
        /// 按当前供应商发送请求，负责让首次生成和修正重试复用同一条调用路径。
        /// </summary>
        private static async Task<string> CallProviderAsync(SimManagementLibSettings settings, CustomerReviewDialogueRequest request, CancellationToken token, int debugId)
        {
            if (settings.reviewProvider == CustomerReviewProvider.Anthropic)
                return await CallAnthropicAsync(settings, request, token, debugId);

            return await CallOpenAiCompatibleAsync(settings, request, token, debugId);
        }

        /// <summary>
        /// 发送一条极短测试请求，用于验证玩家填写的大模型接口配置。
        /// </summary>
        public static async Task<bool> TestConnectionAsync(SimManagementLibSettings settings, CancellationToken token)
        {
            CustomerReviewConnectionTestResult result = await TestConnectionDetailedAsync(settings, token);
            return result != null && result.apiReachable;
        }

        /// <summary>
        /// 发送 BaseUrl 探测和极短生成请求，用于向设置界面返回详细联通性状态。
        /// </summary>
        public static async Task<CustomerReviewConnectionTestResult> TestConnectionDetailedAsync(SimManagementLibSettings settings, CancellationToken token)
        {
            SanitizeSettingsForRequest(settings);
            CustomerReviewSnapshot snapshot = new CustomerReviewSnapshot
            {
                reviewId = "test",
                customerDisplayName = SimTranslation.T("RSMF.CustomerReview.Test.Customer"),
                kindId = "TestKind",
                kindLabel = SimTranslation.T("RSMF.CustomerReview.Test.Customer"),
                raceLabel = SimTranslation.T("RSMF.CustomerReview.Test.Race"),
                ageSummary = SimTranslation.T("RSMF.CustomerReview.Test.Age"),
                backstorySummary = SimTranslation.T("RSMF.CustomerReview.Test.Backstory"),
                traitSummary = SimTranslation.T("RSMF.CustomerReview.Test.Trait"),
                moodSummary = SimTranslation.T("RSMF.CustomerReview.Test.Mood"),
                healthSummary = SimTranslation.T("RSMF.CustomerReview.Test.Health"),
                zoneLabel = SimTranslation.T("RSMF.CustomerReview.Test.Shop"),
                budgetSummary = SimTranslation.T("RSMF.CustomerReview.Test.Budget"),
                spentSilver = 25f,
                purchasedSummary = SimTranslation.T("RSMF.CustomerReview.Test.Purchased"),
                serviceSummary = SimTranslation.T("RSMF.CustomerReview.Test.Service"),
                shopEnvironmentSummary = SimTranslation.T("RSMF.CustomerReview.Test.Environment")
            };

            CustomerReviewConnectionTestResult testResult = await ProbeBaseUrlAsync(settings, token);
            CustomerReviewAiResult result = await GenerateReviewAsync(snapshot, settings, token);
            testResult.apiReachable = result != null;
            if (testResult.apiReachable)
                testResult.message = SimTranslation.T("RSMF.CustomerReview.Connection.GenerationSucceeded");
            else if (string.IsNullOrEmpty(testResult.message))
                testResult.message = SimTranslation.T("RSMF.CustomerReview.Connection.GenerationFailed");
            return testResult;
        }

        /// <summary>
        /// 只测试当前供应商入口地址是否可访问，负责让玩家单独排查 baseUrl 或网络问题。
        /// </summary>
        public static Task<CustomerReviewConnectionTestResult> TestBaseUrlAsync(SimManagementLibSettings settings, CancellationToken token)
        {
            SanitizeSettingsForRequest(settings);
            return ProbeBaseUrlAsync(settings, token);
        }

        /// <summary>
        /// 调用 OpenAI 兼容接口，负责在 JSON 输出约束不被网关支持时退化为普通 JSON 提示。
        /// </summary>
        private static async Task<string> CallOpenAiCompatibleAsync(SimManagementLibSettings settings, CustomerReviewDialogueRequest request, CancellationToken token, int debugId)
        {
            string endpoint = NormalizeOpenAiUrl(settings.openAiBaseUrl);
            string body = BuildOpenAiBody(settings, request, true);
            CustomerReviewHttpResult response = await SendJsonPostAsync(
                endpoint,
                body,
                token,
                settings,
                "Authorization",
                "Bearer " + StringEncodingUtility.SanitizeUtf16(settings.openAiApiKey));
            string responseText = response.responseText;
            string extracted = CustomerReviewJsonUtility.ExtractOpenAiMessageContent(responseText);
            CustomerReviewAiDebugLog.UpdateHttpAttempt(debugId, SimTranslation.T("RSMF.CustomerReview.HttpAttempt.OpenAiJsonObject"), endpoint, body, response.statusCode, response.success, responseText, extracted);
            if (!response.success && ShouldRetryWithoutResponseFormat(responseText))
            {
                body = BuildOpenAiBody(settings, request, false);
                response = await SendJsonPostAsync(
                    endpoint,
                    body,
                    token,
                    settings,
                    "Authorization",
                    "Bearer " + StringEncodingUtility.SanitizeUtf16(settings.openAiApiKey));
                responseText = response.responseText;
                extracted = CustomerReviewJsonUtility.ExtractOpenAiMessageContent(responseText);
                CustomerReviewAiDebugLog.UpdateHttpAttempt(debugId, SimTranslation.T("RSMF.CustomerReview.HttpAttempt.OpenAiNoResponseFormat"), endpoint, body, response.statusCode, response.success, responseText, extracted);
            }
            if (!response.success) return "";
            return extracted;
        }

        /// <summary>
        /// 调用 Anthropic Messages 接口，负责把系统提示词和用户提示词发送给模型。
        /// </summary>
        private static async Task<string> CallAnthropicAsync(SimManagementLibSettings settings, CustomerReviewDialogueRequest request, CancellationToken token, int debugId)
        {
            string endpoint = "https://api.anthropic.com/v1/messages";
            string body =
                "{" +
                "\"model\":" + CustomerReviewJsonUtility.Quote(settings.anthropicModel) + "," +
                "\"max_tokens\":500," +
                "\"temperature\":" + settings.reviewTemperature.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "," +
                "\"system\":" + CustomerReviewJsonUtility.Quote(settings.reviewSystemPrompt) + "," +
                "\"messages\":" + BuildMessagesArray(request) +
                "}";

            CustomerReviewHttpResult response = await SendJsonPostAsync(
                endpoint,
                body,
                token,
                settings,
                "x-api-key",
                StringEncodingUtility.SanitizeUtf16(settings.anthropicApiKey),
                "anthropic-version",
                "2023-06-01");
            string responseText = response.responseText;
            string extracted = CustomerReviewJsonUtility.ExtractAnthropicMessageContent(responseText);
            CustomerReviewAiDebugLog.UpdateHttpAttempt(debugId, SimTranslation.T("RSMF.CustomerReview.HttpAttempt.AnthropicMessages"), endpoint, body, response.statusCode, response.success, responseText, extracted);
            if (!response.success)
                return "";
            return extracted;
        }

        /// <summary>
        /// 探测当前供应商入口是否可访问，负责区分网络或 BaseUrl 问题和模型生成问题。
        /// </summary>
        private static async Task<CustomerReviewConnectionTestResult> ProbeBaseUrlAsync(SimManagementLibSettings settings, CancellationToken token)
        {
            SanitizeSettingsForRequest(settings);
            CustomerReviewConnectionTestResult result = new CustomerReviewConnectionTestResult();
            if (settings == null)
            {
                result.message = SimTranslation.T("RSMF.CustomerReview.Connection.SettingsNull");
                return result;
            }

            string endpoint = settings.reviewProvider == CustomerReviewProvider.Anthropic
                ? "https://api.anthropic.com/v1/messages"
                : NormalizeOpenAiUrl(settings.openAiBaseUrl);
            result.endpoint = StringEncodingUtility.SanitizeUtf16(endpoint);

            try
            {
                CustomerReviewHttpResult response = await SendProbeAsync(endpoint, token, settings);
                result.statusCode = response.statusCode;
                result.baseUrlReachable = response.reachedServer;
                result.message = response.reachedServer
                    ? SimTranslation.T("RSMF.CustomerReview.Connection.BaseUrlReachable", result.statusCode.Named("statusCode"))
                    : SimTranslation.T("RSMF.CustomerReview.Connection.BaseUrlFailed", StringEncodingUtility.SanitizeUtf16(response.error).Named("message"));
            }
            catch (TaskCanceledException)
            {
                result.baseUrlReachable = false;
                result.message = SimTranslation.T("RSMF.CustomerReview.Connection.BaseUrlTimeout");
            }
            catch (Exception ex)
            {
                result.baseUrlReachable = false;
                result.message = SimTranslation.T("RSMF.CustomerReview.Connection.BaseUrlException", StringEncodingUtility.SanitizeUtf16(ex.Message).Named("message"));
            }

            return result;
        }

        /// <summary>
        /// 构造 OpenAI Chat Completions 请求体，负责可选加入 JSON 输出约束。
        /// </summary>
        private static string BuildOpenAiBody(SimManagementLibSettings settings, CustomerReviewDialogueRequest request, bool withResponseFormat)
        {
            string body =
                "{" +
                "\"model\":" + CustomerReviewJsonUtility.Quote(settings.openAiModel) + "," +
                "\"messages\":[{\"role\":\"system\",\"content\":" + CustomerReviewJsonUtility.Quote(settings.reviewSystemPrompt) + "}," + BuildMessagesArrayItems(request) + "]," +
                "\"temperature\":" + settings.reviewTemperature.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "," +
                "\"presence_penalty\":0.35," +
                "\"frequency_penalty\":0.45";

            if (withResponseFormat)
                body += ",\"response_format\":{\"type\":\"json_object\"}";

            return body + "}";
        }

        /// <summary>
        /// 构造 Anthropic 消息数组，负责把滚动对话历史和当前顾客资料作为同一前缀发送。
        /// </summary>
        private static string BuildMessagesArray(CustomerReviewDialogueRequest request)
        {
            return "[" + BuildMessagesArrayItems(request) + "]";
        }

        /// <summary>
        /// 构造消息数组内容，负责保持历史消息顺序稳定以便服务端前缀缓存命中。
        /// </summary>
        private static string BuildMessagesArrayItems(CustomerReviewDialogueRequest request)
        {
            if (request?.messages == null || request.messages.Count == 0)
                return "{\"role\":\"user\",\"content\":\"\"}";

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < request.messages.Count; i++)
            {
                CustomerReviewChatMessage message = request.messages[i];
                if (message == null)
                    continue;

                if (sb.Length > 0) sb.Append(",");
                string role = message.role == "assistant" ? "assistant" : "user";
                sb.Append("{\"role\":").Append(CustomerReviewJsonUtility.Quote(role)).Append(",");
                sb.Append("\"content\":").Append(CustomerReviewJsonUtility.Quote(message.content)).Append("}");
            }

            return sb.Length > 0 ? sb.ToString() : "{\"role\":\"user\",\"content\":\"\"}";
        }

        /// <summary>
        /// 判断 OpenAI 兼容网关是否可能不支持 response_format，负责触发一次降级重试。
        /// </summary>
        private static bool ShouldRetryWithoutResponseFormat(string responseText)
        {
            if (string.IsNullOrEmpty(responseText))
                return false;

            return responseText.IndexOf("response_format", StringComparison.OrdinalIgnoreCase) >= 0
                || responseText.IndexOf("json_object", StringComparison.OrdinalIgnoreCase) >= 0
                || responseText.IndexOf("unsupported", StringComparison.OrdinalIgnoreCase) >= 0
                || responseText.IndexOf("not support", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// 规范化 OpenAI 兼容接口地址，负责兼容填写根地址、v1 地址或完整 chat/completions 地址。
        /// </summary>
        public static string NormalizeOpenAiUrl(string baseUrl)
        {
            string url = string.IsNullOrWhiteSpace(baseUrl) ? "https://api.openai.com/v1" : StringEncodingUtility.SanitizeUtf16(baseUrl).Trim();
            url = url.TrimEnd('/');
            if (url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
                return url;
            return url + "/chat/completions";
        }

        /// <summary>
        /// 清理请求配置文本，负责让测试按钮和后台生成都先经过同一层编码兜底。
        /// </summary>
        private static void SanitizeSettingsForRequest(SimManagementLibSettings settings)
        {
            settings?.SanitizeReviewSettingsText();
        }

        /// <summary>
        /// 清理对话请求中的运行时文本，负责避免顾客、商品或环境字段携带非法代理字符。
        /// </summary>
        private static void SanitizeDialogueRequest(CustomerReviewDialogueRequest request)
        {
            if (request == null)
                return;

            request.userPrompt = StringEncodingUtility.SanitizeUtf16(request.userPrompt);
            request.stablePromptPrefix = StringEncodingUtility.SanitizeUtf16(request.stablePromptPrefix);
            request.dynamicPrompt = StringEncodingUtility.SanitizeUtf16(request.dynamicPrompt);
            if (request.messages == null)
                return;

            for (int i = 0; i < request.messages.Count; i++)
            {
                CustomerReviewChatMessage message = request.messages[i];
                if (message == null)
                    continue;

                message.role = StringEncodingUtility.SanitizeUtf16(message.role);
                message.content = StringEncodingUtility.SanitizeUtf16(message.content);
            }
        }

        /// <summary>
        /// 发送 JSON POST 请求，负责采用 RimTalk 同类的 UnityWebRequest 和 UTF-8 字节上传路径。
        /// </summary>
        private static async Task<CustomerReviewHttpResult> SendJsonPostAsync(string endpoint, string body, CancellationToken token, SimManagementLibSettings settings, params string[] headers)
        {
            endpoint = StringEncodingUtility.SanitizeUtf16(endpoint);
            body = StringEncodingUtility.SanitizeUtf16(body);
            byte[] payload = Encoding.UTF8.GetBytes(body ?? string.Empty);
            using (UnityWebRequest request = new UnityWebRequest(endpoint, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(payload);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                ApplyHeaders(request, headers);
                return await SendUnityRequestAsync(request, token, GetTimeoutSeconds(settings));
            }
        }

        /// <summary>
        /// 探测接口地址，负责只判断服务端是否可达，不把 401、404 或 405 视为网络失败。
        /// </summary>
        private static async Task<CustomerReviewHttpResult> SendProbeAsync(string endpoint, CancellationToken token, SimManagementLibSettings settings)
        {
            endpoint = StringEncodingUtility.SanitizeUtf16(endpoint);
            using (UnityWebRequest request = new UnityWebRequest(endpoint, "OPTIONS"))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                return await SendUnityRequestAsync(request, token, GetTimeoutSeconds(settings));
            }
        }

        /// <summary>
        /// 驱动 UnityWebRequest 异步执行，负责统一超时、取消、响应文本清理和错误归类。
        /// </summary>
        private static async Task<CustomerReviewHttpResult> SendUnityRequestAsync(UnityWebRequest request, CancellationToken token, int timeoutSeconds)
        {
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            float elapsedSeconds = 0f;
            while (!operation.isDone)
            {
                token.ThrowIfCancellationRequested();
                await Task.Delay(RequestPollDelayMs, token);
                elapsedSeconds += RequestPollDelayMs / 1000f;
                if (elapsedSeconds > timeoutSeconds)
                {
                    request.Abort();
                    throw new TaskCanceledException();
                }
            }

            string responseText = StringEncodingUtility.SanitizeUtf16(request.downloadHandler?.text ?? string.Empty);
            UnityWebRequest.Result requestResult = request.result;
            bool networkError = requestResult == UnityWebRequest.Result.ConnectionError;
            bool httpError = requestResult == UnityWebRequest.Result.ProtocolError;
            int statusCode = (int)request.responseCode;
            bool reachedServer = statusCode > 0 && !networkError;
            return new CustomerReviewHttpResult
            {
                statusCode = statusCode,
                responseText = responseText,
                success = reachedServer && !httpError,
                reachedServer = reachedServer,
                error = StringEncodingUtility.SanitizeUtf16(request.error)
            };
        }

        /// <summary>
        /// 写入请求头，负责过滤空键并清理外部配置里的非法文本。
        /// </summary>
        private static void ApplyHeaders(UnityWebRequest request, string[] headers)
        {
            if (request == null || headers == null)
                return;

            for (int i = 0; i + 1 < headers.Length; i += 2)
            {
                string key = StringEncodingUtility.SanitizeUtf16(headers[i]);
                string value = StringEncodingUtility.SanitizeUtf16(headers[i + 1]);
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrEmpty(value))
                    continue;

                request.SetRequestHeader(key, value);
            }
        }

        /// <summary>
        /// 读取安全超时秒数，负责让设置值在合理范围内生效。
        /// </summary>
        private static int GetTimeoutSeconds(SimManagementLibSettings settings)
        {
            int timeoutSeconds = settings != null ? settings.reviewRequestTimeoutSeconds : DefaultTimeoutSeconds;
            return Math.Max(20, Math.Min(180, timeoutSeconds));
        }
    }

    /// <summary>
    /// 保存一次 UnityWebRequest 返回结果，负责让调用方不直接依赖 Unity 网络对象生命周期。
    /// </summary>
    internal class CustomerReviewHttpResult
    {
        public int statusCode;
        public string responseText = "";
        public bool success;
        public bool reachedServer;
        public string error = "";
    }
}
