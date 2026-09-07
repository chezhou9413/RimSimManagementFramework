using SimManagementLib.Pojo;
using System.Threading;
using System.Threading.Tasks;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 提供通用 LLM 调用入口，负责让套餐、评价和后续经营功能共享同一套模型配置。
    /// </summary>
    public static class SimLlmUtility
    {
        /// <summary>
        /// 使用通用 LLM 配置生成短文本，负责封装供应商、密钥、模型和请求格式细节。
        /// </summary>
        public static Task<string> GenerateTextAsync(string systemPrompt, string userPrompt, SimManagementLibSettings settings, CancellationToken token)
        {
            SimManagementLibSettings requestSettings = BuildRequestSettings(settings, requireEnabled: true);
            return CustomerReviewAiClient.GenerateShortTextAsync(systemPrompt, userPrompt, requestSettings, token);
        }

        /// <summary>
        /// 测试通用 LLM 接口地址是否可访问，负责给设置界面提供基础连通性状态。
        /// </summary>
        public static Task<CustomerReviewConnectionTestResult> TestBaseUrlAsync(SimManagementLibSettings settings, CancellationToken token)
        {
            SimManagementLibSettings requestSettings = BuildRequestSettings(settings, requireEnabled: false);
            return CustomerReviewAiClient.TestBaseUrlAsync(requestSettings, token);
        }

        /// <summary>
        /// 测试通用 LLM 是否能完成一次生成，负责给设置界面确认模型、密钥和网关可用性。
        /// </summary>
        public static Task<CustomerReviewConnectionTestResult> TestGenerationAsync(SimManagementLibSettings settings, CancellationToken token)
        {
            SimManagementLibSettings requestSettings = BuildRequestSettings(settings, requireEnabled: false);
            return CustomerReviewAiClient.TestConnectionDetailedAsync(requestSettings, token);
        }

        /// <summary>
        /// 构造供底层网络客户端使用的设置副本，负责把通用 LLM 字段映射到兼容旧客户端的连接字段。
        /// </summary>
        public static SimManagementLibSettings BuildRequestSettings(SimManagementLibSettings source, bool requireEnabled)
        {
            SimManagementLibSettings copy = new SimManagementLibSettings();
            if (source == null)
                return copy;

            copy.llmEnabled = requireEnabled ? source.llmEnabled : true;
            copy.llmProvider = source.llmProvider;
            copy.llmOpenAiBaseUrl = source.llmOpenAiBaseUrl;
            copy.llmOpenAiApiKey = source.llmOpenAiApiKey;
            copy.llmOpenAiModel = source.llmOpenAiModel;
            copy.llmAnthropicApiKey = source.llmAnthropicApiKey;
            copy.llmAnthropicModel = source.llmAnthropicModel;
            copy.reviewAiEnabled = true;
            copy.reviewTemperature = source.reviewTemperature;
            copy.reviewRequestTimeoutSeconds = source.reviewRequestTimeoutSeconds;
            copy.SyncLegacyReviewAiConnectionFields();
            copy.SanitizeReviewSettingsText();
            if (!requireEnabled)
            {
                copy.llmEnabled = true;
                copy.reviewAiEnabled = true;
            }

            return copy;
        }
    }
}
