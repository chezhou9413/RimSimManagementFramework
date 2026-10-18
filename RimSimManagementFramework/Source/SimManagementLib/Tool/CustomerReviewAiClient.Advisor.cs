using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SimManagementLib.BusinessAdvisor;
using SimManagementLib.Pojo;

namespace SimManagementLib.Tool
{
    //经营助手沿用已核对的网络实现，但有独立模型和稳定的聊天前缀。
    public static partial class CustomerReviewAiClient
    {
        //只复制文本历史，不把游戏引用传入异步网络流程。
        internal static async Task<AdvisorCompletion> GenerateAdvisorAsync(string system, IEnumerable<AdvisorEntry> history,
            SimManagementLibSettings settings, CancellationToken token)
        {
            if (!settings.HasValidLlmConfig()) throw new InvalidOperationException(SimTranslation.T("RSMF.Business.Advisor.NeedsConfig"));
            var request = new CustomerReviewDialogueRequest
            {
                systemPromptOverride = system,
                useJsonResponseFormat = settings.advisorJsonMode,
                advisorCaching = true,
                maxOutputTokens = 4096,
                messages = history.Select(e => new CustomerReviewChatMessage { role = e.role, content = e.text }).ToList()
            };
            SanitizeDialogueRequest(request);
            await CallProviderAsync(settings, request, token, 0);
            string result = AdvisorResponseText.Read(request.providerResponse, settings.llmProvider);
            var usage = AdvisorJson.Read<AdvisorUsageEnvelope>(request.providerResponse)?.usage;
            bool anthropic = settings.llmProvider == SimLlmProvider.Anthropic;
            return new AdvisorCompletion { text = result,
                input = anthropic && usage?.input_tokens != null
                    ? usage.input_tokens + (usage.cache_read_input_tokens ?? 0) + (usage.cache_creation_input_tokens ?? 0) : usage?.prompt_tokens,
                output = anthropic ? usage?.output_tokens : usage?.completion_tokens,
                cached = anthropic ? usage?.cache_read_input_tokens : (usage?.prompt_tokens_details?.cached_tokens ?? usage?.prompt_cache_hit_tokens),
                cacheWrite = anthropic ? usage?.cache_creation_input_tokens : usage?.prompt_tokens_details?.cache_write_tokens };
        }
    }
}
