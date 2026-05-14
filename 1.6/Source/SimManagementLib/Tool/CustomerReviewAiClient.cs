using SimManagementLib.Pojo;
using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 负责调用 OpenAI 兼容接口或 Anthropic 接口生成顾客点评文本。
    /// </summary>
    public static class CustomerReviewAiClient
    {
        private const int TimeoutSeconds = 20;

        /// <summary>
        /// 根据当前设置异步请求大模型，并返回解析后的点评结果。
        /// </summary>
        public static async Task<CustomerReviewAiResult> GenerateReviewAsync(CustomerReviewSnapshot snapshot, SimManagementLibSettings settings, CancellationToken token)
        {
            if (snapshot == null || settings == null || !settings.HasValidReviewAiConfig()) return null;

            string userPrompt = CustomerReviewJsonUtility.BuildSnapshotPrompt(snapshot, settings);
            CustomerReviewDialogueRequest request = CustomerReviewDialogueStrategy.Prepare(snapshot, settings, userPrompt);

            string raw = await CallProviderAsync(settings, request, token);
            if (!CustomerReviewJsonUtility.TryParseReviewResult(raw, settings, out CustomerReviewAiResult result))
            {
                raw = await CallProviderAsync(settings, request, token);
                if (!CustomerReviewJsonUtility.TryParseReviewResult(raw, settings, out result))
                    return null;
            }

            CustomerReviewDialogueStrategy.StoreSuccessfulResult(request, snapshot, result, settings);
            return result;
        }

        /// <summary>
        /// 按当前供应商发送请求，负责让首次生成和修正重试复用同一条调用路径。
        /// </summary>
        private static async Task<string> CallProviderAsync(SimManagementLibSettings settings, CustomerReviewDialogueRequest request, CancellationToken token)
        {
            if (settings.reviewProvider == CustomerReviewProvider.Anthropic)
                return await CallAnthropicAsync(settings, request, token);

            return await CallOpenAiCompatibleAsync(settings, request, token);
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
                customerDisplayName = "测试顾客",
                kindId = "TestKind",
                kindLabel = "测试顾客",
                raceLabel = "人类",
                ageSummary = "成年",
                backstorySummary = "路过殖民地的普通旅人",
                traitSummary = "谨慎",
                moodSummary = "平静",
                healthSummary = "健康",
                zoneLabel = "测试商店",
                budgetSummary = "预算 100 银",
                spentSilver = 25f,
                purchasedSummary = "购买了简单商品",
                serviceSummary = "未使用服务",
                shopEnvironmentSummary = "环境普通"
            };

            CustomerReviewConnectionTestResult testResult = await ProbeBaseUrlAsync(settings, token);
            CustomerReviewAiResult result = await GenerateReviewAsync(snapshot, settings, token);
            testResult.apiReachable = result != null;
            if (testResult.apiReachable)
                testResult.message = "BaseUrl 可访问，模型测试生成成功。";
            else if (string.IsNullOrEmpty(testResult.message))
                testResult.message = "BaseUrl 可访问，但模型测试生成失败，请检查模型名、API Key 或响应格式。";
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
        private static async Task<string> CallOpenAiCompatibleAsync(SimManagementLibSettings settings, CustomerReviewDialogueRequest request, CancellationToken token)
        {
            using (HttpClient client = CreateClient())
            {
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.openAiApiKey);
                string endpoint = NormalizeOpenAiUrl(settings.openAiBaseUrl);
                HttpResponseMessage response = await client.PostAsync(endpoint, new StringContent(BuildOpenAiBody(settings, request, true), Encoding.UTF8, "application/json"), token);
                string responseText = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode && ShouldRetryWithoutResponseFormat(responseText))
                {
                    response = await client.PostAsync(endpoint, new StringContent(BuildOpenAiBody(settings, request, false), Encoding.UTF8, "application/json"), token);
                    responseText = await response.Content.ReadAsStringAsync();
                }
                if (!response.IsSuccessStatusCode) return "";
                return CustomerReviewJsonUtility.ExtractOpenAiMessageContent(responseText);
            }
        }

        /// <summary>
        /// 调用 Anthropic Messages 接口，负责把系统提示词和用户提示词发送给模型。
        /// </summary>
        private static async Task<string> CallAnthropicAsync(SimManagementLibSettings settings, CustomerReviewDialogueRequest request, CancellationToken token)
        {
            using (HttpClient client = CreateClient())
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
                if (!response.IsSuccessStatusCode)
                    return "";
                return CustomerReviewJsonUtility.ExtractAnthropicMessageContent(responseText);
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
                result.message = "设置为空。";
                return result;
            }

            string endpoint = settings.reviewProvider == CustomerReviewProvider.Anthropic
                ? "https://api.anthropic.com/v1/messages"
                : NormalizeOpenAiUrl(settings.openAiBaseUrl);
            result.endpoint = endpoint;

            try
            {
                using (HttpClient client = CreateClient())
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Options, endpoint))
                {
                    HttpResponseMessage response = await client.SendAsync(request, token);
                    result.statusCode = (int)response.StatusCode;
                    result.baseUrlReachable = true;
                    result.message = "BaseUrl 可访问，HTTP 状态 " + result.statusCode + "。";
                }
            }
            catch (HttpRequestException ex)
            {
                result.baseUrlReachable = false;
                result.message = "BaseUrl 连接失败: " + ex.Message;
            }
            catch (TaskCanceledException)
            {
                result.baseUrlReachable = false;
                result.message = "BaseUrl 连接超时。";
            }
            catch (Exception ex)
            {
                result.baseUrlReachable = false;
                result.message = "BaseUrl 测试异常: " + ex.Message;
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
        private static HttpClient CreateClient()
        {
            return new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(TimeoutSeconds)
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
