using SimManagementLib.Pojo;
using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 负责调用 OpenAI 兼容接口或 Anthropic 接口生成顾客点评文本。
    /// </summary>
    public static class CustomerReviewAiClient
    {
        private const int DefaultTimeoutSeconds = 90;

        /// <summary>
        /// 根据当前设置异步请求大模型，并返回解析后的点评结果。
        /// </summary>
        public static async Task<CustomerReviewAiResult> GenerateReviewAsync(CustomerReviewSnapshot snapshot, SimManagementLibSettings settings, CancellationToken token)
        {
            if (snapshot == null || settings == null || !settings.HasValidReviewAiConfig()) return null;

            string stablePromptPrefix = CustomerReviewPromptInjector.BuildStablePromptPrefix(settings);
            string antiRepeatContext = CustomerReviewDialogueStrategy.BuildAntiRepeatContext(settings);
            string dynamicPrompt = CustomerReviewPromptInjector.BuildDynamicPrompt(snapshot, settings, antiRepeatContext);
            CustomerReviewDialogueRequest request = CustomerReviewDialogueStrategy.Prepare(snapshot, settings, stablePromptPrefix, dynamicPrompt);
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
                CustomerReviewAiDebugLog.MarkFailed(debugId, SimTranslation.T("RSMF.CustomerReview.AiError.RequestException", ex.Message.Named("message")));
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
            return ProbeBaseUrlAsync(settings, token);
        }

        /// <summary>
        /// 调用 OpenAI 兼容接口，负责在 JSON 输出约束不被网关支持时退化为普通 JSON 提示。
        /// </summary>
        private static async Task<string> CallOpenAiCompatibleAsync(SimManagementLibSettings settings, CustomerReviewDialogueRequest request, CancellationToken token, int debugId)
        {
            using (HttpClient client = CreateClient(settings))
            {
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.openAiApiKey);
                string endpoint = NormalizeOpenAiUrl(settings.openAiBaseUrl);
                string body = BuildOpenAiBody(settings, request, true);
                HttpResponseMessage response = await client.PostAsync(endpoint, new StringContent(body, Encoding.UTF8, "application/json"), token);
                string responseText = await response.Content.ReadAsStringAsync();
                string extracted = CustomerReviewJsonUtility.ExtractOpenAiMessageContent(responseText);
                CustomerReviewAiDebugLog.UpdateHttpAttempt(debugId, SimTranslation.T("RSMF.CustomerReview.HttpAttempt.OpenAiJsonObject"), endpoint, body, (int)response.StatusCode, response.IsSuccessStatusCode, responseText, extracted);
                if (!response.IsSuccessStatusCode && ShouldRetryWithoutResponseFormat(responseText))
                {
                    body = BuildOpenAiBody(settings, request, false);
                    response = await client.PostAsync(endpoint, new StringContent(body, Encoding.UTF8, "application/json"), token);
                    responseText = await response.Content.ReadAsStringAsync();
                    extracted = CustomerReviewJsonUtility.ExtractOpenAiMessageContent(responseText);
                    CustomerReviewAiDebugLog.UpdateHttpAttempt(debugId, SimTranslation.T("RSMF.CustomerReview.HttpAttempt.OpenAiNoResponseFormat"), endpoint, body, (int)response.StatusCode, response.IsSuccessStatusCode, responseText, extracted);
                }
                if (!response.IsSuccessStatusCode) return "";
                return extracted;
            }
        }

        /// <summary>
        /// 调用 Anthropic Messages 接口，负责把系统提示词和用户提示词发送给模型。
        /// </summary>
        private static async Task<string> CallAnthropicAsync(SimManagementLibSettings settings, CustomerReviewDialogueRequest request, CancellationToken token, int debugId)
        {
            using (HttpClient client = CreateClient(settings))
            {
                client.DefaultRequestHeaders.Add("x-api-key", settings.anthropicApiKey);
                client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
                string body =
                    "{" +
                    "\"model\":" + CustomerReviewJsonUtility.Quote(settings.anthropicModel) + "," +
                    "\"max_tokens\":500," +
                    "\"temperature\":" + settings.reviewTemperature.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "," +
                    "\"system\":" + CustomerReviewJsonUtility.Quote(settings.reviewSystemPrompt) + "," +
                    "\"messages\":" + BuildMessagesArray(request) +
                    "}";

                HttpResponseMessage response = await client.PostAsync("https://api.anthropic.com/v1/messages", new StringContent(body, Encoding.UTF8, "application/json"), token);
                string responseText = await response.Content.ReadAsStringAsync();
                string extracted = CustomerReviewJsonUtility.ExtractAnthropicMessageContent(responseText);
                CustomerReviewAiDebugLog.UpdateHttpAttempt(debugId, SimTranslation.T("RSMF.CustomerReview.HttpAttempt.AnthropicMessages"), "https://api.anthropic.com/v1/messages", body, (int)response.StatusCode, response.IsSuccessStatusCode, responseText, extracted);
                if (!response.IsSuccessStatusCode)
                    return "";
                return extracted;
            }
        }

        /// <summary>
        /// 探测当前供应商入口是否可访问，负责区分网络或 BaseUrl 问题和模型生成问题。
        /// </summary>
        private static async Task<CustomerReviewConnectionTestResult> ProbeBaseUrlAsync(SimManagementLibSettings settings, CancellationToken token)
        {
            CustomerReviewConnectionTestResult result = new CustomerReviewConnectionTestResult();
            if (settings == null)
            {
                result.message = SimTranslation.T("RSMF.CustomerReview.Connection.SettingsNull");
                return result;
            }

            string endpoint = settings.reviewProvider == CustomerReviewProvider.Anthropic
                ? "https://api.anthropic.com/v1/messages"
                : NormalizeOpenAiUrl(settings.openAiBaseUrl);
            result.endpoint = endpoint;

            try
            {
                using (HttpClient client = CreateClient(settings))
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Options, endpoint))
                {
                    HttpResponseMessage response = await client.SendAsync(request, token);
                    result.statusCode = (int)response.StatusCode;
                    result.baseUrlReachable = true;
                    result.message = SimTranslation.T("RSMF.CustomerReview.Connection.BaseUrlReachable", result.statusCode.Named("statusCode"));
                }
            }
            catch (HttpRequestException ex)
            {
                result.baseUrlReachable = false;
                result.message = SimTranslation.T("RSMF.CustomerReview.Connection.BaseUrlFailed", ex.Message.Named("message"));
            }
            catch (TaskCanceledException)
            {
                result.baseUrlReachable = false;
                result.message = SimTranslation.T("RSMF.CustomerReview.Connection.BaseUrlTimeout");
            }
            catch (Exception ex)
            {
                result.baseUrlReachable = false;
                result.message = SimTranslation.T("RSMF.CustomerReview.Connection.BaseUrlException", ex.Message.Named("message"));
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
        /// 创建带超时的 HTTP 客户端，负责避免后台请求长期阻塞。
        /// </summary>
        private static HttpClient CreateClient(SimManagementLibSettings settings)
        {
            int timeoutSeconds = settings != null ? settings.reviewRequestTimeoutSeconds : DefaultTimeoutSeconds;
            timeoutSeconds = Math.Max(20, Math.Min(180, timeoutSeconds));
            return new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(timeoutSeconds)
            };
        }

        /// <summary>
        /// 规范化 OpenAI 兼容接口地址，负责兼容填写根地址、v1 地址或完整 chat/completions 地址。
        /// </summary>
        public static string NormalizeOpenAiUrl(string baseUrl)
        {
            string url = string.IsNullOrWhiteSpace(baseUrl) ? "https://api.openai.com/v1" : baseUrl.Trim();
            url = url.TrimEnd('/');
            if (url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
                return url;
            return url + "/chat/completions";
        }
    }
}
