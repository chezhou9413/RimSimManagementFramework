using System.Text;
using System.Text.Json;
using SimManagementLib.Pojo;
using SimManagementLib.Tool;

namespace SimManagementLib.BusinessAdvisor
{
    //按供应商响应结构读取顾问正文，不从任意同名字段或思考块提取指令。
    internal static class AdvisorResponseText
    {
        //兼容 Chat Completions 字符串和文本块，以及 Anthropic Messages 文本块。
        internal static string Read(string raw, SimLlmProvider provider)
        {
            try
            {
                using (JsonDocument document = JsonDocument.Parse(raw))
                {
                    JsonElement root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) throw new JsonException("响应根节点不是对象。");
                    string text, stop;
                    if (provider == SimLlmProvider.Anthropic)
                    {
                        if (!root.TryGetProperty("content", out JsonElement content))
                            throw new JsonException("Messages 响应缺少 content。");
                        text = ContentText(content);
                        stop = StringProperty(root, "stop_reason");
                    }
                    else
                    {
                        if (!root.TryGetProperty("choices", out JsonElement choices) || choices.ValueKind != JsonValueKind.Array
                            || choices.GetArrayLength() == 0 || choices[0].ValueKind != JsonValueKind.Object
                            || !choices[0].TryGetProperty("message", out JsonElement message) || message.ValueKind != JsonValueKind.Object)
                            throw new JsonException("Chat Completions 响应缺少 choices[0].message。");
                        text = message.TryGetProperty("content", out JsonElement content) ? ContentText(content) : "";
                        if (string.IsNullOrWhiteSpace(text)) text = StringProperty(message, "refusal");
                        stop = StringProperty(choices[0], "finish_reason");
                    }
                    if (stop == "length" || stop == "max_tokens")
                        throw Error("ResponseTruncated", "stop_reason=" + stop, text);
                    if (string.IsNullOrWhiteSpace(text))
                        throw Error("ProviderResponseInvalid", "响应没有可读取的文本正文。", raw);
                    return text;
                }
            }
            catch (JsonException ex) { throw Error("ProviderResponseInvalid", ex.Message, raw); }
        }

        //仅拼接 text 块；thinking、reasoning、tool_use 等内容不作为聊天或工具协议。
        private static string ContentText(JsonElement content)
        {
            if (content.ValueKind == JsonValueKind.String) return content.GetString();
            if (content.ValueKind == JsonValueKind.Null) return "";
            if (content.ValueKind != JsonValueKind.Array) throw new JsonException("content 不是字符串或文本块数组。");
            var text = new StringBuilder();
            foreach (JsonElement block in content.EnumerateArray())
            {
                if (block.ValueKind == JsonValueKind.Object && StringProperty(block, "type") == "text")
                    text.Append(StringProperty(block, "text"));
            }
            return text.ToString();
        }

        //可选字段缺失时保持为空，不猜测未返回的正文或结束原因。
        private static string StringProperty(JsonElement item, string name) => item.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.String ? value.GetString() : "";

        //保留有限诊断片段，格式失败或截断均不自动重复调用接口。
        private static LlmRequestException Error(string key, string reason, string text)
        {
            string message = SimTranslation.T("RSMF.Business.Advisor." + key);
            string excerpt = text ?? "";
            if (excerpt.Length > 2000) excerpt = excerpt.Substring(0, 2000) + "…";
            return new LlmRequestException(message, message + "\n\n" + reason + "\n\n" + excerpt, key == "ResponseTruncated");
        }
    }
}
