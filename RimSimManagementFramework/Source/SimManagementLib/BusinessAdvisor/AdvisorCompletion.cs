using System.Runtime.Serialization;

namespace SimManagementLib.BusinessAdvisor
{
    //只记录供应商实际返回的用量；缺失字段保持未知。
    internal sealed class AdvisorCompletion
    {
        internal string text;
        internal int? input;
        internal int? output;
        internal int? cached;
        internal int? cacheWrite;
    }

    //读取 OpenAI 兼容和 Anthropic 的用量字段，忽略响应里的其他内容。
    [DataContract]
    internal sealed class AdvisorUsageEnvelope
    {
        [DataMember] public AdvisorUsage usage { get; set; }
    }

    //保留各供应商字段差异，不把未知命中数显示成零。
    [DataContract]
    internal sealed class AdvisorUsage
    {
        [DataMember] public int? prompt_tokens { get; set; }
        [DataMember] public int? completion_tokens { get; set; }
        [DataMember] public int? input_tokens { get; set; }
        [DataMember] public int? output_tokens { get; set; }
        [DataMember] public int? cache_read_input_tokens { get; set; }
        [DataMember] public int? cache_creation_input_tokens { get; set; }
        [DataMember] public int? prompt_cache_hit_tokens { get; set; }
        [DataMember] public AdvisorPromptTokens prompt_tokens_details { get; set; }
    }

    //读取标准聊天接口的缓存命中细分。
    [DataContract]
    internal sealed class AdvisorPromptTokens
    {
        [DataMember] public int? cached_tokens { get; set; }
        [DataMember] public int? cache_write_tokens { get; set; }
    }
}
