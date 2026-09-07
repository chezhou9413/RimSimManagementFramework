namespace SimManagementLib.Pojo
{
    /// <summary>
    /// 定义通用大模型供应商类型，负责让评价、套餐和后续经营功能共用同一套接口配置。
    /// </summary>
    public enum SimLlmProvider
    {
        OpenAICompatible,
        Anthropic
    }
}
