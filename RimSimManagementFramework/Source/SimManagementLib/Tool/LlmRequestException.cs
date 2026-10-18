using System;
using System.Text.Json;

namespace SimManagementLib.Tool
{
    //保留接口诊断信息，界面只把短原因作为状态和聊天正文显示。
    internal sealed class LlmRequestException : InvalidOperationException
    {
        public string Detail { get; }
        public bool Recoverable { get; }

        //详情已经脱敏，不包含请求头或 API 密钥。
        public LlmRequestException(string message, string detail, bool recoverable = false) : base(message)
        {
            Detail = detail;
            Recoverable = recoverable;
        }

        //解析服务端错误消息；非 JSON 响应保留原文片段，便于判断代理和网络问题。
        internal static LlmRequestException FromResponse(CustomerReviewHttpResult response, SimManagementLibSettings settings)
        {
            string detail = string.IsNullOrWhiteSpace(response.responseText) ? response.error : response.responseText;
            detail = StringEncodingUtility.SanitizeUtf16(detail ?? "");
            if (!string.IsNullOrEmpty(settings.llmOpenAiApiKey)) detail = detail.Replace(settings.llmOpenAiApiKey, "[redacted]");
            if (!string.IsNullOrEmpty(settings.llmAnthropicApiKey)) detail = detail.Replace(settings.llmAnthropicApiKey, "[redacted]");
            string message = detail;
            try
            {
                using (JsonDocument json = JsonDocument.Parse(detail))
                {
                    if (json.RootElement.ValueKind == JsonValueKind.Object
                        && json.RootElement.TryGetProperty("error", out JsonElement error)
                        && error.ValueKind == JsonValueKind.Object
                        && error.TryGetProperty("message", out JsonElement text) && text.ValueKind == JsonValueKind.String)
                        message = text.GetString();
                }
            }
            catch (JsonException) { message = detail; }
            string reason;
            if ((response.statusCode == 400 || response.statusCode == 404 || response.statusCode == 422)
                && (message.IndexOf("model", StringComparison.OrdinalIgnoreCase) >= 0 || message.Contains("模型")))
                reason = SimTranslation.T("RSMF.Llm.Error.ModelRejected");
            else if (response.statusCode == 401 || response.statusCode == 403) reason = SimTranslation.T("RSMF.Llm.Error.Authentication");
            else if (response.statusCode == 429) reason = SimTranslation.T("RSMF.Llm.Error.RateLimit");
            else reason = message.Length > 160 ? message.Substring(0, 160) + "…" : message;
            string prefix = response.statusCode > 0 ? "HTTP " + response.statusCode + " · " : "";
            string model = settings.llmProvider == Pojo.SimLlmProvider.Anthropic ? settings.llmAnthropicModel : settings.llmOpenAiModel;
            string diagnostics = prefix + reason + "\nModel: " + model + "\n\n" + (detail.Length > 2000 ? detail.Substring(0, 2000) + "…" : detail);
            return new LlmRequestException(prefix + reason, diagnostics, response.statusCode == 0 || response.statusCode == 408
                || response.statusCode == 429 || response.statusCode >= 500);
        }
    }
}
